using FluentAssertions;
using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Infrastructure.Messaging;
using GeoTools.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class SqliteMessageBusTests
{
    private static SqliteMessageBus CreateBus(TestDatabase database) =>
        new(database.Factory, TimeProvider.System, NullLogger<SqliteMessageBus>.Instance);

    [Fact]
    public async Task Publish_assigns_increasing_sequences()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var bus = CreateBus(database);
        await bus.StartAsync();

        var first = await bus.PublishAsync(Message("a"));
        var second = await bus.PublishAsync(Message("b"));

        second.Should().BeGreaterThan(first);
        await bus.StopAsync();
    }

    [Fact]
    public async Task Subscriber_receives_its_topics_in_sequence_order()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var bus = CreateBus(database);
        var received = new List<string>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await bus.SubscribeAsync("ordered", [MessageTopics.SettingsChanged], (envelope, _) =>
        {
            var message = MessageSerialization.Deserialize<SettingsChanged>(envelope);
            lock (received)
            {
                received.Add(message.Key);
                if (received.Count == 3)
                    done.TrySetResult();
            }

            return Task.CompletedTask;
        });
        await bus.StartAsync();

        await bus.PublishAsync(Message("a"));
        await bus.PublishAsync(Message("b"));
        await bus.PublishAsync(Message("c"));

        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        received.Should().Equal("a", "b", "c");
        await bus.StopAsync();
    }

    [Fact]
    public async Task Subscriber_ignores_topics_it_did_not_subscribe_to()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var bus = CreateBus(database);
        var received = new List<string>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await bus.SubscribeAsync("filtered", [MessageTopics.SettingsChanged], (envelope, _) =>
        {
            lock (received)
                received.Add(envelope.Topic);
            done.TrySetResult();
            return Task.CompletedTask;
        });
        await bus.StartAsync();

        await bus.PublishAsync(
            new FeatureFlagChanged(
                GeoTools.Shared.Application.State.FeatureFlag.MapGrid,
                false,
                DateTimeOffset.UnixEpoch
            )
        );
        await bus.PublishAsync(Message("a"));

        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        received.Should().Equal(MessageTopics.SettingsChanged);
        await bus.StopAsync();
    }

    [Fact]
    public async Task Cursor_survives_a_restart_and_does_not_replay()
    {
        await using var database = await TestDatabase.CreateAsync();
        var received = new List<string>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using (var first = CreateBus(database))
        {
            await first.SubscribeAsync(
                "persistent",
                [MessageTopics.SettingsChanged],
                (envelope, _) =>
                {
                    lock (received)
                    {
                        received.Add(
                            MessageSerialization.Deserialize<SettingsChanged>(envelope).Key
                        );
                        done.TrySetResult();
                    }

                    return Task.CompletedTask;
                }
            );
            await first.StartAsync();
            await first.PublishAsync(Message("a"));
            await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await first.StopAsync();
        }

        var secondDone = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        await using (var second = CreateBus(database))
        {
            await second.SubscribeAsync(
                "persistent",
                [MessageTopics.SettingsChanged],
                (envelope, _) =>
                {
                    lock (received)
                        received.Add(
                            MessageSerialization.Deserialize<SettingsChanged>(envelope).Key
                        );
                    secondDone.TrySetResult();
                    return Task.CompletedTask;
                }
            );
            await second.StartAsync();
            await second.PublishAsync(Message("b"));
            await secondDone.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await second.StopAsync();
        }

        received.Should().Equal("a", "b");
    }

    [Fact]
    public async Task A_failed_handler_is_redelivered()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var bus = CreateBus(database);
        var attempts = 0;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await bus.SubscribeAsync("retrying", [MessageTopics.SettingsChanged], (_, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new InvalidOperationException("transient");

            done.TrySetResult();
            return Task.CompletedTask;
        });
        await bus.StartAsync();
        await bus.PublishAsync(Message("a"));

        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        attempts.Should().Be(2);
        await bus.StopAsync();
    }

    [Fact]
    public async Task Five_failures_park_the_subscriber_and_reset_unparks_it()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var bus = CreateBus(database);
        var allow = false;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await bus.SubscribeAsync("parked", [MessageTopics.SettingsChanged], (_, _) =>
        {
            if (!Volatile.Read(ref allow))
                throw new InvalidOperationException("keep failing");

            done.TrySetResult();
            return Task.CompletedTask;
        });
        await bus.StartAsync();
        await bus.PublishAsync(Message("a"));
        await WaitForAsync(
            async () =>
            {
                await using var context = await database.Factory.CreateDbContextAsync();
                var row = await context.Subscriptions.SingleAsync(subscription =>
                    subscription.Subscriber == "parked"
                );
                return row.ParkedAt is not null;
            },
            TimeSpan.FromSeconds(10)
        );

        await bus.ResetSubscriptionAsync("parked");
        Volatile.Write(ref allow, true);

        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var check = await database.Factory.CreateDbContextAsync();
        var reset = await check.Subscriptions.SingleAsync(subscription =>
            subscription.Subscriber == "parked"
        );
        reset.ParkedAt.Should().BeNull();
        reset.LastError.Should().BeNull();
        await bus.StopAsync();
    }

    [Fact]
    public async Task Prune_removes_old_acknowledged_messages_only()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var bus = CreateBus(database);
        await bus.SubscribeAsync(
            "pruner",
            [MessageTopics.SettingsChanged],
            (_, _) => Task.CompletedTask,
            startAt: long.MaxValue
        );
        await bus.StartAsync();

        await using (var context = await database.Factory.CreateDbContextAsync())
        {
            context.Messages.AddRange(
                new MessageRow
                {
                    Topic = MessageTopics.SettingsChanged,
                    Body = MessageSerialization.Serialize(Message("old")),
                    PublishedAt = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds(),
                    Publisher = "test",
                },
                new MessageRow
                {
                    Topic = MessageTopics.SettingsChanged,
                    Body = MessageSerialization.Serialize(Message("new")),
                    PublishedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Publisher = "test",
                }
            );
            await context.SaveChangesAsync();
        }

        await bus.PruneAsync();

        await using var verify = await database.Factory.CreateDbContextAsync();
        var topics = await verify.Messages.Select(row => row.Topic).ToListAsync();
        topics.Should().ContainSingle().Which.Should().Be(MessageTopics.SettingsChanged);
        await bus.StopAsync();
    }

    [Fact]
    public async Task Subscribe_requires_at_least_one_topic()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var bus = CreateBus(database);

        var act = async () => await bus.SubscribeAsync("empty", [], (_, _) => Task.CompletedTask);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private static SettingsChanged Message(string key) =>
        new(key, "value", DateTimeOffset.UnixEpoch);

    private static async Task WaitForAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
                return;

            await Task.Delay(100);
        }

        throw new TimeoutException("The condition was not met before the timeout.");
    }
}
