using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using GeoTools.Shared.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure.Geodata;

internal sealed record DownloadRequest(
    string Region,
    Uri Url,
    string DestinationPath,
    bool Force,
    string? Validator
);

internal sealed record DownloadProgress(
    long BytesReceived,
    long TotalBytes,
    double BytesPerSecond,
    double? EtaSeconds
);

internal sealed record DownloadResult(
    bool Changed,
    long SizeBytes,
    DateTimeOffset ModifiedAt,
    string Validator
);

internal interface IPbfDownloader
{
    Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken = default
    );
}

internal sealed class HttpPbfDownloader(
    HttpClient client,
    IOptions<GeodataOptions> options,
    TimeProvider clock,
    ILogger<HttpPbfDownloader> logger
) : IPbfDownloader
{
    private const int BufferSize = 81920;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    public async Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken = default
    )
    {
        var geodata = options.Value;
        var attempts = Math.Max(1, geodata.RetryCount + 1);
        Exception? last = null;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                return await DownloadOnceAsync(request, progress, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
                when (exception
                        is HttpRequestException
                            or IOException
                            or TimeoutException
                            or OperationCanceledException
                )
            {
                last = exception;
                if (attempt == attempts - 1)
                    break;

                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                logger.LogWarning(
                    "Download of {Region} failed ({Error}). Retrying in {Delay}.",
                    request.Region,
                    exception.Message,
                    delay
                );
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new IOException(
            $"Could not download {request.Region}: {last?.Message ?? "unknown error"}",
            last
        );
    }

    private static async Task<Stream> ReadStreamAsync(
        HttpResponseMessage response,
        string region,
        GeodataOptions options,
        CancellationToken inactivityToken,
        CancellationToken requestToken
    )
    {
        try
        {
            return await response.Content.ReadAsStreamAsync(inactivityToken);
        }
        catch (OperationCanceledException) when (!requestToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The download of {region} stalled for {options.InactivityTimeoutSeconds} seconds."
            );
        }
    }

    private async Task<DownloadResult> DownloadOnceAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        var geodata = options.Value;
        var partPath = request.DestinationPath + ".part";
        var destinationDirectory = Path.GetDirectoryName(request.DestinationPath);
        if (!string.IsNullOrEmpty(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);

        var resumeAt = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
        var destinationExists = File.Exists(request.DestinationPath);
        var existing = destinationExists ? new FileInfo(request.DestinationPath) : null;

        using var message = new HttpRequestMessage(HttpMethod.Get, request.Url);
        message.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));

        if (!request.Force && destinationExists && resumeAt == 0 && existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(request.Validator))
                message.Headers.TryAddWithoutValidation("If-None-Match", request.Validator);

            message.Headers.IfModifiedSince = new DateTimeOffset(
                existing.LastWriteTimeUtc,
                TimeSpan.Zero
            );
        }

        if (resumeAt > 0)
            message.Headers.Range = new RangeHeaderValue(resumeAt, null);

        using var inactivity = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        inactivity.CancelAfter(TimeSpan.FromSeconds(geodata.InactivityTimeoutSeconds));

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                inactivity.Token
            );
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The download of {request.Region} stalled for {geodata.InactivityTimeoutSeconds} seconds."
            );
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotModified && existing is not null)
            {
                logger.LogInformation("{Region} is unchanged.", request.Region);
                return new DownloadResult(
                    false,
                    existing.Length,
                    new DateTimeOffset(existing.LastWriteTimeUtc, TimeSpan.Zero),
                    request.Validator ?? string.Empty
                );
            }

            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                throw new IOException(
                    $"The server rejected the resume range for {request.Region}."
                );

            response.EnsureSuccessStatusCode();

            if (response.StatusCode == HttpStatusCode.OK && resumeAt > 0)
                resumeAt = 0;

            var remaining = response.Content.Headers.ContentLength ?? -1;
            var total = remaining >= 0 ? resumeAt + remaining : -1;
            if (remaining > 0)
                DiskSpaceGuard.EnsureAvailable(
                    request.DestinationPath,
                    remaining + 256L * 1024 * 1024
                );

            await using var stream = await ReadStreamAsync(
                response,
                request.Region,
                geodata,
                inactivity.Token,
                cancellationToken
            );
            await using var file = new FileStream(
                partPath,
                resumeAt > 0 ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan
            );

            var buffer = new byte[BufferSize];
            var received = resumeAt;
            var stopwatch = Stopwatch.StartNew();
            var lastReport = TimeSpan.Zero;
            var lastBytes = received;
            var lastTick = TimeSpan.Zero;

            while (true)
            {
                int read;
                try
                {
                    read = await stream.ReadAsync(buffer, inactivity.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"The download of {request.Region} stalled for {geodata.InactivityTimeoutSeconds} seconds."
                    );
                }

                if (read == 0)
                    break;

                await file.WriteAsync(buffer.AsMemory(0, read), inactivity.Token);
                received += read;
                inactivity.CancelAfter(TimeSpan.FromSeconds(geodata.InactivityTimeoutSeconds));

                if (stopwatch.Elapsed - lastReport < ProgressInterval)
                    continue;

                var elapsed = stopwatch.Elapsed - lastTick;
                var speed =
                    elapsed.TotalSeconds > 0 ? (received - lastBytes) / elapsed.TotalSeconds : 0;
                var eta =
                    speed > 0 && total > 0
                        ? Math.Max(0, (total - received) / speed)
                        : (double?)null;

                progress?.Report(new DownloadProgress(received, total, speed, eta));
                lastReport = stopwatch.Elapsed;
                lastBytes = received;
                lastTick = stopwatch.Elapsed;
            }

            await file.FlushAsync(inactivity.Token);
            file.Flush(flushToDisk: true);

            var modified = response.Content.Headers.LastModified
                ?? new DateTimeOffset(clock.GetUtcNow().UtcDateTime, TimeSpan.Zero);
            File.Move(partPath, request.DestinationPath, overwrite: true);
            File.SetLastWriteTimeUtc(request.DestinationPath, modified.UtcDateTime);

            progress?.Report(
                new DownloadProgress(
                    received,
                    total > 0 ? total : received,
                    received > 0 ? received / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds) : 0,
                    0
                )
            );

            logger.LogInformation(
                "Downloaded {Region}: {Size} bytes in {Elapsed}.",
                request.Region,
                received,
                stopwatch.Elapsed
            );

            return new DownloadResult(
                true,
                received,
                modified,
                response.Headers.ETag?.Tag ?? string.Empty
            );
        }
    }
}

internal static class DiskSpaceGuard
{
    public static void EnsureAvailable(string path, long requiredBytes)
    {
        if (requiredBytes <= 0)
            return;

        var root = Path.GetPathRoot(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(root))
            return;

        var drive = new DriveInfo(root);
        if (drive.AvailableFreeSpace >= requiredBytes)
            return;

        throw new IOException(
            $"Not enough free disk space in {root}: {requiredBytes} bytes needed, {drive.AvailableFreeSpace} available."
        );
    }
}
