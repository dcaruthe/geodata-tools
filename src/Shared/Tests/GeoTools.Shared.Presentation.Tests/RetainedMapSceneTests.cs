using FluentAssertions;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class RetainedMapSceneTests
{
    private static readonly ViewSize Size = new(1000, 1000);
    private static readonly WorldRect Visible = new(0.4, 0.4, 0.6, 0.6);

    [Fact]
    public void Build_filters_by_admin_level()
    {
        var scene = new RetainedMapScene();

        scene.Build(
            Geometry(Ring(2, 0.1, 0.1, 0.2, 0.2), Ring(4, 0.6, 0.6, 0.7, 0.7)),
            [2],
            Camera(),
            Size,
            WorldRect.World
        );

        scene.RetainedCount.Should().Be(1);
        scene.BuildCount.Should().Be(1);
    }

    [Fact]
    public void Build_culls_to_the_coverage_rect()
    {
        var scene = new RetainedMapScene();

        scene.Build(
            Geometry(Ring(2, 0.1, 0.1, 0.2, 0.2), Ring(2, 0.6, 0.6, 0.7, 0.7)),
            [2],
            Camera(),
            Size,
            new WorldRect(0.05, 0.05, 0.25, 0.25)
        );

        scene.RetainedCount.Should().Be(1);
    }

    [Fact]
    public void Shapes_returns_only_polylines_intersecting_the_visible_rect()
    {
        var scene = new RetainedMapScene();
        scene.Build(
            Geometry(Ring(2, 0.1, 0.1, 0.2, 0.2), Ring(2, 0.6, 0.6, 0.7, 0.7)),
            [2],
            Camera(),
            Size,
            WorldRect.World
        );
        var camera = Camera();
        var viewport = camera.Viewport(Size.Width, Size.Height);

        var shapes = scene.Shapes(new WorldRect(0.25, 0.25, 0.75, 0.75), viewport);

        shapes.Should().ContainSingle().Which.Should().BeOfType<PolygonDto>();
        scene.RetainedCount.Should().Be(2);
        scene.VisibleCount.Should().Be(1);
    }

    [Fact]
    public void Transform_shifts_points_by_the_expected_pixels()
    {
        var scene = Built();
        var camera = Camera();
        var before = FirstPoint(scene, camera);

        var moved = camera.Pan(64, 0, Size.Width, Size.Height);
        scene.TransformTo(moved, Size);
        var after = FirstPoint(scene, moved);

        after.X.Should().BeApproximately(before.X + 64, 0.001f);
        after.Y.Should().BeApproximately(before.Y, 0.001f);
        scene.TransformCount.Should().Be(1);
    }

    [Fact]
    public void Transform_updates_bounds_so_a_panned_shape_leaves_the_view()
    {
        var scene = Built();
        var camera = Camera();
        scene.Shapes(WorldRect.World, camera.Viewport(Size.Width, Size.Height));

        var moved = camera.Pan(1200, 0, Size.Width, Size.Height);
        scene.TransformTo(moved, Size);
        var movedViewport = moved.Viewport(Size.Width, Size.Height);

        scene
            .Shapes(movedViewport.WorldBounds(Size.Width, Size.Height), movedViewport)
            .Should()
            .BeEmpty();
        scene.RetainedCount.Should().Be(1);
        scene.VisibleCount.Should().Be(0);
    }

    [Fact]
    public void Transform_scales_points_about_the_screen_center_for_a_zoom()
    {
        var scene = Built();
        var camera = Camera();
        var before = FirstPoint(scene, camera);

        var zoomed = camera.Zoom(2);
        scene.TransformTo(zoomed, Size);
        var after = FirstPoint(scene, zoomed);

        after.X.Should().BeApproximately(500 + (before.X - 500) * 2, 0.001f);
        after.Y.Should().BeApproximately(500 + (before.Y - 500) * 2, 0.001f);
        scene.TransformCount.Should().Be(1);
    }

    [Fact]
    public void Transform_does_nothing_for_an_unchanged_camera_or_a_different_size()
    {
        var scene = Built();

        new RetainedMapScene().TransformTo(Camera(), Size);
        scene.TransformTo(Camera(), Size);
        scene.TransformTo(Camera(), new ViewSize(900, 700));

        scene.TransformCount.Should().Be(0);
    }

    [Fact]
    public void CanTransformTo_rejects_an_unbuilt_scene()
    {
        new RetainedMapScene()
            .CanTransformTo(1, 1, [2], true, true, Size, Camera(), Visible)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void CanTransformTo_rejects_a_version_change()
    {
        var scene = Built(geometryVersion: 1, settingsVersion: 1);

        scene.CanTransformTo(2, 1, [2], true, true, Size, Camera(), Visible).Should().BeFalse();
        scene.CanTransformTo(1, 2, [2], true, true, Size, Camera(), Visible).Should().BeFalse();
    }

    [Fact]
    public void CanTransformTo_rejects_a_level_change()
    {
        var scene = Built();

        scene.CanTransformTo(1, 1, [2, 4], true, true, Size, Camera(), Visible).Should().BeFalse();
    }

    [Fact]
    public void CanTransformTo_rejects_a_size_change()
    {
        var scene = Built();

        scene
            .CanTransformTo(1, 1, [2], true, true, new ViewSize(900, 700), Camera(), Visible)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void CanTransformTo_rejects_a_flag_change()
    {
        var scene = Built(grid: true, crosshair: true);

        scene.CanTransformTo(1, 1, [2], false, true, Size, Camera(), Visible).Should().BeFalse();
        scene.CanTransformTo(1, 1, [2], true, false, Size, Camera(), Visible).Should().BeFalse();
    }

    [Fact]
    public void CanTransformTo_rejects_a_view_outside_the_coverage()
    {
        var scene = Built(coverage: new WorldRect(0.3, 0.3, 0.7, 0.7));

        scene
            .CanTransformTo(
                1,
                1,
                [2],
                true,
                true,
                Size,
                Camera(),
                new WorldRect(0.6, 0.6, 0.8, 0.8)
            )
            .Should()
            .BeFalse();
        scene.CanTransformTo(1, 1, [2], true, true, Size, Camera(), Visible).Should().BeTrue();
    }

    [Fact]
    public void CanTransformTo_accepts_a_scale_change_inside_the_coverage()
    {
        var scene = Built();

        scene
            .CanTransformTo(1, 1, [2], true, true, Size, Camera(scale: 4000), Visible)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Coverage_adds_one_full_viewport_of_margin_around_the_view()
    {
        var camera = new MapCamera(0.5, 0.5, 4000);
        var size = new ViewSize(800, 600);
        var visible = camera.Viewport(size.Width, size.Height).WorldBounds(size.Width, size.Height);

        var coverage = RetainedMapScene.Coverage(visible, camera, size);

        coverage.MinX.Should().BeApproximately(0.2, 1e-9);
        coverage.MinY.Should().BeApproximately(0.275, 1e-9);
        coverage.MaxX.Should().BeApproximately(0.8, 1e-9);
        coverage.MaxY.Should().BeApproximately(0.725, 1e-9);
    }

    [Fact]
    public void Coverage_clamps_to_the_world_when_the_view_stays_inside()
    {
        var camera = new MapCamera(0.5, 0.5, 1000);
        var size = new ViewSize(800, 600);
        var visible = camera.Viewport(size.Width, size.Height).WorldBounds(size.Width, size.Height);

        RetainedMapScene.Coverage(visible, camera, size).Should().Be(WorldRect.World);
    }

    [Fact]
    public void Coverage_falls_back_to_the_world_for_an_unusable_camera()
    {
        RetainedMapScene
            .Coverage(Visible, new MapCamera(0, 0, 0), Size)
            .Should()
            .Be(WorldRect.World);
    }

    [Fact]
    public void Coverage_keeps_the_margin_when_clamping_would_exclude_the_view()
    {
        var visible = new WorldRect(-0.1, 0.4, 0.1, 0.6);
        var camera = new MapCamera(0, 0.5, 1000);

        var coverage = RetainedMapScene.Coverage(visible, camera, new ViewSize(100, 100));

        coverage.MinX.Should().BeApproximately(-0.2, 1e-9);
        coverage.MaxX.Should().BeApproximately(0.2, 1e-9);
    }

    private static RetainedMapScene Built(
        long geometryVersion = 1,
        long settingsVersion = 1,
        bool grid = true,
        bool crosshair = true,
        WorldRect? coverage = null
    )
    {
        var scene = new RetainedMapScene();
        scene.Build(
            Geometry(Ring(2, 0.4, 0.4, 0.6, 0.6)),
            [2],
            Camera(),
            Size,
            coverage ?? WorldRect.World,
            grid,
            crosshair,
            geometryVersion,
            settingsVersion
        );
        return scene;
    }

    private static Point FirstPoint(RetainedMapScene scene, MapCamera camera)
    {
        var viewport = camera.Viewport(Size.Width, Size.Height);
        var shapes = scene.Shapes(WorldRect.World, viewport);
        shapes.Should().ContainSingle();
        return ((PolygonDto)shapes[0]).Points[0];
    }

    private static MapCamera Camera(double x = 0.5, double y = 0.5, double scale = 2000) =>
        new(x, y, scale);

    private static BorderGeometry Geometry(params WorldPolyline[] polylines) =>
        new([], polylines, GeoBounds.World);

    private static WorldPolyline Ring(
        int adminLevel,
        double minX,
        double minY,
        double maxX,
        double maxY
    ) =>
        new(
            [
                new WorldPoint(minX, minY),
                new WorldPoint(maxX, minY),
                new WorldPoint(maxX, maxY),
                new WorldPoint(minX, maxY),
                new WorldPoint(minX, minY),
            ],
            true,
            adminLevel,
            new WorldRect(minX, minY, maxX, maxY)
        );
}
