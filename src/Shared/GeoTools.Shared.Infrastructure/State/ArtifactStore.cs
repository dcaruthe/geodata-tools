using GeoTools.Shared.Application.State;
using GeoTools.Shared.Domain.Geospatial;
using GeoTools.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoTools.Shared.Infrastructure.State;

internal sealed class ArtifactStore(IDbContextFactory<GeoToolsDbContext> factory) : IArtifactStore
{
    public async Task<ArtifactRecord?> GetAsync(
        ArtifactKey key,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var row = await context
            .Artifacts.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Key == key.Value, cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<ArtifactRecord>> ListAsync(
        ArtifactKind? kind = null,
        CancellationToken cancellationToken = default
    )
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var query = context.Artifacts.AsNoTracking();
        if (kind is not null)
            query = query.Where(row => row.Kind == kind.Value.ToString());

        var rows = await query.OrderBy(row => row.Key).ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToArray();
    }

    public async Task PutAsync(ArtifactRecord record, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var row = await context.Artifacts.FindAsync([record.Key.Value], cancellationToken);
        if (row is null)
        {
            row = new ArtifactRow { Key = record.Key.Value };
            context.Artifacts.Add(row);
        }

        Apply(record, row);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(ArtifactKey key, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await context
            .Artifacts.Where(row => row.Key == key.Value)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static ArtifactRecord ToRecord(ArtifactRow row) =>
        new(
            new ArtifactKey(row.Key),
            StoreMappings.ParseEnum<ArtifactKind>(row.Kind),
            row.Region,
            row.Path,
            row.SizeBytes,
            StoreMappings.FromUnixMilliseconds(row.ModifiedAt),
            row.Fingerprint,
            StoreMappings.ParseLevels(row.AdminLevels),
            StoreMappings.ParseBounds(row.Bounds),
            StoreMappings.ParseEnum<ArtifactStatus>(row.Status),
            StoreMappings.FromUnixMilliseconds(row.UpdatedAt)
        );

    private static void Apply(ArtifactRecord record, ArtifactRow row)
    {
        row.Kind = StoreMappings.FormatEnum(record.Kind);
        row.Region = record.Region;
        row.Path = record.Path;
        row.SizeBytes = record.SizeBytes;
        row.ModifiedAt = StoreMappings.ToUnixMilliseconds(record.ModifiedAt);
        row.Fingerprint = record.Fingerprint;
        row.AdminLevels = StoreMappings.FormatLevels(record.AdminLevels);
        row.Bounds = record.Bounds is { } bounds ? StoreMappings.FormatBounds(bounds) : null;
        row.Status = StoreMappings.FormatEnum(record.Status);
        row.UpdatedAt = StoreMappings.ToUnixMilliseconds(record.UpdatedAt);
    }
}
