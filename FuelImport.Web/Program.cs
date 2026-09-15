using System.Globalization;
using System.Text;
using System.Text.Json;
using FuelImport.Core.Interfaces;
using FuelImport.Core.Models;
using FuelImport.Core.Options;
using FuelImport.Core.Services;
using FuelImport.Data.Persistence;
using FuelImport.Web.Contracts;
using FuelImport.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ImportOptions>(builder.Configuration.GetSection("Import"));

var sqliteConnection = new SqliteConnectionStringBuilder(
    builder.Configuration.GetConnectionString("FuelImport") ?? "Data Source=fuelimport.db");

var repoRoot = FindNearestParentWithFile("SnapFuel.slnx");
if (repoRoot is not null)
{
    sqliteConnection.DataSource = Path.Combine(repoRoot, "fuelimport.db");
}
else if (!Path.IsPathRooted(sqliteConnection.DataSource))
{
    sqliteConnection.DataSource = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, sqliteConnection.DataSource));
}

builder.Services.AddDbContext<FuelImportDbContext>(options =>
    options.UseSqlite(sqliteConnection.ConnectionString));
builder.Services.AddScoped<IImageMetadataExtractor, FileImageMetadataExtractor>();
builder.Services.AddScoped<IImageClassifier, SimpleImageClassifier>();
builder.Services.AddScoped<ImageImportService>();
builder.Services.AddScoped(_ => new ManualReviewGroupingService(TimeSpan.FromMinutes(15), 0.40d));
builder.Services.AddEndpointsApiExplorer();

static string? FindNearestParentWithFile(string fileName)
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        var candidate = Path.Combine(current.FullName, fileName);
        if (File.Exists(candidate))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    return null;
}

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FuelImportDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.EnsureSeededAsync(db);
}

app.MapPost("/api/import/scan", async (ImageImportService importService, ImportScanRequest? request, CancellationToken ct) =>
{
    ImportOptions? overrideOptions = null;
    if (request is not null && (!string.IsNullOrWhiteSpace(request.RootFolder) || request.DryRun.HasValue || request.Recursive.HasValue))
    {
        overrideOptions = new ImportOptions
        {
            RootFolder = request.RootFolder ?? string.Empty,
            DryRun = request.DryRun ?? false,
            Recursive = request.Recursive ?? true
        };
    }

    var result = await importService.ScanAsync(overrideOptions, ct);
    return string.IsNullOrEmpty(result.ErrorMessage)
        ? Results.Ok(result)
        : Results.BadRequest(result);
});

app.MapGet("/api/import/config", (IOptions<ImportOptions> options) =>
{
    return Results.Ok(options.Value);
});

app.MapGet("/api/events", async (FuelImportDbContext db, bool? needsReview) =>
{
    var query = db.FuelEvents.AsNoTracking();
    if (needsReview.HasValue)
    {
        query = query.Where(e => e.NeedsReview == needsReview.Value);
    }

    return await query
        .OrderByDescending(e => e.EventTimeUtc)
        .Select(e => new FuelEventSummaryResponse
        {
            FuelEventId = e.FuelEventId,
            VehicleId = e.VehicleId,
            Gallons = e.Gallons,
            TotalPrice = e.TotalPrice,
            PricePerGallon = e.PricePerGallon,
            Odometer = e.Odometer,
            OverallConfidence = e.OverallConfidence,
            NeedsReview = e.NeedsReview,
            ReviewStatus = e.ReviewStatus,
            ReviewReason = e.ReviewReason
        })
        .ToListAsync();
});

app.MapGet("/api/events/{id:int}", async (FuelImportDbContext db, int id) =>
{
    var fuelEvent = await db.FuelEvents.AsNoTracking().FirstOrDefaultAsync(e => e.FuelEventId == id);
    return fuelEvent is null ? Results.NotFound() : Results.Ok(fuelEvent);
});

app.MapGet("/api/vehicles", async (FuelImportDbContext db) =>
    await db.Vehicles
        .AsNoTracking()
        .Where(v => v.Active)
        .OrderBy(v => v.Name)
        .Select(v => new VehicleOptionResponse
        {
            VehicleId = v.VehicleId,
            Name = v.Name,
            NoOdometer = v.NoOdometer,
            MaxGallonsPerFillUp = v.MaxGallonsPerFillUp,
            MaxMpg = v.MaxMpg
        })
        .ToListAsync());

app.MapGet("/api/vehicles/manage", async (FuelImportDbContext db) =>
    await db.Vehicles
        .AsNoTracking()
        .OrderByDescending(v => v.Active)
        .ThenBy(v => v.Name)
        .Select(v => new VehicleManagementResponse
        {
            VehicleId = v.VehicleId,
            Name = v.Name,
            Active = v.Active,
            NoOdometer = v.NoOdometer,
            MaxGallonsPerFillUp = v.MaxGallonsPerFillUp,
            MaxMpg = v.MaxMpg
        })
        .ToListAsync());

