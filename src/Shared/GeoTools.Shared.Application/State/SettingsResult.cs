namespace GeoTools.Shared.Application.State;

public readonly record struct SettingsResult(bool Accepted, string? Error)
{
    public static SettingsResult Ok { get; } = new(true, null);

    public static SettingsResult Rejected(string error) => new(false, error);
}
