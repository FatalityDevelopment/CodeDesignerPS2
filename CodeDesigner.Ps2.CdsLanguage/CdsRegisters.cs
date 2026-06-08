using System.Text.RegularExpressions;

namespace CodeDesigner.Ps2.CdsLanguage
{
    /// <summary>
    /// Shared CDS register helpers used by the preprocessor/parser layer.
    /// Mirrors the GPR forms accepted by <c>MipsAssembler</c> and highlighted by
    /// the VS Code <c>.cds</c> grammar: ABI names, numeric aliases, and rN aliases,
    /// with or without a leading <c>$</c>.
    /// </summary>
    internal static partial class CdsRegisters
    {
        private static readonly string[] GprNames =
        {
            "zero", "at", "v0", "v1", "a0", "a1", "a2", "a3",
            "t0", "t1", "t2", "t3", "t4", "t5", "t6", "t7",
            "s0", "s1", "s2", "s3", "s4", "s5", "s6", "s7",
            "t8", "t9", "k0", "k1", "gp", "sp", "fp", "ra"
        };

        private static readonly Dictionary<string, int> GprIndexByToken = BuildGprIndexMap();

        private static Dictionary<string, int> BuildGprIndexMap()
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < GprNames.Length; i++)
            {
                string abiName = GprNames[i];
                map[abiName] = i;
                map["$" + abiName] = i;
                map[i.ToString()] = i;
                map["$" + i] = i;
                map["r" + i] = i;
                map["$r" + i] = i;
            }

            return map;
        }

        /// <summary>
        /// Normalize a GPR token to canonical <c>$abi</c> form, or return
        /// <see langword="null"/> when the token is not a valid GPR.
        /// </summary>
        public static string? NormalizeGpr(string token)
        {
            if (!TryGetGprIndex(token, out int index)) return null;
            return "$" + GprNames[index];
        }

        public static bool TryGetGprIndex(string token, out int index)
        {
            index = -1;
            if (string.IsNullOrWhiteSpace(token)) return false;
            return GprIndexByToken.TryGetValue(token.Trim(), out index);
        }

        public static bool IsCalleeSavedGprIndex(int index)
            => (index >= 16 && index <= 23) || index == 30;

        public static string CanonicalGprName(int index) => GprNames[index];

        public static IEnumerable<(string Token, int Index)> EnumerateGprTokens(string text)
        {
            foreach (Match match in GprTokenRegex().Matches(text))
            {
                string token = match.Value;
                if (TryGetGprIndex(token, out int index))
                    yield return (token, index);
            }
        }

        [GeneratedRegex(@"(?<![A-Za-z0-9_])(?:\$?(?:zero|at|v[01]|a[0-3]|t[0-9]|s[0-7]|k[01]|gp|sp|fp|ra|r(?:[0-9]|[12][0-9]|3[01]))|\$(?:[0-9]|[12][0-9]|3[01]))(?![A-Za-z0-9_])", RegexOptions.IgnoreCase)]
        private static partial Regex GprTokenRegex();
    }
}
