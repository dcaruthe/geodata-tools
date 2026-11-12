using GeoTools.Shared.Application.State;
using GeoTools.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GeoTools.Shared.Infrastructure.State;

internal sealed class StateInitializer(
    IDbContextFactory<GeoToolsDbContext> factory,
    IJobStore jobs,
    ISettingsStore settings,
    ILogger<StateInitializer> logger
) : IStateInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await SqliteSchema.InitializeAsync(context, cancellationToken);
        await jobs.ReconcileAsync(cancellationToken);
        await settings.ReloadAsync(cancellationToken);
        logger.LogInformation(
            "State database ready at schema {Version}.",
            SqliteSchema.CurrentVersion
        );
    }
}
