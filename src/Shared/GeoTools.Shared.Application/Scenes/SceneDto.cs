using GeoTools.Shared.Domain.Primatives;

namespace GeoTools.Shared.Application.Scenes;

public sealed record SceneDto(IReadOnlyList<ShapeDto> Shapes, Color Background);
