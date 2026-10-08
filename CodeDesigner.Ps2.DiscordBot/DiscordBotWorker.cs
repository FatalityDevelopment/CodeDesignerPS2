namespace CodeDesigner.Ps2.DiscordBot;

/// <summary>
/// Polls the AI activity log and posts each new entry to Discord as an embed.
/// </summary>
/// <remarks>
/// Configuration is re-read every poll, so setting the token / channel / webhook
/// in the database takes effect without a restart; the worker idles until then.
/// Progress is tracked with a cursor (the last posted activity Id) stored in the
/// database, so a restart resumes where it left off. On the very first run the
/// cursor starts at the newest row — existing history is not replayed.
/// </remarks>
public sealed class DiscordBotWorker
{
    /// <summary>Rows fetched per poll.</summary>
    private const int BatchSize = 50;

    /// <summary>If the bot falls further behind than this (e.g. it was offline), older rows are skipped.</summary>
    private const int MaxBacklog = 100;

    /// <summary>Back-off after Discord rejects the configuration (bad token, missing channel access, …).</summary>
    private static readonly TimeSpan ConfigErrorBackoff = TimeSpan.FromMinutes(1);

    private readonly IAiActivityProvider _provider;
    private readonly DiscordRestClient _discord;
    private long? _cursor;
    private string? _lastError;

    public DiscordBotWorker(IAiActivityProvider provider, DiscordRestClient discord)
    {
        _provider = provider;
        _discord = discord;
    }

    public async Task RunAsync(TimeSpan pollInterval, CancellationToken cancellationToken = default)
    {
        bool wasConfigured = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            var delay = pollInterval;
            try
            {
                var options = await _provider.GetOptionsAsync(cancellationToken);
                if (options.IsConfigured != wasConfigured)
                {
                    wasConfigured = options.IsConfigured;
                    Console.WriteLine(wasConfigured
                        ? $"[DiscordBot] Posting AI activity via {(options.UsesBotToken ? $"bot to channel {options.ChannelId}" : "webhook")}."
                        : "[DiscordBot] Not configured — set DiscordBot.Token + DiscordBot.ChannelId or DiscordBot.WebhookUrl.");
                }

                if (options.IsConfigured)
                {
                    bool caughtUp = await PostNewActivityAsync(options, cancellationToken);
                    if (!caughtUp) { delay = TimeSpan.Zero; }
                    _lastError = null;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (DiscordConfigurationException ex)
            {
                LogOnce(ex.Message);
                delay = ConfigErrorBackoff;
            }
            catch (Exception ex)
            {
                LogOnce($"Failed to post AI activity: {ex.Message}");
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    /// <summary>Posts one batch of new activity. Returns false when more rows are waiting.</summary>
    private async Task<bool> PostNewActivityAsync(DiscordBotOptions options, CancellationToken cancellationToken)
    {
        long cursor = await GetCursorAsync(cancellationToken);

        long latest = await _provider.GetLatestIdAsync(cancellationToken);
        if (latest < cursor)
        {
            // The log was cleared / the database recreated — restart from the top.
            await AdvanceCursorAsync(latest, cancellationToken);
            return true;
        }

        if (latest - cursor > MaxBacklog)
        {
            long skipTo = latest - MaxBacklog;
            Console.WriteLine($"[DiscordBot] Skipping {skipTo - cursor} older activity entries to catch up.");
            await AdvanceCursorAsync(skipTo, cancellationToken);
            cursor = skipTo;
        }

        var rows = await _provider.GetActivityAfterAsync(cursor, BatchSize, cancellationToken);
        if (rows.Count == 0) { return true; }

        // Group consecutive embeds into as few messages as Discord's limits allow,
        // advancing the cursor after each successful send so a failure resumes
        // exactly at the first unsent entry.
        var pending = new List<DiscordEmbed>();
        int pendingChars = 0;
        int pendingFiles = 0;
        long pendingLastId = cursor;

        foreach (var row in rows)
        {
            if (!options.ShouldPost(row.Kind))
            {
                pendingLastId = row.Id;
                continue;
            }

            var embed = AiActivityMessageFormatter.Format(row);
            int chars = embed.CharCount;
            int files = embed.Attachments.Count;

            if (pending.Count > 0
                && (pending.Count >= DiscordEmbed.MaxEmbedsPerMessage
                    || pendingChars + chars > DiscordEmbed.MaxCharsPerMessage
                    || pendingFiles + files > DiscordEmbed.MaxAttachmentsPerMessage))
            {
                await FlushAsync();
            }

            pending.Add(embed);
            pendingChars += chars;
            pendingFiles += files;
            pendingLastId = row.Id;
        }

        await FlushAsync();
        return rows.Count < BatchSize;

        async Task FlushAsync()
        {
            if (pending.Count > 0)
            {
                await _discord.SendEmbedsAsync(options, pending, cancellationToken);
                pending.Clear();
                pendingChars = 0;
                pendingFiles = 0;
            }

            if (pendingLastId != _cursor)
            {
                await AdvanceCursorAsync(pendingLastId, cancellationToken);
            }
        }
    }

    private async Task<long> GetCursorAsync(CancellationToken cancellationToken)
    {
        if (_cursor is long cached) { return cached; }

        var stored = await _provider.GetLastPostedIdAsync(cancellationToken);
        if (stored is long id)
        {
            _cursor = id;
            return id;
        }

        // First run: start from now rather than replaying the whole history.
        long latest = await _provider.GetLatestIdAsync(cancellationToken);
        await AdvanceCursorAsync(latest, cancellationToken);
        return latest;
    }

    private async Task AdvanceCursorAsync(long id, CancellationToken cancellationToken)
    {
        await _provider.SetLastPostedIdAsync(id, cancellationToken);
        _cursor = id;
    }

    private void LogOnce(string message)
    {
        if (message == _lastError) { return; }
        _lastError = message;
        Console.WriteLine($"[DiscordBot] {message}");
    }
}
