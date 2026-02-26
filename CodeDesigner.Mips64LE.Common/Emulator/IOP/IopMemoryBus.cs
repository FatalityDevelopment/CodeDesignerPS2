using System;
using CodeDesigner.Mips64LE.Common.Emulator.Hardware;

namespace CodeDesigner.Mips64LE.Common.Emulator.IOP
{
    /// <summary>
    /// Memory bus for the PS2 IOP (I/O Processor).
    ///
    /// Physical memory map (relevant subset):
    ///   0x00000000 – 0x001FFFFF  IOP main RAM (2 MB)
    ///   0x1F800000 – 0x1F8003FF  Scratchpad (1 KB)
    ///   0x1F801070 – 0x1F801077  IOP INTC (I_STAT / I_MASK)
    ///   0x1F801080 – 0x1F8010EF  IOP DMAC channels 0–6 (MDECin … OTC)
    ///   0x1F8010F0 – 0x1F8010F7  IOP DMAC global registers (DPCR / DICR)
    ///   0x1F801100 – 0x1F80112F  IOP Timers 0–2 (counter/mode/target, 0x10 stride)
    ///   0x1F801480 – 0x1F8014AF  IOP Timers 3–5 (counter/mode/target, 0x10 stride)
    ///   0x1F801500 – 0x1F801557  IOP DMAC channels 7–12 (SPU2 … cdrom)
    ///   0x1F801570 – 0x1F801577  IOP DMAC global registers 2 (DPCR2 / DICR2)
    ///   0x1D000000 – 0x1D00006F  SIF registers (EE side mapped into IOP address space)
    ///   0x1FC00000 – 0x1FFFFFFF  BIOS ROM (4 MB, shared with EE, read-only from IOP)
    ///
    /// SIF integration:
    ///   The IOP sees the same SIF registers as the EE, albeit with reversed
    ///   semantics on the flag registers (IOP writes to SMFLAG to signal the EE,
    ///   and writes to MSFLAG to acknowledge EE signals).  This bus optionally
    ///   holds a reference to the shared <see cref="SIF"/> instance so that
    ///   reads/writes at 0x1D000000 work across both processors.
    ///
    /// Reference: PCSX2 pcsx2/IopDma.h, pcsx2/IopMem.cpp.
    /// </summary>
    public sealed class IopMemoryBus
    {
        // ── Region sizes ──────────────────────────────────────────────────────
        public const int RamSize       = 2 * 1024 * 1024;  // 2 MB
        public const int ScratchpadSize = 1024;             // 1 KB
        public const int BiosRomSize   = 4 * 1024 * 1024;  // 4 MB

        // ── Physical base addresses ───────────────────────────────────────────
        private const uint RamBase        = 0x00000000u;
        private const uint ScratchpadBase = 0x1F800000u;
        private const uint IntcBase       = 0x1F801070u;
        private const uint TimerBase      = 0x1F801100u;  // T0–T2
        private const uint TimerBase2     = 0x1F801480u;  // T3–T5
        private const uint SifMirrorBase  = 0x1D000000u;
        private const uint BiosBase       = 0x1FC00000u;

        // Each timer bank covers 3 timers × 0x10 bytes = 0x30 bytes.
        private const uint TimerBankSize  = 0x30u;

        private readonly byte[] _ram        = new byte[RamSize];
        private readonly byte[] _scratchpad = new byte[ScratchpadSize];
        private readonly byte[] _bios       = new byte[BiosRomSize];

        // IOP INTC registers: I_STAT (offset 0) and I_MASK (offset 4)
        private uint _iStat;
        private uint _iMask;

        private Sio2?           _sio2;
        private Spu2?           _spu2;
        private UsbController?  _usb;
        private ILinkController? _iLink;
        private Dev9Controller? _dev9;

        private const int Sio2IntcBit  = 17;
        private const int Dev9IntcBit  = 13;
        private const int ILinkIntcBit = 18;
        private const int UsbIntcBit   = 22;

