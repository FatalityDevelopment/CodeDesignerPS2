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

        public List<CheatBlock> Parse(string? cheatList = null)
        {
            _cheatListText = _cheatListText ?? cheatList;

            if (string.IsNullOrWhiteSpace(_cheatListText))
                return _cheatList;

            // Split on blank lines (handles both \r\n and \n line endings)
            var blocks = Regex.Split(_cheatListText.Trim(), @"\r?\n\s*\r?\n");

            foreach (var block in blocks)
            {
                if (!string.IsNullOrWhiteSpace(block))
                    _cheatList.Add(CheatBlock.Parse(block));
            }
            return _cheatList;
        }
    }
}
