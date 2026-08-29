using System.Text.Json;
using FuelImport.Core.Interfaces;
using FuelImport.Core.Models;

namespace FuelImport.Aws.Services;

public class TextractPumpOcrService : IPumpOcrService
{
    public Task<OcrExtractionResult> ExtractAsync(SourceImage image, CancellationToken cancellationToken = default)
    {
        var result = new OcrExtractionResult
        {
            Provider = "AmazonTextract",
            Operation = "AnalyzeExpense",
            RawJson = JsonSerializer.Serialize(new { note = "Phase 1 placeholder integration" }),
            Confidence = 0.55m
        };

        return Task.FromResult(result);
    }
}

public class RekognitionDashboardOcrService : IDashboardOcrService
{
    public Task<OcrExtractionResult> ExtractAsync(SourceImage image, CancellationToken cancellationToken = default)
    {
        var result = new OcrExtractionResult
        {
            Provider = "AmazonRekognition",
            Operation = "DetectText",
            RawJson = JsonSerializer.Serialize(new { note = "Phase 1 placeholder integration" }),
            Confidence = 0.55m
        };

        return Task.FromResult(result);
    }
}

public class A2IReviewRouter
{
    public Task<string> RouteAsync(FuelEvent fuelEvent, IReadOnlyCollection<ValidationIssue> issues, CancellationToken cancellationToken = default)
    {
        var reason = string.Join(";", issues.Select(i => i.IssueType));
        return Task.FromResult(string.IsNullOrWhiteSpace(reason) ? "LowConfidence" : reason);
    }
}