app.MapPost("/api/vehicles", async (FuelImportDbContext db, VehicleUpsertRequest request) =>
{
    var name = NormalizeOptional(request.Name);
    if (name is null)
    {
        return Results.BadRequest(new { message = "Vehicle name is required." });
    }

    if (!AreThresholdsValid(request.MaxGallonsPerFillUp, request.MaxMpg, out var thresholdError))
    {
        return Results.BadRequest(new { message = thresholdError });
    }

    var normalizedName = name.ToUpperInvariant();
    var existingNames = await db.Vehicles
        .AsNoTracking()
        .Select(v => new { v.VehicleId, v.Name })
        .ToListAsync();

    if (existingNames.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
    {
        return Results.BadRequest(new { message = "A vehicle with that name already exists." });
    }

    var labels = await db.Vehicles.AsNoTracking().Select(v => v.DashboardLabel).ToListAsync();
    var vehicle = new Vehicle
    {
        Name = name,
        Active = request.Active ?? true,
        NoOdometer = request.NoOdometer ?? false,
        DashboardLabel = CreateUniqueDashboardLabel(name, labels),
        ExpectedTankGallonsMin = 0m,
        ExpectedTankGallonsMax = 100m,
        MaxGallonsPerFillUp = request.MaxGallonsPerFillUp,
        MaxMpg = request.MaxMpg
    };

    db.Vehicles.Add(vehicle);
    await db.SaveChangesAsync();

    return Results.Ok(new VehicleManagementResponse
    {
        VehicleId = vehicle.VehicleId,
        Name = vehicle.Name,
        Active = vehicle.Active,
        NoOdometer = vehicle.NoOdometer,
        MaxGallonsPerFillUp = vehicle.MaxGallonsPerFillUp,
        MaxMpg = vehicle.MaxMpg
    });
});

app.MapPut("/api/vehicles/{id:int}", async (FuelImportDbContext db, int id, VehicleUpsertRequest request) =>
{
    var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.VehicleId == id);
    if (vehicle is null)
    {
        return Results.NotFound();
    }

    var name = NormalizeOptional(request.Name);
    if (name is null)
    {
        return Results.BadRequest(new { message = "Vehicle name is required." });
    }

    if (!AreThresholdsValid(request.MaxGallonsPerFillUp, request.MaxMpg, out var thresholdError))
    {
        return Results.BadRequest(new { message = thresholdError });
    }

    var duplicateExists = await db.Vehicles
        .AnyAsync(v => v.VehicleId != id && v.Name.ToLower() == name.ToLower());

    if (duplicateExists)
    {
        return Results.BadRequest(new { message = "A vehicle with that name already exists." });
    }

    vehicle.Name = name;
    if (request.Active.HasValue)
    {
        vehicle.Active = request.Active.Value;
    }

    if (request.NoOdometer.HasValue)
    {
        vehicle.NoOdometer = request.NoOdometer.Value;
    }

    vehicle.MaxGallonsPerFillUp = request.MaxGallonsPerFillUp;
    vehicle.MaxMpg = request.MaxMpg;

    await db.SaveChangesAsync();

    return Results.Ok(new VehicleManagementResponse
    {
        VehicleId = vehicle.VehicleId,
        Name = vehicle.Name,
        Active = vehicle.Active,
        NoOdometer = vehicle.NoOdometer,
        MaxGallonsPerFillUp = vehicle.MaxGallonsPerFillUp,
        MaxMpg = vehicle.MaxMpg
    });
});

app.MapGet("/api/manual/groups", async (FuelImportDbContext db, ManualReviewGroupingService groupingService) =>
{
    var groups = await LoadManualReviewGroupsAsync(db, groupingService);
    return Results.Ok(groups);
});

app.MapPost("/api/manual/groups/merge", async (FuelImportDbContext db, ManualReviewGroupingService groupingService, ManualReviewMergeRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.SourceGroupKey) || string.IsNullOrWhiteSpace(request.TargetGroupKey))
    {
        return Results.BadRequest(new { message = "Both source and target groups are required." });
    }

    if (string.Equals(request.SourceGroupKey, request.TargetGroupKey, StringComparison.Ordinal))
    {
        return Results.BadRequest(new { message = "Choose two different groups to merge." });
    }

    var groups = await LoadManualReviewGroupsAsync(db, groupingService);
    var sourceGroup = groups.FirstOrDefault(group => string.Equals(group.GroupKey, request.SourceGroupKey, StringComparison.Ordinal));
    var targetGroup = groups.FirstOrDefault(group => string.Equals(group.GroupKey, request.TargetGroupKey, StringComparison.Ordinal));

    if (sourceGroup is null || targetGroup is null)
    {
        return Results.NotFound();
    }

    if (sourceGroup.FuelEventId.HasValue && targetGroup.FuelEventId.HasValue && sourceGroup.FuelEventId != targetGroup.FuelEventId)
    {
        return Results.BadRequest(new { message = "Cannot merge two groups that are already saved to different fuel events." });
    }

    var imageIds = sourceGroup.Images
        .Select(image => image.SourceImageId)
        .Concat(targetGroup.Images.Select(image => image.SourceImageId))
        .Distinct()
        .ToList();

    var images = await db.SourceImages
        .Where(image => imageIds.Contains(image.SourceImageId))
        .ToListAsync();

    var sharedGroupKey = images
        .Select(image => image.ManualGroupKey)
        .FirstOrDefault(key => !string.IsNullOrWhiteSpace(key))
        ?? $"manual-{Guid.NewGuid():N}";

    foreach (var image in images)
    {
        image.ManualGroupKey = sharedGroupKey;
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { GroupKey = sharedGroupKey });
});