        // IOP timers T0–T5 (T0–T2 at 0x1F801100; T3–T5 at 0x1F801480)
        private readonly IopTimer[] _timers = new IopTimer[6];

        /// <summary>
        /// IOP timers T0–T5.  T0–T2 are mapped at 0x1F801100; T3–T5 at 0x1F801480.
        /// </summary>
        public IopTimer[] Timers => _timers;

        /// <summary>IOP DMA controller (13 channels).</summary>
        public IopDmac Dmac { get; } = new IopDmac();

        /// <summary>Exposes IOP RAM for bulk loading (e.g. from SIF DMA).</summary>
        public byte[] Ram => _ram;

        /// <summary>Exposes BIOS ROM for loading a BIOS image (shared with EE).</summary>
        public byte[] Bios => _bios;

        /// <summary>
        /// Optional reference to the shared SIF instance.  When set, reads/writes
        /// to the SIF mirror region (0x1D000000) are forwarded to this object.
        /// </summary>
        public SIF? SharedSif { get; set; }

        private Cdvd? _cdvd;

        /// <summary>
        /// Optional CDVD controller.  When set:
        /// <list type="bullet">
        ///   <item>Byte-level reads/writes to 0x1F402000–0x1F40201F are forwarded
        ///     to the CDVD register file.</item>
        ///   <item>The CDVD's <see cref="Cdvd.DataReady"/> event asserts IOP INTC
        ///     bit 2 (CDVD interrupt).</item>
        /// </list>
        /// </summary>
        public Cdvd? Cdvd
        {
            get => _cdvd;
            set
            {
                if (_cdvd != null)
                    _cdvd.DataReady -= HandleCdvdDataReady;
                _cdvd = value;
                if (_cdvd != null)
                    _cdvd.DataReady += HandleCdvdDataReady;
            }
        }

        private const int CdvdIntcBit = 2;

        private void HandleCdvdDataReady(object? sender, EventArgs e)
            => RaiseInterrupt(CdvdIntcBit);

        /// <summary>
        /// Optional SIO2 controller.  When set, reads/writes to
        /// 0x1F808200–0x1F80827F are forwarded to the SIO2 register file and
        /// <see cref="Sio2.TransferComplete"/> asserts IOP INTC bit 17.
        /// </summary>
        public Sio2? Sio2
        {
            get => _sio2;
            set
            {
                if (_sio2 != null)
                    _sio2.TransferComplete -= HandleSio2TransferComplete;
                _sio2 = value;
                if (_sio2 != null)
                    _sio2.TransferComplete += HandleSio2TransferComplete;
            }
        }

        private void HandleSio2TransferComplete(object? sender, EventArgs e)
            => RaiseInterrupt(Sio2IntcBit);

        /// <summary>
        /// Optional SPU2 controller.  When set:
        /// <list type="bullet">
        ///   <item>16-bit and 32-bit reads/writes to 0x1F900000–0x1F9007FF are
        ///     forwarded to the SPU2 register file.</item>
        ///   <item>IOP DMAC channel 4 (SPU1 / core 0) and channel 7 (SPU2 / core 1)
        ///     have their <see cref="IopDmacChannel.DeviceDataConsumer"/> wired so that
        ///     RAM→device audio DMA transfers write into
        ///     <see cref="Spu2.AudioRam"/>.</item>
        /// </list>
        /// </summary>
        public Spu2? Spu2
        {
            get => _spu2;
            set
            {
                _spu2 = value;
                // Wire DMAC channels 4 (SPU1 → Core 0) and 7 (SPU2 → Core 1).
                Dmac.Channels[4].DeviceDataConsumer = value != null ? w => value.WriteDmaData(0, w) : null;
                Dmac.Channels[7].DeviceDataConsumer = value != null ? w => value.WriteDmaData(1, w) : null;
            }
        }

