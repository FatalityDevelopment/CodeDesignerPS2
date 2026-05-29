using System.Text;
using CodeDesigner.AI.Abstractions;
using CodeDesigner.Ps2.McpServer;

namespace CodeDesigner.AI.Context;

/// <summary>
/// <see cref="ICdContextProvider"/> backed by the legacy <see cref="McpDatabaseService"/>
/// (reused via linked source). Supplies lazy context so cd_session_init stays compact.
/// </summary>
internal sealed class DbContextProvider(McpDatabaseService db) : ICdContextProvider
{
    public Task<IReadOnlyList<string>> ListProgressAsync(CancellationToken ct = default)
        => db.ListMemoriesAsync(ct);

    public async Task<IReadOnlyList<string>> SearchProgressAsync(string query, CancellationToken ct = default)
    {
        var all = await db.ListMemoriesAsync(ct);
        if (string.IsNullOrWhiteSpace(query)) return all;
        return all.Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<string> GetProgressAsync(string name, CancellationToken ct = default)
        => await db.GetMemoryAsync(name, ct) ?? $"(no progress report named '{name}')";

    public Task SaveProgressAsync(string name, string content, CancellationToken ct = default)
        => db.SaveMemoryAsync(name, content, ct);

    public async Task<string> GetSessionSummaryAsync(string? gameId, CancellationToken ct = default)
    {
        var reports = await db.ListMemoriesAsync(ct);
        return $"Game: {gameId ?? "(unset)"}; {reports.Count} progress report(s).";
    }

    public async Task<string> GetAsync(string kind, string? id, CancellationToken ct = default)
        => (kind ?? "").ToLowerInvariant() switch
        {
            "progress_report"    => await GetProgressAsync(id ?? "", ct),
            "agent_profile"      => await AgentAsync(id, ct),
            "orchestration_plan" => await OrchestrationAsync(id, ct),
            "db_schema"          => CdDocs.DbSchema,
            "packet_workflow"    => CdDocs.PacketWorkflow,
            "rules"              => CdDocs.Rules,
            _ => $"(unknown context kind '{kind}'. Try: agent_profile, orchestration_plan, progress_report, db_schema, packet_workflow, rules.)",
        };

    private async Task<string> AgentAsync(string? id, CancellationToken ct)
    {
        var agents = await db.GetAllAgentsAsync(ct);
        var agent = int.TryParse(id, out var aid)
            ? agents.FirstOrDefault(a => a.Id == aid)
            : agents.FirstOrDefault(a => a.IsAgentLead);
        if (agent is null) return $"(no agent profile for id '{id}')";

        var sb = new StringBuilder();
        sb.AppendLine($"# Agent {agent.Id}: {agent.Name}{(agent.IsAgentLead ? " [LEAD]" : "")}");
        if (!string.IsNullOrWhiteSpace(agent.Objective))   sb.AppendLine($"Objective: {agent.Objective}");
        if (!string.IsNullOrWhiteSpace(agent.Description))  sb.AppendLine(agent.Description);
        if (!string.IsNullOrWhiteSpace(agent.Rules))        { sb.AppendLine("## Rules"); sb.AppendLine(agent.Rules); }
        if (!string.IsNullOrWhiteSpace(agent.Guidelines))   { sb.AppendLine("## Guidelines"); sb.AppendLine(agent.Guidelines); }
        return sb.ToString().TrimEnd();
    }

    private async Task<string> OrchestrationAsync(string? id, CancellationToken ct)
    {
        var plans = await db.GetAllOrchestrationsAsync(ct);
        var plan = int.TryParse(id, out var pid)
            ? plans.FirstOrDefault(o => o.Id == pid)
            : plans.FirstOrDefault();
        if (plan is null) return $"(no orchestration plan for id '{id}')";

        var sb = new StringBuilder();
        sb.AppendLine($"# Orchestration {plan.Id}: {plan.Name}");
        if (!string.IsNullOrWhiteSpace(plan.Description)) sb.AppendLine(plan.Description);
        if (!string.IsNullOrWhiteSpace(plan.Strategy))    { sb.AppendLine("## Strategy"); sb.AppendLine(plan.Strategy); }
        if (!string.IsNullOrWhiteSpace(plan.Roles))       { sb.AppendLine("## Roles"); sb.AppendLine(plan.Roles); }
        if (!string.IsNullOrWhiteSpace(plan.Workflow))    { sb.AppendLine("## Workflow"); sb.AppendLine(plan.Workflow); }
        if (!string.IsNullOrWhiteSpace(plan.Rules))       { sb.AppendLine("## Rules"); sb.AppendLine(plan.Rules); }
        return sb.ToString().TrimEnd();
    }
}
