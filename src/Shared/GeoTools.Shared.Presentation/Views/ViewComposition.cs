using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Domain.Primatives;

namespace GeoTools.Shared.Presentation.Views;

public sealed class ViewComposition(ViewSize size, Color background)
{
    private readonly List<ShapeDto> _shapes = [];

    public ViewSize Size { get; } = size;
    public Color Background { get; } = background;
    public IReadOnlyList<ShapeDto> Shapes => _shapes;

    public void Add(ShapeDto shape) => _shapes.Add(shape);

    public SceneDto Build() => new(_shapes, Background);
}
