using FluentAssertions;
using GeoTools.Shared.Application.Link;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Views;
using GeoTools.Shared.Infrastructure.Link;
using GeoTools.Shared.Infrastructure.Logging;
using GeoTools.Shared.Presentation.Link;
using GeoTools.Shared.Presentation.Options;
using GeoTools.Shared.Presentation.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Presentation.Tests;

public sealed class LauncherLinkServiceTests : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"geotools-service-{Guid.NewGuid():N}"
    );

    public LauncherLinkServiceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task A_disabled_service_leaves_the_client_without_a_key()
    {
        var harness = await Harness.CreateAsync(_directory, enabled: false);
        await using var client = harness.Client;

        var act = async () => await client.GetStateAsync();

        await act.Should()
            .ThrowAsync<LinkUnavailableException>()
            .Where(exception => exception.Code == LinkError.KeyMissing);
    }

    [Fact]
    public async Task State_logs_maintenance_and_settings_answer_without_the_window()
    {
        await using var harness = await Harness.CreateAsync(_directory);
        harness.Logs.CreateLogger("test").LogWarning("a warning from the launcher");
        harness.Jobs.LatestRun = new RunRecord(
            RunId.New(),
            "startup",
            RunStatus.Running,
            DateTimeOffset.UnixEpoch,
            null,
            null
        );

        var state = await harness.Client.GetStateAsync();
        var logs = await harness.Client.GetLogsAsync("Warning", 10, null);
        var maintenance = await harness.Client.GetMaintenanceAsync();
        var settings = await harness.Client.GetSettingsAsync();

        state.Link.Enabled.Should().BeTrue();
        state.ProtocolVersion.Should().Be(LinkProtocol.Version);
        state.LatestRun!.Status.Should().Be("Running");
        state.Diagnostics.SocketPath.Should().Contain("launcher.sock");
        logs.Entries.Should().ContainSingle();
        logs.Entries[0].Message.Should().Be("a warning from the launcher");
        maintenance.Rows.Should().NotBeEmpty();
        maintenance.SelectedRowId.Should().NotBeNull();
        settings.DisplayLevels.Should().Equal(2);
        settings.Tolerance.Should().Be(0.002);
    }

    [Fact]
    public async Task Settings_commands_reach_the_settings_store()
    {
        await using var harness = await Harness.CreateAsync(_directory);

        (await harness.Client.SetDisplayLevelsAsync([2, 4])).Accepted.Should().BeTrue();
        (await harness.Client.SetExtractLevelsAsync([2, 4, 6])).Accepted.Should().BeTrue();
        (await harness.Client.SetToleranceAsync(0.005)).Accepted.Should().BeTrue();
        (await harness.Client.SetFeatureFlagAsync("MapGrid", false)).Accepted.Should().BeTrue();
        (await harness.Client.ResetSettingsAsync()).Accepted.Should().BeTrue();

        harness.Settings.Map.AdminLevels.Should().Equal(2);
        harness.Settings.IsEnabled(FeatureFlag.MapGrid).Should().BeTrue();
    }

    [Fact]
    public async Task An_unknown_feature_flag_is_refused()
    {
        await using var harness = await Harness.CreateAsync(_directory);

        var act = async () => await harness.Client.SetFeatureFlagAsync("Nope", true);

        await act.Should()
            .ThrowAsync<LinkUnavailableException>()
            .Where(exception => exception.Code == LinkError.InvalidRequest);
    }

    [Fact]
    public async Task Window_commands_cross_the_inbox()
    {
        await using var harness = await Harness.CreateAsync(_directory);

        var navigate = await harness.Client.NavigateAsync(ViewId.Maintenance);
        var keys = await harness.Client.PressKeysAsync([WindowKey.Down]);
        var select = await harness.Client.MaintenanceAsync(
            MaintenanceOperation.Select,
            "displayLevels",
            0,
            false
        );
        var pipeline = await harness.Client.PipelineAsync(PipelineAction.Cancel, false);
        var shot = await harness.Client.ScreenshotAsync(320, 200);

        navigate.ActiveView.Should().Be(ViewId.Maintenance);
        keys.Status.Should().Be("ok");
        select.Accepted.Should().BeTrue();
        pipeline.Status.Should().Be("ok");
        shot.Width.Should().Be(320);
        shot.Mode.Should().Be(nameof(ScreenshotMode.Scene));
    }

    [Fact]
    public async Task A_socket_path_that_cannot_be_bound_leaves_the_service_running()
    {
        var options = Options(Path.Combine(_directory, "launcher.sock"));
        options.SocketPath = _directory;
        await using var harness = await Harness.CreateAsync(_directory, options: options);

        var act = async () => await harness.Client.GetStateAsync();

        await act.Should().ThrowAsync<LinkUnavailableException>();
        harness.ServiceHasNoServer.Should().BeTrue();
    }

    [Fact]
    public async Task Stopping_closes_the_inbox_and_removes_the_socket()
    {
        await using var harness = await Harness.CreateAsync(_directory);
        var socket = harness.SocketPath;

        await harness.Service.StopAsync(CancellationToken.None);

        Path.Exists(socket).Should().BeFalse();
        var act = async () => await harness.Client.GetStateAsync();
        await act.Should().ThrowAsync<LinkUnavailableException>();
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (IOException) { }

        return ValueTask.CompletedTask;
    }

    private static GeoTools.Shared.Application.Link.LinkOptions Options(string socket) =>
        new()
        {
            Enabled = true,
            SocketPath = socket,
            KeyPath = Path.Combine(Path.GetDirectoryName(socket)!, "launcher.key"),
            RotateSeconds = 900,
            CommandTimeoutSeconds = 5,
            HandshakeTimeoutSeconds = 5,
        };

    private sealed class Harness : IAsyncDisposable
    {
        private readonly CancellationTokenSource _drainCts = new();
        private readonly Task _drain;
        private readonly LauncherLinkService _service;
        private readonly WindowCommandInbox _inbox;

        private Harness(
            LauncherLinkService service,
            WindowCommandInbox inbox,
            LauncherLinkClient client,
            TestSettingsStore settings,
            FakeJobStore jobs,
            InMemoryLogStore logs,
            string socketPath
        )
        {
            _service = service;
            _inbox = inbox;
            Client = client;
            Settings = settings;
            Jobs = jobs;
            Logs = logs;
            SocketPath = socketPath;
            _drain = Task.Run(DrainAsync);
        }

        public LauncherLinkClient Client { get; }

        public LauncherLinkService Service => _service;

        public TestSettingsStore Settings { get; }

        public FakeJobStore Jobs { get; }

        public InMemoryLogStore Logs { get; }

        public string SocketPath { get; }

        public bool ServiceHasNoServer => _service.HasServer is false;

        public static async Task<Harness> CreateAsync(
            string directory,
            bool enabled = true,
            GeoTools.Shared.Application.Link.LinkOptions? options = null
        )
        {
            options ??= Options(Path.Combine(directory, "launcher.sock"));
            options.Enabled = enabled;

            var settings = new TestSettingsStore();
            var bus = new FakeMessageBus();
            var jobs = new FakeJobStore();
            var progress = new ProgressStore();
            var controller = new MaintenanceController(
                settings,
                jobs,
                new FakeArtifactStore(),
                progress,
                new BorderGeometryStore(),
                bus,
                Microsoft.Extensions.Options.Options.Create(
                    new GeodataOptions { Regions = ["africa"] }
                ),
                new FakeNavigationMediator(),
                TimeProvider.System,
                NullLogger<MaintenanceController>.Instance
            );
            await controller.AttachAsync(bus);

            var inbox = new WindowCommandInbox();
            var logs = new InMemoryLogStore();
            var service = new LauncherLinkService(
                Microsoft.Extensions.Options.Options.Create(options),
                Microsoft.Extensions.Options.Options.Create(
                    new StorageOptions { DatabasePath = Path.Combine(directory, "state.db") }
                ),
                Microsoft.Extensions.Options.Options.Create(
                    new GeodataOptions { Regions = ["africa"] }
                ),
                Microsoft.Extensions.Options.Options.Create(new WindowOptions()),
                TimeProvider.System,
                inbox,
                progress,
                jobs,
                settings,
                controller,
                new FakeNavigationMediator(),
                logs,
                NullLoggerFactory.Instance,
                NullLogger<LauncherLinkService>.Instance
            );
            await service.StartAsync(CancellationToken.None);

            var client = new LauncherLinkClient(
                Microsoft.Extensions.Options.Options.Create(options),
                TimeProvider.System,
                NullLogger<LauncherLinkClient>.Instance
            );

            return new Harness(
                service,
                inbox,
                client,
                settings,
                jobs,
                logs,
                GeoTools.Shared.Infrastructure.Link.LinkKey.ResolvePath(options.SocketPath)
            );
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await _service.StopAsync(CancellationToken.None);
            await _drainCts.CancelAsync();
            try
            {
                await _drain;
            }
            catch (OperationCanceledException) { }

            _drainCts.Dispose();
        }

        private async Task DrainAsync()
        {
            while (!_drainCts.IsCancellationRequested)
            {
                while (_inbox.TryDequeue(out var command))
                {
                    var response = command!.Request switch
                    {
                        NavigateRequest navigate => LinkResponse.Success(
                            navigate.Id,
                            CommandResultDto.Ok("ok", navigate.View)
                        ),
                        ScreenshotRequest screenshot => LinkResponse.Success(
                            screenshot.Id,
                            new ScreenshotDto(
                                [1, 2, 3],
                                screenshot.Width ?? 0,
                                screenshot.Height ?? 0,
                                ViewId.Map,
                                DateTimeOffset.UnixEpoch,
                                "Scene"
                            )
                        ),
                        _ => LinkResponse.Success(
                            command.Request.Id,
                            new CommandResultDto(true, "ok", ViewId.Map, null)
                        ),
                    };
                    command.Completion.TrySetResult(response);
                }

                await Task.Delay(5, _drainCts.Token);
            }
        }
    }
}
