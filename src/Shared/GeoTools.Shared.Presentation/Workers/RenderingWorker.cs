using GeoTools.Shared.Application.Rendering;
using GeoTools.Shared.Presentation.Controls;
using GeoTools.Shared.Presentation.Options;
using GeoTools.Shared.Presentation.Views;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Presentation.Workers;

internal sealed class RenderingWorker(
    ISceneRenderer renderer,
    ISceneSource source,
    SceneWindow window,
    IOptions<WindowOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<RenderingWorker> logger
) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        window.SetScene(
            renderer,
            source.Compose(new ViewSize(window.ClientSize.X, window.ClientSize.Y))
        );
        logger.LogInformation(
            "Window \"{Title}\" {Width}x{Height}. Close the window or press Ctrl+C to exit.",
            options.Value.Title,
            options.Value.Width,
            options.Value.Height
        );

        using var closeOnStop = cancellationToken.Register(window.RequestClose);

        try
        {
            // GLFW needs window and event calls on the main thread, which is where the host
            // starts its hosted services.
            window.Run();
        }
        finally
        {
            lifetime.StopApplication();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
