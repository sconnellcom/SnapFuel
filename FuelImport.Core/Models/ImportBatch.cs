namespace FuelImport.Core.Models;

public class ImportBatch
{
    public int ImportBatchId { get; set; }
    public string RootFolder { get; set; } = string.Empty;
    public bool IsDryRun { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public string Status { get; set; } = "Running";
}
