using FluentAssertions;
using GeoTools.Shared.Application.Abstractions.Graphics;
using GeoTools.Shared.Application.Rendering;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Domain.Primatives;

namespace GeoTools.Shared.Application.Tests;

public sealed class DefaultSceneRendererTests
{
    private static readonly Color Background = new(10, 20, 30);

    [Fact]
    public void Render_draws_one_call_per_shape_in_input_order()
    {
        var surface = RenderScene();

        surface
            .Calls.Select(call => call.Kind)
            .Should()
            .Equal("line", "circle", "circle", "polygon", "polygon");
    }

    [Fact]
    public void Render_passes_each_polygon_its_own_fill_and_thickness()
    {
        var surface = RenderScene();

        surface
            .Calls.Where(call => call.Kind == "polygon")
            .Should()
            .Equal(("polygon", true, 5f), ("polygon", false, 6f));
    }

    [Fact]
    public void Render_passes_each_circle_its_own_fill_and_thickness()
    {
        var surface = RenderScene();

        surface
            .Calls.Where(call => call.Kind == "circle")
            .Should()
            .Equal(("circle", true, 3f), ("circle", false, 4f));
    }

    [Fact]
    public void Render_clears_the_canvas_with_the_scene_background()
    {
        var surface = RenderScene();

        surface.ClearColor.Should().Be(Background);
    }

    private static RecordingSurface RenderScene()
    {
        var scene = new SceneDto(
            [
                new LineDto(new Point(0, 0), new Point(10, 10), new Color(255, 0, 0), 2),
                new CircleDto(new Point(50, 50), 10, new Color(0, 255, 0), 3, Filled: true),
                new CircleDto(new Point(70, 50), 10, new Color(0, 0, 255), 4, Filled: false),
                new PolygonDto(
                    [new Point(0, 0), new Point(10, 0), new Point(10, 10)],
                    new Color(255, 255, 0),
                    5,
                    Filled: true
                ),
                new PolygonDto(
                    [new Point(20, 20), new Point(30, 20), new Point(30, 30)],
                    new Color(255, 0, 255),
                    6,
                    Filled: false
                ),
            ],
            Background
        );

        var surface = new RecordingSurface();
        new DefaultSceneRenderer(surface).Render(scene);
        return surface;
    }

    private sealed class RecordingSurface : IRenderSurface
    {
        public List<(string Kind, bool Filled, float Thickness)> Calls { get; } = [];
        public Color? ClearColor { get; private set; }

        public void Render(Action<ICanvas> draw) => draw(new RecordingCanvas(this));

        private sealed class RecordingCanvas(RecordingSurface surface) : ICanvas
        {
            public void Clear(Color color) => surface.ClearColor = color;

            public void DrawLine(Point a, Point b, Color color, float thickness) =>
                surface.Calls.Add(("line", false, thickness));

            public void DrawCircle(
                Point center,
                float radius,
                Color color,
                float thickness,
                bool filled
            ) => surface.Calls.Add(("circle", filled, thickness));

            public void DrawPolygon(
                IReadOnlyList<Point> points,
                Color color,
                float thickness,
                bool filled
            ) => surface.Calls.Add(("polygon", filled, thickness));
        }
    }
}
