namespace FuelImport.Core.Models;

public class Vehicle
{
    public int VehicleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool NoOdometer { get; set; }
    public string? Description { get; set; }

    /// <summary>Free-text hint describing how this vehicle's dashboard looks, passed to the vision model.</summary>
    public string? PhotoDescription { get; set; }

    public string DashboardLabel { get; set; } = string.Empty;
    public decimal ExpectedTankGallonsMin { get; set; }
    public decimal ExpectedTankGallonsMax { get; set; }
    public decimal? MaxGallonsPerFillUp { get; set; }
    public decimal? MaxMpg { get; set; }
    public int? OdometerMinKnown { get; set; }
    public int? OdometerMaxKnown { get; set; }
    public bool Active { get; set; } = true;
}
