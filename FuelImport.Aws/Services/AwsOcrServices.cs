using System.Text.Json;
using Amazon;
using Amazon.AugmentedAIRuntime;
using Amazon.AugmentedAIRuntime.Model;
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon.Textract;
using Amazon.Textract.Model;
using FuelImport.Aws.Options;
using FuelImport.Core.Interfaces;
using FuelImport.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuelImport.Aws.Services;

public class TextractPumpOcrService : IPumpOcrService
{
    private readonly AwsVisionOptions _options;
    private readonly ILogger<TextractPumpOcrService> _logger;
    private readonly IAmazonTextract? _textract;

    public TextractPumpOcrService(IOptions<AwsVisionOptions> options, ILogger<TextractPumpOcrService> logger)
    {
        _options = options.Value;
        _logger = logger;
        if (_options.EnableAwsApis)
        {
            _textract = new AmazonTextractClient(RegionEndpoint.GetBySystemName(_options.Region));
        }
    }

    public async Task<OcrExtractionResult> ExtractAsync(SourceImage image, CancellationToken cancellationToken = default)
    {
        if (!_options.EnableAwsApis || _textract is null || !File.Exists(image.FilePath))
        {
            return PlaceholderResult();
        }

        try
        {
            await using var file = File.OpenRead(image.FilePath);
            using var memory = new MemoryStream();
            await file.CopyToAsync(memory, cancellationToken);
            memory.Position = 0;

            var response = await _textract.AnalyzeExpenseAsync(new AnalyzeExpenseRequest
            {
                Document = new Amazon.Textract.Model.Document { Bytes = memory }
            }, cancellationToken);

            var summaryFields = response.ExpenseDocuments
                .SelectMany(d => d.SummaryFields)
                .Select(f => (Type: f.Type?.Text ?? string.Empty, Value: f.ValueDetection?.Text ?? string.Empty))
                .ToList();

            var textLines = summaryFields.Select(x => $"{x.Type} {x.Value}").ToList();
            var parsed = OcrParsingHelpers.BuildPumpFieldCandidates(summaryFields, textLines);
            var confidenceValues = response.ExpenseDocuments
                .SelectMany(d => d.SummaryFields)
                .Select(f => f.ValueDetection?.Confidence ?? f.Type?.Confidence ?? 0f)
                .ToList();
            var confidence = confidenceValues.Count == 0 ? 0.45m : (decimal)confidenceValues.Average() / 100m;

            return new OcrExtractionResult
            {
                Provider = "AmazonTextract",
                Operation = "AnalyzeExpense",
                RawJson = JsonSerializer.Serialize(response),
                ParsedText = string.Join('\n', textLines),
                ParsedFields = parsed,
                Confidence = Math.Clamp(confidence, 0m, 1m)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Textract AnalyzeExpense failed for {Path}", image.FilePath);
            return PlaceholderResult();
        }
    }

    private static OcrExtractionResult PlaceholderResult() => new()
    {
        Provider = "AmazonTextract",
        Operation = "AnalyzeExpense",
        RawJson = JsonSerializer.Serialize(new { note = "Fallback result" }),
        ParsedText = string.Empty,
        Confidence = 0.40m
    };
}

public class RekognitionDashboardOcrService : IDashboardOcrService
{
    private readonly AwsVisionOptions _options;
    private readonly ILogger<RekognitionDashboardOcrService> _logger;
    private readonly IAmazonRekognition? _rekognition;

    public RekognitionDashboardOcrService(IOptions<AwsVisionOptions> options, ILogger<RekognitionDashboardOcrService> logger)
    {
        _options = options.Value;
        _logger = logger;
        if (_options.EnableAwsApis)
        {
            _rekognition = new AmazonRekognitionClient(RegionEndpoint.GetBySystemName(_options.Region));
        }
    }

