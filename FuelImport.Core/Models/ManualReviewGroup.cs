namespace FuelImport.Core.Models;

public class ManualReviewGroup
{
    public string GroupKey { get; set; } = string.Empty;
    public int? FuelEventId { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? MaxDistanceKilometers { get; set; }
    public List<SourceImage> Images { get; set; } = [];
}