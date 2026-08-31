using System.Globalization;
using System.Text;
using System.Text.Json;
using FuelImport.Aws.Options;
using FuelImport.Aws.Services;
using FuelImport.Core.Models;
using FuelImport.Data.Persistence;
using FuelImport.Web.Contracts;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<FuelImportDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("FuelImport") ?? "Data Source=fuelimport.db"));
builder.Services.Configure<AwsVisionOptions>(builder.Configuration.GetSection("AwsVision"));
builder.Services.AddScoped<A2IReviewRouter>();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FuelImportDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.EnsureSeededAsync(db);
}

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

app.MapPost("/api/events/{id:int}/review", async (FuelImportDbContext db, int id, ReviewUpdateRequest request) =>
{
    var fuelEvent = await db.FuelEvents.FirstOrDefaultAsync(e => e.FuelEventId == id);
    if (fuelEvent is null)
    {
        return Results.NotFound();
    }

    var original = JsonSerializer.Serialize(fuelEvent);

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
    var reviewStatus = request.Approve switch
    {
        true => ReviewStatus.Approved,
        false => ReviewStatus.Rejected,
        null => ReviewStatus.Corrected
    };

    fuelEvent.ReviewStatus = reviewStatus;
    fuelEvent.NeedsReview = reviewStatus != ReviewStatus.Approved;
    fuelEvent.UpdatedAtUtc = DateTime.UtcNow;

    db.HumanReviews.Add(new HumanReview
    {
        FuelEventId = fuelEvent.FuelEventId,
        ReviewSystem = request.ReviewSystem,
        ReviewStatus = reviewStatus,
        ReviewerName = request.ReviewerName,
        ReviewerAtUtc = DateTime.UtcNow,
        OriginalValuesJson = original,
        CorrectedValuesJson = JsonSerializer.Serialize(request),
        Notes = request.Notes
    });

    await db.SaveChangesAsync();
    return Results.Ok(fuelEvent);
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
    sb.AppendLine("FuelEventId,VehicleId,EventTimeLocal,EventTimeUtc,Latitude,Longitude,Gallons,TotalPrice,PricePerGallon,Odometer,MilesSincePrevious,EstimatedMpg,IsEstimated,OverallConfidence");
    foreach (var row in rows)
    {
        sb.AppendLine(string.Join(',',
            row.FuelEventId,
            row.VehicleId,
            Escape(row.EventTimeLocal?.ToString("O", CultureInfo.InvariantCulture)),
            Escape(row.EventTimeUtc?.ToString("O", CultureInfo.InvariantCulture)),
            row.Latitude?.ToString(CultureInfo.InvariantCulture),
            row.Longitude?.ToString(CultureInfo.InvariantCulture),
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

static string Escape(string? value)
{
    if (string.IsNullOrEmpty(value)) return string.Empty;
    return value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
