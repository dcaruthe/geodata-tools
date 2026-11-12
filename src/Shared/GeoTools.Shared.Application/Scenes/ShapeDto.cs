using GeoTools.Shared.Domain.Primatives;

namespace GeoTools.Shared.Application.Scenes;

public abstract record ShapeDto;

public sealed record LineDto(Point From, Point To, Color Color, float Thickness) : ShapeDto;

public sealed record CircleDto(
    Point Center,
    float Radius,
    Color Color,
    float Thickness,
    bool Filled = true
) : ShapeDto;

public sealed record PolygonDto(
    IReadOnlyList<Point> Points,
    Color Color,
    float Thickness,
    bool Filled = true
) : ShapeDto;
