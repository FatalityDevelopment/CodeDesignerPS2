using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeDesigner.Mips64LE.Common.CheatEngine.Converter
{
    /// <summary>
    /// Converter for <b>GameShark PS2 Version 3+ / Xploder 4+ / Xterminator</b> cheat codes.
    /// <para>
    /// GS3 codes use the same <c>TTAAAAAA VVVVVVVV</c> hex layout as raw codes, but bits
    /// 26–28 of the address word encode the encryption key type (0 = raw, 1–6 = various
    /// cipher modes, 7 = seed-refresh).  The byte-level substitution tables are generated
    /// with a <b>Mersenne Twister</b> PRNG seeded from three 16-bit half-word seeds.
    /// </para>
    /// <para>
    /// Ported to C# from <c>gs3.c</c> in the Omniconvert project
    /// (Copyright © 2004–2008 Pyriel; GPL v2).
    /// </para>
    /// </summary>
    public sealed class GameSharkV3Converter : ICheatCodeConverter
    {
        // -----------------------------------------------------------------------
        // Mersenne Twister constants
        // -----------------------------------------------------------------------
        private const int    MtStateSize   = 624;
        private const int    MtRecOffset   = 397;
        private const uint   MtMaskB       = 0x9D2C5680;
        private const uint   MtMaskC       = 0xEFC60000;
        private const uint   MtSmask       = 0x80000000;
        private const uint   MtLmask       = 0x7FFFFFFF;
        private const uint   MtMultiplier  = 69069;
        private const uint   DecryptMask   = 0xF1FFFFFF;

        // Default half-word seeds (from omniconvert gs3.c gs3_hseeds)
        private static readonly ushort[] DefaultHSeeds = { 0x6C27, 0x1D38, 0x7FE1 };

        // Table sizes for the four substitution tables
        private static readonly int[] TableSizes = { 0x40, 0x39, 0x19, 0x100 };

        // -----------------------------------------------------------------------
        // Mersenne Twister implementation
        // -----------------------------------------------------------------------

        private static void MtInit(uint[] state, uint seed)
        {
            state[0] = seed;
            for (int i = 1; i < MtStateSize; i++)
                state[i] = MtMultiplier * state[i - 1];
        }

        private static readonly uint[] DecisionMatrix = { 0, 0x9908B0DF };

        /// <param name="passCount">Mutable pass-count cursor; callers own the state.</param>
        private static uint MtGetNum(uint[] state, ref int passCount)
        {
            if (passCount >= MtStateSize)
            {
                for (int i = 0; i < MtStateSize - MtRecOffset; i++)
                {
                    uint y = (state[i] & MtSmask) | (state[i + 1] & MtLmask);
                    state[i] = state[i + MtRecOffset] ^ (y >> 1) ^ DecisionMatrix[y & 1];
                }
                for (int i = MtStateSize - MtRecOffset; i < MtStateSize - 1; i++)
                {
                    uint y = (state[i] & MtSmask) | (state[i + 1] & MtLmask);
                    state[i] = state[i + (MtRecOffset - MtStateSize)] ^ (y >> 1) ^ DecisionMatrix[y & 1];
                }
                uint yLast = (state[MtStateSize - 1] & MtSmask) | (state[0] & MtLmask);
                state[MtStateSize - 1] = state[MtRecOffset - 1] ^ (yLast >> 1) ^ DecisionMatrix[yLast & 1];
                passCount = 0;
            }

            uint val = state[passCount++];
            val ^= (val >> 11);
            val ^= (val << 7) & MtMaskB;
            val ^= (val << 15) & MtMaskC;
            val ^= (val >> 18);
            return val;
        }

        // -----------------------------------------------------------------------
        // Seed table generation
        // -----------------------------------------------------------------------

        private static byte[] BuildByteSeedTable(ushort seed, int size)
        {
            var tbl = new byte[size];
            for (int i = 0; i < size; i++) tbl[i] = (byte)i;

            var mtState = new uint[MtStateSize];
            int passCount = MtStateSize;
            MtInit(mtState, seed);

            int passes = size * 2;
            while (passes-- > 0)
            {
                int idx1 = (int)(MtGetNum(mtState, ref passCount) % (uint)size);
                int idx2 = (int)(MtGetNum(mtState, ref passCount) % (uint)size);
                (tbl[idx1], tbl[idx2]) = (tbl[idx2], tbl[idx1]);
            }
            return tbl;
        }

        private static byte[] ReverseTable(byte[] src, int size)
        {
            var dst = new byte[size];
            for (int i = 0; i < size; i++) dst[src[i]] = (byte)i;
            return dst;
        }

        // -----------------------------------------------------------------------
        // Per-converter seed state (not static — each instance holds its own tables)
        // -----------------------------------------------------------------------

        private readonly ushort[] _hseeds;
        private readonly byte[][] _encSeeds;   // [4][]
        private readonly byte[][] _decSeeds;   // [4][]

        /// <summary>
        /// Creates a converter with the default GS3 seeds (<c>0x6C27 / 0x1D38 / 0x7FE1</c>).
        /// </summary>
        public GameSharkV3Converter() : this(DefaultHSeeds[0], DefaultHSeeds[1], DefaultHSeeds[2]) { }

        /// <summary>
        /// Creates a converter with custom half-word seeds.  Pass the three 16-bit values
        /// from a GS3 key/verifier code (<c>7XXXXXXX YYYYYYYY</c>).
        /// </summary>
        public GameSharkV3Converter(ushort seed1, ushort seed2, ushort seed3)
        {
            _hseeds = new ushort[] { seed1, seed2, seed3 };
            _encSeeds = new byte[4][];
            _decSeeds = new byte[4][];
            BuildSeeds(seed1, seed2, seed3);
        }

        private void BuildSeeds(ushort seed1, ushort seed2, ushort seed3)
        {
            for (int i = 0; i < 4; i++)
            {
                int shift = 5 - i;
                if (shift < 3) shift = 0;
                _encSeeds[i] = BuildByteSeedTable((ushort)(seed1 >> shift), TableSizes[i]);
                _decSeeds[i] = ReverseTable(_encSeeds[i], TableSizes[i]);
            }
        }

        // -----------------------------------------------------------------------
        // Decrypt helpers
        // -----------------------------------------------------------------------

        private static uint BytesToWord(byte a, byte b, byte c, byte d) =>
            ((uint)a << 24) | ((uint)b << 16) | ((uint)c << 8) | d;
        private static uint BytesToHalf(byte a, byte b) => ((uint)a << 8) | b;
        private static byte GetByte(uint word, int num) => (byte)(word >> ((num - 1) * 8));

        private bool Gs3Decrypt(ref uint address, ref uint value, ref bool lineTwo, ref byte xkey)
        {
            uint command = address & 0xFE000000;
            byte key = (byte)((address >> 25) & 0x7);

            // Second-line handling for two-line code types
            if (lineTwo)
            {
                switch (xkey)
                {
                    case 1:
                    case 2: // X1 permutation
                    {
                        var tbl  = _decSeeds[0]; int size = TableSizes[0];
                        uint tmp = 0, tmp2 = 0;
                        for (int i = 0; i < size; i++)
                        {
                            uint flag = (i < 32)
                                ? ((value  ^ (uint)(_hseeds[1] << 13)) >> i) & 1u
                                : (((address ^ (uint)(_hseeds[1] << 2))) >> (i - 32)) & 1u;
                            if (flag != 0)
                            {
                                if (tbl[i] < 32) tmp2 |= (1u << tbl[i]);
                                else             tmp  |= (1u << (tbl[i] - 32));
                            }
                        }
                        address = tmp  ^ (uint)(_hseeds[2] << 3);
                        value   = tmp2 ^ (uint)_hseeds[2];
                        lineTwo = false;
                        return true;
                    }
                    case 3:
                    case 4: // X2 byte substitution
                    {
                        var tbl = _decSeeds[3];
                        uint tmp = address ^ (uint)(_hseeds[1] << 3);
                        address = BytesToWord(tbl[GetByte(tmp, 4)], tbl[GetByte(tmp, 3)],
                                              tbl[GetByte(tmp, 2)], tbl[GetByte(tmp, 1)])
                                  ^ (uint)(_hseeds[2] << 16);
                        tmp   = value ^ (uint)(_hseeds[1] << 9);
                        value = BytesToWord(tbl[GetByte(tmp, 4)], tbl[GetByte(tmp, 3)],
                                            tbl[GetByte(tmp, 2)], tbl[GetByte(tmp, 1)])
                                ^ (uint)(_hseeds[2] << 5);
                        lineTwo = false;
                        return true;
                    }
                    default:
                        lineTwo = false;
                        return true;
                }
            }

            if (command >= 0x30000000 && command <= 0x6FFFFFFF)
            { lineTwo = true; xkey = key; }

            if ((command >> 28) == 7) return true; // verifier line — do not modify

            switch (key)
            {
                case 0: break; // raw

                case 1: // bit-permutation using table[0]
                {
                    var tbl = _decSeeds[0]; int size = TableSizes[0];
                    uint tmp = (address & 0x01FFFFFF) ^ (uint)(_hseeds[1] << 8);
                    uint mask = 0;
                    for (int i = 0; i < size; i++)
                        mask |= ((tmp & (1u << i)) >> i) << tbl[i];
                    address = ((mask ^ (uint)_hseeds[2]) | command) & DecryptMask;
                    break;
                }

                case 2: // bit-permutation using table[1]
                {
                    var tbl = _decSeeds[1]; int size = TableSizes[1];
                    uint origAddr = address;
                    address = (address & 0x01FFFFFF) ^ (uint)(_hseeds[1] << 1);
                    uint tmp = 0, tmp2 = 0;
                    for (int i = 0; i < size; i++)
                    {
                        uint flag = (i < 32)
                            ? ((value   ^ (uint)(_hseeds[1] << 16)) >> i) & 1u
                            : ((address >> (i - 32)) & 1u);
                        if (flag != 0)
                        {
                            if (tbl[i] < 32) tmp2 |= (1u << tbl[i]);
                            else             tmp  |= (1u << (tbl[i] - 32));
                        }
                    }
                    address = ((tmp ^ (uint)(_hseeds[2] << 8)) | command) & DecryptMask;
                    value   = tmp2 ^ (uint)_hseeds[2];
                    break;
                }

                case 3: // byte-substitution (lower 2 bytes of address only)
                {
                    var tbl = _decSeeds[3];
                    uint tmp = address ^ (uint)(_hseeds[1] << 8);
                    address = ((BytesToHalf(tbl[GetByte(tmp, 1)], tbl[GetByte(tmp, 2)])
                                | (tmp & 0xFFFF0000)) ^ (uint)(_hseeds[2] << 4)) & DecryptMask;
                    break;
                }

                case 4: // byte-substitution (address and value)
                {
                    var tbl = _decSeeds[3];
                    uint tmp = address ^ (uint)(_hseeds[1] << 8);
                    address = ((BytesToHalf(tbl[GetByte(tmp, 2)], tbl[GetByte(tmp, 1)])
                                | (tmp & 0xFFFF0000)) ^ (uint)(_hseeds[2] << 4)) & DecryptMask;
                    tmp   = value ^ (uint)(_hseeds[1] << 9);
                    value = BytesToWord(tbl[GetByte(tmp, 4)], tbl[GetByte(tmp, 3)],
                                        tbl[GetByte(tmp, 2)], tbl[GetByte(tmp, 1)])
                            ^ (uint)(_hseeds[2] << 5);
                    break;
                }

                case 7: // seed-refresh — update the seed tables
                {
                    UpdateSeeds(address, value);
                    return false; // consume this line; do not emit as a code
                }
            }
            return true;
        }

        private bool Gs3Encrypt(ref uint address, ref uint value, byte requestedKey,
                                 ref bool lineTwo, ref byte xkey)
        {
            uint command = address & 0xFE000000;
            byte key = (requestedKey > 0 && requestedKey < 8)
                       ? requestedKey
                       : (byte)((address >> 25) & 0x7);

            if (lineTwo)
            {
                switch (xkey)
                {
                    case 1:
                    case 2:
                    {
                        var tbl = _encSeeds[0]; int size = TableSizes[0];
                        address ^= (uint)(_hseeds[2] << 3);
                        value   ^= (uint)_hseeds[2];
                        uint tmp = 0, tmp2 = 0;
                        for (int i = 0; i < size; i++)
                        {
                            uint flag = (i < 32)
                                ? ((value   >> i) & 1u)
                                : ((address >> (i - 32)) & 1u);
                            if (flag != 0)
                            {
                                if (tbl[i] < 32) tmp2 |= (1u << tbl[i]);
                                else             tmp  |= (1u << (tbl[i] - 32));
                            }
                        }
                        address = tmp  ^ (uint)(_hseeds[1] << 2);
                        value   = tmp2 ^ (uint)(_hseeds[1] << 13);
                        lineTwo = false;
                        return true;
                    }
                    case 3:
                    case 4:
                    {
                        var tbl = _encSeeds[3];
                        uint tmp = address ^ (uint)(_hseeds[2] << 16);
                        address = BytesToWord(tbl[GetByte(tmp, 4)], tbl[GetByte(tmp, 3)],
                                              tbl[GetByte(tmp, 2)], tbl[GetByte(tmp, 1)])
                                  ^ (uint)(_hseeds[1] << 3);
                        tmp   = value ^ (uint)(_hseeds[2] << 5);
                        value = BytesToWord(tbl[GetByte(tmp, 4)], tbl[GetByte(tmp, 3)],
                                            tbl[GetByte(tmp, 2)], tbl[GetByte(tmp, 1)])
                                ^ (uint)(_hseeds[1] << 9);
                        lineTwo = false;
                        return true;
                    }
                    default:
                        lineTwo = false;
                        return true;
                }
            }

            if (command >= 0x30000000 && command <= 0x6FFFFFFF)
            { lineTwo = true; xkey = key; }

            switch (key)
            {
                case 0: break;

                case 1:
                {
                    var tbl = _encSeeds[0]; int size = TableSizes[0];
                    uint tmp = (address & 0x01FFFFFF) ^ (uint)_hseeds[2];
                    uint mask = 0;
                    for (int i = 0; i < size; i++)
                        mask |= ((tmp & (1u << i)) >> i) << tbl[i];
                    address = (mask ^ (uint)(_hseeds[1] << 8)) | command;
                    break;
                }

                case 2:
                {
                    var tbl = _encSeeds[1]; int size = TableSizes[1];
                    address = (address ^ (uint)(_hseeds[2] << 8)) & 0x01FFFFFF;
                    uint tmp = 0, tmp2 = 0;
                    for (int i = 0; i < size; i++)
                    {
                        uint flag = (i < 32)
                            ? ((value   ^ (uint)_hseeds[2]) >> i) & 1u
                            : ((address ^ (uint)(_hseeds[2] << 8)) >> (i - 32)) & 1u;
                        if (flag != 0)
                        {
                            if (tbl[i] < 32) tmp2 |= (1u << tbl[i]);
                            else             tmp  |= (1u << (tbl[i] - 32));
                        }
                    }
                    address = (tmp ^ (uint)(_hseeds[1] << 16) | command);
                    value   = tmp2 ^ (uint)(_hseeds[1] << 1);
                    break;
                }

                case 3:
                {
                    var tbl = _encSeeds[3];
                    uint tmp = (address ^ (uint)(_hseeds[2] << 4)) & DecryptMask;
                    address = (BytesToHalf(tbl[GetByte(tmp, 1)], tbl[GetByte(tmp, 2)])
                               | (tmp & 0xFFFF0000)) ^ (uint)(_hseeds[1] << 8);
                    break;
                }

                case 4:
                {
                    var tbl = _encSeeds[3];
                    uint tmp = (address ^ (uint)(_hseeds[2] << 4)) & DecryptMask;
                    address = (BytesToHalf(tbl[GetByte(tmp, 1)], tbl[GetByte(tmp, 2)])
                               | (tmp & 0xFFFF0000)) ^ (uint)(_hseeds[1] << 8);
                    tmp   = value ^ (uint)(_hseeds[2] << 5);
                    value = BytesToWord(tbl[GetByte(tmp, 4)], tbl[GetByte(tmp, 3)],
                                        tbl[GetByte(tmp, 2)], tbl[GetByte(tmp, 1)])
                            ^ (uint)(_hseeds[1] << 9);
                    break;
                }
            }
            return true;
        }

        private void UpdateSeeds(uint addr, uint val)
        {
            ushort seed1 = (ushort)(addr       & 0xFFFF);
            ushort seed2 = (ushort)(addr >> 16);
            ushort seed3 = (ushort)(val        & 0xFFFF);
            _hseeds[0] = seed1; _hseeds[1] = seed2; _hseeds[2] = seed3;
            BuildSeeds(seed1, seed2, seed3);
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
            var result  = new List<RawCode>();
            bool lineTwo = false;
            byte xkey    = 0;

            foreach (var rawLine in input.Split('\n'))
            {
                var line = rawLine.Trim();
                var m = HexPair.Match(line);
                if (!m.Success) continue;

                uint addr = Convert.ToUInt32(m.Groups[1].Value, 16);
                uint val  = Convert.ToUInt32(m.Groups[2].Value, 16);

                if (Gs3Decrypt(ref addr, ref val, ref lineTwo, ref xkey))
                    result.Add(new RawCode(addr, val));
            }
            return result;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Encodes raw codes using encryption key type 0 (pass-through/raw), preserving the
        /// existing command byte.  To produce key-type-2 or key-type-4 encrypted codes,
        /// instantiate with the appropriate game seeds and call
        /// <see cref="EncodeWithKey"/> specifying the desired key type.
        /// </remarks>
        public string Encode(IEnumerable<RawCode> codes)
        {
            bool lineTwo = false;
            byte xkey    = 0;
            const byte encKey = 0;   // key 0 = raw pass-through; key indicator is already in the address
            var sb = new StringBuilder();

            foreach (var code in codes)
            {
                uint addr = code.Address;
                uint val  = code.Value;
                Gs3Encrypt(ref addr, ref val, encKey, ref lineTwo, ref xkey);
                sb.AppendLine($"{addr:X8} {val:X8}");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Encodes raw codes using a specific GS3 encryption key type (1–4).
        /// </summary>
        public string EncodeWithKey(IEnumerable<RawCode> codes, byte key)
        {
            if (key == 0 || key > 4) return Encode(codes);
            bool lineTwo = false;
            byte xkey    = 0;
            var sb = new StringBuilder();
            foreach (var code in codes)
            {
                uint addr = code.Address;
                uint val  = code.Value;
                Gs3Encrypt(ref addr, ref val, key, ref lineTwo, ref xkey);
                sb.AppendLine($"{addr:X8} {val:X8}");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
