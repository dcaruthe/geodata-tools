namespace GeoTools.Shared.Application.Options;

public sealed class MapOptions
{
    public const string SectionName = "Map";

    public int[] AdminLevels { get; set; } = [2];
}
