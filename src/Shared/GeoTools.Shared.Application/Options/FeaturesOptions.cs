namespace GeoTools.Shared.Application.Options;

public sealed class FeaturesOptions
{
    public const string SectionName = "Features";

    public bool MapGrid { get; set; } = true;
    public bool MapCrosshair { get; set; } = true;
    public bool GeodataAutoPull { get; set; } = true;
}
