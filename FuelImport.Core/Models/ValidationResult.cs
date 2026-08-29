namespace FuelImport.Core.Models;

public class ValidationResult
{
    public List<ValidationIssue> Issues { get; } = [];
    public bool HasErrors => Issues.Any(i => i.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
}
