using FluentAssertions;
using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class SqliteMessageBusEdgeTests
{
    [Fact]
    public async Task Publishing_after_stop_fails()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var bus = CreateBus(database);
        await bus.StartAsync();
        await bus.StopAsync();
        await bus.StopAsync();

        var act = async () => await bus.PublishAsync(Message("a"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Resetting_an_unknown_subscription_and_pruning_without_subscribers_are_safe()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var bus = CreateBus(database);

        var act = async () =>
        {
            await bus.ResetSubscriptionAsync("missing");
            await bus.PruneAsync();
        };

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Dispatching_with_no_messages_returns_zero()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var bus = CreateBus(database);

        (await bus.DispatchOnceAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Subscribing_twice_updates_the_topic_filter()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var bus = CreateBus(database);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await bus.SubscribeAsync("updated", [MessageTopics.SettingsChanged], (_, _) =>
            Task.CompletedTask
        );
        await bus.SubscribeAsync(
            "updated",
            [MessageTopics.SettingsChanged, MessageTopics.FeatureFlagChanged],
            (envelope, _) =>
            {
                if (envelope.Topic == MessageTopics.FeatureFlagChanged)
                    done.TrySetResult();
                return Task.CompletedTask;
            }
        );
        await bus.StartAsync();

        await bus.PublishAsync(
            new FeatureFlagChanged(FeatureFlag.MapGrid, false, DateTimeOffset.UnixEpoch)
        );

        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await bus.StopAsync();
    }

    private static SqliteMessageBus CreateBus(TestDatabase database) =>
        new(database.Factory, TimeProvider.System, NullLogger<SqliteMessageBus>.Instance);

    private static SettingsChanged Message(string key) =>
        new(key, "value", DateTimeOffset.UnixEpoch);
}
