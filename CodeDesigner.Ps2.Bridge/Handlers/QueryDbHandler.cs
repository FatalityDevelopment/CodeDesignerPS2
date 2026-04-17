using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.Ps2.Bridge
{
    partial class SocketServer
    {
        // ── QUERYDB ───────────────────────────────────────────────────────────

        // DDL blocklist — prevent destructive schema operations.
        // DML (INSERT / UPDATE / DELETE) is intentionally allowed so the MCP
        // agent can bulk-import data directly via the query_db tool.
        private static readonly string[] _queryDbDdlBlocklist =
        [
            "DROP ", "CREATE ", "ALTER ", "ATTACH ", "DETACH ", "VACUUM",
        ];

        /// <summary>
        /// Handle QUERYDB — execute a SQL statement against the CodeDesigner
        /// SQL Server database and return the results as JSON.
        /// <para>
        /// SELECT / CTE statements return the full result set as row objects.
        /// DML statements (INSERT / UPDATE / DELETE) are committed and return the
        /// number of rows affected — useful for bulk-importing memory entries or
        /// cheat data without going through the seeder pipeline.
        /// </para>
        /// <para>
        /// DDL operations (DROP, CREATE, ALTER, ATTACH, DETACH, VACUUM) are
        /// blocked to prevent accidental schema destruction.
        /// </para>
        /// </summary>
        private string HandleQueryDb(string? sql)
        {
            if (Db == null)
                return CreateErrorResponse("Database is not available");
            if (string.IsNullOrWhiteSpace(sql))
                return CreateErrorResponse("SqlQuery is required for QUERYDB");

            // Block DDL — schema mutations are never permitted via this path.
            var upper = sql.ToUpperInvariant();
            foreach (var ddl in _queryDbDdlBlocklist)
            {
                if (upper.Contains(ddl, StringComparison.Ordinal))
                    return CreateErrorResponse(
                        $"QUERYDB does not permit '{ddl.TrimEnd()}' statements — use EF Core migrations for schema changes");
            }

            // Determine whether this is a read (SELECT/CTE) or write (DML) statement.
            var trimmed = sql.TrimStart();
            bool isSelect = trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                         || trimmed.StartsWith("WITH",   StringComparison.OrdinalIgnoreCase);

            try
            {
                var conn = Db.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open)
                    conn.Open();

                if (isSelect)
                {
                    // ── Read path: no transaction needed ─────────────────────
                    using DbCommand cmd = conn.CreateCommand();
                    cmd.CommandText = sql;

                    var rows = new List<Dictionary<string, object?>>();
                    using DbDataReader reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var row = new Dictionary<string, object?>(reader.FieldCount);
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[reader.GetName(i)] = reader.IsDBNull(i)
                                ? null
                                : reader.GetValue(i);
                        }
                        rows.Add(row);
                    }

                    return JsonSerializer.Serialize(new CommandResponse
                    {
                        Success    = true,
                        Command    = "QUERYDB",
                        Message    = $"{rows.Count} row(s) returned",
                        DbRows     = rows,
                        DbRowCount = rows.Count,
                    });
                }
                else
                {
                    // ── Write path: commit on success, rollback on error ──────
                    using DbTransaction txn = conn.BeginTransaction();
                    try
                    {
                        using DbCommand cmd = conn.CreateCommand();
                        cmd.CommandText = sql;
                        cmd.Transaction = txn;

                        int affected = cmd.ExecuteNonQuery();
                        txn.Commit();

                        return JsonSerializer.Serialize(new CommandResponse
                        {
                            Success    = true,
                            Command    = "QUERYDB",
                            Message    = $"{affected} row(s) affected",
                            DbRowCount = affected,
                        });
                    }
                    catch
                    {
                        try { txn.Rollback(); } catch { /* connection already closed */ }
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"QUERYDB error: {ex.Message}");
            }
        }
    }
}
