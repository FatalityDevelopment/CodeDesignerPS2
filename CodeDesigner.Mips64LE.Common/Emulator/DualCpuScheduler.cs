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
    ///   • <see cref="EeToIopRatio"/> EE instructions per IOP instruction (default 4).
    ///   • Hardware timers on both sides are advanced proportionally.
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
    ///       scheduler.RunSlice();          // run one slice (default: 4 EE + 1 IOP step)
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

        /// <summary>The EE (Emotion Engine) emulator.</summary>
        public PS2Emulator EE { get; }

        /// <summary>The IOP (I/O Processor) emulator.</summary>
        public IopEmulator IOP { get; }

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

            // Wire IOP DMAC CH9 (SIF0, RAM→device): IOP sends data to EE via SIF0 FIFO.
            IOP.Bus.Dmac.Channels[9].DeviceDataConsumer  = w => EE.Bus.Sif.WriteSif0(w);

            // Wire IOP DMAC CH10 (SIF1, device→RAM): IOP receives data from EE via SIF1 FIFO.
            IOP.Bus.Dmac.Channels[10].DeviceDataProvider = () => EE.Bus.Sif.ReadSif1();
        }

        /// <summary>
        /// Reset both CPUs to their power-on states and clear all step counters.
        /// </summary>
        public void Reset()
        {
            EE.Reset();
            IOP.Reset();
            TotalEeSteps   = 0;
            TotalIopSteps  = 0;
            _iopAccumulator = 0;
        }

        /// <summary>
        /// Execute one scheduler "slice": run <paramref name="eeSteps"/> EE
        /// instruction steps and a proportional number of IOP steps based on
        /// <see cref="EeToIopRatio"/>.
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

            // ── Run EE ───────────────────────────────────────────────────────
            for (int i = 0; i < eeSteps; i++)
                EE.Step();
            TotalEeSteps += (ulong)eeSteps;

            // ── Run IOP (proportional) ────────────────────────────────────────
            _iopAccumulator += eeSteps;
            int iopSteps = _iopAccumulator / EeToIopRatio;
            _iopAccumulator %= EeToIopRatio;

            for (int i = 0; i < iopSteps; i++)
                IOP.Step();
            TotalIopSteps += (ulong)iopSteps;
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
