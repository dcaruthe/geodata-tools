using GeoTools.Shared.Domain.Primatives;

namespace GeoTools.Shared.Application.Geospatial;

public sealed record ScreenPolyline(Point[] Points, bool Closed);
