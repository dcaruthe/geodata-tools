using GeoTools.Shared.Application.Abstractions.Graphics;
using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.Rendering;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Infrastructure;
using GeoTools.Shared.Infrastructure.Rendering;
using GeoTools.Shared.Infrastructure.Rendering.OpenGl;
using GeoTools.Shared.Presentation.Controls;
using GeoTools.Shared.Presentation.Link;
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

        services
            .AddOptions<MapOptions>()
            .BindConfiguration(MapOptions.SectionName)
            // The binder merges into an existing array, so replace the levels
            // from configuration instead of inheriting the class default.
            .PostConfigure<IConfiguration>(
                (options, configuration) =>
                {
                    var section = configuration.GetSection(
                        $"{MapOptions.SectionName}:{nameof(MapOptions.AdminLevels)}"
                    );
                    if (section.Exists())
                        options.AdminLevels = section.Get<int[]>() ?? [];
                }
            )
            .Validate(o => o.AdminLevels is not null, "Admin levels must not be null.")
            .Validate(
                o => o.AdminLevels is null || o.AdminLevels.All(level => level > 0),
                "Admin levels must be positive."
            );

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<WindowOptions>>().Value;
            return SceneWindow.Create(
                options,
                provider.GetRequiredService<INavigationMediator>(),
                provider.GetRequiredService<IViewInputRouter>(),
                provider.GetRequiredService<WindowCommandPump>()
            );
        });
        services.AddSingleton<IOpenGlContext>(provider =>
            provider.GetRequiredService<SceneWindow>()
        );
        services.AddSingleton<SkiaOpenGlSurface>();
        services.AddSingleton<SkiaOffscreenSurface>();
        services.AddSingleton<IRenderSurface>(provider =>
            provider.GetRequiredService<SkiaOpenGlSurface>()
        );

        services.AddSingleton<IView, LoadingView>();
        services.AddSingleton<IView, MapView>();
        services.AddSingleton<MaintenanceView>();
        services.AddSingleton<IView>(provider => provider.GetRequiredService<MaintenanceView>());
        services.AddSingleton<IViewInputRouter, ViewInputRouter>();
        services.AddSingleton<ISceneRenderer, DefaultSceneRenderer>();
        services.AddSingleton<MaintenanceController>();
        services.AddSingleton<IMessageSubscription>(provider =>
            provider.GetRequiredService<MaintenanceController>()
        );
        services.AddSingleton<IMessageSubscription, ViewTransitionSubscriber>();
        services.AddSingleton<ViewCatalog>();
        services.AddSingleton<ISceneSource, ActiveViewSceneSource>();
        services.AddSingleton<WindowCommandInbox>();
        services.AddSingleton<WindowCommandPump>();
        services.AddHostedService<LauncherLinkService>();
        services.AddHostedService<RenderingWorker>();

        return services;
    }
}
