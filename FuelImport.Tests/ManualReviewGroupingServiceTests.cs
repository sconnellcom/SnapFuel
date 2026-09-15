using FuelImport.Core.Models;
using FuelImport.Core.Services;

namespace FuelImport.Tests;

public class ManualReviewGroupingServiceTests
{
    [Fact]
    public void GroupsNearbyImagesIntoSingleManualReviewItem()
    {
        var now = new DateTime(2026, 9, 11, 18, 0, 0, DateTimeKind.Utc);
        var service = new ManualReviewGroupingService(TimeSpan.FromMinutes(15), 0.40d);

        var images = new[]
        {
            new SourceImage { SourceImageId = 1, CapturedAtUtc = now, Latitude = 35.0, Longitude = -80.0 },
            new SourceImage { SourceImageId = 2, CapturedAtUtc = now.AddMinutes(6), Latitude = 35.0008, Longitude = -80.0008 }
        };

        var groups = service.Group(images);

        Assert.Single(groups);
        Assert.Equal("1-2", groups.Single().GroupKey);
        Assert.Equal(2, groups.Single().Images.Count);
    }

    [Fact]
    public void SplitsImagesWhenLocationIsFarAwayDespiteCloseTimestamp()
    {
        var now = new DateTime(2026, 9, 11, 18, 0, 0, DateTimeKind.Utc);
        var service = new ManualReviewGroupingService(TimeSpan.FromMinutes(15), 0.40d);

        var images = new[]
        {
            new SourceImage { SourceImageId = 1, CapturedAtUtc = now, Latitude = 35.0, Longitude = -80.0 },
            new SourceImage { SourceImageId = 2, CapturedAtUtc = now.AddMinutes(4), Latitude = 35.05, Longitude = -80.05 }
        };

        var groups = service.Group(images);

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void KeepsManualSplitGroupsSeparated()
    {
        var now = new DateTime(2026, 9, 11, 18, 0, 0, DateTimeKind.Utc);
        var service = new ManualReviewGroupingService(TimeSpan.FromMinutes(15), 0.40d);

        var images = new[]
        {
            new SourceImage { SourceImageId = 1, CapturedAtUtc = now, Latitude = 35.0, Longitude = -80.0, ManualGroupKey = "group-a" },
            new SourceImage { SourceImageId = 2, CapturedAtUtc = now.AddMinutes(1), Latitude = 35.0001, Longitude = -80.0001, ManualGroupKey = "group-b" }
        };

        var groups = service.Group(images);

        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, group => group.GroupKey == "group-a");
        Assert.Contains(groups, group => group.GroupKey == "group-b");
    }

    [Fact]
    public void KeepsManualGroupTogetherWhenImagesAreNonContiguousInTimeOrder()
    {
        var now = new DateTime(2026, 9, 11, 18, 0, 0, DateTimeKind.Utc);
        var service = new ManualReviewGroupingService(TimeSpan.FromMinutes(15), 0.40d);

        var images = new[]
        {
            new SourceImage { SourceImageId = 10, CapturedAtUtc = now, Latitude = 35.0, Longitude = -80.0, ManualGroupKey = "split-x" },
            new SourceImage { SourceImageId = 11, CapturedAtUtc = now.AddMinutes(1), Latitude = 35.0001, Longitude = -80.0001, ManualGroupKey = "other" },
            new SourceImage { SourceImageId = 12, CapturedAtUtc = now.AddMinutes(2), Latitude = 35.0002, Longitude = -80.0002, ManualGroupKey = "split-x" }
        };

        var groups = service.Group(images).ToList();

        Assert.Equal(2, groups.Count);
        var splitGroup = Assert.Single(groups, group => group.GroupKey == "split-x");
        Assert.Equal(2, splitGroup.Images.Count);
        Assert.Contains(splitGroup.Images, image => image.SourceImageId == 10);
        Assert.Contains(splitGroup.Images, image => image.SourceImageId == 12);
    }
}