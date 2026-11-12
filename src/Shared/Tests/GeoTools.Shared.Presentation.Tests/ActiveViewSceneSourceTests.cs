using FluentAssertions;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Views;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class ActiveViewSceneSourceTests
{
    [Fact]
    public void Compose_follows_the_active_view()
    {
        var mapView = new TestView(ViewId.Map, new Color(1, 2, 3));
        var settingsView = new TestView(ViewId.Settings, new Color(4, 5, 6));
        var catalog = new ViewCatalog([mapView, settingsView]);
        var mediator = new FakeNavigationMediator();
        var source = new ActiveViewSceneSource(
            catalog,
            mediator,
            NullLogger<ActiveViewSceneSource>.Instance
        );

        source.Compose(new ViewSize(100, 100)).Background.Should().Be(mapView.Background);

        mediator.NavigateTo(ViewId.Settings);

        source.Compose(new ViewSize(100, 100)).Background.Should().Be(settingsView.Background);
    }
}
