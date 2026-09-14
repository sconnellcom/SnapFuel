using FuelImport.Worker.Services;

namespace FuelImport.Tests;

public class FileImageMetadataExtractorTests
{
    [Theory]
    [InlineData("2026:08:02 13:26:03", 2026, 8, 2, 13, 26, 3, 0)]
    [InlineData("2026:08:02 13:26:03.123", 2026, 8, 2, 13, 26, 3, 123)]
    public void TryParseExifTimestamp_ParsesStandardExifFormats(
        string rawValue,
        int year,
        int month,
        int day,
        int hour,
        int minute,
        int second,
        int millisecond)
    {
        var parsed = FileImageMetadataExtractor.TryParseExifTimestamp(rawValue, out var timestamp);

        Assert.True(parsed);
        Assert.Equal(new DateTime(year, month, day, hour, minute, second, millisecond), timestamp);
    }
}