using FluentAssertions;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Infrastructure.Graphics.Skia;
using SkiaSharp;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class SkiaCanvasTests
{
    private const int Size = 64;
    private static readonly SKColor White = new(255, 255, 255);

    [Fact]
    public void Clear_fills_every_pixel_with_the_color()
    {
        using var surface = CreateSurface();

        new SkiaCanvas(surface.Canvas).Clear(new Color(10, 20, 30));

        PixelAt(surface, 0, 0).Should().Be(new SKColor(10, 20, 30));
        PixelAt(surface, Size - 1, Size - 1).Should().Be(new SKColor(10, 20, 30));
    }

    [Fact]
    public void DrawLine_paints_along_the_segment_and_leaves_the_rest_alone()
    {
        using var surface = CreateSurface();
        var canvas = ClearCanvas(surface);

        canvas.DrawLine(new Point(8, 32), new Point(56, 32), Color.White, 4);

        PixelAt(surface, 32, 32).Should().Be(White);
        PixelAt(surface, 32, 8).Should().Be(SKColors.Black);
    }

    [Fact]
    public void DrawCircle_paints_the_outline_only_when_unfilled()
    {
        using var surface = CreateSurface();
        var canvas = ClearCanvas(surface);

        canvas.DrawCircle(new Point(32, 32), 12, Color.White, 2, filled: false);

        PixelAt(surface, 44, 32).Should().NotBe(SKColors.Black);
        PixelAt(surface, 32, 32).Should().Be(SKColors.Black);
    }

    [Fact]
    public void DrawCircle_fills_the_disc_when_asked()
    {
        using var surface = CreateSurface();
        var canvas = ClearCanvas(surface);

        canvas.DrawCircle(new Point(32, 32), 12, Color.White, 2, filled: true);

        PixelAt(surface, 32, 32).Should().Be(White);
    }

    [Fact]
    public void DrawPolygon_fills_its_interior()
    {
        using var surface = CreateSurface();
        var canvas = ClearCanvas(surface);

        canvas.DrawPolygon(
            [new Point(8, 8), new Point(56, 8), new Point(32, 56)],
            Color.White,
            1,
            filled: true
        );

        PixelAt(surface, 32, 24).Should().Be(White);
        PixelAt(surface, 4, 56).Should().Be(SKColors.Black);
    }

    [Fact]
    public void DrawPolyline_strokes_every_segment()
    {
        using var surface = CreateSurface();
        var canvas = ClearCanvas(surface);

        canvas.DrawPolyline(
            [new Point(8, 56), new Point(32, 40), new Point(56, 56)],
            Color.White,
            4
        );

        PixelAt(surface, 32, 40).Should().Be(White);
        PixelAt(surface, 8, 8).Should().Be(SKColors.Black);
    }

    [Fact]
    public void DrawText_marks_pixels_in_the_glyph_area()
    {
        using var surface = CreateSurface();
        var canvas = ClearCanvas(surface);

        canvas.DrawText(new Point(4, 40), "Hi", 32, Color.White);

        CountNonBlackPixels(surface).Should().BeGreaterThan(0);
    }

    private static SKSurface CreateSurface() =>
        SKSurface.Create(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul))!;

    private static SkiaCanvas ClearCanvas(SKSurface surface)
    {
        var canvas = new SkiaCanvas(surface.Canvas);
        canvas.Clear(Color.Black);
        return canvas;
    }

    private static SKColor PixelAt(SKSurface surface, int x, int y)
    {
        using var image = surface.Snapshot();
        using var pixmap = image.PeekPixels();
        return pixmap.GetPixelColor(x, y);
    }

    private static int CountNonBlackPixels(SKSurface surface)
    {
        using var image = surface.Snapshot();
        using var pixmap = image.PeekPixels();
        var count = 0;
        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
            if (pixmap.GetPixelColor(x, y) != SKColors.Black)
                count++;

        return count;
    }
}
