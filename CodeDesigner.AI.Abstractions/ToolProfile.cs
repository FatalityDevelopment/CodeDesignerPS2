namespace CodeDesigner.AI.Abstractions;

/// <summary>
/// A named set of allowed tool domains plus a maximum risk severity. The server
/// loads only the tools a profile allows, so the model never sees hundreds of
/// tools at once. <see cref="ToolDomain.Core"/> is allowed by every profile.
/// </summary>
public sealed class ToolProfile
{
    public ToolProfile(string name, IEnumerable<ToolDomain> domains, ToolRisk maxRisk)
    {
        Name = name;
        Domains = new HashSet<ToolDomain>(domains) { ToolDomain.Core };
        MaxRisk = maxRisk;
    }

    public string Name { get; }
    public IReadOnlySet<ToolDomain> Domains { get; }
    public ToolRisk MaxRisk { get; private set; }

    /// <summary>True when a tool of the given domain + risk should be exposed.</summary>
    public bool Allows(ToolDomain domain, ToolRisk risk) =>
        Domains.Contains(domain) && risk.Severity() <= MaxRisk.Severity();

    // ── Known profiles ──────────────────────────────────────────────────────
    public static readonly IReadOnlyDictionary<string, ToolProfile> Known =
        new Dictionary<string, ToolProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["core"]     = new("core",     Array.Empty<ToolDomain>(),                                                                                                       ToolRisk.MutatesDatabase),
            ["re"]       = new("re",       new[] { ToolDomain.Memory, ToolDomain.Debug, ToolDomain.Labels, ToolDomain.Pcsx2, ToolDomain.Pad },                              ToolRisk.WritesMemory),
            ["packets"]  = new("packets",  new[] { ToolDomain.Network, ToolDomain.Labels },                                                                                 ToolRisk.NetworkInjection),
            ["patching"] = new("patching", new[] { ToolDomain.Memory, ToolDomain.Debug, ToolDomain.Labels, ToolDomain.Patching, ToolDomain.Cheats, ToolDomain.Pcsx2 },       ToolRisk.WritesMemory),
            ["media"]    = new("media",    new[] { ToolDomain.Media, ToolDomain.Labels },                                                                                   ToolRisk.WritesDisk),
            ["pcsx2"]    = new("pcsx2",    new[] { ToolDomain.Pcsx2, ToolDomain.Memory, ToolDomain.Debug, ToolDomain.Labels, ToolDomain.Pad },                              ToolRisk.WritesMemory),
            ["admin"]    = new("admin",    new[] { ToolDomain.Admin, ToolDomain.Memory, ToolDomain.Debug, ToolDomain.Labels, ToolDomain.Network, ToolDomain.Patching, ToolDomain.Cheats, ToolDomain.Pcsx2, ToolDomain.Pad, ToolDomain.Media }, ToolRisk.Dangerous),
            ["all"]      = new("all",      Enum.GetValues<ToolDomain>(),                                                                                                    ToolRisk.Dangerous),
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
        var effective = new ToolProfile(baseProfile.Name, baseProfile.Domains, baseProfile.MaxRisk);

        if (readOnly)
            effective.MaxRisk = ToolRisk.ReadOnly;
        else if (!allowDangerous && effective.MaxRisk == ToolRisk.Dangerous)
            effective.MaxRisk = ToolRisk.NetworkInjection;

        return effective;
    }
}
