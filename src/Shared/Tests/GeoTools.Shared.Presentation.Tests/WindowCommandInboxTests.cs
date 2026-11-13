using FluentAssertions;
using GeoTools.Shared.Application.Link;
using GeoTools.Shared.Presentation.Link;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class WindowCommandInboxTests
{
    [Fact]
    public async Task Closing_the_window_completes_queued_commands_with_closing()
    {
        var inbox = new WindowCommandInbox();
        var pending = inbox.EnqueueAsync(
            new GetStateRequest("1"),
            TimeSpan.FromSeconds(5),
            CancellationToken.None
        );

        inbox.Close();
        var response = await pending;

        response.Ok.Should().BeFalse();
        response.Error!.Code.Should().Be(LinkError.LauncherClosing);
    }

    [Fact]
    public async Task An_enqueue_after_close_fails_immediately()
    {
        var inbox = new WindowCommandInbox();
        inbox.Close();

        var response = await inbox.EnqueueAsync(
            new GetStateRequest("1"),
            TimeSpan.FromSeconds(5),
            CancellationToken.None
        );

        response.Error!.Code.Should().Be(LinkError.LauncherClosing);
    }

    [Fact]
    public async Task A_command_that_is_never_drained_times_out()
    {
        var inbox = new WindowCommandInbox();

        var response = await inbox.EnqueueAsync(
            new GetStateRequest("1"),
            TimeSpan.FromMilliseconds(20),
            CancellationToken.None
        );

        response.Error!.Code.Should().Be(LinkError.Timeout);
    }

    [Fact]
    public async Task A_cancelled_wait_reports_a_timeout()
    {
        var inbox = new WindowCommandInbox();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var response = await inbox.EnqueueAsync(
            new GetStateRequest("1"),
            TimeSpan.FromSeconds(5),
            cancelled.Token
        );

        response.Error!.Code.Should().Be(LinkError.Timeout);
    }
}
