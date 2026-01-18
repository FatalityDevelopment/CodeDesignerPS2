using System;
using System.Numerics;

namespace CodeDesigner.Mips64LE.Common.VectorUnits.VU0
{
    /// <summary>
    /// Vector Unit 0 (VU0) for the Emotion Engine
    /// Operates in both Macro Mode (COP2) and Micro Mode
    /// Handles SIMD vector operations with 128-bit registers
    /// </summary>
    public sealed class VU0State
    {
        /// <summary>
        /// Vector Float Registers (32 × 128-bit)
        /// Each register contains 4 × 32-bit floats (x, y, z, w)
        /// vf0 is hardwired to specific values based on component
        /// </summary>
        public Vector4[] VF { get; } = new Vector4[32];

        /// <summary>
        /// Vector Integer Registers (16 × 16-bit)
        /// vi0 is hardwired to 0
        /// </summary>
        public ushort[] VI { get; } = new ushort[16];

        /// <summary>
        /// Accumulator Register (128-bit)
        /// Used for multiply-accumulate operations
        /// </summary>
        public Vector4 ACC { get; set; }

        /// <summary>
        /// Q Register - holds division/reciprocal results
        /// </summary>
        public float Q { get; set; }

        /// <summary>
        /// P Register - holds square root results
        /// </summary>
        public float P { get; set; }

        /// <summary>
        /// R Register - random number generator state
        /// </summary>
        public float R { get; set; }

        /// <summary>
        /// I Register - intermediate value for load operations
        /// </summary>
        public float I { get; set; }

        /// <summary>
        /// Status flags register
        /// </summary>
        public uint StatusFlags { get; set; }

        /// <summary>
        /// MAC flags register - arithmetic flags
        /// Bits [0-3]: Zero flags for x, y, z, w
        /// Bits [4-7]: Sign flags for x, y, z, w
        /// Bits [8-11]: Overflow flags for x, y, z, w
        /// Bits [12-15]: Underflow flags for x, y, z, w
        /// </summary>
        public ushort MACFlags { get; set; }

        /// <summary>
        /// Clipping flags register
        /// </summary>
        public uint ClipFlags { get; set; }

        /// <summary>
        /// VU0 Micro Memory (4KB for instructions)
        /// </summary>
        public byte[] MicroMem { get; } = new byte[4096];

        /// <summary>
        /// VU0 Data Memory (4KB for data)
        /// </summary>
        public byte[] DataMem { get; } = new byte[4096];

        /// <summary>
        /// VU0 Program Counter (for micro mode)
        /// </summary>
        public ushort PC { get; set; }

        /// <summary>
        /// Running flag - true when VU0 is executing microprogram
        /// </summary>
        public bool IsRunning { get; set; }

        public VU0State()
        {
            InitializeRegisters();
        }

        private void InitializeRegisters()
        {
            // vf0 is special - components have specific values
            // x = 0.0, y = 0.0, z = 0.0, w = 1.0
            VF[0] = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);

            // vi0 is hardwired to 0
            VI[0] = 0;

            // Initialize ACC to zero
            ACC = Vector4.Zero;

            // Initialize special registers
            Q = 1.0f;
            P = 1.0f;
            R = 0.0f;
            I = 0.0f;

            // Clear flags
            StatusFlags = 0;
            MACFlags = 0;
            ClipFlags = 0;

            // Not running initially
            IsRunning = false;
            PC = 0;
        }

        /// <summary>
        /// Read a vector float register
        /// </summary>
        public Vector4 ReadVF(int register)
        {
            if (register < 0 || register >= 32)
                throw new ArgumentOutOfRangeException(nameof(register));

            return VF[register];
        }

        /// <summary>
        /// Write a vector float register with component masking
        /// </summary>
        /// <param name="register">Register index (0-31)</param>
        /// <param name="value">Value to write</param>
        /// <param name="mask">Component mask (bit 0=x, 1=y, 2=z, 3=w)</param>
        public void WriteVF(int register, Vector4 value, byte mask = 0xF)
        {
            if (register < 0 || register >= 32)
                throw new ArgumentOutOfRangeException(nameof(register));

            // vf0 is read-only (except w component is always 1.0)
            if (register == 0)
                return;

            // Apply component masking
            Vector4 current = VF[register];
            Vector4 masked = new Vector4(
                (mask & 0x1) != 0 ? value.X : current.X,
                (mask & 0x2) != 0 ? value.Y : current.Y,
                (mask & 0x4) != 0 ? value.Z : current.Z,
                (mask & 0x8) != 0 ? value.W : current.W
            );

            VF[register] = masked;
            UpdateMACFlags(masked, mask);
        }

        /// <summary>
        /// Read a vector integer register
        /// </summary>
        public ushort ReadVI(int register)
        {
            if (register < 0 || register >= 16)
                throw new ArgumentOutOfRangeException(nameof(register));

            return VI[register];
        }

