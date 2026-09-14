using FuelImport.Core.Models;

namespace FuelImport.Core.Interfaces;

public interface IImageMetadataExtractor
{
    Task<MetadataSnapshot> ExtractAsync(string path, CancellationToken cancellationToken = default);
}

public interface IImageClassifier
{
    Task<ClassificationResult> ClassifyAsync(SourceImage image, CancellationToken cancellationToken = default);
}

public interface IPumpOcrService
{
    Task<OcrExtractionResult> ExtractAsync(SourceImage image, CancellationToken cancellationToken = default);
}

public interface IDashboardOcrService
{
    Task<OcrExtractionResult> ExtractAsync(SourceImage image, CancellationToken cancellationToken = default);
}

public interface IVehicleResolver
{
    Task<(int? VehicleId, decimal Confidence, string Reason)> ResolveAsync(FuelEvent fuelEvent, IReadOnlyCollection<Vehicle> vehicles, CancellationToken cancellationToken = default);
}

public interface IValidationEngine
{
    ValidationResult Validate(FuelEvent fuelEvent, Vehicle? vehicle, FuelEvent? previousVehicleEvent);
}

public interface IEventPairingService
{
    IReadOnlyCollection<EventCandidate> Pair(IEnumerable<SourceImage> images);
}

public interface IConfidenceScorer
{
    decimal Calculate(decimal pumpOcrConfidence, decimal dashOcrConfidence, decimal classificationConfidence, decimal pairingConfidence, decimal vehicleConfidence, int errorCount, int warningCount);
}
