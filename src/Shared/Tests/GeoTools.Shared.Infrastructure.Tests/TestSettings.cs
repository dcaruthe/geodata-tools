using GeoTools.Shared.Application.State;

namespace GeoTools.Shared.Infrastructure.Tests;

internal sealed class TestSettings : ISettingsStore
{
    public MapSettings Map { get; set; } = new([2]);

    public GeodataSettings Geodata { get; set; } = new([2, 4], 0.002);

    public long Version { get; private set; }

    private readonly Dictionary<FeatureFlag, bool> _flags = new()
    {
        [FeatureFlag.MapGrid] = true,
        [FeatureFlag.MapCrosshair] = true,
        [FeatureFlag.GeodataAutoPull] = true,
    };

    public bool IsEnabled(FeatureFlag flag) => _flags.TryGetValue(flag, out var enabled) && enabled;

    public event EventHandler? Changed;

    public Task ReloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<SettingsResult> SetDisplayAdminLevelsAsync(
        IReadOnlyList<int> levels,
        CancellationToken cancellationToken = default
    )
    {
        Map = new MapSettings(levels);
        Bump();
        return Task.FromResult(SettingsResult.Ok);
    }

    public Task<SettingsResult> SetExtractAdminLevelsAsync(
        IReadOnlyList<int> levels,
        CancellationToken cancellationToken = default
    )
    {
        Geodata = Geodata with { ExtractAdminLevels = levels };
        Bump();
        return Task.FromResult(SettingsResult.Ok);
    }

    public Task<SettingsResult> SetSimplifyToleranceAsync(
        double tolerance,
        CancellationToken cancellationToken = default
    )
    {
        Geodata = Geodata with { SimplifyToleranceDegrees = tolerance };
        Bump();
        return Task.FromResult(SettingsResult.Ok);
    }

    public Task<SettingsResult> SetFlagAsync(
        FeatureFlag flag,
        bool enabled,
        CancellationToken cancellationToken = default
    )
    {
        _flags[flag] = enabled;
        Bump();
        return Task.FromResult(SettingsResult.Ok);
    }

    public Task<SettingsResult> ResetToConfigurationAsync(
        CancellationToken cancellationToken = default
    ) => Task.FromResult(SettingsResult.Ok);

    public void SetFlag(FeatureFlag flag, bool enabled)
    {
        _flags[flag] = enabled;
        Bump();
    }

    private void Bump()
    {
        Version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
