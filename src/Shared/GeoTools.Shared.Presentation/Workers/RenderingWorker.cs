using GeoTools.Shared.Application.Rendering;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Presentation.Controls;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GeoTools.Shared.Presentation.Workers;

internal sealed class RenderingWorker(
    ISceneRenderer renderer,
    SceneDto scene,
    SceneWindow window,
    IHostApplicationLifetime lifetime,
    ILogger<RenderingWorker> logger
) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        window.SetScene(renderer, scene);
        logger.LogInformation(
            "Rendering {Width}x{Height}. Close the window or press Ctrl+C to exit.",
            window.ClientSize.X,
            window.ClientSize.Y
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
