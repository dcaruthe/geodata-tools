using FluentAssertions;
using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Presentation.Views;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class ViewTransitionSubscriberTests
{
    [Fact]
    public async Task Border_data_switches_the_loading_view_to_the_map()
    {
        var mediator = new FakeNavigationMediator();
        var bus = new FakeMessageBus();
        var subscriber = Create(mediator);
        await subscriber.AttachAsync(bus);

        await bus.DeliverAsync(
            MessageTopics.BordersAvailable,
            new BordersAvailable("key", 10, [2], GeoBounds.World, DateTimeOffset.UnixEpoch)
        );

        mediator.ActiveView.Should().Be(ViewId.Map);
    }

    [Fact]
    public async Task A_manual_view_is_not_interrupted()
    {
        var mediator = new FakeNavigationMediator();
        mediator.NavigateTo(ViewId.Maintenance);
        var bus = new FakeMessageBus();
        var subscriber = Create(mediator);
        await subscriber.AttachAsync(bus);

        await bus.DeliverAsync(
            MessageTopics.BordersAvailable,
            new BordersAvailable("key", 10, [2], GeoBounds.World, DateTimeOffset.UnixEpoch)
        );

        mediator.ActiveView.Should().Be(ViewId.Maintenance);
    }

    [Fact]
    public async Task A_successful_run_opens_the_map()
    {
        var mediator = new FakeNavigationMediator();
        var bus = new FakeMessageBus();
        var subscriber = Create(mediator);
        await subscriber.AttachAsync(bus);

        await bus.DeliverAsync(
            MessageTopics.BootstrapRunFinished,
            new BootstrapRunFinished(
                RunId.New(),
                RunStatus.Succeeded,
                null,
                1,
                DateTimeOffset.UnixEpoch
            )
        );

        mediator.ActiveView.Should().Be(ViewId.Map);
    }

    [Fact]
    public async Task A_failed_run_stays_on_the_loading_view()
    {
        var mediator = new FakeNavigationMediator();
        var bus = new FakeMessageBus();
        var subscriber = Create(mediator);
        await subscriber.AttachAsync(bus);

        await bus.DeliverAsync(
            MessageTopics.BootstrapRunFinished,
            new BootstrapRunFinished(
                RunId.New(),
                RunStatus.Failed,
                "boom",
                1,
                DateTimeOffset.UnixEpoch
            )
        );

        mediator.ActiveView.Should().Be(ViewId.Loading);
    }

    private static ViewTransitionSubscriber Create(INavigationMediator mediator) =>
        new(mediator, NullLogger<ViewTransitionSubscriber>.Instance);
}
