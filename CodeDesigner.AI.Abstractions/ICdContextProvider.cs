namespace CodeDesigner.AI.Abstractions;

/// <summary>
/// Supplies lazy, on-demand context (agent rules, orchestration plans, DB schema,
/// workflow docs, progress reports) so the compact <c>cd_session_init</c> never has
/// to front-load thousands of tokens. The Server implements this over the DB.
/// </summary>
public interface ICdContextProvider
{
    /// <summary>
    /// Fetch a single context document by kind. Recognized kinds include:
    /// <c>agent_profile</c>, <c>orchestration_plan</c>, <c>progress_report</c>,
    /// <c>db_schema</c>, and named workflow docs (e.g. <c>packet_workflow</c>).
    /// </summary>
    Task<string> GetAsync(string kind, string? id, CancellationToken cancellationToken = default);

    /// <summary>Names of saved progress reports (for resume).</summary>
    Task<IReadOnlyList<string>> ListProgressAsync(CancellationToken cancellationToken = default);

    /// <summary>Search progress reports by free text; returns matching names.</summary>
    Task<IReadOnlyList<string>> SearchProgressAsync(string query, CancellationToken cancellationToken = default);

    Task<string> GetProgressAsync(string name, CancellationToken cancellationToken = default);

    Task SaveProgressAsync(string name, string content, CancellationToken cancellationToken = default);

    /// <summary>Short summary used by cd_session_init (game, active reports, core rules).</summary>
    Task<string> GetSessionSummaryAsync(string? gameId, CancellationToken cancellationToken = default);
}
