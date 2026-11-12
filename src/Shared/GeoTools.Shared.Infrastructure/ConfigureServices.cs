using GeoTools.Shared.Application;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Infrastructure.Geospatial;
using Microsoft.Extensions.DependencyInjection;

namespace GeoTools.Shared.Infrastructure;

public static class ConfigureServices
{
    public static IServiceCollection SetupInfrastructureLayer(this IServiceCollection services)
    {
        services.SetupApplicationLayer();
        services.AddSingleton<IBorderSource, GeoJsonSeqBorderSource>();
        return services;
    }
}
