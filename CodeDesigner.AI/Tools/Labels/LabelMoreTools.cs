using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Labels;

// Additional label tools beyond Core's label_list/label_add: label sets, IOP labels,
// pointer/struct definitions, resolve helpers, label deletion, function filter.
// Commands match legacy CodeDesigner.Ps2.McpServer/Tools/Labels.cs.

// ── Label sets ─────────────────────────────────────────────────────────
[CdTool("label_list_sets", ToolDomain.Labels, ToolRisk.ReadOnly,
    "List all label sets for a game (including the built-in set). Risk: read-only.")]
public sealed class LabelListSetsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("PS2 disc serial (e.g. 'SCUS-97275'). MUST be the SCUS/SLUS/SLES/... serial — not an ISO path, filename, or numeric ID.")] string gameId,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "GETLABELSETS", GameId = gameId }, ct));
}

[CdTool("label_create_set", ToolDomain.Labels, ToolRisk.MutatesDatabase,
    "Create a new, empty label set. Risk: writes database.")]
public sealed class LabelCreateSetTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("PS2 disc serial (e.g. 'SCUS-97275'). MUST be the SCUS/SLUS/SLES/... serial — not an ISO path, filename, or numeric ID.")] string gameId,
        [Description("New label set name (e.g. 'ai-analysis').")] string setName,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "CREATELABELSET", GameId = gameId, SetName = setName }, ct));
}

[CdTool("label_set_active", ToolDomain.Labels, ToolRisk.MutatesDatabase,
    "Mark a label set as active (merged with built-in for lookups). Risk: writes database.")]
public sealed class LabelSetActiveTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("PS2 disc serial (e.g. 'SCUS-97275'). MUST be the SCUS/SLUS/SLES/... serial — not an ISO path, filename, or numeric ID.")] string gameId,
        [Description("Label set name to activate (use 'Built-in' to clear).")] string setName,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "SETACTIVELABELSET", GameId = gameId, SetName = setName }, ct));
}

[CdTool("label_delete_set", ToolDomain.Labels, ToolRisk.MutatesDatabase,
    "Permanently delete a label set and all its entries (irreversible). Risk: writes database.")]
public sealed class LabelDeleteSetTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("PS2 disc serial (e.g. 'SCUS-97275'). MUST be the SCUS/SLUS/SLES/... serial — not an ISO path, filename, or numeric ID.")] string gameId,
        [Description("Label set name to delete.")] string setName,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "DELETELABELSET", GameId = gameId, SetName = setName }, ct));
}

// ── IOP module labels ──────────────────────────────────────────────────
[CdTool("iop_labels_refresh", ToolDomain.Labels, ToolRisk.MutatesDatabase,
    "Refresh every 'IOP: <module>' label set from loaded IOP modules (PCSX2 PINE mode). Idempotent. Risk: writes database.")]
public sealed class IopLabelsRefreshTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("PS2 disc serial (e.g. 'SCUS-97275'). MUST be the SCUS/SLUS/SLES/... serial — not an ISO path, filename, or numeric ID.")] string gameId, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "IOPLABELSREFRESH", GameId = gameId }, ct));
}

[CdTool("iop_labels_refresh_module", ToolDomain.Labels, ToolRisk.MutatesDatabase,
    "Refresh the 'IOP: <module>' label set for a single IOP module. PINE mode required. Risk: writes database.")]
public sealed class IopLabelsRefreshModuleTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("PS2 disc serial (e.g. 'SCUS-97275'). MUST be the SCUS/SLUS/SLES/... serial — not an ISO path, filename, or numeric ID.")] string gameId,
        [Description("IOP module name (e.g. 'secrman').")] string moduleName,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "IOPLABELSREFRESHMODULE", GameId = gameId, SetName = moduleName }, ct));
}

