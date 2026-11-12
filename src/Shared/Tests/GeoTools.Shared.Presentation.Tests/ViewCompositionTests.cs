using FluentAssertions;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class ViewCompositionTests
{
    [Fact]
    public void Collects_shapes_into_a_scene_with_its_size_and_background()
    {
        var composition = new ViewComposition(new ViewSize(800, 600), Color.Black);
        var line = new LineDto(new Point(0, 0), new Point(10, 10), Color.White, 1);

        composition.Add(line);
        var scene = composition.Build();

        composition.Size.Should().Be(new ViewSize(800, 600));
        composition.Background.Should().Be(Color.Black);
        composition.Shapes.Should().ContainSingle().Which.Should().BeSameAs(line);
        scene.Background.Should().Be(Color.Black);
        scene.Shapes.Should().ContainSingle().Which.Should().BeSameAs(line);
    }
}
