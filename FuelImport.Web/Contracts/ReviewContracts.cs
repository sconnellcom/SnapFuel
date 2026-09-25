using FuelImport.Core.Models;

namespace FuelImport.Web.Contracts;

public class ReviewUpdateRequest
{
    public decimal? Gallons { get; set; }
    public decimal? TotalPrice { get; set; }
    public decimal? PricePerGallon { get; set; }
    public int? Odometer { get; set; }
    public int? VehicleId { get; set; }
    public DateTimeOffset? EventTimeLocal { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? LocationName { get; set; }
    public string? Notes { get; set; }
    public string ReviewerName { get; set; } = string.Empty;
    public string ReviewSystem { get; set; } = "ManualApi";
    public bool? Approve { get; set; }
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

public class VehicleOptionResponse
{
    public int VehicleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool NoOdometer { get; set; }
    public decimal? MaxGallonsPerFillUp { get; set; }
    public decimal? MaxMpg { get; set; }
}

public class VehicleManagementResponse
{
    public int VehicleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; }
    public bool NoOdometer { get; set; }
    public decimal? MaxGallonsPerFillUp { get; set; }
    public decimal? MaxMpg { get; set; }
    public string? PhotoDescription { get; set; }
}

public class VehicleUpsertRequest
{
    public string Name { get; set; } = string.Empty;
    public bool? Active { get; set; }
    public bool? NoOdometer { get; set; }
    public decimal? MaxGallonsPerFillUp { get; set; }
    public decimal? MaxMpg { get; set; }
    public string? PhotoDescription { get; set; }
}

public class EventLogResponse
{
    public int FuelEventId { get; set; }
    public int? VehicleId { get; set; }
    public string VehicleName { get; set; } = "Unassigned";
    public DateTime? EventTimeUtc { get; set; }
    public DateTime? EventTimeLocal { get; set; }
    public string? LocationName { get; set; }
    public decimal? Gallons { get; set; }
    public decimal? TotalPrice { get; set; }
    public decimal? PricePerGallon { get; set; }
    public int? Odometer { get; set; }
    public decimal? MilesSincePrevious { get; set; }
    public decimal? CalculatedMpg { get; set; }
    public bool HasImages { get; set; }
    public bool NeedsReview { get; set; }
    public ReviewStatus ReviewStatus { get; set; }
    public List<string> AnomalyFlags { get; set; } = [];
    public bool AnomalyAcknowledged { get; set; }
}

public class AnomalyAcknowledgeRequest
{
    public bool Acknowledged { get; set; }
}

public class ReviewLiveValidationRequest
{
    public int? FuelEventId { get; set; }
    public int? VehicleId { get; set; }
    public int? Odometer { get; set; }
    public decimal? Gallons { get; set; }
    public DateTime? EventTimeUtc { get; set; }
}

public class ReviewLiveValidationResponse
{
    public List<string> Callouts { get; set; } = [];
}

public class VehicleReportResponse
{
    public int VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public decimal? MaxGallonsPerFillUp { get; set; }
    public decimal? MaxMpg { get; set; }
    public int TotalEvents { get; set; }
    public int EventsWithAnomalies { get; set; }
    public decimal? AverageGallons { get; set; }
    public decimal? AverageMpg { get; set; }
    public decimal? TotalGallons { get; set; }
    public decimal? TotalSpend { get; set; }
}

public class DataReportResponse
{
    public int TotalEvents { get; set; }
    public int EventsWithAnomalies { get; set; }
    public List<VehicleReportResponse> Vehicles { get; set; } = [];
}

public class ImportScanRequest
{
    public string? RootFolder { get; set; }
    public bool? DryRun { get; set; }
    public bool? Recursive { get; set; }
}

public class ManualReviewImageResponse
{
    public int SourceImageId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ImageTypeCandidate { get; set; } = string.Empty;
    public decimal ImageTypeConfidence { get; set; }
    public DateTime? CapturedAtUtc { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? DistanceFromPreviousKilometers { get; set; }
    public string ImageUrl { get; set; } = string.Empty;

    // Per-image auto-detect OCR reading, when available.
    public decimal? DetectedGallons { get; set; }
    public decimal? DetectedLiters { get; set; }
    public decimal? DetectedTotalCost { get; set; }
    public int? DetectedOdometer { get; set; }
    public string? DetectedVehicleName { get; set; }
    public decimal? DetectedConfidence { get; set; }
    public string? DetectedNotes { get; set; }
    public List<string> DetectedWarnings { get; set; } = [];
    public string? DetectedErrorMessage { get; set; }
}

public class ManualReviewGroupResponse
{
    public string GroupKey { get; set; } = string.Empty;
    public int? FuelEventId { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? MaxDistanceKilometers { get; set; }
    public string? LocationName { get; set; }
    public int? VehicleId { get; set; }
    public int? Odometer { get; set; }
    public decimal? Gallons { get; set; }
    public decimal? Liters { get; set; }
    public decimal? TotalPrice { get; set; }
    public decimal? PricePerGallon { get; set; }
    public string? Notes { get; set; }
    public ReviewStatus? ReviewStatus { get; set; }
    public EntrySource? EntrySource { get; set; }
    public decimal? DetectionConfidence { get; set; }
    public string? ReviewReason { get; set; }
    public List<string> AnomalyFlags { get; set; } = [];
    public bool AnomalyAcknowledged { get; set; }
    public List<ManualReviewImageResponse> Images { get; set; } = [];
}

public class ManualReviewSaveRequest
{
    public int? FuelEventId { get; set; }
    public List<int> ImageIds { get; set; } = [];
    public int? VehicleId { get; set; }
    public int? Odometer { get; set; }
    public decimal? Gallons { get; set; }
    public decimal? Liters { get; set; }
    public decimal? TotalPrice { get; set; }
    public string? LocationName { get; set; }
    public string? Notes { get; set; }
    public string ReviewerName { get; set; } = "Manual UI";

    /// <summary>False for structural saves (e.g. before a split/merge) that must not count as a human review.</summary>
    public bool MarkReviewed { get; set; } = true;

    /// <summary>Explicitly changes the review state; omitted to preserve the current state.</summary>
    public bool? Reviewed { get; set; }
}

public class ManualReviewSplitRequest
{
    public string GroupKey { get; set; } = string.Empty;
    public List<int> ImageIds { get; set; } = [];
}

public class ManualReviewMergeRequest
{
    public string SourceGroupKey { get; set; } = string.Empty;
    public string TargetGroupKey { get; set; } = string.Empty;
}

public class AutoDetectRequest
{
    /// <summary>Restricts the run to a single review-queue group. When omitted every eligible group is processed.</summary>
    public string? GroupKey { get; set; }

    /// <summary>Re-runs detection over groups that already have an unreviewed fuel event.</summary>
    public bool? RedetectExisting { get; set; }

    public int? Limit { get; set; }

    /// <summary>Correlates this run with progress polling from the browser.</summary>
    public string? ProgressId { get; set; }
}
