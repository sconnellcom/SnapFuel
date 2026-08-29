namespace FuelImport.Core.Models;

public class HumanReview
{
    public int HumanReviewId { get; set; }
    public int FuelEventId { get; set; }
    public string ReviewSystem { get; set; } = "AmazonA2I";
    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.Pending;
    public string? ReviewerName { get; set; }
    public DateTime? ReviewerAtUtc { get; set; }
    public string OriginalValuesJson { get; set; } = "{}";
    public string CorrectedValuesJson { get; set; } = "{}";
    public string? Notes { get; set; }
}
