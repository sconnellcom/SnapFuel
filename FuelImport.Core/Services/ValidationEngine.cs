using FuelImport.Core.Interfaces;
using FuelImport.Core.Models;
using FuelImport.Core.Options;

namespace FuelImport.Core.Services;

public class ValidationEngine(ValidationOptions options) : IValidationEngine
{
    public ValidationResult Validate(FuelEvent fuelEvent, Vehicle? vehicle, FuelEvent? previousVehicleEvent)
    {
        var result = new ValidationResult();

        if (fuelEvent.Gallons is { } gallons && vehicle is not null)
        {
            if (gallons < vehicle.ExpectedTankGallonsMin || gallons > vehicle.ExpectedTankGallonsMax)
            {
                result.Issues.Add(new ValidationIssue { IssueType = "GallonsOutOfRange", Severity = "error", Message = "Gallons are outside expected tank range." });
            }
        }

        if (fuelEvent.TotalPrice is { } total && fuelEvent.Gallons is { } g && fuelEvent.PricePerGallon is { } ppg)
        {
            var expected = g * ppg;
            if (Math.Abs(total - expected) > options.AllowedPriceMismatchAbsolute)
            {
                result.Issues.Add(new ValidationIssue { IssueType = "PriceMathMismatch", Severity = "warning", Message = "Total price does not match gallons × price per gallon." });
            }
        }

        if (previousVehicleEvent?.Odometer is { } prevOdo && fuelEvent.Odometer is { } curOdo)
        {
            if (curOdo < prevOdo)
            {
                result.Issues.Add(new ValidationIssue { IssueType = "OdometerDecreased", Severity = "error", Message = "Odometer cannot decrease for the same vehicle." });
            }
            else
            {
                var delta = curOdo - prevOdo;
                if (delta < options.DefaultMinMilesBetweenEvents || delta > options.DefaultMaxMilesBetweenEvents)
                {
                    result.Issues.Add(new ValidationIssue { IssueType = "MilesBetweenEventsOutOfRange", Severity = "warning", Message = "Miles since previous event are outside configured bounds." });
                }
            }
        }

        if (!fuelEvent.Gallons.HasValue || !fuelEvent.Odometer.HasValue)
        {
            result.Issues.Add(new ValidationIssue { IssueType = "RequiredFieldMissing", Severity = "error", Message = "Required extracted fields are missing." });
        }

        return result;
    }
}
