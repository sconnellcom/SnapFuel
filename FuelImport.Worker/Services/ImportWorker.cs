using FuelImport.Core.Interfaces;
using FuelImport.Core.Models;
using FuelImport.Data.Persistence;
using FuelImport.Worker.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelImport.Worker.Services;

public class ImportWorker(
    IServiceProvider serviceProvider,
    IOptions<ImportOptions> importOptions,
    IHostApplicationLifetime applicationLifetime,
    ILogger<ImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
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

            var batch = new ImportBatch { RootFolder = options.RootFolder, IsDryRun = options.DryRun };
            db.ImportBatches.Add(batch);
            await db.SaveChangesAsync(stoppingToken);

            var discovered = DiscoverFiles(options).ToList();
            logger.LogInformation("Discovered {Count} candidate files", discovered.Count);

            var newImages = new List<SourceImage>();
            foreach (var path in discovered)
            {
                var metadata = await metadataExtractor.ExtractAsync(path, stoppingToken);
                var existingImage = await db.SourceImages.FirstOrDefaultAsync(x => x.FileHash == metadata.FileHash, stoppingToken);
                if (existingImage is not null)
                {
                    ApplyMetadata(existingImage, metadata);

                    var updatedClassification = await classifier.ClassifyAsync(existingImage, stoppingToken);
                    existingImage.ImageTypeCandidate = updatedClassification.ImageType;
                    existingImage.ImageTypeConfidence = updatedClassification.Confidence;
                    existingImage.RawClassificationJson = updatedClassification.RawJson;
                    existingImage.ProcessingStatus = ProcessingStatus.Completed;
                    logger.LogInformation("Refreshed metadata for duplicate file {Path}", path);
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
                image.ProcessingStatus = ProcessingStatus.Completed;
                newImages.Add(image);
            }

            if (!options.DryRun)
            {
                db.SourceImages.AddRange(newImages);
                await db.SaveChangesAsync(stoppingToken);
            }

            batch.CompletedAtUtc = DateTime.UtcNow;
            batch.Status = "Completed";
            await db.SaveChangesAsync(stoppingToken);
        }
        finally
        {
            applicationLifetime.StopApplication();
        }
    }

    private static IEnumerable<string> DiscoverFiles(ImportOptions options)
    {
        var mode = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory
            .EnumerateFiles(options.RootFolder, "*.*", mode)
            .Where(f => options.AllowedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
    }

    private static void ApplyMetadata(SourceImage image, MetadataSnapshot metadata)
    {
        image.FilePath = metadata.FilePath;
        image.FileName = metadata.FileName;
        image.FileHash = metadata.FileHash;
        image.CapturedAtLocal = metadata.CapturedAtLocal;
        image.CapturedAtUtc = metadata.CapturedAtUtc ?? metadata.FileModifiedUtc;
        image.Latitude = metadata.Latitude;
        image.Longitude = metadata.Longitude;
        image.Width = metadata.Width;
        image.Height = metadata.Height;
        image.RawMetadataJson = metadata.RawMetadataJson;
        image.ProcessingStatus = ProcessingStatus.MetadataExtracted;
    }
}
