namespace FuelImport.Core.Models;

public class ImportScanResult
{
    public string RootFolder { get; set; } = string.Empty;
    public bool DryRun { get; set; }
    public int DiscoveredCount { get; set; }
    public int NewImagesCount { get; set; }
    public int ExistingImagesCount { get; set; }
    public string? ErrorMessage { get; set; }
}
