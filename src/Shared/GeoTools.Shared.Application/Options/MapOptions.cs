namespace GeoTools.Shared.Application.Options;

public sealed class MapOptions
{
    public const string SectionName = "Map";

    public string BordersDirectory { get; set; } = "geodata/borders";
    public int[] AdminLevels { get; set; } = [2];
    public double SimplifyToleranceDegrees { get; set; } = 0.002;
}
