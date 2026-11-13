using System.Threading.Channels;
using GeoTools.Shared.Application.Link;

namespace GeoTools.Shared.Presentation.Link;

internal sealed record WindowCommand(
    LinkRequest Request,
    TaskCompletionSource<LinkResponse> Completion
);

// The link runs on the socket's thread. The window runs on the GLFW main thread. Every command
// that touches the window crosses this queue, so the window keeps one thread for its state.
internal sealed class WindowCommandInbox
{
    private readonly Channel<WindowCommand> _queue = Channel.CreateUnbounded<WindowCommand>(
        new UnboundedChannelOptions { SingleReader = true }
    );
    private int _closed;

    public async Task<LinkResponse> EnqueueAsync(
        LinkRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        if (Volatile.Read(ref _closed) == 1)
            return Closing(request.Id);

        var completion = new TaskCompletionSource<LinkResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        if (!_queue.Writer.TryWrite(new WindowCommand(request, completion)))
            return Closing(request.Id);

        try
        {
            return await completion.Task.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return LinkResponse.Failure(
                request.Id,
                new LinkError(
                    LinkError.Timeout,
                    $"The window did not answer within {timeout.TotalSeconds:0.#} seconds."
                )
            );
        }
        catch (OperationCanceledException)
        {
            return LinkResponse.Failure(
                request.Id,
                new LinkError(LinkError.Timeout, "The request was cancelled.")
            );
        }
    }

    public bool TryDequeue(out WindowCommand? command) => _queue.Reader.TryRead(out command);

    public void Close()
    {
        Interlocked.Exchange(ref _closed, 1);
        _queue.Writer.TryComplete();

        while (_queue.Reader.TryRead(out var command))
            command.Completion.TrySetResult(Closing(command.Request.Id));
    }

    private static LinkResponse Closing(string id) =>
        LinkResponse.Failure(
            id,
            new LinkError(LinkError.LauncherClosing, "The window is closing.")
        );
}
