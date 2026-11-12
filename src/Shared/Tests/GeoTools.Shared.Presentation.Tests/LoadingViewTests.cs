using FluentAssertions;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Domain.Primatives;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class LoadingViewTests
{
    [Fact]
    public void Compose_shows_the_stage_fraction_detail_and_error()
    {
        var progress = new ProgressStore();
        progress.Set(
            new BootstrapProgressSnapshot(
                BootstrapPhase.Running,
                RunId.New(),
                "download",
                "Downloading europe",
                "1.5 GB of 33 GB",
                0.42,
                new StepProgress(0.42, 1_500_000_000, 33_000_000_000, 10_000_000, 3000),
                "A warning",
                DateTimeOffset.UnixEpoch
            )
        );
        var view = new LoadingView(progress, new TestTimeProvider(DateTimeOffset.UnixEpoch));

        var scene = view.Compose(new ViewSize(1200, 800));
        var texts = scene.Shapes.OfType<TextDto>().Select(text => text.Text).ToArray();

        texts.Should().Contain("Downloading europe");
        texts.Should().Contain("42%");
        texts.Should().Contain("1.5 GB of 33 GB");
        texts.Should().Contain("A warning");
        texts.Should().Contain(text => text.Contains("50m"));
    }

    [Fact]
    public void Compose_draws_the_spinner_and_a_progress_bar()
    {
        var progress = new ProgressStore();
        progress.Set(BootstrapProgressSnapshot.Idle with { Fraction = 0.5 });

        var scene = new LoadingView(
            progress,
            new TestTimeProvider(DateTimeOffset.UnixEpoch)
        ).Compose(new ViewSize(1200, 800));

        scene.Shapes.OfType<LineDto>().Should().HaveCount(12);
        scene.Shapes.OfType<PolygonDto>().Should().HaveCount(2);
        scene.Shapes.OfType<TextDto>().Should().Contain(text => text.Text.Contains("maintenance"));
    }

    [Fact]
    public void Compose_falls_back_to_a_starting_label()
    {
        var scene = new LoadingView(
            new ProgressStore(),
            new TestTimeProvider(DateTimeOffset.UnixEpoch)
        ).Compose(new ViewSize(1200, 800));

        scene.Shapes.OfType<TextDto>().Should().Contain(text => text.Text == "Starting");
    }

    [Fact]
    public void Id_is_loading()
    {
        new LoadingView(new ProgressStore(), TimeProvider.System).Id.Should().Be(ViewId.Loading);
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
