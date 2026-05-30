namespace CodeDesigner.AI.Mcp;

/// <summary>
/// The identity of the agent this server process serves, set from launch args
/// <c>--agent-id &lt;n&gt;</c> (and optionally <c>--parent-session &lt;n&gt;</c> when
/// running as a sub-agent). When an agent id is present, <c>cd_session_init</c>
/// auto-surfaces that persona's SessionBegin / ConcurrentTasks onboarding without
/// the model having to know or pass its own id. A DI singleton.
/// </summary>
public sealed class AgentIdentity
{
    public AgentIdentity(int? agentId, int? parentSessionId = null)
    {
        AgentId = agentId;
        ParentSessionId = parentSessionId;
    }

    /// <summary>The agent persona id this process serves, or null for an unscoped session.</summary>
    public int? AgentId { get; }

    /// <summary>Parent coordination-session id when launched as a sub-agent, else null.</summary>
    public int? ParentSessionId { get; }

    public static AgentIdentity FromArgs(string[] args)
    {
        int? agentId = null;
        int? parent = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--agent-id" when i + 1 < args.Length && int.TryParse(args[i + 1], out var a):
                    agentId = a; i++; break;
                case "--parent-session" when i + 1 < args.Length && int.TryParse(args[i + 1], out var p):
                    parent = p; i++; break;
            }
        }
        return new AgentIdentity(agentId, parent);
    }
}
