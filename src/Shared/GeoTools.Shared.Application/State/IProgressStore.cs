namespace GeoTools.Shared.Application.State;

public interface IProgressStore
{
    BootstrapProgressSnapshot Current { get; }

    void Set(BootstrapProgressSnapshot snapshot);

    event EventHandler? Changed;
}
