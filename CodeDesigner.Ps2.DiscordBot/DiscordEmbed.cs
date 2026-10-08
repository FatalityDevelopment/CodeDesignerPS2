using System.Text.Json.Serialization;

namespace CodeDesigner.Ps2.DiscordBot;

/// <summary>
/// Minimal Discord embed model (the subset the bot uses), serialised straight
/// into the REST payload. Limits are Discord's: title 256, description 4096,
/// 25 fields (name 256 / value 1024), footer 2048, 6000 characters per message.
/// </summary>
public sealed class DiscordEmbed
{
    public const int MaxTitle = 256;
    public const int MaxDescription = 4096;
    public const int MaxFields = 25;
    public const int MaxFieldName = 256;
    public const int MaxFieldValue = 1024;
    public const int MaxFooter = 2048;

    /// <summary>Discord rejects a message whose embeds total more than this many characters.</summary>
    public const int MaxCharsPerMessage = 6000;

    /// <summary>Discord allows at most this many embeds per message.</summary>
    public const int MaxEmbedsPerMessage = 10;

    /// <summary>Discord allows at most this many file attachments per message.</summary>
    public const int MaxAttachmentsPerMessage = 10;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("color")]
    public int? Color { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("fields")]
    public List<DiscordEmbedField> Fields { get; } = new();

    [JsonPropertyName("footer")]
    public DiscordEmbedFooter? Footer { get; set; }

    /// <summary>Files sent with the message carrying this embed (e.g. a full tool output).</summary>
    [JsonIgnore]
    public List<DiscordAttachment> Attachments { get; set; } = new();

    /// <summary>Characters counted against <see cref="MaxCharsPerMessage"/>.</summary>
    [JsonIgnore]
    public int CharCount =>
        (Title?.Length ?? 0)
        + (Description?.Length ?? 0)
        + (Footer?.Text.Length ?? 0)
        + Fields.Sum(f => f.Name.Length + f.Value.Length);

    public DiscordEmbed AddField(string name, string value, bool inline = false)
    {
        if (Fields.Count >= MaxFields || string.IsNullOrWhiteSpace(value)) { return this; }
        Fields.Add(new DiscordEmbedField
        {
            Name = DiscordText.Truncate(name, MaxFieldName),
            Value = DiscordText.Truncate(value, MaxFieldValue),
            Inline = inline,
        });
        return this;
    }
}

/// <summary>A text file uploaded alongside an embed.</summary>
public sealed record DiscordAttachment(string FileName, string Content);

public sealed class DiscordEmbedField
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("inline")]
    public bool Inline { get; set; }
}

public sealed class DiscordEmbedFooter
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
