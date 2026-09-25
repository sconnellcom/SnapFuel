using System.Collections.Concurrent;

namespace FuelImport.Web.Services;

public record AutoDetectProgress(int ProcessedPhotos, int TotalPhotos, bool Completed);

public class AutoDetectProgressTracker
{
    private readonly ConcurrentDictionary<string, ProgressState> _runs = new(StringComparer.Ordinal);

    public void Start(string? progressId, int totalPhotos)
    {
        if (string.IsNullOrWhiteSpace(progressId))
        {
            return;
        }

        var cutoff = DateTime.UtcNow.AddHours(-1);
        foreach (var staleRun in _runs.Where(run => run.Value.UpdatedAtUtc < cutoff))
        {
            _runs.TryRemove(staleRun.Key, out _);
        }

        _runs[progressId] = new ProgressState(totalPhotos);
    }

    public void Advance(string? progressId)
    {
        if (!string.IsNullOrWhiteSpace(progressId) && _runs.TryGetValue(progressId, out var state))
        {
            state.Advance();
        }
    }

    public void Complete(string? progressId)
    {
        if (!string.IsNullOrWhiteSpace(progressId) && _runs.TryGetValue(progressId, out var state))
        {
            state.Complete();
        }
    }

    public AutoDetectProgress? Get(string progressId) =>
        _runs.TryGetValue(progressId, out var state) ? state.Snapshot() : null;

    private sealed class ProgressState(int totalPhotos)
    {
        private int _processedPhotos;
        private int _completed;
        private long _updatedAtUtcTicks = DateTime.UtcNow.Ticks;

        public DateTime UpdatedAtUtc => new(Interlocked.Read(ref _updatedAtUtcTicks), DateTimeKind.Utc);

        public void Advance()
        {
            Interlocked.Increment(ref _processedPhotos);
            Interlocked.Exchange(ref _updatedAtUtcTicks, DateTime.UtcNow.Ticks);
        }

        public void Complete()
        {
            Interlocked.Exchange(ref _completed, 1);
            Interlocked.Exchange(ref _updatedAtUtcTicks, DateTime.UtcNow.Ticks);
        }

        public AutoDetectProgress Snapshot() => new(
            Math.Min(Volatile.Read(ref _processedPhotos), totalPhotos),
            totalPhotos,
            Volatile.Read(ref _completed) == 1);
    }
}