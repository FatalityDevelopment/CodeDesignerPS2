using System;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Tiny PS2 SYSTEM.CNF parser — extracts the BOOT2 path so handlers can
    /// resolve the boot ELF on a disc without depending on EmulatorCore.
    /// Mirrors the implementation in
    /// <c>CodeDesigner.Ps2.EmulatorCore.GameLoader.ParseSystemCnf</c>.
    /// </summary>
    internal static class SystemCnfParser
    {
        private const string Boot2Key    = "BOOT2";
        private const string CdromPrefix = "cdrom0:";

        /// <summary>
        /// Parse <paramref name="content"/> (the textual SYSTEM.CNF) and return
        /// the BOOT2 disc-relative path (e.g. <c>SLUS_210.40;1</c>) with any
        /// leading <c>cdrom0:</c> prefix stripped. Returns <c>null</c> when no
        /// BOOT2 key is found.
        /// </summary>
        public static string? ParseBoot2(string? content)
        {
            if (string.IsNullOrEmpty(content))
                return null;

            foreach (string rawLine in content.Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                int eq = line.IndexOf('=');
                if (eq < 0) continue;

                string key = line[..eq].Trim();
                if (!key.Equals(Boot2Key, StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = line[(eq + 1)..].Trim();
                if (value.StartsWith(CdromPrefix, StringComparison.OrdinalIgnoreCase))
                    value = value[CdromPrefix.Length..].TrimStart('\\', '/');

                return value.Length > 0 ? value : null;
            }
            return null;
        }
    }
}
