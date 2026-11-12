using FluentAssertions;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Presentation.Views;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class MaintenanceViewTests
{
    [Fact]
    public async Task Compose_draws_the_title_rows_and_hint()
    {
        var (controller, _) = Create();
        await controller.AttachAsync(new FakeMessageBus());
        var view = new MaintenanceView(controller);

        var scene = view.Compose(new ViewSize(1200, 800));
        var texts = scene.Shapes.OfType<TextDto>().Select(text => text.Text).ToArray();

        texts.Should().Contain("Maintenance");
        texts.Should().Contain("Display levels");
        texts.Should().Contain(text => text.Contains("Up/Down"));
        view.Id.Should().Be(ViewId.Maintenance);
    }

    [Fact]
    public async Task Compose_shows_the_confirmation_prompt()
    {
        var (controller, _) = Create();
        await controller.AttachAsync(new FakeMessageBus());
        Select(controller, "action:repull");
        controller.Handle(ViewKey.Enter);
        var view = new MaintenanceView(controller);

        var scene = view.Compose(new ViewSize(1200, 800));

        scene
            .Shapes.OfType<TextDto>()
            .Select(text => text.Text)
            .Should()
            .Contain(text => text.Contains("Enter again to confirm"));
    }

    [Fact]
    public async Task Handle_is_delegated_to_the_controller()
    {
        var (controller, settings) = Create();
        await controller.AttachAsync(new FakeMessageBus());
        Select(controller, "flag:MapCrosshair");
        var view = new MaintenanceView(controller);
        var before = settings.IsEnabled(FeatureFlag.MapCrosshair);

        view.Handle(ViewKey.Enter);

        settings.IsEnabled(FeatureFlag.MapCrosshair).Should().Be(!before);
    }

    private static void Select(MaintenanceController controller, string id)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (controller.Snapshot.Selected?.Id == id)
                return;

            controller.Handle(ViewKey.Down);
        }

        throw new InvalidOperationException($"Could not select {id}.");
    }

    private static (MaintenanceController Controller, TestSettingsStore Settings) Create()
    {
        var settings = new TestSettingsStore();
        var controller = new MaintenanceController(
            settings,
            new FakeJobStore(),
            new FakeArtifactStore(),
            new ProgressStore(),
            new BorderGeometryStore(),
            new FakeMessageBus(),
            Microsoft.Extensions.Options.Options.Create(
                new GeodataOptions { Regions = ["africa"] }
            ),
            new FakeNavigationMediator(),
            TimeProvider.System,
            NullLogger<MaintenanceController>.Instance
        );

        return (controller, settings);
    }
}
