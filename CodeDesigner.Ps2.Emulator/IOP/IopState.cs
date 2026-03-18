namespace CodeDesigner.Ps2.Emulator.IOP
{
    /// <summary>
    /// Register file and architectural state for the PS2 IOP (I/O Processor).
    ///
    /// The IOP is a MIPS R3000A running at ~36.864 MHz.  It is a 32-bit MIPS I
    /// CPU with no MMU (physical addressing only), 32 general-purpose 32-bit
    /// registers, and a minimal COP0 limited to Status (SR), Cause, and EPC.
    ///
    /// Key differences from the EE R5900:
    ///   - All GPRs are 32-bit (no upper 64/128-bit lane).
    ///   - No HI1/LO1, no SA, no VU0, no COP1 FPU.
    ///   - COP0 has only SR (reg 12), Cause (reg 13), EPC (reg 14), and
    ///     PRId (reg 15).
    ///   - Exception vectors:
    ///       BEV = 0 → 0x80000080  (general exception, IOP RAM)
    ///       BEV = 1 → 0xBFC00180  (BIOS ROM handler)
    ///     IOP reset vector: 0xBFC00000
    ///
    /// Reference: PCSX2 pcsx2/IopDma.h, pcsx2/R3000A.h.
    /// </summary>
    public sealed class IopState
    {
        // ── General-purpose registers (32 × 32-bit) ───────────────────────────
        private readonly uint[] _regs = new uint[32];

        /// <summary>Read general-purpose register <paramref name="r"/> (0–31).</summary>
        public uint ReadReg(int r) => (uint)(r == 0 ? 0u : _regs[r]);

        /// <summary>
        /// Write general-purpose register <paramref name="r"/> (0–31).
        /// Writes to $zero (r=0) are silently discarded.
        /// </summary>
        public void WriteReg(int r, uint value)
        {
            if (r != 0) _regs[r] = value;
        }

        // ── Program counter ───────────────────────────────────────────────────

        /// <summary>Current program counter (32-bit physical address).</summary>
        public uint PC { get; set; }

        // ── Multiply / divide results ─────────────────────────────────────────

        /// <summary>HI register (upper 32 bits of MULT/DIV result).</summary>
        public uint HI { get; set; }

        /// <summary>LO register (lower 32 bits of MULT/DIV result).</summary>
        public uint LO { get; set; }

        // ── Minimal COP0 state ────────────────────────────────────────────────
        //
        //   SR     (reg 12) — Status register
        //   Cause  (reg 13) — Cause register
        //   EPC    (reg 14) — Exception Program Counter
        //   PRId   (reg 15) — Processor ID

        private uint _sr;
        private uint _cause;
        private uint _epc;

        // SR bit masks
        private const uint SR_IEc = 1u << 0;   // current interrupt enable
        private const uint SR_KUc = 1u << 1;   // current kernel/user mode
        private const uint SR_IEp = 1u << 2;   // previous interrupt enable
        private const uint SR_KUp = 1u << 3;
        private const uint SR_IEo = 1u << 4;
        private const uint SR_KUo = 1u << 5;
        public  const uint SR_BEV = 1u << 22;  // Bootstrap Exception Vectors

        /// <summary>COP0 Status register (SR).</summary>
        public uint SR
        {
            get => _sr;
            set => _sr = value;
        }

        /// <summary>COP0 Cause register.</summary>
        public uint Cause
        {
            get => _cause;
            set => _cause = value;
        }

        /// <summary>COP0 Exception Program Counter.</summary>
        public uint EPC
        {
            get => _epc;
            set => _epc = value;
        }

        /// <summary>Read a COP0 register by index (supports 12, 13, 14, 15).</summary>
        public uint ReadCop0(int reg) => reg switch
        {
            12 => _sr,
            13 => _cause,
            14 => _epc,
            15 => 0x0000001Fu,  // PRId: IOP R3000A
            _  => 0u
        };

        /// <summary>Write a COP0 register by index (supports 12, 13, 14).</summary>
        public void WriteCop0(int reg, uint value)
        {
            switch (reg)
            {
                case 12: _sr    = value; break;
                case 13: _cause = value; break;
                case 14: _epc   = value; break;
            }
        }

        /// <summary>True when the BEV bit is set in SR (use BIOS ROM exception vectors).</summary>
        public bool Bev => (_sr & SR_BEV) != 0;

        // ── Reset ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Reset to power-on state.  PC is set to the MIPS reset vector 0xBFC00000.
        /// SR defaults to 0x00400000 (BEV=1).
        /// </summary>
        public void Reset()
        {
            for (int i = 1; i < 32; i++) _regs[i] = 0u;
            PC  = 0xBFC00000u;
            HI  = 0u;
            LO  = 0u;
            _sr    = 0x00400000u; // BEV=1
            _cause = 0u;
            _epc   = 0u;
        }
    }
}
