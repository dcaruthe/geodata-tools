using Microsoft.Extensions.DependencyInjection;

namespace GeoTools.Shared.Domain;

public static class ConfigureServices
{
    public static IServiceCollection SetupDomainLayer(this IServiceCollection services) => services;
}
