using FuelImport.Core.Interfaces;
using FuelImport.Core.Models;

namespace FuelImport.Core.Services;

public class EventPairingService : IEventPairingService
{
    private readonly TimeSpan _maxGap;

    public EventPairingService(TimeSpan? maxGap = null)
    {
        _maxGap = maxGap ?? TimeSpan.FromMinutes(10);
    }

    public IReadOnlyCollection<EventCandidate> Pair(IEnumerable<SourceImage> images)
    {
        var sorted = images.OrderBy(i => i.CapturedAtUtc ?? DateTime.MaxValue).ToList();
        var usedDash = new HashSet<int>();
        var result = new List<EventCandidate>();

        foreach (var pump in sorted.Where(i => i.ImageTypeCandidate == ImageType.Pump))
        {
            var pumpTime = pump.CapturedAtUtc;
            var candidates = sorted
                .Where(i => i.ImageTypeCandidate == ImageType.Dashboard && !usedDash.Contains(i.SourceImageId))
                .Where(i => pumpTime.HasValue && i.CapturedAtUtc.HasValue && Math.Abs((i.CapturedAtUtc.Value - pumpTime.Value).TotalMinutes) <= _maxGap.TotalMinutes)
                .OrderBy(i => Math.Abs((i.CapturedAtUtc!.Value - pumpTime!.Value).TotalMinutes))
                .Take(2)
                .ToList();

            if (candidates.Count == 0)
            {
                result.Add(new EventCandidate { PumpImage = pump, PairingConfidence = 0.5m });
                continue;
            }

            var selected = candidates[0];
            usedDash.Add(selected.SourceImageId);
            var minutes = Math.Abs((selected.CapturedAtUtc!.Value - pumpTime!.Value).TotalMinutes);
            var confidence = Math.Max(0.5m, 1m - (decimal)(minutes / _maxGap.TotalMinutes));
            result.Add(new EventCandidate
            {
                PumpImage = pump,
                DashImage = selected,
                PairingConfidence = confidence,
                IsAmbiguous = candidates.Count > 1
            });
        }

        foreach (var unpairedDash in sorted.Where(i => i.ImageTypeCandidate == ImageType.Dashboard && !usedDash.Contains(i.SourceImageId)))
        {
            result.Add(new EventCandidate { DashImage = unpairedDash, PairingConfidence = 0.45m });
        }

        return result;
    }
}
