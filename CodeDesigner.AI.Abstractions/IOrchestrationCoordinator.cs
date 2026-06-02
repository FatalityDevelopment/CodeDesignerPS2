using System.Text.RegularExpressions;

namespace CodeDesigner.AI.Abstractions;

/// <summary>One parsed step of an orchestration <c>Workflow</c>.</summary>
/// <param name="Number">1-based ordinal of the step within the workflow.</param>
/// <param name="Text">The step's text (heading / list-item content, trimmed).</param>
public sealed record OrchestrationStep(int Number, string Text);

/// <summary>
/// The active orchestration plan with its <c>Workflow</c> Markdown parsed into
/// ordered <see cref="Steps"/>, plus the live stage tracked via <c>cd_orch_advance</c>.
/// </summary>
public sealed record OrchestrationInfo(
    int Id,
    string Name,
    string? Workflow,
    string? CurrentStage,
    string? CurrentStageSummary,
    DateTime? StageUpdatedAt,
    IReadOnlyList<OrchestrationStep> Steps)
{
    /// <summary>The step matching <see cref="CurrentStage"/> (by number then text), or null when not started / unresolved.</summary>
    public OrchestrationStep? CurrentStep => WorkflowParser.ResolveCurrent(Steps, CurrentStage);

    /// <summary>The step immediately after <see cref="CurrentStep"/>, or the first step when not started. Null at the end.</summary>
    public OrchestrationStep? NextStep
    {
        get
        {
            if (Steps.Count == 0) return null;
            var cur = CurrentStep;
            if (cur is null) return Steps[0];
            for (int i = 0; i < Steps.Count; i++)
                if (Steps[i].Number == cur.Number)
                    return i + 1 < Steps.Count ? Steps[i + 1] : null;
            return null;
        }
    }
}

/// <summary>
/// Parses an orchestration <c>Workflow</c> (free-form Markdown) into ordered steps
/// and resolves a stored stage label back to a concrete step. Pure functions — no
/// I/O — so they unit-test cleanly and can run on either DB-access path.
/// </summary>
public static class WorkflowParser
{
    // "1. text", "2) text", "Step 3: text"  (captures the explicit number)
    private static readonly Regex NumberedRx = new(
        @"^\s*(?:step\s+)?(\d+)\s*[.):]\s+(.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "# text" … "###### text"
    private static readonly Regex HeadingRx = new(
        @"^\s*#{1,6}\s+(.+?)\s*$", RegexOptions.Compiled);

    // "- text", "* text", "+ text"
    private static readonly Regex BulletRx = new(
        @"^\s*[-*+]\s+(.+?)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Parse the workflow into ordered steps. Precedence: explicit numbered list →
    /// Markdown headings → bullet list → non-blank lines. Returns empty when blank.
    /// </summary>
    public static IReadOnlyList<OrchestrationStep> Parse(string? workflow)
    {
        if (string.IsNullOrWhiteSpace(workflow)) return Array.Empty<OrchestrationStep>();
        var lines = workflow.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var numbered = new List<OrchestrationStep>();
        var headings = new List<string>();
        var bullets = new List<string>();
        var nonBlank = new List<string>();

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Trim().Length == 0) continue;
            nonBlank.Add(line.Trim());

            var m = NumberedRx.Match(line);
            if (m.Success)
            {
                numbered.Add(new OrchestrationStep(int.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim()));
                continue;
            }
            var h = HeadingRx.Match(line);
            if (h.Success) { headings.Add(h.Groups[1].Value.Trim()); continue; }
            var b = BulletRx.Match(line);
            if (b.Success) { bullets.Add(b.Groups[1].Value.Trim()); }
        }

        if (numbered.Count > 0) return numbered;
        if (headings.Count > 0) return Number(headings);
        if (bullets.Count > 0) return Number(bullets);
        return Number(nonBlank);
    }

    /// <summary>
    /// Resolve a stored stage label to a concrete step: exact number → exact text →
    /// substring (either direction). Null when there are no steps or no match.
    /// </summary>
    public static OrchestrationStep? ResolveCurrent(IReadOnlyList<OrchestrationStep> steps, string? currentStage)
    {
        if (steps.Count == 0 || string.IsNullOrWhiteSpace(currentStage)) return null;
        var stage = currentStage.Trim();

        if (int.TryParse(stage, out var n))
        {
            var byNum = steps.FirstOrDefault(s => s.Number == n);
            if (byNum is not null) return byNum;
        }

        var exact = steps.FirstOrDefault(s => string.Equals(s.Text, stage, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        // Substring either direction: a short stage label inside a longer step text
        // (e.g. "Patch" → "Patch with CDS…"), or a longer stage that embeds a step's
        // text. The reverse direction requires a non-trivial step text so single
        // words/chars (e.g. "a") don't greedily match arbitrary stage strings.
        return steps.FirstOrDefault(s =>
            s.Text.Contains(stage, StringComparison.OrdinalIgnoreCase) ||
            (s.Text.Length >= 4 && stage.Contains(s.Text, StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyList<OrchestrationStep> Number(IReadOnlyList<string> texts)
    {
        var list = new List<OrchestrationStep>(texts.Count);
        for (int i = 0; i < texts.Count; i++) list.Add(new OrchestrationStep(i + 1, texts[i]));
        return list;
    }
}

/// <summary>
/// Read/advance the live stage of the currently active <c>McpOrchestration</c>.
/// DB-backed (EF-direct) so every IDE-spawned agent process shares one source of
/// truth for "where is the team in the workflow". Stage changes can be broadcast
/// to the agent message bus by the calling tool.
/// </summary>
public interface IOrchestrationCoordinator
{
    /// <summary>The active orchestration with its workflow parsed into steps, or null when none is active.</summary>
    Task<OrchestrationInfo?> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Set the active orchestration's current stage (and optional one-line summary),
    /// stamping <c>StageUpdatedAt</c>. Returns the refreshed info, or null when no plan is active.
    /// </summary>
    Task<OrchestrationInfo?> AdvanceAsync(string stage, string? summary, CancellationToken cancellationToken = default);
}
