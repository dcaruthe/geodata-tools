using FluentAssertions;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Presentation.Controls;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class WindowSizePresetsTests
{
    [Fact]
    public void Next_walks_the_presets_in_order()
    {
        WindowSizePresets.Next(WindowSize.Maximized, 1).Should().Be(new FixedWindow(1280, 800));
        WindowSizePresets
            .Next(new FixedWindow(1280, 800), 1)
            .Should()
            .Be(new FixedWindow(1440, 900));
        WindowSizePresets
            .Next(new FixedWindow(1440, 900), 1)
            .Should()
            .Be(new FixedWindow(1600, 900));
        WindowSizePresets
            .Next(new FixedWindow(1600, 900), 1)
            .Should()
            .Be(new FixedWindow(1920, 1080));
    }

    [Fact]
    public void Next_wraps_at_both_ends()
    {
        WindowSizePresets.Next(new FixedWindow(1920, 1080), 1).Should().Be(WindowSize.Maximized);
        WindowSizePresets.Next(WindowSize.Maximized, -1).Should().Be(new FixedWindow(1920, 1080));
    }

    [Fact]
    public void A_custom_size_stays_next_to_maximized()
    {
        var custom = new FixedWindow(1000, 700);

        WindowSizePresets.Next(custom, 1).Should().Be(new FixedWindow(1280, 800));
        WindowSizePresets.Next(custom, -1).Should().Be(WindowSize.Maximized);
        WindowSizePresets.All.Should().NotContain(custom);
    }

    [Fact]
    public void A_preset_size_is_not_duplicated()
    {
        WindowSizePresets
            .Next(new FixedWindow(1440, 900), 1)
            .Should()
            .Be(new FixedWindow(1600, 900));
        WindowSizePresets.All.Should().OnlyHaveUniqueItems();
    }
}

public sealed class WindowStateStoreTests
{
    [Fact]
    public void The_store_starts_empty_and_bumps_its_version_on_each_set()
    {
        var store = new WindowStateStore();

        store.Current.Should().Be(new WindowStateSnapshot(0, 0, false));
        store.Version.Should().Be(0);

        store.Set(new WindowStateSnapshot(1600, 900, false));
        store.Current.Should().Be(new WindowStateSnapshot(1600, 900, false));
        store.Version.Should().Be(1);

        store.Set(new WindowStateSnapshot(1920, 1080, true));
        store.Current.Should().Be(new WindowStateSnapshot(1920, 1080, true));
        store.Version.Should().Be(2);
    }
}
