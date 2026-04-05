using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeDesigner.Ps2.CheatEngine
{
    /// <summary>
    /// Converter for <b>CodeBreaker PS2 Version 1–6 (CB1/CB5)</b> cheat codes.
    /// <para>
    /// CB1-style codes use a seed-table XOR cipher.  Each address word is
    /// transformed based on the top 4 bits (the "command" nibble), using three
    /// seed tables derived from the code's own address word.
    /// </para>
    /// <para>
    /// Ported to C# from <c>cb2_crypto.c</c> in the cb2util project
    /// (Copyright © 2006–2013 Mathias Lafeldt; MIT licence).
    /// </para>
    /// </summary>
    public sealed class CodeBreakerV1Converter : ICheatCodeConverter
    {
        // -----------------------------------------------------------------------
        // Seed tables (identical to cb2_crypto.c seedtable[0..2])
        // -----------------------------------------------------------------------
        private static readonly uint[,] SeedTable = {
            {
                0x0A0B8D9B,0x0A0133F8,0x0AF733EC,0x0A15C574,
                0x0A50AC20,0x0A920FB9,0x0A599F0B,0x0A4AA0E3,
                0x0A21C012,0x0A906254,0x0A31FD54,0x0A091C0E,
                0x0A372B38,0x0A6F266C,0x0A61DD4A,0x0A0DBF92,
            },
            {
                0x00288596,0x0037DD28,0x003BEEF1,0x000BC822,
                0x00BC935D,0x00A139F2,0x00E9BBF8,0x00F57F7B,
                0x0090D704,0x001814D4,0x00C5848E,0x005B83E7,
                0x00108CF7,0x0046CE5A,0x003A5BF4,0x006FAFFC,
            },
            {
                0x1DD9A10A,0xB95AB9B0,0x5CF5D328,0x95FE7F10,
                0x8E2D6303,0x16BB6286,0xE389324C,0x07AC6EA8,
                0xAA4811D8,0x76CE4E18,0xFE447516,0xF9CD94D0,
                0x4C24DEDB,0x68275C4E,0x72494382,0xC8AA88E8,
            },
        };

        // -----------------------------------------------------------------------
        // Cipher
        // -----------------------------------------------------------------------

        private static uint Decrypt(uint addr, uint val)
        {
            int cmd = (int)(addr >> 28);
            if (cmd > 2)
                val = (addr ^ val) - SeedTable[2, cmd];

            uint tmp = addr ^ SeedTable[0, cmd];
            addr = tmp - SeedTable[1, cmd];
            addr = (tmp & 0xFF000000) | ((addr & 0xFFFF) << 8) | ((addr >> 16) & 0xFF);
            return addr;
            // (val is returned unchanged once we've patched addr)
        }

        private static (uint addr, uint val) DecryptPair(uint rawAddr, uint rawVal)
        {
            int cmd = (int)(rawAddr >> 28);

            uint decVal = rawVal;
            if (cmd > 2)
                decVal = (rawAddr ^ rawVal) - SeedTable[2, cmd];

            uint tmp    = rawAddr ^ SeedTable[0, cmd];
            uint decAddr = tmp - SeedTable[1, cmd];
            decAddr = (tmp & 0xFF000000) | ((decAddr & 0xFFFF) << 8) | ((decAddr >> 16) & 0xFF);

            return (decAddr, decVal);
        }

        private static (uint addr, uint val) EncryptPair(uint addr, uint val)
        {
            int cmd = (int)(addr >> 28);

            uint encAddr = addr & 0xFF000000;
            uint middle  = ((addr & 0xFFFFFF) >> 8) | ((addr & 0xFF) << 16);
            encAddr |= (middle + SeedTable[1, cmd]) & 0x00FFFFFF;
            encAddr ^= SeedTable[0, cmd];

            uint encVal = val;
            if (cmd > 2)
                encVal = encAddr ^ (val + SeedTable[2, cmd]);

            return (encAddr, encVal);
        }

        // -----------------------------------------------------------------------
        // Regex
        // -----------------------------------------------------------------------

        private static readonly Regex HexPair =
            new Regex(@"^([0-9A-Fa-f]{8})\s+([0-9A-Fa-f]{8})$", RegexOptions.Compiled);

        // -----------------------------------------------------------------------
        // ICheatCodeConverter
        // -----------------------------------------------------------------------

        /// <inheritdoc/>
        public IReadOnlyList<RawCode> Decode(string input)
        {
            var result = new List<RawCode>();
            foreach (var rawLine in input.Split('\n'))
            {
                var line = rawLine.Trim();
                var m = HexPair.Match(line);
                if (!m.Success) continue;

                uint encAddr = Convert.ToUInt32(m.Groups[1].Value, 16);
                uint encVal  = Convert.ToUInt32(m.Groups[2].Value, 16);
                var (addr, val) = DecryptPair(encAddr, encVal);
                result.Add(new RawCode(addr, val));
            }
            return result;
        }

        /// <inheritdoc/>
        public string Encode(IEnumerable<RawCode> codes)
        {
            var sb = new StringBuilder();
            foreach (var code in codes)
            {
                var (encAddr, encVal) = EncryptPair(code.Address, code.Value);
                sb.AppendLine($"{encAddr:X8} {encVal:X8}");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