        /// <summary>
        /// Optional USB OHCI controller.  When set:
        /// <list type="bullet">
        ///   <item>32-bit reads/writes to 0x1F801600–0x1F8016FF are forwarded
        ///     to the USB register file.</item>
        ///   <item>The USB <see cref="UsbController.InterruptRaised"/> event asserts IOP INTC
        ///     bit 22 (USB interrupt).</item>
        /// </list>
        /// </summary>
        public UsbController? Usb
        {
            get => _usb;
            set
            {
                if (_usb != null)
                    _usb.InterruptRaised -= HandleUsbInterrupt;
                _usb = value;
                if (_usb != null)
                    _usb.InterruptRaised += HandleUsbInterrupt;
            }
        }

        private void HandleUsbInterrupt(object? sender, EventArgs e)
            => RaiseInterrupt(UsbIntcBit);

        /// <summary>
        /// Optional i.LINK (IEEE 1394 / FireWire) controller.  When set:
        /// <list type="bullet">
        ///   <item>32-bit reads/writes to 0x1F808400–0x1F8085FF are forwarded
        ///     to the i.LINK register file.</item>
        ///   <item>The i.LINK <see cref="ILinkController.InterruptRaised"/> event asserts IOP INTC
        ///     bit 18 (i.LINK interrupt).</item>
        /// </list>
        /// </summary>
        public ILinkController? ILink
        {
            get => _iLink;
            set
            {
                if (_iLink != null)
                    _iLink.InterruptRaised -= HandleILinkInterrupt;
                _iLink = value;
                if (_iLink != null)
                    _iLink.InterruptRaised += HandleILinkInterrupt;
            }
        }

        private void HandleILinkInterrupt(object? sender, EventArgs e)
            => RaiseInterrupt(ILinkIntcBit);

        /// <summary>
        /// Optional DEV9 parallel-port expansion controller.  When set:
        /// <list type="bullet">
        ///   <item>Access to 0x1F80146E–0x1F80146F (HW_DEV9_DATA) forwards to
        ///     <see cref="Dev9Controller.IopData"/>.</item>
        ///   <item>Access to 0x10000000–0x1000FFFF (expansion bus) forwards to
        ///     the SPEED ASIC register file.</item>
        ///   <item>IOP DMAC channel 8 (DEV9 DMA) is wired to the controller.</item>
        ///   <item>The DEV9 <see cref="Dev9Controller.InterruptRaised"/> event asserts IOP INTC
        ///     bit 13 (DEV9 interrupt).</item>
        /// </list>
        /// </summary>
        public Dev9Controller? Dev9
        {
            get => _dev9;
            set
            {
                if (_dev9 != null)
                    _dev9.InterruptRaised -= HandleDev9Interrupt;
                _dev9 = value;
                if (_dev9 != null)
                    _dev9.InterruptRaised += HandleDev9Interrupt;
                // Wire DMAC channel 8 (DEV9 DMA) for device↔RAM transfers.
                Dmac.Channels[8].DeviceDataProvider  = value != null
                    ? (() => (uint)value.ReadExpHalf(0u)) : (Func<uint>?)null;
                Dmac.Channels[8].DeviceDataConsumer  = value != null
                    ? (w => value.WriteExpHalf(0u, (ushort)w)) : (Action<uint>?)null;
            }
        }

        private void HandleDev9Interrupt(object? sender, EventArgs e)
            => RaiseInterrupt(Dev9IntcBit);

        public IopMemoryBus()
        {
            for (int i = 0; i < 6; i++)
                _timers[i] = new IopTimer(i);

            // Wire CDVD sector-buffer data to DMAC channel 3 (CDVD channel)
            // for device→RAM transfers.  The lambda reads _cdvd lazily so the
            // Cdvd property may be set any time before the transfer runs.
            Dmac.Channels[3].DeviceDataProvider = () => _cdvd?.ReadNextDmaWord() ?? 0u;
        }

        // ── IOP INTC accessors ────────────────────────────────────────────────

        /// <summary>IOP I_STAT register (interrupt status, W1C).</summary>
        public uint IStat => _iStat;

