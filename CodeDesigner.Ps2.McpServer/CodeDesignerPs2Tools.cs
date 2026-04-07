using ModelContextProtocol.Server;

namespace CodeDesigner.Ps2.McpServer.Tools;

/// <summary>
/// MCP tools that expose the CodeDesigner MIPS64 Emotion Engine socket server
/// capabilities to GitHub Copilot. Every tool forwards its request to the
/// socket server running on localhost:8765 and returns a human-readable result.
/// Tools are split across partial class files by category.
/// </summary>
[McpServerToolType]
public sealed partial class CodeDesignerPs2Tools(SocketBridgeClient bridge, McpDatabaseService db)
{
}
