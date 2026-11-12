using GeoTools.Shared.Application;
using GeoTools.Shared.Application.Geodata;
using GeoTools.Shared.Application.Geospatial;
using GeoTools.Shared.Application.Messaging;
using GeoTools.Shared.Application.Options;
using GeoTools.Shared.Application.State;
using GeoTools.Shared.Application.Storage;
using GeoTools.Shared.Infrastructure.Geodata;
using GeoTools.Shared.Infrastructure.Geospatial;
using GeoTools.Shared.Infrastructure.Messaging;
using GeoTools.Shared.Infrastructure.Persistence;
using GeoTools.Shared.Infrastructure.Processes;
using GeoTools.Shared.Infrastructure.State;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GeoTools.Shared.Infrastructure;

public static class ConfigureServices
{
    public static IServiceCollection SetupInfrastructureLayer(this IServiceCollection services)
    {
        services.SetupApplicationLayer();

        services
            .AddOptions<StorageOptions>()
            .BindConfiguration(StorageOptions.SectionName)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.DatabasePath),
                "Database path is required."
            );

        services
            .AddOptions<GeodataOptions>()
            .BindConfiguration(GeodataOptions.SectionName)
            .PostConfigure<IConfiguration>(
                (options, configuration) =>
                {
                    var section = configuration.GetSection(GeodataOptions.SectionName);
                    var regions = section.GetSection(nameof(GeodataOptions.Regions));
                    if (regions.Exists())
                        options.Regions = regions.Get<string[]>() ?? [];
                    var levels = section.GetSection(nameof(GeodataOptions.ExtractAdminLevels));
                    if (levels.Exists())
                        options.ExtractAdminLevels = levels.Get<int[]>() ?? [];

                    if (!string.IsNullOrWhiteSpace(options.RegionList))
                    {
                        options.Regions = options
                            .RegionList.Split(
                                ',',
                                StringSplitOptions.RemoveEmptyEntries
                                    | StringSplitOptions.TrimEntries
                            )
                            .ToArray();
                    }

                    if (!string.IsNullOrWhiteSpace(options.ExtractAdminLevelsList))
                    {
                        options.ExtractAdminLevels = options
                            .ExtractAdminLevelsList.Split(
                                ',',
                                StringSplitOptions.RemoveEmptyEntries
                                    | StringSplitOptions.TrimEntries
                            )
                            .Select(value => int.TryParse(value, out var level) ? level : -1)
                            .ToArray();
                    }
                }
            )
            .Validate(
                options => options.Regions.Length > 0,
                "At least one geodata region is required."
            )
            .Validate(
                options => options.Regions.All(region => !string.IsNullOrWhiteSpace(region)),
                "Region names must not be blank."
            )
            .Validate(
                options => options.ExtractAdminLevels.Length > 0,
                "At least one extraction admin level is required."
            )
            .Validate(
                options => options.ExtractAdminLevels.All(level => level > 0),
                "Extraction admin levels must be positive."
            )
            .Validate(
                options => options.SimplifyToleranceDegrees >= 0,
                "Simplify tolerance must not be negative."
            )
            .Validate(
                options => options.PullConcurrency is > 0 and <= 8,
                "Pull concurrency must be between 1 and 8."
            )
            .Validate(options => options.RetryCount >= 0, "Retry count must not be negative.")
            .Validate(
                options => options.InactivityTimeoutSeconds > 0,
                "The inactivity timeout must be positive."
            )
            .Validate(
                options =>
                    Uri.TryCreate(options.SourceBaseUrl, UriKind.Absolute, out var uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps),
                "The geodata source must be an absolute HTTP or HTTPS URL."
            );

        services.AddOptions<FeaturesOptions>().BindConfiguration(FeaturesOptions.SectionName);

        services.TryAddSingleton(TimeProvider.System);

        services.AddDbContextFactory<GeoToolsDbContext>(
            (provider, options) =>
            {
                var storage = provider.GetRequiredService<IOptions<StorageOptions>>().Value;
                var path = RepositoryPaths.Resolve(storage.DatabasePath);
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Pooling = true,
                    ForeignKeys = true,
                }.ToString();

                options.UseSqlite(connectionString).AddInterceptors(new SqlitePragmaInterceptor());
            }
        );

        services.AddSingleton<IArtifactStore, ArtifactStore>();
        services.AddSingleton<IJobStore, JobStore>();
        services.AddSingleton<IGeometryCache, GeometryCacheStore>();
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<IStateInitializer, StateInitializer>();

        services.AddSingleton<IMessageBus, SqliteMessageBus>();
        services.AddSingleton<IMessageSubscription, BootstrapProjector>();
        services.AddHostedService<MessageBusService>();

        services.AddSingleton(_ => new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IPbfDownloader, HttpPbfDownloader>();
        services.AddSingleton<IBorderExtractor, BorderExtractor>();
        services.AddSingleton<IBorderGeometryLoader, CachedBorderGeometryLoader>();
        services.AddSingleton<IGeodataPipeline, GeodataPipeline>();
        services.AddSingleton<GeodataWorker>();
        services.AddSingleton<IMessageSubscription>(provider =>
            provider.GetRequiredService<GeodataWorker>()
        );
        services.AddHostedService(provider => provider.GetRequiredService<GeodataWorker>());

        return services;
    }
}
