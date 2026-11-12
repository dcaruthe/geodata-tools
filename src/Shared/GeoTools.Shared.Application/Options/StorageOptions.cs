namespace GeoTools.Shared.Application.Options;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string DatabasePath { get; set; } = "geodata/state/geotools.db";

    // The API reads the launcher's database. A read-only connection keeps the launcher the
    // single writer and turns an accidental write into a SQLite error instead of a race.
    public bool ReadOnly { get; set; }
}
