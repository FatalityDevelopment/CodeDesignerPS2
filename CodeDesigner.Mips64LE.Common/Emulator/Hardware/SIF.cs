namespace CodeDesigner.Mips64LE.Common.Emulator.Hardware
{
    /// <summary>
    /// Emulates the PS2 Sub-bus Interface (SIF) — the communication channel
    /// between the EE (main Emotion Engine CPU) and the IOP (I/O processor).
    ///
    /// The SIF exposes seven 32-bit memory-mapped registers in the EE's I/O space
    /// starting at physical address 0x1000F200:
    ///
    ///   0x1000F200  SBUS_MSCOM  — EE-to-IOP message/command mailbox
    ///   0x1000F210  SBUS_SMCOM  — IOP-to-EE message/command mailbox
    ///   0x1000F220  SBUS_MSFLAG — EE-to-IOP flags (EE writes to set bits)
    ///   0x1000F230  SBUS_SMFLAG — IOP-to-EE flags (EE writes to clear bits / W1C)
    ///   0x1000F240  SBUS_F240   — EE/IOP bootstrap handshake control register
    ///   0x1000F250  SBUS_F250   — (reserved / additional control)
    ///   0x1000F260  SBUS_F260   — (reserved / additional control)
    ///
    /// Write semantics:
    ///   MSCOM  — plain write (value stored as-is; IOP reads to see EE commands)
    ///   SMCOM  — plain write (IOP sets this; EE reads to see IOP responses)
    ///   MSFLAG — write-1-to-set (OR); used by EE to signal the IOP
    ///   SMFLAG — write-1-to-clear (AND-NOT); used by EE to acknowledge IOP flags
    ///   F240   — plain write (control / handshake bits)
    ///   F250   — plain write
    ///   F260   — plain write
    ///
    /// In this emulator there is no real IOP; the registers simply hold their last
    /// written values so BIOS probe code can read them back and the bootstrap
    /// handshake sequence can be replayed.
    ///
    /// Reference: PCSX2 pcsx2/Sif.h, pcsx2/SifCmd.cpp, PS2 EE Users Manual §5.
    /// </summary>
    public sealed class SIF
    {
        // ── Register offsets (byte offset from 0x1000F200) ───────────────────
        public const uint REG_MSCOM  = 0x00u;  // 0x1000F200
        public const uint REG_SMCOM  = 0x10u;  // 0x1000F210
        public const uint REG_MSFLAG = 0x20u;  // 0x1000F220
        public const uint REG_SMFLAG = 0x30u;  // 0x1000F230
        public const uint REG_F240   = 0x40u;  // 0x1000F240
        public const uint REG_F250   = 0x50u;  // 0x1000F250
        public const uint REG_F260   = 0x60u;  // 0x1000F260

        private uint _mscom;
        private uint _smcom;
        private uint _msflag;
        private uint _smflag;
        private uint _f240;
        private uint _f250;
        private uint _f260;

        /// <summary>EE-to-IOP command/message register (SBUS_MSCOM).</summary>
        public uint MsCom => _mscom;

        /// <summary>IOP-to-EE command/message register (SBUS_SMCOM).</summary>
        public uint SmCom => _smcom;

        /// <summary>EE-to-IOP flags (SBUS_MSFLAG).</summary>
        public uint MsFlag => _msflag;

        /// <summary>IOP-to-EE flags (SBUS_SMFLAG).</summary>
        public uint SmFlag => _smflag;

        /// <summary>EE/IOP bootstrap handshake register (SBUS_F240).</summary>
        public uint F240 => _f240;

        // ── Register I/O ──────────────────────────────────────────────────────

        /// <summary>
        /// Read a 32-bit SIF register.
        /// <paramref name="offset"/> is the byte offset from 0x1000F200.
        /// Unrecognised offsets return 0.
        /// </summary>
        public uint ReadRegister(uint offset)
        {
            return (offset & 0x70u) switch
            {
                REG_MSCOM  => _mscom,
                REG_SMCOM  => _smcom,
                REG_MSFLAG => _msflag,
                REG_SMFLAG => _smflag,
                REG_F240   => _f240,
                REG_F250   => _f250,
                REG_F260   => _f260,
                _          => 0u
            };
        }

        /// <summary>
        /// Write a 32-bit SIF register.
        /// MSFLAG uses write-1-to-set; SMFLAG uses write-1-to-clear; all others
        /// store the value as-is.
        /// </summary>
        public void WriteRegister(uint offset, uint value)
        {
            switch (offset & 0x70u)
            {
                case REG_MSCOM:  _mscom  = value;        break;
                case REG_SMCOM:  _smcom  = value;        break;
                case REG_MSFLAG: _msflag |= value;       break;  // W1S
                case REG_SMFLAG: _smflag &= ~value;      break;  // W1C
                case REG_F240:   _f240   = value;        break;
                case REG_F250:   _f250   = value;        break;
                case REG_F260:   _f260   = value;        break;
            }
        }
    }
}
