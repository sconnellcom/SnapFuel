using FuelImport.Core.Models;
using FuelImport.Core.Options;

namespace FuelImport.Core.Services;

/// <summary>
/// Builds plausible odometer / gallons / cost windows for a photo so the vision model gets guardrails
/// and its answers can be sanity-checked before they reach the review queue.
/// </summary>
public class DetectionEstimator(AutoDetectOptions options)
{
    private readonly AutoDetectOptions _options = options;

    public DetectionEstimates Create(Vehicle? vehicle, DateTime captureUtc, IEnumerable<FuelEvent> vehicleHistory)
    {
        var estimates = new DetectionEstimates
        {
            MinPricePerGallon = _options.MinPricePerGallon,
            MaxPricePerGallon = _options.MaxPricePerGallon
        };

        ApplyGallonsWindow(estimates, vehicle?.MaxGallonsPerFillUp);
        ApplyOdometerWindow(estimates, vehicle, captureUtc, vehicleHistory);
        return estimates;
    }

    /// <summary>
    /// Builds a window when the vehicle is not yet known, by unioning the plausible range of every
    /// candidate vehicle so the bounds still come from recorded readings rather than a fixed default.
    /// </summary>
    public DetectionEstimates CreateForUnknownVehicle(
        DateTime captureUtc,
        IReadOnlyCollection<Vehicle> vehicles,
        IReadOnlyDictionary<int, List<FuelEvent>> historyByVehicleId)
    {
        var perVehicle = vehicles
            .Select(vehicle => Create(
                vehicle,
                captureUtc,
                historyByVehicleId.TryGetValue(vehicle.VehicleId, out var history) ? history : []))
            .ToList();

        var withHistory = vehicles
            .Where(vehicle => historyByVehicleId.TryGetValue(vehicle.VehicleId, out var history) && history.Count > 0)
            .Select(vehicle => Create(vehicle, captureUtc, historyByVehicleId[vehicle.VehicleId]))
            .ToList();

        // Vehicles with no readings would only contribute the configured default, which would swamp real data.
        var contributing = withHistory.Count > 0 ? withHistory : perVehicle;
        if (contributing.Count == 0)
        {
            return Create(null, captureUtc, []);
        }

        var estimates = new DetectionEstimates
        {
            MinPricePerGallon = _options.MinPricePerGallon,
            MaxPricePerGallon = _options.MaxPricePerGallon,
            OdometerMinimum = contributing.Min(candidate => candidate.OdometerMinimum),
            OdometerMaximum = contributing.Max(candidate => candidate.OdometerMaximum),
            OdometerBasis = contributing.Count == 1
                ? contributing[0].OdometerBasis
                : $"combined range across {contributing.Count} candidate vehicles"
        };

        estimates.OdometerExpected = contributing.Count == 1 ? contributing[0].OdometerExpected : null;
        ApplyGallonsWindow(estimates, vehicles.Max(vehicle => vehicle.MaxGallonsPerFillUp));
        return estimates;
    }

    private void ApplyGallonsWindow(DetectionEstimates estimates, decimal? maxGallonsPerFillUp)
    {
        var maxGallons = maxGallonsPerFillUp is decimal vehicleMax && vehicleMax > 0
            ? Math.Min(vehicleMax, _options.MaxGallons)
            : _options.MaxGallons;

        estimates.GallonsMinimum = 0m;
        estimates.GallonsMaximum = Math.Round(maxGallons, 2, MidpointRounding.AwayFromZero);
        estimates.CostMinimum = 0m;
        estimates.CostMaximum = Math.Round(estimates.GallonsMaximum * _options.MaxPricePerGallon, 2, MidpointRounding.AwayFromZero);
    }

