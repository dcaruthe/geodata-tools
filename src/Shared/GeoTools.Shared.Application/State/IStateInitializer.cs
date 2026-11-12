namespace GeoTools.Shared.Application.State;

public interface IStateInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
