using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CodeDesigner.Ps2.CheatEngine
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

            // Walk line by line so that any text line (name/label) above code lines always
            // starts a new block, even without blank lines between blocks.  This handles the
            // common pattern of pasting cheat lists where names have no # prefix:
            //   Infinite Health
            //   20123456 000003E8
            //   Max Ammo
            //   20789ABC 000001F4
            // The # prefix is still accepted for backward compatibility.
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

                bool isCode = _codeLine.IsMatch(trimmed);

                // Any text line (with or without # prefix) that arrives while the current
                // block already has at least one code line means a new cheat block is
                // starting.  The text line becomes the name/label of the new block.
                if (!isCode && currentBlockHasCodes)
                    FlushBlock();

                currentBlockLines.Add(trimmed);

                if (isCode)
                    currentBlockHasCodes = true;
            }

            FlushBlock();
            return _cheatList;
        }
    }
}
