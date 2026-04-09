using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CodeDesigner.Ps2.Database;

/// <summary>
/// EF Core connection interceptor that applies essential SQLite PRAGMAs
/// automatically on every connection open, for both synchronous and
/// asynchronous open paths.
///
/// PRAGMAs set:
/// <list type="bullet">
///   <item><term>journal_mode=WAL</term>
///     <description>
///       Write-Ahead Logging — allows multiple concurrent readers alongside
///       one writer, dramatically reducing "database is locked" errors.
///     </description>
///   </item>
///   <item><term>busy_timeout=30000</term>
///     <description>
///       Instructs SQLite to spin-wait up to 30 seconds when it encounters a
///       write lock held by another connection or process, rather than
///       immediately returning SQLITE_BUSY (error 5).  This is the primary
///       guard against transient cross-process write conflicts between the
///       Bridge socket server and parallel MCP agent processes.
///     </description>
///   </item>
/// </list>
/// </summary>
internal sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string PragmaSql =
        "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=30000;";

    /// <inheritdoc/>
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyPragmas(connection);
        base.ConnectionOpened(connection, eventData);
    }

    /// <inheritdoc/>
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyPragmasAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ApplyPragmas(DbConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = PragmaSql;
        cmd.ExecuteNonQuery();
    }

    private static async Task ApplyPragmasAsync(DbConnection connection, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = PragmaSql;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
