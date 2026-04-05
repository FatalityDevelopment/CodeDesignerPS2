using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeDesigner.Ps2.CheatEngine
{
    /// <summary>
    /// Identity converter for the canonical unencrypted GameShark / Action Replay PS2
    /// raw format: two 8-digit hex fields per line separated by whitespace.
    /// <para>
    /// Both hex-only lines and lines that begin with a <c>#</c> comment are accepted on input.
    /// Comment lines are preserved on round-trip encoding.
    /// </para>
    /// </summary>
    public sealed class RawConverter : ICheatCodeConverter
    {
        private static readonly Regex CodeLine =
            new Regex(@"^([0-9A-Fa-f]{8})\s+([0-9A-Fa-f]{8})$", RegexOptions.Compiled);

        /// <inheritdoc/>
        public IReadOnlyList<RawCode> Decode(string input)
        {
            var result = new List<RawCode>();
            foreach (var rawLine in input.Split('\n'))
            {
                var line = rawLine.Trim();
                var m = CodeLine.Match(line);
                if (!m.Success) continue;
                result.Add(new RawCode(
                    Convert.ToUInt32(m.Groups[1].Value, 16),
                    Convert.ToUInt32(m.Groups[2].Value, 16)));
            }
            return result;
        }

        /// <inheritdoc/>
        public string Encode(IEnumerable<RawCode> codes)
        {
            var sb = new StringBuilder();
            foreach (var code in codes)
                sb.AppendLine(code.ToString());
            return sb.ToString().TrimEnd();
        }
    }
}
