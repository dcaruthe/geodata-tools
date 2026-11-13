using FluentAssertions;
using GeoTools.Shared.Application.Link;
using GeoTools.Shared.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class InMemoryLogStoreTests
{
    [Fact]
    public void A_tail_returns_the_newest_entries_in_order()
    {
        var store = new InMemoryLogStore();
        var logger = store.CreateLogger("test");

        for (var index = 0; index < 10; index++)
            logger.LogInformation("entry {Index}", index);

        var page = store.Read(null, 3, null);

        page.Entries.Select(entry => entry.Message).Should().Equal("entry 7", "entry 8", "entry 9");
        page.NextSequence.Should().Be(10);
    }

    [Fact]
    public void A_poll_returns_only_entries_after_the_cursor()
    {
        var store = new InMemoryLogStore();
        var logger = store.CreateLogger("test");
        logger.LogInformation("one");
        logger.LogInformation("two");
        logger.LogInformation("three");

        var page = store.Read(null, 10, after: 1);

        page.Entries.Select(entry => entry.Message).Should().Equal("two", "three");
    }

    [Fact]
    public void A_level_filter_keeps_warnings_and_errors_only()
    {
        var store = new InMemoryLogStore();
        var logger = store.CreateLogger("test");
        logger.LogInformation("quiet");
        logger.LogWarning("careful");
        logger.LogError("broken");

        var page = store.Read("Warning", 10, null);

        page.Entries.Select(entry => entry.Level).Should().Equal("Warning", "Error");
    }

    [Fact]
    public void An_exception_is_kept_with_the_entry()
    {
        var store = new InMemoryLogStore();
        var logger = store.CreateLogger("test");
        logger.LogError(new InvalidOperationException("boom"), "failed");

        var entry = store.Read(null, 10, null).Entries.Single();

        entry.Exception.Should().Contain("boom");
        entry.Category.Should().Be("test");
    }

    [Fact]
    public void The_store_is_bounded()
    {
        var store = new InMemoryLogStore();
        var logger = store.CreateLogger("test");

        for (var index = 0; index < 2100; index++)
            logger.LogInformation("entry {Index}", index);

        var page = store.Read(null, 500, after: 0);

        page.Entries.Should().HaveCount(500);
        page.Entries[0].Sequence.Should().Be(101);
        page.NextSequence.Should().Be(2100);
    }
}
