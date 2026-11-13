using GeoTools.Api.Contracts;
using GeoTools.Api.Facade;
using GeoTools.Shared.Application.Link;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Api.Rest;

public static class RestEndpoints
{
    public static void MapGeoToolsRest(this WebApplication app)
    {
        app.MapGet("/health", (GeoToolsFacade facade, CancellationToken token) =>
                facade.GetHealthAsync(token)
            )
            .WithTags("system")
            .WithSummary("Reports the API, launcher link, and database state.");

        var api = app.MapGroup("/api").WithTags("geotools");

        api.MapGet("/state", (GeoToolsFacade facade, CancellationToken token) =>
                facade.GetStateAsync(token)
            )
            .WithSummary("Launcher state when it is running, plus the local state database.");

        api.MapGet(
                "/logs",
                (
                    string? level,
                    int? limit,
                    long? after,
                    GeoToolsFacade facade,
                    CancellationToken token
                ) => facade.GetLogsAsync(level, limit ?? 100, after, token)
            )
            .WithSummary("A tail of the launcher's own log.");

        api.MapGet(
                "/launcher/screenshot",
                async (int? width, int? height, GeoToolsFacade facade, CancellationToken token) =>
                {
                    var screenshot = await facade.GetScreenshotAsync(width, height, token);
                    return Results.File(
                        screenshot.Png,
                        "image/png",
                        $"geotools-{screenshot.Width}x{screenshot.Height}.png"
                    );
                }
            )
            .WithSummary(
                "Renders the active view to a PNG. Returns 409 when the launcher is down."
            );

        api.MapPost(
                "/launcher/navigate",
                (NavigateBody body, GeoToolsFacade facade, CancellationToken token) =>
                    facade.NavigateAsync(body.View, token)
            )
            .WithSummary("Switches the window to a view.");

        api.MapPost(
                "/launcher/keys",
                (KeysBody body, GeoToolsFacade facade, CancellationToken token) =>
                    facade.PressKeysAsync(body.Keys, token)
            )
            .WithSummary("Sends keys to the active view.");

        api.MapGet("/maintenance", (GeoToolsFacade facade, CancellationToken token) =>
                facade.GetMaintenanceAsync(token)
            )
            .WithSummary("The maintenance rows, selection, and status line.");

        api.MapPost(
                "/maintenance/select",
                (SelectRowBody body, GeoToolsFacade facade, CancellationToken token) =>
                    facade.MaintenanceAsync(
                        MaintenanceOperation.Select,
                        body.RowId,
                        0,
                        false,
                        token
                    )
            )
            .WithSummary("Selects a maintenance row by id.");

        api.MapPost(
                "/maintenance/adjust",
                (AdjustRowBody body, GeoToolsFacade facade, CancellationToken token) =>
                    facade.MaintenanceAsync(
                        MaintenanceOperation.Adjust,
                        body.RowId,
                        body.Direction,
                        false,
                        token
                    )
            )
            .WithSummary("Changes a choice or toggle row by one step.");

        api.MapPost(
                "/maintenance/activate",
                (ActivateRowBody body, GeoToolsFacade facade, CancellationToken token) =>
                    facade.MaintenanceAsync(
                        MaintenanceOperation.Activate,
                        body.RowId,
                        0,
                        body.Confirm,
                        token
                    )
            )
            .WithSummary("Runs a row's action. Destructive rows need confirm=true.");

        api.MapPost("/maintenance/cancel", (GeoToolsFacade facade, CancellationToken token) =>
                facade.MaintenanceAsync(MaintenanceOperation.Cancel, null, 0, false, token)
            )
            .WithSummary("Requests cancellation of the running work.");

        api.MapPost(
                "/pipeline",
                (PipelineBody body, GeoToolsFacade facade, CancellationToken token) =>
                    facade.PipelineAsync(body.Action, body.Confirm, token)
            )
            .WithSummary("Runs a pipeline action by name.");

        api.MapGet("/settings", (GeoToolsFacade facade) => facade.GetSettings())
            .WithSummary("Runtime settings and feature flags.");

        api.MapPut(
                "/settings/display-levels",
                (LevelsBody body, GeoToolsFacade facade, CancellationToken token) =>
                    facade.SetDisplayLevelsAsync(body.Levels, token)
            )
            .WithSummary("Sets the admin levels the map draws.");

        api.MapPut(
                "/settings/extract-levels",
                (LevelsBody body, GeoToolsFacade facade, CancellationToken token) =>
                    facade.SetExtractLevelsAsync(body.Levels, token)
            )
            .WithSummary("Sets the admin levels written to the border files.");

        api.MapPut(
                "/settings/tolerance",
                (ToleranceBody body, GeoToolsFacade facade, CancellationToken token) =>
                    facade.SetToleranceAsync(body.Tolerance, token)
            )
            .WithSummary("Sets the geometry simplify tolerance.");

        api.MapPut(
                "/settings/flags/{name}",
                (string name, EnabledBody body, GeoToolsFacade facade, CancellationToken token) =>
                    facade.SetFeatureFlagAsync(name, body.Enabled, token)
            )
            .WithSummary("Turns a feature flag on or off.");

        api.MapPost("/settings/reset", (GeoToolsFacade facade, CancellationToken token) =>
                facade.ResetSettingsAsync(token)
            )
            .WithSummary("Resets runtime settings to configuration.");

        api.MapGet("/runs/latest", (GeoToolsFacade facade, CancellationToken token) =>
                facade.GetLatestRunAsync(token)
            )
            .WithSummary("The latest run and its steps.");

        api.MapGet("/artifacts", (string? region, GeoToolsFacade facade, CancellationToken token) =>
                facade.GetArtifactsAsync(region, token)
            )
            .WithSummary("Downloaded and extracted artifacts, optionally by region.");

        api.MapGet("/regions", (GeoToolsFacade facade, CancellationToken token) =>
                facade.GetRegionsAsync(token)
            )
            .WithSummary("Regions, artifacts, admin levels, and border totals.");

        api.MapGet(
                "/borders",
                async (
                    double? minLon,
                    double? minLat,
                    double? maxLon,
                    double? maxLat,
                    string? levels,
                    int? limit,
                    string? format,
                    GeoToolsFacade facade,
                    CancellationToken token
                ) =>
                {
                    var bounds = GeodataQueryService.ParseBounds(minLon, minLat, maxLon, maxLat);
                    return await facade.QueryBordersAsync(
                        bounds,
                        ParseLevels(levels),
                        limit ?? 0,
                        format ?? "summary",
                        token
                    );
                }
            )
            .WithSummary("Queries border polylines. GeoJSON needs bounds to stay under the cap.");
    }

    private static int[]? ParseLevels(string? levels)
    {
        if (string.IsNullOrWhiteSpace(levels))
            return null;

        var parsed = levels
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value =>
                int.TryParse(value, out var level)
                    ? level
                    : throw new ArgumentException($"'{value}' is not an admin level.")
            )
            .ToArray();

        return parsed.Length == 0 ? null : parsed;
    }
}