app.MapPost("/api/manual/groups/split", async (FuelImportDbContext db, ManualReviewGroupingService groupingService, ManualReviewSplitRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.GroupKey) || request.ImageIds.Count == 0)
    {
        return Results.BadRequest(new { message = "A source group and at least one image are required." });
    }

    var groups = await LoadManualReviewGroupsAsync(db, groupingService);
    var sourceGroup = groups.FirstOrDefault(group => string.Equals(group.GroupKey, request.GroupKey, StringComparison.Ordinal));
    if (sourceGroup is null)
    {
        return Results.NotFound();
    }

    var sourceImageIds = sourceGroup.Images.Select(image => image.SourceImageId).ToHashSet();
    if (request.ImageIds.Any(imageId => !sourceImageIds.Contains(imageId)))
    {
        return Results.BadRequest(new { message = "One or more selected images do not belong to the chosen group." });
    }

    if (request.ImageIds.Count >= sourceGroup.Images.Count)
    {
        return Results.BadRequest(new { message = "Select fewer than the full group when splitting images out." });
    }

    var newGroupKey = $"manual-{Guid.NewGuid():N}";
    var sourceManualGroupKey = sourceGroup.GroupKey;
    var selectedImageIds = request.ImageIds.ToHashSet();

    var images = await db.SourceImages
        .Where(image => sourceImageIds.Contains(image.SourceImageId))
        .ToListAsync();

    foreach (var image in images)
    {
        image.ManualGroupKey = selectedImageIds.Contains(image.SourceImageId)
            ? newGroupKey
            : sourceManualGroupKey;
    }

    if (sourceGroup.FuelEventId.HasValue)
    {
        var fuelEvent = await db.FuelEvents.FirstOrDefaultAsync(e => e.FuelEventId == sourceGroup.FuelEventId.Value);
        if (fuelEvent is not null)
        {
            var movedLinks = await db.FuelEventSourceImages
                .Where(link => link.FuelEventId == fuelEvent.FuelEventId && selectedImageIds.Contains(link.SourceImageId))
                .ToListAsync();

            foreach (var link in movedLinks)
            {
                db.FuelEventSourceImages.Remove(link);
            }

            var remainingImages = images
                .Where(image => !selectedImageIds.Contains(image.SourceImageId))
                .OrderBy(image => image.CapturedAtUtc ?? image.CapturedAtLocal ?? DateTime.MaxValue)
                .ThenBy(image => image.SourceImageId)
                .ToList();

            fuelEvent.PumpSourceImageId = remainingImages.FirstOrDefault(image => image.ImageTypeCandidate == ImageType.Pump)?.SourceImageId;
            fuelEvent.DashSourceImageId = remainingImages.FirstOrDefault(image => image.ImageTypeCandidate == ImageType.Dashboard)?.SourceImageId;
            fuelEvent.EventTimeUtc = FirstTimestamp(remainingImages.Select(image => image.CapturedAtUtc));
            fuelEvent.EventTimeLocal = FirstTimestamp(remainingImages.Select(image => image.CapturedAtLocal));
            fuelEvent.Latitude = Average(remainingImages.Select(image => image.Latitude));
            fuelEvent.Longitude = Average(remainingImages.Select(image => image.Longitude));
            fuelEvent.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { GroupKey = newGroupKey, SourceGroupKey = sourceManualGroupKey });
});

