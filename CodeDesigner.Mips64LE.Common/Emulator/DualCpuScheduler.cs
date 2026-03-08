using CodeDesigner.Mips64LE.Common.Emulator.Hardware;
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
        /// <c>true</c> once the initial WaitIntr SBUS conditions have been met and the
        /// persistent SBUS keep-alive is active.
        ///
        /// In the real PS2 IOP BIOS, SIFMAN calls <c>WaitIntr(SBUS=1)</c> before
        /// entering its idle loop.  The BIOS kernel records the wait but does not write
        /// hardware I_MASK bit 1 directly.  Without bit 1 set,
        /// <see cref="IopMemoryBus.IsInterruptPending"/> stays false even when the EE
        /// has signalled the IOP via MSFLAG, so the IOP fast-forward loop never
        /// releases and SIFMAN never wakes.
        ///
        /// PCSX2 applies a ROM patch that makes <c>WaitIntr</c> write the hardware
        /// I_MASK register.  We replicate the effect here, but the IOP timer ISR at
        /// ~0x4C60 immediately overwrites I_MASK with a constant value that clears
        /// bit 1 after every timer tick (it does a plain store, not a read-modify-write).
        ///
        /// Therefore we re-assert I_MASK bit 1 and I_STAT bit 1 at the start of
        /// <em>every</em> <see cref="RunSlice"/> call once the SBUS conditions are met,
        /// until SMFLAG bit 30 is set (either by the IOP itself or by our fallback
        /// injection).  This matches PCSX2's persistent-patch behavior.
        ///
        /// Triggered when:
        ///   • IOP SIFMAN has completed the F240 handshake (SMFLAG bits 16–18 all set)
        ///   • The EE has sent its ready signal (MSFLAG bit 16 set)
        /// </summary>
        private bool _sifmanSbusPatched;

        /// <summary>
        /// <c>true</c> once SMFLAG bit 30 (SIF_STAT_SIFINIT = 0x40000000) has been
        /// injected by the emulator on behalf of IOP SIFMAN.
        ///
        /// This is a fallback: if the IOP processes the persistent SBUS interrupt itself
        /// (SIFMAN wakes, runs, and sets SMFLAG bit 30), then the keep-alive stops
        /// naturally and this flag is never needed.  If the IOP does not set bit 30
        /// within a settling window (≥ 200 000 IOP steps), we inject it directly.
        ///
        /// After injection, the EE sees bit 30 and proceeds with the boot-time
        /// SIF command protocol, matching PCSX2 post-patch behavior.
        /// </summary>
        private bool _sifmanSmflagInjected;

        /// <summary>
        /// <c>true</c> once the EE EELOAD SIF-init loop guard at 0x80024124 has been
        /// cleared to 0 by the scheduler.
        ///
        /// The BIOS byte-fill loop (at ~0x800108B0) writes 0x20202020 to EE RAM
        /// physical 0x24124 (virtual 0x80024124).  The EELOAD SIF init loop at
        /// 0x80012540 keeps looping while [0x80024124] != 0, waiting for the IOP to
        /// send the SIF BootReply command (which would clear it).  Since our emulated
        /// IOP SIFMAN cannot complete the full SIF handshake, this field is never
        /// cleared and the loop runs forever.
        ///
        /// We detect this condition and clear 0x80024124 (and the adjacent 0x80024100
        /// guard used by the loop exit at 0x800126D4) after the SMFLAG bit-30 injection
        /// has fired, mimicking the effect of the IOP's SIF BootReply.
        /// </summary>
        private bool _sifLoopGuardCleared;

        /// <summary>
        /// IOP step count snapshot when <see cref="_sifmanSbusPatched"/> first became
        /// <c>true</c>.  Used to measure the settling window before the SMFLAG fallback
        /// injection.
        /// </summary>
        private ulong _sifmanPatchedAtIopStep;

        /// <summary>
        /// <c>true</c> once the synthetic CDVD disc-detection interrupt (IOP INTC bit 2)
        /// has been fired.  This interrupt simulates the PS2 hardware disc-detect event
        /// that PCSX2 fires via <c>cdvdNewDiskCB()</c> / <c>iopIntcIrq(2)</c> a short
        /// time after the drive detects a disc.  Without it CDVDman never runs its
        /// interrupt handler to set its internal disc-ready flags, creating a deadlock
        /// where no N-command is ever issued.
        /// Reference: PCSX2 pcsx2/CDVD/CDVD.cpp cdvdNewDiskCB().
        /// </summary>
        private bool _cdvdDiscInterruptFired;

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

            // When EE DMAC CH6 (SIF1 send) completes, the SIF1 FIFO now holds EE→IOP
            // command data.  If IOP DMAC CH10 (SIF1 receive) is already armed (STR=1)
            // but was deferred because the FIFO was empty at arm time, run it now.
            // Reference: PCSX2 pcsx2/Sif1.cpp — psxDma10 triggered from sif1Interrupt.
            EE.Bus.Dmac.Channels[6].TransferComplete += (_, _) =>
            {
                if (IOP.Bus.Dmac.Channels[10].IsRunning && EE.Bus.Sif.Sif1Count > 0)
                    IOP.Bus.Dmac.ExecuteChannel(10, IOP.Bus);
            };

            // When IOP DMAC CH9 (SIF0 send) completes, the SIF0 FIFO now holds IOP→EE
            // reply data.  If EE DMAC CH5 (SIF0 receive) is already armed (STR=1) but
            // was deferred because the FIFO was empty at arm time, run it now.
            // Reference: PCSX2 pcsx2/Sif0.cpp — dmaChain/sif0Interrupt cross-trigger.
            IOP.Bus.Dmac.Channels[9].TransferComplete += (_, _) =>
            {
                if (EE.Bus.Dmac.Channels[5].IsRunning && EE.Bus.Sif.Sif0Count > 0)
                    EE.Bus.Dmac.ExecuteChannel(5, EE.Bus);
            };

            // Wire the IOP SIF mirror (0x1D000000) to the shared SIF object so that
            // IOP writes to SMFLAG (offset 0x30) and MSFLAG (offset 0x20) are visible
            // to the EE and vice-versa.  Without this, IOP SIFMAN's SMFLAG writes are
            // silently discarded and the EE never receives the IOP-ready signal,
            // permanently stalling the EE-IOP SIF handshake.
            // Reference: PCSX2 pcsx2/IopMem.cpp SIF mirror at 0x1D000000.
            IOP.Bus.SharedSif = EE.Bus.Sif;

            // When IOP writes new bits to SMFLAG (IOP→EE signal), raise SBUS interrupt
            // on the EE so it wakes up from its SIF handshake poll loop.
            // EE INTC bit 1 = SBUS/SIF (0x0002), confirmed by EE BIOS polling
            // `INTC_STAT & 0x0002` at 0x8000FE30, and by PCSX2 Hw.cpp which writes
            // `INTC_STAT |= 0x2` on every SMFLAG write.
            // NOTE: The old code incorrectly used RaiseInterrupt(2) = VBlank-start.
            EE.Bus.Sif.IopSignaledEe += (_, _) => EE.Bus.Intc.RaiseInterrupt(INTC.INT_SBUS); // EE SBUS bit 1

            // When EE writes new bits to MSFLAG (EE→IOP signal), raise SBUS interrupt
            // on the IOP so SIFMAN wakes up from its poll loop and processes the EE signal.
            // Reference: PCSX2 pcsx2/Hw.cpp hwWrite32 SBUS_MSFLAG → iopIntcIrq(1).
            EE.Bus.Sif.EeSignaledIop += (_, _) => IOP.Bus.RaiseInterrupt(1); // IOP SBUS bit 1

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
            TotalEeSteps       = 0;
            TotalIopSteps      = 0;
            _iopAccumulator    = 0;
            _cachedIopPc          = uint.MaxValue;
            _cachedIopSelfJump    = false;
            _sifmanSbusPatched    = false;
            _sifmanSmflagInjected = false;
            _sifmanPatchedAtIopStep = 0;
            _cdvdDiscInterruptFired = false;
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

            // ── Persistent SBUS keep-alive (WaitIntr SBUS patch) ─────────────
            // SIFMAN calls WaitIntr(SBUS=1) before entering its idle loop, but
            // the real IOP ROM's WaitIntr does not write hardware I_MASK bit 1.
            // Without bit 1 set in I_MASK, IsInterruptPending stays false even
            // when the EE has signalled via MSFLAG.
            //
            // Additionally, the IOP timer ISR at ~0x4C60 immediately overwrites
            // I_MASK with a constant value (0x0001080D) that clears bit 1 after
            // every timer tick.  A single one-shot write is therefore not enough;
            // we must re-assert I_MASK bit 1 on every slice from the moment
            // SIFMAN starts its F240 handshake until boot is complete.  We also
            // re-assert I_STAT bit 1 (the synthetic SBUS wake) until SmFlag bit 30
            // is set so SIFMAN's WaitIntr(SBUS) can return.
            //
            // This matches PCSX2's persistent WaitIntr ROM-patch behavior
            // (IopBios.cpp — WaitIntr hook continuously enables I_MASK bit).
            //
            // Start condition (earliest trigger):
            //   • SIFMAN has written SmFlag bit 16 (the IOP-SIF-init flag it sets
            //     at the very beginning of its F240 handshake), indicating the IOP
            //     SIFMAN module has started.  Triggering at this early point lets
            //     the keep-alive be in place before SIFMAN calls WaitIntr(SBUS).
            //
            // Fallback start condition:
            //   • If SmFlag bit 16 is never set (e.g. SIFMAN failed to load or
            //     CDVDman is holding up the module chain), force-activate after
            //     5 000 000 IOP steps so the SMFLAG injection below can still fire.
            //
            // I_MASK bit 1 is kept enabled indefinitely after the patch activates
            // (not just until SmFlag bit 30) so that SIFCMD can continue to receive
            // EE SBUS signals even after SIF initialisation is complete.  Without
            // this, the timer ISR clears bit 1 after every tick and the IOP is
            // permanently unable to receive EE commands — causing the IOP to stay
            // at the kernel idle loop (0x0000ae94) while the EE waits at 0x8000fb18.
            if (!_sifmanSbusPatched
                && ((EE.Bus.Sif.SmFlag & 0x00010000u) != 0u         // SmFlag bit 16: SIFMAN started
                    || TotalIopSteps >= 5_000_000UL))                // fallback: 5 M IOP steps
            {
                _sifmanSbusPatched      = true;
                _sifmanPatchedAtIopStep = TotalIopSteps;
                if ((EE.Bus.Sif.SmFlag & 0x00010000u) == 0u)
                    Console.WriteLine($"[SBUS PATCH] Fallback-activated at IOP step {TotalIopSteps:N0} (SmFlag=0x{EE.Bus.Sif.SmFlag:X8})");
            }

            if (_sifmanSbusPatched)
            {
                // Keep I_MASK bit 1 (SBUS) enabled so the timer ISR cannot
                // permanently suppress EE→IOP SBUS signals.  This must persist
                // past SmFlag bit 30 (SIF init complete) so SIFCMD continues to
                // receive EE SIF commands throughout the game boot.
                IOP.Bus.EnableInterruptMask(1);

                // While SIFMAN's WaitIntr(SBUS) is still active (SmFlag bit 30
                // not yet set), also synthetically assert I_STAT bit 1 so the
                // WaitIntr semaphore is woken in spite of any timer-ISR races.
                if ((EE.Bus.Sif.SmFlag & 0x40000000u) == 0u)
                    IOP.Bus.RaiseInterrupt(1);

                // Keep I_MASK bit 2 (CDVD) enabled from the moment the
                // disc-detection interrupt fires until CDVDman's handler has run.
                // The IOP timer ISR at ~0x4C60 writes a fixed I_MASK value
                // (0x00010809) that clears bit 2, so we must re-assert it on
                // every slice — identical to the SBUS (bit 1) keep-alive above.
                // Once _cdvdDiscInterruptFired is true the I_STAT bit 2 is set;
                // we keep I_MASK bit 2 enabled until I_STAT bit 2 is cleared by
                // CDVDman's interrupt handler (W0C write to 0x1F801070).
                if (_cdvdDiscInterruptFired && (IOP.Bus.IStat & 0x4u) != 0u)
                    IOP.Bus.EnableInterruptMask(2);
            }

            // ── Fallback: SMFLAG bit-30 (SIF_STAT_SIFINIT) injection ─────────
            // If the IOP processes the SBUS interrupt and SIFMAN runs correctly,
            // the IOP will set SMFLAG bit 30 on its own and the keep-alive above
            // will stop.  If that does not happen within a generous settling window
            // (≥ 200 000 IOP steps), we inject bit 30 directly as a fallback.
            //
            // Reference: PCSX2 IopBios.cpp post-WaitIntr SIFMAN flow sets
            //   psHu32(SBUS_SMFLAG) |= SIF_STAT_SIFINIT (0x40000000).
            if (_sifmanSbusPatched
                && !_sifmanSmflagInjected
                && TotalIopSteps - _sifmanPatchedAtIopStep >= 200_000UL
                && (EE.Bus.Sif.SmFlag & 0x40000000u) == 0u)
            {
                _sifmanSmflagInjected = true;
                Console.WriteLine($"[SMFLAG FALLBACK] Injecting SMFLAG bit 30 (SIF_STAT_SIFINIT) at IOP step {TotalIopSteps:N0} (EE step {TotalEeSteps:N0})");
                EE.Bus.Sif.WriteIopRegister(Hardware.SIF.REG_SMFLAG, 0x40000000u);
            }

            // ── CDVD disc-detection interrupt injection ───────────────────────
            // PCSX2 fires IOP INTC bit 2 (CDVD interrupt) a short time after the
            // drive detects a disc — implemented as cdvdNewDiskCB() → iopIntcIrq(2).
            // This is an "unsolicited" interrupt (no N-command was issued): REG_ISTAT
            // is NOT set, so CDVDman's handler identifies it as a disc-detection
            // event rather than an N-command completion.
            //
            // CDVDman's disc-detection handler reads REG_NREADY (0x4C = PAUSE),
            // REG_STATUS (0x0A = PAUSE), and REG_CRT (disc type) to set its internal
            // disc-ready flags including [0x11218].bit5.  Without this interrupt the
            // CDVDman internal flag is never set → no N-command is ever issued →
            // WaitSema(0x404) blocks forever.
            //
            // Timing: fire after SMFLAG bit 30 (SIF init complete) AND enough IOP
            // steps have elapsed for CDVDman to have loaded and registered its
            // interrupt handler (~10 M IOP steps after the SBUS patch fires).
            // The 10M delay is generous; CDVDman typically registers within 1-3 M.
            if (!_cdvdDiscInterruptFired
                && IOP.Bus.Cdvd?.HasDisc == true
                && _sifmanSbusPatched
                && TotalIopSteps - _sifmanPatchedAtIopStep >= 10_000_000UL)
            {
                _cdvdDiscInterruptFired = true;
                // Set CDVD ISTAT bit 0 (disc-detection event) before raising IOP INTC.
                // PCSX2 cdvdDetectDisk(): cdvd.IntrStat |= 1 → iopIntcIrq(2).
                // SIFMAN's interrupt handler at ~0x1F640 reads ISTAT; bit 0 tells it
                // "disc-detection" rather than "N-cmd complete" so it calls CDVDman's
                // disc-detection callback which sets [0x11218].bit5 = 1.
                IOP.Bus.Cdvd!.RaiseDiscDetect();
                // Enable I_MASK bit 2 so the interrupt is actually delivered
                // (CDVDman may not have called EnableIntr(CDVD) yet, but enabling
                // it here is safe — PCSX2's ROM patch also forces mask bits).
                IOP.Bus.EnableInterruptMask(2);
                IOP.Bus.RaiseInterrupt(2);
                Console.WriteLine($"[CDVD DISC] Fired disc-detection interrupt (IOP INTC bit 2) at IOP step {TotalIopSteps:N0}");
            }

            // ── EE EELOAD SIF-init loop guard clear ───────────────────────────
            // After SMFLAG bit 30 is set (IOP-ready signal delivered), clear the
            // EE EELOAD SIF-init loop guard at 0x80024124 and 0x80024100 if they
            // are still non-zero (fill-byte value 0x20202020 from BIOS memset).
            // The loop at 0x80012540 continues while [0x80024124] != 0, waiting
            // for the IOP SIF BootReply which we cannot produce.  Clearing it
            // mimics the effect of the IOP's reply and allows boot to continue.
            if (!_sifLoopGuardCleared
                && (EE.Bus.Sif.SmFlag & 0x40000000u) != 0u)
            {
                const uint PhysGuard1 = 0x24124u;  // [0x80024124] loop-continue
                const uint PhysGuard2 = 0x24100u;  // [0x80024100] loop-exit guard
                bool g1NonZero = EE.Bus.Ram.Length > (int)PhysGuard1 + 3 &&
                                 (EE.Bus.Ram[PhysGuard1]     != 0 ||
                                  EE.Bus.Ram[PhysGuard1 + 1] != 0 ||
                                  EE.Bus.Ram[PhysGuard1 + 2] != 0 ||
                                  EE.Bus.Ram[PhysGuard1 + 3] != 0);
                if (g1NonZero)
                {
                    _sifLoopGuardCleared = true;
                    uint val1 = (uint)(EE.Bus.Ram[PhysGuard1]
                                    | (EE.Bus.Ram[PhysGuard1+1] << 8)
                                    | (EE.Bus.Ram[PhysGuard1+2] << 16)
                                    | (EE.Bus.Ram[PhysGuard1+3] << 24));
                    uint val2 = (uint)(EE.Bus.Ram[PhysGuard2]
                                    | (EE.Bus.Ram[PhysGuard2+1] << 8)
                                    | (EE.Bus.Ram[PhysGuard2+2] << 16)
                                    | (EE.Bus.Ram[PhysGuard2+3] << 24));
                    EE.Bus.Ram[PhysGuard1]     = 0;
                    EE.Bus.Ram[PhysGuard1 + 1] = 0;
                    EE.Bus.Ram[PhysGuard1 + 2] = 0;
                    EE.Bus.Ram[PhysGuard1 + 3] = 0;
                    EE.Bus.Ram[PhysGuard2]     = 0;
                    EE.Bus.Ram[PhysGuard2 + 1] = 0;
                    EE.Bus.Ram[PhysGuard2 + 2] = 0;
                    EE.Bus.Ram[PhysGuard2 + 3] = 0;
                    Console.WriteLine($"[EE LOOP GUARD] Cleared 0x80024124 (was 0x{val1:X8}) and 0x80024100 (was 0x{val2:X8}) at EE step {TotalEeSteps:N0}");
                }
            }

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
