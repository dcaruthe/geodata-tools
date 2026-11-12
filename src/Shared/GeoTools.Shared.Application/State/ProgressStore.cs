namespace GeoTools.Shared.Application.State;

public sealed class ProgressStore : IProgressStore
{
    private BootstrapProgressSnapshot _current = BootstrapProgressSnapshot.Idle;

    public BootstrapProgressSnapshot Current => Volatile.Read(ref _current);

    public event EventHandler? Changed;

    public void Set(BootstrapProgressSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Volatile.Write(ref _current, snapshot);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
