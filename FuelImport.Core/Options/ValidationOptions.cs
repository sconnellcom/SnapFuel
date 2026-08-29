namespace FuelImport.Core.Options;

public class ValidationOptions
{
    public int MaxMinutesBetweenPumpAndDash { get; set; } = 10;
    public decimal AllowedPriceMismatchAbsolute { get; set; } = 0.35m;
    public decimal DefaultMinMilesBetweenEvents { get; set; } = 10;
    public decimal DefaultMaxMilesBetweenEvents { get; set; } = 800;
}
