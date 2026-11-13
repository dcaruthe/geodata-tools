using FluentAssertions;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Presentation.Controls;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class WindowSizePolicyTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    [Fact]
    public void A_changed_setting_applies_after_the_settle_delay()
    {
        var policy = new WindowSizePolicy(new FixedWindow(1200, 800));
        var live = new WindowSizeSnapshot(1200, 800, false);

        policy.Update(new FixedWindow(1600, 900), live, Start).Should().BeEmpty();
        policy
            .Update(new FixedWindow(1600, 900), live, Start.AddMilliseconds(149))
            .Should()
            .BeEmpty();

        var actions = policy.Update(new FixedWindow(1600, 900), live, Start.AddMilliseconds(150));

        actions.Should().ContainSingle();
        actions[0].Kind.Should().Be(WindowActionKind.Apply);
        actions[0].Size.Should().Be(new FixedWindow(1600, 900));
    }

    [Fact]
    public void A_maximized_setting_applies_after_the_settle_delay()
    {
        var policy = new WindowSizePolicy(new FixedWindow(1200, 800));

        policy.Update(WindowSize.Maximized, new WindowSizeSnapshot(1200, 800, false), Start);
        var actions = policy.Update(
            WindowSize.Maximized,
            new WindowSizeSnapshot(1200, 800, false),
            Start.AddMilliseconds(150)
        );

        actions.Should().ContainSingle();
        actions[0].Kind.Should().Be(WindowActionKind.Apply);
        actions[0].Size.Should().Be(WindowSize.Maximized);
    }

    [Fact]
    public void An_applied_setting_is_not_applied_again()
    {
        var policy = new WindowSizePolicy(new FixedWindow(1200, 800));
        policy.Update(new FixedWindow(1600, 900), new WindowSizeSnapshot(1200, 800, false), Start);
        policy
            .Update(
                new FixedWindow(1600, 900),
                new WindowSizeSnapshot(1200, 800, false),
                Start.AddMilliseconds(150)
            )
            .Should()
            .ContainSingle();

        var actions = policy.Update(
            new FixedWindow(1600, 900),
            new WindowSizeSnapshot(1600, 900, false),
            Start.AddSeconds(5)
        );

        actions.Should().BeEmpty();
    }

    [Fact]
    public void A_stable_user_resize_persists_after_one_second()
    {
        var policy = new WindowSizePolicy(new FixedWindow(1200, 800));
        var resized = new WindowSizeSnapshot(1400, 900, false);

        policy.Update(new FixedWindow(1200, 800), resized, Start).Should().BeEmpty();
        policy
            .Update(new FixedWindow(1200, 800), resized, Start.AddMilliseconds(999))
            .Should()
            .BeEmpty();

        var actions = policy.Update(
            new FixedWindow(1200, 800),
            resized,
            Start.AddMilliseconds(1000)
        );

        actions.Should().ContainSingle();
        actions[0].Kind.Should().Be(WindowActionKind.Persist);
        actions[0].Size.Should().Be(new FixedWindow(1400, 900));
        policy
            .Update(new FixedWindow(1200, 800), resized, Start.AddMilliseconds(2000))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void A_change_during_the_stable_second_restarts_the_timer()
    {
        var policy = new WindowSizePolicy(new FixedWindow(1200, 800));

        policy.Update(new FixedWindow(1200, 800), new WindowSizeSnapshot(1400, 900, false), Start);
        policy
            .Update(
                new FixedWindow(1200, 800),
                new WindowSizeSnapshot(1500, 1000, false),
                Start.AddMilliseconds(700)
            )
            .Should()
            .BeEmpty();

        policy
            .Update(
                new FixedWindow(1200, 800),
                new WindowSizeSnapshot(1500, 1000, false),
                Start.AddMilliseconds(1600)
            )
            .Should()
            .BeEmpty();
        policy
            .Update(
                new FixedWindow(1200, 800),
                new WindowSizeSnapshot(1500, 1000, false),
                Start.AddMilliseconds(1700)
            )
            .Should()
            .ContainSingle(action => action.Kind == WindowActionKind.Persist);
    }

    [Fact]
    public void A_maximize_change_persists_after_one_second()
    {
        var policy = new WindowSizePolicy(new FixedWindow(1200, 800));
        var maximized = new WindowSizeSnapshot(1920, 1080, true);

        policy.Update(new FixedWindow(1200, 800), maximized, Start);
        var actions = policy.Update(new FixedWindow(1200, 800), maximized, Start.AddSeconds(1));

        actions.Should().ContainSingle();
        actions[0].Kind.Should().Be(WindowActionKind.Persist);
        actions[0].Size.Should().Be(WindowSize.Maximized);
    }

    [Fact]
    public void One_pixel_differences_count_as_the_applied_size()
    {
        var policy = new WindowSizePolicy(new FixedWindow(1200, 800));
        var jitter = new WindowSizeSnapshot(1201, 799, false);

        policy.Update(new FixedWindow(1200, 800), jitter, Start).Should().BeEmpty();
        policy.Update(new FixedWindow(1200, 800), jitter, Start.AddSeconds(5)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 800)]
    [InlineData(1200, 0)]
    [InlineData(319, 800)]
    [InlineData(1200, 239)]
    [InlineData(7681, 1080)]
    [InlineData(1920, 4321)]
    public void Sizes_outside_the_guards_are_never_persisted(int width, int height)
    {
        var policy = new WindowSizePolicy(new FixedWindow(1200, 800));
        var live = new WindowSizeSnapshot(width, height, false);

        policy.Update(new FixedWindow(1200, 800), live, Start).Should().BeEmpty();
        policy.Update(new FixedWindow(1200, 800), live, Start.AddSeconds(5)).Should().BeEmpty();
    }

    [Fact]
    public void Live_sizes_are_ignored_right_after_an_apply()
    {
        var policy = new WindowSizePolicy(new FixedWindow(1200, 800));
        policy.Update(new FixedWindow(1600, 900), new WindowSizeSnapshot(1200, 800, false), Start);
        policy
            .Update(
                new FixedWindow(1600, 900),
                new WindowSizeSnapshot(1200, 800, false),
                Start.AddMilliseconds(150)
            )
            .Should()
            .ContainSingle(action => action.Kind == WindowActionKind.Apply);

        var transient = new WindowSizeSnapshot(1500, 850, false);
        policy
            .Update(new FixedWindow(1600, 900), transient, Start.AddMilliseconds(450))
            .Should()
            .BeEmpty();
        policy
            .Update(new FixedWindow(1600, 900), transient, Start.AddMilliseconds(1449))
            .Should()
            .BeEmpty();
        policy
            .Update(new FixedWindow(1600, 900), transient, Start.AddMilliseconds(1450))
            .Should()
            .ContainSingle(action => action.Kind == WindowActionKind.Persist);
    }

    [Fact]
    public void Flush_persists_a_changed_live_size_once()
    {
        var policy = new WindowSizePolicy(new FixedWindow(1200, 800));

        var action = policy.Flush(new WindowSizeSnapshot(1400, 900, false));

        action.Should().NotBeNull();
        action!.Value.Kind.Should().Be(WindowActionKind.Persist);
        action.Value.Size.Should().Be(new FixedWindow(1400, 900));
        policy.Flush(new WindowSizeSnapshot(1400, 900, false)).Should().BeNull();
    }

    [Fact]
    public void Flush_ignores_an_unchanged_or_invalid_live_size()
    {
        var policy = new WindowSizePolicy(new FixedWindow(1200, 800));

        policy.Flush(new WindowSizeSnapshot(1201, 799, false)).Should().BeNull();
        policy.Flush(new WindowSizeSnapshot(0, 0, false)).Should().BeNull();
        policy.Flush(new WindowSizeSnapshot(100, 100, false)).Should().BeNull();
    }

    [Fact]
    public void Flush_persists_a_maximized_window()
    {
        var policy = new WindowSizePolicy(new FixedWindow(1200, 800));

        var action = policy.Flush(new WindowSizeSnapshot(1920, 1080, true));

        action.Should().NotBeNull();
        action!.Value.Size.Should().Be(WindowSize.Maximized);
    }
}
