using FluentAssertions;
using GeoTools.Shared.Application.Views;

namespace GeoTools.Shared.Application.Tests;

public sealed class NavigationMediatorTests
{
    [Fact]
    public void Starts_on_the_loading_view()
    {
        var mediator = new NavigationMediator();

        mediator.ActiveView.Should().Be(ViewId.Loading);
    }

    [Fact]
    public void NavigateTo_raises_one_change_with_the_previous_and_current_view()
    {
        var mediator = new NavigationMediator();
        var changes = new List<ViewChangedEventArgs>();
        mediator.ViewChanged += (_, args) => changes.Add(args);

        mediator.NavigateTo(ViewId.Maintenance);

        changes.Should().ContainSingle();
        changes[0].Previous.Should().Be(ViewId.Loading);
        changes[0].Current.Should().Be(ViewId.Maintenance);
        mediator.ActiveView.Should().Be(ViewId.Maintenance);
    }

    [Fact]
    public void NavigateTo_the_active_view_raises_nothing()
    {
        var mediator = new NavigationMediator();
        var changes = new List<ViewChangedEventArgs>();
        mediator.ViewChanged += (_, args) => changes.Add(args);

        mediator.NavigateTo(ViewId.Loading);

        changes.Should().BeEmpty();
    }

    [Fact]
    public void NextView_wraps_past_the_last_navigable_view_to_the_first()
    {
        var mediator = new NavigationMediator();
        mediator.NavigateTo(ViewId.Maintenance);

        mediator.NextView();

        mediator.ActiveView.Should().Be(ViewId.Map);
    }

    [Fact]
    public void PreviousView_wraps_from_the_first_navigable_view_to_the_last()
    {
        var mediator = new NavigationMediator();
        mediator.NavigateTo(ViewId.Map);

        mediator.PreviousView();

        mediator.ActiveView.Should().Be(ViewId.Maintenance);
    }

    [Fact]
    public void NextView_from_loading_goes_to_the_map()
    {
        var mediator = new NavigationMediator();

        mediator.NextView();

        mediator.ActiveView.Should().Be(ViewId.Map);
    }

    [Fact]
    public void NavigateTo_an_undefined_view_throws()
    {
        var mediator = new NavigationMediator();

        var act = () => mediator.NavigateTo((ViewId)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