app.MapPost("/api/manual/groups/save", async (FuelImportDbContext db, ManualReviewSaveRequest request) =>
{
    if (request.ImageIds.Count == 0)
    {
        return Results.BadRequest(new { message = "At least one image is required." });
    }

    var images = await db.SourceImages
        .Where(image => request.ImageIds.Contains(image.SourceImageId))
        .OrderBy(image => image.CapturedAtUtc)
        .ThenBy(image => image.SourceImageId)
        .ToListAsync();

    if (images.Count != request.ImageIds.Count)
    {
        return Results.BadRequest(new { message = "One or more selected images no longer exist." });
    }

    var existingLinkEventIds = await db.FuelEventSourceImages
        .Where(link => request.ImageIds.Contains(link.SourceImageId))
        .Select(link => link.FuelEventId)
        .Distinct()
        .ToListAsync();

    if (existingLinkEventIds.Count > 1)
    {
        return Results.BadRequest(new { message = "Selected images already belong to different fuel events." });
    }

    int? targetFuelEventId = request.FuelEventId ?? (existingLinkEventIds.Count == 1 ? existingLinkEventIds[0] : null);
    FuelEvent? fuelEvent = null;
    var previousVehicleId = default(int?);
    if (targetFuelEventId.HasValue)
    {
        fuelEvent = await db.FuelEvents.FirstOrDefaultAsync(e => e.FuelEventId == targetFuelEventId.Value);
        if (fuelEvent is null)
        {
            return Results.NotFound();
        }

        previousVehicleId = fuelEvent.VehicleId;
    }

    var original = fuelEvent is null ? null : JsonSerializer.Serialize(fuelEvent);
    var eventTimeUtc = FirstTimestamp(images.Select(image => image.CapturedAtUtc));
    var eventTimeLocal = FirstTimestamp(images.Select(image => image.CapturedAtLocal));
    var gallons = request.Gallons;
    var totalPrice = request.TotalPrice;
    decimal? pricePerGallon = gallons.HasValue && totalPrice.HasValue && gallons.Value > 0
        ? Math.Round(totalPrice.Value / gallons.Value, 3, MidpointRounding.AwayFromZero)
        : null;

    fuelEvent ??= new FuelEvent
    {
        CreatedAtUtc = DateTime.UtcNow
    };

    fuelEvent.VehicleId = request.VehicleId;
    fuelEvent.Odometer = request.Odometer;
    fuelEvent.Gallons = gallons;
    fuelEvent.TotalPrice = totalPrice;
    fuelEvent.PricePerGallon = pricePerGallon;
    fuelEvent.LocationName = NormalizeOptional(request.LocationName);
    fuelEvent.Notes = NormalizeOptional(request.Notes);
    fuelEvent.EventTimeUtc = eventTimeUtc;
    fuelEvent.EventTimeLocal = eventTimeLocal;
    fuelEvent.Latitude = Average(images.Select(image => image.Latitude));
    fuelEvent.Longitude = Average(images.Select(image => image.Longitude));
    fuelEvent.PumpSourceImageId = images.FirstOrDefault(image => image.ImageTypeCandidate == ImageType.Pump)?.SourceImageId;
    fuelEvent.DashSourceImageId = images.FirstOrDefault(image => image.ImageTypeCandidate == ImageType.Dashboard)?.SourceImageId;
    fuelEvent.OverallConfidence = 1.0m;
    fuelEvent.NeedsReview = false;
    fuelEvent.ReviewStatus = ReviewStatus.Approved;
    fuelEvent.ReviewReason = null;
    fuelEvent.UpdatedAtUtc = DateTime.UtcNow;

    if (fuelEvent.FuelEventId == 0)
    {
        db.FuelEvents.Add(fuelEvent);
        await db.SaveChangesAsync();
    }

    await RecomputeVehicleDerivedMetricsAsync(db, fuelEvent.VehicleId);
    if (previousVehicleId.HasValue && previousVehicleId != fuelEvent.VehicleId)
    {
        await RecomputeVehicleDerivedMetricsAsync(db, previousVehicleId);
    }

    var existingLinks = await db.FuelEventSourceImages
        .Where(link => link.FuelEventId == fuelEvent.FuelEventId)
        .ToListAsync();

    var selectedImageIds = request.ImageIds.ToHashSet();
    foreach (var staleLink in existingLinks.Where(link => !selectedImageIds.Contains(link.SourceImageId)))
    {
        db.FuelEventSourceImages.Remove(staleLink);
    }

    var linkedImageIds = existingLinks.Select(link => link.SourceImageId).ToHashSet();
    foreach (var imageId in request.ImageIds.Where(imageId => !linkedImageIds.Contains(imageId)))
    {
        db.FuelEventSourceImages.Add(new FuelEventSourceImage
        {
            FuelEventId = fuelEvent.FuelEventId,
            SourceImageId = imageId
        });
    }

    foreach (var image in images)
    {
        image.ProcessingStatus = ProcessingStatus.Completed;
    }

    db.HumanReviews.Add(new HumanReview
    {
        FuelEventId = fuelEvent.FuelEventId,
        ReviewSystem = "ManualEntryUi",
        ReviewStatus = ReviewStatus.Approved,
        ReviewerName = request.ReviewerName,
        ReviewerAtUtc = DateTime.UtcNow,
        OriginalValuesJson = original ?? "{}",
        CorrectedValuesJson = JsonSerializer.Serialize(request),
        Notes = NormalizeOptional(request.Notes)
    });

    await db.SaveChangesAsync();
    return Results.Ok(fuelEvent);
});

app.MapGet("/api/images/{id:int}", async (FuelImportDbContext db, int id) =>
{
    var image = await db.SourceImages.AsNoTracking().FirstOrDefaultAsync(x => x.SourceImageId == id);
    if (image is null || string.IsNullOrWhiteSpace(image.FilePath) || !File.Exists(image.FilePath))
    {
        return Results.NotFound();
    }

    return Results.File(image.FilePath, GetContentType(image.FilePath), enableRangeProcessing: true);
});