    public async Task<OcrExtractionResult> ExtractAsync(SourceImage image, CancellationToken cancellationToken = default)
    {
        if (!_options.EnableAwsApis || _rekognition is null || !File.Exists(image.FilePath))
        {
            return PlaceholderResult();
        }

        try
        {
            await using var file = File.OpenRead(image.FilePath);
            using var memory = new MemoryStream();
            await file.CopyToAsync(memory, cancellationToken);
            memory.Position = 0;

            var response = await _rekognition.DetectTextAsync(new DetectTextRequest
            {
                Image = new Amazon.Rekognition.Model.Image { Bytes = memory }
            }, cancellationToken);

            var lines = response.TextDetections
                .Where(t => !string.IsNullOrWhiteSpace(t.DetectedText) && t.Type == TextTypes.LINE)
                .OrderByDescending(t => t.Confidence)
                .Select(t => t.DetectedText!)
                .ToList();

            var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var odometer = OcrParsingHelpers.ExtractOdometerFromText(lines);
            if (odometer.HasValue)
            {
                parsed["odometer"] = odometer.Value.ToString();
            }

            var confidence = response.TextDetections.Count == 0
                ? 0.45m
                : (decimal)response.TextDetections.Average(t => t.Confidence ?? 0f) / 100m;

            return new OcrExtractionResult
            {
                Provider = "AmazonRekognition",
                Operation = "DetectText",
                RawJson = JsonSerializer.Serialize(response),
                ParsedText = string.Join('\n', lines),
                ParsedFields = parsed,
                Confidence = Math.Clamp(confidence, 0m, 1m)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rekognition DetectText failed for {Path}", image.FilePath);
            return PlaceholderResult();
        }
    }

    private static OcrExtractionResult PlaceholderResult() => new()
    {
        Provider = "AmazonRekognition",
        Operation = "DetectText",
        RawJson = JsonSerializer.Serialize(new { note = "Fallback result" }),
        ParsedText = string.Empty,
        Confidence = 0.40m
    };
}

public class RekognitionImageClassifier : IImageClassifier
{
    private readonly AwsVisionOptions _options;
    private readonly ILogger<RekognitionImageClassifier> _logger;
    private readonly IAmazonRekognition? _rekognition;

    public RekognitionImageClassifier(IOptions<AwsVisionOptions> options, ILogger<RekognitionImageClassifier> logger)
    {
        _options = options.Value;
        _logger = logger;
        if (_options.EnableAwsApis)
        {
            _rekognition = new AmazonRekognitionClient(RegionEndpoint.GetBySystemName(_options.Region));
        }
    }

