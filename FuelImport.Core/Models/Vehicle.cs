namespace FuelImport.Core.Models;

public class Vehicle
{
    public int VehicleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DashboardLabel { get; set; } = string.Empty;
    public decimal ExpectedTankGallonsMin { get; set; }
    public decimal ExpectedTankGallonsMax { get; set; }
    public int? OdometerMinKnown { get; set; }
    public int? OdometerMaxKnown { get; set; }
    public bool Active { get; set; } = true;
}
