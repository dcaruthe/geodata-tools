using FluentAssertions;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class ViewCatalogTests
{
    [Fact]
    public void Constructing_without_a_view_for_every_id_throws()
    {
        var act = () => new ViewCatalog([new TestView(ViewId.Loading), new TestView(ViewId.Map)]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Maintenance*");
    }

    [Fact]
    public void Constructing_with_duplicate_view_ids_throws()
    {
        var act = () =>
            new ViewCatalog([
                new TestView(ViewId.Loading),
                new TestView(ViewId.Map),
                new TestView(ViewId.Map),
                new TestView(ViewId.Maintenance),
            ]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Map*");
    }

    [Fact]
    public void Get_returns_the_registered_view()
    {
        var loading = new TestView(ViewId.Loading);
        var map = new TestView(ViewId.Map);
        var maintenance = new TestView(ViewId.Maintenance);
        var catalog = new ViewCatalog([loading, map, maintenance]);

        catalog.Get(ViewId.Loading).Should().BeSameAs(loading);
        catalog.Get(ViewId.Map).Should().BeSameAs(map);
        catalog.Get(ViewId.Maintenance).Should().BeSameAs(maintenance);
    }
}
