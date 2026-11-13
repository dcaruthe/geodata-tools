using System.Collections.Concurrent;
using GeoTools.Shared.Application.Diagnostics;
using GeoTools.Shared.Application.Link;
using Microsoft.Extensions.Logging;

namespace GeoTools.Shared.Infrastructure.Logging;

// A bounded tail of the launcher's own log. An agent reads it through the link because the
// launcher's stdout belongs to whoever started it.
public sealed class InMemoryLogStore : ILoggerProvider, ILogStore, IDisposable
{
    private const int Capacity = 2000;
    private const int MaxPage = 500;

    private readonly ConcurrentQueue<LogEntryDto> _entries = new();
    private long _sequence;

    public ILogger CreateLogger(string categoryName) => new StoreLogger(this, categoryName);

    public void Dispose() { }

    public void Write(LogLevel level, string category, string message, Exception? exception)
    {
        _entries.Enqueue(
            new LogEntryDto(
                Interlocked.Increment(ref _sequence),
                DateTimeOffset.UtcNow,
                level.ToString(),
                category,
                message,
                exception?.ToString()
            )
        );

        while (_entries.Count > Capacity && _entries.TryDequeue(out _)) { }
    }

    public LogPageDto Read(string? level, int limit, long? after)
    {
        var take = Math.Clamp(limit, 1, MaxPage);
        var minimum = Parse(level);
        var filtered = _entries
            .Where(entry => after is null || entry.Sequence > after)
            .Where(entry => minimum is null || Parse(entry.Level) >= minimum)
            .OrderBy(entry => entry.Sequence);

        // A poll asks for what is new; a tail asks for what is recent.
        var entries = after is null ? filtered.TakeLast(take) : filtered.Take(take);

        return new LogPageDto(entries.ToArray(), Interlocked.Read(ref _sequence));
    }

    private static LogLevel? Parse(string? level) =>
        Enum.TryParse<LogLevel>(level, ignoreCase: true, out var parsed) ? parsed : null;

    private sealed class StoreLogger(InMemoryLogStore store, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (!IsEnabled(logLevel))
                return;

            store.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
