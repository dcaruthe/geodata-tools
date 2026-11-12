using FluentAssertions;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class MapViewTests
{
    [Fact]
    public void Compose_draws_the_map_over_a_black_background()
    {
        var scene = new MapView().Compose(new ViewSize(800, 600));

        scene.Background.Should().Be(Color.Black);
        scene.Shapes.OfType<LineDto>().Should().NotBeEmpty();
        scene.Shapes.OfType<CircleDto>().Should().NotBeEmpty();
        scene.Shapes.OfType<PolygonDto>().Should().NotBeEmpty();
        scene.Shapes.OfType<TextDto>().Should().Contain(text => text.Text.Contains("Tab"));
    }
}
