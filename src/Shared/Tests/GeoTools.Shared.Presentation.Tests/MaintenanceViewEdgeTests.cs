using FluentAssertions;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class MaintenanceViewEdgeTests
{
    // The default Skia face at 14 px has an 11 px ascent and a 3 px descent. The selection bar
    // has to cover that ink without touching the row above or below.
    private const float TextAscent = 11;
    private const float TextDescent = 3;

    [Fact]
    public void Compose_returns_the_cached_scene()
    {
        var view = new MaintenanceView(new ControllerFixture().Controller);

        var first = view.Compose(new ViewSize(1200, 800));
        var second = view.Compose(new ViewSize(1200, 800));

        second.Should().BeSameAs(first);
    }

    [Fact]
    public void Compose_uses_the_combined_text_at_a_narrow_width()
    {
        var fixture = new ControllerFixture();
        fixture.Controller.Handle(ViewKey.Down);

        var texts = Texts(fixture.Controller, new ViewSize(600, 500));

        texts.Should().Contain(text => text.Contains("Display levels:"));
    }

    [Fact]
    public void Compose_shows_every_row_at_the_default_size()
    {
        var fixture = new ControllerFixture();

        var texts = Texts(fixture.Controller, new ViewSize(1200, 900));

        texts.Should().Contain("! Reset settings");
    }

    [Fact]
    public void Compose_renders_sections_as_small_uppercase_labels()
    {
        var fixture = new ControllerFixture();

        var texts = Texts(fixture.Controller, new ViewSize(1200, 800));

        texts.Should().Contain("DISPLAY");
        texts.Should().Contain("ACTIONS");
    }

    [Fact]
    public void Compose_encloses_the_selected_row_in_the_bar()
    {
        var fixture = new ControllerFixture();

        var scene = new MaintenanceView(fixture.Controller).Compose(new ViewSize(1200, 900));
        var selected = Single(scene, "Display levels");
        var next = Single(scene, "Simplify tolerance");
        var bar = scene.Shapes.OfType<PolygonDto>().Single(shape => shape.Filled);

        var top = bar.Points.Min(point => point.Y);
        var bottom = bar.Points.Max(point => point.Y);

        top.Should().BeLessThanOrEqualTo(selected.Position.Y - TextAscent);
        bottom.Should().BeGreaterThanOrEqualTo(selected.Position.Y + TextDescent);
        bottom.Should().BeLessThan(next.Position.Y - TextAscent);
    }

    [Fact]
    public void Compose_keeps_rows_above_the_confirmation_prompt_in_a_short_window()
    {
        var fixture = new ControllerFixture();
        fixture.Controller.SelectRow("action:repull").Accepted.Should().BeTrue();
        fixture.Controller.Handle(ViewKey.Enter);

        var scene = new MaintenanceView(fixture.Controller).Compose(new ViewSize(1200, 260));
        var prompt = Single(scene, "Enter again to confirm. Moving or leaving cancels.");
        var rows = scene
            .Shapes.OfType<TextDto>()
            .Where(text => text.Size == 14 && text.Position.X == 48)
            .Where(text => text.Position.Y > 120 && text.Position.Y < prompt.Position.Y)
            .ToArray();

        rows.Should().NotBeEmpty();
        rows.Max(text => text.Position.Y)
            .Should()
            .BeLessThanOrEqualTo(prompt.Position.Y - TextAscent - 6);
    }

    [Fact]
    public void Compose_splits_the_key_hint_at_a_narrow_width()
    {
        var fixture = new ControllerFixture();

        var texts = Texts(fixture.Controller, new ViewSize(420, 800));

        texts.Should().Contain("Up/Down select   Left/Right change");
        texts.Should().Contain("Enter run   Backspace cancel   Home/End jump");
    }

    [Fact]
    public void Compose_draws_no_scrollbar_when_every_row_fits()
    {
        var fixture = new ControllerFixture();

        var scene = new MaintenanceView(fixture.Controller).Compose(new ViewSize(1200, 900));

        FilledBars(scene).Should().ContainSingle();
    }

    [Fact]
    public void Compose_draws_a_scrollbar_when_rows_are_hidden()
    {
        var fixture = new ControllerFixture();

        var scene = new MaintenanceView(fixture.Controller).Compose(new ViewSize(1200, 300));

        var scrollbar = FilledBars(scene)
            .Where(bar => Width(bar) <= 8 && bar.Points.Min(point => point.X) > 1160)
            .ToArray();

        scrollbar.Should().HaveCount(2);
    }

    [Fact]
    public void Compose_moves_the_scrollbar_thumb_to_the_bottom_on_the_last_row()
    {
        var fixture = new ControllerFixture();
        fixture.Controller.SelectRow("action:reset").Accepted.Should().BeTrue();

        var scene = new MaintenanceView(fixture.Controller).Compose(new ViewSize(1200, 300));
        var bars = FilledBars(scene)
            .Where(bar => Width(bar) <= 8 && bar.Points.Min(point => point.X) > 1160)
            .OrderBy(bar => bar.Points.Min(point => point.Y))
            .ToArray();

        bars.Should().HaveCount(2);
        Height(bars[0]).Should().BeGreaterThan(Height(bars[1]));
        bars[1]
            .Points.Min(point => point.Y)
            .Should()
            .BeGreaterThan(bars[0].Points.Min(point => point.Y));
    }

    [Fact]
    public void Compose_prefixes_destructive_actions()
    {
        var fixture = new ControllerFixture();
        fixture.Controller.SelectRow("action:repull");

        var texts = Texts(fixture.Controller, new ViewSize(1200, 800));

        texts.Should().Contain("! Force repull");
    }

    private static PolygonDto[] FilledBars(SceneDto scene) =>
        scene.Shapes.OfType<PolygonDto>().Where(shape => shape.Filled).ToArray();

    private static TextDto Single(SceneDto scene, string text) =>
        scene.Shapes.OfType<TextDto>().Single(item => item.Text == text);

    private static float Height(PolygonDto polygon) =>
        polygon.Points.Max(point => point.Y) - polygon.Points.Min(point => point.Y);

    private static float Width(PolygonDto polygon) =>
        polygon.Points.Max(point => point.X) - polygon.Points.Min(point => point.X);

    private static string[] Texts(MaintenanceController controller, ViewSize size) =>
        new MaintenanceView(controller)
            .Compose(size)
            .Shapes.OfType<TextDto>()
            .Select(text => text.Text)
            .ToArray();

    private sealed class ControllerFixture
    {
        public ControllerFixture()
        {
            Controller = new MaintenanceController(
                new TestSettingsStore(),
                new FakeJobStore(),
                new FakeArtifactStore(),
                new ProgressStore(),
                new BorderGeometryStore(),
                new FakeRegionIndex(),
                new FakeRegionIndexWriter(),
                new FakeMessageBus(),
                Microsoft.Extensions.Options.Options.Create(
                    new GeodataOptions
                    {
                        Regions =
                        [
                            "africa",
                            "antarctica",
                            "asia",
                            "australia-oceania",
                            "central-america",
                            "europe",
                            "north-america",
                            "south-america",
                        ],
                    }
                ),
                new FakeNavigationMediator(),
                TimeProvider.System,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MaintenanceController>.Instance
            );
            Controller.AttachAsync(new FakeMessageBus()).GetAwaiter().GetResult();
        }

        public MaintenanceController Controller { get; }
    }
}
