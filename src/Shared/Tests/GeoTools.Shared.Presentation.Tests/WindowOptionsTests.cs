using FluentAssertions;
using GeoTools.Shared.Application.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class WindowOptionsTests
{
    [Fact]
    public void Window_section_binds_title_width_and_height()
    {
        var options = ResolveOptions(
            new Dictionary<string, string?>
            {
                ["Window:Title"] = "GeoTools (development)",
                ["Window:Width"] = "1600",
                ["Window:Height"] = "900",
                ["Window:Maximized"] = "false",
            }
        );

        options.Title.Should().Be("GeoTools (development)");
        options.Width.Should().Be(1600);
        options.Height.Should().Be(900);
        options.Maximized.Should().BeFalse();
    }

    [Fact]
    public void Window_options_default_when_the_section_is_absent()
    {
        var options = ResolveOptions([]);

        options.Title.Should().Be("GeoTools");
        options.Width.Should().Be(1200);
        options.Height.Should().Be(800);
        options.Maximized.Should().BeTrue();
    }

    private static WindowOptions ResolveOptions(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.SetupPresentationLayer();

        return services.BuildServiceProvider().GetRequiredService<IOptions<WindowOptions>>().Value;
    }
}
