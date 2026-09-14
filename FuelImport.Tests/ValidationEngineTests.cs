using FuelImport.Core.Models;
using FuelImport.Core.Options;
using FuelImport.Core.Services;

namespace FuelImport.Tests;

public class ValidationEngineTests
{
    [Fact]
    public void Validate_FlagsDecreasingOdometerAsError()
    {
        var engine = new ValidationEngine(new ValidationOptions());
        var vehicle = new Vehicle { VehicleId = 1, ExpectedTankGallonsMin = 5, ExpectedTankGallonsMax = 20 };

        var previous = new FuelEvent { VehicleId = 1, Odometer = 150000 };
        var current = new FuelEvent { VehicleId = 1, Odometer = 149000, Gallons = 10 };

        var result = engine.Validate(current, vehicle, previous);

        Assert.Contains(result.Issues, i => i.IssueType == "OdometerDecreased" && i.Severity == "error");
    }
}
