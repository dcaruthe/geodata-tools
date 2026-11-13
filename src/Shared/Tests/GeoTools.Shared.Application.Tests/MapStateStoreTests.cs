using FluentAssertions;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Domain.Geospatial;

namespace GeoTools.Shared.Application.Tests;

public sealed class MapStateStoreTests
{
    [Fact]
    public void Current_starts_empty_and_uninitialised()
    {
        var store = new MapStateStore();

        store.Current.Should().Be(MapViewState.Empty);
        store.Current.IsInitialised.Should().BeFalse();
        store.Version.Should().Be(0);
    }

    [Fact]
    public void Set_publishes_the_state_and_increments_the_version()
    {
        var store = new MapStateStore();
        var state = State();

        store.Set(state);

        store.Current.Should().BeSameAs(state);
        store.Version.Should().Be(1);
    }

    [Fact]
    public void Set_keeps_one_of_the_concurrent_states_and_counts_every_write()
    {
        var store = new MapStateStore();
        var states = Enumerable.Range(0, 100).Select(index => State(index * 0.01)).ToArray();

        Parallel.ForEach(states, store.Set);

        store.Version.Should().Be(100);
        states.Should().Contain(store.Current);
    }

    [Fact]
    public void Set_raises_changed_for_each_write()
    {
        var store = new MapStateStore();
        var count = 0;
        store.Changed += (_, _) => count++;

        store.Set(State());
        store.Set(State());

        count.Should().Be(2);
    }

    private static MapViewState State(double centerX = 0.5) =>
        new(
            new MapCamera(centerX, 0.5, 100),
            new GeoPoint(0, 0),
            new WorldRect(0, 0, 1, 1),
            string.Empty,
            [],
            []
        );
}
