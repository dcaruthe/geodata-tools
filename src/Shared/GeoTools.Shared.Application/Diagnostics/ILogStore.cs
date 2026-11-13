using GeoTools.Shared.Application.Link;

namespace GeoTools.Shared.Application.Diagnostics;

public interface ILogStore
{
    LogPageDto Read(string? level, int limit, long? after);
}
