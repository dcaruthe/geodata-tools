using FluentAssertions;
using GeoTools.Shared.Application.Scenes;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Presentation.Views;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class ViewInputRouterTests
{
    [Fact]
    public void Input_is_routed_to_the_active_view()
    {
        var map = new InputView(ViewId.Map);
        var maintenance = new InputView(ViewId.Maintenance);
        var mediator = new FakeNavigationMediator();
        var router = new ViewInputRouter(
            [new TestView(ViewId.Loading), map, maintenance],
            mediator
        );

        router.Handle(ViewKey.Enter).Should().BeFalse();

        mediator.NavigateTo(ViewId.Map);
        router.Handle(ViewKey.Enter).Should().BeTrue();
        map.Keys.Should().ContainSingle().Which.Should().Be(ViewKey.Enter);

        mediator.NavigateTo(ViewId.Maintenance);
        router.Handle(ViewKey.Down).Should().BeTrue();
        maintenance.Keys.Should().ContainSingle().Which.Should().Be(ViewKey.Down);
    }

    private sealed class InputView(ViewId id) : IViewInputTarget
    {
        public List<ViewKey> Keys { get; } = [];

        public ViewId Id { get; } = id;

        public SceneDto Compose(ViewSize size) => new([], default);

        public bool Handle(ViewKey key)
        {
            Keys.Add(key);
            return true;
        }
    }
}
