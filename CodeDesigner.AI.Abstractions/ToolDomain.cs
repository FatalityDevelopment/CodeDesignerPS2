namespace CodeDesigner.AI.Abstractions;

/// <summary>
/// Functional domain a tool belongs to. Profiles allow-list domains so the model
/// only ever sees the tools relevant to the current job.
/// </summary>
public enum ToolDomain
{
    /// <summary>Always-on essentials (session, status, tool discovery, context, progress, labels).</summary>
    Core,
    /// <summary>Analysis of bytes the agent supplies (strings, functions, pointers…) and snapshot records.</summary>
    Memory,
    Labels,
    Network,
    Patching,
    Cheats,
    Media,
    /// <summary>Server/admin operations (DB queries, agent/orchestration CRUD, settings). Never in default profiles.</summary>
    Admin,
}
