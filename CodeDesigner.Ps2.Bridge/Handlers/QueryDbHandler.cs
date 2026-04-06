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

        // Keywords that must never appear in a read-only query.
        private static readonly string[] _queryDbBlocklist =
        [
            "INSERT", "UPDATE", "DELETE", "DROP", "CREATE", "ALTER",
            "ATTACH", "DETACH", "VACUUM", "PRAGMA WRITEABLE",
        ];

        /// <summary>
        /// Handle QUERYDB — execute a read-only SELECT statement against the
        /// CodeDesigner SQLite database and return the results as JSON rows.
        /// <para>
        /// Only SELECT and WITH … SELECT (CTE) statements are permitted.
        /// Attempts to modify data are rejected.
        /// </para>
        /// <para>Available tables:</para>
        /// <list type="bullet">
        ///   <item><description>Settings (Key, Value, Description, UpdatedAt)</description></item>
        ///   <item><description>LabelSets (Id, SetName, GameId, IsBuiltIn)</description></item>
        ///   <item><description>Labels (Id, LabelSetId, Address, Name, Type, Hidden, Comment)</description></item>
        ///   <item><description>LabelMembers (Id, LabelId, Offset, Name)</description></item>
        ///   <item><description>Games (Id, Title, Description)</description></item>
        ///   <item><description>GameRegions (Id, Name)</description></item>
        ///   <item><description>GameDiscs (Id, GameId, GameRegionId, Serial, CRC, IsoPath)</description></item>
        ///   <item><description>Snapshots (Id, GameId, Title, Description, SnapshotPath, SnapshotDateTime)</description></item>
        ///   <item><description>CheatLists (Id, GameId, GameDiscId, Title, Description, CreatedAt, UpdatedAt)</description></item>
        ///   <item><description>Cheats (Id, CheatListId, Label, Description, Author, IsEnabled, SortOrder)</description></item>
        ///   <item><description>CheatLines (Id, CheatId, Code, SortOrder)</description></item>
        ///   <item><description>McpConfig (Id, Key, Value)</description></item>
        ///   <item><description>McpMemory (Id, Name, Content)</description></item>
        ///   <item><description>McpAgents (Id, Name)</description></item>
        ///   <item><description>McpAgentTasks (Id, McpAgentId, Phase, SortOrder)</description></item>
        /// </list>
        /// </summary>
        private string HandleQueryDb(string? sql)
        {
            if (Db == null)
                return CreateErrorResponse("Database is not available");
            if (string.IsNullOrWhiteSpace(sql))
                return CreateErrorResponse("SqlQuery is required for QUERYDB");

            var trimmed = sql.TrimStart();

            // Only allow SELECT or WITH … SELECT (CTEs).
            if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("WITH",   StringComparison.OrdinalIgnoreCase))
                return CreateErrorResponse(
                    "QUERYDB only supports read-only SELECT queries (or CTEs starting with WITH … SELECT)");

            // Blocklist: reject any mutating or structural keyword.
            var upper = sql.ToUpperInvariant();
            foreach (var bad in _queryDbBlocklist)
            {
                if (upper.Contains(bad, StringComparison.Ordinal))
                    return CreateErrorResponse(
                        $"QUERYDB does not permit '{bad}' statements — only SELECT is allowed");
            }

            try
            {
                var conn = Db.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open)
                    conn.Open();

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

                var opts = new JsonSerializerOptions { WriteIndented = false };
                return JsonSerializer.Serialize(new CommandResponse
                {
                    Success    = true,
                    Command    = "QUERYDB",
                    Message    = $"{rows.Count} row(s) returned",
                    DbRows     = rows,
                    DbRowCount = rows.Count,
                }, opts);
            }
            catch (Exception ex)
            {
                return CreateErrorResponse($"QUERYDB error: {ex.Message}");
            }
        }
    }
}
