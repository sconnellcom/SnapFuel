using System.Globalization;
using System.Text.RegularExpressions;

namespace FuelImport.Aws.Services;

public static partial class OcrParsingHelpers
{
    private static readonly string[] PumpTextHints = ["gal", "gallon", "diesel", "regular", "price/gal", "price per gallon", "fuel"];
    private static readonly string[] DashTextHints = ["odometer", "odo", "trip", "mi", "miles"];

    public static decimal? ExtractDecimal(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var match = DecimalRegex().Match(input.Replace(",", string.Empty));
        if (!match.Success) return null;
        return decimal.TryParse(match.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    public static int? ExtractOdometerFromText(IEnumerable<string> lines)
    {
        var candidates = new List<int>();
        foreach (var line in lines)
        {
            foreach (Match match in OdometerRegex().Matches(line))
            {
                if (int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                {
                    candidates.Add(value);
                }
            }
        }

        if (candidates.Count == 0) return null;
        return candidates.OrderByDescending(x => x).First();
    }

    public static Dictionary<string, string> BuildPumpFieldCandidates(IEnumerable<(string Type, string Value)> summaryFields, IEnumerable<string> textLines)
    {
        decimal? gallons = null;
        decimal? total = null;
        decimal? pricePerGallon = null;

        foreach (var (type, value) in summaryFields)
        {
            var normalized = type.Trim().ToLowerInvariant();
            if (normalized.Contains("quantity") || normalized.Contains("gallon") || normalized.Contains("volume"))
            {
                gallons ??= ExtractDecimal(value);
            }
            else if (normalized.Contains("total") || normalized.Contains("amount") || normalized.Contains("subtotal"))
            {
                total ??= ExtractDecimal(value);
            }
            else if (normalized.Contains("unit") || normalized.Contains("price") || normalized.Contains("rate"))
            {
                pricePerGallon ??= ExtractDecimal(value);
            }
        }

        var rawLines = textLines.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (!gallons.HasValue)
        {
            gallons = rawLines
                .Where(l => l.Contains("gal", StringComparison.OrdinalIgnoreCase))
                .Select(ExtractDecimal)
                .FirstOrDefault(v => v.HasValue);
        }

        if (!pricePerGallon.HasValue)
        {
            pricePerGallon = rawLines
                .Where(l => l.Contains("/gal", StringComparison.OrdinalIgnoreCase) || l.Contains("per gallon", StringComparison.OrdinalIgnoreCase))
                .Select(ExtractDecimal)
                .FirstOrDefault(v => v.HasValue);
        }

        if (!total.HasValue)
        {
            total = rawLines
                .Where(l => l.Contains("total", StringComparison.OrdinalIgnoreCase))
                .Select(ExtractDecimal)
                .FirstOrDefault(v => v.HasValue);
        }

        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (gallons.HasValue) parsed["gallons"] = gallons.Value.ToString(CultureInfo.InvariantCulture);
        if (pricePerGallon.HasValue) parsed["pricePerGallon"] = pricePerGallon.Value.ToString(CultureInfo.InvariantCulture);
        if (total.HasValue) parsed["totalPrice"] = total.Value.ToString(CultureInfo.InvariantCulture);
        return parsed;
    }

    public static (decimal PumpScore, decimal DashScore) ScoreImageTypeHints(IEnumerable<string> labels, IEnumerable<string> textLines)
    {
        decimal pump = 0m;
        decimal dash = 0m;

        foreach (var label in labels)
        {
            var l = label.ToLowerInvariant();
            if (l.Contains("fuel") || l.Contains("gas") || l.Contains("receipt") || l.Contains("petrol")) pump += 0.30m;
            if (l.Contains("dashboard") || l.Contains("speedometer") || l.Contains("gauge") || l.Contains("vehicle interior")) dash += 0.35m;
        }

        foreach (var line in textLines)
        {
            var t = line.ToLowerInvariant();
            if (PumpTextHints.Any(h => t.Contains(h))) pump += 0.12m;
            if (DashTextHints.Any(h => t.Contains(h))) dash += 0.12m;
        }

        return (Math.Clamp(pump, 0m, 1m), Math.Clamp(dash, 0m, 1m));
    }

    [GeneratedRegex(@"\d+(?:\.\d+)?")]
    private static partial Regex DecimalRegex();

    [GeneratedRegex(@"\b\d{4,7}\b")]
    private static partial Regex OdometerRegex();
}