    private void ApplyOdometerWindow(DetectionEstimates estimates, Vehicle? vehicle, DateTime captureUtc, IEnumerable<FuelEvent> vehicleHistory)
    {
        var known = vehicleHistory
            .Where(fuelEvent => fuelEvent.Odometer.HasValue)
            .Select(fuelEvent => (
                Timestamp: fuelEvent.EventTimeUtc ?? fuelEvent.EventTimeLocal ?? fuelEvent.CreatedAtUtc,
                Odometer: fuelEvent.Odometer!.Value))
            .OrderBy(point => point.Timestamp)
            .ToList();

        var previous = known.LastOrDefault(point => point.Timestamp <= captureUtc);
        var next = known.FirstOrDefault(point => point.Timestamp > captureUtc);
        var hasPrevious = known.Any(point => point.Timestamp <= captureUtc);
        var hasNext = known.Any(point => point.Timestamp > captureUtc);

        int minimum;
        int maximum;
        int? expected;
        string basis;

        if (hasPrevious && hasNext)
        {
            var span = (next.Timestamp - previous.Timestamp).TotalDays;
            var fraction = span <= 0 ? 0.5d : Math.Clamp((captureUtc - previous.Timestamp).TotalDays / span, 0d, 1d);
            minimum = previous.Odometer;
            maximum = Math.Max(next.Odometer, previous.Odometer);
            expected = previous.Odometer + (int)Math.Round((maximum - minimum) * fraction);
            basis = $"interpolated between {previous.Odometer:N0} on {previous.Timestamp:yyyy-MM-dd} and {next.Odometer:N0} on {next.Timestamp:yyyy-MM-dd}";
        }
        else if (hasPrevious)
        {
            var days = Math.Max(0d, (captureUtc - previous.Timestamp).TotalDays);
            minimum = previous.Odometer;
            maximum = previous.Odometer + (int)Math.Ceiling(days * (double)_options.MaxMilesPerDay);
            expected = previous.Odometer + (int)Math.Round(days * (double)_options.TypicalMilesPerDay);
            basis = $"projected forward from {previous.Odometer:N0} on {previous.Timestamp:yyyy-MM-dd} ({days:F1} days earlier)";
        }
        else if (hasNext)
        {
            var days = Math.Max(0d, (next.Timestamp - captureUtc).TotalDays);
            maximum = next.Odometer;
            minimum = Math.Max(0, next.Odometer - (int)Math.Ceiling(days * (double)_options.MaxMilesPerDay));
            expected = Math.Max(0, next.Odometer - (int)Math.Round(days * (double)_options.TypicalMilesPerDay));
            basis = $"projected backward from {next.Odometer:N0} on {next.Timestamp:yyyy-MM-dd} ({days:F1} days later)";
        }
        else
        {
            minimum = vehicle?.OdometerMinKnown ?? _options.FallbackOdometerMinimum;
            maximum = vehicle?.OdometerMaxKnown ?? _options.FallbackOdometerMaximum;
            if (maximum <= minimum)
            {
                maximum = minimum + _options.MinimumOdometerWindow;
            }

            expected = minimum + ((maximum - minimum) / 2);
            basis = "no recorded history for this vehicle; using the configured default range";
        }

        if (maximum - minimum < _options.MinimumOdometerWindow)
        {
            maximum = minimum + _options.MinimumOdometerWindow;
        }

        estimates.OdometerMinimum = Math.Max(0, minimum);
        estimates.OdometerMaximum = Math.Max(estimates.OdometerMinimum, maximum);
        estimates.OdometerExpected = expected.HasValue
            ? Math.Clamp(expected.Value, estimates.OdometerMinimum, estimates.OdometerMaximum)
            : null;
        estimates.OdometerBasis = basis;
    }

    /// <summary>Flags values the model returned that fall outside the plausible windows.</summary>
    public IReadOnlyList<string> Validate(ImageDetection detection, DetectionEstimates estimates)
    {
        var warnings = new List<string>();

        if (detection.Gallons is decimal gallons &&
            (gallons < estimates.GallonsMinimum || gallons > estimates.GallonsMaximum))
        {
            warnings.Add($"Gallons {gallons:0.###} is outside the expected range {estimates.GallonsMinimum:0.##}-{estimates.GallonsMaximum:0.##}.");
        }

        if (detection.TotalCost is decimal cost && cost < 0m)
        {
            warnings.Add($"Cost {cost:0.00} cannot be negative.");
        }

        if (detection.Gallons is decimal g && g > 0m && detection.TotalCost is decimal c)
        {
            var pricePerGallon = c / g;
            if (pricePerGallon < estimates.MinPricePerGallon || pricePerGallon > estimates.MaxPricePerGallon)
            {
                warnings.Add($"Price per gallon {pricePerGallon:0.00} is outside {estimates.MinPricePerGallon:0.00}-{estimates.MaxPricePerGallon:0.00}.");
            }
        }

        if (detection.Odometer is int odometer &&
            (odometer < estimates.OdometerMinimum || odometer > estimates.OdometerMaximum))
        {
            warnings.Add($"Odometer {odometer:N0} is outside the expected range {estimates.OdometerMinimum:N0}-{estimates.OdometerMaximum:N0}.");
        }

        return warnings;
    }
}
