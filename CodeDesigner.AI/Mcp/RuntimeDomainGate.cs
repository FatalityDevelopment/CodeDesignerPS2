using CodeDesigner.AI.Abstractions;
using ModelContextProtocol.Server;

namespace CodeDesigner.AI.Mcp;

/// <summary>
/// Lets a running server widen its exposed tool set by enabling an additional
/// <see cref="ToolDomain"/> on top of the launch-time profile. New tools are added
/// to the live <see cref="McpServerPrimitiveCollection{T}"/>, which fires a
/// <c>tools/list_changed</c> notification so the client refreshes. The launch risk
/// cap is always honoured, so <c>--readonly</c> stays safe even after widening.
/// </summary>
public sealed class RuntimeDomainGate
{
    private readonly McpServerPrimitiveCollection<McpServerTool> _tools;
    private readonly IServiceProvider _services;
    private readonly IReadOnlyList<ToolDescriptor> _all;
    private readonly ToolProfile _profile;
    private readonly HashSet<string> _present;   // tool names currently in the collection
    private readonly HashSet<ToolDomain> _enabled;
    private readonly object _lock = new();

    public RuntimeDomainGate(
        McpServerPrimitiveCollection<McpServerTool> tools,
        IServiceProvider services,
        IReadOnlyList<ToolDescriptor> allDescriptors,
        ToolProfile profile)
    {
        _tools = tools;
        _services = services;
        _all = allDescriptors;
        _profile = profile;

        // Seed with whatever the launch profile already exposes.
        var allowed = allDescriptors
            .Where(d => profile.Allows(d.Meta.Name, d.Meta.Domain, d.Meta.Risk))
            .ToList();
        _present = new HashSet<string>(allowed.Select(d => d.Meta.Name), StringComparer.Ordinal);
        _enabled = new HashSet<ToolDomain>(allowed.Select(d => d.Meta.Domain));
    }

    /// <summary>Domains currently exposed (launch profile + any widened at runtime).</summary>
    public IReadOnlySet<ToolDomain> Enabled
    {
        get { lock (_lock) return new HashSet<ToolDomain>(_enabled); }
    }

    /// <summary>
    /// Expose every tool in <paramref name="domain"/> that fits under the launch risk
    /// cap and is not already present. Returns the names of the tools added (empty when
    /// the domain was already fully enabled or nothing fit the risk cap).
    /// </summary>
    public IReadOnlyList<string> EnableDomain(ToolDomain domain)
    {
        lock (_lock)
        {
            var added = new List<string>();
            foreach (var d in _all.Where(d => d.Meta.Domain == domain))
            {
                if (d.Meta.Risk.Severity() > _profile.MaxRisk.Severity()) continue; // honour risk cap
                if (!_present.Add(d.Meta.Name)) continue;                            // already present
                _tools.Add(McpToolFactory.Create(d, _services));                     // fires list_changed
                added.Add(d.Meta.Name);
            }
            _enabled.Add(domain);
            return added;
        }
    }
}
