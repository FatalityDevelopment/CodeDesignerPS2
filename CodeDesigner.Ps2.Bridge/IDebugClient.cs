using System;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Common surface implemented by both <see cref="PineClient"/> (PCSX2 PINE)
    /// and <see cref="InternalEmulatorClient"/> (built-in emulator's
    /// InternalPineSocket). Bridge handlers depend on this interface so a single
    /// code path covers both runtimes.
    /// <para>
    /// Methods here cover ops that exist in both PCSX2 PINE 0x00–0x16 and the
    /// initial InternalPineSocket implementation: memory read/write, basic
    /// execution control, status, breakpoint set/clear/clear-all.
    /// </para>
    /// <para>
    /// Mode-specific functionality (PCSX2 game metadata, OS thread inspection,
    /// scheduler-internal state) stays on the concrete client classes —
    /// handlers that need those features check the concrete type and gracefully
    /// degrade in the other mode.
    /// </para>
    /// </summary>
    public interface IDebugClient : IDisposable
    {
        // ── Memory R/W ────────────────────────────────────────────────────────

        /// <summary>Read a contiguous range of guest memory.</summary>
        byte[] ReadBytes(uint address, int length);

        /// <summary>Write a contiguous range to guest memory.</summary>
        void WriteBytes(uint address, byte[] data);

        // ── Execution control ─────────────────────────────────────────────────

        /// <summary>Pause the EE.</summary>
        void Pause();

        /// <summary>Resume the EE.</summary>
        void Resume();

        /// <summary>Execute one EE instruction; return the new PC.</summary>
        uint Step();

        /// <summary>Current EE program counter.</summary>
        uint GetProgramCounter();

        /// <summary>Current EE program counter, with the paused state.</summary>
        uint GetProgramCounter(out bool isPaused);

        // ── Breakpoints ───────────────────────────────────────────────────────

        void SetBreakpoint(uint address);

        /// <summary>Clear a breakpoint. Returns false if no breakpoint existed at the address.</summary>
        bool ClearBreakpoint(uint address);
        void ClearAllBreakpoints();

        // ── Status ────────────────────────────────────────────────────────────

        /// <summary>
        /// Generic emulator status. Both PCSX2 (Running/Paused/Shutdown) and the
        /// built-in emulator return values from this set.
        /// </summary>
        DebugClientStatus GetStatusGeneric();
    }

    /// <summary>
    /// Cross-runtime status enum. Both <see cref="PineClient.EmuStatus"/> and
    /// <see cref="InternalEmulatorClient.EmuStatus"/> map onto this.
    /// </summary>
    public enum DebugClientStatus : uint
    {
        Running  = 0,
        Paused   = 1,
        Shutdown = 2,
    }
}
