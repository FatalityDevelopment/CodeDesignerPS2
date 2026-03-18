using CodeDesigner.Ps2.CheatEngine.Enums;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CodeDesigner.Ps2.CheatEngine
{
    public class CheatBlock
    {
        public string? Label     { get; set; }
        /// <summary>
        /// Any non-label, non-hex-code text lines found in the cheat block (e.g. comments or
        /// descriptions written between the #label line and the hex code lines).  Null when
        /// no such lines are present.
        /// </summary>
        public string? BlockText { get; set; }
        public List<Cheat> Cheats { get; set; } = new List<Cheat>();

        private static readonly Regex _hexCodeLine =
            new Regex(@"^[a-f0-9]{8}\s+[a-f0-9]{8}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static CheatBlock Parse(string block)
        {
            // Match the first #label line; [^\r\n]+ means a newline is not required at
            // end-of-block (handles single-line blocks and last-line labels correctly).
            var labelMatch = Regex.Match(block, @"(#[^\r\n]+)");
            string label   = labelMatch.Success ? labelMatch.Groups[1].Value.TrimEnd() : string.Empty;
            var cheatBlock = new CheatBlock { Label = label };

            // Capture any lines that are neither a #label nor a hex code pair as description text.
            var textLines = new List<string>();
            foreach (var rawLine in block.Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.None))
            {
                var trimmed = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                if (trimmed.StartsWith("#"))             continue; // label line
                if (_hexCodeLine.IsMatch(trimmed))       continue; // hex code pair
                textLines.Add(trimmed);
            }
            if (textLines.Count > 0)
                cheatBlock.BlockText = string.Join("\n", textLines);

            // _hexCodeLine uses ^ anchoring and no capture groups (line-filter use above).
            // The pattern below uses capture groups for address/data extraction across the whole block.
            var cheatMatches = Regex.Matches(block, @"([a-f0-9]{8})\s+([a-f0-9]{8})", RegexOptions.IgnoreCase);

            int i = 0;
            foreach (Match match in cheatMatches)
            {
                var addressField = match.Groups[1].Value;
                var dataField    = match.Groups[2].Value;

                var data = new byte[4];
                for (int b = 0; b < 4; b++)
                    data[b] = Convert.ToByte(dataField.Substring(b * 2, 2), 16);
                Array.Reverse(data);

                cheatBlock.Cheats.Add(new Cheat
                {
                    Position  = i,
                    Address   = Convert.ToInt32(addressField.Substring(2, 6), 16),
                    CheatType = (CheatType)Convert.ToInt32(addressField.Substring(0, 2), 16),
                    Data      = data,
                });
                i++;
            }
            return cheatBlock;
        }
    }
}
