using Microsoft.EntityFrameworkCore;

namespace GeoTools.Shared.Infrastructure.Persistence;

internal static class SqliteSchema
{
    public const int CurrentVersion = 1;

    private static readonly string[][] Migrations =
    [
        [
            """
                CREATE TABLE IF NOT EXISTS Messages (
                    Sequence INTEGER NOT NULL CONSTRAINT PK_Messages PRIMARY KEY AUTOINCREMENT,
                    Topic TEXT NOT NULL,
                    Body BLOB NOT NULL,
                    PublishedAt INTEGER NOT NULL,
                    Publisher TEXT NOT NULL,
                    CorrelationId TEXT NULL
                );
                """,
            "CREATE INDEX IF NOT EXISTS IX_Messages_Topic_Sequence ON Messages (Topic, Sequence);",
            "CREATE INDEX IF NOT EXISTS IX_Messages_PublishedAt ON Messages (PublishedAt);",
            """
                CREATE TABLE IF NOT EXISTS Subscriptions (
                    Subscriber TEXT NOT NULL CONSTRAINT PK_Subscriptions PRIMARY KEY,
                    Topics TEXT NOT NULL,
                    LastSequence INTEGER NOT NULL,
                    Attempts INTEGER NOT NULL DEFAULT 0,
                    ParkedAt INTEGER NULL,
                    LastError TEXT NULL
                );
                """,
            """
                CREATE TABLE IF NOT EXISTS Runs (
                    Id TEXT NOT NULL CONSTRAINT PK_Runs PRIMARY KEY,
                    Reason TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    StartedAt INTEGER NOT NULL,
                    FinishedAt INTEGER NULL,
                    Error TEXT NULL
                );
                """,
            """
                CREATE TABLE IF NOT EXISTS Steps (
                    RunId TEXT NOT NULL,
                    StepKey TEXT NOT NULL,
                    Ordinal INTEGER NOT NULL,
                    Stage TEXT NOT NULL,
                    Label TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    Attempts INTEGER NOT NULL,
                    Fraction REAL NOT NULL,
                    BytesReceived INTEGER NULL,
                    TotalBytes INTEGER NULL,
                    BytesPerSecond REAL NULL,
                    EtaSeconds REAL NULL,
                    Error TEXT NULL,
                    CONSTRAINT PK_Steps PRIMARY KEY (RunId, StepKey),
                    CONSTRAINT FK_Steps_Runs FOREIGN KEY (RunId) REFERENCES Runs (Id) ON DELETE CASCADE
                );
                """,
            "CREATE INDEX IF NOT EXISTS IX_Steps_RunId ON Steps (RunId);",
            """
                CREATE TABLE IF NOT EXISTS Artifacts (
                    Key TEXT NOT NULL CONSTRAINT PK_Artifacts PRIMARY KEY,
                    Kind TEXT NOT NULL,
                    Region TEXT NULL,
                    Path TEXT NOT NULL,
                    SizeBytes INTEGER NOT NULL,
                    ModifiedAt INTEGER NOT NULL,
                    Fingerprint TEXT NOT NULL,
                    AdminLevels TEXT NOT NULL,
                    Bounds TEXT NULL,
                    Status TEXT NOT NULL,
                    UpdatedAt INTEGER NOT NULL
                );
                """,
            """
                CREATE TABLE IF NOT EXISTS GeometryCache (
                    Key TEXT NOT NULL CONSTRAINT PK_GeometryCache PRIMARY KEY,
                    Region TEXT NOT NULL,
                    AdminLevels TEXT NOT NULL,
                    Tolerance REAL NOT NULL,
                    CodecVersion INTEGER NOT NULL,
                    PointCount INTEGER NOT NULL,
                    Bounds TEXT NOT NULL,
                    Blob BLOB NOT NULL,
                    CreatedAt INTEGER NOT NULL
                );
                """,
            """
                CREATE TABLE IF NOT EXISTS Settings (
                    Key TEXT NOT NULL CONSTRAINT PK_Settings PRIMARY KEY,
                    Value TEXT NOT NULL,
                    Source TEXT NOT NULL,
                    UpdatedAt INTEGER NOT NULL
                );
                """,
            """
                CREATE TABLE IF NOT EXISTS FeatureFlags (
                    Name TEXT NOT NULL CONSTRAINT PK_FeatureFlags PRIMARY KEY,
                    Enabled INTEGER NOT NULL,
                    UpdatedAt INTEGER NOT NULL
                );
                """,
            """
                CREATE TABLE IF NOT EXISTS HandledRequests (
                    RequestId TEXT NOT NULL CONSTRAINT PK_HandledRequests PRIMARY KEY,
                    HandledAt INTEGER NOT NULL
                );
                """,
        ],
    ];

    public static async Task InitializeAsync(
        GeoToolsDbContext context,
        CancellationToken cancellationToken
    )
    {
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            var version = await ReadUserVersionAsync(connection, cancellationToken);
            if (version > CurrentVersion)
                throw new InvalidOperationException(
                    $"Database schema {version} is newer than this build supports ({CurrentVersion})."
                );

            if (version == CurrentVersion)
                return;

            await using var transaction = await context.Database.BeginTransactionAsync(
                cancellationToken
            );

            for (var index = version; index < CurrentVersion; index++)
            {
                foreach (var statement in Migrations[index])
                    await context.Database.ExecuteSqlRawAsync(statement, cancellationToken);
            }

            await context.Database.ExecuteSqlRawAsync(
                $"PRAGMA user_version = {CurrentVersion};",
                cancellationToken
            );
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static async Task<int> ReadUserVersionAsync(
        System.Data.Common.DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value);
    }
}
