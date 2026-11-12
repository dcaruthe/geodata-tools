using GeoTools.Shared.Application;
using Microsoft.Extensions.DependencyInjection;

namespace GeoTools.Shared.Infrastructure;

public static class ConfigureServices
{
    public static IServiceCollection SetupInfrastructureLayer(this IServiceCollection services) =>
        services.SetupApplicationLayer();
}
