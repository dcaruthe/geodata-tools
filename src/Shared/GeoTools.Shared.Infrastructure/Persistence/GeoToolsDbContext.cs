using Microsoft.EntityFrameworkCore;

namespace GeoTools.Shared.Infrastructure.Persistence;

internal sealed class GeoToolsDbContext(DbContextOptions<GeoToolsDbContext> options)
    : DbContext(options)
{
    public DbSet<MessageRow> Messages => Set<MessageRow>();

    public DbSet<SubscriptionRow> Subscriptions => Set<SubscriptionRow>();

    public DbSet<RunRow> Runs => Set<RunRow>();

    public DbSet<StepRow> Steps => Set<StepRow>();

    public DbSet<ArtifactRow> Artifacts => Set<ArtifactRow>();

    public DbSet<GeometryCacheRow> GeometryCache => Set<GeometryCacheRow>();

    public DbSet<SettingRow> Settings => Set<SettingRow>();

    public DbSet<FeatureFlagRow> FeatureFlags => Set<FeatureFlagRow>();

    public DbSet<HandledRequestRow> HandledRequests => Set<HandledRequestRow>();

    public DbSet<RegionRow> Regions => Set<RegionRow>();

    public DbSet<BorderLineRow> BorderLines => Set<BorderLineRow>();

    public DbSet<RegionResolutionRow> RegionResolutions => Set<RegionResolutionRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MessageRow>(entity =>
        {
            entity.ToTable("Messages");
            entity.HasKey(row => row.Sequence);
            entity.Property(row => row.Sequence).ValueGeneratedOnAdd();
            entity.Property(row => row.Topic).IsRequired();
            entity.Property(row => row.Body).IsRequired();
            entity.Property(row => row.Publisher).IsRequired();
            entity.HasIndex(row => new { row.Topic, row.Sequence });
            entity.HasIndex(row => row.PublishedAt);
        });

        modelBuilder.Entity<SubscriptionRow>(entity =>
        {
            entity.ToTable("Subscriptions");
            entity.HasKey(row => row.Subscriber);
        });

        modelBuilder.Entity<RunRow>(entity =>
        {
            entity.ToTable("Runs");
            entity.HasKey(row => row.Id);
        });

        modelBuilder.Entity<StepRow>(entity =>
        {
            entity.ToTable("Steps");
            entity.HasKey(row => new { row.RunId, row.StepKey });
            entity.HasIndex(row => row.RunId);
            entity
                .HasOne<RunRow>()
                .WithMany()
                .HasForeignKey(row => row.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ArtifactRow>(entity =>
        {
            entity.ToTable("Artifacts");
            entity.HasKey(row => row.Key);
        });

        modelBuilder.Entity<GeometryCacheRow>(entity =>
        {
            entity.ToTable("GeometryCache");
            entity.HasKey(row => row.Key);
        });

        modelBuilder.Entity<SettingRow>(entity =>
        {
            entity.ToTable("Settings");
            entity.HasKey(row => row.Key);
        });

        modelBuilder.Entity<FeatureFlagRow>(entity =>
        {
            entity.ToTable("FeatureFlags");
            entity.HasKey(row => row.Name);
        });

        modelBuilder.Entity<HandledRequestRow>(entity =>
        {
            entity.ToTable("HandledRequests");
            entity.HasKey(row => row.RequestId);
        });

        modelBuilder.Entity<RegionRow>(entity =>
        {
            entity.ToTable("Regions");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.AdminLevel);
            entity.HasIndex(row => new { row.ParentId, row.AdminLevel });
            entity.HasIndex(row => row.SourceKey);
            entity.HasIndex(row => new
            {
                row.AdminLevel,
                row.MinLongitude,
                row.MaxLongitude,
                row.MinLatitude,
                row.MaxLatitude,
            });
        });

        modelBuilder.Entity<BorderLineRow>(entity =>
        {
            entity.ToTable("BorderLines");
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.AdminLevel);
            entity.HasIndex(row => row.SourceKey);
            entity.HasIndex(row => new
            {
                row.AdminLevel,
                row.MinLongitude,
                row.MaxLongitude,
                row.MinLatitude,
                row.MaxLatitude,
            });
        });

        modelBuilder.Entity<RegionResolutionRow>(entity =>
        {
            entity.ToTable("RegionResolutions");
            entity.HasKey(row => new { row.RegionId, row.ChildLevel });
        });
    }
}