app.MapPost("/api/events/{id:int}/review", async (FuelImportDbContext db, int id, ReviewUpdateRequest request) =>
{
    var fuelEvent = await db.FuelEvents.FirstOrDefaultAsync(e => e.FuelEventId == id);
    if (fuelEvent is null)
    {
        return Results.NotFound();
    }

    var original = JsonSerializer.Serialize(fuelEvent);
    var previousVehicleId = fuelEvent.VehicleId;

    fuelEvent.Gallons = request.Gallons ?? fuelEvent.Gallons;
    fuelEvent.TotalPrice = request.TotalPrice ?? fuelEvent.TotalPrice;
    fuelEvent.PricePerGallon = request.PricePerGallon ?? fuelEvent.PricePerGallon;
    fuelEvent.Odometer = request.Odometer ?? fuelEvent.Odometer;
    fuelEvent.VehicleId = request.VehicleId ?? fuelEvent.VehicleId;
    if (request.EventTimeLocal.HasValue)
    {
        fuelEvent.EventTimeLocal = request.EventTimeLocal.Value.LocalDateTime;
        fuelEvent.EventTimeUtc = request.EventTimeLocal.Value.UtcDateTime;
    }

    fuelEvent.Latitude = request.Latitude ?? fuelEvent.Latitude;
    fuelEvent.Longitude = request.Longitude ?? fuelEvent.Longitude;
    fuelEvent.LocationName = request.LocationName ?? fuelEvent.LocationName;
    fuelEvent.Notes = request.Notes ?? fuelEvent.Notes;

    var reviewStatus = request.Approve switch
    {
        true => ReviewStatus.Approved,
        false => ReviewStatus.Rejected,
        null => ReviewStatus.Corrected
    };

    fuelEvent.ReviewStatus = reviewStatus;
    fuelEvent.NeedsReview = reviewStatus != ReviewStatus.Approved;
    fuelEvent.UpdatedAtUtc = DateTime.UtcNow;

    await RecomputeVehicleDerivedMetricsAsync(db, fuelEvent.VehicleId);
    if (previousVehicleId.HasValue && previousVehicleId != fuelEvent.VehicleId)
    {
        await RecomputeVehicleDerivedMetricsAsync(db, previousVehicleId);
    }

    db.HumanReviews.Add(new HumanReview
    {
        FuelEventId = fuelEvent.FuelEventId,
        ReviewSystem = request.ReviewSystem,
        ReviewStatus = reviewStatus,
        ReviewerName = request.ReviewerName,
        ReviewerAtUtc = DateTime.UtcNow,
        OriginalValuesJson = original,
        CorrectedValuesJson = JsonSerializer.Serialize(request),
        Notes = NormalizeOptional(request.Notes)
    });

    await db.SaveChangesAsync();
    return Results.Ok(fuelEvent);
});

app.MapGet("/api/events/log", async (FuelImportDbContext db) =>
{
    var vehicleMap = await db.Vehicles
        .AsNoTracking()
        .ToDictionaryAsync(vehicle => vehicle.VehicleId);

    var events = await db.FuelEvents
        .AsNoTracking()
        .OrderBy(e => e.EventTimeUtc ?? e.EventTimeLocal ?? e.CreatedAtUtc)
        .ThenBy(e => e.FuelEventId)
        .ToListAsync();

    var eventMetrics = BuildEventMetrics(events, vehicleMap);

    var response = events
        .OrderByDescending(e => e.EventTimeUtc ?? e.EventTimeLocal ?? e.CreatedAtUtc)
        .ThenByDescending(e => e.FuelEventId)
        .Select(fuelEvent =>
        {
            vehicleMap.TryGetValue(fuelEvent.VehicleId ?? 0, out var vehicle);
            var metrics = eventMetrics.TryGetValue(fuelEvent.FuelEventId, out var candidate)
                ? candidate
                : EmptyEventMetric();

            return new EventLogResponse
            {
                FuelEventId = fuelEvent.FuelEventId,
                VehicleId = fuelEvent.VehicleId,
                VehicleName = vehicle?.Name ?? "Unassigned",
                EventTimeUtc = fuelEvent.EventTimeUtc,
                EventTimeLocal = fuelEvent.EventTimeLocal,
                LocationName = fuelEvent.LocationName,
                Gallons = fuelEvent.Gallons,
                TotalPrice = fuelEvent.TotalPrice,
                PricePerGallon = fuelEvent.PricePerGallon,
                Odometer = fuelEvent.Odometer,
                MilesSincePrevious = metrics.MilesSincePrevious,
                CalculatedMpg = metrics.CalculatedMpg,
                NeedsReview = fuelEvent.NeedsReview,
                ReviewStatus = fuelEvent.ReviewStatus,
                AnomalyFlags = metrics.AnomalyFlags
            };
        })
        .ToList();

    return Results.Ok(response);
});

