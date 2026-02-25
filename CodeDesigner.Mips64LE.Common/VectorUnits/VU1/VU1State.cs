using System;
using System.Numerics;

namespace CodeDesigner.Mips64LE.Common.VectorUnits.VU1
{
    /// <summary>
    /// Vector Unit 1 (VU1) state for the PlayStation 2 Emotion Engine.
    /// VU1 runs standalone microprograms uploaded via VIF1/DMAC and feeds
    /// geometry data to the GIF via the XGKICK instruction (PATH1).
    /// Micro memory: 16 KB.  Data memory: 16 KB.
    /// </summary>
    public sealed class VU1State
    {
        /// <summary>
        /// Vector Float Registers (32 × 128-bit).
        /// vf0 is hardwired to (0, 0, 0, 1).
        /// </summary>
        public Vector4[] VF { get; } = new Vector4[32];

        /// <summary>
        /// Vector Integer Registers (16 × 16-bit).
        /// vi0 is hardwired to 0.
        /// </summary>
        public ushort[] VI { get; } = new ushort[16];

        /// <summary>Accumulator Register (128-bit) for multiply-accumulate ops.</summary>
        public Vector4 ACC { get; set; }

        /// <summary>Q Register — holds division/reciprocal results.</summary>
        public float Q { get; set; }

        /// <summary>P Register — holds EFU results (sqrt, acos, …).</summary>
        public float P { get; set; }

        /// <summary>
        /// LFSR RNG state stored as <c>0x3F800000 | mantissa</c> (float in [1, 2)).
        /// </summary>
        public uint RngState { get; set; }

        /// <summary>I Register — float immediate loaded by LOI lower-word encoding.</summary>
        public float I { get; set; }

        /// <summary>Status flags register.</summary>
        public uint StatusFlags { get; set; }

        /// <summary>
        /// MAC flags register.
        /// Bits [3:0] = zero flags (w,z,y,x); [7:4] = sign; [11:8] = overflow; [15:12] = underflow.
        /// </summary>
        public ushort MACFlags { get; set; }

        /// <summary>Clipping flags register (24-bit, updated by CLIP instruction).</summary>
        public uint ClipFlags { get; set; }

        /// <summary>VU1 Micro Memory — 16 KB for microprogram instructions.</summary>
        public byte[] MicroMem { get; } = new byte[16 * 1024];

        /// <summary>VU1 Data Memory — 16 KB for vertex/matrix data.</summary>
        public byte[] DataMem { get; } = new byte[16 * 1024];

        /// <summary>Program counter (byte offset into MicroMem).</summary>
        public ushort PC { get; set; }

        /// <summary>True while VU1 is executing a microprogram.</summary>
        public bool IsRunning { get; set; }

        public VU1State()
        {
            InitializeRegisters();
        }

        private void InitializeRegisters()
        {
            VF[0] = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
            VI[0] = 0;
            ACC = Vector4.Zero;
            Q = 1.0f;
            P = 1.0f;
            I = 0.0f;
            RngState = 0x3F800001u;
            StatusFlags = 0;
            MACFlags = 0;
            ClipFlags = 0;
            IsRunning = false;
            PC = 0;
        }

        /// <summary>Read a VF register.</summary>
        public Vector4 ReadVF(int reg)
        {
            if ((uint)reg >= 32) throw new ArgumentOutOfRangeException(nameof(reg));
            return VF[reg];
        }

        /// <summary>Write a VF register with 4-bit component mask (bit0=x, 1=y, 2=z, 3=w).</summary>
        public void WriteVF(int reg, Vector4 value, byte mask = 0xF)
        {
            if ((uint)reg >= 32) throw new ArgumentOutOfRangeException(nameof(reg));
            if (reg == 0) return; // vf0 is read-only

            Vector4 cur = VF[reg];
            VF[reg] = new Vector4(
                (mask & 0x1) != 0 ? FlushDenormal(value.X) : cur.X,
                (mask & 0x2) != 0 ? FlushDenormal(value.Y) : cur.Y,
                (mask & 0x4) != 0 ? FlushDenormal(value.Z) : cur.Z,
                (mask & 0x8) != 0 ? FlushDenormal(value.W) : cur.W
            );
            UpdateMACFlags(VF[reg], mask);
        }

