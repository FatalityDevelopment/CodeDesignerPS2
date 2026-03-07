using CodeDesigner.Mips64LE.Common.Emulator.IOP;

namespace CodeDesigner.Mips64LE.Common.Emulator
{

    /// <summary>
    /// Coordinates execution of the EE (Emotion Engine) and IOP (I/O Processor)
    /// running at their native clock ratios.
    ///
    /// Clock frequencies (approximate):
    ///   EE  — 147.456 MHz bus clock (instruction throughput ~150 MHz)
    ///   IOP — 36.864 MHz (exactly 1/4 of the EE bus clock)
    ///
    /// The scheduler runs both CPUs interleaved in fixed-size "slices":
    ///   • EE and IOP steps are interleaved: one IOP step for every
    ///     <see cref="EeToIopRatio"/> EE steps, within the same slice.
    ///   • This matches PCSX2's scheduling model and ensures IOP signals
    ///     (e.g. SIF SMFLAG writes) are visible to the EE in the same slice.
    ///
    /// The EE and IOP communicate through the shared <see cref="SIF"/> instance
    /// inside <see cref="PS2Emulator.Bus"/>; the IOP emulator is constructed with
    /// a reference to that same SIF so register reads/writes on both sides reflect
    /// the same state.
    ///
    /// Usage:
    /// <code>
    ///   var scheduler = new DualCpuScheduler();
    ///   // load BIOS into scheduler.EE.Bus.Bios / scheduler.IOP.Bus.Ram ...
    ///   while (running)
    ///   {
    ///       scheduler.RunSlice();          // run one slice (interleaved: 4 EE + 1 IOP step)
    ///       scheduler.TickHardware();      // advance timers / INTC for that slice
    ///   }
    /// </code>
    ///
    /// Reference: PCSX2 pcsx2/IopCounters.cpp, pcsx2/R5900.cpp scheduling loop.
    /// </summary>
    public sealed class DualCpuScheduler
    {
        // ── EE:IOP cycle ratio ────────────────────────────────────────────────
        /// <summary>
        /// Number of EE instruction steps executed per one IOP instruction step.
        /// Matches the hardware ratio of 147.456 MHz / 36.864 MHz = 4.
        /// </summary>
        public const int EeToIopRatio = 4;

        // ── Pad buffer addresses in IOP RAM ───────────────────────────────────

        /// <summary>
        /// Default IOP RAM address for the port 0 pad data buffer (9 bytes).
        /// Mirrors the region used by padman.irx in a real PS2.
        /// </summary>
        public const uint DefaultPad0BufAddr = 0x1F200u;

        /// <summary>
        /// Default IOP RAM address for the port 1 pad data buffer (9 bytes).
        /// </summary>
        public const uint DefaultPad1BufAddr = 0x1F210u;

        /// <summary>The EE (Emotion Engine) emulator.</summary>
        public PS2Emulator EE { get; }

        /// <summary>The IOP (I/O Processor) emulator.</summary>
        public IopEmulator IOP { get; }

        /// <summary>
        /// The SIO2 serial I/O controller shared between this scheduler and the IOP bus.
        /// IOP software (padman.irx) polls this device to read controller input.
        /// </summary>
        public Sio2 Sio2 { get; }

        /// <summary>DualShock 2 state for controller port 0 (wired to <see cref="Sio2"/> port 0).</summary>
        public PadState Pad0 { get; }

        /// <summary>DualShock 2 state for controller port 1 (wired to <see cref="Sio2"/> port 1).</summary>
        public PadState Pad1 { get; }

        /// <summary>
        /// Memory card inserted in slot A (port 0).
        /// Assign a <see cref="MemoryCard"/> instance to enable memory-card access
        /// on port 0; set to <c>null</c> to simulate an empty slot.
        /// </summary>
        public MemoryCard? MemCard0
        {
            get => Sio2.MemCard0;
            set => Sio2.MemCard0 = value;
        }

        /// <summary>
        /// Memory card inserted in slot B (port 1).
        /// Assign a <see cref="MemoryCard"/> instance to enable memory-card access
        /// on port 1; set to <c>null</c> to simulate an empty slot.
        /// </summary>
        public MemoryCard? MemCard1
        {
            get => Sio2.MemCard1;
            set => Sio2.MemCard1 = value;
        }

        /// <summary>
        /// Total EE instruction steps executed since the last <see cref="Reset"/>.
        /// </summary>
        public ulong TotalEeSteps { get; private set; }

