using CodeDesigner.AI.Abstractions;
using CodeDesigner.Ps2.Database;
using Microsoft.EntityFrameworkCore;

namespace CodeDesigner.AI.Context;

/// <summary>
/// EF-backed <see cref="IOrchestrationCoordinator"/>. Reads the single active
/// <see cref="CodeDesigner.Ps2.Database.Entities.McpOrchestration"/> and parses its
/// <c>Workflow</c> Markdown into ordered steps. Uses short-lived
/// <see cref="CodeDesignerDbContext"/> instances per call (same pattern as
/// <see cref="DbAgentCoordinator"/>), so it works on the EF-direct path without the
/// socket bridge / emulator running.
/// </summary>
internal sealed class DbOrchestrationCoordinator(DbContextOptions<CodeDesignerDbContext> dbOptions) : IOrchestrationCoordinator
{
    public async Task<OrchestrationInfo?> GetActiveAsync(CancellationToken ct = default)
    {
        await using var db = new CodeDesignerDbContext(dbOptions);
        var o = await db.McpOrchestrations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsActive, ct);
        return o is null ? null : Map(o.Id, o.Name, o.Workflow, o.CurrentStage, o.CurrentStageSummary, o.StageUpdatedAt);
    }

    public async Task<OrchestrationInfo?> AdvanceAsync(string stage, string? summary, CancellationToken ct = default)
    {
        await using var db = new CodeDesignerDbContext(dbOptions);
        var o = await db.McpOrchestrations.FirstOrDefaultAsync(x => x.IsActive, ct);
        if (o is null) return null;

        o.CurrentStage = stage;
        o.CurrentStageSummary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();
        o.StageUpdatedAt = DateTime.UtcNow;
        o.UpdatedAt = o.StageUpdatedAt.Value;
        await db.SaveChangesAsync(ct);

        return Map(o.Id, o.Name, o.Workflow, o.CurrentStage, o.CurrentStageSummary, o.StageUpdatedAt);
    }

    private static OrchestrationInfo Map(
        int id, string name, string? workflow, string? stage, string? summary, DateTime? updatedAt)
        => new(id, name, workflow, stage, summary, updatedAt, WorkflowParser.Parse(workflow));
}
