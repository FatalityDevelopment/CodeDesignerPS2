namespace CodeDesigner.Ps2.DiscordBot;

/// <summary>Helpers for building Discord markdown safely within length limits.</summary>
public static class DiscordText
{
    private const string Ellipsis = "…";

    public static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) { return text; }
        return text[..Math.Max(0, maxLength - Ellipsis.Length)] + Ellipsis;
    }

    /// <summary>Wraps <paramref name="text"/> in inline code, neutralising backticks that would break it.</summary>
    public static string InlineCode(string text, int maxLength = 80)
    {
        var clean = Truncate(text.Replace('`', 'ˋ').Replace('\n', ' ').Replace('\r', ' '), maxLength);
        return clean.Length == 0 ? "` `" : $"`{clean}`";
    }

    /// <summary>
    /// Wraps <paramref name="text"/> in a fenced code block whose total length
    /// (fences included) fits in <paramref name="maxLength"/>.
    /// </summary>
    public static string CodeBlock(string text, string language = "", int maxLength = DiscordEmbed.MaxFieldValue)
    {
        var open = $"```{language}\n";
        const string close = "\n```";
        var body = text.Replace("```", "`​``").TrimEnd();
        return open + Truncate(body, maxLength - open.Length - close.Length) + close;
    }

    /// <summary>True when <see cref="CodeBlock"/> would hold <paramref name="text"/> without truncating it.</summary>
    public static bool FitsInCodeBlock(string text, string language, int maxLength) =>
        CodeBlock(text, language, int.MaxValue).Length <= maxLength;

    /// <summary>"startAddress" / "max_results" → "Start Address" / "Max Results".</summary>
    public static string Humanize(string key)
    {
        if (string.IsNullOrEmpty(key)) { return key; }

        var sb = new System.Text.StringBuilder(key.Length + 8);
        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            if (c is '_' or '-')
            {
                sb.Append(' ');
                continue;
            }

            bool wordStart = i == 0 || key[i - 1] is '_' or '-' or ' '
                || (char.IsUpper(c) && char.IsLower(key[i - 1]));
            if (wordStart && i > 0 && sb.Length > 0 && sb[^1] != ' ') { sb.Append(' '); }
            sb.Append(wordStart ? char.ToUpperInvariant(c) : c);
        }

        return sb.ToString();
    }
}
