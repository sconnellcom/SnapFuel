using FuelImport.Core.Models;

namespace FuelImport.Core.Services;

public class ManualReviewGroupingService
{
    private readonly TimeSpan _maxGap;
    private readonly double _maxDistanceKilometers;

    public ManualReviewGroupingService(TimeSpan? maxGap = null, double maxDistanceKilometers = 0.40d)
    {
        _maxGap = maxGap ?? TimeSpan.FromMinutes(15);
        _maxDistanceKilometers = maxDistanceKilometers;
    }

    public IReadOnlyCollection<ManualReviewGroup> Group(
        IEnumerable<SourceImage> images,
        IReadOnlyDictionary<int, int>? linkedEventByImageId = null)
    {
        var groups = new List<ManualReviewGroup>();

        foreach (var image in images
                     .OrderBy(i => i.CapturedAtUtc ?? i.CapturedAtLocal ?? DateTime.MaxValue)
                     .ThenBy(i => i.SourceImageId))
        {
            var currentGroup = FindGroupForImage(groups, image, linkedEventByImageId);
            if (currentGroup is null)
            {
                groups.Add(CreateGroup(image, linkedEventByImageId));
                continue;
            }

            AppendImageToGroup(currentGroup, image, linkedEventByImageId);
        }

        return groups;
    }

    private ManualReviewGroup? FindGroupForImage(
        List<ManualReviewGroup> groups,
        SourceImage image,
        IReadOnlyDictionary<int, int>? linkedEventByImageId)
    {
        if (!string.IsNullOrWhiteSpace(image.ManualGroupKey))
        {
            return groups.FirstOrDefault(group =>
                string.Equals(group.Images[0].ManualGroupKey, image.ManualGroupKey, StringComparison.Ordinal));
        }

        var currentGroup = groups.LastOrDefault();
        return currentGroup is not null && BelongsInGroup(currentGroup, image, linkedEventByImageId)
            ? currentGroup
            : null;
    }

    private static void AppendImageToGroup(
        ManualReviewGroup group,
        SourceImage image,
        IReadOnlyDictionary<int, int>? linkedEventByImageId)
    {
        group.Images.Add(image);
        group.StartedAtUtc = MinTimestamp(group.StartedAtUtc, image.CapturedAtUtc ?? image.CapturedAtLocal);
        group.EndedAtUtc = MaxTimestamp(group.EndedAtUtc, image.CapturedAtUtc ?? image.CapturedAtLocal);
        group.Latitude = AverageCoordinate(group.Images, x => x.Latitude);
        group.Longitude = AverageCoordinate(group.Images, x => x.Longitude);
        group.MaxDistanceKilometers = CalculateMaxDistanceKilometers(group.Images);
        group.FuelEventId ??= linkedEventByImageId is not null && linkedEventByImageId.TryGetValue(image.SourceImageId, out var fuelEventId)
            ? fuelEventId
            : null;
        group.GroupKey = BuildGroupKey(group.Images);
    }

    private bool BelongsInGroup(
        ManualReviewGroup group,
        SourceImage image,
        IReadOnlyDictionary<int, int>? linkedEventByImageId)
    {
        var groupEventId = group.FuelEventId;
        if (group.Images[0].ManualGroupKey is not null || image.ManualGroupKey is not null)
        {
            return string.Equals(group.Images[0].ManualGroupKey, image.ManualGroupKey, StringComparison.Ordinal);
        }

        var imageEventId = linkedEventByImageId is not null && linkedEventByImageId.TryGetValue(image.SourceImageId, out var linkedId)
            ? linkedId
            : (int?)null;

        if (groupEventId != imageEventId)
        {
            if (groupEventId.HasValue || imageEventId.HasValue)
            {
                return false;
            }
        }

        var groupTime = group.EndedAtUtc ?? group.StartedAtUtc;
        var imageTime = image.CapturedAtUtc ?? image.CapturedAtLocal;
        if (groupTime.HasValue && imageTime.HasValue && Math.Abs((imageTime.Value - groupTime.Value).TotalMinutes) > _maxGap.TotalMinutes)
        {
            return false;
        }

        if (group.Latitude.HasValue && group.Longitude.HasValue && image.Latitude.HasValue && image.Longitude.HasValue)
        {
            return DistanceKilometers(group.Latitude.Value, group.Longitude.Value, image.Latitude.Value, image.Longitude.Value) <= _maxDistanceKilometers;
        }

        return true;
    }

