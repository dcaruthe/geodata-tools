using System.Globalization;
using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.State;

internal sealed class SettingsStore : ISettingsStore
{
    private const string UserSource = "User";

    private readonly IDbContextFactory<GeoToolsDbContext> _factory;
    private readonly IMessageBus _bus;
    private readonly TimeProvider _clock;
    private readonly ILogger<SettingsStore> _logger;
    private readonly Snapshot _defaults;
    private Snapshot _current;

    public SettingsStore(
        IDbContextFactory<GeoToolsDbContext> factory,
        IOptions<MapOptions> map,
        IOptions<GeodataOptions> geodata,
        IOptions<FeaturesOptions> features,
        IOptions<WindowOptions> window,
        IMessageBus bus,
        TimeProvider clock,
        ILogger<SettingsStore> logger
    )
    {
        _factory = factory;
        _bus = bus;
        _clock = clock;
        _logger = logger;

        _defaults = new Snapshot(
            new MapSettings(map.Value.AdminLevels.Order().Distinct().ToArray()),
            new GeodataSettings(
                geodata.Value.ExtractAdminLevels.Order().Distinct().ToArray(),
                geodata.Value.SimplifyToleranceDegrees
            ),
            new Dictionary<FeatureFlag, bool>
            {
                [FeatureFlag.MapGrid] = features.Value.MapGrid,
                [FeatureFlag.MapCrosshair] = features.Value.MapCrosshair,
                [FeatureFlag.GeodataAutoPull] = features.Value.GeodataAutoPull,
            },
            WindowSize.FromOptions(window.Value.Width, window.Value.Height, window.Value.Maximized),
            0
        );
        _current = _defaults;
    }

    public MapSettings Map => Current.Map;

    public GeodataSettings Geodata => Current.Geodata;

    public WindowSize Window => Current.Window;

    public long Version => Current.Version;

    public event EventHandler? Changed;

    private Snapshot Current => Volatile.Read(ref _current);

    public bool IsEnabled(FeatureFlag flag) =>
        Current.Flags.TryGetValue(flag, out var enabled) && enabled;

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var settings = await context.Settings.AsNoTracking().ToListAsync(cancellationToken);
        var flags = await context.FeatureFlags.AsNoTracking().ToListAsync(cancellationToken);

        var mapLevels = ReadLevels(settings, SettingKeys.DisplayAdminLevels)
            ?? _defaults.Map.AdminLevels;
        var extractLevels = ReadLevels(settings, SettingKeys.ExtractAdminLevels)
            ?? _defaults.Geodata.ExtractAdminLevels;
        var tolerance = ReadDouble(settings, SettingKeys.SimplifyTolerance)
            ?? _defaults.Geodata.SimplifyToleranceDegrees;
        var window = ReadWindowSize(settings) ?? _defaults.Window;

        var flagValues = new Dictionary<FeatureFlag, bool>(_defaults.Flags);
        foreach (var flag in flags)
        {
            if (Enum.TryParse<FeatureFlag>(flag.Name, out var parsed) && Enum.IsDefined(parsed))
                flagValues[parsed] = flag.Enabled;
        }

        var next = new Snapshot(
            new MapSettings(mapLevels),
            new GeodataSettings(extractLevels, tolerance),
            flagValues,
            window,
            Current.Version + 1
        );
        Volatile.Write(ref _current, next);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<SettingsResult> SetDisplayAdminLevelsAsync(
        IReadOnlyList<int> levels,
        CancellationToken cancellationToken = default
    )
    {
        var validation = SettingsValidation.ValidateAdminLevels(levels, allowEmpty: true);
        if (!validation.Accepted)
            return validation;

        var normalized = Normalize(levels);
        var saved = await PersistAsync(
            SettingKeys.DisplayAdminLevels,
            string.Join(',', normalized),
            cancellationToken
        );
        if (!saved.Accepted)
            return saved;

        Update(current => current with { Map = new MapSettings(normalized) });
        await PublishAsync(
            new SettingsChanged(
                SettingKeys.DisplayAdminLevels,
                string.Join(',', normalized),
                _clock.GetUtcNow()
            ),
            cancellationToken
        );
        return SettingsResult.Ok;
    }

    public async Task<SettingsResult> SetExtractAdminLevelsAsync(
        IReadOnlyList<int> levels,
        CancellationToken cancellationToken = default
    )
    {
        var validation = SettingsValidation.ValidateAdminLevels(levels, allowEmpty: false);
        if (!validation.Accepted)
            return validation;

        var normalized = Normalize(levels);
        var saved = await PersistAsync(
            SettingKeys.ExtractAdminLevels,
            string.Join(',', normalized),
            cancellationToken
        );
        if (!saved.Accepted)
            return saved;

        Update(current =>
            current with
            {
                Geodata = current.Geodata with { ExtractAdminLevels = normalized },
            }
        );
        await PublishAsync(
            new SettingsChanged(
                SettingKeys.ExtractAdminLevels,
                string.Join(',', normalized),
                _clock.GetUtcNow()
            ),
            cancellationToken
        );
        return SettingsResult.Ok;
    }

