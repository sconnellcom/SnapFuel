namespace FuelImport.Core.Options;

public class AutoDetectOptions
{
    /// <summary>Hard ceiling on gallons for any vehicle.</summary>
    public decimal MaxGallons { get; set; } = 40m;

    public decimal MinPricePerGallon { get; set; } = 2m;

    public decimal MaxPricePerGallon { get; set; } = 8m;

    /// <summary>Typical driving rate used to project an expected odometer reading forward from the last known value.</summary>
    public decimal TypicalMilesPerDay { get; set; } = 35m;

    /// <summary>Upper bound on driving rate used to size the plausible odometer window.</summary>
    public decimal MaxMilesPerDay { get; set; } = 400m;

    /// <summary>Minimum width of the odometer window so a same-day photo still has room to move.</summary>
    public int MinimumOdometerWindow { get; set; } = 500;

    /// <summary>Odometer window used when the vehicle has no recorded history.</summary>
    public int FallbackOdometerMinimum { get; set; } = 150_000;

    public int FallbackOdometerMaximum { get; set; } = 200_000;

    /// <summary>Maximum number of image groups processed by a single auto-detect run.</summary>
    public int MaxGroupsPerRun { get; set; } = 25;

    /// <summary>Maximum image requests sent to the vision model concurrently within one group.</summary>
    public int MaxConcurrentImages { get; set; } = 3;

    /// <summary>Guards against accidentally sending every photo to the model; leave off until per-group testing looks right.</summary>
    public bool AllowBulkRuns { get; set; }
}
