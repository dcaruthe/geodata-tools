using FluentAssertions;
using GeoTools.Shared.Application.State;

namespace GeoTools.Shared.Application.Tests;

public sealed class WindowSizeTests
{
    [Fact]
    public void Maximized_round_trips_through_display_and_parse()
    {
        WindowSize.Maximized.Display.Should().Be("maximized");
        WindowSize.TryParse("maximized", out var parsed).Should().BeTrue();
        parsed.Should().Be(WindowSize.Maximized);
        WindowSize.TryParse("MAXIMIZED", out var upper).Should().BeTrue();
        upper.Should().Be(WindowSize.Maximized);
    }

    [Theory]
    [InlineData("640x480", 640, 480)]
    [InlineData("1600x900", 1600, 900)]
    [InlineData("7680x4320", 7680, 4320)]
    public void A_fixed_size_round_trips_through_display_and_parse(
        string text,
        int width,
        int height
    )
    {
        var size = new FixedWindow(width, height);

        size.Display.Should().Be(text);
        WindowSize.TryParse(text, out var parsed).Should().BeTrue();
        parsed.Should().Be(size);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("maximised")]
    [InlineData("0x100")]
    [InlineData("100x0")]
    [InlineData("-100x200")]
    [InlineData("+100x200")]
    [InlineData("100x")]
    [InlineData("x200")]
    [InlineData("100x200x300")]
    [InlineData("100x200junk")]
    [InlineData("100X200")]
    [InlineData("100 x 200")]
    public void Invalid_text_is_rejected(string? text)
    {
        WindowSize.TryParse(text, out _).Should().BeFalse();
    }

    [Fact]
    public void FromOptions_picks_maximized_or_the_configured_size()
    {
        WindowSize.FromOptions(1200, 800, maximized: true).Should().Be(WindowSize.Maximized);
        WindowSize.FromOptions(1200, 800, maximized: false).Should().Be(new FixedWindow(1200, 800));
    }

    [Fact]
    public void Validation_accepts_maximized()
    {
        SettingsValidation.ValidateWindowSize(WindowSize.Maximized).Accepted.Should().BeTrue();
    }

    [Theory]
    [InlineData(640, 480)]
    [InlineData(1600, 900)]
    [InlineData(7680, 4320)]
    public void Validation_accepts_sizes_inside_the_bounds(int width, int height)
    {
        SettingsValidation
            .ValidateWindowSize(new FixedWindow(width, height))
            .Accepted.Should()
            .BeTrue();
    }

    [Theory]
    [InlineData(639, 480)]
    [InlineData(640, 479)]
    [InlineData(7681, 4320)]
    [InlineData(7680, 4321)]
    [InlineData(0, 0)]
    public void Validation_rejects_sizes_outside_the_bounds(int width, int height)
    {
        var result = SettingsValidation.ValidateWindowSize(new FixedWindow(width, height));

        result.Accepted.Should().BeFalse();
        result.Error.Should().Contain("640x480");
        result.Error.Should().Contain("7680x4320");
    }
}
