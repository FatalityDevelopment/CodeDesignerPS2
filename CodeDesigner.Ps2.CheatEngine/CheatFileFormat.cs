using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeDesigner.Ps2.CheatEngine
{
    /// <summary>
    /// One cheat as represented in the editor's text format — a section, a
    /// bracketed label, and its verbatim code lines.
    /// </summary>
    public sealed class CheatFileEntry
    {
        public string Label { get; set; } = string.Empty;
        public string? Section { get; set; }
        public string? Description { get; set; }
        public string? Author { get; set; }
        public List<string> CodeLines { get; set; } = new();
    }

    /// <summary>Outcome of parsing editor text back into cheat entries.</summary>
    public sealed class CheatFileParseResult
    {
        public List<CheatFileEntry> Entries { get; } = new();
        public List<string> Errors { get; } = new();
        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>
    /// Serialises cheat entries to / from the plain-text format used by the
    /// cheats editor webview:
    /// <code>
    ///   [--SectionStart:Others--]
    ///
    ///   [invincible]
    ///   04000000 006740B0
    ///
    ///   [Infinite Health]
    ///   20123456 000003E8
    /// </code>
    /// Section markers group the cheats that follow them; a bracketed line that
    /// is not a section marker starts a new cheat; every other non-blank line is
    /// a verbatim code line for the current cheat.
    /// </summary>
    public static class CheatFileFormat
    {
        private const string DefaultSection = "Others";

        private static readonly Regex SectionMarker =
            new(@"^\[--SectionStart:(?<name>.*)--\]$", RegexOptions.Compiled);

        private static readonly Regex BracketLine =
            new(@"^\[(?<label>.+)\]$", RegexOptions.Compiled);

        // A code line is one or more groups of hex digits, with optional question-mark or
        // asterisk placeholder characters, separated by whitespace.
        private static readonly Regex CodeLine =
            new(@"^[0-9A-Fa-f?*]+(\s+[0-9A-Fa-f?*]+)*$", RegexOptions.Compiled);

        /// <summary>Builds the editor text for a set of cheat entries, grouped by section.</summary>
        public static string Serialize(IEnumerable<CheatFileEntry> entries)
        {
            var sb = new StringBuilder();
            string? currentSection = null;
            bool first = true;

            foreach (var entry in entries)
            {
                var section = string.IsNullOrWhiteSpace(entry.Section) ? DefaultSection : entry.Section!.Trim();
                if (first || !string.Equals(section, currentSection, StringComparison.Ordinal))
                {
                    if (!first) sb.AppendLine();
                    sb.Append("[--SectionStart:").Append(section).AppendLine("--]");
                    sb.AppendLine();
                    currentSection = section;
                }
                first = false;

                sb.Append('[').Append(entry.Label).AppendLine("]");
                AppendCommentLines(sb, "Description", entry.Description);
                AppendCommentLines(sb, "Author", entry.Author);
                foreach (var code in entry.CodeLines)
                    sb.AppendLine(code);
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd() + "\n";
        }

        /// <summary>Parses editor text back into cheat entries, collecting validation errors.</summary>
        public static CheatFileParseResult Parse(string? text)
        {
            var result = new CheatFileParseResult();
            if (string.IsNullOrWhiteSpace(text))
                return result;

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            string? currentSection = null;
            CheatFileEntry? current = null;
            int lineNo = 0;
            bool inBlockComment = false;

            foreach (var raw in lines)
            {
                lineNo++;
                var commentMeta = TryReadCommentMetadata(raw);
                if (commentMeta is not null)
                {
                    if (current is null)
                        continue;

                    if (string.Equals(commentMeta.Value.Key, "Author", StringComparison.OrdinalIgnoreCase))
                        current.Author = AppendMetadata(current.Author, commentMeta.Value.Value, ", ");
                    else
                        current.Description = AppendMetadata(current.Description, commentMeta.Value.Value, Environment.NewLine);
                    continue;
                }

                var line = StripComments(raw, ref inBlockComment).Trim();
                if (line.Length == 0)
                    continue;

                var sectionMatch = SectionMarker.Match(line);
                if (sectionMatch.Success)
                {
                    currentSection = sectionMatch.Groups["name"].Value.Trim();
                    current = null;
                    continue;
                }

                var bracketMatch = BracketLine.Match(line);
                if (bracketMatch.Success)
                {
                    current = new CheatFileEntry
                    {
                        Label   = bracketMatch.Groups["label"].Value.Trim(),
                        Section = string.IsNullOrWhiteSpace(currentSection) ? DefaultSection : currentSection,
                    };
                    result.Entries.Add(current);
                    continue;
                }

                // A code line.
                if (current == null)
                {
                    result.Errors.Add($"Line {lineNo}: code line \"{line}\" appears before any [cheat] header.");
                    continue;
                }

                if (!CodeLine.IsMatch(line))
                {
                    result.Errors.Add($"Line {lineNo}: \"{line}\" is not a valid code line (hex digits plus ? or * placeholders).");
                    continue;
                }

                current.CodeLines.Add(line.ToUpperInvariant());
            }

            foreach (var entry in result.Entries.Where(e => e.CodeLines.Count == 0))
                result.Errors.Add($"Cheat \"{entry.Label}\" has no code lines.");

            return result;
        }

        private static void AppendCommentLines(StringBuilder sb, string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var lines = value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var line in lines)
                sb.Append("// ").Append(key).Append(": ").AppendLine(line.Trim());
        }

        private static (string Key, string Value)? TryReadCommentMetadata(string raw)
        {
            var trimmed = raw.TrimStart();
            if (!trimmed.StartsWith("//", StringComparison.Ordinal))
                return null;

            var comment = trimmed[2..].Trim();
            var match = Regex.Match(comment, @"^(?<key>Description|Author)\s*:\s*(?<value>.+)$", RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;

            return (match.Groups["key"].Value, match.Groups["value"].Value.Trim());
        }

        private static string AppendMetadata(string? existing, string value, string separator)
            => string.IsNullOrWhiteSpace(existing) ? value : existing + separator + value;

        private static string StripComments(string line, ref bool inBlockComment)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < line.Length;)
            {
                if (inBlockComment)
                {
                    var end = line.IndexOf("*/", i, StringComparison.Ordinal);
                    if (end < 0)
                        return sb.ToString();
                    i = end + 2;
                    inBlockComment = false;
                    continue;
                }

                if (i + 1 < line.Length && line[i] == '/' && line[i + 1] == '/')
                    break;

                if (i + 1 < line.Length && line[i] == '/' && line[i + 1] == '*')
                {
                    inBlockComment = true;
                    i += 2;
                    continue;
                }

                sb.Append(line[i]);
                i++;
            }
            return sb.ToString();
        }
    }
}
