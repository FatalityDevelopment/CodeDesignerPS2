using System.ComponentModel;
using CodeDesigner.AI.Abstractions;
using CodeDesigner.AI.Mcp;

namespace CodeDesigner.AI.Tools.Core;

[CdTool("cd_enable_domain", ToolDomain.Core, ToolRisk.ReadOnly,
    "Expose another tool domain at runtime without relaunching (memory|debug|labels|network|patching|cheats|pcsx2|pad|media|admin). Honours the launch risk cap. Risk: changes the available tool list.")]
public sealed class EnableDomainTool(RuntimeDomainGate gate)
{
    [ToolEntryPoint]
    public Task<string> InvokeAsync(
        [Description("Domain name, e.g. 'patching'. Case-insensitive.")] string domain,
        CancellationToken ct = default)
    {
        var valid = string.Join(", ", Enum.GetNames<ToolDomain>().Where(n => n != nameof(ToolDomain.Core)));

        if (!Enum.TryParse<ToolDomain>(domain, ignoreCase: true, out var parsed))
            return Task.FromResult(CdText.Error($"unknown domain '{domain}'. Valid: {valid}."));
        if (parsed == ToolDomain.Core)
            return Task.FromResult("Domain 'Core' is always enabled.");

        var added = gate.EnableDomain(parsed);
        if (added.Count == 0)
            return Task.FromResult($"No new tools for '{parsed}' — already enabled, or none fit the launch risk cap.");

        return Task.FromResult(
            $"Enabled '{parsed}' (+{added.Count} tools): {string.Join(", ", added)}\n" +
            "A tools/list_changed notification was sent — refresh your tool list to use them.");
    }
}

[CdTool("cd_list_domains", ToolDomain.Core, ToolRisk.ReadOnly,
    "List tool domains and whether each is currently enabled (launch profile + any widened at runtime). Risk: read-only.")]
public sealed class ListDomainsTool(RuntimeDomainGate gate)
{
    [ToolEntryPoint]
    public Task<string> InvokeAsync(CancellationToken ct = default)
    {
        var enabled = gate.Enabled;
        var lines = Enum.GetValues<ToolDomain>()
            .OrderBy(d => d.ToString())
            .Select(d => $"  [{(enabled.Contains(d) ? "x" : " ")}] {d}");
        return Task.FromResult("Domains (enable more with cd_enable_domain):\n" + string.Join("\n", lines));
    }
}
