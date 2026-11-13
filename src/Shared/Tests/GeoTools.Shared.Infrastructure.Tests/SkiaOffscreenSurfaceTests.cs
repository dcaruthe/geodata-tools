using FluentAssertions;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Infrastructure.Rendering;
using SkiaSharp;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class SkiaOffscreenSurfaceTests
{
    [Fact]
    public void A_scene_renders_to_a_png_at_the_requested_size()
    {
        var scene = new SceneDto(
            [
                new PolygonDto(
                    [new Point(0, 0), new Point(64, 0), new Point(64, 48), new Point(0, 48)],
                    new Color(220, 40, 40),
                    1,
                    Filled: true
                ),
            ],
            new Color(0, 0, 0)
        );

        var png = new SkiaOffscreenSurface().CaptureScene(scene, 64, 48);

        using var bitmap = SKBitmap.Decode(png);
        bitmap.Width.Should().Be(64);
        bitmap.Height.Should().Be(48);
        var pixel = bitmap.GetPixel(32, 24);
        pixel.Red.Should().BeGreaterThan(180);
        pixel.Green.Should().BeLessThan(80);
    }

    [Fact]
    public void A_zero_size_capture_is_rejected()
    {
        var act = () =>
            new SkiaOffscreenSurface().CaptureScene(new SceneDto([], new Color(0, 0, 0)), 0, 10);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