app.MapGet("/api/events/report", async (FuelImportDbContext db) =>
{
    var vehicles = await db.Vehicles.AsNoTracking().OrderBy(v => v.Name).ToListAsync();
    var events = await db.FuelEvents
        .AsNoTracking()
        .OrderBy(e => e.EventTimeUtc ?? e.EventTimeLocal ?? e.CreatedAtUtc)
        .ThenBy(e => e.FuelEventId)
        .ToListAsync();

    var vehicleById = vehicles.ToDictionary(vehicle => vehicle.VehicleId);
    var eventMetrics = BuildEventMetrics(events, vehicleById);
    var eventsByVehicle = events
        .Where(fuelEvent => fuelEvent.VehicleId.HasValue)
        .GroupBy(fuelEvent => fuelEvent.VehicleId!.Value)
        .ToDictionary(group => group.Key, group => group.ToList());

    var vehicleRows = vehicles
        .Select(vehicle =>
        {
            eventsByVehicle.TryGetValue(vehicle.VehicleId, out var vehicleEvents);
            vehicleEvents ??= [];

            var metrics = vehicleEvents
                .Select(fuelEvent => eventMetrics.TryGetValue(fuelEvent.FuelEventId, out var metric)
                    ? metric
                    : EmptyEventMetric())
                .ToList();

            var gallons = vehicleEvents.Where(e => e.Gallons.HasValue).Select(e => e.Gallons!.Value).ToList();
            var mpgValues = metrics.Where(metric => metric.CalculatedMpg.HasValue).Select(metric => metric.CalculatedMpg!.Value).ToList();
            var spend = vehicleEvents.Where(e => e.TotalPrice.HasValue).Select(e => e.TotalPrice!.Value).ToList();
            var anomalyCount = metrics.Count(metric => metric.AnomalyFlags.Count > 0);

            return new VehicleReportResponse
            {
                VehicleId = vehicle.VehicleId,
                VehicleName = vehicle.Name,
                MaxGallonsPerFillUp = vehicle.MaxGallonsPerFillUp,
                MaxMpg = vehicle.MaxMpg,
                TotalEvents = vehicleEvents.Count,
                EventsWithAnomalies = anomalyCount,
                AverageGallons = gallons.Count > 0 ? Math.Round(gallons.Average(), 2, MidpointRounding.AwayFromZero) : null,
                AverageMpg = mpgValues.Count > 0 ? Math.Round(mpgValues.Average(), 2, MidpointRounding.AwayFromZero) : null,
                TotalGallons = gallons.Count > 0 ? Math.Round(gallons.Sum(), 2, MidpointRounding.AwayFromZero) : null,
                TotalSpend = spend.Count > 0 ? Math.Round(spend.Sum(), 2, MidpointRounding.AwayFromZero) : null
            };
        })
        .ToList();

    var allAnomalyCount = events.Count(fuelEvent =>
        eventMetrics.TryGetValue(fuelEvent.FuelEventId, out var metrics) && metrics.AnomalyFlags.Count > 0);

    return Results.Ok(new DataReportResponse
    {
        TotalEvents = events.Count,
        EventsWithAnomalies = allAnomalyCount,
        Vehicles = vehicleRows
    });
});

app.MapGet("/api/events/export/csv", async (FuelImportDbContext db) =>
{
    var rows = await db.FuelEvents
        .AsNoTracking()
        .Where(e => e.ReviewStatus == ReviewStatus.Approved && !e.NeedsReview)
        .OrderBy(e => e.EventTimeUtc)
        .Select(e => new
        {
            e.FuelEventId,
            e.VehicleId,
            e.EventTimeLocal,
            e.EventTimeUtc,
            e.Latitude,
            e.Longitude,
            e.LocationName,
            e.Gallons,
            e.TotalPrice,
            e.PricePerGallon,
            e.Odometer,
            e.MilesSincePrevious,
            e.EstimatedMpg,
            e.IsEstimated,
            e.OverallConfidence
        })
        .ToListAsync();

    var sb = new StringBuilder();
    sb.AppendLine("FuelEventId,VehicleId,EventTimeLocal,EventTimeUtc,Latitude,Longitude,LocationName,Gallons,TotalPrice,PricePerGallon,Odometer,MilesSincePrevious,EstimatedMpg,IsEstimated,OverallConfidence");
    foreach (var row in rows)
    {
        sb.AppendLine(string.Join(',',
            row.FuelEventId,
            row.VehicleId,
            Escape(row.EventTimeLocal?.ToString("O", CultureInfo.InvariantCulture)),
            Escape(row.EventTimeUtc?.ToString("O", CultureInfo.InvariantCulture)),
            row.Latitude?.ToString(CultureInfo.InvariantCulture),
            row.Longitude?.ToString(CultureInfo.InvariantCulture),
            Escape(row.LocationName),
            row.Gallons?.ToString(CultureInfo.InvariantCulture),
            row.TotalPrice?.ToString(CultureInfo.InvariantCulture),
            row.PricePerGallon?.ToString(CultureInfo.InvariantCulture),
            row.Odometer,
            row.MilesSincePrevious?.ToString(CultureInfo.InvariantCulture),
            row.EstimatedMpg?.ToString(CultureInfo.InvariantCulture),
            row.IsEstimated,
            row.OverallConfidence.ToString(CultureInfo.InvariantCulture)));
    }

    return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "fuel-events.csv");
});

