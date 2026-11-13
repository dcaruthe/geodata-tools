using GeoTools.Shared.Application.Geodata;
using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Messaging.Messages;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Presentation;

internal static class HeadlessGeodataRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
            return Usage();

        var command = args[0];
        var force = args.Contains("--force", StringComparer.Ordinal);
        var region = ReadOption(args, "--region");

        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                Args = [],
                ContentRootPath = AppContext.BaseDirectory,
            }
        );
        builder.Services.SetupInfrastructureLayer();

        using var host = builder.Build();
        await host.Services.GetRequiredService<IStateInitializer>().InitializeAsync();

        if (command == "status")
        {
            await PrintStatusAsync(host.Services);
            return 0;
        }

        var request = command switch
        {
            "pull" => new BootstrapRequest("headless:pull", RebuildScope.Pull, force, region),
            "extract" => new BootstrapRequest(
                "headless:extract",
                RebuildScope.Extract,
                true,
                region
            ),
            "cache" => new BootstrapRequest(
                "headless:cache",
                RebuildScope.GeometryCache,
                force,
                region
            ),
            "rebuild" => new BootstrapRequest("headless:rebuild", RebuildScope.All, force, region),
            _ => null,
        };

        if (request is null)
            return Usage();

        var bus = host.Services.GetRequiredService<IMessageBus>();
        foreach (var subscription in host.Services.GetServices<IMessageSubscription>())
            await subscription.AttachAsync(bus, CancellationToken.None);

        await bus.SubscribeAsync(
            "headless",
            [
                MessageTopics.BootstrapRunStarted,
                MessageTopics.BootstrapStepChanged,
                MessageTopics.BootstrapRunFinished,
            ],
            HandleAsync,
            startAt: null
        );
        await bus.StartAsync();

        var pipeline = host.Services.GetRequiredService<IGeodataPipeline>();
        BootstrapOutcome outcome;
        try
        {
            outcome = await pipeline.RunAsync(request);
        }
        finally
        {
            await bus.DrainAsync();
            await bus.StopAsync();
        }

        Console.WriteLine($"{outcome.Status}: {outcome.Error ?? "done"}");
        return outcome.Status == RunStatus.Succeeded ? 0 : 1;
    }

    private static Task HandleAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        switch (envelope.Topic)
        {
            case MessageTopics.BootstrapRunStarted:
                var started = MessageSerialization.Deserialize<BootstrapRunStarted>(envelope);
                Console.WriteLine(
                    $"Run {started.RunId}: {started.Reason} ({started.Steps.Count} steps)"
                );
                break;
            case MessageTopics.BootstrapStepChanged:
                var step = MessageSerialization.Deserialize<BootstrapStepChanged>(envelope);
                if (step.Status is StepStatus.Succeeded or StepStatus.Failed or StepStatus.Skipped)
                    Console.WriteLine(
                        $"  {step.Key.Value}: {step.Status}{(step.Error is null ? "" : $" ({step.Error})")}"
                    );
                break;
            case MessageTopics.BootstrapRunFinished:
                var finished = MessageSerialization.Deserialize<BootstrapRunFinished>(envelope);
                Console.WriteLine($"Finished {finished.Status} in {finished.DurationSeconds:0.0}s");
                break;
        }

        return Task.CompletedTask;
    }

    private static async Task PrintStatusAsync(IServiceProvider provider)
    {
        var artifacts = provider.GetRequiredService<IArtifactStore>();
        var jobs = provider.GetRequiredService<IJobStore>();
        var options = provider.GetRequiredService<IOptions<GeodataOptions>>().Value;

        Console.WriteLine($"Regions: {string.Join(", ", options.Regions)}");
        foreach (var artifact in await artifacts.ListAsync())
        {
            Console.WriteLine(
                $"{artifact.Kind, -14} {artifact.Region, -20} {ProgressAggregator.FormatBytes(artifact.SizeBytes), 10}  {artifact.Status}"
            );
        }

        if (await jobs.FindLatestRunAsync() is { } run)
            Console.WriteLine($"Last run: {run.Status} ({run.Reason}) {run.Error ?? ""}");
        else
            Console.WriteLine("No runs recorded.");
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int Usage()
    {
        Console.Error.WriteLine(
            "usage: GeoTools.Launcher --geodata <status|pull|extract|cache|rebuild> [--force] [--region name]"
        );
        return 2;
    }
}
