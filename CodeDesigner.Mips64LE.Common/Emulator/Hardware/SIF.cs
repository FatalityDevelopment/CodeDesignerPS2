using System.Collections.Generic;

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
    /// In addition to the register file the SIF contains two 32-word (128-byte)
    /// FIFOs that carry actual data between the two CPUs:
    ///
    ///   SIF0 FIFO — IOP→EE direction.
    ///               IOP DMAC channel 9 pushes 32-bit words into this FIFO.
    ///               EE  DMAC channel 5 drains it into EE main RAM.
    ///
    ///   SIF1 FIFO — EE→IOP direction.
    ///               EE  DMAC channel 6 pushes 32-bit words into this FIFO.
    ///               IOP DMAC channel 10 drains it into IOP main RAM.
    ///
    /// Reference: PCSX2 pcsx2/Sif.h, pcsx2/Sif0.cpp, pcsx2/Sif1.cpp,
    ///            pcsx2/SifCmd.cpp, PS2 EE Users Manual §5.
    /// </summary>
    public sealed class SIF
    {
        // ── FIFO capacity (matches PCSX2: 128 bytes = 32 × 32-bit words) ──────
        public const int FifoCapacity = 32;

        // ── SIF0 FIFO: IOP → EE ──────────────────────────────────────────────
        private readonly Queue<uint> _sif0Fifo = new Queue<uint>(FifoCapacity);

        // ── SIF1 FIFO: EE → IOP ──────────────────────────────────────────────
        private readonly Queue<uint> _sif1Fifo = new Queue<uint>(FifoCapacity);

        /// <summary>Number of words currently waiting in the SIF0 (IOP→EE) FIFO.</summary>
        public int Sif0Count => _sif0Fifo.Count;

        /// <summary>Number of words currently waiting in the SIF1 (EE→IOP) FIFO.</summary>
        public int Sif1Count => _sif1Fifo.Count;

        /// <summary>
        /// Write one 32-bit word into the SIF0 FIFO (IOP→EE direction).
        /// Called by the IOP DMAC (channel 9) when it transfers a word from IOP RAM
        /// toward the EE.  Words beyond <see cref="FifoCapacity"/> are silently dropped
        /// (matching hardware behaviour when the EE-side DMA is not yet armed).
        /// </summary>
        public void WriteSif0(uint word)
        {
            if (_sif0Fifo.Count < FifoCapacity)
                _sif0Fifo.Enqueue(word);
        }

        /// <summary>
        /// Read one 32-bit word from the SIF0 FIFO (IOP→EE direction).
        /// Called by the EE DMAC (channel 5) when it transfers data from the FIFO
        /// into EE main RAM.  Returns 0 when the FIFO is empty.
        /// </summary>
        public uint ReadSif0() =>
            _sif0Fifo.Count > 0 ? _sif0Fifo.Dequeue() : 0u;

        /// <summary>
        /// Write one 32-bit word into the SIF1 FIFO (EE→IOP direction).
        /// Called by the EE DMAC (channel 6) when it transfers a word from EE RAM
        /// toward the IOP.  Words beyond <see cref="FifoCapacity"/> are silently dropped.
        /// </summary>
        public void WriteSif1(uint word)
        {
            if (_sif1Fifo.Count < FifoCapacity)
                _sif1Fifo.Enqueue(word);
        }

        /// <summary>
        /// Read one 32-bit word from the SIF1 FIFO (EE→IOP direction).
        /// Called by the IOP DMAC (channel 10) when it transfers data from the FIFO
        /// into IOP main RAM.  Returns 0 when the FIFO is empty.
        /// </summary>
        public uint ReadSif1() =>
            _sif1Fifo.Count > 0 ? _sif1Fifo.Dequeue() : 0u;

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
        /// Write a 32-bit SIF register from the EE side.
        /// MSFLAG uses write-1-to-set; SMFLAG uses write-1-to-clear; all others
        /// store the value as-is.
        /// </summary>
        public void WriteRegister(uint offset, uint value)
        {
            switch (offset & 0x70u)
            {
                case REG_MSCOM:  _mscom  = value;        break;
                case REG_SMCOM:  _smcom  = value;        break;
                case REG_MSFLAG: _msflag |= value;       break;  // W1S from EE (EE signals IOP)
                case REG_SMFLAG: _smflag &= ~value;      break;  // W1C from EE (EE acks IOP signal)
                case REG_F240:   _f240   = value;        break;
                case REG_F250:   _f250   = value;        break;
                case REG_F260:   _f260   = value;        break;
            }
        }

        /// <summary>
        /// Write a 32-bit SIF register from the IOP side (via the 0x1D000000 mirror).
        /// The IOP flag semantics are the inverse of the EE:
        ///   MSFLAG (offset 0x20) → write-1-to-clear (IOP acknowledges the EE's signal)
        ///   SMFLAG (offset 0x30) → write-1-to-set   (IOP signals the EE)
        /// All other registers use plain-write semantics, same as the EE side.
        ///
        /// Reference: PCSX2 pcsx2/Hw.cpp sifWrite32 / IopMem.cpp SIF mirror handling.
        /// </summary>
        public void WriteIopRegister(uint offset, uint value)
        {
            switch (offset & 0x70u)
            {
                case REG_MSCOM:  _mscom  = value;        break;
                case REG_SMCOM:  _smcom  = value;        break;
                case REG_MSFLAG: _msflag &= ~value;      break;  // W1C from IOP (IOP acks EE signal)
                case REG_SMFLAG: _smflag |= value;       break;  // W1S from IOP (IOP signals EE)
                case REG_F240:   _f240   = value;        break;
                case REG_F250:   _f250   = value;        break;
                case REG_F260:   _f260   = value;        break;
            }
        }
    }
}