        /// <summary>IOP I_MASK register (interrupt mask, plain R/W).</summary>
        public uint IMask => _iMask;

        /// <summary>True when any interrupt is both pending and enabled.</summary>
        public bool IsInterruptPending => (_iStat & _iMask) != 0;

        /// <summary>Assert an IOP interrupt line (sets the corresponding I_STAT bit).</summary>
        public void RaiseInterrupt(int bit)
        {
            if ((uint)bit < 32u) _iStat |= 1u << bit;
        }

        // ── 32-bit read/write (main interface) ────────────────────────────────

        /// <summary>Read a 32-bit little-endian word from the IOP address space.</summary>
        public uint ReadWord(uint addr)
        {
            // IOP INTC
            if (addr == IntcBase)      return _iStat;
            if (addr == IntcBase + 4u) return _iMask;

            // IOP timers T0–T2 (0x1F801100)
            if (addr >= TimerBase && addr < TimerBase + TimerBankSize)
                return ReadTimerWord(addr, TimerBase, 0);

            // IOP timers T3–T5 (0x1F801480)
            if (addr >= TimerBase2 && addr < TimerBase2 + TimerBankSize)
                return ReadTimerWord(addr, TimerBase2, 3);

            // IOP DMAC channel registers
            if (IopDmac.TryDecodeChannelAddress(addr, out int chIdx, out uint chReg))
                return Dmac.Channels[chIdx].ReadRegister(chReg);

            // IOP DMAC global registers (DPCR/DICR/DPCR2/DICR2)
            if (IopDmac.IsGlobalRegister(addr))
                return Dmac.ReadGlobal(addr);

            // SIF mirror
            if (SharedSif != null && addr >= SifMirrorBase && addr < SifMirrorBase + 0x70u)
                return ReadSifMirror(addr - SifMirrorBase);

            // CDVD registers (byte-granular; assemble 4 bytes for a word read)
            if (_cdvd != null && addr >= Cdvd.RegBase && addr < Cdvd.RegBase + Cdvd.RegSize)
            {
                uint off = addr - Cdvd.RegBase;
                return (uint)(_cdvd.ReadRegister(off)
                            | (_cdvd.ReadRegister(off + 1) << 8)
                            | (_cdvd.ReadRegister(off + 2) << 16)
                            | (_cdvd.ReadRegister(off + 3) << 24));
            }

            // SIO2 registers
            if (_sio2 != null && addr >= Sio2.RegBase && addr < Sio2.RegBase + Sio2.RegSize)
                return _sio2.ReadRegister(addr - Sio2.RegBase);

            // SPU2 registers
            if (_spu2 != null && addr >= Spu2.RegBase && addr < Spu2.RegBase + Spu2.RegSize)
                return _spu2.ReadWord(addr - Spu2.RegBase);

            // USB OHCI registers (32-bit access only, per PCSX2 USBread8/16 returning 0)
            if (_usb != null && addr >= UsbController.RegBase && addr < UsbController.RegBase + UsbController.RegSize)
                return _usb.ReadRegister(addr - UsbController.RegBase);

            // i.LINK registers
            if (_iLink != null && addr >= ILinkController.RegBase && addr < ILinkController.RegBase + ILinkController.RegSize)
                return _iLink.ReadRegister(addr - ILinkController.RegBase);

            // DEV9 expansion bus (SPEED ASIC registers at 0x10000000–0x1000FFFF)
            if (_dev9 != null && addr >= Dev9Controller.ExpBase && addr < Dev9Controller.ExpBase + Dev9Controller.ExpSize)
                return _dev9.ReadExpWord(addr - Dev9Controller.ExpBase);

            var (region, offset) = Map(addr);
            return ReadWordFromRegion(region, offset);
        }

