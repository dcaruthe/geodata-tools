using GeoTools.Shared.Application.Abstractions.Graphics;
using GeoTools.Shared.Infrastructure.Graphics.Skia;
using SkiaSharp;

namespace GeoTools.Shared.Infrastructure.Rendering.Raster;

public sealed class SkiaRasterSurface : IRenderSurface, IDisposable
{
    private readonly int _width;
    private readonly int _height;
    private SKSurface? _surface;

    public SkiaRasterSurface(int width, int height)
    {
        _width = width;
        _height = height;
    }

    public void Render(Action<ICanvas> draw) => draw(new SkiaCanvas(RequireSurface().Canvas));

    public void Save(string path)
    {
        var surface = RequireSurface();

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private SKSurface RequireSurface()
    {
        _surface ??= SKSurface.Create(
            new SKImageInfo(_width, _height, SKColorType.Rgba8888, SKAlphaType.Premul)
        );

        return _surface
            ?? throw new InvalidOperationException("Skia could not create a raster surface.");
    }

    public void Dispose() => _surface?.Dispose();
}
