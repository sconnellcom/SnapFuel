using FuelImport.Core.Models;
using FuelImport.Core.Services;

namespace FuelImport.Tests;

public class DetectionResponseParserTests
{
    [Fact]
    public void Parse_ReadsPumpReading()
    {
        const string content = "{\"imageType\":\"pump\",\"gallons\":12.345,\"totalCost\":41.2,\"odometer\":null,\"confidence\":0.82,\"notes\":\"clear display\"}";

        var detection = DetectionResponseParser.Parse(7, content);

        Assert.Equal(7, detection.SourceImageId);
        Assert.Equal(ImageType.Pump, detection.ImageType);
        Assert.Equal(12.345m, detection.Gallons);
        Assert.Equal(41.2m, detection.TotalCost);
        Assert.Null(detection.Odometer);
        Assert.Equal(0.82m, detection.Confidence);
        Assert.Null(detection.ErrorMessage);
    }

    [Fact]
    public void Parse_ReadsDashboardReadingWrappedInProseAndCodeFence()
    {
        const string content = "Here you go:\n```json\n{\"imageType\":\"dashboard\",\"odometer\":\"176,482\",\"confidence\":0.6}\n```";

        var detection = DetectionResponseParser.Parse(3, content);

        Assert.Equal(ImageType.Dashboard, detection.ImageType);
        Assert.Equal(176482, detection.Odometer);
        Assert.Equal(0.6m, detection.Confidence);
    }

    [Fact]
    public void Parse_ReportsErrorWhenNoJsonIsPresent()
    {
        var detection = DetectionResponseParser.Parse(1, "I cannot tell what this photo shows.");

        Assert.Equal(ImageType.Unknown, detection.ImageType);
        Assert.NotNull(detection.ErrorMessage);
    }

    [Fact]
    public void Parse_ClampsConfidenceToUnitRange()
    {
        var detection = DetectionResponseParser.Parse(1, "{\"imageType\":\"other\",\"confidence\":7}");

        Assert.Equal(1m, detection.Confidence);
    }

    [Fact]
    public void Parse_ConvertsLitersToGallons()
    {
        const string content = "{\"imageType\":\"pump\",\"volume\":45.42,\"volumeUnit\":\"liters\",\"totalCost\":41.2,\"confidence\":0.8}";

        var detection = DetectionResponseParser.Parse(9, content);

        Assert.Equal(45.42m, detection.Liters);
        Assert.Equal(VolumeUnitConverter.LitersToGallons(45.42m), detection.Gallons);
    }

    [Fact]
    public void Parse_AssumesGallonsWhenUnitIsMissing()
    {
        const string content = "{\"imageType\":\"pump\",\"volume\":12.0,\"totalCost\":41.2,\"confidence\":0.8}";

        var detection = DetectionResponseParser.Parse(9, content);

        Assert.Equal(12.0m, detection.Gallons);
        Assert.Null(detection.Liters);
    }
}