app.Run();

static async Task<List<ManualReviewGroupResponse>> LoadManualReviewGroupsAsync(
    FuelImportDbContext db,
    ManualReviewGroupingService groupingService)
{
    var images = await db.SourceImages.AsNoTracking().ToListAsync();
    var links = await db.FuelEventSourceImages.AsNoTracking().ToListAsync();
    var fuelEvents = await db.FuelEvents.AsNoTracking().ToListAsync();
    var linkedEventByImageId = links.ToDictionary(link => link.SourceImageId, link => link.FuelEventId);

    foreach (var fuelEvent in fuelEvents)
    {
        if (fuelEvent.PumpSourceImageId.HasValue)
        {
            linkedEventByImageId.TryAdd(fuelEvent.PumpSourceImageId.Value, fuelEvent.FuelEventId);
        }

        if (fuelEvent.DashSourceImageId.HasValue)
        {
            linkedEventByImageId.TryAdd(fuelEvent.DashSourceImageId.Value, fuelEvent.FuelEventId);
        }
    }

    var fuelEventsById = fuelEvents.ToDictionary(fuelEvent => fuelEvent.FuelEventId);
    return groupingService.Group(images, linkedEventByImageId)
        .OrderByDescending(group => group.StartedAtUtc)
        .Select(group =>
        {
            fuelEventsById.TryGetValue(group.FuelEventId ?? 0, out var fuelEvent);
            var orderedImages = group.Images
                .OrderBy(image => image.CapturedAtUtc ?? image.CapturedAtLocal ?? DateTime.MaxValue)
                .ThenBy(image => image.SourceImageId)
                .ToList();

            return new ManualReviewGroupResponse
            {
                GroupKey = group.GroupKey,
                FuelEventId = group.FuelEventId,
                StartedAtUtc = group.StartedAtUtc,
                EndedAtUtc = group.EndedAtUtc,
                Latitude = group.Latitude,
                Longitude = group.Longitude,
                MaxDistanceKilometers = group.MaxDistanceKilometers,
                LocationName = fuelEvent?.LocationName,
                VehicleId = fuelEvent?.VehicleId,
                Odometer = fuelEvent?.Odometer,
                Gallons = fuelEvent?.Gallons,
                TotalPrice = fuelEvent?.TotalPrice,
                PricePerGallon = fuelEvent?.PricePerGallon,
                Notes = fuelEvent?.Notes,
                ReviewStatus = fuelEvent?.ReviewStatus,
                Images = orderedImages
                    .Select((image, index) => new ManualReviewImageResponse
                    {
                        SourceImageId = image.SourceImageId,
                        FileName = image.FileName,
                        ImageTypeCandidate = image.ImageTypeCandidate.ToString(),
                        ImageTypeConfidence = image.ImageTypeConfidence,
                        CapturedAtUtc = image.CapturedAtUtc,
                        Latitude = image.Latitude,
                        Longitude = image.Longitude,
                        DistanceFromPreviousKilometers = index == 0 ? 0d : CalculateDistanceKilometers(orderedImages[index - 1], image),
                        ImageUrl = $"/api/images/{image.SourceImageId}"
                    })
                    .ToList()
            };
        })
        .ToList();
}

static DateTime? FirstTimestamp(IEnumerable<DateTime?> values)
{
    return values
        .Where(value => value.HasValue)
        .Select(value => value!.Value)
        .OrderBy(value => value)
        .Cast<DateTime?>()
        .FirstOrDefault();
}

static async Task RecomputeVehicleDerivedMetricsAsync(FuelImportDbContext db, int? vehicleId)
{
    if (!vehicleId.HasValue)
    {
        return;
    }

    var vehicleEvents = await db.FuelEvents
        .Where(fuelEvent => fuelEvent.VehicleId == vehicleId)
        .OrderBy(fuelEvent => fuelEvent.EventTimeUtc ?? fuelEvent.EventTimeLocal ?? fuelEvent.CreatedAtUtc)
        .ThenBy(fuelEvent => fuelEvent.FuelEventId)
        .ToListAsync();

    FuelEvent? previous = null;
    foreach (var current in vehicleEvents)
    {
        current.MilesSincePrevious = null;
        current.EstimatedMpg = null;
        current.IsEstimated = false;

        if (previous?.Odometer is int previousOdometer && current.Odometer is int currentOdometer)
        {
            var miles = currentOdometer - previousOdometer;
            if (miles >= 0)
            {
                current.MilesSincePrevious = miles;
                if (current.Gallons is decimal gallons && gallons > 0)
                {
                    current.EstimatedMpg = Math.Round(miles / gallons, 3, MidpointRounding.AwayFromZero);
                }
            }
        }

        previous = current;
    }
}

