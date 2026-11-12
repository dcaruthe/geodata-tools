using GeoTools.Shared.Application.Abstractions.Graphics;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Infrastructure;
using GeoTools.Shared.Infrastructure.Rendering.OpenGl;
using GeoTools.Shared.Presentation.Controls;
using GeoTools.Shared.Presentation.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Presentation;

public static class ConfigureServices
{
    public static IServiceCollection SetupPresentationLayer(this IServiceCollection services)
    {
        services.SetupInfrastructureLayer();

        services
            .AddOptions<RenderingOptions>()
            .Configure<IConfiguration>((options, configuration) => configuration.Bind(options))
            .Validate(
                options => options.Width > 0 && options.Height > 0,
                "Rendering width and height must be positive."
            );

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<RenderingOptions>>().Value;
            return SceneWindow.Create(options.Width, options.Height);
        });
        services.AddSingleton<IOpenGlContext>(provider =>
            provider.GetRequiredService<SceneWindow>()
        );
        services.AddSingleton<SkiaOpenGlSurface>();
        services.AddSingleton<IRenderSurface>(provider =>
            provider.GetRequiredService<SkiaOpenGlSurface>()
        );
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<RenderingOptions>>().Value;
            return DemoScene.Create(options.Width, options.Height);
        });
        services.AddHostedService<RenderingWorker>();

        return services;
    }
}
