namespace GeoTools.Shared.Application.Options;

public sealed class GeodataOptions
{
    public const string SectionName = "Geodata";

    public static string[] DefaultRegions { get; } =
    [
        "africa",
        "antarctica",
        "asia",
        "australia-oceania",
        "central-america",
        "europe",
        "north-america",
        "south-america",
    ];

    public string RootPath { get; set; } = "geodata";
    public string BordersSubdirectory { get; set; } = "borders";
    public string[] Regions { get; set; } = DefaultRegions;

    // A comma-separated replacement for Regions. Environment variables cannot
    // express an array, so this keeps a fixture or a single-region run honest.
    public string? RegionList { get; set; }
    public string SourceBaseUrl { get; set; } = "https://download.geofabrik.de/";
    public string SourceFilePattern { get; set; } = "{region}-latest.osm.pbf";
    public int[] ExtractAdminLevels { get; set; } = [2, 4];

    // A comma-separated replacement for ExtractAdminLevels, for environment and
    // script overrides. The array binder merges indices instead of replacing them.
    public string? ExtractAdminLevelsList { get; set; }
    public double SimplifyToleranceDegrees { get; set; } = 0.002;
    public int PullConcurrency { get; set; } = 3;
    public int RetryCount { get; set; } = 3;
    public int InactivityTimeoutSeconds { get; set; } = 60;
    public string OsmiumPath { get; set; } = "osmium";
    public bool RunOnStartup { get; set; } = true;
    public bool SkipDiskSpaceCheck { get; set; }
    public double RequiredFreeSpaceMultiplier { get; set; } = 1.2;
}
