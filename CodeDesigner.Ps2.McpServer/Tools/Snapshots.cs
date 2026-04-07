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
    // Snapshot / Save state
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "snapshot")]
    [DynamicDescription("snapshot_desc")]
    public async Task<string> Snapshot(
        [Description("Absolute path to the output directory where snapshot files will be written.")] string outputPath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "SNAPSHOT", OutputPath = outputPath },
            cancellationToken);

        return FormatSnapshot(json);
    }

    [McpServerTool(Name = "load_snapshot")]
    [DynamicDescription("load_snapshot_desc")]
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
    // Save-state file management (PINE mode)
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "save_state_file")]
    [DynamicDescription("save_state_file_desc")]
    public async Task<string> SaveStateFile(
        [Description("Absolute file path where the save state should be written (e.g. '/saves/slot1.p2s').")] string filePath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "SAVESTATETOFILE", FilePath = filePath }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "load_state_file")]
    [DynamicDescription("load_state_file_desc")]
    public async Task<string> LoadStateFile(
        [Description("Absolute path to the .p2s save-state file to load.")] string filePath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "LOADSTATEFROMFILE", FilePath = filePath }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "extract_p2s")]
    [DynamicDescription("extract_p2s_desc")]
    public async Task<string> ExtractP2s(
        [Description("Absolute path to the .p2s PCSX2 save-state file to extract.")] string filePath,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(new { Command = "EXTRACTP2S", FilePath = filePath }, cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Snapshot history — list saved snapshots from the database
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_snapshots")]
    [DynamicDescription("get_snapshots_desc")]
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
