using FluentAssertions;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Presentation.Controls;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class ViewShortcutsTests
{
    [Fact]
    public void Number_keys_map_to_their_views()
    {
        ViewShortcuts.TryGetView(Keys.D1, out var first).Should().BeTrue();
        first.Should().Be(ViewId.Map);

        ViewShortcuts.TryGetView(Keys.D2, out var second).Should().BeTrue();
        second.Should().Be(ViewId.Maintenance);
    }

    [Fact]
    public void Unmapped_keys_are_rejected()
    {
        ViewShortcuts.TryGetView(Keys.A, out _).Should().BeFalse();
    }
}