static Dictionary<int, (decimal? MilesSincePrevious, decimal? CalculatedMpg, List<string> AnomalyFlags)> BuildEventMetrics(
    IReadOnlyList<FuelEvent> orderedEvents,
    IReadOnlyDictionary<int, Vehicle> vehiclesById)
{
    var result = new Dictionary<int, (decimal? MilesSincePrevious, decimal? CalculatedMpg, List<string> AnomalyFlags)>();
    var previousEventByVehicle = new Dictionary<int, FuelEvent>();

    foreach (var fuelEvent in orderedEvents)
    {
        decimal? milesSincePrevious = null;
        decimal? calculatedMpg = null;
        var anomalyFlags = new List<string>();

        FuelEvent? previousEvent = null;
        if (fuelEvent.VehicleId is int vehicleId)
        {
            previousEventByVehicle.TryGetValue(vehicleId, out previousEvent);
        }

        if (previousEvent?.Odometer is int previousOdometer && fuelEvent.Odometer is int currentOdometer)
        {
            var miles = currentOdometer - previousOdometer;
            if (miles >= 0)
            {
                milesSincePrevious = miles;
            }
        }

        if (milesSincePrevious.HasValue && fuelEvent.Gallons is decimal gallons && gallons > 0)
        {
            calculatedMpg = Math.Round(milesSincePrevious.Value / gallons, 3, MidpointRounding.AwayFromZero);
        }

        if (fuelEvent.VehicleId is int currentVehicleId && vehiclesById.TryGetValue(currentVehicleId, out var vehicleWithThresholds))
        {
            if (vehicleWithThresholds.MaxGallonsPerFillUp is decimal maxGallons && fuelEvent.Gallons is decimal eventGallons && eventGallons > maxGallons)
            {
                anomalyFlags.Add($"Gallons {eventGallons:0.###} exceeds vehicle max {maxGallons:0.###}.");
            }

            if (vehicleWithThresholds.MaxMpg is decimal maxMpg && calculatedMpg is decimal eventMpg && eventMpg > maxMpg)
            {
                anomalyFlags.Add($"MPG {eventMpg:0.###} exceeds vehicle max {maxMpg:0.###}.");
            }
        }

        result[fuelEvent.FuelEventId] = (milesSincePrevious, calculatedMpg, anomalyFlags);

        if (fuelEvent.VehicleId is int id)
        {
            previousEventByVehicle[id] = fuelEvent;
        }
    }

    return result;
}

static (decimal? MilesSincePrevious, decimal? CalculatedMpg, List<string> AnomalyFlags) EmptyEventMetric()
{
    return (null, null, new List<string>());
}

static bool AreThresholdsValid(decimal? maxGallonsPerFillUp, decimal? maxMpg, out string? error)
{
    if (maxGallonsPerFillUp.HasValue && maxGallonsPerFillUp.Value <= 0)
    {
        error = "Max gallons must be greater than zero when provided.";
        return false;
    }

    if (maxMpg.HasValue && maxMpg.Value <= 0)
    {
        error = "Max MPG must be greater than zero when provided.";
        return false;
    }

    error = null;
    return true;
}

static string Escape(string? value)
{
    if (string.IsNullOrEmpty(value)) return string.Empty;
    return value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}

static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

static string CreateUniqueDashboardLabel(string name, IEnumerable<string> existingLabels)
{
    var slug = new string(name
        .Trim()
        .ToLowerInvariant()
        .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
        .ToArray())
        .Trim('-');

    if (string.IsNullOrWhiteSpace(slug))
    {
        slug = "vehicle";
    }

    var existing = existingLabels
        .Where(label => !string.IsNullOrWhiteSpace(label))
        .Select(label => label.ToLowerInvariant())
        .ToHashSet();

    var candidate = slug;
    var suffix = 2;
    while (existing.Contains(candidate))
    {
        candidate = $"{slug}-{suffix}";
        suffix++;
    }

    return candidate;
}

static double? Average(IEnumerable<double?> values)
{
    var items = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
    return items.Count == 0 ? null : items.Average();
}

static double? CalculateDistanceKilometers(SourceImage? left, SourceImage? right)
{
    if (left?.Latitude is not { } leftLatitude || left.Longitude is not { } leftLongitude)
    {
        return null;
    }

    if (right?.Latitude is not { } rightLatitude || right.Longitude is not { } rightLongitude)
    {
        return null;
    }

    const double earthRadiusKm = 6371.0d;
    var deltaLatitude = DegreesToRadians(rightLatitude - leftLatitude);
    var deltaLongitude = DegreesToRadians(rightLongitude - leftLongitude);
    var a = Math.Pow(Math.Sin(deltaLatitude / 2), 2) +
            Math.Cos(DegreesToRadians(leftLatitude)) * Math.Cos(DegreesToRadians(rightLatitude)) *
            Math.Pow(Math.Sin(deltaLongitude / 2), 2);
    var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    return earthRadiusKm * c;
}

static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0d;

static string GetContentType(string path)
{
    return Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        _ => "application/octet-stream"
    };
}
