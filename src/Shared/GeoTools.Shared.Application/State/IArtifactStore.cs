namespace GeoTools.Shared.Application.State;

public interface IArtifactStore
{
    Task<ArtifactRecord?> GetAsync(ArtifactKey key, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArtifactRecord>> ListAsync(
        ArtifactKind? kind = null,
        CancellationToken cancellationToken = default
    );

    Task PutAsync(ArtifactRecord record, CancellationToken cancellationToken = default);

    Task DeleteAsync(ArtifactKey key, CancellationToken cancellationToken = default);
}