    public async Task<ClassificationResult> ClassifyAsync(SourceImage image, CancellationToken cancellationToken = default)
    {
        if (!_options.EnableAwsApis || _rekognition is null || !File.Exists(image.FilePath))
        {
            return FallbackFromFileName(image.FileName);
        }

        try
        {
            await using var file = File.OpenRead(image.FilePath);
            using var memory = new MemoryStream();
            await file.CopyToAsync(memory, cancellationToken);
            memory.Position = 0;

            var labelResponse = await _rekognition.DetectLabelsAsync(new DetectLabelsRequest
            {
                Image = new Amazon.Rekognition.Model.Image { Bytes = memory },
                MaxLabels = 25,
                MinConfidence = 55f
            }, cancellationToken);

            memory.Position = 0;
            var textResponse = await _rekognition.DetectTextAsync(new DetectTextRequest
            {
                Image = new Amazon.Rekognition.Model.Image { Bytes = memory }
            }, cancellationToken);

            var labels = labelResponse.Labels.Select(l => l.Name ?? string.Empty).ToList();
            var textLines = textResponse.TextDetections
                .Where(t => t.Type == TextTypes.LINE && !string.IsNullOrWhiteSpace(t.DetectedText))
                .Select(t => t.DetectedText!)
                .ToList();

            var (pumpScore, dashScore) = OcrParsingHelpers.ScoreImageTypeHints(labels, textLines);
            var fallback = FallbackFromFileName(image.FileName);
            pumpScore = Math.Clamp(pumpScore + (fallback.ImageType == ImageType.Pump ? 0.10m : 0m), 0m, 1m);
            dashScore = Math.Clamp(dashScore + (fallback.ImageType == ImageType.Dashboard ? 0.10m : 0m), 0m, 1m);

            var selected = ImageType.Unknown;
            var confidence = 0.45m;
            if (pumpScore >= 0.55m || dashScore >= 0.55m)
            {
                selected = pumpScore >= dashScore ? ImageType.Pump : ImageType.Dashboard;
                confidence = Math.Max(pumpScore, dashScore);
            }

            return new ClassificationResult
            {
                ImageType = selected,
                Confidence = confidence,
                RawJson = JsonSerializer.Serialize(new
                {
                    labels,
                    textLines,
                    pumpScore,
                    dashScore,
                    selected,
                    confidence
                })
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rekognition classification failed for {Path}", image.FilePath);
            return FallbackFromFileName(image.FileName);
        }
    }

    private static ClassificationResult FallbackFromFileName(string fileName)
    {
        var file = fileName.ToLowerInvariant();
        var result = new ClassificationResult { ImageType = ImageType.Unknown, Confidence = 0.40m };

        if (file.Contains("pump") || file.Contains("fuel") || file.Contains("receipt"))
        {
            result.ImageType = ImageType.Pump;
            result.Confidence = 0.70m;
        }
        else if (file.Contains("dash") || file.Contains("odo") || file.Contains("odometer"))
        {
            result.ImageType = ImageType.Dashboard;
            result.Confidence = 0.70m;
        }

        result.RawJson = JsonSerializer.Serialize(new { strategy = "filename-fallback", result.ImageType, result.Confidence });
        return result;
    }
}

public class A2IReviewRouter
{
    private readonly AwsVisionOptions _options;
    private readonly ILogger<A2IReviewRouter> _logger;
    private readonly IAmazonAugmentedAIRuntime? _a2i;

    public A2IReviewRouter(IOptions<AwsVisionOptions> options, ILogger<A2IReviewRouter> logger)
    {
        _options = options.Value;
        _logger = logger;
        if (_options.EnableAwsApis && !string.IsNullOrWhiteSpace(_options.A2iFlowDefinitionArn))
        {
            _a2i = new AmazonAugmentedAIRuntimeClient(RegionEndpoint.GetBySystemName(_options.Region));
        }
    }

    public async Task<string> RouteAsync(FuelEvent fuelEvent, IReadOnlyCollection<ValidationIssue> issues, CancellationToken cancellationToken = default)
    {
        var reason = string.Join(';', issues.Select(i => i.IssueType));
        if (_a2i is null || string.IsNullOrWhiteSpace(_options.A2iFlowDefinitionArn))
        {
            return string.IsNullOrWhiteSpace(reason) ? "LowConfidence" : reason;
        }

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                fuelEvent.FuelEventId,
                fuelEvent.PumpSourceImageId,
                fuelEvent.DashSourceImageId,
                fuelEvent.EventTimeUtc,
                fuelEvent.Gallons,
                fuelEvent.TotalPrice,
                fuelEvent.PricePerGallon,
                fuelEvent.Odometer,
                issues
            });

            var loopName = $"snapfuel-{fuelEvent.FuelEventId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            await _a2i.StartHumanLoopAsync(new StartHumanLoopRequest
            {
                HumanLoopName = loopName,
                FlowDefinitionArn = _options.A2iFlowDefinitionArn,
                HumanLoopInput = new HumanLoopInput { InputContent = payload }
            }, cancellationToken);

            return string.IsNullOrWhiteSpace(reason) ? "A2IQueued" : $"A2IQueued:{reason}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start A2I review workflow for FuelEvent {FuelEventId}", fuelEvent.FuelEventId);
            return string.IsNullOrWhiteSpace(reason) ? "LowConfidence" : reason;
        }
    }
}