        /// <summary>
        /// Total IOP instruction steps executed since the last <see cref="Reset"/>.
        /// </summary>
        public ulong TotalIopSteps { get; private set; }

        /// <summary>
        /// Accumulated fractional IOP steps (in units of EE steps).
        /// Used to keep the IOP running at exactly 1/4 of the EE rate even when
        /// <see cref="RunSlice"/> is called with step counts that are not multiples
        /// of <see cref="EeToIopRatio"/>.
        /// </summary>
        private int _iopAccumulator;

        /// <summary>
        /// Cached IOP PC for the self-jump fast-forward check.
        /// Persists across <see cref="RunSlice"/> calls to avoid redundant bus reads
        /// when the IOP stays at the same idle address across many slices.
        /// </summary>
        private uint _cachedIopPc = uint.MaxValue;

        /// <summary>
        /// Whether the instruction at <see cref="_cachedIopPc"/> is an unconditional
        /// jump-to-self.  Recomputed whenever <see cref="_cachedIopPc"/> changes.
        /// </summary>
        private bool _cachedIopSelfJump;

        /// <summary>
        /// Construct a scheduler, optionally supplying a pre-configured EE emulator.
        /// The IOP is always constructed fresh and wired to the EE's SIF.
        /// </summary>
        /// <param name="ee">
        /// Optional pre-built EE emulator.  When null a new <see cref="PS2Emulator"/>
        /// is created with a default <see cref="PS2MemoryBus"/>.
        /// </param>
        public DualCpuScheduler(PS2Emulator? ee = null)
        {
            EE  = ee ?? new PS2Emulator();
            IOP = new IopEmulator(EE.Bus); // shares SIF via PS2MemoryBus.Sif

            // Share the EE scratchpad with the IOP bus so that 0x70000000–0x70003FFF
            // is coherent between both CPUs.  The real PS2 BIOS uses this region as a
            // bootstrap stack and IPC handshake area on both the EE and IOP side.
            IOP.Bus.SharedScratchpad = EE.Bus.Scratchpad;

            // Bridge IOP RAM into the EE bus (EE physical 0x1A000000–0x1BFFFFFF).
            // The EE kernel polls physical 0x1A000006 waiting for the IOP BIOS to
            // write its ready flag there during boot.  Sharing the same byte[] array
            // makes all IOP writes to IOP RAM[0–1FFFFF] immediately visible to the EE.
            // Reference: PCSX2 vtlb.cpp — sub-bus 0x1A000000 maps to psxM (IOP RAM).
            EE.Bus.IopRam = IOP.Bus.Ram;

            // Bridge EE timer registers (0x10000000–0x10001FFF) into the IOP bus so
            // the IOP BIOS clock-calibration loop can observe EE Timer T0 COUNT.
            IOP.Bus.EeBus = EE.Bus;

            // Wire IOP DMAC CH9 (SIF0, RAM→device): IOP sends data to EE via SIF0 FIFO.
            IOP.Bus.Dmac.Channels[9].DeviceDataConsumer  = w => EE.Bus.Sif.WriteSif0(w);

            // Wire IOP DMAC CH10 (SIF1, device→RAM): IOP receives data from EE via SIF1 FIFO.
            IOP.Bus.Dmac.Channels[10].DeviceDataProvider = () => EE.Bus.Sif.ReadSif1();

            // Wire EE DMAC CH5 (SIF0, FIFO→RAM): EE receives data from IOP via SIF0 FIFO.
            // Without this, CH5 zero-fills EE RAM instead of draining real IOP→EE data,
            // causing SIFMAN's pending-packet counter to remain non-zero forever.
            // Reference: PCSX2 pcsx2/Dmac.cpp sif0Interrupt / sifRead.
            EE.Bus.Dmac.Channels[5].DataProvider = () => EE.Bus.Sif.ReadSif0();

            // Wire EE DMAC CH6 (SIF1, RAM→FIFO): EE sends commands to IOP via SIF1 FIFO.
            // Without this, EE→IOP command packets are silently discarded, preventing
            // SIFMAN from completing the SIF handshake and the IOP from advancing past
            // its idle self-jump at 0x0000ae94.
            // Reference: PCSX2 pcsx2/Dmac.cpp sif1Interrupt / sifWrite.
            EE.Bus.Dmac.Channels[6].DataConsumer = (data, off, len) =>
            {
                for (int i = off; i < off + len; i++)
                    EE.Bus.Sif.WriteSif1(data[i]);
            };

            // Wire SIO2 and pad states so IOP software can poll controllers.
            Pad0 = new PadState();
            Pad1 = new PadState();
            Sio2 = new Sio2 { Port0 = Pad0, Port1 = Pad1 };
            IOP.Bus.Sio2 = Sio2;

            // Always install a CDVD hardware object, even when no disc is inserted.
            // The IOP BIOS polls REG_NREADY (0x1F402005) bit 3 (MECHA_INIT) during
            // its boot sequence; if this bit is never set the BIOS times out and
            // skips SIF initialisation, permanently disabling IOP SBUS (I_MASK bit 1).
            // A no-disc Cdvd returns READY | MECHA_INIT | DEV9CON (0x4C), matching
            // PCSX2 cdvdReset() → cdvdUpdateReady(CDVD_DRIVE_READY) which always
            // includes those two always-on bits.  GameLoader.InsertDisc() replaces
            // this with the real Cdvd when a disc image is loaded.
            IOP.Bus.Cdvd = new Cdvd();

            // Wire EE VBlank events to IOP INTC so the IOP receives the same
            // vertical-sync interrupts as on real hardware.
            // Reference: PCSX2 Counters.cpp vSync0/vSync1 → psxVBlankStart/psxVBlankEnd
            //   → iopIntcIrq(0) (bit 0 = VBlank) and iopIntcIrq(11) (bit 11 = EvBlank).
            EE.VBlankStarted += (_, _) => IOP.Bus.RaiseInterrupt(0);   // IOP VBlank-start (bit 0)
            EE.VBlankEnded   += (_, _) => IOP.Bus.RaiseInterrupt(11);  // IOP EvBlank (bit 11)
        }

