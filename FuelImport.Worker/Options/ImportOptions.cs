namespace FuelImport.Worker.Options;

public class ImportOptions
{
    public string RootFolder { get; set; } = string.Empty;
    public bool DryRun { get; set; }
    public bool Recursive { get; set; } = true;
    public string[] AllowedExtensions { get; set; } = [".jpg", ".jpeg", ".png", ".heic"];
}
