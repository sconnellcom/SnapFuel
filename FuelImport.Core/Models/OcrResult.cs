namespace FuelImport.Core.Models;

public class OcrResult
{
    public int OcrResultId { get; set; }
    public int SourceImageId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderOperation { get; set; } = string.Empty;
    public string RawResponseJson { get; set; } = "{}";
    public string ParsedText { get; set; } = string.Empty;
    public string ParsedFieldsJson { get; set; } = "{}";
    public decimal ConfidenceScore { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
