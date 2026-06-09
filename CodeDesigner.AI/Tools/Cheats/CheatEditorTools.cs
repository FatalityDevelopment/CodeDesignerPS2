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
    "Read a full cheat list as editor text. Prefer cheat_list_sections/search/get_section first to keep token usage low. Risk: read-only.")]
public sealed class CheatListGetTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Cheat list id (from cheat_list_builds). Prefer scoped section reads for large lists.")] int listId, CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "CHEATEDITOR_GET", CheatListId = listId }, ct));
}

[CdTool("cheat_list_sections", ToolDomain.Cheats, ToolRisk.ReadOnly,
    "List section headers in one cheat list with cheat/code counts. Use this before reading codes to keep token usage low. Risk: read-only.")]
public sealed class CheatListSectionsTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Cheat list id (from cheat_list_builds).")]
        int listId,
        [Description("Optional case-insensitive section-header substring filter.")]
        string? query = null,
        CancellationToken ct = default)
        => CdText.Message(await bridge.SendAsync(new { Command = "CHEATEDITOR_SECTIONS", CheatListId = listId, Query = query }, ct));
}

[CdTool("cheat_list_get_section", ToolDomain.Cheats, ToolRisk.ReadOnly,
    "Read only one section from a cheat list as editor text; optionally filter cheats inside it. Risk: read-only.")]
public sealed class CheatListGetSectionTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Cheat list id (from cheat_list_builds).")]
        int listId,
        [Description("Section header returned by cheat_list_sections, e.g. 'Health' or 'Others'.")]
        string section,
        [Description("Optional case-insensitive filter for cheat labels, descriptions, or code lines inside the section.")]
        string? query = null,
        [Description("Maximum cheats to return from the section. Default 200, capped by the bridge.")]
        int? maxCount = null,
        CancellationToken ct = default)
    {
        var take = maxCount is > 0 ? Math.Min(maxCount.Value, 500) : (int?)null;
        return CdText.Message(await bridge.SendAsync(new
        {
            Command = "CHEATEDITOR_GET_SECTION",
            CheatListId = listId,
            CheatSection = section,
            Query = query,
            MaxCount = take,
        }, ct));
    }
}

[CdTool("cheat_search", ToolDomain.Cheats, ToolRisk.ReadOnly,
    "Search cheat labels, section headers, descriptions, and code text. Returns compact hits; fetch a section afterward for full codes. Risk: read-only.")]
public sealed class CheatSearchTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Search text. Matches labels, sections, descriptions, and code lines. May be omitted when section is provided.")]
        string? query = null,
        [Description("Optional game id from cheat_list_games to scope the search.")]
        int? gameId = null,
        [Description("Optional cheat list id from cheat_list_builds to scope the search.")]
        int? listId = null,
        [Description("Optional exact section header to scope the search, e.g. 'Health'.")]
        string? section = null,
        [Description("Maximum compact hits to return. Default 50, capped by the bridge.")]
        int? maxCount = null,
        CancellationToken ct = default)
    {
        var take = maxCount is > 0 ? Math.Min(maxCount.Value, 500) : (int?)null;
        return CdText.Message(await bridge.SendAsync(new
        {
            Command = "CHEATEDITOR_SEARCH",
            CheatGameId = gameId,
            CheatListId = listId,
            CheatSection = section,
            Query = query,
            MaxCount = take,
        }, ct));
    }
}

[CdTool("cheat_list_save", ToolDomain.Cheats, ToolRisk.MutatesDatabase,
    "Overwrite a cheat list's entries. Data is the editor text returned by cheat_list_get or cheat_list_get_section. Risk: writes database.")]
public sealed class CheatListSaveTool(IEmulatorBridge bridge)
{
    [ToolEntryPoint] public async Task<string> InvokeAsync(
        [Description("Cheat list id to overwrite.")] int listId,
        [Description("Cheat-list editor text to persist, using [--SectionStart:...--], [cheat label], and code lines.")] string data,
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
