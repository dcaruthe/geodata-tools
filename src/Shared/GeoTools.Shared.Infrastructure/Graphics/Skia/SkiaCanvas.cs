using GeoTools.Shared.Application.Abstractions.Graphics;
using GeoTools.Shared.Domain.Primatives;
using SkiaSharp;

namespace GeoTools.Shared.Infrastructure.Graphics.Skia;

internal sealed class SkiaCanvas : ICanvas
{
    private readonly SKCanvas _canvas;

    public SkiaCanvas(SKCanvas canvas) => _canvas = canvas;

    public void Clear(Color color) => _canvas.Clear(ToSk(color));

    public void DrawLine(Point a, Point b, Color color, float thickness)
    {
        using var paint = CreatePaint(color, thickness, filled: false);
        _canvas.DrawLine(a.X, a.Y, b.X, b.Y, paint);
    }

    public void DrawCircle(Point center, float radius, Color color, float thickness, bool filled)
    {
        using var paint = CreatePaint(color, thickness, filled);
        _canvas.DrawCircle(center.X, center.Y, radius, paint);
    }

    public void DrawPolygon(IReadOnlyList<Point> points, Color color, float thickness, bool filled)
    {
        using var paint = CreatePaint(color, thickness, filled);
        using var builder = new SKPathBuilder();
        builder.AddPoly(points.Select(p => new SKPoint(p.X, p.Y)).ToArray(), close: true);
        using var path = builder.Detach();
        _canvas.DrawPath(path, paint);
    }

    public void DrawPolyline(IReadOnlyList<Point> points, Color color, float thickness)
    {
        using var paint = CreatePaint(color, thickness, filled: false);
        using var builder = new SKPathBuilder();
        builder.AddPoly(points.Select(p => new SKPoint(p.X, p.Y)).ToArray(), close: false);
        using var path = builder.Detach();
        _canvas.DrawPath(path, paint);
    }

    public void DrawText(Point position, string text, float size, Color color)
    {
        using var font = new SKFont { Size = size, Edging = SKFontEdging.Antialias };
        using var paint = new SKPaint
        {
            Color = ToSk(color),
            Style = SKPaintStyle.Fill,
            IsAntialias = true,
        };
        _canvas.DrawText(text, position.X, position.Y, SKTextAlign.Left, font, paint);
    }

    private static SKPaint CreatePaint(Color color, float thickness, bool filled) =>
        new()
        {
            Color = ToSk(color),
            Style = filled ? SKPaintStyle.Fill : SKPaintStyle.Stroke,
            StrokeWidth = thickness,
            IsAntialias = true,
        };

    private static SKColor ToSk(Color c) => new(c.R, c.G, c.B, c.A);
}
