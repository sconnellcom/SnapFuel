using FuelImport.Core.Interfaces;
using FuelImport.Core.Options;

namespace FuelImport.Core.Services;

public class ConfidenceScorer(ConfidenceOptions options) : IConfidenceScorer
{
    public decimal Calculate(decimal pumpOcrConfidence, decimal dashOcrConfidence, decimal classificationConfidence, decimal pairingConfidence, decimal vehicleConfidence, int errorCount, int warningCount)
    {
        var weighted =
            (pumpOcrConfidence * options.PumpOcrWeight)
            + (dashOcrConfidence * options.DashOcrWeight)
            + (classificationConfidence * options.ClassificationWeight)
            + (pairingConfidence * options.PairingWeight)
            + (vehicleConfidence * options.VehicleWeight);

        var penalty = (errorCount * options.ValidationPenaltyPerError) + (warningCount * options.ValidationPenaltyPerWarning);
        return Math.Clamp(weighted - penalty, 0m, 1m);
    }
}