        /// <summary>Write a 32-bit little-endian word to the IOP address space.</summary>
        public void WriteWord(uint addr, uint value)
        {
            // IOP INTC
            if (addr == IntcBase)      { _iStat &= ~value; return; }  // W1C
            if (addr == IntcBase + 4u) { _iMask  = value;  return; }

            // IOP timers T0–T2 (0x1F801100)
            if (addr >= TimerBase && addr < TimerBase + TimerBankSize)
            {
                WriteTimerWord(addr, value, TimerBase, 0);
                return;
            }

            // IOP timers T3–T5 (0x1F801480)
            if (addr >= TimerBase2 && addr < TimerBase2 + TimerBankSize)
            {
                WriteTimerWord(addr, value, TimerBase2, 3);
                return;
            }

            // IOP DMAC channel registers — execute transfer if CHCR.STR is set.
            if (IopDmac.TryDecodeChannelAddress(addr, out int chIdx, out uint chReg))
            {
                Dmac.Channels[chIdx].WriteRegister(chReg, value);
                if (chReg == IopDmacChannel.REG_CHCR && Dmac.Channels[chIdx].IsRunning)
                    Dmac.ExecuteChannel(chIdx, this);
                return;
            }

            // IOP DMAC global registers (DPCR/DICR/DPCR2/DICR2)
            if (IopDmac.IsGlobalRegister(addr))
            {
                Dmac.WriteGlobal(addr, value);
                return;
            }

            // SIF mirror
            if (SharedSif != null && addr >= SifMirrorBase && addr < SifMirrorBase + 0x70u)
            {
                WriteSifMirror(addr - SifMirrorBase, value);
                return;
            }

            // CDVD registers (byte-granular; dispatch each of the 4 bytes)
            if (_cdvd != null && addr >= Cdvd.RegBase && addr < Cdvd.RegBase + Cdvd.RegSize)
            {
                uint off = addr - Cdvd.RegBase;
                _cdvd.WriteRegister(off,     (byte)value);
                _cdvd.WriteRegister(off + 1, (byte)(value >> 8));
                _cdvd.WriteRegister(off + 2, (byte)(value >> 16));
                _cdvd.WriteRegister(off + 3, (byte)(value >> 24));
                return;
            }

            // SIO2 registers
            if (_sio2 != null && addr >= Sio2.RegBase && addr < Sio2.RegBase + Sio2.RegSize)
            {
                _sio2.WriteRegister(addr - Sio2.RegBase, value);
                return;
            }

            // SPU2 registers
            if (_spu2 != null && addr >= Spu2.RegBase && addr < Spu2.RegBase + Spu2.RegSize)
            {
                _spu2.WriteWord(addr - Spu2.RegBase, value);
                return;
            }

            // USB OHCI registers (32-bit access only, per PCSX2 USBwrite8/16 being no-ops)
            if (_usb != null && addr >= UsbController.RegBase && addr < UsbController.RegBase + UsbController.RegSize)
            {
                _usb.WriteRegister(addr - UsbController.RegBase, value);
                return;
            }

            // i.LINK registers
            if (_iLink != null && addr >= ILinkController.RegBase && addr < ILinkController.RegBase + ILinkController.RegSize)
            {
                _iLink.WriteRegister(addr - ILinkController.RegBase, value);
                return;
            }

            // DEV9 expansion bus (SPEED ASIC registers at 0x10000000–0x1000FFFF)
            if (_dev9 != null && addr >= Dev9Controller.ExpBase && addr < Dev9Controller.ExpBase + Dev9Controller.ExpSize)
            {
                _dev9.WriteExpWord(addr - Dev9Controller.ExpBase, value);
                return;
            }

            var (region, offset, ro) = MapWrite(addr);
            if (!ro) WriteWordToRegion(region, offset, value);
        }

        // ── 8-bit and 16-bit helpers ──────────────────────────────────────────

