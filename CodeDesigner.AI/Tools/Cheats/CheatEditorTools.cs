using System.ComponentModel;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Tools.Cheats;

// Persistent cheat-table editor: browse and edit the cheat "builds" (lists) stored
// in the archive database (separate from applying live cheats). Backend:
// CodeDesigner.Ps2.Bridge/Services/CheatEditorService.cs.

[CdTool("cheat_list_games", ToolDomain.Cheats, ToolRisk.ReadOnly,
    "List games that own at least one saved cheat list/build. Risk: read-only.")]
public sealed class CheatListGamesTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "CHEATEDITOR_GAMES" }, ct));
}

[CdTool("cheat_list_builds", ToolDomain.Cheats, ToolRisk.ReadOnly,
    "List the saved cheat lists ('builds') for a game. Risk: read-only.")]
public sealed class CheatListBuildsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Game id (from cheat_list_games).")] int gameId, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "CHEATEDITOR_LISTS", CheatGameId = gameId }, ct));
}

[CdTool("cheat_list_get", ToolDomain.Cheats, ToolRisk.ReadOnly,
    "Read one cheat list's entries (label, section, code lines). Risk: read-only.")]
public sealed class CheatListGetTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Cheat list id (from cheat_list_builds).")] int listId, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "CHEATEDITOR_GET", CheatListId = listId }, ct));
}

[CdTool("cheat_list_save", ToolDomain.Cheats, ToolRisk.MutatesDatabase,
    "Overwrite a cheat list's entries. Data is the list body (same shape cheat_list_get returns). Risk: writes database.")]
public sealed class CheatListSaveTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Cheat list id to overwrite.")] int listId,
        [Description("Cheat-list body to persist (JSON of {label, section, codeLines} entries, as returned by cheat_list_get).")] string data,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "CHEATEDITOR_SAVE", CheatListId = listId, Data = data }, ct));
}

[CdTool("cheat_list_create", ToolDomain.Cheats, ToolRisk.MutatesDatabase,
    "Create a new empty cheat list/build for a game. Risk: writes database.")]
public sealed class CheatListCreateTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Game id to attach the new list to.")] int gameId,
        [Description("Title for the new build (e.g. 'v1.2 enable codes').")] string title,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "CHEATEDITOR_CREATE", CheatGameId = gameId, GameTitle = title }, ct));
}

[CdTool("cheat_list_delete", ToolDomain.Cheats, ToolRisk.MutatesDatabase,
    "Delete a cheat list/build by id. Risk: writes database.")]
public sealed class CheatListDeleteTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Cheat list id to delete.")] int listId, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "CHEATEDITOR_DELETE", CheatListId = listId }, ct));
}

[CdTool("cheat_list_import", ToolDomain.Cheats, ToolRisk.MutatesDatabase,
    "Import a cheat list from a file on disk into the archive database. Risk: writes database.")]
public sealed class CheatListImportTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Absolute path to the cheat file to import.")] string filePath, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "CHEATEDITOR_IMPORT", FilePath = filePath }, ct));
}
