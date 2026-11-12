using FluentAssertions;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class ViewCatalogTests
{
    [Fact]
    public void Constructing_without_a_view_for_every_id_throws()
    {
        var act = () => new ViewCatalog([new TestView(ViewId.Map)]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Settings*");
    }

    [Fact]
    public void Constructing_with_duplicate_view_ids_throws()
    {
        var act = () =>
            new ViewCatalog([
                new TestView(ViewId.Map),
                new TestView(ViewId.Map),
                new TestView(ViewId.Settings),
            ]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Map*");
    }

    [Fact]
    public void Get_returns_the_registered_view()
    {
        var map = new TestView(ViewId.Map);
        var settings = new TestView(ViewId.Settings);
        var catalog = new ViewCatalog([map, settings]);

        catalog.Get(ViewId.Map).Should().BeSameAs(map);
        catalog.Get(ViewId.Settings).Should().BeSameAs(settings);
    }
}
