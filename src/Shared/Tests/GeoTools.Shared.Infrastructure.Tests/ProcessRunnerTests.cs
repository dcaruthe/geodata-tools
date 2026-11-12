using System.ComponentModel;
using FluentAssertions;
using GeoTools.Shared.Infrastructure.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoTools.Shared.Infrastructure.Tests;

public sealed class ProcessRunnerTests
{
    private static ProcessRunner Create() => new(NullLogger<ProcessRunner>.Instance);

    [Fact]
    public async Task Run_returns_the_exit_code_and_captures_both_streams()
    {
        var errors = new List<string>();

        var result = await Create()
            .RunAsync(
                new ProcessSpec("/bin/sh", ["-c", "echo out; echo err >&2; exit 3"]),
                errors.Add
            );

        result.ExitCode.Should().Be(3);
        result.StandardOutput.Should().Contain("out");
        result.StandardError.Should().Contain("err");
        errors.Should().Contain("err");
    }

    [Fact]
    public async Task Run_throws_when_the_executable_is_missing()
    {
        var act = async () => await Create().RunAsync(new ProcessSpec("no-such-executable", []));

        await act.Should().ThrowAsync<Win32Exception>();
    }

    [Fact]
    public async Task Run_keeps_only_the_tail_of_a_long_output()
    {
        var result = await Create().RunAsync(new ProcessSpec("/bin/sh", ["-c", "seq 1 300"]));

        var lines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(200);
        lines[0].Should().Be("101");
        lines[^1].Should().Be("300");
    }

    [Fact]
    public async Task Run_kills_the_child_on_cancellation()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var act = async () =>
            await Create()
                .RunAsync(new ProcessSpec("/bin/sh", ["-c", "sleep 5"]), null, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
