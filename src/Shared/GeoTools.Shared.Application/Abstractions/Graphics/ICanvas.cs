using GeoTools.Shared.Domain.Primatives;

namespace GeoTools.Shared.Application.Abstractions.Graphics;

public interface ICanvas
{
    void Clear(Color color);
    void DrawLine(Point a, Point b, Color color, float thickness);
    void DrawCircle(Point center, float radius, Color color, float thickness, bool filled);
    void DrawPolygon(IReadOnlyList<Point> points, Color color, float thickness, bool filled);
}
