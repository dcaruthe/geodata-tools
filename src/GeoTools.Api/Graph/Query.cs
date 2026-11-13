using GeoTools.Api.Contracts;
using GeoTools.Api.Facade;
using GeoTools.Shared.Application.Link;
using HotChocolate;

namespace GeoTools.Api.Graph;

public sealed class Query
{
    public Task<HealthResponse> GetHealthAsync(
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.GetHealthAsync(cancellationToken);

    public Task<StateResponse> GetStateAsync(
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.GetStateAsync(cancellationToken);

    public Task<LogPageDto> GetLogsAsync(
        string? level,
        int? limit,
        long? after,
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.GetLogsAsync(level, limit ?? 100, after, cancellationToken);

    public Task<MaintenanceDto> GetMaintenanceAsync(
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.GetMaintenanceAsync(cancellationToken);

    public Task<SettingsViewDto> GetSettingsAsync(
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.GetSettingsAsync(cancellationToken);

    public Task<RunDetailDto?> GetLatestRunAsync(
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.GetLatestRunAsync(cancellationToken);

    public Task<IReadOnlyList<ArtifactDto>> GetArtifactsAsync(
        string? region,
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.GetArtifactsAsync(region, cancellationToken);

    public Task<RegionsResponse> GetRegionsAsync(
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.GetRegionsAsync(cancellationToken);

    public Task<BorderQueryResponse> GetBordersAsync(
        BoundsInput? bounds,
        int[]? adminLevels,
        int? limit,
        string? format,
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) =>
        facade.QueryBordersAsync(
            GeodataQueryService.ParseBounds(
                bounds?.MinLongitude,
                bounds?.MinLatitude,
                bounds?.MaxLongitude,
                bounds?.MaxLatitude
            ),
            adminLevels,
            limit ?? 0,
            format ?? "summary",
            cancellationToken
        );

    public Task<ScreenshotInfoDto> GetScreenshotAsync(
        int? width,
        int? height,
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.GetScreenshotInfoAsync(width, height, cancellationToken);
}
