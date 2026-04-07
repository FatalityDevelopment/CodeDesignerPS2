using System.Buffers.Binary;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using CodeDesigner.Ps2.CheatEngine;
using CodeDesigner.Ps2.McpServer;
using CodeDesigner.Ps2.McpServer.Prompts;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    // -------------------------------------------------------------------------
    // Progress reports — persistent session memory (database-backed)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "list_progress_reports")]
    [DynamicDescription("list_progress_reports_desc")]
    public async Task<string> ListProgressReports(CancellationToken cancellationToken = default)
    {
        var names = await db.ListMemoriesAsync(cancellationToken);
        if (names.Count == 0)
            return WithRules("No progress reports found. This appears to be a new session.");

        var sb = new StringBuilder();
        sb.AppendLine("Progress reports (call get_progress_report to load one):");
        foreach (var name in names)
            sb.AppendLine($"  {name}");
        return WithRules(sb.ToString().TrimEnd());
    }

    [McpServerTool(Name = "get_progress_report")]
    [DynamicDescription("get_progress_report_desc")]
    public async Task<string> GetProgressReport(
        [Description(
            "The report name as returned by list_progress_reports " +
            "(e.g. 'socom2-player-health', 'gtavc-vehicle-struct').")] string name,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var content = await db.GetMemoryAsync(name, cancellationToken);
            if (content is null)
                return $"Error: no progress report named '{McpDatabaseService.SanitiseName(name)}' found.";
            return WithRules(content);
        }
        catch (ArgumentException ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "save_progress_report")]
    [DynamicDescription("save_progress_report_desc")]
    public async Task<string> SaveProgressReport(
        [Description(
            "Short unique name for this report. Use hyphens as separators. " +
            "Examples: 'socom2-player-health', 'gtavc-vehicle-struct', 'mgs3-codec-addresses'.")] string name,
        [Description(
            "Full Markdown content of the progress report. Include: game name, current objective, " +
            "all discovered addresses with their labels, structure maps, watchpoint findings, " +
            "confirmed cheat codes, and a clear list of next steps.")] string content,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveMemoryAsync(name, content, cancellationToken);
            var sanitised = McpDatabaseService.SanitiseName(name);
            return WithRules($"Progress report '{sanitised}' saved to the database.");
        }
        catch (ArgumentException ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "delete_progress_report")]
    [DynamicDescription("delete_progress_report_desc")]
    public async Task<string> DeleteProgressReport(
        [Description("The report name to delete (as returned by list_progress_reports).")] string name,
        CancellationToken cancellationToken = default)
    {
        try
        {
            bool deleted = await db.DeleteMemoryAsync(name, cancellationToken);
            return deleted
                ? WithRules($"Progress report '{McpDatabaseService.SanitiseName(name)}' deleted.")
                : $"Error: no progress report named '{McpDatabaseService.SanitiseName(name)}' found.";
        }
        catch (ArgumentException ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    // -------------------------------------------------------------------------
    // Settings — application configuration via the socket server database
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_setting")]
    [DynamicDescription("get_setting_desc")]
    public async Task<string> GetSetting(
        [Description("Setting key to retrieve (e.g. 'BiosPath', 'DiscPath', 'ui.selectedGame').")] string key,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "GETSETTING", SettingKey = key }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "set_setting")]
    [DynamicDescription("set_setting_desc")]
    public async Task<string> SetSetting(
        [Description("Setting key to insert or update (e.g. 'BiosPath', 'ui.selectedGame').")] string key,
        [Description("Value to store. Pass an empty string to clear the value.")] string value,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "SETSETTING", SettingKey = key, SettingValue = value },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "get_all_settings")]
    [DynamicDescription("get_all_settings_desc")]
    public async Task<string> GetAllSettings(
        [Description("Optional key prefix to filter results (e.g. 'ui.' returns only UI settings). Leave blank for all settings.")] string? prefix = null,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "GETALLSETTINGS", SettingPrefix = prefix ?? string.Empty },
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";

            if (!root.TryGetProperty("Data", out var data))
                return SimpleMessage(json);

            // Data is a JSON-encoded dictionary of key → value pairs.
            using var dataDoc = JsonDocument.Parse(data.GetString() ?? "{}");
            var sb = new StringBuilder();
            foreach (var prop in dataDoc.RootElement.EnumerateObject())
                sb.AppendLine($"  {prop.Name} = {prop.Value.GetString() ?? "(null)"}");

            return sb.Length > 0
                ? WithRules(sb.ToString().TrimEnd())
                : WithRules("(no settings found)");
        }
        catch
        {
            return json;
        }
    }

    [McpServerTool(Name = "delete_setting")]
    [DynamicDescription("delete_setting_desc")]
    public async Task<string> DeleteSetting(
        [Description("Setting key to remove (e.g. 'ui.selectedGame').")] string key,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "DELETESETTING", SettingKey = key },
            cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Navigation history & snapshot line history
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_nav_history")]
    [DynamicDescription("get_nav_history_desc")]
    public async Task<string> GetNavHistory(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETNAVHISTORY" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("NavHistory", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            var entries = arr.EnumerateArray().ToList();
            if (entries.Count == 0) return "(navigation history is empty)";
            sb.AppendLine($"Navigation history ({entries.Count} entries, newest last):");
            foreach (var item in entries)
            {
                string addr  = item.TryGetProperty("Address",   out var a)   ? (a.GetString()  ?? "?") : "?";
                string? lbl  = item.TryGetProperty("Label",     out var l) && l.ValueKind == JsonValueKind.String
                                   ? l.GetString() : null;
                string src   = item.TryGetProperty("Source",    out var s)   ? (s.GetString()  ?? "?") : "?";
                string ts    = item.TryGetProperty("Timestamp", out var t)   ? (t.GetString()  ?? "?") : "?";
                string name  = lbl != null ? $"{addr}  {lbl}" : addr;
                sb.AppendLine($"  {name,-40} [{src}]  {ts}");
            }
            return sb.ToString().TrimEnd();
        }
        catch { return json; }
    }

    [McpServerTool(Name = "clear_nav_history")]
    [DynamicDescription("clear_nav_history_desc")]
    public async Task<string> ClearNavHistory(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "CLEARNAVHISTORY" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "navigate_to")]
    [DynamicDescription("navigate_to_desc")]
    public async Task<string> NavigateTo(
        [Description("EE virtual address to navigate to (hex or decimal, e.g. '0x8054ABCD').")] string address,
        CancellationToken cancellationToken = default)
    {
        // Parse address to uint so the server can push an ai_activity notification.
        uint addr = address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToUInt32(address, 16)
            : address.StartsWith("0X", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToUInt32(address, 16)
                : uint.Parse(address);

        var json = await bridge.SendAsync(
            new { Command = "NAVIGATETO", Address = addr },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "get_address_snapshot_history")]
    [DynamicDescription("get_address_snapshot_history_desc")]
    public async Task<string> GetAddressSnapshotHistory(
        [Description("EE virtual address to look up (hex or decimal, e.g. '0x80200000').")] string address,
        [Description("Root directory to scan for snapshots (e.g. '/workspace/snapshots/SOCOM2/').")] string snapshotDir,
        CancellationToken cancellationToken = default)
    {
        uint addr = address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                    address.StartsWith("0X", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToUInt32(address, 16)
            : uint.Parse(address);

        var json = await bridge.SendAsync(
            new { Command = "GETADDRESSHISTORY", Address = addr, SnapshotDir = snapshotDir },
            cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("AddressHistory", out var arr))
                return SimpleMessage(json);

            var entries = arr.EnumerateArray().ToList();
            if (entries.Count == 0)
                return $"No snapshot history found for {address} in {snapshotDir}";

            var sb = new StringBuilder();
            sb.AppendLine($"Snapshot history for {address} ({entries.Count} snapshot(s)):");
            foreach (var item in entries)
            {
                string name   = item.TryGetProperty("SnapshotName", out var n) ? (n.GetString() ?? "?") : "?";
                string value  = item.TryGetProperty("Value",        out var v) ? (v.GetString() ?? "?") : "?";
                string ts     = item.TryGetProperty("Timestamp",    out var t) ? (t.GetString() ?? "?") : "?";
                string? disasm = item.TryGetProperty("Disasm", out var d) && d.ValueKind == JsonValueKind.String
                                    ? d.GetString() : null;
                string dis = disasm != null ? $"  {disasm}" : string.Empty;
                sb.AppendLine($"  [{name}]  0x{value}{dis}  ({ts})");
            }
            return sb.ToString().TrimEnd();
        }
        catch { return json; }
    }

    // ── Database query ────────────────────────────────────────────────────────

    private const int QueryDbMaxCellWidth      = 60;
    private const int QueryDbTruncatedCellWidth = 57; // MaxCellWidth - 3 for the "…" suffix

    [McpServerTool(Name = "query_db")]
    [Description("""
        Execute a read-only SELECT query against the CodeDesigner SQLite database and return the results as a formatted table.
        Only SELECT and CTE (WITH … SELECT) statements are permitted — INSERT/UPDATE/DELETE/DROP/CREATE are blocked.

        Available tables:
          Settings          (Key, Value, Description, UpdatedAt)
          LabelSets         (Id, SetName, GameId, IsBuiltIn)
          Labels            (Id, LabelSetId, Address, Name, Type, Hidden, Comment)
          LabelMembers      (Id, LabelId, Offset, Name)
          Games             (Id, Title, Description)
          GameRegions       (Id, Name)
          GameDiscs         (Id, GameId, GameRegionId, Serial, CRC, IsoPath)
          Snapshots         (Id, GameId, Title, Description, SnapshotPath, SnapshotDateTime)
          CheatLists        (Id, GameId, GameDiscId, Title, Description, CreatedAt, UpdatedAt)
          Cheats            (Id, CheatListId, Label, Description, Author, IsEnabled, SortOrder)
          CheatLines        (Id, CheatId, Code, SortOrder)
          McpConfig         (Id, Key, Value)
          McpMemory         (Id, Name, Content)
          McpAgents         (Id, Name)
          McpAgentTasks     (Id, McpAgentId, Phase, SortOrder)

        Examples:
          SELECT * FROM Settings WHERE Key LIKE 'theme%'
          SELECT g.Title, d.Serial, d.IsoPath FROM Games g JOIN GameDiscs d ON d.GameId = g.Id
          SELECT l.Address, l.Name, l.Type FROM Labels l JOIN LabelSets ls ON ls.Id = l.LabelSetId WHERE ls.SetName = 'MyGame'
        """)]
    public async Task<string> QueryDb(
        [Description("A read-only SQL SELECT statement to execute against the CodeDesigner database.")] string sql,
        [Description("Maximum number of rows to display in the formatted output (default 200, max 1000). Full data is still returned in JSON.")] int maxDisplayRows = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return "Error: sql is required.";

        var json = await bridge.SendAsync(new { Command = "QUERYDB", SqlQuery = sql }, cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";

            if (!root.TryGetProperty("DbRows", out var rows) || rows.ValueKind != JsonValueKind.Array)
                return SimpleMessage(json);

            int total = root.TryGetProperty("DbRowCount", out var cnt) ? cnt.GetInt32() : rows.GetArrayLength();
            if (total == 0)
                return "Query returned 0 rows.";

            // Build column headers from first row.
            var firstRow = rows[0];
            var columns  = new List<string>();
            foreach (var prop in firstRow.EnumerateObject())
                columns.Add(prop.Name);

            if (columns.Count == 0) return "Query returned rows with no columns.";

            // Calculate column widths (capped at 60 chars).
            var widths = columns.Select(c => c.Length).ToList();
            int display = Math.Min(total, Math.Clamp(maxDisplayRows, 1, 1000));
            int rowIdx  = 0;
            var cellCache = new List<string[]>(display);
            foreach (var row in rows.EnumerateArray())
            {
                if (rowIdx >= display) break;
                var cells = new string[columns.Count];
                for (int c = 0; c < columns.Count; c++)
                {
                    string cell = "null";
                    if (row.TryGetProperty(columns[c], out var v) && v.ValueKind != JsonValueKind.Null)
                        cell = v.ToString();
                    if (cell.Length > QueryDbMaxCellWidth) cell = cell[..QueryDbTruncatedCellWidth] + "…";
                    cells[c] = cell;
                    if (cell.Length > widths[c]) widths[c] = cell.Length;
                }
                cellCache.Add(cells);
                rowIdx++;
            }

            // Render ASCII table.
            var sb = new StringBuilder();
            string sep = "+" + string.Join("+", widths.Select(w => new string('-', w + 2))) + "+";
            sb.AppendLine(sep);
            sb.Append("|");
            for (int c = 0; c < columns.Count; c++)
                sb.Append($" {columns[c].PadRight(widths[c])} |");
            sb.AppendLine();
            sb.AppendLine(sep);
            foreach (var cells in cellCache)
            {
                sb.Append("|");
                for (int c = 0; c < cells.Length; c++)
                    sb.Append($" {cells[c].PadRight(widths[c])} |");
                sb.AppendLine();
            }
            sb.AppendLine(sep);

            if (total > display)
                sb.AppendLine($"(showing {display} of {total} rows — reduce result set or increase maxDisplayRows)");
            else
                sb.AppendLine($"({total} row{(total == 1 ? "" : "s")})");

            return sb.ToString().TrimEnd();
        }
        catch { return json; }
    }
}
