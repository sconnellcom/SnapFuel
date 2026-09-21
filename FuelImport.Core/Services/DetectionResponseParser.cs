using System.Globalization;
using System.Text.Json;
using FuelImport.Core.Models;

namespace FuelImport.Core.Services;

/// <summary>Parses the JSON answer produced by a vision model into a strongly typed detection.</summary>
public static class DetectionResponseParser
{
    public static ImageDetection Parse(int sourceImageId, string? content)
    {
        var detection = new ImageDetection
        {
            SourceImageId = sourceImageId,
            RawJson = string.IsNullOrWhiteSpace(content) ? "{}" : content
        };

        var json = ExtractJsonObject(content);
        if (json is null)
        {
            detection.ErrorMessage = "The model did not return a JSON object.";
            return detection;
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            detection.ErrorMessage = $"The model returned malformed JSON: {ex.Message}";
            return detection;
        }

        detection.RawJson = json;
        detection.ImageType = ParseImageType(ReadString(root, "imageType"));
        ApplyVolume(detection, root);
        detection.TotalCost = ReadDecimal(root, "totalCost") ?? ReadDecimal(root, "cost");
        detection.Odometer = ReadDecimal(root, "odometer") is decimal odometer
            ? (int)Math.Round(odometer, MidpointRounding.AwayFromZero)
            : null;
        detection.Confidence = ReadDecimal(root, "confidence") is decimal confidence
            ? Math.Clamp(confidence, 0m, 1m)
            : 0m;
        detection.VehicleName = ReadString(root, "vehicle");
        detection.Notes = ReadString(root, "notes");

        return detection;
    }

    /// <summary>Reads the reported volume and unit, converting to canonical gallons; assumes gallons when the unit is unclear.</summary>
    private static void ApplyVolume(ImageDetection detection, JsonElement root)
    {
        var volume = ReadDecimal(root, "volume") ?? ReadDecimal(root, "gallons");
        var unit = ReadString(root, "volumeUnit")?.Trim().ToLowerInvariant();

        if (volume is null)
        {
            return;
        }

        if (unit is "liter" or "liters" or "litre" or "litres" or "l")
        {
            detection.Liters = volume;
            detection.Gallons = VolumeUnitConverter.LitersToGallons(volume.Value);
        }
        else
        {
            detection.Gallons = volume;
        }
    }

    private static string? ExtractJsonObject(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : null;
    }

    private static ImageType ParseImageType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "pump" or "fuelpump" or "fuel_pump" or "fuel pump" => ImageType.Pump,
        "dashboard" or "dash" or "odometer" => ImageType.Dashboard,
        _ => ImageType.Unknown
    };

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private static decimal? ReadDecimal(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDecimal(out var number) => number,
            JsonValueKind.String => ParseLooseDecimal(element.GetString()),
            _ => null
        };
    }

    private static decimal? ParseLooseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = new string(value.Where(character => char.IsDigit(character) || character is '.' or '-').ToArray());
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }
}
