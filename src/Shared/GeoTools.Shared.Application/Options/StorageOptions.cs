namespace GeoTools.Shared.Application.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string DatabasePath { get; set; } = "geodata/state/geotools.db";
}
