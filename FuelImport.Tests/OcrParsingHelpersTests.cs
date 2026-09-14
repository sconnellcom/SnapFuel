using FuelImport.Aws.Services;

namespace FuelImport.Tests;

public class OcrParsingHelpersTests
{
    [Fact]
    public void BuildPumpFieldCandidates_ParsesExpectedFields()
    {
        var fields = new List<(string Type, string Value)>
        {
            ("QUANTITY", "12.345"),
            ("UNIT_PRICE", "$3.799"),
            ("TOTAL", "$46.89")
        };

        var parsed = OcrParsingHelpers.BuildPumpFieldCandidates(fields, []);

        Assert.Equal("12.345", parsed["gallons"]);
        Assert.Equal("3.799", parsed["pricePerGallon"]);
        Assert.Equal("46.89", parsed["totalPrice"]);
    }

    [Fact]
    public void ExtractOdometerFromText_ReturnsLargestLikelyReading()
    {
        var lines = new[]
        {
            "Trip A 124.5",
            "ODO 153245 mi",
            "Range 240"
        };

        var odometer = OcrParsingHelpers.ExtractOdometerFromText(lines);

        Assert.Equal(153245, odometer);
    }
}
