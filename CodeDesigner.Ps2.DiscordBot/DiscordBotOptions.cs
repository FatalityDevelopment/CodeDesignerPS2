namespace CodeDesigner.Ps2.DiscordBot;

/// <summary>
/// Where the bot posts and what it posts. Either a bot token + channel ID or a
/// channel webhook URL must be set; the bot idles until one is.
/// </summary>
public sealed record DiscordBotOptions
{
    /// <summary>Bot token (Developer Portal → Bot → Token). Used with <see cref="ChannelId"/>.</summary>
    public string? Token { get; init; }

    /// <summary>Numeric ID of the channel the bot posts activity to.</summary>
    public string? ChannelId { get; init; }

    /// <summary>Channel webhook URL — an alternative to a bot token + channel ID.</summary>
    public string? WebhookUrl { get; init; }

    /// <summary>
    /// Activity kinds to post ("tool", "activity", "session", "label", "packet").
    /// Empty means all kinds.
    /// </summary>
    public IReadOnlySet<string> Kinds { get; init; } = new HashSet<string>();

    public bool UsesBotToken => !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(ChannelId);

    public bool UsesWebhook => !string.IsNullOrWhiteSpace(WebhookUrl);

    public bool IsConfigured => UsesBotToken || UsesWebhook;

    public bool ShouldPost(string kind) => Kinds.Count == 0 || Kinds.Contains(kind);
}
