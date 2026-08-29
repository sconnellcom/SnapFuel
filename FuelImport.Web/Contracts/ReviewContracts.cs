using FuelImport.Core.Models;

namespace FuelImport.Web.Contracts;

public class ReviewUpdateRequest
{
    public decimal? Gallons { get; set; }
    public decimal? TotalPrice { get; set; }
    public decimal? PricePerGallon { get; set; }
    public int? Odometer { get; set; }
    public int? VehicleId { get; set; }
    public DateTime? EventTimeLocal { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Notes { get; set; }
    public string ReviewerName { get; set; } = string.Empty;
    public bool Approve { get; set; } = true;
}

public class FuelEventSummaryResponse
{
    public int FuelEventId { get; set; }
    public int? VehicleId { get; set; }
    public decimal? Gallons { get; set; }
    public decimal? TotalPrice { get; set; }
    public decimal? PricePerGallon { get; set; }
    public int? Odometer { get; set; }
    public decimal OverallConfidence { get; set; }
    public bool NeedsReview { get; set; }
    public ReviewStatus ReviewStatus { get; set; }
    public string? ReviewReason { get; set; }
}
