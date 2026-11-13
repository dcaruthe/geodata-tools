using GeoTools.Api.Contracts;
using GeoTools.Api.Facade;
using GeoTools.Shared.Application.Link;
using GeoTools.Shared.Application.Views;
using HotChocolate;

namespace GeoTools.Api.Graph;

public sealed class Mutation
{
    public Task<CommandResultDto> NavigateAsync(
        ViewId view,
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.NavigateAsync(view, cancellationToken);

    public Task<CommandResultDto> PressKeysAsync(
        WindowKey[] keys,
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.PressKeysAsync(keys, cancellationToken);

    public Task<CommandResultDto> MaintenanceAsync(
        MaintenanceOperation operation,
        string? rowId,
        int? direction,
        bool? confirm,
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) =>
        facade.MaintenanceAsync(
            operation,
            rowId,
            direction ?? 0,
            confirm ?? false,
            cancellationToken
        );

    public Task<CommandResultDto> PipelineAsync(
        PipelineAction action,
        bool? confirm,
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.PipelineAsync(action, confirm ?? false, cancellationToken);

    public Task<CommandResultDto> SetDisplayLevelsAsync(
        int[] levels,
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.SetDisplayLevelsAsync(levels, cancellationToken);

    public Task<CommandResultDto> SetExtractLevelsAsync(
        int[] levels,
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.SetExtractLevelsAsync(levels, cancellationToken);

    public Task<CommandResultDto> SetToleranceAsync(
        double tolerance,
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.SetToleranceAsync(tolerance, cancellationToken);

    public Task<CommandResultDto> SetWindowSizeAsync(
        bool maximized,
        int width,
        int height,
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.SetWindowSizeAsync(maximized, width, height, cancellationToken);

    public Task<CommandResultDto> SetFeatureFlagAsync(
        string flag,
        bool enabled,
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.SetFeatureFlagAsync(flag, enabled, cancellationToken);

    public Task<CommandResultDto> ResetSettingsAsync(
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.ResetSettingsAsync(cancellationToken);

    public Task<CommandResultDto> FocusAtAsync(
        double longitude,
        double latitude,
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.FocusPointAsync(longitude, latitude, cancellationToken);

    public Task<CommandResultDto> FocusRegionAsync(
        string regionId,
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.FocusRegionAsync(regionId, cancellationToken);

    public Task<CommandResultDto> BlurFocusAsync(
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.BlurFocusAsync(cancellationToken);

    public Task<CommandResultDto> PanAsync(
        double dx,
        double dy,
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.PanAsync(dx, dy, cancellationToken);

    public Task<CommandResultDto> ZoomAsync(
        int direction,
        [Service] GeoToolsFacade facade,
        CancellationToken cancellationToken
    ) => facade.ZoomAsync(direction, cancellationToken);
}