// ── Label list/delete ──────────────────────────────────────────────────
[CdTool("label_list_functions", ToolDomain.Labels, ToolRisk.ReadOnly,
    "List only Function-typed labels (FNC_ prefix) from the active label set. Risk: read-only.")]
public sealed class LabelListFunctionsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdFormat.LabelsFiltered(
            await bridge.SendAsync(new { Command = "GETLABELS" }, ct),
            name => name.StartsWith("FNC_", StringComparison.Ordinal));
}

[CdTool("label_delete", ToolDomain.Labels, ToolRisk.MutatesDatabase,
    "Remove a label entry from a label set. Risk: writes database.")]
public sealed class LabelDeleteTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("PS2 disc serial (e.g. 'SCUS-97275'). MUST be the SCUS/SLUS/SLES/... serial — not an ISO path, filename, or numeric ID.")] string gameId,
        [Description("Label set name.")] string labelSet,
        [Description("Virtual address (hex/decimal).")] string address,
        CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "DELETELABELENTRY", GameId = gameId, SetName = labelSet, Address = a.Value }, ct));
    }
}

// ── Resolve helpers ────────────────────────────────────────────────────
[CdTool("label_resolve_strings", ToolDomain.Labels, ToolRisk.MutatesDatabase,
    "Read live PCSX2 memory for each String-typed label and update with the null-terminated ASCII found there. Risk: writes database.")]
public sealed class LabelResolveStringsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "RESOLVESTRINGS" }, ct));
}

[CdTool("label_resolve_linked_list", ToolDomain.Labels, ToolRisk.ReadOnly,
    "Walk a linked list in live PCSX2 memory from a head, using a per-node next-offset. Risk: read-only.")]
public sealed class LabelResolveLinkedListTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Virtual address of the first node (hex/decimal).")] string address,
        [Description("Byte offset within each node of the next-node pointer.")] int nextOffset,
        CancellationToken ct = default)
    {
        var a = CdText.TryParseAddress(address);
        if (a is null) return CdText.Error($"invalid address '{address}'.");
        return CdText.Message(await bridge.SendAsync(new { Command = "RESOLVELINKEDLIST", Address = a.Value, MemberOffset = nextOffset }, ct));
    }
}

// ── Pointer / struct definitions ───────────────────────────────────────
[CdTool("label_get_pointers", ToolDomain.Labels, ToolRisk.ReadOnly,
    "List struct definitions (a named base address + its member fields). 'Pointer' = struct here. Risk: read-only.")]
public sealed class LabelGetPointersTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "GETSTRUCTS" }, ct));
}

[CdTool("label_add_pointer", ToolDomain.Labels, ToolRisk.MutatesDatabase,
    "Define a struct: a named base address whose fields you then map with label_add_pointer_member. Risk: writes database.")]
public sealed class LabelAddPointerTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Unique pointer name (e.g. 'PlayerState').")] string pointerName,
        [Description("Base address of the pointer variable in EE memory (hex/decimal).")] string baseAddress,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "ADDSTRUCT", StructName = pointerName, StructBaseAddress = baseAddress }, ct));
}

[CdTool("label_delete_pointer", ToolDomain.Labels, ToolRisk.MutatesDatabase,
    "Delete a struct definition by name. Risk: writes database.")]
public sealed class LabelDeletePointerTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Pointer name to delete.")] string pointerName, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "DELETESTRUCT", StructName = pointerName }, ct));
}

[CdTool("label_add_pointer_member", ToolDomain.Labels, ToolRisk.MutatesDatabase,
    "Add/update a struct field: a named member at a byte offset from the (dereferenced) base. Risk: writes database.")]
public sealed class LabelAddPointerMemberTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Pointer name.")] string pointerName,
        [Description("Byte offset of the field from the dereferenced base.")] int memberOffset,
        [Description("Field name (e.g. 'health').")] string memberName,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "ADDSTRUCTMEMBER", StructName = pointerName, MemberOffset = memberOffset, MemberName = memberName }, ct));
}
