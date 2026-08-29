using System.Text.Json;
using FuelImport.Aws.Services;
using FuelImport.Core.Interfaces;
using FuelImport.Core.Models;
using FuelImport.Core.Options;
using FuelImport.Core.Services;
using FuelImport.Data.Persistence;
using FuelImport.Worker.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelImport.Worker.Services;

public class ImportWorker(
    IServiceProvider serviceProvider,
    IOptions<ImportOptions> importOptions,
    ILogger<ImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = importOptions.Value;
        if (string.IsNullOrWhiteSpace(options.RootFolder) || !Directory.Exists(options.RootFolder))
        {
            logger.LogWarning("Import skipped because RootFolder is missing or does not exist: {Folder}", options.RootFolder);
            return;
        }

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelImportDbContext>();
        var metadataExtractor = scope.ServiceProvider.GetRequiredService<IImageMetadataExtractor>();
        var classifier = scope.ServiceProvider.GetRequiredService<IImageClassifier>();
        var pumpOcr = scope.ServiceProvider.GetRequiredService<IPumpOcrService>();
        var dashOcr = scope.ServiceProvider.GetRequiredService<IDashboardOcrService>();
        var pairing = scope.ServiceProvider.GetRequiredService<IEventPairingService>();
        var validation = scope.ServiceProvider.GetRequiredService<IValidationEngine>();
        var resolver = scope.ServiceProvider.GetRequiredService<IVehicleResolver>();
        var confidence = scope.ServiceProvider.GetRequiredService<IConfidenceScorer>();
        var a2i = scope.ServiceProvider.GetRequiredService<A2IReviewRouter>();
        var confidenceOptions = scope.ServiceProvider.GetRequiredService<IOptions<ConfidenceOptions>>().Value;

        var batch = new ImportBatch { RootFolder = options.RootFolder, IsDryRun = options.DryRun };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(stoppingToken);

        var discovered = DiscoverFiles(options).ToList();
        logger.LogInformation("Discovered {Count} candidate files", discovered.Count);

        var newImages = new List<SourceImage>();
        foreach (var path in discovered)
        {
            var metadata = await metadataExtractor.ExtractAsync(path, stoppingToken);
            if (await db.SourceImages.AnyAsync(x => x.FileHash == metadata.FileHash, stoppingToken))
            {
                logger.LogInformation("Skipped duplicate file {Path}", path);
                continue;
            }

            var image = new SourceImage
            {
                FilePath = metadata.FilePath,
                FileName = metadata.FileName,
                FileHash = metadata.FileHash,
                CapturedAtLocal = metadata.CapturedAtLocal,
                CapturedAtUtc = metadata.CapturedAtUtc ?? metadata.FileModifiedUtc,
                Latitude = metadata.Latitude,
                Longitude = metadata.Longitude,
                Width = metadata.Width,
                Height = metadata.Height,
                RawMetadataJson = metadata.RawMetadataJson,
                ProcessingStatus = ProcessingStatus.MetadataExtracted
            };

            var classification = await classifier.ClassifyAsync(image, stoppingToken);
            image.ImageTypeCandidate = classification.ImageType;
            image.ImageTypeConfidence = classification.Confidence;
            image.RawClassificationJson = classification.RawJson;
            image.ProcessingStatus = ProcessingStatus.Classified;
            newImages.Add(image);
        }

        if (!options.DryRun)
        {
            db.SourceImages.AddRange(newImages);
            await db.SaveChangesAsync(stoppingToken);
        }

        var pairCandidates = pairing.Pair(newImages);
        var vehicles = await db.Vehicles.Where(v => v.Active).ToListAsync(stoppingToken);

        foreach (var candidate in pairCandidates)
        {
            if (options.DryRun)
            {
                continue;
            }

            var fuelEvent = new FuelEvent
            {
                PumpSourceImageId = candidate.PumpImage?.SourceImageId,
                DashSourceImageId = candidate.DashImage?.SourceImageId,
                EventTimeUtc = candidate.PumpImage?.CapturedAtUtc ?? candidate.DashImage?.CapturedAtUtc,
                EventTimeLocal = candidate.PumpImage?.CapturedAtLocal ?? candidate.DashImage?.CapturedAtLocal,
                Latitude = candidate.PumpImage?.Latitude ?? candidate.DashImage?.Latitude,
                Longitude = candidate.PumpImage?.Longitude ?? candidate.DashImage?.Longitude,
                NeedsReview = candidate.IsAmbiguous,
                ReviewReason = candidate.IsAmbiguous ? "AmbiguousPairing" : null
            };

            decimal pumpOcrConfidence = 0m;
            decimal dashOcrConfidence = 0m;

            if (candidate.PumpImage is not null)
            {
                var pump = await pumpOcr.ExtractAsync(candidate.PumpImage, stoppingToken);
                db.OcrResults.Add(new OcrResult
                {
                    SourceImageId = candidate.PumpImage.SourceImageId,
                    Provider = pump.Provider,
                    ProviderOperation = pump.Operation,
                    RawResponseJson = pump.RawJson,
                    ParsedText = pump.ParsedText,
                    ParsedFieldsJson = JsonSerializer.Serialize(pump.ParsedFields),
                    ConfidenceScore = pump.Confidence
                });
                pumpOcrConfidence = pump.Confidence;
                fuelEvent.Gallons = TryDecimal(pump.ParsedFields, "gallons");
                fuelEvent.TotalPrice = TryDecimal(pump.ParsedFields, "totalPrice");
                fuelEvent.PricePerGallon = TryDecimal(pump.ParsedFields, "pricePerGallon");
            }

            if (candidate.DashImage is not null)
            {
                var dash = await dashOcr.ExtractAsync(candidate.DashImage, stoppingToken);
                db.OcrResults.Add(new OcrResult
                {
                    SourceImageId = candidate.DashImage.SourceImageId,
                    Provider = dash.Provider,
                    ProviderOperation = dash.Operation,
                    RawResponseJson = dash.RawJson,
                    ParsedText = dash.ParsedText,
                    ParsedFieldsJson = JsonSerializer.Serialize(dash.ParsedFields),
                    ConfidenceScore = dash.Confidence
                });
                dashOcrConfidence = dash.Confidence;
                fuelEvent.Odometer = TryInt(dash.ParsedFields, "odometer");
            }

            var vehicle = await resolver.ResolveAsync(fuelEvent, vehicles, stoppingToken);
            fuelEvent.VehicleId = vehicle.VehicleId;
            var previous = await db.FuelEvents
                .Where(e => e.VehicleId == fuelEvent.VehicleId)
                .OrderByDescending(e => e.EventTimeUtc)
                .FirstOrDefaultAsync(stoppingToken);

            var selectedVehicle = vehicles.FirstOrDefault(v => v.VehicleId == fuelEvent.VehicleId);
            var validationResult = validation.Validate(fuelEvent, selectedVehicle, previous);
            foreach (var issue in validationResult.Issues)
            {
                issue.FuelEventId = fuelEvent.FuelEventId;
            }

            var classificationConfidence = ((candidate.PumpImage?.ImageTypeConfidence ?? 0m) + (candidate.DashImage?.ImageTypeConfidence ?? 0m)) / (candidate.DashImage is null || candidate.PumpImage is null ? 1 : 2);

            fuelEvent.OverallConfidence = confidence.Calculate(pumpOcrConfidence, dashOcrConfidence, classificationConfidence, candidate.PairingConfidence, vehicle.Confidence, validationResult.Issues.Count(i => i.Severity == "error"), validationResult.Issues.Count(i => i.Severity == "warning"));

            if (validationResult.HasErrors || fuelEvent.OverallConfidence < confidenceOptions.ReviewThreshold || fuelEvent.NeedsReview)
            {
                fuelEvent.NeedsReview = true;
                fuelEvent.ReviewStatus = ReviewStatus.Pending;
                fuelEvent.ReviewReason ??= await a2i.RouteAsync(fuelEvent, validationResult.Issues, stoppingToken);
            }
            else if (fuelEvent.OverallConfidence >= confidenceOptions.AutoApproveThreshold)
            {
                fuelEvent.ReviewStatus = ReviewStatus.Approved;
            }

            db.FuelEvents.Add(fuelEvent);
            await db.SaveChangesAsync(stoppingToken);

            foreach (var issue in validationResult.Issues)
            {
                issue.FuelEventId = fuelEvent.FuelEventId;
                db.ValidationIssues.Add(issue);
            }
        }

        if (!options.DryRun)
        {
            await db.SaveChangesAsync(stoppingToken);
        }

        batch.CompletedAtUtc = DateTime.UtcNow;
        batch.Status = "Completed";
        await db.SaveChangesAsync(stoppingToken);
    }

    private static IEnumerable<string> DiscoverFiles(ImportOptions options)
    {
        var mode = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory
            .EnumerateFiles(options.RootFolder, "*.*", mode)
            .Where(f => options.AllowedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
    }

    private static decimal? TryDecimal(Dictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var raw)) return null;
        return decimal.TryParse(raw, out var value) ? value : null;
    }

    private static int? TryInt(Dictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var raw)) return null;
        return int.TryParse(raw, out var value) ? value : null;
    }
}