        /// <summary>
        /// Reset both CPUs to their power-on states and clear all step counters.
        /// </summary>
        public void Reset()
        {
            EE.Reset();
            IOP.Reset();
            TotalEeSteps     = 0;
            TotalIopSteps    = 0;
            _iopAccumulator  = 0;
            _cachedIopPc     = uint.MaxValue;
            _cachedIopSelfJump = false;
        }

        /// <summary>
        /// Execute one scheduler "slice": run <paramref name="eeSteps"/> EE
        /// instruction steps interleaved with a proportional number of IOP steps
        /// based on <see cref="EeToIopRatio"/>.
        ///
        /// EE and IOP steps are interleaved (one IOP step for every
        /// <see cref="EeToIopRatio"/> EE steps) rather than run as separate batches.
        /// This matches PCSX2's scheduling model and ensures that IOP hardware
        /// signals (e.g. SMFLAG writes) are visible to the EE within the same
        /// slice, preventing SIF-handshake timing deadlocks during boot.
        ///
        /// The IOP accumulator ensures that over time the IOP always executes
        /// exactly 1/<see cref="EeToIopRatio"/> as many steps as the EE, even
        /// when <paramref name="eeSteps"/> is not a multiple of
        /// <see cref="EeToIopRatio"/>.
        ///
        /// Hardware timers are advanced after both CPUs have run:
        /// call <see cref="TickHardware"/> separately if you need finer control.
        /// </summary>
        /// <param name="eeSteps">
        /// Number of EE instruction steps to execute in this slice.  Must be ≥ 1.
        /// Default is <see cref="EeToIopRatio"/> so that one IOP step is always
        /// executed per slice.
        /// </param>
        public void RunSlice(int eeSteps = EeToIopRatio)
        {
            if (eeSteps < 1) eeSteps = 1;

            for (int i = 0; i < eeSteps; i++)
            {
                // ── EE step ───────────────────────────────────────────────────
                EE.Step();
                TotalEeSteps++;

                // ── IOP step(s) (proportional, interleaved after each EE step) ─
                _iopAccumulator++;
                while (_iopAccumulator >= EeToIopRatio)
                {
                    _iopAccumulator -= EeToIopRatio;

                    // Fast-forward IOP self-jump (idle) loops: when the IOP is
                    // executing a tight `j $PC` with no hardware interrupt pending,
                    // the step produces no observable state change.  Skip calling
                    // IOP.Step() to avoid millions of wasted decode/execute cycles
                    // during the IOP's idle wait.  The cache persists across calls
                    // so we only re-read the bus when the PC actually changes.
                    uint currentPc = IOP.State.PC;
                    if (currentPc != _cachedIopPc)
                    {
                        _cachedIopPc       = currentPc;
                        _cachedIopSelfJump = IsIopSelfJump(currentPc);
                    }

                    TotalIopSteps++;
                    if (!IOP.Bus.IsInterruptPending && _cachedIopSelfJump)
                        continue;  // fast-forward: count step, skip execution
                    IOP.Step();
                }
            }
        }

