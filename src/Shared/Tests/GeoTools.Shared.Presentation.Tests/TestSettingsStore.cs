using GeoTools.Shared.Application.State;

namespace GeoTools.Shared.Presentation.Tests;

internal sealed class TestSettingsStore : ISettingsStore
{
    private MapSettings _map = new([2]);
    private GeodataSettings _geodata = new([2, 4], 0.002);
    private WindowSize _window = WindowSize.Maximized;
    private readonly Dictionary<FeatureFlag, bool> _flags = new()
    {
        [FeatureFlag.MapGrid] = true,
        [FeatureFlag.MapCrosshair] = true,
        [FeatureFlag.GeodataAutoPull] = true,
    };

    public MapSettings Map
    {
        get => _map;
        set
        {
            _map = value;
            Touch();
        }
    }

    public GeodataSettings Geodata
    {
        get => _geodata;
        set
        {
            _geodata = value;
            Touch();
        }
    }

    public WindowSize Window
    {
        get => _window;
        set
        {
            _window = value;
            Touch();
        }
    }

    public long Version { get; private set; }

    public bool ThrowOnSet { get; set; }

    public bool IsEnabled(FeatureFlag flag) => _flags.TryGetValue(flag, out var enabled) && enabled;

    public event EventHandler? Changed;

    public Task ReloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<SettingsResult> SetDisplayAdminLevelsAsync(
        IReadOnlyList<int> levels,
        CancellationToken cancellationToken = default
    )
    {
        if (ThrowOnSet)
            throw new InvalidOperationException("set failed");

        Map = new MapSettings(levels);
        return Task.FromResult(SettingsResult.Ok);
    }

    public Task<SettingsResult> SetExtractAdminLevelsAsync(
        IReadOnlyList<int> levels,
        CancellationToken cancellationToken = default
    )
    {
        Geodata = Geodata with { ExtractAdminLevels = levels };
        return Task.FromResult(SettingsResult.Ok);
    }

    public Task<SettingsResult> SetSimplifyToleranceAsync(
        double tolerance,
        CancellationToken cancellationToken = default
    )
    {
        Geodata = Geodata with { SimplifyToleranceDegrees = tolerance };
        return Task.FromResult(SettingsResult.Ok);
    }

    public Task<SettingsResult> SetFlagAsync(
        FeatureFlag flag,
        bool enabled,
        CancellationToken cancellationToken = default
    )
    {
        _flags[flag] = enabled;
        Touch();
        return Task.FromResult(SettingsResult.Ok);
    }

    public Task<SettingsResult> SetWindowSizeAsync(
        WindowSize size,
        CancellationToken cancellationToken = default
    )
    {
        if (ThrowOnSet)
            throw new InvalidOperationException("set failed");

        var validation = SettingsValidation.ValidateWindowSize(size);
        if (!validation.Accepted)
            return Task.FromResult(validation);

        Window = size;
        return Task.FromResult(SettingsResult.Ok);
    }

    public Task<SettingsResult> ResetToConfigurationAsync(
        CancellationToken cancellationToken = default
    )
    {
        _map = new MapSettings([2]);
        _geodata = new GeodataSettings([2, 4], 0.002);
        _window = WindowSize.Maximized;
        _flags[FeatureFlag.MapGrid] = true;
        _flags[FeatureFlag.MapCrosshair] = true;
        _flags[FeatureFlag.GeodataAutoPull] = true;
        Touch();
        return Task.FromResult(SettingsResult.Ok);
    }

    public void Touch()
    {
        Version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetFlag(FeatureFlag flag, bool enabled)
    {
        _flags[flag] = enabled;
        Touch();
    }
}
