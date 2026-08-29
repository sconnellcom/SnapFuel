namespace FuelImport.Core.Models;

public class OcrExtractionResult
{
    public string Provider { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string RawJson { get; set; } = "{}";
    public string ParsedText { get; set; } = string.Empty;
    public Dictionary<string, string> ParsedFields { get; set; } = [];
    public decimal Confidence { get; set; }
}
