namespace CodeDesigner.Ps2.Database.Entities;

/// <summary>
/// Identifies which phase of an agent session a task belongs to.
/// </summary>
public enum McpAgentTaskPhase
{
    /// <summary>Runs once at the beginning of a session.</summary>
    SessionBegin    = 0,

    /// <summary>Runs continuously throughout a session.</summary>
    Continuous      = 1,

    /// <summary>Runs once at the end of a session.</summary>
    SessionEnd      = 2,
}
