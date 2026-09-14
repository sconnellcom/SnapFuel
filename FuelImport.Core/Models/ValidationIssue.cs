namespace FuelImport.Core.Models;

public class ValidationIssue
{
    public int ValidationIssueId { get; set; }
    public int FuelEventId { get; set; }
    public string IssueType { get; set; } = string.Empty;
    public string Severity { get; set; } = "warning";
    public string Message { get; set; } = string.Empty;
    public string? SuggestedValue { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
}
