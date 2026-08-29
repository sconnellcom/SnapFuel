using FuelImport.Core.Models;
using FuelImport.Core.Services;

namespace FuelImport.Tests;

public class EventPairingServiceTests
{
    [Fact]
    public void Pair_MatchesPumpAndDashWithinWindow()
    {
        var now = DateTime.UtcNow;
        var service = new EventPairingService(TimeSpan.FromMinutes(10));
        var images = new[]
        {
            new SourceImage { SourceImageId = 1, ImageTypeCandidate = ImageType.Pump, CapturedAtUtc = now },
            new SourceImage { SourceImageId = 2, ImageTypeCandidate = ImageType.Dashboard, CapturedAtUtc = now.AddMinutes(3) }
        };

        var result = service.Pair(images).ToList();

        Assert.Single(result);
        Assert.Equal(1, result[0].PumpImage?.SourceImageId);
        Assert.Equal(2, result[0].DashImage?.SourceImageId);
        Assert.True(result[0].PairingConfidence >= 0.5m);
    }
}
