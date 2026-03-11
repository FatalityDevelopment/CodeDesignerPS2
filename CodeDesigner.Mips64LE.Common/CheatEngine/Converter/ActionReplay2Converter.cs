using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeDesigner.Mips64LE.Common.CheatEngine.Converter
{
    /// <summary>
    /// Converter for the <b>Action Replay 1 &amp; 2 / GameShark 2</b> PS2 cheat code format.
    /// <para>
    /// AR2 codes are standard 8-digit hex pairs but the address and value words are each
    /// encrypted with a byte-wise XOR/rotate cipher seeded by a <c>DEADFACE XXXXXXXX</c>
    /// key line.  AR1 uses a fixed seed (<c>0x3D3D_3D3D</c>); AR2 reads the seed from the
    /// first <c>DEADFACE</c> line in the input.
    /// </para>
    /// <para>
    /// Ported to C# from <c>ar2.c</c> in the Omniconvert project
    /// (Copyright © 2003–2008 misfire, Pyriel; GPL v2).
    /// </para>
    /// </summary>
    public sealed class ActionReplay2Converter : ICheatCodeConverter
    {
        // AR1 uses this fixed seed (DEADFACE 3D3D3D3D in the original tool)
        private const uint Ar1Seed = 0x3D3D3D3D;

        // Marker address that introduces a new encryption seed
        private const uint DeadFaceAddr = 0xDEADFACE;

        // -----------------------------------------------------------------------
        // Byte-level seed tables  (identical to omniconvert ar2.c)
        // -----------------------------------------------------------------------
        private static readonly byte[,] Tbl = {
            {   // table 0
                0x00,0x1F,0x9B,0x69,0xA5,0x80,0x90,0xB2,
                0xD7,0x44,0xEC,0x75,0x3B,0x62,0x0C,0xA3,
                0xA6,0xE4,0x1F,0x4C,0x05,0xE4,0x44,0x6E,
                0xD9,0x5B,0x34,0xE6,0x08,0x31,0x91,0x72,
            },
            {   // table 1
                0x00,0xAE,0xF3,0x7B,0x12,0xC9,0x83,0xF0,
                0xA9,0x57,0x50,0x08,0x04,0x81,0x02,0x21,
                0x96,0x09,0x0F,0x90,0xC3,0x62,0x27,0x21,
                0x3B,0x22,0x4E,0x88,0xF5,0xC5,0x75,0x91,
            },
            {   // table 2
                0x00,0xE3,0xA2,0x45,0x40,0xE0,0x09,0xEA,
                0x42,0x65,0x1C,0xC1,0xEB,0xB0,0x69,0x14,
                0x01,0xD2,0x8E,0xFB,0xFA,0x86,0x09,0x95,
                0x1B,0x61,0x14,0x0E,0x99,0x21,0xEC,0x40,
            },
            {   // table 3
                0x00,0x25,0x6D,0x4F,0xC5,0xCA,0x04,0x39,
                0x3A,0x7D,0x0D,0xF1,0x43,0x05,0x71,0x66,
                0x82,0x31,0x21,0xD8,0xFE,0x4D,0xC2,0xC8,
                0xCC,0x09,0xA0,0x06,0x49,0xD5,0xF1,0x83,
            },
        };

        // -----------------------------------------------------------------------
        // Cipher primitives
        // -----------------------------------------------------------------------

        private static byte NibbleFlip(byte b) => (byte)((b << 4) | (b >> 4));

        private static uint Ar2Decrypt(uint code, byte type, byte seed)
        {
            if (type == 7)
            {
                if ((seed & 1) != 0) type = 1;
                else return ~code;
            }

            byte b0 = (byte)(code);
            byte b1 = (byte)(code >> 8);
            byte b2 = (byte)(code >> 16);
            byte b3 = (byte)(code >> 24);

            switch (type)
            {
                case 0:
                    b3 ^= Tbl[0, seed]; b2 ^= Tbl[1, seed];
                    b1 ^= Tbl[2, seed]; b0 ^= Tbl[3, seed];
                    break;
                case 1:
                    b3 = (byte)(NibbleFlip(b3) ^ Tbl[0, seed]);
                    b2 = (byte)(NibbleFlip(b2) ^ Tbl[2, seed]);
                    b1 = (byte)(NibbleFlip(b1) ^ Tbl[3, seed]);
                    b0 = (byte)(NibbleFlip(b0) ^ Tbl[1, seed]);
                    break;
                case 2:
                    b3 += Tbl[0, seed]; b2 += Tbl[1, seed];
                    b1 += Tbl[2, seed]; b0 += Tbl[3, seed];
                    break;
                case 3:
                    b3 -= Tbl[3, seed]; b2 -= Tbl[2, seed];
                    b1 -= Tbl[1, seed]; b0 -= Tbl[0, seed];
                    break;
                case 4:
                    b3 = (byte)((b3 ^ Tbl[0, seed]) + Tbl[0, seed]);
                    b2 = (byte)((b2 ^ Tbl[3, seed]) + Tbl[3, seed]);
                    b1 = (byte)((b1 ^ Tbl[1, seed]) + Tbl[1, seed]);
                    b0 = (byte)((b0 ^ Tbl[2, seed]) + Tbl[2, seed]);
                    break;
                case 5:
                    b3 = (byte)((b3 - Tbl[1, seed]) ^ Tbl[0, seed]);
                    b2 = (byte)((b2 - Tbl[2, seed]) ^ Tbl[1, seed]);
                    b1 = (byte)((b1 - Tbl[3, seed]) ^ Tbl[2, seed]);
                    b0 = (byte)((b0 - Tbl[0, seed]) ^ Tbl[3, seed]);
                    break;
                case 6:
                    b3 += Tbl[0, seed];
                    b2 -= Tbl[1, (seed + 1) & 0x1F];
                    b1 += Tbl[2, (seed + 2) & 0x1F];
                    b0 -= Tbl[3, (seed + 3) & 0x1F];
                    break;
            }
            return (uint)(b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));
        }

        private static uint Ar2Encrypt(uint code, byte type, byte seed)
        {
            if (type == 7)
            {
                if ((seed & 1) != 0) type = 1;
                else return ~code;
            }

            byte b0 = (byte)(code);
            byte b1 = (byte)(code >> 8);
            byte b2 = (byte)(code >> 16);
            byte b3 = (byte)(code >> 24);

            switch (type)
            {
                case 0:
                    b3 ^= Tbl[0, seed]; b2 ^= Tbl[1, seed];
                    b1 ^= Tbl[2, seed]; b0 ^= Tbl[3, seed];
                    break;
                case 1:
                    b3 = (byte)NibbleFlip((byte)(b3 ^ Tbl[0, seed]));
                    b2 = (byte)NibbleFlip((byte)(b2 ^ Tbl[2, seed]));
                    b1 = (byte)NibbleFlip((byte)(b1 ^ Tbl[3, seed]));
                    b0 = (byte)NibbleFlip((byte)(b0 ^ Tbl[1, seed]));
                    break;
                case 2:
                    b3 -= Tbl[0, seed]; b2 -= Tbl[1, seed];
                    b1 -= Tbl[2, seed]; b0 -= Tbl[3, seed];
                    break;
                case 3:
                    b3 += Tbl[3, seed]; b2 += Tbl[2, seed];
                    b1 += Tbl[1, seed]; b0 += Tbl[0, seed];
                    break;
                case 4:
                    b3 = (byte)((b3 - Tbl[0, seed]) ^ Tbl[0, seed]);
                    b2 = (byte)((b2 - Tbl[3, seed]) ^ Tbl[3, seed]);
                    b1 = (byte)((b1 - Tbl[1, seed]) ^ Tbl[1, seed]);
                    b0 = (byte)((b0 - Tbl[2, seed]) ^ Tbl[2, seed]);
                    break;
                case 5:
                    b3 = (byte)((b3 ^ Tbl[0, seed]) + Tbl[1, seed]);
                    b2 = (byte)((b2 ^ Tbl[1, seed]) + Tbl[2, seed]);
                    b1 = (byte)((b1 ^ Tbl[2, seed]) + Tbl[3, seed]);
                    b0 = (byte)((b0 ^ Tbl[3, seed]) + Tbl[0, seed]);
                    break;
                case 6:
                    b3 -= Tbl[0, seed];
                    b2 += Tbl[1, (seed + 1) & 0x1F];
                    b1 -= Tbl[2, (seed + 2) & 0x1F];
                    b0 += Tbl[3, (seed + 3) & 0x1F];
                    break;
            }
            return (uint)(b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));
        }

        // -----------------------------------------------------------------------
        // Seed helpers
        // -----------------------------------------------------------------------

        private static byte[] SeedToBytes(uint seed)
        {
            // The seed is stored big-endian in the key code: swap bytes before storing
            seed = SwapBytes(seed);
            return new[] { (byte)seed, (byte)(seed >> 8), (byte)(seed >> 16), (byte)(seed >> 24) };
        }

        private static uint SwapBytes(uint v) =>
            ((v & 0xFF000000) >> 24) | ((v & 0x00FF0000) >> 8) |
            ((v & 0x0000FF00) << 8)  | ((v & 0x000000FF) << 24);

        // -----------------------------------------------------------------------
        // Regex
        // -----------------------------------------------------------------------

        private static readonly Regex HexPair =
            new Regex(@"^([0-9A-Fa-f]{8})\s+([0-9A-Fa-f]{8})$", RegexOptions.Compiled);

        // -----------------------------------------------------------------------
        // ICheatCodeConverter
        // -----------------------------------------------------------------------

        /// <summary>
        /// Whether to operate in AR1 mode (fixed seed) rather than AR2 mode
        /// (seed from DEADFACE line).  Defaults to <c>false</c> (AR2).
        /// </summary>
        public bool UseAr1Seed { get; init; } = false;

        /// <inheritdoc/>
        public IReadOnlyList<RawCode> Decode(string input)
        {
            byte[] seed = SeedToBytes(UseAr1Seed ? Ar1Seed : 0xFFFFFFFF);
            var result  = new List<RawCode>();

            foreach (var rawLine in input.Split('\n'))
            {
                var line = rawLine.Trim();
                var m = HexPair.Match(line);
                if (!m.Success) continue;

                uint addrWord = Convert.ToUInt32(m.Groups[1].Value, 16);
                uint valWord  = Convert.ToUInt32(m.Groups[2].Value, 16);

                uint decAddr = Ar2Decrypt(addrWord, seed[0], seed[1]);
                uint decVal  = Ar2Decrypt(valWord,  seed[2], seed[3]);

                if (decAddr == DeadFaceAddr)
                {
                    // DEADFACE: update the running seed for subsequent lines
                    seed = SeedToBytes(decVal);
                    continue;
                }

                result.Add(new RawCode(decAddr, decVal));
            }
            return result;
        }

        /// <inheritdoc/>
        public string Encode(IEnumerable<RawCode> codes)
        {
            byte[] seed = SeedToBytes(UseAr1Seed ? Ar1Seed : 0xFFFFFFFF);
            var sb = new StringBuilder();

            // Emit the DEADFACE key line (only in AR2 mode with the default seed)
            if (!UseAr1Seed)
            {
                uint encDead = Ar2Encrypt(DeadFaceAddr, seed[0], seed[1]);
                uint encSeed = Ar2Encrypt(0xFFFFFFFF,   seed[2], seed[3]);
                sb.AppendLine($"{encDead:X8} {encSeed:X8}");
            }

            foreach (var code in codes)
            {
                uint encAddr = Ar2Encrypt(code.Address, seed[0], seed[1]);
                uint encVal  = Ar2Encrypt(code.Value,   seed[2], seed[3]);
                sb.AppendLine($"{encAddr:X8} {encVal:X8}");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
