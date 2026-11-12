using FluentAssertions;
using GeoTools.Shared.Application.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class ConfigureServicesValidationTests
{
    [Theory]
    [InlineData("Geodata:Regions:0", "", "Region names")]
    [InlineData("Geodata:ExtractAdminLevels:0", "0", "admin levels")]
    [InlineData("Geodata:SimplifyToleranceDegrees", "-1", "tolerance")]
    [InlineData("Geodata:PullConcurrency", "0", "concurrency")]
    [InlineData("Geodata:RetryCount", "-1", "Retry count")]
    [InlineData("Geodata:InactivityTimeoutSeconds", "0", "timeout")]
    [InlineData("Geodata:SourceBaseUrl", "not a url", "source")]
    public void Invalid_geodata_options_are_rejected(string key, string value, string expected)
    {
        var provider = Build(new Dictionary<string, string?> { [key] = value });

        var act = () => provider.GetRequiredService<IOptions<GeodataOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage($"*{expected}*");
    }

    [Fact]
    public void A_blank_database_path_is_rejected()
    {
        var provider = Build(new Dictionary<string, string?> { ["Storage:DatabasePath"] = " " });

        var act = () => provider.GetRequiredService<IOptions<StorageOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Database path*");
    }

    [Fact]
    public void Configured_arrays_replace_the_defaults()
    {
        var provider = Build(
            new Dictionary<string, string?>
            {
                ["Geodata:Regions:0"] = "africa",
                ["Geodata:ExtractAdminLevels:0"] = "6",
            }
        );

        var options = provider.GetRequiredService<IOptions<GeodataOptions>>().Value;

        options.Regions.Should().Equal("africa");
        options.ExtractAdminLevels.Should().Equal(6);
    }

    [Fact]
    public void Compact_lists_replace_the_default_arrays()
    {
        var provider = Build(
            new Dictionary<string, string?>
            {
                ["Geodata:RegionList"] = "fixture,test",
                ["Geodata:ExtractAdminLevelsList"] = "6,8",
            }
        );

        var options = provider.GetRequiredService<IOptions<GeodataOptions>>().Value;

        options.Regions.Should().Equal("fixture", "test");
        options.ExtractAdminLevels.Should().Equal(6, 8);
    }

    [Fact]
    public void A_malformed_level_list_is_rejected_by_validation()
    {
        var provider = Build(
            new Dictionary<string, string?> { ["Geodata:ExtractAdminLevelsList"] = "6,x" }
        );

        var act = () => provider.GetRequiredService<IOptions<GeodataOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*positive*");
    }

    private static ServiceProvider Build(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.SetupInfrastructureLayer();
        return services.BuildServiceProvider();
    }
}
