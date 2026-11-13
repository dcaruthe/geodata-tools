using GeoTools.Shared.Application.Link;
using GeoTools.Shared.Application.State;
using Microsoft.Extensions.Options;

namespace GeoTools.Api;

// The API only reads the state database. This loads the runtime settings once and leaves the
// database writable by the launcher alone.
public sealed class ApiStartupService(ISettingsStore settings, ILogger<ApiStartupService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await settings.ReloadAsync(cancellationToken);
            logger.LogInformation("Loaded runtime settings from the state database.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "The state database is not readable yet. Start the launcher once, then retry."
            );
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

// The launcher link owns the rotation schedule. The API only ticks it, so the interval lives
// in one place and a verify run can shorten it.
public sealed class LinkRotationService(
    ILauncherLink link,
    IOptions<GeoTools.Shared.Application.Link.LinkOptions> options,
    ILogger<LinkRotationService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tick = TimeSpan.FromSeconds(
            Math.Clamp(
                options.Value.RotateSeconds is > 0 ? options.Value.RotateSeconds / 5.0 : 1,
                1,
                60
            )
        );
        using var timer = new PeriodicTimer(tick);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await link.RotateIfDueAsync(stoppingToken);
            }
            catch (LinkUnavailableException)
            {
                // The next request reconnects. Rotation is best effort.
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "The link rotation tick failed.");
            }
        }
    }
}
