using System.ComponentModel;
using CodeDesigner.AI.Context;
using ModelContextProtocol.Server;

namespace CodeDesigner.AI.Resources;

/// <summary>
/// Passive MCP Resources: large docs (schema, workflows, rules) live here instead of
/// in always-visible tool descriptions, so they cost tokens only when actually read.
/// </summary>
[McpServerResourceType]
public class CdResources
{
    [McpServerResource(UriTemplate = "cd://docs/db-schema", Name = "db_schema", MimeType = "text/markdown")]
    [Description("CodeDesigner database schema (tables/columns) for query building.")]
    public static string DbSchema() => CdDocs.DbSchema;

    [McpServerResource(UriTemplate = "cd://docs/packet-workflow", Name = "packet_workflow", MimeType = "text/markdown")]
    [Description("Step-by-step SOCOM/Medius/DNAS packet reverse-engineering workflow.")]
    public static string PacketWorkflow() => CdDocs.PacketWorkflow;

    [McpServerResource(UriTemplate = "cd://docs/rules", Name = "rules", MimeType = "text/markdown")]
    [Description("CodeDesigner operating rules for AI sessions.")]
    public static string Rules() => CdDocs.Rules;
}
