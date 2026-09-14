namespace FuelImport.Core.Models;

public class ClassificationResult
{
    public ImageType ImageType { get; set; } = ImageType.Unknown;
    public decimal Confidence { get; set; }
    public string RawJson { get; set; } = "{}";
}
