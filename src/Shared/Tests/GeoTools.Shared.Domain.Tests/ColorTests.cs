using FluentAssertions;
using GeoTools.Shared.Domain.Primatives;

namespace GeoTools.Shared.Domain.Tests;

public sealed class ColorTests
{
    [Fact]
    public void Color_defaults_to_an_opaque_alpha()
    {
        new Color(1, 2, 3).A.Should().Be(255);
    }

    [Fact]
    public void Black_and_white_are_opaque()
    {
        Color.Black.Should().Be(new Color(0, 0, 0, 255));
        Color.White.Should().Be(new Color(255, 255, 255, 255));
    }
}
