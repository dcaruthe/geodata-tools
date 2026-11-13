using GeoTools.Shared.Application.Link;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Infrastructure.Rendering;
using GeoTools.Shared.Presentation.Views;
using Microsoft.Extensions.Logging;

namespace GeoTools.Shared.Presentation.Link;

// Runs on the render thread inside SceneWindow.OnUpdateFrame.
internal sealed class WindowCommandPump(
    WindowCommandInbox inbox,
    INavigationMediator mediator,
    IViewInputRouter inputRouter,
    ISceneSource sceneSource,
    MaintenanceController maintenance,
    MapController map,
    SkiaOffscreenSurface capture,
    TimeProvider clock,
    ILogger<WindowCommandPump> logger
)
{
    public void Drain(ViewSize size)
    {
        while (inbox.TryDequeue(out var command))
            command!.Completion.TrySetResult(Execute(command.Request, size));
    }

    private LinkResponse Execute(LinkRequest request, ViewSize size)
    {
        try
        {
            return request switch
            {
                NavigateRequest navigate => Navigate(navigate),
                PressKeysRequest press => PressKeys(press, size),
                MaintenanceCommandRequest command => MaintenanceCommand(command),
                PipelineCommandRequest pipeline => Pipeline(pipeline),
                ScreenshotRequest screenshot => Screenshot(screenshot, size),
                FocusPointRequest focus => FocusPoint(focus, size),
                FocusRegionRequest focus => FocusRegion(focus, size),
                BlurFocusRequest blur => BlurFocus(blur, size),
                PanRequest pan => Pan(pan, size),
                ZoomRequest zoom => Zoom(zoom, size),
                _ => LinkResponse.Failure(
                    request.Id,
                    new LinkError(
                        LinkError.Unsupported,
                        $"{request.GetType().Name} is not a window command."
                    )
                ),
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "A window command failed.");
            return LinkResponse.Failure(
                request.Id,
                new LinkError(LinkError.Internal, exception.Message)
            );
        }
    }

    private LinkResponse Navigate(NavigateRequest request)
    {
        mediator.NavigateTo(request.View);
        return Result(
            request.Id,
            CommandResultDto.Ok($"View is {mediator.ActiveView}.", mediator.ActiveView)
        );
    }

    private LinkResponse PressKeys(PressKeysRequest request, ViewSize size)
    {
        var handled = 0;
        foreach (var key in request.Keys)
        {
            if (inputRouter.Handle(ToViewKey(key), size))
                handled++;
        }

        return Result(
            request.Id,
            new CommandResultDto(
                true,
                $"Sent {request.Keys.Length} key(s); {handled} handled.",
                mediator.ActiveView,
                null
            )
        );
    }

    private LinkResponse MaintenanceCommand(MaintenanceCommandRequest request)
    {
        var result = request.Operation switch
        {
            MaintenanceOperation.Select => maintenance.SelectRow(request.RowId ?? string.Empty),
            MaintenanceOperation.Adjust => maintenance.AdjustRow(
                request.RowId ?? string.Empty,
                request.Direction
            ),
            MaintenanceOperation.Activate => maintenance.ActivateRow(
                request.RowId ?? string.Empty,
                request.Confirm
            ),
            MaintenanceOperation.Cancel => maintenance.CancelRun(),
            _ => new MaintenanceCommandResult(
                false,
                string.Empty,
                null,
                $"Unknown maintenance operation {request.Operation}."
            ),
        };

        return Result(request.Id, FromMaintenance(result));
    }

    private LinkResponse Pipeline(PipelineCommandRequest request)
    {
        if (request.Action == PipelineAction.Cancel)
            return Result(request.Id, FromMaintenance(maintenance.CancelRun()));

        var result = maintenance.ActivateRow(RowFor(request.Action), request.Confirm);
        return Result(request.Id, FromMaintenance(result));
    }

    private LinkResponse Screenshot(ScreenshotRequest request, ViewSize size)
    {
        var width = Math.Clamp(request.Width ?? size.Width, 64, 4096);
        var height = Math.Clamp(request.Height ?? size.Height, 64, 4096);
        var scene = sceneSource.Compose(new ViewSize(width, height));
        var png = capture.CaptureScene(scene, width, height);

        return Result(
            request.Id,
            new ScreenshotDto(
                png,
                width,
                height,
                mediator.ActiveView,
                clock.GetUtcNow(),
                request.Mode.ToString()
            )
        );
    }

    private LinkResponse FocusPoint(FocusPointRequest request, ViewSize size)
    {
        map.FocusAt(new GeoPoint(request.Longitude, request.Latitude), size);
        return Result(
            request.Id,
            CommandResultDto.Ok(
                $"Focusing the region at {request.Longitude:0.####},{request.Latitude:0.####}.",
                mediator.ActiveView
            )
        );
    }

    private LinkResponse FocusRegion(FocusRegionRequest request, ViewSize size)
    {
        map.FocusRegion(request.RegionId, size);
        return Result(
            request.Id,
            CommandResultDto.Ok($"Focusing region {request.RegionId}.", mediator.ActiveView)
        );
    }

    private LinkResponse BlurFocus(BlurFocusRequest request, ViewSize size)
    {
        if (!map.Blur(size))
        {
            return LinkResponse.Failure(
                request.Id,
                new LinkError(LinkError.InvalidRequest, "Nothing is focused to blur.")
            );
        }

        return Result(
            request.Id,
            CommandResultDto.Ok("Popped the focus stack.", mediator.ActiveView)
        );
    }

    private LinkResponse Pan(PanRequest request, ViewSize size)
    {
        map.Pan(request.Dx, request.Dy, size);
        return Result(
            request.Id,
            CommandResultDto.Ok(
                $"Panned by {request.Dx:0.#},{request.Dy:0.#} px.",
                mediator.ActiveView
            )
        );
    }

    private LinkResponse Zoom(ZoomRequest request, ViewSize size)
    {
        if (!map.ZoomByDirection(request.Direction, size))
        {
            return LinkResponse.Failure(
                request.Id,
                new LinkError(
                    LinkError.InvalidRequest,
                    $"Zoom direction must be 1 or -1, not {request.Direction}."
                )
            );
        }

        return Result(
            request.Id,
            CommandResultDto.Ok(
                $"Zoomed {(request.Direction > 0 ? "in" : "out")}.",
                mediator.ActiveView
            )
        );
    }

    private CommandResultDto FromMaintenance(MaintenanceCommandResult result) =>
        new(result.Accepted, result.Status, mediator.ActiveView, result.Error);

    private static LinkResponse Result(string id, object payload) =>
        LinkResponse.Success(id, payload);

    private static string RowFor(PipelineAction action) =>
        action switch
        {
            PipelineAction.Check => "action:check",
            PipelineAction.Repull => "action:repull",
            PipelineAction.Extract => "action:extract",
            PipelineAction.Cache => "action:cache",
            PipelineAction.Reindex => "action:reindex",
            PipelineAction.Retry => "action:retry",
            PipelineAction.Reset => "action:reset",
            _ => "action:check",
        };

    private static ViewKey ToViewKey(WindowKey key) =>
        key switch
        {
            WindowKey.Up => ViewKey.Up,
            WindowKey.Down => ViewKey.Down,
            WindowKey.Left => ViewKey.Left,
            WindowKey.Right => ViewKey.Right,
            WindowKey.Enter => ViewKey.Enter,
            WindowKey.Backspace => ViewKey.Backspace,
            WindowKey.Home => ViewKey.Home,
            WindowKey.End => ViewKey.End,
            _ => ViewKey.Enter,
        };
}