        public byte ReadByte(uint addr)
        {
            if (_cdvd != null && addr >= Cdvd.RegBase && addr < Cdvd.RegBase + Cdvd.RegSize)
                return _cdvd.ReadRegister(addr - Cdvd.RegBase);

            if (_sio2 != null && addr >= Sio2.RegBase && addr < Sio2.RegBase + Sio2.RegSize)
                return (byte)_sio2.ReadRegister(addr - Sio2.RegBase);

            if (_spu2 != null && addr >= Spu2.RegBase && addr < Spu2.RegBase + Spu2.RegSize)
            {
                // Read the 16-bit register that contains this byte, extract the relevant byte.
                uint off     = addr - Spu2.RegBase;
                ushort half  = _spu2.ReadHalf(off & ~1u);
                return (off & 1u) == 0u ? (byte)half : (byte)(half >> 8);
            }

            // DEV9 IOP hardware register (HW_DEV9_DATA at 0x1F80146E, 2 bytes)
            if (_dev9 != null && addr >= Dev9Controller.IopRegBase && addr < Dev9Controller.IopRegBase + Dev9Controller.IopRegSize)
            {
                uint byteOff = addr - Dev9Controller.IopRegBase;
                return byteOff == 0u ? (byte)_dev9.IopData : (byte)(_dev9.IopData >> 8);
            }

            // DEV9 expansion bus
            if (_dev9 != null && addr >= Dev9Controller.ExpBase && addr < Dev9Controller.ExpBase + Dev9Controller.ExpSize)
                return _dev9.ReadExpByte(addr - Dev9Controller.ExpBase);

            var (region, offset) = Map(addr);
            return region[offset];
        }

        public void WriteByte(uint addr, byte value)
        {
            if (_cdvd != null && addr >= Cdvd.RegBase && addr < Cdvd.RegBase + Cdvd.RegSize)
            {
                _cdvd.WriteRegister(addr - Cdvd.RegBase, value);
                return;
            }

            if (_sio2 != null && addr >= Sio2.RegBase && addr < Sio2.RegBase + Sio2.RegSize)
            {
                _sio2.WriteRegister(addr - Sio2.RegBase, value);
                return;
            }

            if (_spu2 != null && addr >= Spu2.RegBase && addr < Spu2.RegBase + Spu2.RegSize)
            {
                // Read-modify-write the containing halfword.
                uint off    = addr - Spu2.RegBase;
                uint hOff   = off & ~1u;
                ushort old  = _spu2.ReadHalf(hOff);
                ushort nov  = (off & 1u) == 0u
                    ? (ushort)((old & 0xFF00u) | value)
                    : (ushort)((old & 0x00FFu) | ((uint)value << 8));
                _spu2.WriteHalf(hOff, nov);
                return;
            }

            // DEV9 IOP hardware register (HW_DEV9_DATA at 0x1F80146E, 2 bytes)
            if (_dev9 != null && addr >= Dev9Controller.IopRegBase && addr < Dev9Controller.IopRegBase + Dev9Controller.IopRegSize)
            {
                uint byteOff = addr - Dev9Controller.IopRegBase;
                ushort cur = _dev9.IopData;
                _dev9.IopData = byteOff == 0u
                    ? (ushort)((cur & 0xFF00u) | value)
                    : (ushort)((cur & 0x00FFu) | ((uint)value << 8));
                return;
            }

            // DEV9 expansion bus
            if (_dev9 != null && addr >= Dev9Controller.ExpBase && addr < Dev9Controller.ExpBase + Dev9Controller.ExpSize)
            {
                _dev9.WriteExpByte(addr - Dev9Controller.ExpBase, value);
                return;
            }

            var (region, offset, ro) = MapWrite(addr);
            if (!ro) region[offset] = value;
        }

        public ushort ReadHalf(uint addr)
            => (ushort)(ReadByte(addr) | (ReadByte(addr + 1) << 8));

        public void WriteHalf(uint addr, ushort value)
        {
            WriteByte(addr,     (byte)value);
            WriteByte(addr + 1, (byte)(value >> 8));
        }

        // ── Timer helpers ─────────────────────────────────────────────────────
        //
        // Each IOP timer occupies 3 × 4-byte slots at base + timer*0x10 + reg*4
        //   reg offset 0x00 = count, 0x04 = mode, 0x08 = target

