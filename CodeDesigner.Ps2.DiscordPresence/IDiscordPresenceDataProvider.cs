namespace CodeDesigner.Ps2.DiscordPresence;

/// <summary>
/// Provides data used to build Discord rich presence for game research / debugging.
/// Implementation should read the active game details from the database.
/// </summary>
public interface IDiscordPresenceDataProvider
{
    Task<GameResearchContext?> GetCurrentContextAsync(CancellationToken cancellationToken = default);
}
