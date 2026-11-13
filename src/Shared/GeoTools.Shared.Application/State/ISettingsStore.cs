namespace GeoTools.Shared.Application.State;

public interface ISettingsStore
{
    MapSettings Map { get; }

    GeodataSettings Geodata { get; }

    WindowSize Window { get; }

    long Version { get; }

    bool IsEnabled(FeatureFlag flag);

    Task ReloadAsync(CancellationToken cancellationToken = default);

    Task<SettingsResult> SetDisplayAdminLevelsAsync(
        IReadOnlyList<int> levels,
        CancellationToken cancellationToken = default
    );

    Task<SettingsResult> SetExtractAdminLevelsAsync(
        IReadOnlyList<int> levels,
        CancellationToken cancellationToken = default
    );

    Task<SettingsResult> SetSimplifyToleranceAsync(
        double tolerance,
        CancellationToken cancellationToken = default
    );

    Task<SettingsResult> SetWindowSizeAsync(
        WindowSize size,
        CancellationToken cancellationToken = default
    );

    Task<SettingsResult> SetFlagAsync(
        FeatureFlag flag,
        bool enabled,
        CancellationToken cancellationToken = default
    );

    Task<SettingsResult> ResetToConfigurationAsync(CancellationToken cancellationToken = default);

    event EventHandler? Changed;
}
