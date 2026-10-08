using CodeDesigner.Ps2.Database.Entities;

namespace CodeDesigner.Ps2.DiscordBot;

/// <summary>
/// Reads bot configuration and new AI activity, and stores the bot's cursor.
/// Implementations read from the database the Bridge writes activity into.
/// </summary>
public interface IAiActivityProvider
{
    Task<DiscordBotOptions> GetOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Id of the newest activity row, or 0 when the log is empty.</summary>
    Task<long> GetLatestIdAsync(CancellationToken cancellationToken = default);

    /// <summary>Up to <paramref name="take"/> activity rows with Id greater than <paramref name="afterId"/>, oldest first.</summary>
    Task<IReadOnlyList<AiActivityLog>> GetActivityAfterAsync(long afterId, int take, CancellationToken cancellationToken = default);

    /// <summary>Id of the last activity row already posted, or null when the bot has never run.</summary>
    Task<long?> GetLastPostedIdAsync(CancellationToken cancellationToken = default);

    Task SetLastPostedIdAsync(long id, CancellationToken cancellationToken = default);
}