        private uint ReadTimerWord(uint addr, uint bankBase, int bankOffset)
        {
            uint off = addr - bankBase;
            int idx  = (int)(off >> 4) + bankOffset; // timer index 0–5
            if ((uint)idx >= 6u) return 0u;
            return _timers[idx].ReadRegister(off & 0x0Fu);
        }

        private void WriteTimerWord(uint addr, uint value, uint bankBase, int bankOffset)
        {
            uint off = addr - bankBase;
            int idx  = (int)(off >> 4) + bankOffset;
            if ((uint)idx >= 6u) return;
            _timers[idx].WriteRegister(off & 0x0Fu, value);
        }

        // ── SIF mirror helpers ────────────────────────────────────────────────
        //
        // The IOP's SIF mirror at 0x1D000000 maps to the same registers as the
        // EE's SIF window at 0x1000F200, but with swapped flag semantics:
        //   IOP offset 0x00 → SMCOM (IOP reads what EE wrote to MSCOM)
        //   IOP offset 0x10 → MSCOM (IOP writes commands the EE reads as SMCOM)
        //   IOP offset 0x20 → SMFLAG (IOP writes to set flags EE sees in SMFLAG)
        //   IOP offset 0x30 → MSFLAG (IOP writes to clear flags it previously set)
        //   IOP offset 0x40 → F240
        //   IOP offset 0x50 → F250
        //   IOP offset 0x60 → F260
        //
        // For simplicity we forward all IOP SIF accesses directly to the shared SIF
        // object using IOP-side semantics.

        private uint ReadSifMirror(uint relOffset)
        {
            // IOP reads from the perspective of the IOP:
            //   IOP:0x00 → same SMCOM the EE writes
            //   IOP:0x10 → same MSCOM the IOP writes
            // Map to EE SIF register offsets (same layout, just swapped COM roles)
            return SharedSif?.ReadRegister(relOffset) ?? 0u;
        }

        private void WriteSifMirror(uint relOffset, uint value)
        {
            SharedSif?.WriteRegister(relOffset, value);
        }

        // ── Region mapping ────────────────────────────────────────────────────

        private (byte[] region, uint offset) Map(uint addr)
        {
            if (addr < RamSize)
                return (_ram, addr);
            if (addr >= ScratchpadBase && addr < ScratchpadBase + ScratchpadSize)
                return (_scratchpad, addr - ScratchpadBase);
            if (addr >= BiosBase && addr < BiosBase + BiosRomSize)
                return (_bios, addr - BiosBase);

            // Unmapped — return a zero slice from scratchpad (read as 0)
            return (_scratchpad, 0u);
        }

        private (byte[] region, uint offset, bool readOnly) MapWrite(uint addr)
        {
            if (addr < RamSize)
                return (_ram, addr, false);
            if (addr >= ScratchpadBase && addr < ScratchpadBase + ScratchpadSize)
                return (_scratchpad, addr - ScratchpadBase, false);
            if (addr >= BiosBase && addr < BiosBase + BiosRomSize)
                return (_bios, 0, true); // ROM: read-only

            return (_scratchpad, 0u, true); // unmapped writes silently ignored
        }

        // ── Low-level helpers ─────────────────────────────────────────────────

        private static uint ReadWordFromRegion(byte[] region, uint offset)
        {
            if (offset + 3 >= (uint)region.Length) return 0u;
            return (uint)(region[offset]
                        | (region[offset + 1] << 8)
                        | (region[offset + 2] << 16)
                        | (region[offset + 3] << 24));
        }

        private static void WriteWordToRegion(byte[] region, uint offset, uint value)
        {
            if (offset + 3 >= (uint)region.Length) return;
            region[offset]     = (byte)value;
            region[offset + 1] = (byte)(value >> 8);
            region[offset + 2] = (byte)(value >> 16);
            region[offset + 3] = (byte)(value >> 24);
        }
    }
}
