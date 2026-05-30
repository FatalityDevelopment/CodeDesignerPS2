namespace CodeDesigner.AI.Abstractions;

/// <summary>
/// A named set of allowed tool domains plus a maximum risk severity. The server
/// loads only the tools a profile allows, so the model never sees hundreds of
/// tools at once. <see cref="ToolDomain.Core"/> is allowed by every profile.
/// </summary>
public sealed class ToolProfile
{
    public ToolProfile(string name, IEnumerable<ToolDomain> domains, ToolRisk maxRisk, IEnumerable<string>? allowTools = null)
    {
        Name = name;
        Domains = new HashSet<ToolDomain>(domains) { ToolDomain.Core };
        MaxRisk = maxRisk;
        AllowTools = allowTools is null ? null : new HashSet<string>(allowTools, StringComparer.OrdinalIgnoreCase);
    }

    public string Name { get; }
    public IReadOnlySet<ToolDomain> Domains { get; }
    public ToolRisk MaxRisk { get; private set; }

    /// <summary>
    /// When non-null, the profile is a <em>curated</em> allow-list: a tool is exposed
    /// only if its name is in this set (the risk cap still applies, so
    /// <c>--readonly</c> keeps working). When null, domain + risk gating is used.
    /// </summary>
    public IReadOnlySet<string>? AllowTools { get; }

    /// <summary>True when a tool with the given name + domain + risk should be exposed.</summary>
    public bool Allows(string toolName, ToolDomain domain, ToolRisk risk)
    {
        if (risk.Severity() > MaxRisk.Severity()) return false;          // global risk cap (honours --readonly)
        return AllowTools is not null ? AllowTools.Contains(toolName)    // curated list
                                      : Domains.Contains(domain);        // domain gate
    }

    /// <summary>
    /// Curated minimal set for cheap, cost-sensitive sessions: session hygiene +
    /// read-only inspection + a single save. Used by the <c>lite</c> profile.
    /// </summary>
    private static readonly string[] LiteTools =
    {
        "cd_session_init", "cd_status", "cd_tool_index", "cd_tool_help", "cd_context_get",
        "progress_search", "progress_get", "progress_save",
        "cd_set_goal", "cd_get_goal", "cd_check_session_health",
        "label_list", "memory_read", "memory_disasm", "memory_scan", "get_pc",
    };

    // ── Known profiles ──────────────────────────────────────────────────────
    public static readonly IReadOnlyDictionary<string, ToolProfile> Known =
        new Dictionary<string, ToolProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["core"]        = new("core",        Array.Empty<ToolDomain>(),                                                                                                       ToolRisk.MutatesDatabase),
            ["lite"]        = new("lite",        Array.Empty<ToolDomain>(),                                                                                                       ToolRisk.MutatesDatabase, LiteTools),
            ["re"]          = new("re",          new[] { ToolDomain.Memory, ToolDomain.Debug, ToolDomain.Labels, ToolDomain.Pcsx2, ToolDomain.Pad },                              ToolRisk.WritesMemory),
            ["live-debug"]  = new("live-debug",  new[] { ToolDomain.Memory, ToolDomain.Debug },                                                                                   ToolRisk.WritesMemory),
            ["packets"]     = new("packets",     new[] { ToolDomain.Network, ToolDomain.Labels },                                                                                 ToolRisk.NetworkInjection),
            ["patching"]    = new("patching",    new[] { ToolDomain.Memory, ToolDomain.Debug, ToolDomain.Labels, ToolDomain.Patching, ToolDomain.Cheats, ToolDomain.Pcsx2 },       ToolRisk.WritesMemory),
            ["cheats"]      = new("cheats",      new[] { ToolDomain.Memory, ToolDomain.Patching, ToolDomain.Cheats },                                                             ToolRisk.WritesMemory),
            ["media"]       = new("media",       new[] { ToolDomain.Media, ToolDomain.Labels },                                                                                   ToolRisk.WritesDisk),
            ["pcsx2"]       = new("pcsx2",       new[] { ToolDomain.Pcsx2, ToolDomain.Memory, ToolDomain.Debug, ToolDomain.Labels, ToolDomain.Pad },                              ToolRisk.WritesMemory),
            ["multi-agent"] = new("multi-agent", new[] { ToolDomain.Admin },                                                                                                      ToolRisk.MutatesDatabase),
            ["admin"]       = new("admin",       new[] { ToolDomain.Admin, ToolDomain.Memory, ToolDomain.Debug, ToolDomain.Labels, ToolDomain.Network, ToolDomain.Patching, ToolDomain.Cheats, ToolDomain.Pcsx2, ToolDomain.Pad, ToolDomain.Media }, ToolRisk.Dangerous),
            ["all"]         = new("all",         Enum.GetValues<ToolDomain>(),                                                                                                    ToolRisk.Dangerous),
        };

    public const string DefaultProfile = "core";

    /// <summary>
    /// Builds the active profile from CLI args: <c>--profile &lt;name&gt;</c>
    /// (default <c>core</c>), <c>--readonly</c> (caps risk at ReadOnly), and
    /// <c>--dangerous-tools false</c> (excludes Dangerous-risk tools).
    /// </summary>
    public static ToolProfile FromArgs(string[] args)
    {
        string name = DefaultProfile;
        bool readOnly = false;
        bool allowDangerous = true;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--profile" when i + 1 < args.Length:
                    name = args[++i];
                    break;
                case "--readonly":
                    readOnly = true;
                    break;
                case "--dangerous-tools" when i + 1 < args.Length:
                    allowDangerous = !string.Equals(args[++i], "false", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }

        var baseProfile = Known.TryGetValue(name, out var p) ? p : Known[DefaultProfile];
        var effective = new ToolProfile(baseProfile.Name, baseProfile.Domains, baseProfile.MaxRisk, baseProfile.AllowTools);

        if (readOnly)
            effective.MaxRisk = ToolRisk.ReadOnly;
        else if (!allowDangerous && effective.MaxRisk == ToolRisk.Dangerous)
            effective.MaxRisk = ToolRisk.NetworkInjection;

        return effective;
    }
}
