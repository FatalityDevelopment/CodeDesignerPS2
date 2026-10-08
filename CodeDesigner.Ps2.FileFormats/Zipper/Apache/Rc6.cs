using System;

namespace CodeDesigner.Ps2.FileFormats.Zipper.Apache
{
    /// <summary>
    /// RC6-32/20/16 block cipher (Rivest et al.), the real block cipher used by SOCOM II's
    /// APACHE00.ZDB loader. Confirmed by live PCSX2 trace: the loader's "AES_CBC_Core"
    /// (EE 0x0053FC60) looks a cipher up by the name "rc6" in a libtomcrypt-style registry
    /// (EE 0x00562190) with 20 rounds and a 16-byte key; the unrolled block routines live at
    /// EE 0x00527C20 (decrypt) and 0x005273A0 (encrypt), key setup at 0x005286F0.
    ///
    /// This is a clean-room BCL-only implementation used for READ-ONLY decryption and
    /// verification of the on-disc format. Block = 16 bytes, four little-endian 32-bit words.
    /// Verified against Rivest's published test vector (zero key, zero block ->
    /// 8F C3 A5 36 56 B1 F7 78 C1 29 DF 4E 98 48 A4 1E) and against live key schedules
    /// captured from EE RAM.
    /// </summary>
    public sealed class Rc6
    {
        private const uint P32 = 0xB7E15163;
        private const uint Q32 = 0x9E3779B9;
        private const int Rounds = 20;

        private readonly uint[] _s; // round-key schedule, 2*Rounds+4 words

        public Rc6(ReadOnlySpan<byte> key)
        {
            if (key.Length != 16)
                throw new ArgumentException("RC6 key must be 16 bytes.", nameof(key));
            _s = KeySchedule(key);
        }

        private static uint Rol(uint x, int n) { n &= 31; return (x << n) | (x >> (32 - n)); }
        private static uint Ror(uint x, int n) { n &= 31; return (x >> n) | (x << (32 - n)); }

        private static uint[] KeySchedule(ReadOnlySpan<byte> key)
        {
            int c = key.Length / 4;
            var l = new uint[c];
            for (int i = 0; i < c; i++)
                l[i] = BitConverter.ToUInt32(key.Slice(i * 4, 4));

            int t = 2 * Rounds + 4;
            var s = new uint[t];
            s[0] = P32;
            for (int i = 1; i < t; i++) s[i] = s[i - 1] + Q32;

            uint a = 0, b = 0;
            int ii = 0, jj = 0;
            int iters = 3 * Math.Max(c, t);
            for (int k = 0; k < iters; k++)
            {
                a = s[ii] = Rol(s[ii] + a + b, 3);
                b = l[jj] = Rol(l[jj] + a + b, (int)(a + b));
                ii = (ii + 1) % t;
                jj = (jj + 1) % c;
            }
            return s;
        }

        /// <summary>Decrypts one 16-byte block in place-free fashion; returns a new 16-byte array.</summary>
        public byte[] DecryptBlock(ReadOnlySpan<byte> block)
        {
            if (block.Length != 16) throw new ArgumentException("Block must be 16 bytes.", nameof(block));
            uint a = BitConverter.ToUInt32(block.Slice(0, 4));
            uint b = BitConverter.ToUInt32(block.Slice(4, 4));
            uint cc = BitConverter.ToUInt32(block.Slice(8, 4));
            uint d = BitConverter.ToUInt32(block.Slice(12, 4));

            cc -= _s[2 * Rounds + 3];
            a -= _s[2 * Rounds + 2];
            for (int i = Rounds; i >= 1; i--)
            {
                (a, b, cc, d) = (d, a, b, cc);
                uint u = Rol(d * (2 * d + 1), 5);
                uint tt = Rol(b * (2 * b + 1), 5);
                cc = Ror(cc - _s[2 * i + 1], (int)tt) ^ u;
                a = Ror(a - _s[2 * i], (int)u) ^ tt;
            }
            d -= _s[1];
            b -= _s[0];
            return Pack(a, b, cc, d);
        }

        /// <summary>Encrypts one 16-byte block; returns a new 16-byte array.</summary>
        public byte[] EncryptBlock(ReadOnlySpan<byte> block)
        {
            if (block.Length != 16) throw new ArgumentException("Block must be 16 bytes.", nameof(block));
            uint a = BitConverter.ToUInt32(block.Slice(0, 4));
            uint b = BitConverter.ToUInt32(block.Slice(4, 4));
            uint cc = BitConverter.ToUInt32(block.Slice(8, 4));
            uint d = BitConverter.ToUInt32(block.Slice(12, 4));

            b += _s[0];
            d += _s[1];
            for (int i = 1; i <= Rounds; i++)
            {
                uint tt = Rol(b * (2 * b + 1), 5);
                uint u = Rol(d * (2 * d + 1), 5);
                a = Rol(a ^ tt, (int)u) + _s[2 * i];
                cc = Rol(cc ^ u, (int)tt) + _s[2 * i + 1];
                (a, b, cc, d) = (b, cc, d, a);
            }
            a += _s[2 * Rounds + 2];
            cc += _s[2 * Rounds + 3];
            return Pack(a, b, cc, d);
        }

        /// <summary>CBC decrypt with no padding. Length must be a multiple of 16.</summary>
        public byte[] DecryptCbc(ReadOnlySpan<byte> data, ReadOnlySpan<byte> iv)
        {
            if (data.Length % 16 != 0) throw new ArgumentException("CBC length must be a multiple of 16.", nameof(data));
            var outBuf = new byte[data.Length];
            byte[] prev = iv.ToArray();
            for (int off = 0; off < data.Length; off += 16)
            {
                byte[] dec = DecryptBlock(data.Slice(off, 16));
                for (int i = 0; i < 16; i++) outBuf[off + i] = (byte)(dec[i] ^ prev[i]);
                prev = data.Slice(off, 16).ToArray();
            }
            return outBuf;
        }

        /// <summary>
        /// CBC-MAC: runs CBC encryption and returns the final ciphertext block.
        /// Used to derive the FTScore "layer 5" IV (stub @0x5360C8).
        /// </summary>
        public byte[] CbcMac(ReadOnlySpan<byte> data, ReadOnlySpan<byte> iv)
        {
            if (data.Length % 16 != 0) throw new ArgumentException("CBC-MAC length must be a multiple of 16.", nameof(data));
            byte[] prev = iv.ToArray();
            for (int off = 0; off < data.Length; off += 16)
            {
                var x = new byte[16];
                for (int i = 0; i < 16; i++) x[i] = (byte)(data[off + i] ^ prev[i]);
                prev = EncryptBlock(x);
            }
            return prev;
        }

        private static byte[] Pack(uint a, uint b, uint c, uint d)
        {
            var o = new byte[16];
            BitConverter.TryWriteBytes(o.AsSpan(0, 4), a);
            BitConverter.TryWriteBytes(o.AsSpan(4, 4), b);
            BitConverter.TryWriteBytes(o.AsSpan(8, 4), c);
            BitConverter.TryWriteBytes(o.AsSpan(12, 4), d);
            return o;
        }
    }
}
