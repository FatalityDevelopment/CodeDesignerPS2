using System.Text;
using CodeDesigner.AI.Abstractions;

namespace CodeDesigner.AI.Mcp;

/// <summary>
/// Thread-safe per-process state tracking session health metrics: when progress
/// was last saved, how many labels have been added, the current declared goal,
/// milestones reached. Injected as a DI singleton; read by cd_check_session_health
/// and written by progress_save / label_add / cd_set_goal / cd_milestone_reached.
/// </summary>
public sealed class SessionState
{
    private readonly object _gate = new();
    private readonly DateTime _started = DateTime.UtcNow;
    private DateTime? _lastSave;
    private DateTime? _lastLabel;
    private long _labelsAdded;
    private long _riskyOps;
    private long _riskyOpsNoReason;
    private string? _goal;
    private readonly List<(DateTime At, string Name, string? Summary)> _milestones = new();
    private readonly List<(DateTime At, string Tool, ToolRisk Risk, string? Reason)> _riskyLog = new();

    public DateTime Started => _started;
    public DateTime? LastSave { get { lock (_gate) return _lastSave; } }
    public DateTime? LastLabelAdded { get { lock (_gate) return _lastLabel; } }
    public long LabelsAdded => Interlocked.Read(ref _labelsAdded);
    public long RiskyOps => Interlocked.Read(ref _riskyOps);
    public long RiskyOpsWithoutReason => Interlocked.Read(ref _riskyOpsNoReason);
    public string? Goal { get { lock (_gate) return _goal; } }

    public IReadOnlyList<(DateTime At, string Name, string? Summary)> RecentMilestones(int take = 5)
    {
        lock (_gate) return _milestones.TakeLast(take).ToArray();
    }

    public int MilestoneCount { get { lock (_gate) return _milestones.Count; } }

    public void RecordSave()                    { lock (_gate) _lastSave = DateTime.UtcNow; }
    public void SetGoal(string text)            { lock (_gate) _goal = text; }
    public void RecordMilestone(string name, string? summary = null)
    {
        lock (_gate) _milestones.Add((DateTime.UtcNow, name, summary));
    }

    public void RecordLabelAdded(int count = 1)
    {
        if (count <= 0) return;
        Interlocked.Add(ref _labelsAdded, count);
        lock (_gate) _lastLabel = DateTime.UtcNow;
    }

    /// <summary>
    /// Log a high-risk tool invocation (WritesDisk and up). Returns the running risky-op
    /// count for this session. <paramref name="reason"/> is the optional one-line
    /// justification the model supplied; null/blank means it ran unjustified.
    /// </summary>
    public long RecordRiskyOp(string tool, ToolRisk risk, string? reason)
    {
        var n = Interlocked.Increment(ref _riskyOps);
        if (string.IsNullOrWhiteSpace(reason))
            Interlocked.Increment(ref _riskyOpsNoReason);
        lock (_gate)
        {
            _riskyLog.Add((DateTime.UtcNow, tool, risk, reason));
            if (_riskyLog.Count > 100) _riskyLog.RemoveAt(0); // cap memory
        }
        return n;
    }

    public IReadOnlyList<(DateTime At, string Tool, ToolRisk Risk, string? Reason)> RecentRiskyOps(int take = 5)
    {
        lock (_gate) return _riskyLog.TakeLast(take).ToArray();
    }

    /// <summary>Render a human-readable health report + recommendations.</summary>
    public string GetHealthReport()
    {
        var now = DateTime.UtcNow;
        var sb = new StringBuilder();
        sb.AppendLine($"Session age:        {(now - _started).TotalMinutes:F1} min");
        sb.AppendLine($"Current goal:       {_goal ?? "(unset — call cd_set_goal)"}");
        sb.AppendLine($"Labels added:       {LabelsAdded}");
        sb.AppendLine($"High-risk ops:      {RiskyOps}{(RiskyOpsWithoutReason > 0 ? $"  ({RiskyOpsWithoutReason} without a stated reason)" : "")}");
        sb.AppendLine($"Last progress save: {(_lastSave is { } s ? $"{(now - s).TotalMinutes:F1} min ago" : "never")}");
        sb.AppendLine($"Last label added:   {(_lastLabel is { } l ? $"{(now - l).TotalMinutes:F1} min ago" : "never")}");

        var recent = RecentMilestones(5);
        if (recent.Count > 0)
        {
            sb.AppendLine($"Milestones ({MilestoneCount}, last {recent.Count} shown):");
            foreach (var (at, n, summary) in recent)
                sb.AppendLine($"  - {at:HH:mm:ss} {n}{(summary is null ? "" : $": {summary}")}");
        }

        // Recommendations
        var recs = new List<string>();
        if (_goal is null)
            recs.Add("cd_set_goal — declare an explicit session goal");
        if (_lastSave is null || (now - _lastSave.Value).TotalMinutes > 30)
            recs.Add("save_progress_report — long since last save");
        if (LabelsAdded == 0 && (now - _started).TotalMinutes > 10)
            recs.Add("label_add — no labels recorded yet this session");
        if (recs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Recommendations:");
            foreach (var r in recs) sb.AppendLine($"  ⚠ {r}");
        }
        return sb.ToString().TrimEnd();
    }
}
