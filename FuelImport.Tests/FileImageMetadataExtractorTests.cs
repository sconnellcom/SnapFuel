using FuelImport.Core.Services;

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

    [Fact]
    public async Task ExtractAsync_HandlesMissingExifDimensionsGracefully()
    {
        var path = Path.Combine(Path.GetTempPath(), $"snapfuel-missing-exif-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(path, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 });

        try
        {
            var result = await new FileImageMetadataExtractor().ExtractAsync(path);

            Assert.Equal(Path.GetFileName(path), result.FileName);
            Assert.False(string.IsNullOrWhiteSpace(result.FileHash));
            Assert.Equal(0, result.Width);
            Assert.Equal(0, result.Height);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}