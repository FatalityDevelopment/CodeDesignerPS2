using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodeDesigner.Ps2.CheatEngine
{
    /// <summary>
    /// Lists the cheat code formats that <see cref="CheatFormatDetector"/> and the
    /// converter factory can work with.
    /// </summary>
    public enum CheatFormat
    {
        /// <summary>Unencrypted GameShark / Action Replay PS2 hex format (<c>TTAAAAAA VVVVVVVV</c>).</summary>
        Raw,

        /// <summary>Action Replay 1 — fixed-seed byte-XOR encryption.</summary>
        ActionReplay1,

        /// <summary>Action Replay 2 / GameShark 2 — DEADFACE-seeded byte-XOR encryption.</summary>
        ActionReplay2,

        /// <summary>Action Replay MAX — DES-like block cipher with 32-char alphanumeric encoding.</summary>
        ActionReplayMax,

        /// <summary>GameShark PS2 Version 3 / Xploder 4 / Xterminator — Mersenne Twister cipher.</summary>
        GameSharkV3,

        /// <summary>CodeBreaker PS2 Version 1–6 — seed-table XOR/rotation cipher.</summary>
        CodeBreakerV1,

        /// <summary>CodeBreaker PS2 Version 7+ — ARCFOUR + modular-inverse cipher.</summary>
        CodeBreakerV7,
    }

    /// <summary>
    /// Heuristically identifies the format of a block of PS2 cheat codes.
    /// </summary>
    public static class CheatFormatDetector
    {
        // ARMax code line: XXXX-XXXX-XXXXX where chars are in the 32-char ARMax alphabet
        // The alphabet is "0123456789ABCDEFGHJKMNPQRTUVWXYZ" (no I, L, O, S).
        // Expressed as ranges: [0-9A-HJ-KM-NP-RT-Z]
        private static readonly Regex ArMaxLine =
            new Regex(@"^[0-9A-HJ-KM-NP-RT-Z]{4}-[0-9A-HJ-KM-NP-RT-Z]{4}-[0-9A-HJ-KM-NP-RT-Z]{5}$",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex HexPair =
            new Regex(@"^[0-9A-Fa-f]{8}\s+[0-9A-Fa-f]{8}$", RegexOptions.Compiled);

        /// <summary>
        /// Attempts to detect the format of <paramref name="input"/>.
        /// Returns <see cref="CheatFormat.Raw"/> when the format cannot be determined
        /// more precisely.
        /// </summary>
        public static CheatFormat Detect(string input)
        {
            var lines = input
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#") && !l.StartsWith("//"))
                .ToList();

            if (lines.Count == 0) return CheatFormat.Raw;

            // --- ARMax: alphanumeric lines with dashes ---
            int armaxCount = lines.Count(l => ArMaxLine.IsMatch(l.ToUpperInvariant()));
            if (armaxCount > 0 && armaxCount == lines.Count)
                return CheatFormat.ActionReplayMax;

            // --- For hex-pair formats, look for distinctive marker lines ---
            int hexCount = lines.Count(l => HexPair.IsMatch(l));
            if (hexCount == 0) return CheatFormat.Raw;

            // BEEFC0DE or BEEFC0DF → CodeBreaker V7
            if (lines.Any(l => l.StartsWith("BEEFC0DE", StringComparison.OrdinalIgnoreCase) ||
                               l.StartsWith("BEEFC0DF", StringComparison.OrdinalIgnoreCase)))
                return CheatFormat.CodeBreakerV7;

            // DEADFACE → Action Replay 2
            if (lines.Any(l => l.StartsWith("DEADFACE", StringComparison.OrdinalIgnoreCase)))
                return CheatFormat.ActionReplay2;

            // GS3 verifier lines begin with 76XXXXXX or 77XXXXXX
            if (lines.Any(l => HexPair.IsMatch(l) &&
                               (l.StartsWith("76", StringComparison.OrdinalIgnoreCase) ||
                                l.StartsWith("77", StringComparison.OrdinalIgnoreCase))))
                return CheatFormat.GameSharkV3;

            // CB1: the top nibble of the address word for standard CB1 codes is always
            // in the range 0–C (standard GS types). If all top nibbles are in 0–F and
            // any line has bits 26–28 of the address non-zero (GS3 key bits), favour GS3.
            bool hasGsKeyBits = lines.Where(l => HexPair.IsMatch(l) && l.Length >= 8).Any(l =>
            {
                uint addr = Convert.ToUInt32(l.Substring(0, 8), 16);
                return ((addr >> 25) & 0x7) is 1 or 2 or 3 or 4;
            });
            if (hasGsKeyBits) return CheatFormat.GameSharkV3;

            // Default to raw (unencrypted) hex
            return CheatFormat.Raw;
        }

        /// <summary>
        /// Creates the appropriate <see cref="ICheatCodeConverter"/> for the detected format.
        /// </summary>
        public static ICheatCodeConverter CreateConverter(CheatFormat format) =>
            format switch
            {
                CheatFormat.ActionReplayMax => new ArMaxConverter(),
                CheatFormat.ActionReplay2   => new ActionReplay2Converter(),
                CheatFormat.ActionReplay1   => new ActionReplay2Converter { UseAr1Seed = true },
                CheatFormat.GameSharkV3     => new GameSharkV3Converter(),
                CheatFormat.CodeBreakerV1   => new CodeBreakerV1Converter(),
                CheatFormat.CodeBreakerV7   => new CodeBreakerV7Converter(),
                _                           => new RawConverter(),
            };

        /// <summary>
        /// Convenience method: auto-detects the format and decodes <paramref name="input"/>
        /// into a list of <see cref="RawCode"/> entries.
        /// </summary>
        public static System.Collections.Generic.IReadOnlyList<RawCode> AutoDecode(string input)
        {
            var format = Detect(input);
            return CreateConverter(format).Decode(input);
        }
    }
}
