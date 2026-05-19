using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    // -------------------------------------------------------------------------
    // Label groups — the cross-cutting "objectmap"
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "group_list")]
    [DynamicDescription("group_list_desc")]
    [Description("List all label groups (classes, modules, subsystems, vtables) defined in a label set, including each group's member labels. Label groups collect scattered labels — e.g. functions at unrelated addresses — into a single named object for the Structure Visualizer.")]
    public async Task<string> ListLabelGroups(
        [Description("Game identifier / ISO serial (e.g. 'SCUS-97130').")] string gameId,
        [Description("Label set whose groups to list (e.g. 'ai-analysis').")] string setName,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new
            {
                Command = "GETLABELGROUPS",
                Data    = JsonSerializer.Serialize(new { gameId, setName }),
            },
            cancellationToken);
        return ExtractGroupData(json);
    }

    [McpServerTool(Name = "group_create")]
    [DynamicDescription("group_create_desc")]
    [Description("Create or update a label group. A group is a named object (class, module, subsystem, or vtable) that the Structure Visualizer renders as a diagram. Add labels to it with group_add_member.")]
    public async Task<string> CreateLabelGroup(
        [Description("Game identifier / ISO serial (e.g. 'SCUS-97130').")] string gameId,
        [Description("Label set to create the group in (e.g. 'ai-analysis').")] string setName,
        [Description("Group name, e.g. 'PlayerController'.")] string name,
        [Description("Group kind: Class (default), Module, Subsystem, or VTable.")] string? kind = null,
        [Description("Optional description shown as a tooltip / class note.")] string? comment = null,
        [Description("Optional CSS colour (e.g. '#7fd1b9') used to tint the group in diagrams.")] string? color = null,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new
            {
                Command = "ADDLABELGROUP",
                Data    = JsonSerializer.Serialize(new { gameId, setName, name, kind, comment, color }),
            },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "group_delete")]
    [DynamicDescription("group_delete_desc")]
    [Description("Delete a label group. The member labels themselves are not deleted — only the grouping.")]
    public async Task<string> DeleteLabelGroup(
        [Description("Game identifier / ISO serial (e.g. 'SCUS-97130').")] string gameId,
        [Description("Label set the group belongs to.")] string setName,
        [Description("Name of the group to delete.")] string groupName,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new
            {
                Command = "DELETELABELGROUP",
                Data    = JsonSerializer.Serialize(new { gameId, setName, groupName }),
            },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "group_add_member")]
    [DynamicDescription("group_add_member_desc")]
    [Description("Add a label to a label group. The label is identified by its address and must already exist in the same label set. Use 'role' to mark how the label is used (e.g. 'Field', 'Method', 'Constructor', 'Entry').")]
    public async Task<string> AddLabelGroupMember(
        [Description("Game identifier / ISO serial (e.g. 'SCUS-97130').")] string gameId,
        [Description("Label set the group and label belong to.")] string setName,
        [Description("Name of the group to add the label to.")] string groupName,
        [Description("Virtual address of the label to add (hex or decimal).")] string labelAddress,
        [Description("Optional role: 'Field', 'Method', 'Constructor', 'Entry', etc.")] string? role = null,
        [Description("Optional ordering hint within the group (e.g. vtable slot index).")] int? sortOrder = null,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new
            {
                Command = "ADDLABELGROUPMEMBER",
                Data    = JsonSerializer.Serialize(new { gameId, setName, groupName, labelAddress, role, sortOrder }),
            },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "group_remove_member")]
    [DynamicDescription("group_remove_member_desc")]
    [Description("Remove a label from a label group. The label itself is not deleted.")]
    public async Task<string> RemoveLabelGroupMember(
        [Description("Game identifier / ISO serial (e.g. 'SCUS-97130').")] string gameId,
        [Description("Label set the group belongs to.")] string setName,
        [Description("Name of the group to remove the label from.")] string groupName,
        [Description("Virtual address of the label to remove (hex or decimal).")] string labelAddress,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new
            {
                Command = "DELETELABELGROUPMEMBER",
                Data    = JsonSerializer.Serialize(new { gameId, setName, groupName, labelAddress }),
            },
            cancellationToken);
        return SimpleMessage(json);
    }

    // -------------------------------------------------------------------------
    // Call graph
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "callgraph_scan")]
    [DynamicDescription("callgraph_scan_desc")]
    [Description("Scan a range of code memory for JAL (direct) and JALR (indirect) call instructions and persist the discovered caller→callee edges. Run this once per game before using callgraph_query. Requires a live memory backend.")]
    public async Task<string> ScanCallGraph(
        [Description("Game identifier / ISO serial (e.g. 'SCUS-97130'). Omit for file-only sessions.")] string? gameId,
        [Description("Start address of the code segment to scan (hex or decimal).")] string address,
        [Description("Number of bytes to scan from the start address.")] int length,
        [Description("Memory region: 'ee' (default) or 'iop'.")] string? region = null,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new
            {
                Command = "SCANCALLGRAPH",
                Data    = JsonSerializer.Serialize(new { gameId, address, length, region }),
            },
            cancellationToken);
        return ExtractGroupData(json);
    }

    [McpServerTool(Name = "callgraph_query")]
    [DynamicDescription("callgraph_query_desc")]
    [Description("Return the call-graph subgraph around a focus function: its callers, its callees, or both, up to a given depth. Use direction 'callers' to trace the call chain back to a hook. Requires a prior callgraph_scan.")]
    public async Task<string> QueryCallGraph(
        [Description("Game identifier / ISO serial (e.g. 'SCUS-97130'). Omit for file-only sessions.")] string? gameId,
        [Description("Focus function address (hex or decimal).")] string address,
        [Description("Maximum hops from the focus (1–12, default 3).")] int? depth = null,
        [Description("Traversal direction: 'callers', 'callees', or 'both' (default).")] string? direction = null,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new
            {
                Command = "GETCALLGRAPH",
                Data    = JsonSerializer.Serialize(new { gameId, address, depth, direction }),
            },
            cancellationToken);
        return ExtractGroupData(json);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Extracts the JSON payload that the Structure-Visualizer commands return in
    /// the response <c>Data</c> field, or surfaces the <c>Error</c> message.
    /// </summary>
    private static string ExtractGroupData(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.String)
                return $"Error: {err.GetString()}";

            if (root.TryGetProperty("Data", out var data) && data.ValueKind == JsonValueKind.String)
                return WithRules(data.GetString() ?? json);

            return SimpleMessage(json);
        }
        catch
        {
            return json;
        }
    }
}
