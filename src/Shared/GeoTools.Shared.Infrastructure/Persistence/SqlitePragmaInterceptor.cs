using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GeoTools.Shared.Infrastructure.Persistence;

internal sealed class SqlitePragmaInterceptor(bool readOnly = false) : DbConnectionInterceptor
{
    private const string ReadWritePragmas =
        "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";

    // A read-only connection cannot set the journal mode, so only the pragmas that work on
    // a reader run here. The database is already in WAL because the launcher created it.
    private const string ReadOnlyPragmas = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";

    private string Pragmas => readOnly ? ReadOnlyPragmas : ReadWritePragmas;

    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData
    ) => Apply(connection);

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default
    ) => await ApplyAsync(connection, cancellationToken);

    private void Apply(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }

    private async Task ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
