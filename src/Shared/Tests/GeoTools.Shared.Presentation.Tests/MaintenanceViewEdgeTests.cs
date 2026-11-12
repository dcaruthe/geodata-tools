using FluentAssertions;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class MaintenanceViewEdgeTests
{
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
    public void Compose_marks_rows_below_the_window()
    {
        var fixture = new ControllerFixture();

        var texts = Texts(fixture.Controller, new ViewSize(1200, 300));

        texts.Should().Contain("more below");
    }

    [Fact]
    public void Compose_marks_rows_above_the_window()
    {
        var fixture = new ControllerFixture();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (fixture.Controller.Snapshot.Selected?.Id == "action:reset")
                break;
            fixture.Controller.Handle(ViewKey.Down);
        }

        var texts = Texts(fixture.Controller, new ViewSize(1200, 300));

        texts.Should().Contain("more above");
    }

    [Fact]
    public void Compose_prefixes_destructive_actions()
    {
        var fixture = new ControllerFixture();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (fixture.Controller.Snapshot.Selected?.Id == "action:repull")
                break;
            fixture.Controller.Handle(ViewKey.Down);
        }

        var texts = Texts(fixture.Controller, new ViewSize(1200, 800));

        texts.Should().Contain("! Force repull");
    }

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
                new FakeMessageBus(),
                Microsoft.Extensions.Options.Options.Create(
                    new GeoTools.Shared.Application.Options.GeodataOptions { Regions = ["africa"] }
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
