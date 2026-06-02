namespace CodeDesigner.AI.Abstractions;

/// <summary>
/// Marks a class as a CodeDesigner MCP tool and carries the metadata used to
/// gate it by profile. The single tool method on the class is marked with
/// <see cref="ToolEntryPointAttribute"/>; its parameters (with
/// <see cref="System.ComponentModel.DescriptionAttribute"/>) form the MCP schema.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CdToolAttribute : Attribute
{
    public CdToolAttribute(string name, ToolDomain domain, ToolRisk risk, string shortDescription)
    {
        Name = name;
        Domain = domain;
        Risk = risk;
        ShortDescription = shortDescription;
    }

    /// <summary>MCP tool name (domain-prefixed snake_case, e.g. <c>memory_read</c>).</summary>
    public string Name { get; }

    public ToolDomain Domain { get; }

    public ToolRisk Risk { get; }

    /// <summary>
    /// One-line description following the template:
    /// "[Purpose]. Use when [situation]. Returns [shape]. Risk: ...".
    /// Long docs/examples belong in MCP Resources, not here.
    /// </summary>
    public string ShortDescription { get; }
}

/// <summary>Marks the single invoke method of a <see cref="CdToolAttribute"/> class.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ToolEntryPointAttribute : Attribute
{
}
