namespace FuelImport.Core.Options;

public class ConfidenceOptions
{
    public decimal PumpOcrWeight { get; set; } = 0.25m;
    public decimal DashOcrWeight { get; set; } = 0.20m;
    public decimal ClassificationWeight { get; set; } = 0.15m;
    public decimal PairingWeight { get; set; } = 0.15m;
    public decimal VehicleWeight { get; set; } = 0.20m;
    public decimal ValidationPenaltyPerError { get; set; } = 0.10m;
    public decimal ValidationPenaltyPerWarning { get; set; } = 0.03m;
    public decimal AutoApproveThreshold { get; set; } = 0.90m;
    public decimal ReviewThreshold { get; set; } = 0.70m;
}