    public async Task<SettingsResult> SetSimplifyToleranceAsync(
        double tolerance,
        CancellationToken cancellationToken = default
    )
    {
        var validation = SettingsValidation.ValidateTolerance(tolerance);
        if (!validation.Accepted)
            return validation;

        var saved = await PersistAsync(
            SettingKeys.SimplifyTolerance,
            tolerance.ToString("R", CultureInfo.InvariantCulture),
            cancellationToken
        );
        if (!saved.Accepted)
            return saved;

        Update(current =>
            current with
            {
                Geodata = current.Geodata with { SimplifyToleranceDegrees = tolerance },
            }
        );
        await PublishAsync(
            new SettingsChanged(
                SettingKeys.SimplifyTolerance,
                tolerance.ToString("R", CultureInfo.InvariantCulture),
                _clock.GetUtcNow()
            ),
            cancellationToken
        );
        return SettingsResult.Ok;
    }

    public async Task<SettingsResult> SetWindowSizeAsync(
        WindowSize size,
        CancellationToken cancellationToken = default
    )
    {
        var validation = SettingsValidation.ValidateWindowSize(size);
        if (!validation.Accepted)
            return validation;

        var saved = await PersistAsync(SettingKeys.WindowSize, size.Display, cancellationToken);
        if (!saved.Accepted)
            return saved;

        Update(current => current with { Window = size });
        await PublishAsync(
            new SettingsChanged(SettingKeys.WindowSize, size.Display, _clock.GetUtcNow()),
            cancellationToken
        );
        return SettingsResult.Ok;
    }

    public async Task<SettingsResult> SetFlagAsync(
        FeatureFlag flag,
        bool enabled,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken);
            var row = await context.FeatureFlags.FindAsync([flag.ToString()], cancellationToken);
            if (row is null)
            {
                row = new FeatureFlagRow { Name = flag.ToString() };
                context.FeatureFlags.Add(row);
            }

            row.Enabled = enabled;
            row.UpdatedAt = StoreMappings.ToUnixMilliseconds(_clock.GetUtcNow());
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not save feature flag {Flag}.", flag);
            return SettingsResult.Rejected("Could not save the feature flag.");
        }

        Update(current =>
        {
            var flags = new Dictionary<FeatureFlag, bool>(current.Flags) { [flag] = enabled };
            return current with { Flags = flags };
        });

        await PublishAsync(
            new FeatureFlagChanged(flag, enabled, _clock.GetUtcNow()),
            cancellationToken
        );
        return SettingsResult.Ok;
    }

    public async Task<SettingsResult> ResetToConfigurationAsync(
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken);
            await context.Settings.ExecuteDeleteAsync(cancellationToken);
            await context.FeatureFlags.ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not reset settings.");
            return SettingsResult.Rejected("Could not reset settings.");
        }

        await ReloadAsync(cancellationToken);
        await PublishAsync(
            new SettingsChanged("all", "reset", _clock.GetUtcNow()),
            cancellationToken
        );
        return SettingsResult.Ok;
    }

    private async Task<SettingsResult> PersistAsync(
        string key,
        string value,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken);
            var row = await context.Settings.FindAsync([key], cancellationToken);
            if (row is null)
            {
                row = new SettingRow { Key = key };
                context.Settings.Add(row);
            }

            row.Value = value;
            row.Source = UserSource;
            row.UpdatedAt = StoreMappings.ToUnixMilliseconds(_clock.GetUtcNow());
            await context.SaveChangesAsync(cancellationToken);
            return SettingsResult.Ok;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not save setting {Key}.", key);
            return SettingsResult.Rejected("Could not save the setting.");
        }
    }

    private async Task PublishAsync<T>(T message, CancellationToken cancellationToken)
    {
        try
        {
            await _bus.PublishAsync(message, cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not publish a settings change.");
        }
    }

    private void Update(Func<Snapshot, Snapshot> change)
    {
        while (true)
        {
            var current = Current;
            var next = change(current) with { Version = current.Version + 1 };
            if (ReferenceEquals(Interlocked.CompareExchange(ref _current, next, current), current))
            {
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }
        }
    }

    private int[]? ReadLevels(IReadOnlyList<SettingRow> settings, string key)
    {
        var value = settings.FirstOrDefault(row => row.Key == key)?.Value;
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return Normalize(StoreMappings.ParseLevels(value));
        }
        catch (InvalidDataException exception)
        {
            _logger.LogWarning(exception, "Ignoring invalid {Key} setting.", key);
            return null;
        }
    }

    private double? ReadDouble(IReadOnlyList<SettingRow> settings, string key)
    {
        var value = settings.FirstOrDefault(row => row.Key == key)?.Value;
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed
        )
            ? parsed
            : null;
    }

    private WindowSize? ReadWindowSize(IReadOnlyList<SettingRow> settings)
    {
        var value = settings.FirstOrDefault(row => row.Key == SettingKeys.WindowSize)?.Value;
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (WindowSize.TryParse(value, out var parsed))
            return parsed;

        _logger.LogWarning("Ignoring invalid {Key} setting.", SettingKeys.WindowSize);
        return null;
    }

    private static int[] Normalize(IReadOnlyList<int> levels) =>
        levels.Where(level => level > 0).Distinct().Order().ToArray();

    private sealed record Snapshot(
        MapSettings Map,
        GeodataSettings Geodata,
        IReadOnlyDictionary<FeatureFlag, bool> Flags,
        WindowSize Window,
        long Version
    );
}

internal static class SettingKeys
{
    public const string DisplayAdminLevels = "map.adminLevels";
    public const string ExtractAdminLevels = "geodata.extractAdminLevels";
    public const string SimplifyTolerance = "geodata.simplifyTolerance";
    public const string WindowSize = "window.size";
}
