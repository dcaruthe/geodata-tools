namespace GeoTools.Shared.Application.Geodata;

public interface IGeodataPipeline
{
    Task<BootstrapOutcome> RunAsync(
        BootstrapRequest request,
        CancellationToken cancellationToken = default
    );
}