        /// <summary>
        /// Write a vector integer register
        /// </summary>
        public void WriteVI(int register, ushort value)
        {
            if (register < 0 || register >= 16)
                throw new ArgumentOutOfRangeException(nameof(register));

            // vi0 is hardwired to 0
            if (register == 0)
                return;

            VI[register] = value;
        }

        /// <summary>
        /// Read from ACC register
        /// </summary>
        public Vector4 ReadACC()
        {
            return ACC;
        }

        /// <summary>
        /// Write to ACC register with component masking
        /// </summary>
        public void WriteACC(Vector4 value, byte mask = 0xF)
        {
            Vector4 current = ACC;
            ACC = new Vector4(
                (mask & 0x1) != 0 ? value.X : current.X,
                (mask & 0x2) != 0 ? value.Y : current.Y,
                (mask & 0x4) != 0 ? value.Z : current.Z,
                (mask & 0x8) != 0 ? value.W : current.W
            );
            UpdateMACFlags(ACC, mask);
        }

        /// <summary>
        /// Update MAC flags based on vector result
        /// </summary>
        private void UpdateMACFlags(Vector4 result, byte mask)
        {
            // Clear flags for masked components
            ushort newFlags = 0;

            // Denormal threshold for PS2 VU
            const float DENORMAL_THRESHOLD = 1.175494e-38f;

            // Zero flags (bits 0-3)
            if ((mask & 0x1) != 0 && result.X == 0.0f) newFlags |= 0x0001;
            if ((mask & 0x2) != 0 && result.Y == 0.0f) newFlags |= 0x0002;
            if ((mask & 0x4) != 0 && result.Z == 0.0f) newFlags |= 0x0004;
            if ((mask & 0x8) != 0 && result.W == 0.0f) newFlags |= 0x0008;

            // Sign flags (bits 4-7)
            if ((mask & 0x1) != 0 && result.X < 0.0f) newFlags |= 0x0010;
            if ((mask & 0x2) != 0 && result.Y < 0.0f) newFlags |= 0x0020;
            if ((mask & 0x4) != 0 && result.Z < 0.0f) newFlags |= 0x0040;
            if ((mask & 0x8) != 0 && result.W < 0.0f) newFlags |= 0x0080;

            // Overflow flags (bits 8-11) - simplified check
            if ((mask & 0x1) != 0 && (float.IsInfinity(result.X) || float.IsNaN(result.X))) newFlags |= 0x0100;
            if ((mask & 0x2) != 0 && (float.IsInfinity(result.Y) || float.IsNaN(result.Y))) newFlags |= 0x0200;
            if ((mask & 0x4) != 0 && (float.IsInfinity(result.Z) || float.IsNaN(result.Z))) newFlags |= 0x0400;
            if ((mask & 0x8) != 0 && (float.IsInfinity(result.W) || float.IsNaN(result.W))) newFlags |= 0x0800;

            // Underflow flags (bits 12-15) - check for denormals
            if ((mask & 0x1) != 0 && result.X != 0.0f && MathF.Abs(result.X) < DENORMAL_THRESHOLD) newFlags |= 0x1000;
            if ((mask & 0x2) != 0 && result.Y != 0.0f && MathF.Abs(result.Y) < DENORMAL_THRESHOLD) newFlags |= 0x2000;
            if ((mask & 0x4) != 0 && result.Z != 0.0f && MathF.Abs(result.Z) < DENORMAL_THRESHOLD) newFlags |= 0x4000;
            if ((mask & 0x8) != 0 && result.W != 0.0f && MathF.Abs(result.W) < DENORMAL_THRESHOLD) newFlags |= 0x8000;

            MACFlags = newFlags;
        }

        /// <summary>
        /// Get register name for debugging
        /// </summary>
        public static string GetVFRegisterName(int register)
        {
            if (register < 0 || register >= 32)
                return $"vf?{register}";
            return $"vf{register}";
        }

        /// <summary>
        /// Get integer register name for debugging
        /// </summary>
        public static string GetVIRegisterName(int register)
        {
            if (register < 0 || register >= 16)
                return $"vi?{register}";
            return $"vi{register}";
        }

        /// <summary>
        /// Reset VU0 state
        /// </summary>
        public void Reset()
        {
            InitializeRegisters();
            Array.Clear(MicroMem, 0, MicroMem.Length);
            Array.Clear(DataMem, 0, DataMem.Length);
        }

        /// <summary>
        /// Start microprogram execution
        /// </summary>
        public void Start(ushort entryPoint)
        {
            PC = entryPoint;
            IsRunning = true;
        }

        /// <summary>
        /// Stop microprogram execution
        /// </summary>
        public void Stop()
        {
            IsRunning = false;
        }
    }
}
