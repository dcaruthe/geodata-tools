using GeoTools.Shared.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace GeoTools.Shared.Application;

public static class ConfigureServices
{
    public static IServiceCollection SetupApplicationLayer(this IServiceCollection services) =>
        services.SetupDomainLayer();
}
