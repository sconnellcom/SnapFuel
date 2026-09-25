using System.Text.Json;
using FuelImport.Core.Interfaces;
using FuelImport.Core.Models;
using FuelImport.Core.Options;
using FuelImport.Core.Services;
using FuelImport.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelImport.Web.Services;

/// <summary>
/// Runs the vision model over grouped photos and writes the results into the review queue as
/// auto-detected fuel events that still require a human to mark them reviewed.
/// </summary>
public class AutoDetectionService(
    FuelImportDbContext db,
    ManualReviewGroupingService groupingService,
    IVisionAutoDetector detector,
    DetectionEstimator estimator,
    AutoDetectProgressTracker progressTracker,
    IOptions<AutoDetectOptions> autoDetectOptions,
    ILogger<AutoDetectionService> logger)
{
    private readonly AutoDetectOptions _options = autoDetectOptions.Value;

    public async Task<AutoDetectRunResult> RunAsync(
        string? groupKey,
        bool redetectExisting,
        int? limit,
        string? progressId,
        CancellationToken cancellationToken = default)
    {
        var result = new AutoDetectRunResult();

        if (!detector.IsConfigured)
        {
            result.ErrorMessage = "Auto-detect is not configured. Enable HuggingFaceVision and provide an API token.";
            return result;
        }

        var targetsSingleGroup = !string.IsNullOrWhiteSpace(groupKey);
        if (!targetsSingleGroup && !_options.AllowBulkRuns)
        {
            result.ErrorMessage = "Bulk auto-detect is turned off. Run it one group at a time, or set AutoDetect:AllowBulkRuns to true once you are happy with the results.";
            return result;
        }

        var images = await db.SourceImages.AsNoTracking().ToListAsync(cancellationToken);
        var links = await db.FuelEventSourceImages.AsNoTracking().ToListAsync(cancellationToken);
        var fuelEvents = await db.FuelEvents.AsNoTracking().ToListAsync(cancellationToken);
        var vehicles = await db.Vehicles.AsNoTracking().ToListAsync(cancellationToken);

        var linkedEventByImageId = links
            .GroupBy(link => link.SourceImageId)
            .ToDictionary(group => group.Key, group => group.Max(link => link.FuelEventId));
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
        var candidates = groupingService.Group(images, linkedEventByImageId)
            .Where(group => !targetsSingleGroup || string.Equals(group.GroupKey, groupKey, StringComparison.Ordinal))
            .Where(group => ShouldDetect(group, fuelEventsById, redetectExisting, targetsSingleGroup))
            .OrderByDescending(group => group.StartedAtUtc)
            .Take(Math.Clamp(limit ?? _options.MaxGroupsPerRun, 1, _options.MaxGroupsPerRun))
            .ToList();

        progressTracker.Start(progressId, candidates.Sum(group => group.Images.Count));
        try
        {
            foreach (var group in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.GroupsExamined++;

                var groupResult = await DetectGroupAsync(group, vehicles, progressId, cancellationToken);
                result.Groups.Add(groupResult);
                if (groupResult.Succeeded)
                {
                    result.GroupsDetected++;
                }
                else if (groupResult.WasSplit)
                {
                    result.GroupsSplit++;
                }
                else
                {
                    result.GroupsFailed++;
                }
            }
        }
        finally
        {
            progressTracker.Complete(progressId);
        }

        return result;
    }

    private static bool ShouldDetect(
        ManualReviewGroup group,
        IReadOnlyDictionary<int, FuelEvent> fuelEventsById,
        bool redetectExisting,
        bool targetsSingleGroup)
    {
        if (group.Images.Count == 0)
        {
            return false;
        }

        if (group.FuelEventId is not int eventId || !fuelEventsById.TryGetValue(eventId, out var fuelEvent))
        {
            return true;
        }

        if (!redetectExisting)
        {
            return false;
        }

        // Re-running a single hand-picked group is an explicit request, so an already reviewed event is fair game.
        return targetsSingleGroup || fuelEvent.ReviewStatus != ReviewStatus.Reviewed;
    }

    private async Task<GroupDetectionResult> DetectGroupAsync(
        ManualReviewGroup group,
        IReadOnlyCollection<Vehicle> vehicles,
        string? progressId,
        CancellationToken cancellationToken)
    {
        var groupResult = new GroupDetectionResult
        {
            GroupKey = group.GroupKey,
            FuelEventId = group.FuelEventId
        };

        var existingEvent = group.FuelEventId is int eventId
            ? await db.FuelEvents.FirstOrDefaultAsync(e => e.FuelEventId == eventId, cancellationToken)
            : null;

        var knownVehicle = existingEvent?.VehicleId is int vehicleId
            ? vehicles.FirstOrDefault(vehicle => vehicle.VehicleId == vehicleId)
            : null;

        var candidateVehicles = knownVehicle is not null
            ? [knownVehicle]
            : vehicles.Where(vehicle => vehicle.Active).ToList();

        var orderedImages = group.Images
            .OrderBy(image => image.CapturedAtUtc ?? image.CapturedAtLocal ?? DateTime.MaxValue)
            .ThenBy(image => image.SourceImageId)
            .ToList();

        var captureUtc = orderedImages
            .Select(image => image.CapturedAtUtc ?? image.CapturedAtLocal)
            .FirstOrDefault(value => value.HasValue) ?? DateTime.UtcNow;

        var historyByVehicleId = await LoadOdometerHistoryAsync(candidateVehicles, existingEvent?.FuelEventId, cancellationToken);
        var estimates = knownVehicle is not null
            ? estimator.Create(
                knownVehicle,
                captureUtc,
                historyByVehicleId.TryGetValue(knownVehicle.VehicleId, out var knownHistory) ? knownHistory : [])
            : estimator.CreateForUnknownVehicle(captureUtc, candidateVehicles, historyByVehicleId);

        var hints = candidateVehicles
            .Select(vehicle => new VehicleHint
            {
                VehicleId = vehicle.VehicleId,
                Name = vehicle.Name,
                PhotoDescription = vehicle.PhotoDescription
            })
            .ToList();

        var maxConcurrency = Math.Clamp(_options.MaxConcurrentImages, 1, orderedImages.Count);
        using var concurrencyGate = new SemaphoreSlim(maxConcurrency);
        var detectionTasks = orderedImages.Select(async image =>
        {
            await concurrencyGate.WaitAsync(cancellationToken);
            try
            {
                var detection = await detector.DetectAsync(image, estimates, hints, cancellationToken);
                detection.Warnings.AddRange(estimator.Validate(detection, estimates));
                return detection;
            }
            finally
            {
                progressTracker.Advance(progressId);
                concurrencyGate.Release();
            }
        });

        groupResult.Images.AddRange(await Task.WhenAll(detectionTasks));

        var usable = groupResult.Images.Where(detection => detection.ErrorMessage is null).ToList();
        if (usable.Count == 0)
        {
            groupResult.Succeeded = false;
            groupResult.Message = groupResult.Images.FirstOrDefault()?.ErrorMessage ?? "No images could be analyzed.";
            return groupResult;
        }

        var dashboards = usable
            .Where(detection => detection.ImageType == ImageType.Dashboard && detection.Odometer.HasValue)
            .ToList();

        var distinctOdometers = dashboards.Select(detection => detection.Odometer!.Value).Distinct().ToList();
        if (distinctOdometers.Count > 1)
        {
            return await HandleMultipleDashboardsAsync(group, orderedImages, groupResult, existingEvent, dashboards, cancellationToken);
        }

        var pumpTotals = SumPumpReadings(usable);
        var dashboard = dashboards.OrderByDescending(detection => detection.Confidence).FirstOrDefault();

        if (pumpTotals.Gallons is null && pumpTotals.TotalCost is null && dashboard is null)
        {
            groupResult.Succeeded = false;
            groupResult.Message = "The model did not find a fuel pump or dashboard reading in this group.";
            return groupResult;
        }

        groupResult.Gallons = pumpTotals.Gallons;
        groupResult.Liters = pumpTotals.Liters;
        groupResult.TotalPrice = pumpTotals.TotalCost;
        groupResult.Odometer = dashboard?.Odometer;
        groupResult.VehicleId = knownVehicle?.VehicleId ?? ResolveDetectedVehicleId(usable, candidateVehicles);
        groupResult.Confidence = Math.Round(
            usable
                .Where(detection => detection.ImageType != ImageType.Unknown)
                .Select(detection => detection.Confidence)
                .DefaultIfEmpty(0m)
                .Average(),
            3,
            MidpointRounding.AwayFromZero);

        await PersistAsync(group, orderedImages, existingEvent, groupResult, pumpTotals.SourceCount, cancellationToken);

        groupResult.Succeeded = true;
        var notes = new List<string> { "Auto-detected and queued for review." };
        if (pumpTotals.SourceCount > 1)
        {
            notes[0] = $"Auto-detected and queued for review (totals summed from {pumpTotals.SourceCount} pump photos).";
        }

        if (groupResult.LeftExistingValuesAlone)
        {
            notes.Add("Values you already entered were left alone.");
        }

        groupResult.Message = string.Join(" ", notes);
        return groupResult;
    }

    /// <summary>Adds up distinct pump readings so a multi-transaction stop lands as one total.</summary>
    private static (decimal? Gallons, decimal? Liters, decimal? TotalCost, int SourceCount) SumPumpReadings(IReadOnlyCollection<ImageDetection> detections)
    {
        var readings = detections
            .Where(detection => detection.ImageType == ImageType.Pump && (detection.Gallons.HasValue || detection.TotalCost.HasValue))
            .GroupBy(detection => (
                Gallons: detection.Gallons.HasValue ? Math.Round(detection.Gallons.Value, 2) : (decimal?)null,
                Cost: detection.TotalCost.HasValue ? Math.Round(detection.TotalCost.Value, 2) : (decimal?)null))
            .Select(group => group.First())
            .ToList();

        if (readings.Count == 0)
        {
            return (null, null, null, 0);
        }

        var gallons = readings.Where(reading => reading.Gallons.HasValue).Select(reading => reading.Gallons!.Value).ToList();
        var liters = readings.Where(reading => reading.Liters.HasValue).Select(reading => reading.Liters!.Value).ToList();
        var costs = readings.Where(reading => reading.TotalCost.HasValue).Select(reading => reading.TotalCost!.Value).ToList();

        // Only surface a liters total when every summed reading was reported in liters; otherwise the sum would be ambiguous.
        return (
            gallons.Count > 0 ? Math.Round(gallons.Sum(), 3, MidpointRounding.AwayFromZero) : null,
            liters.Count == readings.Count ? Math.Round(liters.Sum(), 3, MidpointRounding.AwayFromZero) : null,
            costs.Count > 0 ? Math.Round(costs.Sum(), 2, MidpointRounding.AwayFromZero) : null,
            readings.Count);
    }

    private static int? ResolveDetectedVehicleId(IEnumerable<ImageDetection> detections, IReadOnlyCollection<Vehicle> vehicles)
    {
        var names = detections
            .Where(detection => !string.IsNullOrWhiteSpace(detection.VehicleName))
            .OrderByDescending(detection => detection.Confidence)
            .Select(detection => detection.VehicleName!);

        foreach (var name in names)
        {
            var match = vehicles.FirstOrDefault(vehicle => string.Equals(vehicle.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match.VehicleId;
            }
        }

        return null;
    }

    /// <summary>
    /// Two different odometer readings mean the photos cover more than one fuel stop, so the images are
    /// re-keyed into separate review groups instead of being merged into a single event.
    /// </summary>
    private async Task<GroupDetectionResult> HandleMultipleDashboardsAsync(
        ManualReviewGroup group,
        IReadOnlyList<SourceImage> orderedImages,
        GroupDetectionResult groupResult,
        FuelEvent? existingEvent,
        IReadOnlyCollection<ImageDetection> dashboards,
        CancellationToken cancellationToken)
    {
        groupResult.Succeeded = false;

        if (existingEvent is not null && existingEvent.ReviewStatus == ReviewStatus.Reviewed)
        {
            groupResult.Message = $"Found {dashboards.Count} different odometer readings, but this group is already reviewed. Split it by hand if needed.";
            return groupResult;
        }

        var dashboardImageIds = dashboards.Select(detection => detection.SourceImageId).ToHashSet();
        var segments = new List<List<SourceImage>>();
        var current = new List<SourceImage>();

        foreach (var image in orderedImages)
        {
            var startsNewSegment = dashboardImageIds.Contains(image.SourceImageId)
                && current.Any(existing => dashboardImageIds.Contains(existing.SourceImageId));

            if (startsNewSegment)
            {
                segments.Add(current);
                current = [];
            }

            current.Add(image);
        }

        segments.Add(current);

        if (segments.Count < 2)
        {
            groupResult.Message = $"Found {dashboards.Count} different odometer readings but could not work out where to split the group.";
            return groupResult;
        }

        var imageIds = orderedImages.Select(image => image.SourceImageId).ToList();
        var trackedImages = await db.SourceImages
            .Where(image => imageIds.Contains(image.SourceImageId))
            .ToListAsync(cancellationToken);

        foreach (var segment in segments)
        {
            var segmentKey = $"manual-{Guid.NewGuid():N}";
            var segmentIds = segment.Select(image => image.SourceImageId).ToHashSet();
            foreach (var image in trackedImages.Where(image => segmentIds.Contains(image.SourceImageId)))
            {
                image.ManualGroupKey = segmentKey;
            }
        }

        if (existingEvent is not null)
        {
            var links = await db.FuelEventSourceImages
                .Where(link => link.FuelEventId == existingEvent.FuelEventId)
                .ToListAsync(cancellationToken);

            db.FuelEventSourceImages.RemoveRange(links);
            db.FuelEvents.Remove(existingEvent);
            groupResult.FuelEventId = null;
        }

        await db.SaveChangesAsync(cancellationToken);

        groupResult.WasSplit = true;
        groupResult.Message = $"Found {dashboards.Count} different odometer readings, so the group was split into {segments.Count} groups. Run auto detect on each one.";
        logger.LogInformation("Split group {GroupKey} into {SegmentCount} groups after multiple dashboards were detected.", group.GroupKey, segments.Count);
        return groupResult;
    }

    private async Task<Dictionary<int, List<FuelEvent>>> LoadOdometerHistoryAsync(
        IReadOnlyCollection<Vehicle> vehicles,
        int? excludeFuelEventId,
        CancellationToken cancellationToken)
    {
        var vehicleIds = vehicles.Select(vehicle => vehicle.VehicleId).ToList();
        if (vehicleIds.Count == 0)
        {
            return [];
        }

        var query = db.FuelEvents
            .AsNoTracking()
            .Where(fuelEvent => fuelEvent.Odometer != null
                && fuelEvent.VehicleId != null
                && vehicleIds.Contains(fuelEvent.VehicleId.Value));

        if (excludeFuelEventId.HasValue)
        {
            query = query.Where(fuelEvent => fuelEvent.FuelEventId != excludeFuelEventId.Value);
        }

        var events = await query.ToListAsync(cancellationToken);
        return events
            .GroupBy(fuelEvent => fuelEvent.VehicleId!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    private async Task PersistAsync(
        ManualReviewGroup group,
        IReadOnlyList<SourceImage> orderedImages,
        FuelEvent? existingEvent,
        GroupDetectionResult groupResult,
        int pumpSourceCount,
        CancellationToken cancellationToken)
    {
        var fuelEvent = existingEvent ?? new FuelEvent { CreatedAtUtc = DateTime.UtcNow };

        // Only a value this service produced may be replaced; anything a person entered stays untouched.
        var mayReplaceExisting = existingEvent is null
            || (existingEvent.EntrySource == EntrySource.AutoDetected && existingEvent.ReviewStatus == ReviewStatus.AutoDetected);

        var previousGallons = fuelEvent.Gallons;
        var previousLiters = fuelEvent.Liters;
        var previousTotalPrice = fuelEvent.TotalPrice;
        var previousOdometer = fuelEvent.Odometer;
        var previousVehicleId = fuelEvent.VehicleId;

        fuelEvent.VehicleId ??= groupResult.VehicleId;
        fuelEvent.Gallons = Merge(fuelEvent.Gallons, groupResult.Gallons, mayReplaceExisting);
        fuelEvent.Liters = Merge(fuelEvent.Liters, groupResult.Liters, mayReplaceExisting);
        fuelEvent.TotalPrice = Merge(fuelEvent.TotalPrice, groupResult.TotalPrice, mayReplaceExisting);
        fuelEvent.Odometer = Merge(fuelEvent.Odometer, groupResult.Odometer, mayReplaceExisting);

        var changedAnything = fuelEvent.Gallons != previousGallons
            || fuelEvent.Liters != previousLiters
            || fuelEvent.TotalPrice != previousTotalPrice
            || fuelEvent.Odometer != previousOdometer
            || fuelEvent.VehicleId != previousVehicleId;

        groupResult.Gallons = fuelEvent.Gallons;
        groupResult.Liters = fuelEvent.Liters;
        groupResult.TotalPrice = fuelEvent.TotalPrice;
        groupResult.Odometer = fuelEvent.Odometer;
        groupResult.LeftExistingValuesAlone = !mayReplaceExisting;
        fuelEvent.PricePerGallon = fuelEvent.Gallons is decimal g && g > 0m && fuelEvent.TotalPrice is decimal p
            ? Math.Round(p / g, 3, MidpointRounding.AwayFromZero)
            : fuelEvent.PricePerGallon;
        fuelEvent.EventTimeUtc ??= orderedImages.Select(image => image.CapturedAtUtc).FirstOrDefault(value => value.HasValue);
        fuelEvent.EventTimeLocal ??= orderedImages.Select(image => image.CapturedAtLocal).FirstOrDefault(value => value.HasValue);
        fuelEvent.Latitude ??= Average(orderedImages.Select(image => image.Latitude));
        fuelEvent.Longitude ??= Average(orderedImages.Select(image => image.Longitude));
        fuelEvent.PumpSourceImageId = groupResult.Images.FirstOrDefault(detection => detection.ImageType == ImageType.Pump)?.SourceImageId
            ?? fuelEvent.PumpSourceImageId;
        fuelEvent.DashSourceImageId = groupResult.Images.FirstOrDefault(detection => detection.ImageType == ImageType.Dashboard)?.SourceImageId
            ?? fuelEvent.DashSourceImageId;
        fuelEvent.OverallConfidence = groupResult.Confidence;

        // A group that a human already signed off on only reopens if detection actually filled a gap.
        if (changedAnything || existingEvent is null || existingEvent.ReviewStatus != ReviewStatus.Reviewed)
        {
            fuelEvent.NeedsReview = true;
            fuelEvent.ReviewStatus = ReviewStatus.AutoDetected;
            fuelEvent.EntrySource = existingEvent?.EntrySource ?? EntrySource.AutoDetected;
        }

        fuelEvent.DetectedAtUtc = DateTime.UtcNow;
        fuelEvent.DetectionDetailsJson = JsonSerializer.Serialize(groupResult.Images);
        fuelEvent.ReviewReason = BuildReviewReason(groupResult, pumpSourceCount);
        fuelEvent.UpdatedAtUtc = DateTime.UtcNow;

        if (fuelEvent.FuelEventId == 0)
        {
            db.FuelEvents.Add(fuelEvent);
            await db.SaveChangesAsync(cancellationToken);
        }

        groupResult.FuelEventId = fuelEvent.FuelEventId;

        var existingLinks = await db.FuelEventSourceImages
            .Where(link => link.FuelEventId == fuelEvent.FuelEventId)
            .Select(link => link.SourceImageId)
            .ToListAsync(cancellationToken);

        foreach (var image in orderedImages.Where(image => !existingLinks.Contains(image.SourceImageId)))
        {
            db.FuelEventSourceImages.Add(new FuelEventSourceImage
            {
                FuelEventId = fuelEvent.FuelEventId,
                SourceImageId = image.SourceImageId
            });
        }

        var detectionByImageId = groupResult.Images.ToDictionary(detection => detection.SourceImageId);
        var trackedImages = await db.SourceImages
            .Where(image => orderedImages.Select(ordered => ordered.SourceImageId).Contains(image.SourceImageId))
            .ToListAsync(cancellationToken);

        foreach (var image in trackedImages)
        {
            if (!detectionByImageId.TryGetValue(image.SourceImageId, out var detection) || detection.ImageType == ImageType.Unknown)
            {
                continue;
            }

            image.ImageTypeCandidate = detection.ImageType;
            image.ImageTypeConfidence = detection.Confidence;
            image.RawClassificationJson = detection.RawJson;
            image.ProcessingStatus = ProcessingStatus.OcrCompleted;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Auto-detected fuel event {FuelEventId} for group {GroupKey}.", fuelEvent.FuelEventId, group.GroupKey);
    }

    private static string? BuildReviewReason(GroupDetectionResult groupResult, int pumpSourceCount)
    {
        var parts = new List<string>();
        if (pumpSourceCount > 1)
        {
            parts.Add($"Totals summed from {pumpSourceCount} pump photos.");
        }

        if (groupResult.LeftExistingValuesAlone)
        {
            parts.Add("Existing entered values were kept; only blanks were filled.");
        }

        parts.AddRange(groupResult.Images.SelectMany(detection => detection.Warnings).Distinct());

        var reason = parts.Count > 0
            ? $"Auto-detected. {string.Join(" ", parts)}"
            : "Auto-detected and awaiting human review.";

        return reason.Length > 512 ? reason[..512] : reason;
    }

    private static decimal? Merge(decimal? existing, decimal? detected, bool mayReplaceExisting) =>
        existing is null || mayReplaceExisting ? detected ?? existing : existing;

    private static int? Merge(int? existing, int? detected, bool mayReplaceExisting) =>
        existing is null || mayReplaceExisting ? detected ?? existing : existing;

    private static double? Average(IEnumerable<double?> values)
    {
        var present = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        return present.Count == 0 ? null : present.Average();
    }
}
