using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace GeoTools.Shared.Infrastructure.Processes;

internal sealed record ProcessSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null
);

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        ProcessSpec spec,
        Action<string>? onStandardError = null,
        CancellationToken cancellationToken = default
    );
}

internal sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    private const int KeptLines = 200;

    public async Task<ProcessResult> RunAsync(
        ProcessSpec spec,
        Action<string>? onStandardError = null,
        CancellationToken cancellationToken = default
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = spec.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = spec.WorkingDirectory ?? Environment.CurrentDirectory,
        };

        foreach (var argument in spec.Arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };

        if (!process.Start())
            throw new InvalidOperationException($"Could not start {spec.FileName}.");

        var output = new LineBuffer(KeptLines);
        var error = new LineBuffer(KeptLines);

        var outputTask = ReadAsync(process.StandardOutput, output, null, cancellationToken);
        var errorTask = ReadAsync(process.StandardError, error, onStandardError, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            await Task.WhenAll(outputTask, errorTask);
            logger.LogInformation("Stopped {File}.", spec.FileName);
            throw;
        }

        await Task.WhenAll(outputTask, errorTask);
        return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
    }

    private static async Task ReadAsync(
        StreamReader reader,
        LineBuffer buffer,
        Action<string>? onLine,
        CancellationToken cancellationToken
    )
    {
        try
        {
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                buffer.Add(line);
                onLine?.Invoke(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The process already exited or cannot be killed; nothing else to do.
        }
    }

    private sealed class LineBuffer(int capacity)
    {
        private readonly Queue<string> _lines = new(capacity);

        public void Add(string line)
        {
            if (_lines.Count == capacity)
                _lines.Dequeue();

            _lines.Enqueue(line);
        }

        public override string ToString() => string.Join('\n', _lines);
    }
}
