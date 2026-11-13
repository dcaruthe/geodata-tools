using System.ComponentModel;
using GeoTools.Api.Contracts;
using GeoTools.Api.Facade;
using GeoTools.Shared.Application.Link;
using GeoTools.Shared.Application.Views;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GeoTools.Api.Mcp;

[McpServerToolType]
public sealed class GeoToolsTools(GeoToolsFacade facade)
{
    [McpServerTool(Name = "state", ReadOnly = true)]
    [Description(
        "Launcher state and local database state. Includes the active view, bootstrap phase and progress, the latest run, whether the launcher link is connected, and the resolved paths."
    )]
    public Task<StateResponse> StateAsync(CancellationToken cancellationToken) =>
        facade.GetStateAsync(cancellationToken);

    [McpServerTool(Name = "logs", ReadOnly = true)]
    [Description(
        "Recent log entries from the running launcher. Use level to keep warnings and errors only. Use after with a previous nextSequence to poll for new lines."
    )]
    public Task<LogPageDto> LogsAsync(
        [Description("Minimum level: Trace, Debug, Information, Warning, Error, Critical.")]
            string? level = null,
        [Description("Maximum entries to return, 1 to 500.")] int limit = 100,
        [Description("Return entries after this sequence number.")] long? after = null,
        CancellationToken cancellationToken = default
    ) => facade.GetLogsAsync(level, limit, after, cancellationToken);

    [McpServerTool(Name = "screenshot", ReadOnly = true)]
    [Description(
        "Renders the launcher's active view to a PNG and returns it as an image. Pass width and height to capture at a different size than the window."
    )]
    public async Task<IEnumerable<ContentBlock>> ScreenshotAsync(
        [Description("Capture width in pixels, 64 to 4096.")] int? width = null,
        [Description("Capture height in pixels, 64 to 4096.")] int? height = null,
        CancellationToken cancellationToken = default
    )
    {
        var screenshot = await facade.GetScreenshotAsync(width, height, cancellationToken);
        return
        [
            ImageContentBlock.FromBytes(screenshot.Png, "image/png"),
            new TextContentBlock
            {
                Text =
                    $"{screenshot.Width}x{screenshot.Height} of {screenshot.View} captured at {screenshot.CapturedAt:O}.",
            },
        ];
    }

    [McpServerTool(Name = "navigate", Idempotent = true)]
    [Description("Switches the launcher window to a view: Loading, Map, or Maintenance.")]
    public Task<CommandResultDto> NavigateAsync(
        [Description("The view to show.")] ViewId view,
        CancellationToken cancellationToken = default
    ) => facade.NavigateAsync(view, cancellationToken);

    [McpServerTool(Name = "press_keys", Idempotent = true)]
    [Description(
        "Sends keys to the active view, the same keys a person can press: Up, Down, Left, Right, Enter, Backspace, Home, End."
    )]
    public Task<CommandResultDto> PressKeysAsync(
        [Description("Keys in order.")] WindowKey[] keys,
        CancellationToken cancellationToken = default
    ) => facade.PressKeysAsync(keys, cancellationToken);

    [McpServerTool(Name = "maintenance", ReadOnly = true)]
    [Description(
        "The maintenance screen rows with their ids, values, and selection. Use the row ids with maintenance_select, maintenance_adjust, and maintenance_activate."
    )]
    public Task<MaintenanceDto> MaintenanceAsync(CancellationToken cancellationToken) =>
        facade.GetMaintenanceAsync(cancellationToken);

    [McpServerTool(Name = "maintenance_select", Idempotent = true)]
    [Description("Selects a maintenance row by id.")]
    public Task<CommandResultDto> MaintenanceSelectAsync(
        [Description("Row id from the maintenance tool, such as displayLevels or action:repull.")]
            string rowId,
        CancellationToken cancellationToken = default
    ) => facade.MaintenanceAsync(MaintenanceOperation.Select, rowId, 0, false, cancellationToken);

    [McpServerTool(Name = "maintenance_adjust", Idempotent = true)]
    [Description("Changes a choice or toggle row by one step.")]
    public Task<CommandResultDto> MaintenanceAdjustAsync(
        [Description("Row id, such as displayLevels, tolerance, or flag:MapGrid.")] string rowId,
        [Description("1 for next or -1 for previous.")] int direction,
        CancellationToken cancellationToken = default
    ) =>
        facade.MaintenanceAsync(
            MaintenanceOperation.Adjust,
            rowId,
            direction,
            false,
            cancellationToken
        );

