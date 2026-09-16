using FuelImport.Core.Models;
using FuelImport.Core.Options;
using FuelImport.Core.Services;

namespace FuelImport.Tests;

public class DetectionEstimatorTests
{
    private static readonly AutoDetectOptions Options = new();

    [Fact]
    public void Create_UsesConfiguredFallbackRange_WhenVehicleHasNoHistory()
    {
        var estimator = new DetectionEstimator(Options);

        var estimates = estimator.Create(new Vehicle { Name = "Truck" }, new DateTime(2026, 5, 1), []);

        Assert.Equal(150_000, estimates.OdometerMinimum);
        Assert.Equal(200_000, estimates.OdometerMaximum);
        Assert.Equal(175_000, estimates.OdometerExpected);
    }

    [Fact]
    public void Create_ProjectsOdometerForwardFromMostRecentEvent()
    {
        var estimator = new DetectionEstimator(Options);
        var history = new[]
        {
            new FuelEvent { EventTimeUtc = new DateTime(2026, 4, 1), Odometer = 160_000 }
        };

        var estimates = estimator.Create(null, new DateTime(2026, 4, 11), history);

        Assert.Equal(160_000, estimates.OdometerMinimum);
        Assert.Equal(160_350, estimates.OdometerExpected);
        Assert.Equal(164_000, estimates.OdometerMaximum);
    }

    [Fact]
    public void Create_InterpolatesBetweenSurroundingEvents()
    {
        var estimator = new DetectionEstimator(Options);
        var history = new[]
        {
            new FuelEvent { EventTimeUtc = new DateTime(2026, 4, 1), Odometer = 160_000 },
            new FuelEvent { EventTimeUtc = new DateTime(2026, 4, 21), Odometer = 161_000 }
        };

        var estimates = estimator.Create(null, new DateTime(2026, 4, 11), history);

        Assert.Equal(160_000, estimates.OdometerMinimum);
        Assert.Equal(161_000, estimates.OdometerMaximum);
        Assert.Equal(160_500, estimates.OdometerExpected);
    }

    [Fact]
    public void Create_CapsGallonsAtVehicleAndGlobalLimits()
    {
        var estimator = new DetectionEstimator(Options);

        var limitedByVehicle = estimator.Create(new Vehicle { MaxGallonsPerFillUp = 18m }, DateTime.UtcNow, []);
        var limitedByGlobal = estimator.Create(new Vehicle { MaxGallonsPerFillUp = 90m }, DateTime.UtcNow, []);

        Assert.Equal(18m, limitedByVehicle.GallonsMaximum);
        Assert.Equal(40m, limitedByGlobal.GallonsMaximum);
        Assert.Equal(320m, limitedByGlobal.CostMaximum);
    }

    [Fact]
    public void Validate_FlagsValuesOutsideExpectedWindows()
    {
        var estimator = new DetectionEstimator(Options);
        var estimates = estimator.Create(new Vehicle { MaxGallonsPerFillUp = 20m }, new DateTime(2026, 5, 1), []);

        var detection = new ImageDetection
        {
            Gallons = 55m,
            TotalCost = 12m,
            Odometer = 10_000
        };

        var warnings = estimator.Validate(detection, estimates);

        Assert.Equal(3, warnings.Count);
    }

    [Fact]
    public void Validate_AcceptsPlausibleReadings()
    {
        var estimator = new DetectionEstimator(Options);
        var estimates = estimator.Create(new Vehicle { MaxGallonsPerFillUp = 20m }, new DateTime(2026, 5, 1), []);

        var detection = new ImageDetection
        {
            Gallons = 12m,
            TotalCost = 42m,
            Odometer = 176_400
        };

        Assert.Empty(estimator.Validate(detection, estimates));
    }

    [Fact]
    public void CreateForUnknownVehicle_UsesRecordedReadingsRatherThanTheDefaultRange()
    {
        var estimator = new DetectionEstimator(Options);
        var vehicles = new[]
        {
            new Vehicle { VehicleId = 1, Name = "Truck" },
            new Vehicle { VehicleId = 2, Name = "Car" }
        };

        var history = new Dictionary<int, List<FuelEvent>>
        {
            [1] =
            [
                new FuelEvent { EventTimeUtc = new DateTime(2026, 4, 1), Odometer = 268_000 },
                new FuelEvent { EventTimeUtc = new DateTime(2026, 4, 21), Odometer = 269_000 }
            ],
            [2] =
            [
                new FuelEvent { EventTimeUtc = new DateTime(2026, 4, 2), Odometer = 90_000 },
                new FuelEvent { EventTimeUtc = new DateTime(2026, 4, 20), Odometer = 91_000 }
            ]
        };

        var estimates = estimator.CreateForUnknownVehicle(new DateTime(2026, 4, 11), vehicles, history);

        Assert.Equal(90_000, estimates.OdometerMinimum);
        Assert.Equal(269_000, estimates.OdometerMaximum);
    }

    [Fact]
    public void CreateForUnknownVehicle_IgnoresVehiclesWithNoHistory()
    {
        var estimator = new DetectionEstimator(Options);
        var vehicles = new[]
        {
            new Vehicle { VehicleId = 1, Name = "Truck" },
            new Vehicle { VehicleId = 2, Name = "Never driven" }
        };

        var history = new Dictionary<int, List<FuelEvent>>
        {
            [1] = [new FuelEvent { EventTimeUtc = new DateTime(2026, 4, 1), Odometer = 268_000 }]
        };

        var estimates = estimator.CreateForUnknownVehicle(new DateTime(2026, 4, 11), vehicles, history);

        Assert.Equal(268_000, estimates.OdometerMinimum);
        Assert.True(estimates.OdometerMaximum < 280_000);
    }

    [Fact]
    public void CreateForUnknownVehicle_FallsBackWhenNothingIsRecorded()
    {
        var estimator = new DetectionEstimator(Options);
        var vehicles = new[] { new Vehicle { VehicleId = 1, Name = "Truck" } };

        var estimates = estimator.CreateForUnknownVehicle(DateTime.UtcNow, vehicles, new Dictionary<int, List<FuelEvent>>());

        Assert.Equal(150_000, estimates.OdometerMinimum);
        Assert.Equal(200_000, estimates.OdometerMaximum);
    }
}
