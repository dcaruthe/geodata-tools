using GeoTools.Api.Contracts;
using GeoTools.Shared.Application.Geodata;
using GeoTools.Shared.Application.Link;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain.Geospatial;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace GeoTools.Api.Facade;

// One place where the API reads state and issues commands. REST, GraphQL, and MCP are thin
// adapters over this, so a behavior fixed here is fixed on all three surfaces.
public sealed class GeoToolsFacade(
    ILauncherLink link,
    ISettingsStore settings,
    IJobStore jobs,
    IArtifactStore artifacts,
    GeodataQueryService geodata,
    IOptions<GeodataOptions> geodataOptions,
    TimeProvider clock,
    ILogger<GeoToolsFacade> logger
)
{
    public async Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken)
    {
        var state = await GetStateAsync(cancellationToken);
        return new HealthResponse(
            "ok",
            state.LauncherStatus,
            state.Local.DatabaseAvailable,
            clock.GetUtcNow()
        );
    }

    public async Task<StateResponse> GetStateAsync(CancellationToken cancellationToken)
    {
        AppStateDto? launcher = null;
        string? reason = null;
        try
        {
            launcher = await link.GetStateAsync(cancellationToken);
        }
        catch (LinkUnavailableException exception)
        {
            reason = exception.Message;
        }

        var local = await BuildLocalAsync(cancellationToken);
        return new StateResponse(
            launcher,
            launcher is null ? "offline" : "connected",
            reason,
            local
        );
    }

    public Task<LogPageDto> GetLogsAsync(
        string? level,
        int limit,
        long? after,
        CancellationToken cancellationToken
    ) => link.GetLogsAsync(level, limit, after, cancellationToken);

    public Task<MaintenanceDto> GetMaintenanceAsync(CancellationToken cancellationToken) =>
        link.GetMaintenanceAsync(cancellationToken);

    public Task<ScreenshotDto> GetScreenshotAsync(
        int? width,
        int? height,
        CancellationToken cancellationToken
    ) => link.ScreenshotAsync(width, height, ScreenshotMode.Scene, cancellationToken);

    public async Task<ScreenshotInfoDto> GetScreenshotInfoAsync(
        int? width,
        int? height,
        CancellationToken cancellationToken
    )
    {
        var screenshot = await GetScreenshotAsync(width, height, cancellationToken);
        return new ScreenshotInfoDto(
            Convert.ToBase64String(screenshot.Png),
            screenshot.Width,
            screenshot.Height,
            screenshot.View,
            screenshot.CapturedAt
        );
    }

    public Task<CommandResultDto> NavigateAsync(ViewId view, CancellationToken cancellationToken) =>
        link.NavigateAsync(view, cancellationToken);

    public Task<CommandResultDto> PressKeysAsync(
        IReadOnlyList<WindowKey> keys,
        CancellationToken cancellationToken
    ) => link.PressKeysAsync(keys, cancellationToken);

    public Task<CommandResultDto> MaintenanceAsync(
        MaintenanceOperation operation,
        string? rowId,
        int direction,
        bool confirm,
        CancellationToken cancellationToken
    ) => link.MaintenanceAsync(operation, rowId, direction, confirm, cancellationToken);

    public Task<CommandResultDto> PipelineAsync(
        PipelineAction action,
        bool confirm,
        CancellationToken cancellationToken
    ) => link.PipelineAsync(action, confirm, cancellationToken);

    public Task<CommandResultDto> SetDisplayLevelsAsync(
        IReadOnlyList<int> levels,
        CancellationToken cancellationToken
    ) => link.SetDisplayLevelsAsync(levels, cancellationToken);

    public Task<CommandResultDto> SetExtractLevelsAsync(
        IReadOnlyList<int> levels,
        CancellationToken cancellationToken
    ) => link.SetExtractLevelsAsync(levels, cancellationToken);

    public Task<CommandResultDto> SetToleranceAsync(
        double tolerance,
        CancellationToken cancellationToken
    ) => link.SetToleranceAsync(tolerance, cancellationToken);

    public Task<CommandResultDto> SetFeatureFlagAsync(
        string flag,
        bool enabled,
        CancellationToken cancellationToken
    ) => link.SetFeatureFlagAsync(flag, enabled, cancellationToken);

    public Task<CommandResultDto> ResetSettingsAsync(CancellationToken cancellationToken) =>
        link.ResetSettingsAsync(cancellationToken);

    public SettingsViewDto GetSettings() =>
        new(
            settings.Map.AdminLevels.ToArray(),
            settings.Geodata.ExtractAdminLevels.ToArray(),
            settings.Geodata.SimplifyToleranceDegrees,
            new[]
            {
                new FlagDto(nameof(FeatureFlag.MapGrid), settings.IsEnabled(FeatureFlag.MapGrid)),
                new FlagDto(
                    nameof(FeatureFlag.MapCrosshair),
                    settings.IsEnabled(FeatureFlag.MapCrosshair)
                ),
                new FlagDto(
                    nameof(FeatureFlag.GeodataAutoPull),
                    settings.IsEnabled(FeatureFlag.GeodataAutoPull)
                ),
            }
        );

    public async Task<RunDetailDto?> GetLatestRunAsync(CancellationToken cancellationToken)
    {
        var run = await jobs.FindLatestRunAsync(cancellationToken);
        if (run is null)
            return null;

        var steps = await jobs.ListStepsAsync(run.Id, cancellationToken);
        return new RunDetailDto(
            ToDto(run),
            steps
                .Select(step => new StepDto(
                    step.Key.ToString(),
                    step.Stage,
                    step.Label,
                    step.Status.ToString(),
                    step.Attempts,
                    step.Progress.Fraction,
                    step.Error
                ))
                .ToArray()
        );
    }

    public async Task<IReadOnlyList<ArtifactDto>> GetArtifactsAsync(
        string? region,
        CancellationToken cancellationToken
    )
    {
        var records = await artifacts.ListAsync(cancellationToken: cancellationToken);
        return records
            .Where(record =>
                string.IsNullOrWhiteSpace(region)
                || string.Equals(record.Region, region, StringComparison.OrdinalIgnoreCase)
            )
            .Select(record => new ArtifactDto(
                record.Key.ToString(),
                record.Kind.ToString(),
                record.Region,
                record.Path,
                record.SizeBytes,
                record.ModifiedAt,
                record.Status.ToString(),
                record.AdminLevels.ToArray()
            ))
            .ToArray();
    }

    public async Task<RegionsResponse> GetRegionsAsync(CancellationToken cancellationToken)
    {
        var records = await TryListArtifactsAsync(cancellationToken) ?? [];
        var (geometry, levels) = await geodata.GetGeometryAsync(cancellationToken);

        var byRegion = records
            .Where(record => record.Region is not null)
            .GroupBy(record => record.Region!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        var regions = geodataOptions
            .Value.Regions.Select(name =>
            {
                var regionRecords = byRegion.GetValueOrDefault(name) ?? [];
                var pbf = Array.Find(regionRecords, record => record.Kind == ArtifactKind.Pbf);
                var borders = Array.Find(regionRecords, record =>
                    record.Kind == ArtifactKind.Borders
                );
                return new RegionDto(
                    name,
                    pbf is not null,
                    borders is not null,
                    pbf?.SizeBytes ?? 0,
                    borders?.SizeBytes ?? 0,
                    (borders ?? pbf)?.Status.ToString()
                );
            })
            .ToArray();

        return new RegionsResponse(
            regions,
            levels,
            geometry.Bounds,
            geometry.Polylines.Count,
            geometry.Polylines.Sum(polyline => polyline.Points.Count),
            geometry.Polylines.Count > 0
        );
    }

    public Task<BorderQueryResponse> QueryBordersAsync(
        GeoBounds? bounds,
        IReadOnlyList<int>? adminLevels,
        int limit,
        string format,
        CancellationToken cancellationToken
    ) => geodata.QueryAsync(bounds, adminLevels, limit, format, cancellationToken);

    private async Task<LocalStateDto> BuildLocalAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ArtifactRecord>? records = null;
        RunDto? latestRun = null;
        string? error = null;
        try
        {
            records = await artifacts.ListAsync(cancellationToken: cancellationToken);
            if (await jobs.FindLatestRunAsync(cancellationToken) is { } run)
                latestRun = ToDto(run);
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
        {
            error = exception.Message;
            logger.LogWarning(exception, "The state database is not readable.");
        }

        var levels = records is null
            ? settings.Geodata.ExtractAdminLevels.ToArray()
            : records
                .SelectMany(record => record.AdminLevels)
                .Where(level => level > 0)
                .Distinct()
                .Order()
                .ToArray();

        return new LocalStateDto(
            records is not null,
            error,
            GetSettings(),
            latestRun,
            records?.Count ?? 0,
            geodata.BorderFileCount,
            levels
        );
    }

    private async Task<IReadOnlyList<ArtifactRecord>?> TryListArtifactsAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await artifacts.ListAsync(cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
        {
            logger.LogWarning(exception, "The artifact table is not readable.");
            return null;
        }
    }

    private static RunDto ToDto(RunRecord run) =>
        new(
            run.Id.ToString(),
            run.Reason,
            run.Status.ToString(),
            run.StartedAt,
            run.FinishedAt,
            run.Error
        );
}