    [McpServerTool(Name = "maintenance_activate")]
    [Description(
        "Runs a maintenance row's action. Destructive rows need confirm=true, which replaces the second Enter press."
    )]
    public Task<CommandResultDto> MaintenanceActivateAsync(
        [Description("Row id, such as action:extract or flag:MapCrosshair.")] string rowId,
        [Description("Confirm a destructive action.")] bool confirm = false,
        CancellationToken cancellationToken = default
    ) =>
        facade.MaintenanceAsync(
            MaintenanceOperation.Activate,
            rowId,
            0,
            confirm,
            cancellationToken
        );

    [McpServerTool(Name = "pipeline")]
    [Description(
        "Runs a pipeline action: Check, Repull, Extract, Cache, Cancel, Retry, or Reset. Repull, Extract, and Reset need confirm=true."
    )]
    public Task<CommandResultDto> PipelineAsync(
        [Description("The action to run.")] PipelineAction action,
        [Description("Confirm a destructive action.")] bool confirm = false,
        CancellationToken cancellationToken = default
    ) => facade.PipelineAsync(action, confirm, cancellationToken);

    [McpServerTool(Name = "latest_run", ReadOnly = true)]
    [Description("The latest geodata run and its steps, with timings, attempts, and errors.")]
    public Task<RunDetailDto?> LatestRunAsync(CancellationToken cancellationToken) =>
        facade.GetLatestRunAsync(cancellationToken);

    [McpServerTool(Name = "artifacts", ReadOnly = true)]
    [Description("Downloaded PBF and extracted border artifacts, optionally for one region.")]
    public Task<IReadOnlyList<ArtifactDto>> ArtifactsAsync(
        [Description("Region name, such as europe.")] string? region = null,
        CancellationToken cancellationToken = default
    ) => facade.GetArtifactsAsync(region, cancellationToken);

    [McpServerTool(Name = "regions", ReadOnly = true)]
    [Description(
        "Configured regions with their artifact sizes, the extracted admin levels, and the border bounds and totals."
    )]
    public Task<RegionsResponse> RegionsAsync(CancellationToken cancellationToken) =>
        facade.GetRegionsAsync(cancellationToken);

    [McpServerTool(Name = "borders", ReadOnly = true)]
    [Description(
        "Queries border polylines. The default summary returns counts and bounds. format=geojson returns coordinates and needs bounds; a response over 100000 points is refused, so narrow the bounds instead of retrying."
    )]
    public Task<BorderQueryResponse> BordersAsync(
        [Description("Western longitude of the box.")] double? minLon = null,
        [Description("Southern latitude of the box.")] double? minLat = null,
        [Description("Eastern longitude of the box.")] double? maxLon = null,
        [Description("Northern latitude of the box.")] double? maxLat = null,
        [Description("Admin levels to include, for example [2,4].")] int[]? adminLevels = null,
        [Description("Maximum polylines, 1 to 2000.")] int? limit = null,
        [Description("summary or geojson.")] string? format = null,
        CancellationToken cancellationToken = default
    ) =>
        facade.QueryBordersAsync(
            GeodataQueryService.ParseBounds(minLon, minLat, maxLon, maxLat),
            adminLevels,
            limit ?? 0,
            format ?? "summary",
            cancellationToken
        );

    [McpServerTool(Name = "settings", ReadOnly = true)]
    [Description("Current display levels, extract levels, simplify tolerance, and feature flags.")]
    public SettingsViewDto Settings() => facade.GetSettings();

    [McpServerTool(Name = "update_settings")]
    [Description(
        "Changes runtime settings. Only the arguments you pass are applied. Use reset to restore configuration."
    )]
    public async Task<CommandResultDto> UpdateSettingsAsync(
        [Description("Admin levels the map draws, for example [2,4].")] int[]? displayLevels = null,
        [Description("Admin levels written to border files, for example [2,4].")]
            int[]? extractLevels = null,
        [Description("Geometry simplify tolerance, for example 0.002.")] double? tolerance = null,
        [Description("Feature flag name: MapGrid, MapCrosshair, or GeodataAutoPull.")]
            string? flag = null,
        [Description("Whether the named flag is on.")] bool? flagEnabled = null,
        [Description("Reset every setting to configuration.")] bool reset = false,
        CancellationToken cancellationToken = default
    )
    {
        var result = CommandResultDto.Failed("Pass at least one setting to change.");

        if (reset)
            result = await facade.ResetSettingsAsync(cancellationToken);

        if (displayLevels is not null)
            result = await facade.SetDisplayLevelsAsync(displayLevels, cancellationToken);

        if (extractLevels is not null)
            result = await facade.SetExtractLevelsAsync(extractLevels, cancellationToken);

        if (tolerance is not null)
            result = await facade.SetToleranceAsync(tolerance.Value, cancellationToken);

        if (flag is not null && flagEnabled is not null)
            result = await facade.SetFeatureFlagAsync(flag, flagEnabled.Value, cancellationToken);

        return result;
    }
}
