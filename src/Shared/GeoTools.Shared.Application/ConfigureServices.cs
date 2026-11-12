using GeoTools.Shared.Application.Rendering;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace GeoTools.Shared.Application;

public static class ConfigureServices
{
    public static IServiceCollection SetupApplicationLayer(this IServiceCollection services)
    {
        services.SetupDomainLayer();
        services.AddSingleton<ISceneRenderer, DefaultSceneRenderer>();
        services.AddSingleton<INavigationMediator>(_ => new NavigationMediator());
        return services;
    }
}
