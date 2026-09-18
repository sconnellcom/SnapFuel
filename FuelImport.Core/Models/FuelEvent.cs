namespace FuelImport.Core.Models;

public class FuelEvent
{
    public int FuelEventId { get; set; }
    public int? VehicleId { get; set; }
    public int? PumpSourceImageId { get; set; }
    public int? DashSourceImageId { get; set; }
    public DateTime? EventTimeUtc { get; set; }
    public DateTime? EventTimeLocal { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? LocationName { get; set; }
    public decimal? Gallons { get; set; }
    public decimal? TotalPrice { get; set; }
    public decimal? PricePerGallon { get; set; }
    public int? Odometer { get; set; }
    public string? Notes { get; set; }
    public decimal? MilesSincePrevious { get; set; }
    public decimal? EstimatedMpg { get; set; }
    public bool IsEstimated { get; set; }
    public decimal OverallConfidence { get; set; }
    public bool NeedsReview { get; set; }
    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.Pending;
    public string? ReviewReason { get; set; }
    /// <summary>True once a human has confirmed the callout(s) on this event are not a data problem.</summary>
    public bool AnomalyAcknowledged { get; set; }
    public EntrySource EntrySource { get; set; } = EntrySource.Manual;
    public string? DetectionDetailsJson { get; set; }
    public DateTime? DetectedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
