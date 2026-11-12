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
        var loading = new TestView(ViewId.Loading, new Color(1, 2, 3));
        var map = new TestView(ViewId.Map, new Color(4, 5, 6));
        var maintenance = new TestView(ViewId.Maintenance, new Color(7, 8, 9));
        var catalog = new ViewCatalog([loading, map, maintenance]);
        var mediator = new FakeNavigationMediator();
        var source = new ActiveViewSceneSource(
            catalog,
            mediator,
            NullLogger<ActiveViewSceneSource>.Instance
        );

        source.Compose(new ViewSize(100, 100)).Background.Should().Be(loading.Background);

        mediator.NavigateTo(ViewId.Maintenance);
        source.Compose(new ViewSize(100, 100)).Background.Should().Be(maintenance.Background);

        mediator.NavigateTo(ViewId.Map);
        source.Compose(new ViewSize(100, 100)).Background.Should().Be(map.Background);
    }
}
