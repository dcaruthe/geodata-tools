using System.Diagnostics;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Domain.Geospatial;
using Microsoft.Extensions.Logging;

namespace GeoTools.Shared.Presentation.Views;

// Focuses the administrative region under the crosshair, fits it, and reveals the next extracted
// admin level inside it. The camera moves as soon as the hit test returns; the region and its
// children load off the render thread and publish immutable snapshots into the shared stores.
// A focus stack remembers the pushed regions so Backspace can pop back to the parent or the world.
internal sealed class MapFocusService
{
    private const float FitPadding = 24f;

    private readonly IRegionIndex _index;
    private readonly IRegionIndexWriter _writer;
    private readonly IBorderGeometryStore _geometry;
    private readonly IMapStateStore _mapState;
    private readonly IFocusOverlayStore _overlay;
    private readonly ISettingsStore _settings;
    private readonly ILogger<MapFocusService> _logger;

    private readonly object _gate = new();
    private readonly List<FocusEntry> _stack = [];
    private readonly Dictionary<string, RegionDetail> _details = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BorderGeometry> _composed = new(StringComparer.Ordinal);

    private BorderGeometry _root = BorderGeometry.Empty;
    private IReadOnlyList<int> _rootAvailable = [];
    private long _rootVersion = -1;
    private long _writtenVersion = -1;
    private int _generation;
    private CancellationTokenSource? _pending;

    public MapFocusService(
        IRegionIndex index,
        IRegionIndexWriter writer,
        IBorderGeometryStore geometry,
        IMapStateStore mapState,
        IFocusOverlayStore overlay,
        ISettingsStore settings,
        ILogger<MapFocusService> logger
    )
    {
        _index = index;
        _writer = writer;
        _geometry = geometry;
        _mapState = mapState;
        _overlay = overlay;
        _settings = settings;
        _logger = logger;
    }

    public void FocusAt(GeoPoint point, ViewSize size)
    {
        lock (_gate)
        {
            _generation++;
            var generation = _generation;
            var token = ResetPendingLocked();
            var candidateLevels = CandidateLevelsLocked();
            if (SynchronizeRootLocked())
                ResetToRootLocked(size);

            _ = RunFocusAtAsync(point, size, candidateLevels, generation, token);
        }
    }

    public void FocusRegion(string regionId, ViewSize size)
    {
        lock (_gate)
        {
            _generation++;
            var generation = _generation;
            var token = ResetPendingLocked();
            if (SynchronizeRootLocked())
                ResetToRootLocked(size);

            _ = RunFocusRegionAsync(regionId, size, generation, token);
        }
    }

    public bool Blur(ViewSize size)
    {
        lock (_gate)
        {
            _generation++;
            ResetPendingLocked();
            if (SynchronizeRootLocked())
            {
                ResetToRootLocked(size);
                return false;
            }

            return PopLocked(size);
        }
    }

    private async Task RunFocusAtAsync(
        GeoPoint point,
        ViewSize size,
        IReadOnlyList<int> candidateLevels,
        int generation,
        CancellationToken token
    )
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var matches = await _index.FindContainingAsync(point, candidateLevels, token);
            if (matches.Count == 0)
            {
                lock (_gate)
                {
                    if (!IsCurrentLocked(generation))
                        return;

                    PublishNoMatchLocked(size, candidateLevels);
                }

                return;
            }