    private static ManualReviewGroup CreateGroup(SourceImage image, IReadOnlyDictionary<int, int>? linkedEventByImageId)
    {
        var fuelEventId = linkedEventByImageId is not null && linkedEventByImageId.TryGetValue(image.SourceImageId, out var existingEventId)
            ? existingEventId
            : (int?)null;

        return new ManualReviewGroup
        {
            GroupKey = BuildGroupKey([image]),
            FuelEventId = fuelEventId,
            StartedAtUtc = image.CapturedAtUtc ?? image.CapturedAtLocal,
            EndedAtUtc = image.CapturedAtUtc ?? image.CapturedAtLocal,
            Latitude = image.Latitude,
            Longitude = image.Longitude,
            MaxDistanceKilometers = 0d,
            Images = [image]
        };
    }

    private static string BuildGroupKey(IEnumerable<SourceImage> images)
    {
        var ordered = images.OrderBy(i => i.SourceImageId).ToList();
        var manualKey = ordered.Select(i => i.ManualGroupKey).FirstOrDefault(key => !string.IsNullOrWhiteSpace(key));
        return !string.IsNullOrWhiteSpace(manualKey)
            ? manualKey
            : string.Join('-', ordered.Select(i => i.SourceImageId));
    }

    private static DateTime? MinTimestamp(DateTime? left, DateTime? right)
    {
        if (!left.HasValue) return right;
        if (!right.HasValue) return left;
        return left.Value <= right.Value ? left : right;
    }

    private static DateTime? MaxTimestamp(DateTime? left, DateTime? right)
    {
        if (!left.HasValue) return right;
        if (!right.HasValue) return left;
        return left.Value >= right.Value ? left : right;
    }

    private static double? AverageCoordinate(IEnumerable<SourceImage> images, Func<SourceImage, double?> selector)
    {
        var values = images.Select(selector).Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return values.Count == 0 ? null : values.Average();
    }

    private static double DistanceKilometers(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        const double earthRadiusKm = 6371.0d;
        var deltaLatitude = DegreesToRadians(latitude2 - latitude1);
        var deltaLongitude = DegreesToRadians(longitude2 - longitude1);
        var a = Math.Pow(Math.Sin(deltaLatitude / 2), 2) +
                Math.Cos(DegreesToRadians(latitude1)) * Math.Cos(DegreesToRadians(latitude2)) *
                Math.Pow(Math.Sin(deltaLongitude / 2), 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusKm * c;
    }

    internal static double? CalculateDistanceKilometers(SourceImage? left, SourceImage? right)
    {
        if (left?.Latitude is not { } leftLatitude || left.Longitude is not { } leftLongitude)
        {
            return null;
        }

        if (right?.Latitude is not { } rightLatitude || right.Longitude is not { } rightLongitude)
        {
            return null;
        }

        return DistanceKilometers(leftLatitude, leftLongitude, rightLatitude, rightLongitude);
    }

    private static double? CalculateMaxDistanceKilometers(IEnumerable<SourceImage> images)
    {
        var coordinates = images
            .Where(i => i.Latitude.HasValue && i.Longitude.HasValue)
            .ToList();

        if (coordinates.Count < 2)
        {
            return coordinates.Count == 1 ? 0d : null;
        }

        double maxDistance = 0d;
        for (var i = 0; i < coordinates.Count - 1; i++)
        {
            for (var j = i + 1; j < coordinates.Count; j++)
            {
                var distance = DistanceKilometers(
                    coordinates[i].Latitude!.Value,
                    coordinates[i].Longitude!.Value,
                    coordinates[j].Latitude!.Value,
                    coordinates[j].Longitude!.Value);
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                }
            }
        }

        return maxDistance;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0d;
}