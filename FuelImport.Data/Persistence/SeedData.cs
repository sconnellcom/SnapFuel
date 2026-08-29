using FuelImport.Core.Models;

namespace FuelImport.Data.Persistence;

public static class SeedData
{
    public static async Task EnsureSeededAsync(FuelImportDbContext dbContext, CancellationToken cancellationToken = default)
    {
        if (dbContext.Vehicles.Any())
        {
            return;
        }

        dbContext.Vehicles.AddRange(
            new Vehicle
            {
                Name = "Vehicle A",
                DashboardLabel = "vehicle-a",
                ExpectedTankGallonsMin = 5,
                ExpectedTankGallonsMax = 18,
                OdometerMinKnown = 0,
                OdometerMaxKnown = 250000,
                Active = true
            },
            new Vehicle
            {
                Name = "Vehicle B",
                DashboardLabel = "vehicle-b",
                ExpectedTankGallonsMin = 5,
                ExpectedTankGallonsMax = 22,
                OdometerMinKnown = 0,
                OdometerMaxKnown = 300000,
                Active = true
            });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
