using FuelImport.Core.Interfaces;
using FuelImport.Core.Models;
using FuelImport.Core.Options;
using FuelImport.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelImport.Web.Services;

public class ImageImportService(
    FuelImportDbContext db,
    IImageMetadataExtractor metadataExtractor,
    IImageClassifier classifier,
    IOptions<ImportOptions> importOptions,
    ILogger<ImageImportService> logger)
{
    public async Task<ImportScanResult> ScanAsync(ImportOptions? overrideOptions = null, CancellationToken cancellationToken = default)
    {
        var configured = importOptions.Value;
        var folder = overrideOptions?.RootFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = configured.RootFolder;
        }

        var isDryRun = overrideOptions?.DryRun ?? configured.DryRun;
        var isRecursive = overrideOptions?.Recursive ?? configured.Recursive;
        var allowedExtensions = (overrideOptions?.AllowedExtensions is { Length: > 0 }
            ? overrideOptions.AllowedExtensions
            : (configured.AllowedExtensions is { Length: > 0 } ? configured.AllowedExtensions : [".jpg", ".jpeg", ".png", ".heic"]))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            logger.LogWarning("Import skipped because RootFolder is missing or does not exist: {Folder}", folder);
            return new ImportScanResult
            {
                RootFolder = folder ?? string.Empty,
                DryRun = isDryRun,
                DiscoveredCount = 0,
                NewImagesCount = 0,
                ExistingImagesCount = 0,
                ErrorMessage = string.IsNullOrWhiteSpace(folder)
                    ? "RootFolder is not configured."
                    : $"Folder '{folder}' does not exist."
            };
        }

        var batch = new ImportBatch { RootFolder = folder, IsDryRun = isDryRun };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(cancellationToken);

        var searchOption = isRecursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var discovered = Directory
            .EnumerateFiles(folder, "*.*", searchOption)
            .Where(f => allowedExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        logger.LogInformation("Discovered {Count} candidate files in {Folder}", discovered.Count, folder);

        var newImages = new List<SourceImage>();
        var existingCount = 0;

        foreach (var path in discovered)
        {
            var metadata = await metadataExtractor.ExtractAsync(path, cancellationToken);
            var existingImage = await db.SourceImages.FirstOrDefaultAsync(x => x.FileHash == metadata.FileHash, cancellationToken);
            if (existingImage is not null)
            {
                ApplyMetadata(existingImage, metadata);

                var updatedClassification = await classifier.ClassifyAsync(existingImage, cancellationToken);
                existingImage.ImageTypeCandidate = updatedClassification.ImageType;
                existingImage.ImageTypeConfidence = updatedClassification.Confidence;
                existingImage.RawClassificationJson = updatedClassification.RawJson;
                existingImage.ProcessingStatus = ProcessingStatus.Completed;
                existingCount++;
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

            var classification = await classifier.ClassifyAsync(image, cancellationToken);
            image.ImageTypeCandidate = classification.ImageType;
            image.ImageTypeConfidence = classification.Confidence;
            image.RawClassificationJson = classification.RawJson;
            image.ProcessingStatus = ProcessingStatus.Completed;
            newImages.Add(image);
        }

        if (!isDryRun)
        {
            db.SourceImages.AddRange(newImages);
            await db.SaveChangesAsync(cancellationToken);
        }

        batch.CompletedAtUtc = DateTime.UtcNow;
        batch.Status = "Completed";
        await db.SaveChangesAsync(cancellationToken);

        return new ImportScanResult
        {
            RootFolder = folder,
            DryRun = isDryRun,
            DiscoveredCount = discovered.Count,
            NewImagesCount = newImages.Count,
            ExistingImagesCount = existingCount
        };
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
