using GeoTools.Shared.Application.State;

namespace GeoTools.Shared.Application.Geodata;

public sealed record BootstrapOutcome(RunId RunId, RunStatus Status, string? Error);
