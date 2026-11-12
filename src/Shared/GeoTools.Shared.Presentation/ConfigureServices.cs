using GeoTools.Shared.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GeoTools.Shared.Presentation;

public static class ConfigureServices
{
    public static IServiceCollection SetupPresentationLayer(this IServiceCollection services) =>
        services.SetupInfrastructureLayer();
}
