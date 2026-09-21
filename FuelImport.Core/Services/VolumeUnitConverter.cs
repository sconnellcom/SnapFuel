namespace FuelImport.Core.Services;

/// <summary>Converts fuel volumes between US gallons (the unit everything is stored/reported in) and liters.</summary>
public static class VolumeUnitConverter
{
    public const decimal LitersPerGallon = 3.785411784m;

    public static decimal LitersToGallons(decimal liters) => Math.Round(liters / LitersPerGallon, 3, MidpointRounding.AwayFromZero);

    public static decimal GallonsToLiters(decimal gallons) => Math.Round(gallons * LitersPerGallon, 3, MidpointRounding.AwayFromZero);
}
