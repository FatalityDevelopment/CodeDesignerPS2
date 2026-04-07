using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

public sealed partial class CodeDesignerPs2Tools
{
    // -------------------------------------------------------------------------
    // Symbol labels
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_labels")]
    [Description("Return all symbol labels (with type badges) from the active label set. Useful for reviewing previously annotated addresses before continuing analysis.")]
    public async Task<string> GetLabels(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETLABELS" }, cancellationToken);
        return FormatLabels(json);
    }

    [McpServerTool(Name = "get_function_labels")]
    [Description("Return only Function-typed labels from the active label set, including named argument and return register annotations for each function.")]
    public async Task<string> GetFunctionLabels(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETLABELS" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var errProp) &&
                errProp.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(errProp.GetString()))
                return $"Error: {errProp.GetString()}";

            if (!root.TryGetProperty("Labels", out var labelsArr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            foreach (var item in labelsArr.EnumerateArray())
            {
                string addr  = item.TryGetProperty("address", out var a) ? (a.GetString() ?? "?") : "?";
                string label = item.TryGetProperty("label",   out var l) ? (l.GetString() ?? "?") : "?";

                // Only include labels that look like functions (FNC_ prefix from sync).
                if (!label.StartsWith("FNC_", StringComparison.Ordinal)) continue;

                string? cmt = item.TryGetProperty("comment", out var c) ? c.GetString() : null;
                sb.Append($"  {addr,-12}  {label}");
                if (!string.IsNullOrEmpty(cmt))
                    sb.Append($"  ; {cmt}");
                sb.AppendLine();
            }

            return WithRules(sb.Length > 0 ? sb.ToString().TrimEnd() : "(no function labels defined)");
        }
        catch { return json; }
    }

    [McpServerTool(Name = "add_label")]
    [Description("Insert or update a label entry in the active label set. Supports all label types: Address, Function, Struct, Array, ArrayOfPointers, LinkedList, String.")]
    public async Task<string> AddLabel(
        [Description("Virtual address to label (hex or decimal).")] string address,
        [Description("Label name (e.g. 'scePadRead').")] string label,
        [Description("Optional comment to attach to the label.")] string? comment = null,
        [Description("Label type: Address (default), Struct, Function, Array, ArrayOfPointers, LinkedList, String.")] string? type = null,
        [Description("For Function: comma-separated argument registers, e.g. 'a0=playerPtr,a1=flags'.")] string? argRegs = null,
        [Description("For Function: comma-separated return registers, e.g. 'v0=result'.")] string? returnRegs = null,
        [Description("For Array: byte stride between elements, e.g. 4.")] int? elementStride = null,
        [Description("For Array: total number of elements (omit if unknown).")] int? elementCount = null,
        [Description("For LinkedList: byte offset within each node where the next-node pointer lives.")] int? nodeNextOffset = null,
        [Description("For LinkedList: total size of one node in bytes (informational).")] int? nodeSize = null,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new
            {
                Command = "ADDLABEL",
                Address = parsedAddress.Value,
                Label   = label,
                Comment = comment,
                LabelType = type,
                ArgRegs      = argRegs?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                ReturnRegs   = returnRegs?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                ElementStride = elementStride,
                ElementCount  = elementCount,
                NodeNextOffset = nodeNextOffset,
                NodeSize      = nodeSize,
            },
            cancellationToken);

