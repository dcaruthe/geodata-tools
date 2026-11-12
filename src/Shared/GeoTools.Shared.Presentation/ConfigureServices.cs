using GeoTools.Shared.Application.Abstractions.Graphics;
using GeoTools.Shared.Infrastructure;
using GeoTools.Shared.Infrastructure.Rendering.OpenGl;
using GeoTools.Shared.Presentation.Controls;
using GeoTools.Shared.Presentation.Options;
using GeoTools.Shared.Presentation.Views;
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
            .AddOptions<WindowOptions>()
            .Configure<IConfiguration>(
                (options, configuration) =>
                    configuration.GetSection(WindowOptions.SectionName).Bind(options)
            )
            .Validate(o => !string.IsNullOrWhiteSpace(o.Title), "Window title is required.")
            .Validate(
                o => o.Width > 0 && o.Height > 0,
                "Window width and height must be positive."
            );

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<WindowOptions>>().Value;
            return SceneWindow.Create(options);
        });
        services.AddSingleton<IOpenGlContext>(provider =>
            provider.GetRequiredService<SceneWindow>()
        );
        services.AddSingleton<SkiaOpenGlSurface>();
        services.AddSingleton<IRenderSurface>(provider =>
            provider.GetRequiredService<SkiaOpenGlSurface>()
        );
        services.AddSingleton<IView, MapView>();
        services.AddSingleton<IView, SettingsView>();
        services.AddSingleton<ViewCatalog>();
        services.AddSingleton<ISceneSource, ActiveViewSceneSource>();
        services.AddHostedService<RenderingWorker>();

        return services;
    }
}
