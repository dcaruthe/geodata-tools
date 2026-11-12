namespace GeoTools.Shared.Domain.Primatives;

public readonly record struct Color(byte R, byte G, byte B, byte A = 255)
{
    public static Color Black => new(0, 0, 0);
    public static Color White => new(255, 255, 255);
}
