using System.Text.Json;
using FuelImport.Core.Interfaces;
using FuelImport.Core.Models;

namespace FuelImport.Worker.Services;

public class SimpleImageClassifier : IImageClassifier
{
    public Task<ClassificationResult> ClassifyAsync(SourceImage image, CancellationToken cancellationToken = default)
    {
        var file = image.FileName.ToLowerInvariant();
        var result = new ClassificationResult { ImageType = ImageType.Unknown, Confidence = 0.40m };

        if (file.Contains("dash") || file.Contains("odo") || file.Contains("odometer"))
        {
            result.ImageType = ImageType.Dashboard;
            result.Confidence = 0.78m;
        }
        else if (file.Contains("pump") || file.Contains("dispenser"))
        {
            result.ImageType = ImageType.Pump;
            result.Confidence = 0.76m;
        }
        else if (file.Contains("fuel") || file.Contains("gas") || file.Contains("receipt"))
        {
            result.ImageType = ImageType.FuelReceipt;
            result.Confidence = 0.72m;
        }
        else if (file.Contains("store") || file.Contains("invoice") || file.Contains("other"))
        {
            result.ImageType = ImageType.OtherReceipt;
            result.Confidence = 0.68m;
        }

        result.RawJson = JsonSerializer.Serialize(new { strategy = "filename-heuristic", result.ImageType, result.Confidence });
        return Task.FromResult(result);
    }
}