            await PushAsync(matches[0], size, generation, token, stopwatch);
        }
        catch (OperationCanceledException)
        {
            // A newer focus or blur superseded this run.
        }
        catch (Exception exception)
        {
            FailLocked(size, exception, generation);
        }
    }

    private async Task RunFocusRegionAsync(
        string regionId,
        ViewSize size,
        int generation,
        CancellationToken token
    )
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var detail = await _index.GetRegionAsync(regionId, token);
            if (detail is null)
            {
                lock (_gate)
                {
                    if (!IsCurrentLocked(generation))
                        return;

                    PublishStatusLocked(size, $"No region with id {regionId}.");
                }

                return;
            }

            await PushAsync(detail.Summary, size, generation, token, stopwatch);
        }
        catch (OperationCanceledException)
        {
            // Superseded.
        }
        catch (Exception exception)
        {
            FailLocked(size, exception, generation);
        }
    }

    private async Task PushAsync(
        RegionSummary summary,
        ViewSize size,
        int generation,
        CancellationToken token,
        Stopwatch stopwatch
    )
    {
        var region = new FocusedRegion(
            summary.Id,
            summary.Name,
            summary.AdminLevel,
            summary.Bounds
        );
        var next = NextExtractedLevel(summary.AdminLevel);

        // Decide already-top or begin loading, and move the camera immediately before the
        // detail and its children arrive.
        lock (_gate)
        {
            if (!IsCurrentLocked(generation))
                return;

            if (_stack.Count > 0 && _stack[^1].Region.Id == summary.Id)
            {
                PublishStateLocked(
                    size,
                    MapCamera.Fit(region.Bounds, size.Width, size.Height, FitPadding),
                    _stack.Select(entry => entry.Region).ToArray(),
                    VisibleLevelsLocked(_stack),
                    $"Already focused on {Label(region)} (level {region.AdminLevel})."
                );
                return;
            }

            var focus = _stack.Select(entry => entry.Region).ToList();
            focus.Add(region);
            var loadingLevels = ComputeVisibleLevels(
                _stack.Select(entry => (entry.Region, entry.NextLevel)).Append((region, next))
            );
            PublishStateLocked(
                size,
                MapCamera.Fit(region.Bounds, size.Width, size.Height, FitPadding),
                focus,
                loadingLevels,
                LoadingStatus(region, next)
            );
        }

        var detail = await LoadDetailAsync(region, token);
        if (detail is null)
        {
            FailLocked(
                size,
                new InvalidOperationException($"The region {region.Id} has no readable geometry."),
                generation
            );
            return;
        }

        IReadOnlyList<RegionDetail> children = [];
        if (next > 0)
        {
            var summaries = await _writer.ResolveChildrenAsync(region.Id, next, token);
            if (summaries.Count > 0)
                children = await _index.GetRegionsAsync(
                    summaries.Select(child => child.Id).ToArray(),
                    token
                );
        }

        lock (_gate)
        {
            if (!IsCurrentLocked(generation))
                return;

            var entry = new FocusEntry(region, next, detail, children);
            _stack.Add(entry);
            _details[region.Id] = detail;
            foreach (var child in children)
                _details[child.Summary.Id] = child;

            var visible = VisibleLevelsLocked(_stack);
            _geometry.Set(ComposeLocked(), visible);
            _writtenVersion = _geometry.Version;
            _overlay.Set(GeometryProjector.Project(detail.Polylines), region.Name, region.Bounds);

            var status =
                next > 0
                    ? $"Focused {Label(region)} (level {region.AdminLevel}): revealing level {next}, {children.Count} regions."
                    : $"Focused {Label(region)} (level {region.AdminLevel}): deepest extracted level.";
            PublishStateLocked(
                size,
                MapCamera.Fit(region.Bounds, size.Width, size.Height, FitPadding),
                _stack.Select(entry => entry.Region).ToArray(),
                visible,
                status
            );

            _logger.LogInformation(
                "Focused region {Id} {Name} at level {Level} revealing level {Next} with {ChildCount} children in {ElapsedMilliseconds} ms",
                region.Id,
                region.Name,
                region.AdminLevel,
                next,
                children.Count,
                stopwatch.ElapsedMilliseconds
            );
        }
    }

    private async Task<RegionDetail?> LoadDetailAsync(FocusedRegion region, CancellationToken token)
    {
        lock (_gate)
        {
            if (_details.TryGetValue(region.Id, out var cached))
                return cached;
        }

        return await _index.GetRegionAsync(region.Id, token);
    }

    private bool PopLocked(ViewSize size)
    {
        if (_stack.Count == 0)
            return false;

        _stack.RemoveAt(_stack.Count - 1);

        if (_stack.Count == 0)
        {
            _logger.LogInformation("Popped focus to the world view.");
            ResetToRootLocked(size);
            return true;
        }

        var parent = _stack[^1];
        var visible = VisibleLevelsLocked(_stack);
        _geometry.Set(ComposeLocked(), visible);
        _writtenVersion = _geometry.Version;
        _overlay.Set(
            GeometryProjector.Project(parent.Detail.Polylines),
            parent.Region.Name,
            parent.Region.Bounds
        );
        PublishStateLocked(
            size,
            MapCamera.Fit(parent.Region.Bounds, size.Width, size.Height, FitPadding),
            _stack.Select(entry => entry.Region).ToArray(),
            visible,
            $"Focused {Label(parent.Region)} (level {parent.Region.AdminLevel})."
        );

        _logger.LogInformation(
            "Popped focus to {Id} {Name} at level {Level}",
            parent.Region.Id,
            parent.Region.Name,
            parent.Region.AdminLevel
        );
        return true;
    }

    // Restores the root geometry and the world fit, keeping the detail and composed caches so a
    // re-push is instant.
    private void ResetToRootLocked(ViewSize size)
    {
        _stack.Clear();
        _overlay.Clear();
        _geometry.Set(_root, _rootAvailable);
        _writtenVersion = _geometry.Version;
        PublishStateLocked(
            size,
            MapCamera.Fit(_root.Bounds, size.Width, size.Height, FitPadding),
            [],
            [],
            string.Empty
        );
    }

    // Caches the current border geometry as the root and clears the focus state whenever the
    // geometry version changed outside this service. The version the service last wrote is its
    // own, so its composed geometry is never mistaken for a reload.
    private bool SynchronizeRootLocked()
    {
        var version = _geometry.Version;
        if (version == _rootVersion || version == _writtenVersion)
            return false;

        var hadFocus = _stack.Count > 0 || _overlay.Polylines.Count > 0;
        _root = _geometry.Geometry;
        _rootAvailable = _geometry.AvailableAdminLevels;
        _rootVersion = version;
        _stack.Clear();
        _details.Clear();
        _composed.Clear();
        _logger.LogInformation(
            "Cached root geometry at version {Version}; focus {Cleared}.",
            version,
            hadFocus ? "cleared" : "already empty"
        );
        return hadFocus;
    }

    private void PublishNoMatchLocked(ViewSize size, IReadOnlyList<int> candidateLevels)
    {
        var state = _mapState.Current;
        PublishStateLocked(
            size,
            state.Camera,
            state.Focus,
            state.VisibleLevels,
            $"No administrative region under the crosshair at levels {string.Join(",", candidateLevels)}."
        );
    }

    private void FailLocked(ViewSize size, Exception exception, int generation)
    {
        lock (_gate)
        {
            if (!IsCurrentLocked(generation))
                return;

            var message = $"Focus failed: {exception.Message}";
            _logger.LogError(exception, "{Message}", message);
            var state = _mapState.Current;
            PublishStateLocked(size, state.Camera, state.Focus, state.VisibleLevels, message);
        }
    }

    private void PublishStatusLocked(ViewSize size, string status)
    {
        var state = _mapState.Current;
        PublishStateLocked(size, state.Camera, state.Focus, state.VisibleLevels, status);
    }

    private void PublishStateLocked(
        ViewSize size,
        MapCamera camera,
        IReadOnlyList<FocusedRegion> focus,
        IReadOnlyList<int> visible,
        string status
    )
    {
        _mapState.Set(
            new MapViewState(
                camera,
                camera.CenterGeo,
                camera.Viewport(size.Width, size.Height).WorldBounds(size.Width, size.Height),
                status,
                focus,
                visible
            )
        );
    }

    private IReadOnlyList<int> CandidateLevelsLocked()
    {
        var state = _mapState.Current;
        return state.Focus.Count > 0 ? state.VisibleLevels : Normalize(_settings.Map.AdminLevels);
    }

    private IReadOnlyList<int> VisibleLevelsLocked(IReadOnlyList<FocusEntry> entries) =>
        ComputeVisibleLevels(entries.Select(entry => (entry.Region, entry.NextLevel)));

    // The union of the root display levels, the focused regions' own levels, and each revealed
    // child level that is not already a root display level. The skip rule avoids drawing the same
    // child level twice.
    private IReadOnlyList<int> ComputeVisibleLevels(
        IEnumerable<(FocusedRegion Region, int Next)> frames
    )
    {
        var root = Normalize(_settings.Map.AdminLevels);
        var levels = new HashSet<int>(root);
        foreach (var (region, next) in frames)
        {
            levels.Add(region.AdminLevel);
            if (next > 0 && !root.Contains(next))
                levels.Add(next);
        }

        return levels.Order().ToArray();
    }

    private BorderGeometry ComposeLocked()
    {
        var signature = string.Join(
            ">",
            _stack.Select(entry => $"{entry.Region.Id}:{entry.NextLevel}")
        );
        var cacheKey = $"{_rootVersion}:{signature}";
        if (_composed.TryGetValue(cacheKey, out var cached))
            return cached;

        var root = Normalize(_settings.Map.AdminLevels);
        var polylines = new List<GeoPolyline>(_root.Polylines);
        foreach (var entry in _stack)
        {
            polylines.AddRange(entry.Detail.Polylines);
            if (entry.NextLevel > 0 && !root.Contains(entry.NextLevel))
                polylines.AddRange(entry.Children.SelectMany(child => child.Polylines));
        }

        var composed = BorderGeometry.FromGeo(
            polylines,
            GeoBounds.FromPolylines(polylines, GeoBounds.World)
        );
        _composed[cacheKey] = composed;
        return composed;
    }

    private int NextExtractedLevel(int level)
    {
        var next = _settings.Geodata.ExtractAdminLevels.Where(candidate => candidate > level);
        return next.Any() ? next.Min() : 0;
    }

    private static string Label(FocusedRegion region) => region.Name ?? region.Id;

    private static string LoadingStatus(FocusedRegion region, int next) =>
        next > 0
            ? $"Focusing {Label(region)} (level {region.AdminLevel}); loading level {next}..."
            : $"Focusing {Label(region)} (level {region.AdminLevel})...";

    private static int[] Normalize(IReadOnlyList<int>? levels) =>
        levels is null ? [] : levels.Where(level => level > 0).Distinct().Order().ToArray();

    private bool IsCurrentLocked(int generation) => generation == _generation;

    private CancellationToken ResetPendingLocked()
    {
        _pending?.Cancel();
        _pending?.Dispose();
        var cts = new CancellationTokenSource();
        _pending = cts;
        return cts.Token;
    }

    private sealed record FocusEntry(
        FocusedRegion Region,
        int NextLevel,
        RegionDetail Detail,
        IReadOnlyList<RegionDetail> Children
    );
}
