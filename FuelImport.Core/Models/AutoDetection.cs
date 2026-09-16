namespace FuelImport.Core.Models;

/// <summary>Plausible value windows handed to the vision model and used to sanity-check its answers.</summary>
public class DetectionEstimates
{
    public int OdometerMinimum { get; set; }
    public int OdometerMaximum { get; set; }
    public int? OdometerExpected { get; set; }
    public string OdometerBasis { get; set; } = string.Empty;
    public decimal GallonsMinimum { get; set; }
    public decimal GallonsMaximum { get; set; }
    public decimal CostMinimum { get; set; }
    public decimal CostMaximum { get; set; }
    public decimal MinPricePerGallon { get; set; }
    public decimal MaxPricePerGallon { get; set; }
}

public class VehicleHint
{
    public int VehicleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PhotoDescription { get; set; }
}

public class ImageDetection
{
    public int SourceImageId { get; set; }
    public ImageType ImageType { get; set; } = ImageType.Unknown;
    public decimal? Gallons { get; set; }
    public decimal? TotalCost { get; set; }
    public int? Odometer { get; set; }
    public string? VehicleName { get; set; }
    public decimal Confidence { get; set; }
    public string? Notes { get; set; }
    public string RawJson { get; set; } = "{}";
    public string? ErrorMessage { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public class GroupDetectionResult
{
    public string GroupKey { get; set; } = string.Empty;
    public int? FuelEventId { get; set; }
    public bool Succeeded { get; set; }
    public string? Message { get; set; }
    public decimal? Gallons { get; set; }
    public decimal? TotalPrice { get; set; }
    public int? Odometer { get; set; }
    public int? VehicleId { get; set; }
    public bool WasSplit { get; set; }
    public bool LeftExistingValuesAlone { get; set; }
    public decimal Confidence { get; set; }
    public List<ImageDetection> Images { get; set; } = [];
}

public class AutoDetectRunResult
{
    public int GroupsExamined { get; set; }
    public int GroupsDetected { get; set; }
    public int GroupsFailed { get; set; }
    public int GroupsSplit { get; set; }
    public string? ErrorMessage { get; set; }
    public List<GroupDetectionResult> Groups { get; set; } = [];
}