        /// <summary>
        /// Returns <c>true</c> if the IOP instruction at <paramref name="pc"/> is
        /// an unconditional jump back to itself (<c>j $PC</c>).
        /// Used by <see cref="RunSlice"/> to fast-forward IOP idle loops.
        /// </summary>
        private bool IsIopSelfJump(uint pc)
        {
            uint word;
            try
            {
                word = IOP.Bus.ReadWord(pc);
            }
            catch (Exception)
            {
                // Any bus error (unmapped address, etc.) means this is not a safe
                // self-jump candidate; fall back to normal execution.
                return false;
            }

            // J opcode = 2 (bits 31:26)
            if ((word >> 26) != 2u) return false;

            // Effective target = PC[31:28] | instr[25:0] << 2
            uint target = (pc & 0xF0000000u) | ((word & 0x03FFFFFFu) << 2);
            return target == pc;
        }

        /// <summary>
        /// Poll both SIO2 ports using the DualShock 2 command 0x42 (read data) and
        /// write the 9-byte response for each port to the specified IOP RAM addresses.
        ///
        /// This replicates the periodic polling performed by the IOP padman.irx driver.
        /// After calling this method the raw pad response bytes are available in IOP RAM
        /// and can be transferred to EE RAM via IOP DMAC CH9 → SIF0 → EE DMAC CH5.
        ///
        /// Layout of each 9-byte buffer (DualShock 2 poll response):
        /// <list type="table">
        ///   <item><term>[0]</term><description>0xFF (leading idle)</description></item>
        ///   <item><term>[1]</term><description>0x73 (device ID = DualShock 2 analog)</description></item>
        ///   <item><term>[2]</term><description>0x5A (acknowledge)</description></item>
        ///   <item><term>[3]</term><description>button byte 0 (Select/L3/R3/Start/Up/Right/Down/Left, active-low)</description></item>
        ///   <item><term>[4]</term><description>button byte 1 (L2/R2/L1/R1/Triangle/Circle/Cross/Square, active-low)</description></item>
        ///   <item><term>[5]</term><description>right stick X</description></item>
        ///   <item><term>[6]</term><description>right stick Y</description></item>
        ///   <item><term>[7]</term><description>left stick X</description></item>
        ///   <item><term>[8]</term><description>left stick Y</description></item>
        /// </list>
        /// </summary>
        /// <param name="pad0BufAddr">IOP RAM address for port 0 result (default <see cref="DefaultPad0BufAddr"/>).</param>
        /// <param name="pad1BufAddr">IOP RAM address for port 1 result (default <see cref="DefaultPad1BufAddr"/>).</param>
        public void PollPads(uint pad0BufAddr = DefaultPad0BufAddr,
                             uint pad1BufAddr = DefaultPad1BufAddr)
        {
            PollOnePad(Pad0, pad0BufAddr);
            PollOnePad(Pad1, pad1BufAddr);
        }

        private void PollOnePad(PadState pad, uint bufAddr)
        {
            byte[] cmd = new byte[PadState.PollResponseMinSize]; // 9 zeros; byte[1]=0x42 set implicitly below
            cmd[0] = PadState.AddrPad;
            cmd[1] = PadState.CmdPoll;
            byte[] resp = pad.ProcessCommand(cmd, PadState.PollResponseMinSize);
            for (int i = 0; i < resp.Length; i++)
                IOP.Bus.WriteByte(bufAddr + (uint)i, resp[i]);
        }

        /// <summary>
        /// Tick hardware timers on both the EE and IOP sides for
        /// <paramref name="eeCycles"/> EE bus clock cycles.
        /// The IOP timers are advanced by <c>eeCycles / <see cref="EeToIopRatio"/></c>
        /// IOP cycles (minimum 1 when eeCycles ≥ 1).
        /// </summary>
        /// <param name="eeCycles">
        /// EE bus-clock cycles to advance.  Default is <see cref="EeToIopRatio"/>
        /// which advances the IOP hardware by exactly 1 IOP cycle.
        /// </param>
        public void TickHardware(uint eeCycles = EeToIopRatio)
        {
            EE.TickHardware(eeCycles);
            uint iopCycles = eeCycles / (uint)EeToIopRatio;
            if (iopCycles > 0)
                IOP.TickHardware(iopCycles);
        }
    }
}
