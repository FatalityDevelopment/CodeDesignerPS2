namespace CodeDesigner.Ps2.DiscordPresence;

/// <summary>
/// Polls a database-backed provider and pushes updates to Discord rich presence.
/// </summary>
public sealed class DiscordPresenceWorker
{
    private readonly IDiscordPresenceDataProvider _provider;
    private readonly DiscordPresenceClient _presenceClient;

    public DiscordPresenceWorker(
        IDiscordPresenceDataProvider provider,
        DiscordPresenceClient presenceClient)
    {
        _provider = provider;
        _presenceClient = presenceClient;
    }

    public async Task RunAsync(TimeSpan pollInterval, CancellationToken cancellationToken = default)
    {
        if (!_presenceClient.Initialize())
        {
            throw new InvalidOperationException("Failed to initialize Discord rich presence client.");
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var context = await _provider.GetCurrentContextAsync(cancellationToken);

            if (context is null)
            {
                _presenceClient.UpdateIdle();
            }
            else
            {
                _presenceClient.Update(context);
            }

            await Task.Delay(pollInterval, cancellationToken);
        }
    }
}
