using FuelImport.Core.Interfaces;
using FuelImport.Core.Models;

namespace FuelImport.Core.Services;

public class SimpleVehicleResolver : IVehicleResolver
{
    public Task<(int? VehicleId, decimal Confidence, string Reason)> ResolveAsync(FuelEvent fuelEvent, IReadOnlyCollection<Vehicle> vehicles, CancellationToken cancellationToken = default)
    {
        if (vehicles.Count == 0)
        {
            return Task.FromResult<(int?, decimal, string)>((null, 0m, "No vehicles configured"));
        }

        var candidate = vehicles
            .Where(v => v.Active)
            .OrderBy(v =>
            {
                if (!fuelEvent.Odometer.HasValue)
                {
                    return int.MaxValue;
                }

                var odo = fuelEvent.Odometer.Value;
                if (v.OdometerMinKnown.HasValue && v.OdometerMaxKnown.HasValue && odo >= v.OdometerMinKnown.Value && odo <= v.OdometerMaxKnown.Value)
                {
                    return 0;
                }

                return Math.Min(Math.Abs((v.OdometerMinKnown ?? odo) - odo), Math.Abs((v.OdometerMaxKnown ?? odo) - odo));
            })
            .FirstOrDefault();

        return Task.FromResult<(int?, decimal, string)>((candidate?.VehicleId, candidate is null ? 0m : 0.75m, "Odometer range heuristic"));
    }
}
