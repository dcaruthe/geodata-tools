using System.Diagnostics;
using GeoTools.Shared.Application.Diagnostics;
using GeoTools.Shared.Application.Link;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Storage;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Infrastructure.Link;
using GeoTools.Shared.Presentation.Options;
using GeoTools.Shared.Presentation.Views;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Presentation.Link;

// Serves the API. Reads answer from thread-safe snapshots. Everything that touches the window
// goes through the inbox so the render thread keeps sole ownership of navigation and input.
internal sealed class LauncherLinkService(
    IOptions<LinkOptions> options,
    IOptions<StorageOptions> storage,
    IOptions<GeodataOptions> geodata,
    IOptions<WindowOptions> window,
    TimeProvider clock,
    WindowCommandInbox inbox,
    IProgressStore progress,
    IJobStore jobs,
    ISettingsStore settings,
    MaintenanceController maintenance,
    INavigationMediator mediator,
    ILogStore logs,
    ILoggerFactory loggerFactory,
    ILogger<LauncherLinkService> logger
) : IHostedService, IAsyncDisposable
{
    private LauncherLinkServer? _server;
    private DateTimeOffset _startedAt = DateTimeOffset.MinValue;

    internal bool HasServer => _server is not null;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("The launcher link is disabled.");
            return;
        }

        try
        {
            var key = LinkKey.EnsureCreated(options.Value.KeyPath);
            var server = new LauncherLinkServer(
                options.Value.SocketPath,
                key,
                TimeSpan.FromSeconds(options.Value.HandshakeTimeoutSeconds),
                HandleAsync,
                clock,
                loggerFactory.CreateLogger<LauncherLinkServer>()
            );
            _startedAt = clock.GetUtcNow();
            await server.StartAsync(cancellationToken);
            _server = server;
        }
        catch (Exception exception)
        {
            _server = null;
            logger.LogError(
                exception,
                "The launcher link could not start at {SocketPath}. The window still runs.",
                options.Value.SocketPath
            );
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_server is not null)
            await _server.StopAsync(cancellationToken);

        inbox.Close();
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is not null)
            await _server.DisposeAsync();
    }

    private async Task<LinkResponse> HandleAsync(
        LinkRequest request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return request switch
            {
                GetStateRequest state => LinkResponse.Success(state.Id, BuildState()),
                GetLogsRequest page => LinkResponse.Success(
                    page.Id,
                    logs.Read(page.Level, page.Limit, page.After)
                ),
                GetMaintenanceRequest maintenanceRequest => LinkResponse.Success(
                    maintenanceRequest.Id,
                    BuildMaintenance()
                ),
                GetSettingsRequest settingsRequest => LinkResponse.Success(
                    settingsRequest.Id,
                    BuildSettings()
                ),
                SetDisplayLevelsRequest levels => Command(
                    levels.Id,
                    await settings.SetDisplayAdminLevelsAsync(levels.Levels, cancellationToken)
                ),
                SetExtractLevelsRequest levels => Command(
                    levels.Id,
                    await settings.SetExtractAdminLevelsAsync(levels.Levels, cancellationToken)
                ),
                SetToleranceRequest tolerance => Command(
                    tolerance.Id,
                    await settings.SetSimplifyToleranceAsync(tolerance.Tolerance, cancellationToken)
                ),
                SetFeatureFlagRequest flag => await SetFlagAsync(flag, cancellationToken),
                ResetSettingsRequest reset => Command(
                    reset.Id,
                    await settings.ResetToConfigurationAsync(cancellationToken)
                ),
                _ => await inbox.EnqueueAsync(
                    request,
                    TimeSpan.FromSeconds(options.Value.CommandTimeoutSeconds),
                    cancellationToken
                ),
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "A link request failed.");
            return LinkResponse.Failure(
                request.Id,
                new LinkError(LinkError.Internal, exception.Message)
            );
        }
    }

    private async Task<LinkResponse> SetFlagAsync(
        SetFeatureFlagRequest request,
        CancellationToken cancellationToken
    )
    {
        if (
            !Enum.TryParse<FeatureFlag>(request.Flag, ignoreCase: true, out var flag)
            || !Enum.IsDefined(flag)
        )
            return LinkResponse.Failure(
                request.Id,
                new LinkError(LinkError.InvalidRequest, $"Unknown feature flag {request.Flag}.")
            );

        return Command(
            request.Id,
            await settings.SetFlagAsync(flag, request.Enabled, cancellationToken)
        );
    }

    private LinkResponse Command(string id, SettingsResult result) =>
        LinkResponse.Success(
            id,
            new CommandResultDto(
                result.Accepted,
                result.Accepted ? "Saved." : string.Empty,
                mediator.ActiveView,
                result.Error
            )
        );

    private AppStateDto BuildState()
    {
        var process = Process.GetCurrentProcess();
        var startedAt = ReadStartTime(process);
        var current = progress.Current;
        var run = jobs.LatestRun;

        return new AppStateDto(
            typeof(AppStateDto).Assembly.GetName().Version?.ToString() ?? "unknown",
            LinkProtocol.Version,
            clock.GetUtcNow(),
            startedAt,
            (long)Math.Max(0, (clock.GetUtcNow() - startedAt).TotalSeconds),
            mediator.ActiveView,
            window.Value.Title,
            window.Value.Width,
            window.Value.Height,
            new ProgressDto(
                current.Phase.ToString(),
                current.Stage,
                current.Label,
                current.Detail,
                current.Fraction,
                current.Progress.EtaSeconds,
                current.Error,
                current.UpdatedAt
            ),
            run is null
                ? null
                : new RunDto(
                    run.Id.ToString(),
                    run.Reason,
                    run.Status.ToString(),
                    run.StartedAt,
                    run.FinishedAt,
                    run.Error
                ),
            new LinkStatusDto(
                _server is not null,
                _server?.ActiveConnections ?? 0,
                _server?.Rotations ?? 0,
                options.Value.RotateSeconds,
                _server?.LastRotatedAt
            ),
            new DiagnosticsDto(
                process.Id,
                GC.GetTotalMemory(forceFullCollection: false),
                process.Threads.Count,
                RepositoryPaths.Resolve(storage.Value.DatabasePath),
                RepositoryPaths.Resolve(geodata.Value.RootPath),
                LinkKey.ResolvePath(options.Value.SocketPath),
                LinkKey.ResolvePath(options.Value.KeyPath)
            )
        );
    }

    private MaintenanceDto BuildMaintenance()
    {
        var snapshot = maintenance.Snapshot;
        return new MaintenanceDto(
            snapshot
                .Rows.Select(row => new MaintenanceRowDto(
                    row.Id,
                    row.Label,
                    row.Value,
                    row.Kind.ToString(),
                    row.Selectable,
                    row.Enabled,
                    row.Destructive
                ))
                .ToArray(),
            snapshot.Selected?.Id,
            snapshot.Status,
            snapshot.PendingConfirmation
        );
    }

    private SettingsDto BuildSettings() =>
        new(
            settings.Map.AdminLevels.ToArray(),
            settings.Geodata.ExtractAdminLevels.ToArray(),
            settings.Geodata.SimplifyToleranceDegrees,
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                [nameof(FeatureFlag.MapGrid)] = settings.IsEnabled(FeatureFlag.MapGrid),
                [nameof(FeatureFlag.MapCrosshair)] = settings.IsEnabled(FeatureFlag.MapCrosshair),
                [nameof(FeatureFlag.GeodataAutoPull)] = settings.IsEnabled(
                    FeatureFlag.GeodataAutoPull
                ),
            }
        );

    private DateTimeOffset ReadStartTime(Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime).ToUniversalTime();
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or NotSupportedException)
        {
            return _startedAt == DateTimeOffset.MinValue ? clock.GetUtcNow() : _startedAt;
        }
    }
}
