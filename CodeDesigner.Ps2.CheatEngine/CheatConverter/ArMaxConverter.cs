using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeDesigner.Ps2.CheatEngine
{
    /// <summary>
    /// Converter for the <b>Action Replay MAX (ARMax)</b> PS2 cheat code format.
    /// <para>
    /// ARMax codes are alphanumeric strings in the form <c>XXXX-XXXX-XXXXX</c> (13 payload
    /// characters plus 2 separator dashes).  Each string encodes a single 64-bit code
    /// (address + value) using a 32-character alphabet, a 1-bit parity check, and a
    /// DES-based block cipher with a fixed key schedule.
    /// </para>
    /// <para>
    /// Ported to C# from <c>armax.c</c> in the Omniconvert project
    /// (Copyright © 2003–2004 Parasyte, © 2008 Pyriel; GPL v2).
    /// </para>
    /// </summary>
    public sealed class ArMaxConverter : ICheatCodeConverter
    {
        // -----------------------------------------------------------------------
        // Alphabet
        // -----------------------------------------------------------------------

        /// <summary>
        /// The 32-character ARMax alphabet (indices 0–31).
        /// Excludes visually ambiguous characters: I, L, O, S.
        /// </summary>
        private const string Filter = "0123456789ABCDEFGHJKMNPQRTUVWXYZ";

        // -----------------------------------------------------------------------
        // DES-like S-box tables  (identical to omniconvert armax.c)
        // -----------------------------------------------------------------------
        private static readonly uint[] Table0 = {
            0x01010400,0x00000000,0x00010000,0x01010404,0x01010004,0x00010404,0x00000004,0x00010000,
            0x00000400,0x01010400,0x01010404,0x00000400,0x01000404,0x01010004,0x01000000,0x00000004,
            0x00000404,0x01000400,0x01000400,0x00010400,0x00010400,0x01010000,0x01010000,0x01000404,
            0x00010004,0x01000004,0x01000004,0x00010004,0x00000000,0x00000404,0x00010404,0x01000000,
            0x00010000,0x01010404,0x00000004,0x01010000,0x01010400,0x01000000,0x01000000,0x00000400,
            0x01010004,0x00010000,0x00010400,0x01000004,0x00000400,0x00000004,0x01000404,0x00010404,
            0x01010404,0x00010004,0x01010000,0x01000404,0x01000004,0x00000404,0x00010404,0x01010400,
            0x00000404,0x01000400,0x01000400,0x00000000,0x00010004,0x00010400,0x00000000,0x01010004,
        };
        private static readonly uint[] Table1 = {
            0x80108020,0x80008000,0x00008000,0x00108020,0x00100000,0x00000020,0x80100020,0x80008020,
            0x80000020,0x80108020,0x80108000,0x80000000,0x80008000,0x00100000,0x00000020,0x80100020,
            0x00108000,0x00100020,0x80008020,0x00000000,0x80000000,0x00008000,0x00108020,0x80100000,
            0x00100020,0x80000020,0x00000000,0x00108000,0x00008020,0x80108000,0x80100000,0x00008020,
            0x00000000,0x00108020,0x80100020,0x00100000,0x80008020,0x80100000,0x80108000,0x00008000,
            0x80100000,0x80008000,0x00000020,0x80108020,0x00108020,0x00000020,0x00008000,0x80000000,
            0x00008020,0x80108000,0x00100000,0x80000020,0x00100020,0x80008020,0x80000020,0x00100020,
            0x00108000,0x00000000,0x80008000,0x00008020,0x80000000,0x80100020,0x80108020,0x00108000,
        };
        private static readonly uint[] Table2 = {
            0x00000208,0x08020200,0x00000000,0x08020008,0x08000200,0x00000000,0x00020208,0x08000200,
            0x00020008,0x08000008,0x08000008,0x00020000,0x08020208,0x00020008,0x08020000,0x00000208,
            0x08000000,0x00000008,0x08020200,0x00000200,0x00020200,0x08020000,0x08020008,0x00020208,
            0x08000208,0x00020200,0x00020000,0x08000208,0x00000008,0x08020208,0x00000200,0x08000000,
            0x08020200,0x08000000,0x00020008,0x00000208,0x00020000,0x08020200,0x08000200,0x00000000,
            0x00000200,0x00020008,0x08020208,0x08000200,0x08000008,0x00000200,0x00000000,0x08020008,
            0x08000208,0x00020000,0x08000000,0x08020208,0x00000008,0x00020208,0x00020200,0x08000008,
            0x08020000,0x08000208,0x00000208,0x08020000,0x00020208,0x00000008,0x08020008,0x00020200,
        };
        private static readonly uint[] Table3 = {
            0x00802001,0x00002081,0x00002081,0x00000080,0x00802080,0x00800081,0x00800001,0x00002001,
            0x00000000,0x00802000,0x00802000,0x00802081,0x00000081,0x00000000,0x00800080,0x00800001,
            0x00000001,0x00002000,0x00800000,0x00802001,0x00000080,0x00800000,0x00002001,0x00002080,
            0x00800081,0x00000001,0x00002080,0x00800080,0x00002000,0x00802080,0x00802081,0x00000081,
            0x00800080,0x00800001,0x00802000,0x00802081,0x00000081,0x00000000,0x00000000,0x00802000,
            0x00002080,0x00800080,0x00800081,0x00000001,0x00802001,0x00002081,0x00002081,0x00000080,
            0x00802081,0x00000081,0x00000001,0x00002000,0x00800001,0x00002001,0x00802080,0x00800081,
            0x00002001,0x00002080,0x00800000,0x00802001,0x00000080,0x00800000,0x00002000,0x00802080,
        };
        private static readonly uint[] Table4 = {
            0x00000100,0x02080100,0x02080000,0x42000100,0x00080000,0x00000100,0x40000000,0x02080000,
            0x40080100,0x00080000,0x02000100,0x40080100,0x42000100,0x42080000,0x00080100,0x40000000,
            0x02000000,0x40080000,0x40080000,0x00000000,0x40000100,0x42080100,0x42080100,0x02000100,
            0x42080000,0x40000100,0x00000000,0x42000000,0x02080100,0x02000000,0x42000000,0x00080100,
            0x00080000,0x42000100,0x00000100,0x02000000,0x40000000,0x02080000,0x42000100,0x40080100,
            0x02000100,0x40000000,0x42080000,0x02080100,0x40080100,0x00000100,0x02000000,0x42080000,
            0x42080100,0x00080100,0x42000000,0x42080100,0x02080000,0x00000000,0x40080000,0x42000000,
            0x00080100,0x02000100,0x40000100,0x00080000,0x00000000,0x40080000,0x02080100,0x40000100,
        };
        private static readonly uint[] Table5 = {
            0x20000010,0x20400000,0x00004000,0x20404010,0x20400000,0x00000010,0x20404010,0x00400000,
            0x20004000,0x00404010,0x00400000,0x20000010,0x00400010,0x20004000,0x20000000,0x00004010,
            0x00000000,0x00400010,0x20004010,0x00004000,0x00404000,0x20004010,0x00000010,0x20400010,
            0x20400010,0x00000000,0x00404010,0x20404000,0x00004010,0x00404000,0x20404000,0x20000000,
            0x20004000,0x00000010,0x20400010,0x00404000,0x20404010,0x00400000,0x00004010,0x20000010,
            0x00400000,0x20004000,0x20000000,0x00004010,0x20000010,0x20404010,0x00404000,0x20400000,
            0x00404010,0x20404000,0x00000000,0x20400010,0x00000010,0x00004000,0x20400000,0x00404010,
            0x00004000,0x00400010,0x20004010,0x00000000,0x20404000,0x20000000,0x00400010,0x20004010,
        };
        private static readonly uint[] Table6 = {
            0x00200000,0x04200002,0x04000802,0x00000000,0x00000800,0x04000802,0x00200802,0x04200800,
            0x04200802,0x00200000,0x00000000,0x04000002,0x00000002,0x04000000,0x04200002,0x00000802,
            0x04000800,0x00200802,0x00200002,0x04000800,0x04000002,0x04200000,0x04200800,0x00200002,
            0x04200000,0x00000800,0x00000802,0x04200802,0x00200800,0x00000002,0x04000000,0x00200800,
            0x04000000,0x00200800,0x00200000,0x04000802,0x04000802,0x04200002,0x04200002,0x00000002,
            0x00200002,0x04000000,0x04000800,0x00200000,0x04200800,0x00000802,0x00200802,0x04200800,
            0x00000802,0x04000002,0x04200802,0x04200000,0x00200800,0x00000000,0x00000002,0x04200802,
            0x00000000,0x00200802,0x04200000,0x00000800,0x04000002,0x04000800,0x00000800,0x00200002,
        };
        private static readonly uint[] Table7 = {
            0x10001040,0x00001000,0x00040000,0x10041040,0x10000000,0x10001040,0x00000040,0x10000000,
            0x00040040,0x10040000,0x10041040,0x00041000,0x10041000,0x00041040,0x00001000,0x00000040,
            0x10040000,0x10000040,0x10001000,0x00001040,0x00041000,0x00040040,0x10040040,0x10041000,
            0x00001040,0x00000000,0x00000000,0x10040040,0x10000040,0x10001000,0x00041040,0x00040000,
            0x00041040,0x00040000,0x10041000,0x00001000,0x00000040,0x10040040,0x00001000,0x00041040,
            0x10001000,0x00000040,0x10000040,0x10040000,0x10040040,0x10000000,0x00040000,0x10001040,
            0x00000000,0x10041040,0x00040040,0x10000040,0x10040000,0x10001000,0x10001040,0x00000000,
            0x10041040,0x00041000,0x00041000,0x00001040,0x00001040,0x00040040,0x10000000,0x10041000,
        };

        // -----------------------------------------------------------------------
        // Seed generation (DES key schedule) — ported from generateseeds()
        // -----------------------------------------------------------------------

        private static readonly byte[] GenTable0 = {
            0x39,0x31,0x29,0x21,0x19,0x11,0x09,0x01,
            0x3A,0x32,0x2A,0x22,0x1A,0x12,0x0A,0x02,
            0x3B,0x33,0x2B,0x23,0x1B,0x13,0x0B,0x03,
            0x3C,0x34,0x2C,0x24,0x3F,0x37,0x2F,0x27,
            0x1F,0x17,0x0F,0x07,0x3E,0x36,0x2E,0x26,
            0x1E,0x16,0x0E,0x06,0x3D,0x35,0x2D,0x25,
            0x1D,0x15,0x0D,0x05,0x1C,0x14,0x0C,0x04,
        };
        private static readonly byte[] GenTable1 = { 0x80,0x40,0x20,0x10,0x08,0x04,0x02,0x01 };
        private static readonly byte[] GenTable2 = {
            0x01,0x02,0x04,0x06,0x08,0x0A,0x0C,0x0E,
            0x0F,0x11,0x13,0x15,0x17,0x19,0x1B,0x1C,
        };
        private static readonly byte[] GenTable3 = {
            0x0E,0x11,0x0B,0x18,0x01,0x05,0x03,0x1C,
            0x0F,0x06,0x15,0x0A,0x17,0x13,0x0C,0x04,
            0x1A,0x08,0x10,0x07,0x1B,0x14,0x0D,0x02,
            0x29,0x34,0x1F,0x25,0x2F,0x37,0x1E,0x28,
            0x33,0x2D,0x21,0x30,0x2C,0x31,0x27,0x38,
            0x22,0x35,0x2E,0x2A,0x32,0x24,0x1D,0x20,
        };
        private static readonly byte[] GenSubTable = { 0x1D,0x2E,0x7A,0x85,0x3F,0xAB,0xD9,0x46 };

        // Pre-computed seeds shared across all instances (constant, key-independent)
        private static readonly uint[] GlobalSeeds = BuildSeeds();

        private static uint[] BuildSeeds()
        {
            var seeds = new uint[0x20];
            GenerateSeeds(seeds, GenSubTable, false);
            return seeds;
        }

        /// <summary>Ported from <c>generateseeds()</c> in armax.c.</summary>
        private static void GenerateSeeds(uint[] seeds, byte[] seedTable, bool doReverse)
        {
            var array0 = new byte[0x38];
            var array1 = new byte[0x38];
            var array2 = new byte[0x08];

            // PC-1: apply gentable0 permutation to the 8-byte seed table, producing 56 key bits.
            for (int i = 0; i < 0x38; i++)
            {
                int t = GenTable0[i] - 1;
                // array0[i] = 1 if bit (t&7) of seedTable[t>>3] is set, else 0.
                // GenTable1[t&7] = 0x80>>((t&7)) which gives the bitmask for that bit.
                array0[i] = (byte)(((seedTable[t >> 3] & GenTable1[t & 7]) != 0) ? 1 : 0);
            }

            // Generate 16 subkeys using cumulative left rotations + PC-2 permutation.
            for (int i = 0; i < 0x10; i++)
            {
                Array.Clear(array2, 0, 8);
                int rot = GenTable2[i]; // cumulative rotation amount for this round

                // Rotate the 56-bit key, treating the first 28 bits and the last 28 bits
                // as two independent circular shift registers.
                for (int j = 0; j < 0x38; j++)
                {
                    int t = rot + j;
                    if (j > 0x1B)          // second 28-bit half (j = 28..55)
                    {
                        if (t > 0x37) t -= 0x1C; // wrap within 28..55
                    }
                    else                   // first 28-bit half (j = 0..27)
                    {
                        if (t > 0x1B) t -= 0x1C; // wrap within 0..27
                    }
                    array1[j] = array0[t];
                }

                // PC-2: select 48 bits from the rotated key and pack into 8 groups of 6 bits.
                for (int j = 0; j < 0x30; j++)
                {
                    if (array1[GenTable3[j] - 1] == 0) continue;
                    // 0x2AAB = ceil(65536/6); (j * 0x2AAB) >> 16 is a branchless integer division by 6.
                    int grp = (j * 0x2AAB) >> 16; // group index: j / 6  (which 6-bit byte of array2)
                    int bit = j - grp * 6;         // bit index:   j % 6  (which bit within that byte)
                    // GenTable1[bit] >> 2  =  { 0x20,0x10,0x08,0x04,0x02,0x01 }[bit]
                    array2[grp] |= (byte)(GenTable1[bit] >> 2);
                }

                // Pack the 8 six-bit groups into two 32-bit subkeys, interleaving even/odd bytes.
                seeds[i * 2]     = ((uint)array2[0] << 24) | ((uint)array2[2] << 16) |
                                   ((uint)array2[4] << 8)  | array2[6];
                seeds[i * 2 + 1] = ((uint)array2[1] << 24) | ((uint)array2[3] << 16) |
                                   ((uint)array2[5] << 8)  | array2[7];
            }

            // When doReverse is false, reverse the subkey pairs so that iterating
            // the array forward performs decryption (standard ARMax decrypt order).
            if (!doReverse)
            {
                int jj = 0x1F;
                for (int i = 0; i < 16; i += 2)
                {
                    uint t3       = seeds[i];
                    seeds[i]      = seeds[jj - 1];
                    seeds[jj - 1] = t3;

                    t3            = seeds[i + 1];
                    seeds[i + 1]  = seeds[jj];
                    seeds[jj]     = t3;
                    jj -= 2;
                }
            }
        }

        // -----------------------------------------------------------------------
        // Bit-manipulation helpers
        // -----------------------------------------------------------------------

        private static uint RotateLeft(uint v, int n)  => (v << n)  | (v >> (32 - n));
        private static uint RotateRight(uint v, int n) => (v >> n)  | (v << (32 - n));
        private static uint ByteSwap(uint v) =>
            ((v & 0xFF000000) >> 24) | ((v & 0x00FF0000) >> 8) |
            ((v & 0x0000FF00) << 8)  | ((v & 0x000000FF) << 24);

        // -----------------------------------------------------------------------
        // DES cipher — Unscramble / Scramble (IP and IP-inverse permutations)
        // -----------------------------------------------------------------------

        private static void Unscramble1(ref uint addr, ref uint val)
        {
            val = RotateLeft(val, 4);
            uint tmp = (addr ^ val) & 0xF0F0F0F0;
            addr ^= tmp; val = RotateRight(val ^ tmp, 0x14);
            tmp = (addr ^ val) & 0xFFFF0000;
            addr ^= tmp; val = RotateRight(val ^ tmp, 0x12);
            tmp = (addr ^ val) & 0x33333333;
            addr ^= tmp; val = RotateRight(val ^ tmp, 6);
            tmp = (addr ^ val) & 0x00FF00FF;
            addr ^= tmp; val = RotateLeft(val ^ tmp, 9);
            tmp = (addr ^ val) & 0xAAAAAAAA;
            addr = RotateLeft(addr ^ tmp, 1); val ^= tmp;
        }

        private static void Unscramble2(ref uint addr, ref uint val)
        {
            val = RotateRight(val, 1);
            uint tmp = (addr ^ val) & 0xAAAAAAAA;
            val ^= tmp; addr = RotateRight(addr ^ tmp, 9);
            tmp = (addr ^ val) & 0x00FF00FF;
            val ^= tmp; addr = RotateLeft(addr ^ tmp, 6);
            tmp = (addr ^ val) & 0x33333333;
            val ^= tmp; addr = RotateLeft(addr ^ tmp, 0x12);
            tmp = (addr ^ val) & 0xFFFF0000;
            val ^= tmp; addr = RotateLeft(addr ^ tmp, 0x14);
            tmp = (addr ^ val) & 0xF0F0F0F0;
            val ^= tmp; addr = RotateRight(addr ^ tmp, 4);
        }

        private static void Scramble1(ref uint addr, ref uint val)
        {
            addr = RotateLeft(addr, 4);
            uint tmp = (addr ^ val) & 0xF0F0F0F0;
            val ^= tmp; addr = RotateRight(addr ^ tmp, 0x14);
            tmp = (addr ^ val) & 0xFFFF0000;
            val ^= tmp; addr = RotateRight(addr ^ tmp, 0x12);
            tmp = (addr ^ val) & 0x33333333;
            val ^= tmp; addr = RotateRight(addr ^ tmp, 6);
            tmp = (addr ^ val) & 0x00FF00FF;
            val ^= tmp; addr = RotateLeft(addr ^ tmp, 9);
            tmp = (addr ^ val) & 0xAAAAAAAA;
            val = RotateLeft(val ^ tmp, 1); addr ^= tmp;
        }

        private static void Scramble2(ref uint addr, ref uint val)
        {
            addr = RotateRight(addr, 1);
            uint tmp = (addr ^ val) & 0xAAAAAAAA;
            addr ^= tmp; val = RotateRight(val ^ tmp, 9);
            tmp = (addr ^ val) & 0x00FF00FF;
            addr ^= tmp; val = RotateLeft(val ^ tmp, 6);
            tmp = (addr ^ val) & 0x33333333;
            addr ^= tmp; val = RotateLeft(val ^ tmp, 0x12);
            tmp = (addr ^ val) & 0xFFFF0000;
            addr ^= tmp; val = RotateLeft(val ^ tmp, 0x14);
            tmp = (addr ^ val) & 0xF0F0F0F0;
            addr ^= tmp; val = RotateRight(val ^ tmp, 4);
        }

        // -----------------------------------------------------------------------
        // Core 16-round DES-like cipher
        // -----------------------------------------------------------------------

        private static uint DESRound(uint l, uint r, uint sk1, uint sk2)
        {
            uint t  = RotateRight(r, 4) ^ sk1;
            uint t2 = r ^ sk2;
            return l ^ (Table6[t  & 0x3F] ^ Table4[(t  >> 8) & 0x3F] ^
                        Table2[(t  >> 16) & 0x3F] ^ Table0[(t  >> 24) & 0x3F] ^
                        Table7[t2 & 0x3F] ^ Table5[(t2 >> 8) & 0x3F] ^
                        Table3[(t2 >> 16) & 0x3F] ^ Table1[(t2 >> 24) & 0x3F]);
        }

        /// <summary>
        /// Decrypts one 64-bit code word in-place.  <paramref name="code"/> is
        /// a 2-element array: [0] = word0, [1] = word1.
        /// </summary>
        private static void DecryptCode(uint[] code)
        {
            // getcode: addr = byteswap(code[0]), val = byteswap(code[1])
            uint addr = ByteSwap(code[0]);
            uint val  = ByteSwap(code[1]);

            Unscramble1(ref addr, ref val);
            for (int i = 0; i < 32; i += 4)
            {
                addr = DESRound(addr, val,  GlobalSeeds[i],     GlobalSeeds[i + 1]);
                val  = DESRound(val,  addr, GlobalSeeds[i + 2], GlobalSeeds[i + 3]);
            }
            Unscramble2(ref addr, ref val);

            // setcode(code, val, addr) — note swapped order
            code[0] = ByteSwap(val);
            code[1] = ByteSwap(addr);
        }

        /// <summary>Encrypts one 64-bit code word in-place.</summary>
        private static void EncryptCode(uint[] code)
        {
            // getcode for encrypt: getcode(code, &val, &addr)
            uint val  = ByteSwap(code[0]);
            uint addr = ByteSwap(code[1]);

            Scramble1(ref addr, ref val);
            for (int i = 31; i >= 0; i -= 4)
            {
                val  = DESRound(val,  addr, GlobalSeeds[i - 1], GlobalSeeds[i]);
                addr = DESRound(addr, val,  GlobalSeeds[i - 3], GlobalSeeds[i - 2]);
            }
            Scramble2(ref addr, ref val);

            // setcode(code, addr, val)
            code[0] = ByteSwap(addr);
            code[1] = ByteSwap(val);
        }

        // -----------------------------------------------------------------------
        // Alpha ↔ binary encoding
        // -----------------------------------------------------------------------

        private static int GetVal(char c) => Filter.IndexOf(char.ToUpperInvariant(c));

        /// <summary>
        /// Converts a 13-character ARMax code string (no dashes) into two uint32 words.
        /// Returns <c>false</c> if any character is invalid or the parity check fails.
        /// </summary>
        private static bool AlphaToBin(string alpha, out uint w0, out uint w1)
        {
            w0 = 0; w1 = 0;

            if (alpha.Length != 13) return false;

            for (int i = 0; i < 13; i++)
            {
                if (GetVal(alpha[i]) < 0) return false;
            }

            // First word: chars 0–5 supply 5 bits each into bits 31..2, char 6 top 2 bits → bits 1..0
            for (int i = 0; i < 6; i++)
                w0 |= (uint)(GetVal(alpha[i]) << ((5 - i) * 5 + 2));
            w0 |= (uint)(GetVal(alpha[6]) >> 3);

            // Second word: char 6 bottom 3 bits → bits 31..29, chars 7–11 → bits 28..4, char 12 top 4 bits → bits 3..0
            for (int i = 0; i < 6; i++)
                w1 |= (uint)(GetVal(alpha[i + 6]) << ((5 - i) * 5 + 4));
            w1 |= (uint)(GetVal(alpha[12]) >> 1);

            // Verify parity bit (bit 0 of alpha[12])
            byte parity = 0;
            uint tmp0 = w0, tmp1 = w1;
            for (int i = 0; i < 32; i++) { parity ^= (byte)(tmp0 >> i); }
            for (int i = 0; i < 32; i++) { parity ^= (byte)(tmp1 >> i); }
            parity &= 1;

            return parity == (GetVal(alpha[12]) & 1);
        }

        /// <summary>
        /// Converts two uint32 words into a 15-character ARMax string (<c>XXXX-XXXX-XXXXX</c>).
        /// </summary>
        private static string BinToAlpha(uint w0, uint w1)
        {
            var buf = new char[15];

            // Chars 0–3 from w0 bits 31..12
            buf[0] = Filter[(int)((w0 >> 27) & 0x1F)];
            buf[1] = Filter[(int)((w0 >> 22) & 0x1F)];
            buf[2] = Filter[(int)((w0 >> 17) & 0x1F)];
            buf[3] = Filter[(int)((w0 >> 12) & 0x1F)];
            buf[4] = '-';
            // Chars 4–5 from w0 bits 11..2
            buf[5] = Filter[(int)((w0 >> 7) & 0x1F)];
            buf[6] = Filter[(int)((w0 >> 2) & 0x1F)];
            // Char 6 bridges w0 bits 1..0 and w1 bits 31..29
            buf[7] = Filter[(int)(((w0 << 3) | (w1 >> 29)) & 0x1F)];
            // Char 7 from w1 bits 28..24
            buf[8] = Filter[(int)((w1 >> 24) & 0x1F)];
            buf[9] = '-';
            // Chars 8–11 from w1 bits 23..4
            buf[10] = Filter[(int)((w1 >> 19) & 0x1F)];
            buf[11] = Filter[(int)((w1 >> 14) & 0x1F)];
            buf[12] = Filter[(int)((w1 >> 9)  & 0x1F)];
            buf[13] = Filter[(int)((w1 >> 4)  & 0x1F)];

            // Char 12: w1 bits 3..0 in upper nibble, parity in bit 0
            byte parity = 0;
            for (int i = 0; i < 32; i++) parity ^= (byte)(w0 >> i);
            for (int i = 0; i < 32; i++) parity ^= (byte)(w1 >> i);
            buf[14] = Filter[(int)(((w1 << 1) & 0x1E) | (parity & 1u))];

            return new string(buf);
        }

        // -----------------------------------------------------------------------
        // CRC-16 verifier used in batch decrypt/encrypt
        // -----------------------------------------------------------------------

        private static readonly ushort[] CrcTable0 = {
            0x0000,0x1081,0x2102,0x3183,0x4204,0x5285,0x6306,0x7387,
            0x8408,0x9489,0xA50A,0xB58B,0xC60C,0xD68D,0xE70E,0xF78F,
        };
        private static readonly ushort[] CrcTable1 = {
            0x0000,0x1189,0x2312,0x329B,0x4624,0x57AD,0x6536,0x74BF,
            0x8C48,0x9DC1,0xAF5A,0xBED3,0xCA6C,0xDBE5,0xE97E,0xF8F7,
        };

        private static ushort GenCrc16(uint[] codes, int count)
        {
            ushort ret = 0;
            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    byte b = (byte)((codes[i] >> (j * 8)) ^ ret);
                    ret = (ushort)((CrcTable0[(b >> 4) & 0x0F] ^ CrcTable1[b & 0x0F]) ^ (ret >> 8));
                }
            }
            return ret;
        }

        private static byte VerifyCode(uint[] codes, int count)
        {
            ushort crc = GenCrc16(codes, count);
            return (byte)(((crc >> 12) ^ (crc >> 8) ^ (crc >> 4) ^ crc) & 0x0F);
        }

        // -----------------------------------------------------------------------
        // Batch decrypt / encrypt
        // -----------------------------------------------------------------------

        /// <summary>
        /// Decrypts a flat array of word pairs in-place and validates the CRC nibble.
        /// Returns <c>false</c> if the CRC check fails.
        /// </summary>
        private static bool BatchDecrypt(uint[] words)
        {
            int pairs = words.Length / 2;
            var tmp = new uint[2];
            for (int i = 0; i < pairs; i++)
            {
                tmp[0] = words[i * 2];
                tmp[1] = words[i * 2 + 1];
                DecryptCode(tmp);
                words[i * 2]     = tmp[0];
                words[i * 2 + 1] = tmp[1];
            }

            // Verify CRC nibble embedded in the top 4 bits of the first word
            uint savedTop = words[0];
            words[0] &= 0x0FFFFFFF;
            byte crc = VerifyCode(words, words.Length);
            bool ok = ((savedTop >> 28) == crc);
            words[0] = savedTop & 0x0FFFFFFF; // keep CRC stripped for callers
            return ok;
        }

        /// <summary>
        /// Encrypts a flat array of word pairs in-place, embedding the CRC nibble.
        /// Returns <c>false</c> if the top 4 bits of the first word are already non-zero.
        /// </summary>
        private static bool BatchEncrypt(uint[] words)
        {
            if ((words[0] >> 28) != 0) return false;

            words[0] |= (uint)(VerifyCode(words, words.Length) << 28);

            int pairs = words.Length / 2;
            var tmp = new uint[2];
            for (int i = 0; i < pairs; i++)
            {
                tmp[0] = words[i * 2];
                tmp[1] = words[i * 2 + 1];
                EncryptCode(tmp);
                words[i * 2]     = tmp[0];
                words[i * 2 + 1] = tmp[1];
            }
            return true;
        }

        // -----------------------------------------------------------------------
        // Regex for detecting ARMax code lines
        // -----------------------------------------------------------------------

        private static readonly Regex ArMaxLine =
            new Regex(@"^[0-9A-HJ-KM-NP-RT-Z]{4}-[0-9A-HJ-KM-NP-RT-Z]{4}-[0-9A-HJ-KM-NP-RT-Z]{5}$",
                      RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // -----------------------------------------------------------------------
        // ICheatCodeConverter implementation
        // -----------------------------------------------------------------------

        /// <summary>
        /// When <c>true</c>, enables the ARMax batch-block CRC mechanism used by real
        /// ARMax cheat devices: a 4-bit CCITT-derived CRC is embedded in the top nibble
        /// of the first decrypted word.  This limits the first code's value to having a
        /// zero low nibble (<c>val &amp; 0xF == 0</c>) because those bits are overwritten.
        /// <para>
        /// Set to <c>false</c> (the default) to use plain per-pair DES encryption,
        /// which round-trips any arbitrary address/value pair without data loss.
        /// </para>
        /// </summary>
        public bool BlockMode { get; init; } = false;

        /// <inheritdoc/>
        public IReadOnlyList<RawCode> Decode(string input)
        {
            var alphas = new List<string>();
            foreach (var rawLine in input.Split('\n'))
            {
                var line = rawLine.Trim().ToUpperInvariant();
                if (line.Length == 0 || line[0] == '#') continue;
                if (IsArMaxLine(line))
                    alphas.Add(RemoveDashes(line));
            }

            if (alphas.Count == 0)
                return Array.Empty<RawCode>();

            // Decode alpha strings to raw uint32 pairs
            var words = new uint[alphas.Count * 2];
            for (int i = 0; i < alphas.Count; i++)
            {
                if (!AlphaToBin(alphas[i], out uint w0, out uint w1))
                    return Array.Empty<RawCode>(); // invalid character or bad parity
                words[i * 2]     = w0;
                words[i * 2 + 1] = w1;
            }

            if (BlockMode)
            {
                // Full ARMax block decrypt with CRC verification and stripping.
                BatchDecrypt(words);
            }
            else
            {
                // Per-pair decrypt: no CRC layer, preserves all 64 bits.
                var tmp = new uint[2];
                for (int i = 0; i < alphas.Count; i++)
                {
                    tmp[0] = words[i * 2];
                    tmp[1] = words[i * 2 + 1];
                    DecryptCode(tmp);
                    words[i * 2]     = tmp[0];
                    words[i * 2 + 1] = tmp[1];
                }
            }

            // After DecryptCode: words[i*2] = byteswap(val), words[i*2+1] = byteswap(addr)
            var result = new List<RawCode>(alphas.Count);
            for (int i = 0; i < alphas.Count; i++)
            {
                uint addr = ByteSwap(words[i * 2 + 1]);
                uint val  = ByteSwap(words[i * 2]);
                result.Add(new RawCode(addr, val));
            }
            return result;
        }

        /// <inheritdoc/>
        public string Encode(IEnumerable<RawCode> codes)
        {
            var list = new List<RawCode>(codes);
            if (list.Count == 0) return string.Empty;

            // Build word pairs in the pre-decryption layout:
            //   words[i*2] = byteswap(val), words[i*2+1] = byteswap(addr)
            var words = new uint[list.Count * 2];
            for (int i = 0; i < list.Count; i++)
            {
                words[i * 2]     = ByteSwap(list[i].Value);
                words[i * 2 + 1] = ByteSwap(list[i].Address);
            }

            if (BlockMode)
            {
                // Full ARMax block encrypt with CRC embedding.
                // Note: requires (words[0] & 0xF0000000) == 0, i.e. (val & 0x0F) == 0.
                words[0] &= 0x0FFFFFFF;   // clear top nibble to make room for CRC
                BatchEncrypt(words);
            }
            else
            {
                // Per-pair encrypt: no CRC, round-trips any value losslessly.
                var tmp = new uint[2];
                for (int i = 0; i < list.Count; i++)
                {
                    tmp[0] = words[i * 2];
                    tmp[1] = words[i * 2 + 1];
                    EncryptCode(tmp);
                    words[i * 2]     = tmp[0];
                    words[i * 2 + 1] = tmp[1];
                }
            }

            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
                sb.AppendLine(BinToAlpha(words[i * 2], words[i * 2 + 1]));
            return sb.ToString().TrimEnd();
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static string RemoveDashes(string s)
        {
            var sb = new StringBuilder(13);
            foreach (char c in s)
                if (c != '-') sb.Append(c);
            return sb.ToString();
        }

        private static bool IsArMaxLine(string s)
        {
            if (s.Length != 15) return false;
            if (s[4] != '-' || s[9] != '-') return false;
            foreach (char c in s)
                if (c != '-' && Filter.IndexOf(c) < 0) return false;
            return true;
        }
    }
}
