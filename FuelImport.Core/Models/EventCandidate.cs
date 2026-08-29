namespace FuelImport.Core.Models;

public class EventCandidate
{
    public SourceImage? PumpImage { get; set; }
    public SourceImage? DashImage { get; set; }
    public decimal PairingConfidence { get; set; }
    public bool IsAmbiguous { get; set; }
}
