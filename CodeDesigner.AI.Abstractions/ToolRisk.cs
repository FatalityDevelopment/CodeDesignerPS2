namespace CodeDesigner.AI.Abstractions;

/// <summary>
/// Side-effect class of a tool, used to gate tools by profile and by the
/// <c>--readonly</c> / <c>--dangerous-tools</c> launch flags. Ordered by severity
/// via <see cref="ToolRiskExtensions.Severity"/>.
/// </summary>
public enum ToolRisk
{
    ReadOnly,
    MutatesDatabase,
    WritesDisk,
    WritesMemory,
    NetworkInjection,
    Dangerous,
}

public static class ToolRiskExtensions
{
    /// <summary>Numeric severity (higher = more dangerous) for profile max-risk caps.</summary>
    public static int Severity(this ToolRisk risk) => risk switch
    {
        ToolRisk.ReadOnly         => 0,
        ToolRisk.MutatesDatabase  => 1,
        ToolRisk.WritesDisk       => 2,
        ToolRisk.WritesMemory     => 3,
        ToolRisk.NetworkInjection => 4,
        ToolRisk.Dangerous        => 5,
        _                         => 5,
    };
}
