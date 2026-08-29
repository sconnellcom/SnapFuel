namespace FuelImport.Core.Models;

public class MetadataSnapshot
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public DateTime? CapturedAtLocal { get; set; }
    public DateTime? CapturedAtUtc { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public DateTime FileCreatedUtc { get; set; }
    public DateTime FileModifiedUtc { get; set; }
    public string RawMetadataJson { get; set; } = "{}";
}
