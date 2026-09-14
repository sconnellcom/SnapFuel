using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using FuelImport.Core.Interfaces;
using FuelImport.Core.Models;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace FuelImport.Worker.Services;

public class FileImageMetadataExtractor : IImageMetadataExtractor
{
    private static readonly string[] ExifDateFormats =
    [
        "yyyy:MM:dd HH:mm:ss",
        "yyyy:MM:dd HH:mm:ss.FFF",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm:ss.FFF"
    ];

    public async Task<MetadataSnapshot> ExtractAsync(string path, CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(path);
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hashBytes = await sha.ComputeHashAsync(stream, cancellationToken);
        var hash = Convert.ToHexString(hashBytes);

        DateTime? localCapture = null;
        DateTime? utcCapture = null;
        double? lat = null;
        double? lon = null;
        int width = 0;
        int height = 0;
        var raw = new Dictionary<string, string?>();

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);
            var exifSub = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var exifIfd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var gps = directories.OfType<GpsDirectory>().FirstOrDefault();

            var dateStr = exifSub?.GetString(ExifDirectoryBase.TagDateTimeOriginal)
                ?? exifSub?.GetString(ExifDirectoryBase.TagDateTimeDigitized)
                ?? exifIfd0?.GetString(ExifDirectoryBase.TagDateTime);

            if (TryParseExifTimestamp(dateStr, out var dtLocal))
            {
                localCapture = dtLocal;
                utcCapture = dtLocal.ToUniversalTime();
            }

            var location = gps?.GetGeoLocation();
            if (location is not null)
            {
                lat = location.Latitude;
                lon = location.Longitude;
            }

            width = exifSub?.GetInt32(ExifDirectoryBase.TagExifImageWidth) ?? 0;
            height = exifSub?.GetInt32(ExifDirectoryBase.TagExifImageHeight) ?? 0;

            raw["dateTimeOriginal"] = dateStr;
            raw["latitude"] = lat?.ToString(CultureInfo.InvariantCulture);
            raw["longitude"] = lon?.ToString(CultureInfo.InvariantCulture);
        }
        catch (ImageProcessingException)
        {
            // Keep file-system metadata fallback when EXIF extraction fails.
        }
        catch (IOException)
        {
            // Keep file-system metadata fallback when EXIF extraction fails.
        }
        catch (UnauthorizedAccessException)
        {
            // Keep file-system metadata fallback when EXIF extraction fails.
        }

        return new MetadataSnapshot
        {
            FilePath = path,
            FileName = fileInfo.Name,
            FileHash = hash,
            CapturedAtLocal = localCapture,
            CapturedAtUtc = utcCapture,
            Latitude = lat,
            Longitude = lon,
            Width = width,
            Height = height,
            FileCreatedUtc = fileInfo.CreationTimeUtc,
            FileModifiedUtc = fileInfo.LastWriteTimeUtc,
            RawMetadataJson = JsonSerializer.Serialize(raw)
        };
    }

    internal static bool TryParseExifTimestamp(string? rawValue, out DateTime localCapture)
    {
        if (DateTime.TryParseExact(
                rawValue,
                ExifDateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out localCapture))
        {
            return true;
        }

        return DateTime.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out localCapture);
    }
}
