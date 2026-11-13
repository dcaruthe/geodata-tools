using GeoTools.Shared.Application.Geodata;
using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Presentation.Views;

internal sealed record MaintenanceCommandResult(
    bool Accepted,
    string Status,
    string? PendingConfirmation,
    string? Error
);

internal sealed class MaintenanceController(
    ISettingsStore settings,
    IJobStore jobs,
    IArtifactStore artifacts,
    IProgressStore progress,
    IBorderGeometryStore geometry,
    IMessageBus bus,
    IOptions<GeodataOptions> options,
    INavigationMediator mediator,
    TimeProvider clock,
    ILogger<MaintenanceController> logger
) : IMessageSubscription
{
    private static readonly int[][] DisplayPresets =
    [
        [],
        [2],
        [2, 4],
        [2, 4, 6],
    ];
    private static readonly int[][] ExtractPresets =
    [
        [2],
        [2, 4],
        [2, 4, 6],
    ];
    private static readonly double[] TolerancePresets = [0.001, 0.002, 0.005, 0.01, 0.02];

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private MaintenanceSnapshot _snapshot = MaintenanceSnapshot.Empty;

    public MaintenanceSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public event EventHandler? Changed;

    // The link adapter drives the same state machine as the keyboard. Selection is by row id so
    // an agent never has to count key presses.
    public MaintenanceCommandResult SelectRow(string rowId)
    {
        var row = Find(rowId);
        if (row is null)
            return Failed($"No maintenance row {rowId}.");

        if (!row.Selectable)
            return Failed($"{row.Label} is not selectable.");

        Jump(MaintenanceRows.IndexOf(Snapshot.Rows, rowId));
        return new MaintenanceCommandResult(true, Snapshot.Status, null, null);
    }

    public MaintenanceCommandResult AdjustRow(string rowId, int direction)
    {
        var row = Find(rowId);
        if (row is null)
            return Failed($"No maintenance row {rowId}.");

        if (!row.Enabled)
            return Failed($"{row.Label} is disabled.");

        if (!row.Selectable || row.Kind == MaintenanceRowKind.Action)
            return Failed($"{row.Label} is not adjustable.");

        Jump(MaintenanceRows.IndexOf(Snapshot.Rows, rowId));
        Adjust(direction);
        return new MaintenanceCommandResult(true, Snapshot.Status, null, null);
    }

    public MaintenanceCommandResult ActivateRow(string rowId, bool confirm)
    {
        var row = Find(rowId);
        if (row is null)
            return Failed($"No maintenance row {rowId}.");

        if (!row.Enabled)
            return Failed($"{row.Label} is disabled.");

        if (!row.Selectable || row.Kind == MaintenanceRowKind.Info)
            return Failed($"{row.Label} is not an action.");

        Jump(MaintenanceRows.IndexOf(Snapshot.Rows, rowId));
        Activate();

        if (!row.Destructive)
            return new MaintenanceCommandResult(true, Snapshot.Status, null, null);

        // A destructive row asks for a second press. The link caller sends confirm instead.
        if (!confirm)
            return new MaintenanceCommandResult(
                true,
                Snapshot.Status,
                Snapshot.PendingConfirmation,
                null
            );

        Activate();
        return new MaintenanceCommandResult(true, Snapshot.Status, null, null);
    }

    public MaintenanceCommandResult CancelRun()
    {
        _ = PublishCancelAsync();
        return new MaintenanceCommandResult(true, "Cancellation requested.", null, null);
    }

    private MaintenanceRow? Find(string rowId) =>
        Snapshot.Rows.FirstOrDefault(row => string.Equals(row.Id, rowId, StringComparison.Ordinal));

    private MaintenanceCommandResult Failed(string error) =>
        new(false, Snapshot.Status, Snapshot.PendingConfirmation, error);

    public async Task AttachAsync(
        IMessageBus busInstance,
        CancellationToken cancellationToken = default
    )
    {
        mediator.ViewChanged += OnViewChanged;

        await busInstance.SubscribeAsync(
            "maintenance",
            [
                MessageTopics.BootstrapStepChanged,
                MessageTopics.BootstrapRunFinished,
                MessageTopics.BordersAvailable,
                MessageTopics.SettingsChanged,
                MessageTopics.FeatureFlagChanged,
            ],
            async (_, token) =>
            {
                if (mediator.ActiveView == ViewId.Maintenance)
                    await RefreshSafeAsync(token);
            },
            startAt: null,
            cancellationToken
        );

        await RefreshSafeAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var records = await artifacts.ListAsync(cancellationToken: cancellationToken);
            var inputs = BuildInputs(records);
            var rows = MaintenanceRows.Build(inputs);
            var current = Snapshot;
            var selectedId = current.Selected?.Id;
            var index = selectedId is null
                ? MaintenanceRows.FirstSelectable(rows)
                : MaintenanceRows.IndexOf(rows, selectedId);
            var pending =
                current.PendingConfirmation is not null
                && rows.Any(row => row.Id == current.PendingConfirmation)
                    ? current.PendingConfirmation
                    : null;

            Update(snapshot =>
                snapshot with
                {
                    Rows = rows,
                    SelectedIndex = index,
                    PendingConfirmation = pending,
                }
            );
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public bool Handle(ViewKey key)
    {
        if (key != ViewKey.Enter && Snapshot.PendingConfirmation is not null)
            Update(snapshot => snapshot with { PendingConfirmation = null });

        switch (key)
        {
            case ViewKey.Up:
                Move(-1);
                return true;
            case ViewKey.Down:
                Move(1);
                return true;
            case ViewKey.Left:
                Adjust(-1);
                return true;
            case ViewKey.Right:
                Adjust(1);
                return true;
            case ViewKey.Enter:
                return Activate();
            case ViewKey.Backspace:
                _ = PublishCancelAsync();
                return true;
            case ViewKey.Home:
                Jump(MaintenanceRows.FirstSelectable(Snapshot.Rows));
                return true;
            case ViewKey.End:
                Jump(MaintenanceRows.LastSelectable(Snapshot.Rows));
                return true;
            default:
                return false;
        }
    }

    private MaintenanceInputs BuildInputs(IReadOnlyList<ArtifactRecord> records)
    {
        var flags = new Dictionary<FeatureFlag, bool>
        {
            [FeatureFlag.MapGrid] = settings.IsEnabled(FeatureFlag.MapGrid),
            [FeatureFlag.MapCrosshair] = settings.IsEnabled(FeatureFlag.MapCrosshair),
            [FeatureFlag.GeodataAutoPull] = settings.IsEnabled(FeatureFlag.GeodataAutoPull),
        };

        var byRegion = records
            .Where(record => record.Region is not null)
            .GroupBy(record => record.Region!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var summaries = new List<MaintenanceArtifactSummary>();
        foreach (var region in options.Value.Regions)
        {
            var regionRecords = byRegion.GetValueOrDefault(region) ?? [];
            var pbf = Array.Find(regionRecords, record => record.Kind == ArtifactKind.Pbf);
            var borders = Array.Find(regionRecords, record => record.Kind == ArtifactKind.Borders);
            summaries.Add(
                new MaintenanceArtifactSummary(
                    region,
                    pbf is null ? null : ProgressAggregator.FormatBytes(pbf.SizeBytes),
                    borders is null ? null : ProgressAggregator.FormatBytes(borders.SizeBytes)
                )
            );
        }

        return new MaintenanceInputs(
            progress.Current,
            jobs.LatestRun,
            settings.Map,
            settings.Geodata,
            flags,
            geometry.AvailableAdminLevels,
            summaries,
            progress.Current.Phase == BootstrapPhase.Running
        );
    }

    private void OnViewChanged(object? sender, ViewChangedEventArgs args)
    {
        if (args.Current == ViewId.Maintenance)
        {
            _ = RefreshSafeAsync(CancellationToken.None);
            return;
        }

        if (Snapshot.PendingConfirmation is not null)
            Update(snapshot => snapshot with { PendingConfirmation = null });
    }

    private async Task RefreshSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not refresh the maintenance view.");
        }
    }

    private void Jump(int index) =>
        Update(current =>
            current with
            {
                SelectedIndex = index,
                PendingConfirmation = null,
                Status = string.Empty,
            }
        );

    private void Move(int direction)
    {
        var snapshot = Snapshot;
        var index = MaintenanceRows.Move(snapshot.Rows, snapshot.SelectedIndex, direction);
        Update(current =>
            current with
            {
                SelectedIndex = index,
                PendingConfirmation = null,
                Status = string.Empty,
            }
        );
    }

    private void Adjust(int direction)
    {
        var row = Snapshot.Selected;
        if (row is null || !row.Enabled)
            return;

        switch (row.Id)
        {
            case "displayLevels":
                var display = NextPreset(DisplayPresets, settings.Map.AdminLevels, direction);
                Apply(() => settings.SetDisplayAdminLevelsAsync(display));
                break;
            case "extractLevels":
                var extract = NextPreset(
                    ExtractPresets,
                    settings.Geodata.ExtractAdminLevels,
                    direction
                );
                Apply(() => settings.SetExtractAdminLevelsAsync(extract));
                break;
            case "tolerance":
                var tolerance = NextTolerance(settings.Geodata.SimplifyToleranceDegrees, direction);
                Apply(async () =>
                {
                    var result = await settings.SetSimplifyToleranceAsync(tolerance);
                    if (result.Accepted)
                        await PublishRebuildAsync(
                            RebuildScope.GeometryCache,
                            force: false,
                            "Geometry cache rebuild"
                        );
                    return result;
                });
                break;
            default:
                if (row.Id.StartsWith("flag:", StringComparison.Ordinal))
                    ToggleFlag(row.Id["flag:".Length..]);
                break;
        }
    }

    private bool Activate()
    {
        var snapshot = Snapshot;
        var row = snapshot.Selected;
        if (row is null || !row.Enabled)
            return true;

        if (row.Kind == MaintenanceRowKind.Action)
        {
            if (row.Destructive && snapshot.PendingConfirmation != row.Id)
            {
                Update(current =>
                    current with
                    {
                        PendingConfirmation = row.Id,
                        Status = $"Press Enter again to {row.Label.ToLowerInvariant()}.",
                    }
                );
                return true;
            }

            Update(current =>
                current with
                {
                    PendingConfirmation = null,
                    Status = $"{row.Label}...",
                }
            );
            Execute(row);
            return true;
        }

        if (row.Kind is MaintenanceRowKind.Choice or MaintenanceRowKind.Toggle)
        {
            Adjust(1);
            return true;
        }

        return false;
    }

    private void Execute(MaintenanceRow row)
    {
        switch (row.Id)
        {
            case "action:check":
                _ = PublishRebuildAsync(RebuildScope.Pull, force: false, "Update check");
                break;
            case "action:repull":
                _ = PublishRebuildAsync(RebuildScope.Pull, force: true, "Repull");
                break;
            case "action:extract":
                _ = PublishRebuildAsync(RebuildScope.Extract, force: true, "Border rebuild");
                break;
            case "action:cache":
                _ = PublishRebuildAsync(
                    RebuildScope.GeometryCache,
                    force: false,
                    "Geometry cache rebuild"
                );
                break;
            case "action:cancel":
                _ = PublishCancelAsync();
                break;
            case "action:retry":
                _ = RetrySubscriptionsAsync();
                break;
            case "action:reset":
                Apply(() => settings.ResetToConfigurationAsync());
                break;
        }
    }

    private void ToggleFlag(string name)
    {
        if (!Enum.TryParse<FeatureFlag>(name, out var flag) || !Enum.IsDefined(flag))
            return;

        var enabled = !settings.IsEnabled(flag);
        Apply(() => settings.SetFlagAsync(flag, enabled));
    }

    private void Apply(Func<Task<SettingsResult>> action) => _ = ApplyAsync(action);

    private async Task ApplyAsync(Func<Task<SettingsResult>> action)
    {
        try
        {
            var result = await action();
            SetStatus(result.Accepted ? "Saved." : result.Error ?? "Rejected.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not apply a setting.");
            SetStatus($"Could not save: {exception.Message}");
        }

        await RefreshSafeAsync(CancellationToken.None);
    }

    private async Task PublishRebuildAsync(RebuildScope scope, bool force, string label)
    {
        try
        {
            await bus.PublishAsync(
                new RebuildRequested(
                    Guid.NewGuid().ToString("N"),
                    scope,
                    force,
                    null,
                    clock.GetUtcNow()
                )
            );
            SetStatus($"{label} queued.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not queue {Label}.", label);
            SetStatus($"Could not queue: {exception.Message}");
        }
    }

    private async Task PublishCancelAsync()
    {
        try
        {
            await bus.PublishAsync(
                new CancelRequested(Guid.NewGuid().ToString("N"), clock.GetUtcNow())
            );
            SetStatus("Cancellation requested.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not cancel the run.");
            SetStatus($"Could not cancel: {exception.Message}");
        }
    }

    private async Task RetrySubscriptionsAsync()
    {
        try
        {
            await bus.ResetSubscriptionAsync("bootstrap-projector");
            await bus.ResetSubscriptionAsync("geodata-worker");
            SetStatus("Failed events will be retried.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not reset the subscriptions.");
            SetStatus($"Could not retry: {exception.Message}");
        }
    }

    private void SetStatus(string status) => Update(snapshot => snapshot with { Status = status });

    private void Update(Func<MaintenanceSnapshot, MaintenanceSnapshot> change)
    {
        while (true)
        {
            var current = Snapshot;
            var next = change(current) with { Version = current.Version + 1 };
            if (ReferenceEquals(Interlocked.CompareExchange(ref _snapshot, next, current), current))
            {
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }
        }
    }

    private static int[] NextPreset(int[][] presets, IReadOnlyList<int> current, int direction)
    {
        var normalized = current.Where(level => level > 0).Distinct().Order().ToArray();
        var index = Array.FindIndex(presets, preset => preset.SequenceEqual(normalized));
        if (index < 0)
            index = 0;

        return presets[(index + direction + presets.Length) % presets.Length];
    }

    private static double NextTolerance(double current, int direction)
    {
        var index = Array.FindIndex(TolerancePresets, value => Math.Abs(value - current) < 1e-9);
        if (index < 0)
            index = 1;

        var next = Math.Clamp(index + direction, 0, TolerancePresets.Length - 1);
        return TolerancePresets[next];
    }
}
