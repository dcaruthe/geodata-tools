using FluentAssertions;
using GeoTools.Shared.Application.Views;

namespace GeoTools.Shared.Application.Tests;

public sealed class NavigationMediatorTests
{
    [Fact]
    public void Starts_on_the_map_view()
    {
        var mediator = new NavigationMediator();

        mediator.ActiveView.Should().Be(ViewId.Map);
    }

    [Fact]
    public void NavigateTo_raises_one_change_with_the_previous_and_current_view()
    {
        var mediator = new NavigationMediator();
        var changes = new List<ViewChangedEventArgs>();
        mediator.ViewChanged += (_, args) => changes.Add(args);

        mediator.NavigateTo(ViewId.Settings);

        changes.Should().ContainSingle();
        changes[0].Previous.Should().Be(ViewId.Map);
        changes[0].Current.Should().Be(ViewId.Settings);
        mediator.ActiveView.Should().Be(ViewId.Settings);
    }

    [Fact]
    public void NavigateTo_the_active_view_raises_nothing()
    {
        var mediator = new NavigationMediator();
        var changes = new List<ViewChangedEventArgs>();
        mediator.ViewChanged += (_, args) => changes.Add(args);

        mediator.NavigateTo(ViewId.Map);

        changes.Should().BeEmpty();
    }

    [Fact]
    public void NextView_wraps_past_the_last_view_to_the_first()
    {
        var mediator = new NavigationMediator();
        mediator.NavigateTo(ViewId.Settings);

        mediator.NextView();

        mediator.ActiveView.Should().Be(ViewId.Map);
    }

    [Fact]
    public void PreviousView_wraps_from_the_first_view_to_the_last()
    {
        var mediator = new NavigationMediator();

        mediator.PreviousView();

        mediator.ActiveView.Should().Be(ViewId.Settings);
    }

    [Fact]
    public void NavigateTo_an_undefined_view_throws()
    {
        var mediator = new NavigationMediator();

        var act = () => mediator.NavigateTo((ViewId)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
