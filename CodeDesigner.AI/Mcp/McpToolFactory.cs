using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace CodeDesigner.AI.Mcp;

/// <summary>
/// Turns a <see cref="ToolDescriptor"/> into an <see cref="McpServerTool"/>: the tool
/// instance is created via DI (so its ctor deps — bridge, context, profile, catalog —
/// are injected), and the MCP name + description come from the <c>[CdTool]</c> metadata.
/// </summary>
internal static class McpToolFactory
{
    public static McpServerTool Create(ToolDescriptor descriptor, IServiceProvider services)
    {
        var instance = ActivatorUtilities.CreateInstance(services, descriptor.ImplType);

        var tool = McpServerTool.Create(
            descriptor.Entry,
            instance,
            new McpServerToolCreateOptions
            {
                Name = descriptor.Meta.Name,
                Description = descriptor.Meta.ShortDescription,
            });

        // High-risk tools (WritesDisk and up) get a soft "reason" nudge + session logging.
        if (RiskFrictionTool.AppliesTo(descriptor.Meta.Risk) &&
            services.GetService<SessionState>() is { } state)
        {
            tool = new RiskFrictionTool(tool, state, descriptor.Meta.Name, descriptor.Meta.Risk);
        }

        return tool;
    }
}
