using FluentAssertions;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Infrastructure.Rendering;
using GeoTools.Shared.Presentation.Link;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class ConfigureServicesTests
{
    [Fact]
    public void Map_options_reject_null_admin_levels()
    {
        var act = () => WithOptions<MapOptions>(options => options.AdminLevels = null!, _ => { });

        act.Should().Throw<OptionsValidationException>().WithMessage("*Admin levels*");
    }

    [Fact]
    public void Map_options_reject_a_negative_admin_level()
    {
        var act = () => WithOptions<MapOptions>(options => options.AdminLevels = [-1], _ => { });

        act.Should().Throw<OptionsValidationException>().WithMessage("*positive*");
    }

    [Fact]
    public void Window_options_reject_a_blank_title()
    {
        var act = () => WithOptions<WindowOptions>(options => options.Title = " ", _ => { });

        act.Should().Throw<OptionsValidationException>().WithMessage("*Window title*");
    }

    [Fact]
    public void Window_options_reject_a_non_positive_size()
    {
        var act = () => WithOptions<WindowOptions>(options => options.Width = 0, _ => { });

        act.Should().Throw<OptionsValidationException>().WithMessage("*Window width and height*");
    }

    [Fact]
    public void Map_options_bind_the_defaults_when_no_section_is_configured()
    {
        WithOptions<MapOptions>(
            _ => { },
            options =>
            {
                options.AdminLevels.Should().Equal(2);
            }
        );
    }

    [Fact]
    public void Map_options_replace_the_default_admin_levels_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Map:AdminLevels:0"] = "4" })
            .Build();

        WithOptions<MapOptions>(
            configuration,
            _ => { },
            options => options.AdminLevels.Should().Equal(4)
        );
    }

    [Fact]
    public async Task The_link_services_resolve_without_a_window()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Geodata:RegionList"] = "africa",
                    ["Geodata:ExtractAdminLevelsList"] = "2",
                    ["Storage:DatabasePath"] = Path.Combine(
                        Path.GetTempPath(),
                        $"geotools-link-{Guid.NewGuid():N}.db"
                    ),
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.SetupPresentationLayer();

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<WindowCommandInbox>().Should().NotBeNull();
        provider.GetRequiredService<WindowCommandPump>().Should().NotBeNull();
        provider.GetRequiredService<SkiaOffscreenSurface>().Should().NotBeNull();
    }

    private static void WithOptions<T>(Action<T> configure, Action<T> inspect)
        where T : class => WithOptions(new ConfigurationBuilder().Build(), configure, inspect);

    private static void WithOptions<T>(
        IConfiguration configuration,
        Action<T> configure,
        Action<T> inspect
    )
        where T : class
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.SetupPresentationLayer();
        services.Configure(configure);

        using var provider = services.BuildServiceProvider();
        inspect(provider.GetRequiredService<IOptions<T>>().Value);
    }
}
