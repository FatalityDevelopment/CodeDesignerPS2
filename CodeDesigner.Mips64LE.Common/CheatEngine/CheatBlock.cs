using CodeDesigner.Mips64LE.Common.CheatEngine.Enums;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CodeDesigner.Mips64LE.Common.CheatEngine
{
    public class CheatBlock
    {
        public string? Label { get; set; }
        public List<Cheat> Cheats { get; set; } = new List<Cheat>();

        public static CheatBlock Parse(string block)
        {
            var labelMatch = Regex.Match(block, @"(#.+?)(\r\n|\n)", RegexOptions.IgnoreCase);
            var cheatBlock = new CheatBlock { Label = labelMatch.Groups[1].Value };

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
