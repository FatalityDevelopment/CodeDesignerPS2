using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    // -------------------------------------------------------------------------
    // Snapshot / Save state
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "snapshot")]
    [Description("Save a full memory snapshot of the running PS2 emulator to a directory on disk. Snapshots can be compared later to detect memory changes across game states. Provide isoName to link the snapshot to a game so get_snapshots can retrieve it; if omitted the snapshot is saved but not associated with any game.")]
    public async Task<string> Snapshot(
        [Description("ISO filename or game serial used to associate this snapshot with a game (e.g. 'GoW.iso' or 'SCUS-97130'). Omitting this creates an orphaned snapshot that get_snapshots cannot find.")] string? isoName = null,
        [Description("Short human-readable title for the snapshot (e.g. 'before-boss-fight'). Auto-generated from the timestamp when omitted.")] string? title = null,
        [Description("Optional free-text note describing what state was captured.")] string? description = null,
        [Description("Absolute path to the output directory where snapshot files will be written. Auto-generated inside the system temp folder when omitted.")] string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "SNAPSHOT", OutputPath = outputPath, IsoName = isoName, Title = title, Description = description },
            cancellationToken);

        return FormatSnapshot(json);
    }

    [McpServerTool(Name = "load_snapshot")]
    [Description("Reload a previously saved memory snapshot as a save state into the running PS2 emulator. Restores EE and IOP RAM to the state at snapshot time.")]
    public async Task<string> LoadSnapshot(
        [Description("Absolute path to the snapshot directory containing the .bin files to restore.")] string inputPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "LOADSNAPSHOT", InputPath = inputPath },
            cancellationToken);

        return FormatLoadSnapshot(json);
    }

    // -------------------------------------------------------------------------
    // Snapshot history — list saved snapshots from the database
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_snapshots")]
    [Description("List all saved memory snapshots for a game, looked up by ISO filename or game title. Returns snapshot titles, paths, and timestamps so you can choose one to load with load_snapshot.")]
    public async Task<string> GetSnapshots(
        [Description("ISO filename or game title used to look up associated snapshots (e.g. 'GoW.iso' or 'God of War').")] string isoName,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "GETSNAPSHOTS", IsoName = isoName },
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";

            if (!root.TryGetProperty("Snapshots", out var snapshots))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            int count = 0;
            foreach (var s in snapshots.EnumerateArray())
            {
                var title = s.TryGetProperty("Title", out var t)       ? t.GetString()  ?? "?"  : "?";
                var path  = s.TryGetProperty("SnapshotPath", out var p) ? p.GetString() ?? "?"  : "?";
                var date  = s.TryGetProperty("SnapshotDateTime", out var d) ? d.GetString() ?? "?" : "?";
                var desc  = s.TryGetProperty("Description", out var de) && de.ValueKind == JsonValueKind.String
                    ? de.GetString() : null;

                sb.AppendLine($"  [{count + 1}] {title}  ({date})");
                sb.AppendLine($"       Path: {path}");
                if (!string.IsNullOrWhiteSpace(desc))
                    sb.AppendLine($"       Note: {desc}");
                count++;
            }

            if (count == 0)
                return WithRules($"No snapshots found for '{isoName}'.");

            return WithRules($"Snapshots for '{isoName}' ({count}):\n" + sb.ToString().TrimEnd());
        }
        catch
        {
            return json;
        }
    }
}
