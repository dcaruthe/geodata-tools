using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GeoTools.Shared.Infrastructure.Messaging;

internal sealed class SqliteMessageBus : IMessageBus, IAsyncDisposable
{
    private const int WriteBatchSize = 32;
    private const int DispatchBatchSize = 128;
    private const int MaxAttempts = 5;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MessageRetention = TimeSpan.FromDays(7);

    private readonly IDbContextFactory<GeoToolsDbContext> _factory;
    private readonly TimeProvider _clock;
    private readonly ILogger<SqliteMessageBus> _logger;
    private readonly Channel<PendingMessage> _outbound = Channel.CreateUnbounded<PendingMessage>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }
    );
    private readonly SemaphoreSlim _signal = new(0);
    private readonly ConcurrentDictionary<string, Subscription> _subscriptions = new(
        StringComparer.Ordinal
    );
    private readonly CancellationTokenSource _cts = new();
    private Task? _writer;
    private Task? _dispatcher;
    private int _started;
    private int _stopped;
    private volatile bool _stopRequested;

    public SqliteMessageBus(
        IDbContextFactory<GeoToolsDbContext> factory,
        TimeProvider clock,
        ILogger<SqliteMessageBus> logger
    )
    {
        _factory = factory;
        _clock = clock;
        _logger = logger;
    }

    public async ValueTask<long> PublishAsync<TMessage>(
        TMessage message,
        MessagePublishOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var topic = MessageSerialization.TopicFor<TMessage>();
        var body = MessageSerialization.Serialize(message);
        var completion = new TaskCompletionSource<long>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var pending = new PendingMessage(
            topic,
            body,
            _clock.GetUtcNow(),
            options?.Publisher ?? "geotools",
            options?.CorrelationId,
            completion
        );

        if (!_outbound.Writer.TryWrite(pending))
            throw new InvalidOperationException("The message bus is stopping.");

        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SubscribeAsync(
        string subscriberId,
        IReadOnlyList<string> topics,
        Func<MessageEnvelope, CancellationToken, Task> handler,
        long? startAt = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriberId);
        if (topics.Count == 0)
            throw new ArgumentException("At least one topic is required.", nameof(topics));

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await context.Subscriptions.FindAsync([subscriberId], cancellationToken);
        if (row is null)
        {
            row = new SubscriptionRow
            {
                Subscriber = subscriberId,
                Topics = string.Join('\n', topics),
                LastSequence = startAt ?? await GetMaxSequenceAsync(context, cancellationToken),
            };
            context.Subscriptions.Add(row);
        }
        else
        {
            row.Topics = string.Join('\n', topics);
        }

        await context.SaveChangesAsync(cancellationToken);

        _subscriptions[subscriberId] = new Subscription(
            subscriberId,
            topics,
            row.LastSequence,
            row.Attempts,
            row.ParkedAt is not null,
            handler
        );
    }

    public async Task ResetSubscriptionAsync(
        string subscriberId,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var row = await context.Subscriptions.FindAsync([subscriberId], cancellationToken);
        if (row is null)
            return;

        row.Attempts = 0;
        row.ParkedAt = null;
        row.LastError = null;
        await context.SaveChangesAsync(cancellationToken);

        if (_subscriptions.TryGetValue(subscriberId, out var subscription))
            subscription.Unpark();
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            return Task.CompletedTask;

        _writer = Task.Run(() => WriteLoopAsync(_cts.Token), CancellationToken.None);
        _dispatcher = Task.Run(() => DispatchLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
            return;

        _outbound.Writer.TryComplete();

        if (_writer is not null)
        {
            try
            {
                await _writer.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("The message writer did not drain within 10 seconds.");
            }
        }

        // Let the dispatcher finish the batch it is on so it can persist its cursor.
        _stopRequested = true;
        _signal.Release();

        if (_dispatcher is not null)
        {
            try
            {
                await _dispatcher.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("The message dispatcher did not stop within 5 seconds.");
                await _cts.CancelAsync();
                try
                {
                    await _dispatcher.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                }
                catch (TimeoutException) { }
            }
        }

        await _cts.CancelAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts.Dispose();
        _signal.Dispose();
    }

    internal async Task<int> DispatchOnceAsync(CancellationToken cancellationToken = default)
    {
        var handled = 0;
        foreach (var subscription in _subscriptions.Values)
            handled += await DispatchSubscriptionAsync(subscription, cancellationToken);

        return handled;
    }

    internal async Task PruneAsync(CancellationToken cancellationToken = default)
    {
        var floor = _subscriptions
            .Values.Select(subscription => subscription.LastSequence)
            .DefaultIfEmpty(0)
            .Min();
        if (floor <= 0)
            return;

        var cutoff = StoreMappings.ToUnixMilliseconds(
            _clock.GetUtcNow().Subtract(MessageRetention)
        );

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        await context
            .Messages.Where(row => row.Sequence <= floor && row.PublishedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _outbound.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var batch = new List<PendingMessage>(WriteBatchSize);
                while (batch.Count < WriteBatchSize && _outbound.Reader.TryRead(out var pending))
                    batch.Add(pending);

                if (batch.Count == 0)
                    continue;

                await PersistBatchAsync(batch, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The message writer stopped.");
        }
    }

    private async Task PersistBatchAsync(
        List<PendingMessage> batch,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var context = await _factory.CreateDbContextAsync(cancellationToken);
            var rows = batch
                .Select(pending => new MessageRow
                {
                    Topic = pending.Topic,
                    Body = pending.Body,
                    PublishedAt = StoreMappings.ToUnixMilliseconds(pending.PublishedAt),
                    Publisher = pending.Publisher,
                    CorrelationId = pending.CorrelationId,
                })
                .ToList();

            context.Messages.AddRange(rows);
            await context.SaveChangesAsync(cancellationToken);

            for (var index = 0; index < batch.Count; index++)
                batch[index].Completion.TrySetResult(rows[index].Sequence);

            _signal.Release();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not append {Count} messages.", batch.Count);
            foreach (var pending in batch)
                pending.Completion.TrySetException(exception);
        }
    }

    private async Task DispatchLoopAsync(CancellationToken cancellationToken)
    {
        var sincePrune = Stopwatch.StartNew();

        try
        {
            while (!_stopRequested && !cancellationToken.IsCancellationRequested)
            {
                var handled = await DispatchOnceAsync(cancellationToken).ConfigureAwait(false);

                if (sincePrune.Elapsed >= PruneInterval)
                {
                    await PruneAsync(cancellationToken).ConfigureAwait(false);
                    sincePrune.Restart();
                }

                if (handled == 0)
                    await _signal.WaitAsync(PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The message dispatcher stopped.");
        }
    }

    private async Task<int> DispatchSubscriptionAsync(
        Subscription subscription,
        CancellationToken cancellationToken
    )
    {
        if (subscription.Parked)
            return 0;

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var topics = subscription.Topics;
        var rows = await context
            .Messages.AsNoTracking()
            .Where(row => topics.Contains(row.Topic) && row.Sequence > subscription.LastSequence)
            .OrderBy(row => row.Sequence)
            .Take(DispatchBatchSize)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return 0;

        var handled = 0;
        foreach (var row in rows)
        {
            var envelope = new MessageEnvelope(
                row.Sequence,
                row.Topic,
                row.Body,
                StoreMappings.FromUnixMilliseconds(row.PublishedAt),
                row.Publisher,
                row.CorrelationId
            );

            try
            {
                await subscription.Handler(envelope, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await SaveCursorAsync(context, subscription, cancellationToken)
                    .ConfigureAwait(false);
                await RecordFailureAsync(context, subscription, row, exception, cancellationToken)
                    .ConfigureAwait(false);
                return handled;
            }

            subscription.LastSequence = row.Sequence;
            subscription.Attempts = 0;
            handled++;
        }

        await SaveCursorAsync(context, subscription, cancellationToken).ConfigureAwait(false);
        return handled;
    }

    private async Task SaveCursorAsync(
        GeoToolsDbContext context,
        Subscription subscription,
        CancellationToken cancellationToken
    )
    {
        var row = await context.Subscriptions.FindAsync([subscription.Id], cancellationToken);
        if (row is null)
            return;

        row.LastSequence = subscription.LastSequence;
        row.Attempts = subscription.Attempts;
        row.ParkedAt = subscription.Parked
            ? StoreMappings.ToUnixMilliseconds(_clock.GetUtcNow())
            : null;
        row.LastError = subscription.LastError;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordFailureAsync(
        GeoToolsDbContext context,
        Subscription subscription,
        MessageRow row,
        Exception exception,
        CancellationToken cancellationToken
    )
    {
        subscription.Attempts++;
        subscription.LastError = exception.Message;

        if (subscription.Attempts >= MaxAttempts)
        {
            subscription.Parked = true;
            _logger.LogError(
                exception,
                "Parking subscriber {Subscriber} after {Attempts} failures on {Topic} {Sequence}.",
                subscription.Id,
                subscription.Attempts,
                row.Topic,
                row.Sequence
            );
        }
        else
        {
            _logger.LogWarning(
                exception,
                "Subscriber {Subscriber} failed on {Topic} {Sequence}.",
                subscription.Id,
                row.Topic,
                row.Sequence
            );
        }

        await SaveCursorAsync(context, subscription, cancellationToken).ConfigureAwait(false);

        if (!subscription.Parked)
            return;

        try
        {
            await PublishAsync(
                    new SubscriptionFaulted(
                        subscription.Id,
                        row.Topic,
                        row.Sequence,
                        exception.Message,
                        _clock.GetUtcNow()
                    ),
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (Exception publishException)
        {
            _logger.LogWarning(publishException, "Could not publish a subscription fault.");
        }
    }

    private static async Task<long> GetMaxSequenceAsync(
        GeoToolsDbContext context,
        CancellationToken cancellationToken
    ) => await context.Messages.MaxAsync(row => (long?)row.Sequence, cancellationToken) ?? 0;

    private sealed record PendingMessage(
        string Topic,
        byte[] Body,
        DateTimeOffset PublishedAt,
        string Publisher,
        string? CorrelationId,
        TaskCompletionSource<long> Completion
    );

    private sealed class Subscription(
        string id,
        IReadOnlyList<string> topics,
        long lastSequence,
        int attempts,
        bool parked,
        Func<MessageEnvelope, CancellationToken, Task> handler
    )
    {
        public string Id { get; } = id;
        public IReadOnlyList<string> Topics { get; } = topics;
        public long LastSequence { get; set; } = lastSequence;
        public int Attempts { get; set; } = attempts;
        public bool Parked { get; set; } = parked;
        public string? LastError { get; set; }
        public Func<MessageEnvelope, CancellationToken, Task> Handler { get; } = handler;

        public void Unpark()
        {
            Parked = false;
            Attempts = 0;
            LastError = null;
        }
    }
}