        /// <summary>Read a VI integer register.</summary>
        public ushort ReadVI(int reg)
        {
            if ((uint)reg >= 16) throw new ArgumentOutOfRangeException(nameof(reg));
            return VI[reg];
        }

        /// <summary>Write a VI integer register (vi0 is read-only).</summary>
        public void WriteVI(int reg, ushort value)
        {
            if ((uint)reg >= 16) throw new ArgumentOutOfRangeException(nameof(reg));
            if (reg == 0) return;
            VI[reg] = value;
        }

        /// <summary>Write ACC with component mask.</summary>
        public void WriteACC(Vector4 value, byte mask = 0xF)
        {
            Vector4 cur = ACC;
            ACC = new Vector4(
                (mask & 0x1) != 0 ? FlushDenormal(value.X) : cur.X,
                (mask & 0x2) != 0 ? FlushDenormal(value.Y) : cur.Y,
                (mask & 0x4) != 0 ? FlushDenormal(value.Z) : cur.Z,
                (mask & 0x8) != 0 ? FlushDenormal(value.W) : cur.W
            );
            UpdateMACFlags(ACC, mask);
        }

        /// <summary>Reset VU1 state and clear memories.</summary>
        public void Reset()
        {
            InitializeRegisters();
            Array.Clear(MicroMem, 0, MicroMem.Length);
            Array.Clear(DataMem, 0, DataMem.Length);
        }

        /// <summary>Start microprogram execution at the given byte offset.</summary>
        public void Start(ushort entryPoint)
        {
            PC = entryPoint;
            IsRunning = true;
        }

        /// <summary>Halt microprogram execution.</summary>
        public void Stop()
        {
            IsRunning = false;
        }

        // ── helpers ───────────────────────────────────────────────────────────

        /// <summary>PS2 hardware flushes denormals to zero.</summary>
        internal static float FlushDenormal(float v)
        {
            if (float.IsNaN(v)) return 0.0f;
            const float threshold = 1.175494e-38f;
            return (v != 0.0f && MathF.Abs(v) < threshold) ? 0.0f : v;
        }

        private void UpdateMACFlags(Vector4 r, byte mask)
        {
            const float D = 1.175494e-38f;
            ushort f = 0;
            if ((mask & 0x1) != 0)
            {
                if (r.X == 0f)                                            f |= 0x0001;
                if (r.X  < 0f)                                            f |= 0x0010;
                if (float.IsInfinity(r.X) || float.IsNaN(r.X))           f |= 0x0100;
                if (r.X != 0f && MathF.Abs(r.X) < D)                     f |= 0x1000;
            }
            if ((mask & 0x2) != 0)
            {
                if (r.Y == 0f)                                            f |= 0x0002;
                if (r.Y  < 0f)                                            f |= 0x0020;
                if (float.IsInfinity(r.Y) || float.IsNaN(r.Y))           f |= 0x0200;
                if (r.Y != 0f && MathF.Abs(r.Y) < D)                     f |= 0x2000;
            }
            if ((mask & 0x4) != 0)
            {
                if (r.Z == 0f)                                            f |= 0x0004;
                if (r.Z  < 0f)                                            f |= 0x0040;
                if (float.IsInfinity(r.Z) || float.IsNaN(r.Z))           f |= 0x0400;
                if (r.Z != 0f && MathF.Abs(r.Z) < D)                     f |= 0x4000;
            }
            if ((mask & 0x8) != 0)
            {
                if (r.W == 0f)                                            f |= 0x0008;
                if (r.W  < 0f)                                            f |= 0x0080;
                if (float.IsInfinity(r.W) || float.IsNaN(r.W))           f |= 0x0800;
                if (r.W != 0f && MathF.Abs(r.W) < D)                     f |= 0x8000;
            }
            MACFlags = f;
        }
    }
}
