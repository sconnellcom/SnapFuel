namespace FuelImport.Core.Models;

public class SourceImage
{
    public int SourceImageId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public DateTime? CapturedAtUtc { get; set; }
    public DateTime? CapturedAtLocal { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public ImageType ImageTypeCandidate { get; set; } = ImageType.Unknown;
    public decimal ImageTypeConfidence { get; set; }
    public ProcessingStatus ProcessingStatus { get; set; } = ProcessingStatus.Discovered;
    public string RawMetadataJson { get; set; } = "{}";
    public string RawClassificationJson { get; set; } = "{}";
}
