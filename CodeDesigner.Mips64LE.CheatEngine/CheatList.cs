using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CodeDesigner.Mips64LE.Common.CheatEngine
{
    public class CheatList
    {
        private string? _cheatListText;
        private List<CheatBlock> _cheatList { get; set; } = new List<CheatBlock>();

        public CheatList() { }

        public CheatList(string cheatList)
        {
            _cheatListText = cheatList;
        }

        // Matches a GameShark hex-pair code line: 8 hex digits, whitespace, 8 hex digits.
        private static readonly Regex _codeLine =
            new Regex(@"^[a-f0-9]{8}\s+[a-f0-9]{8}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public List<CheatBlock> Parse(string? cheatList = null)
        {
            _cheatListText = _cheatListText ?? cheatList;

            if (string.IsNullOrWhiteSpace(_cheatListText))
                return _cheatList;

            // Walk line by line so that a new #label line always starts a new block even
            // when there is no blank line between blocks.  This handles the common pattern
            // of pasting cheat lists where each named cheat is on consecutive lines without
            // blank separators:
            //   #Infinite Health
            //   20123456 000003E8
            //   #Max Ammo
            //   20789ABC 000001F4
            var lines = _cheatListText.Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.None);
            var currentBlockLines = new List<string>();
            bool currentBlockHasCodes = false;

            // FlushBlock writes currentBlockLines into _cheatList and then resets both
            // currentBlockLines and currentBlockHasCodes for the next block.
            void FlushBlock()
            {
                if (currentBlockLines.Count > 0)
                {
                    _cheatList.Add(CheatBlock.Parse(string.Join("\n", currentBlockLines)));
                    currentBlockLines.Clear();
                    currentBlockHasCodes = false;
                }
            }

            foreach (var line in lines)
            {
                var trimmed = line.TrimEnd();

                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    // Blank line always flushes the current block.
                    FlushBlock();
                    continue;
                }

                // A #label line that arrives while the current block already has at least
                // one code line means a new cheat block is starting.
                if (trimmed.StartsWith("#") && currentBlockHasCodes)
                    FlushBlock();

                currentBlockLines.Add(trimmed);

                if (_codeLine.IsMatch(trimmed))
                    currentBlockHasCodes = true;
            }

            FlushBlock();
            return _cheatList;
        }
    }
}