        return SimpleMessage(json);
    }

    [McpServerTool(Name = "delete_label")]
    [Description("Remove a symbol label from the active label set. Changes are persisted to disk immediately.")]
    public async Task<string> DeleteLabel(
        [Description("Virtual address of the label to remove (hex or decimal).")] string address,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "DELETELABEL", Address = parsedAddress.Value },
            cancellationToken);

        return SimpleMessage(json);
    }

    [McpServerTool(Name = "resolve_strings")]
    [Description("Read each String-typed label's memory content from live PCSX2 (PINE mode) and update the label value with the null-terminated ASCII string found at that address.")]
    public async Task<string> ResolveStrings(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "RESOLVESTRINGS" }, cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "resolve_linked_list")]
    [Description("Walk a linked list in live PCSX2 memory starting from the given address, using node size and next-pointer offset from the corresponding LinkedList label.")]
    public async Task<string> ResolveLinkedList(
        [Description("Virtual address of the first node (hex or decimal).")] string address,
        [Description("Byte offset within each node of the next-node pointer (e.g. 0 or 4).")] int nextOffset,
        CancellationToken cancellationToken = default)
    {
        uint? parsedAddress = TryParseAddress(address);
        if (parsedAddress is null)
            return $"Error: invalid address '{address}'.";

        var json = await bridge.SendAsync(
            new { Command = "RESOLVELINKEDLIST", Address = parsedAddress.Value, MemberOffset = nextOffset },
            cancellationToken);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.GetProperty("Success").GetBoolean())
                return $"Error: {(root.TryGetProperty("Error", out var e) ? e.GetString() : "RESOLVELINKEDLIST failed")}";

            string msg = root.TryGetProperty("Message", out var m) ? (m.GetString() ?? "") : "";
            string? data = root.TryGetProperty("Data", out var d) ? d.GetString() : null;
            if (data is not null)
            {
                var addrs = JsonSerializer.Deserialize<List<string>>(data) ?? [];
                return $"{msg}\n" + string.Join("\n", addrs.Select((a, i) => $"  [{i}] {a}"));
            }
            return msg;
        }
        catch { return json; }
    }

    // -------------------------------------------------------------------------
    // Struct / memory-map viewer
    // -------------------------------------------------------------------------

    [McpServerTool(Name = "get_structs")]
    [Description("Return all user-defined PS2 memory struct definitions stored on the server. Each struct has a name, base address, and a list of typed member fields.")]
    public async Task<string> GetStructs(CancellationToken cancellationToken)
    {
        var json = await bridge.SendAsync(new { Command = "GETSTRUCTS" }, cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("Error", out var err))
                return $"Error: {err.GetString()}";
            if (!root.TryGetProperty("Structs", out var arr))
                return SimpleMessage(json);

            var sb = new StringBuilder();
            foreach (var item in arr.EnumerateArray())
            {
                string name = item.TryGetProperty("name",        out var n)  ? (n.GetString()  ?? "?") : "?";
                string addr = item.TryGetProperty("baseAddress", out var a)  ? (a.GetString()  ?? "?") : "?";
                sb.AppendLine($"struct {name}  @ {addr}");
                if (item.TryGetProperty("members", out var members))
                {
                    foreach (var m in members.EnumerateArray())
                    {
                        int    offset = m.TryGetProperty("offset", out var o)  ? o.GetInt32()            : 0;
                        string mname  = m.TryGetProperty("name",   out var mn) ? (mn.GetString() ?? "?") : "?";
                        sb.AppendLine($"  +0x{offset:X3}  {mname}");
                    }
                }
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no structs defined)";
        }
        catch { return json; }
    }

    [McpServerTool(Name = "add_struct")]
    [Description("Create or update a named PS2 memory struct definition with a base address. Use add_struct_member to add fields after creating the struct.")]
    public async Task<string> AddStruct(
        [Description("Unique name for this struct (e.g. 'PlayerState').")] string structName,
        [Description("Base address of the struct in EE memory (hex or decimal, e.g. '0x80200000').")] string baseAddress,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "ADDSTRUCT", StructName = structName, StructBaseAddress = baseAddress },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "delete_struct")]
    [Description("Remove a named PS2 memory struct definition. Works in both internal emulator and PINE/PCSX2 modes.")]
    public async Task<string> DeleteStruct(
        [Description("Name of the struct to delete.")] string structName,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "DELETESTRUCT", StructName = structName },
            cancellationToken);
        return SimpleMessage(json);
    }

    [McpServerTool(Name = "add_struct_member")]
    [Description("Add or update a named member field in an existing PS2 memory struct definition. Members have an offset, a data type, and an optional count for array fields.")]
    public async Task<string> AddStructMember(
        [Description("Name of the struct to add the member to.")] string structName,
        [Description("Byte offset of the member within the struct (e.g. 0 for the first field).")] int memberOffset,
        [Description("Display name for the member field (e.g. 'health' or 'posX').")] string memberName,
        CancellationToken cancellationToken = default)
    {
        var json = await bridge.SendAsync(
            new { Command = "ADDSTRUCTMEMBER", StructName = structName, MemberOffset = memberOffset, MemberName = memberName },
            cancellationToken);
        return SimpleMessage(json);
    }
}
