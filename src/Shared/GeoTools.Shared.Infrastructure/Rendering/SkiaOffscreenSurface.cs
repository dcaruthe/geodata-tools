using GeoTools.Shared.Application.Abstractions.Graphics;
using GeoTools.Shared.Application.Rendering;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Infrastructure.Graphics.Skia;
using SkiaSharp;

namespace GeoTools.Shared.Infrastructure.Rendering;

// Renders the active scene to a PNG without a window, so an agent gets the same picture the
// GL surface draws. It uses the same renderer and canvas, so the two paths cannot drift.
public sealed class SkiaOffscreenSurface
{
    public byte[] CaptureScene(SceneDto scene, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "A capture needs a positive size."
            );

        using var surface = SKSurface.Create(
            new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul)
        )
            ?? throw new InvalidOperationException(
                $"Skia could not create a {width}x{height} surface."
            );

        new DefaultSceneRenderer(new BitmapSurface(surface)).Render(scene);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Skia could not encode the capture as PNG.");

        return data.ToArray();
    }

    private sealed class BitmapSurface(SKSurface surface) : IRenderSurface
    {
        public void Render(Action<ICanvas> draw)
        {
            draw(new SkiaCanvas(surface.Canvas));
            surface.Canvas.Flush();
        }
    }
}
