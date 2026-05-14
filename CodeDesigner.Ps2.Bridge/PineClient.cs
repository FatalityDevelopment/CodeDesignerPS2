using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// PCSX2 PINE (Protocol for Instrumentation of Emulators) IPC client.
    /// <para>
    /// Implements the binary PINE protocol to read/write PCSX2 guest memory,
    /// query emulator status, and control EE execution (pause, resume, step,
    /// breakpoints, registers, threads, modules, symbols, and more).
    /// </para>
    /// <para>
    /// <b>Existing opcodes (upstream PINE):</b> 0x00–0x0F<br/>
    /// <b>Extended opcodes (debugger extension):</b> 0x10–0x2D<br/>
    /// <b>Watchpoint / snapshot extension:</b> 0x2E–0x32<br/>
    /// </para>
    /// <para>
    /// <b>Packet format:</b><br/>
    ///   Request:  <c>[total_size : u32 LE][opcode : u8][args…]</c><br/>
    ///   Reply:    <c>[total_size : u32 LE][status : u8][data…]</c><br/>
    ///   status = 0x00 (IPC_OK) or 0xFF (IPC_FAIL).
    /// </para>
    /// <para>
    /// Reference: <see href="https://github.com/GovanifY/pine"/> and
    /// <see href="https://github.com/PCSX2/pcsx2/blob/master/pcsx2/PINE.h"/>.
    /// </para>
    /// </summary>
    public sealed class PineClient : IDebugClient
    {
        // ── Default slot / port ───────────────────────────────────────────────
        /// <summary>Default PCSX2 PINE TCP port (Windows) / Unix socket suffix.</summary>
        public const int DefaultSlot = 28011;

        // ── Existing PINE opcodes (upstream) ──────────────────────────────────
        private const byte MsgRead8       = 0x00;
        private const byte MsgRead16      = 0x01;
        private const byte MsgRead32      = 0x02;
        private const byte MsgRead64      = 0x03;
        private const byte MsgWrite8      = 0x04;
        private const byte MsgWrite16     = 0x05;
        private const byte MsgWrite32     = 0x06;
        private const byte MsgWrite64     = 0x07;
        private const byte MsgVersion     = 0x08;
        private const byte MsgSaveState   = 0x09;
        private const byte MsgLoadState   = 0x0A;
        private const byte MsgTitle       = 0x0B;
        private const byte MsgID          = 0x0C;
        private const byte MsgUUID        = 0x0D;
        private const byte MsgGameVersion = 0x0E;
        private const byte MsgStatus      = 0x0F;

        // ── Extended PINE opcodes (debugger extension) ────────────────────────
        private const byte MsgGetProgramCounter   = 0x10;
        private const byte MsgPause               = 0x11;
        private const byte MsgResume              = 0x12;
        private const byte MsgStep                = 0x13;
        private const byte MsgSetBreakpoint       = 0x14;
        private const byte MsgClearBreakpoint     = 0x15;
        private const byte MsgClearAllBreakpoints = 0x16;
        private const byte MsgGetRegisters        = 0x17;
        private const byte MsgGetRegister         = 0x18;
        private const byte MsgSetRegister         = 0x19;
        private const byte MsgGetEEThreads        = 0x1A;
        private const byte MsgGetIOPThreads       = 0x1B;
        private const byte MsgGetModules          = 0x1C;
        private const byte MsgGetStack            = 0x1D;
        private const byte MsgStepInto            = 0x1E;
        private const byte MsgStepOver            = 0x1F;
        private const byte MsgStepOut             = 0x20;
        private const byte MsgGetSymbol           = 0x21;
        private const byte MsgSaveStateFile       = 0x22;
        private const byte MsgLoadStateFile       = 0x23;
        private const byte MsgReset               = 0x24;
        private const byte MsgFrameAdvance        = 0x25;
        private const byte MsgGetFPS              = 0x26;
        private const byte MsgSetLimiterMode      = 0x27;
        private const byte MsgListBreakpoints     = 0x28;
        private const byte MsgDisassemble         = 0x29;
        private const byte MsgListFunctions       = 0x2A;
        private const byte MsgGetSymbolByName     = 0x2B;
        private const byte MsgListGlobals         = 0x2C;
        private const byte MsgGetLocals           = 0x2D;

        // ── Watchpoint / snapshot extension opcodes ───────────────────────────
        private const byte MsgAddWatch          = 0x2E;
        private const byte MsgRemoveWatch       = 0x2F;
        private const byte MsgListWatches       = 0x30;
        private const byte MsgClearAllWatches   = 0x31;
        private const byte MsgSaveScreenshot    = 0x32;

        // ── Pad / controller input opcodes ────────────────────────────────────
        private const byte MsgPadGetState       = 0x33;
        private const byte MsgPadSetButton      = 0x34;
        private const byte MsgPadSetAnalog      = 0x35;
        private const byte MsgPadSetState       = 0x36;
        private const byte MsgPadGetType        = 0x37;

        // ── DEV9 network bridge opcodes ───────────────────────────────────────
        private const byte MsgNetGetStatus      = 0x38;
        private const byte MsgNetCapture        = 0x39;
        private const byte MsgNetRecvCapture    = 0x3A;
        private const byte MsgNetInject         = 0x3B;
        private const byte MsgNetInjectTx       = 0x3C;
        private const byte MsgNetDisassemble    = 0x3D;
        private const byte MsgNetSetFilter      = 0x3E;
        private const byte MsgNetGetStats       = 0x3F;

        // ── GS stream control opcodes ─────────────────────────────────────────
        // The bulk frame data does NOT travel over PINE — these opcodes only
        // expose discovery/status for the dedicated GSStreamServer socket
        // (see GSStreamClient.cs).
        private const byte MsgGSStreamGetPort   = 0x40;
        private const byte MsgGSStreamGetStatus = 0x41;
        private const byte MsgGSStreamPing      = 0x42;

        // ── PINE result codes ─────────────────────────────────────────────────
        private const byte IPC_OK   = 0x00;
        private const byte IPC_FAIL = 0xFF;

        // ── Batch-mode limits ─────────────────────────────────────────────────
        // Keeps packets well within pine.h MAX_IPC_SIZE (650000 bytes).
        private const int MaxRead32PerBatch  = 4096;
        private const int MaxWrite8PerBatch  = 4096;

        // ── Connection state ──────────────────────────────────────────────────
        private readonly string _host;
        private readonly int    _slot;   // TCP port on Windows; Unix socket suffix on Linux/macOS
        private Socket?  _socket;
        private readonly object _lock = new object();

        // ── Public enums / structs ────────────────────────────────────────────

        /// <summary>Emulator status values returned by <see cref="GetStatus"/>.</summary>
        public enum EmuStatus : uint
        {
            Running  = 0,
            Paused   = 1,
            Shutdown = 2,
        }

        /// <summary>EE register snapshot returned by <see cref="GetRegisters"/>.</summary>
        public sealed class RegisterSnapshot
        {
            public ulong[] Gpr { get; }
            public uint[] GprLo32 { get; }
            public uint[] GprHi32 { get; }
            public uint[] GprUpLo32 { get; }
            public uint[] GprUpHi32 { get; }
            public uint PC  { get; }
            public uint HI  { get; }
            public uint LO  { get; }
            public uint HI1 { get; }
            public uint LO1 { get; }
            public uint SA  { get; }
            public uint[] Cop0 { get; }
            public uint[] Fpr { get; }
            public uint FprAcc { get; }
            public uint[] Fcr { get; }
            public uint Fcr0 => (Fcr.Length > 0) ? Fcr[0] : 0;
            public uint Fcr31 => (Fcr.Length > 31) ? Fcr[31] : 0;
            public uint[] Vu0fX { get; }
            public uint[] Vu0fY { get; }
            public uint[] Vu0fZ { get; }
            public uint[] Vu0fW { get; }
            public uint Vu0AccX { get; }
            public uint Vu0AccY { get; }
            public uint Vu0AccZ { get; }
            public uint Vu0AccW { get; }
            public uint Vu0Q { get; }
            public uint Vu0P { get; }
            public uint Vu0I { get; }
            public uint[] Vu0i { get; }
            public ulong[] GsPriv { get; }

            internal RegisterSnapshot(
                ulong[] gpr,
                uint[] gprLo32,
                uint[] gprHi32,
                uint[] gprUpLo32,
                uint[] gprUpHi32,
                uint pc,
                uint hi,
                uint lo,
                uint hi1,
                uint lo1,
                uint sa,
                uint[] cop0,
                uint[] fpr,
                uint fprAcc,
                uint[] fcr,
                uint[] vu0fX,
                uint[] vu0fY,
                uint[] vu0fZ,
                uint[] vu0fW,
                uint vu0AccX,
                uint vu0AccY,
                uint vu0AccZ,
                uint vu0AccW,
                uint vu0Q,
                uint vu0P,
                uint vu0I,
                uint[] vu0i,
                ulong[] gsPriv)
            {
                Gpr = gpr;
                GprLo32 = gprLo32;
                GprHi32 = gprHi32;
                GprUpLo32 = gprUpLo32;
                GprUpHi32 = gprUpHi32;
                PC = pc;
                HI = hi;
                LO = lo;
                HI1 = hi1;
                LO1 = lo1;
                SA = sa;
                Cop0 = cop0;
                Fpr = fpr;
                FprAcc = fprAcc;
                Fcr = fcr;
                Vu0fX = vu0fX;
                Vu0fY = vu0fY;
                Vu0fZ = vu0fZ;
                Vu0fW = vu0fW;
                Vu0AccX = vu0AccX;
                Vu0AccY = vu0AccY;
                Vu0AccZ = vu0AccZ;
                Vu0AccW = vu0AccW;
                Vu0Q = vu0Q;
                Vu0P = vu0P;
                Vu0I = vu0I;
                Vu0i = vu0i;
                GsPriv = gsPriv;
            }
        }

        /// <summary>An EE or IOP OS thread returned by <see cref="GetEEThreads"/> / <see cref="GetIOPThreads"/>.</summary>
        public sealed class PineThread
        {
            /// <summary>Thread ID.</summary>
            public uint TID        { get; }
            /// <summary>Current program counter.</summary>
            public uint PC         { get; }
            /// <summary>Raw thread status byte.</summary>
            public byte Status     { get; }
            /// <summary>Wait type byte.</summary>
            public byte Wait       { get; }
            /// <summary>Thread priority.</summary>
            public uint Priority   { get; }
            /// <summary>Thread entry-point address.</summary>
            public uint EntryPoint { get; }
            /// <summary>Stack-top address.</summary>
            public uint StackTop   { get; }

            internal PineThread(uint tid, uint pc, byte status, byte wait, uint priority, uint entry, uint stackTop)
            {
                TID = tid; PC = pc; Status = status; Wait = wait;
                Priority = priority; EntryPoint = entry; StackTop = stackTop;
            }
        }

        /// <summary>An IOP module entry returned by <see cref="GetModules"/>.</summary>
        public sealed class IopModule
        {
            /// <summary>Module name (up to 31 characters).</summary>
            public string Name      { get; }
            /// <summary>Module version (BCD-encoded).</summary>
            public ushort Version   { get; }
            /// <summary>Address of the module's text (code) segment.</summary>
            public uint   TextAddr  { get; }
            /// <summary>Module entry point.</summary>
            public uint   Entry     { get; }
            /// <summary>Module global-pointer register value.</summary>
            public uint   GP        { get; }
            /// <summary>Size of the text segment.</summary>
            public uint   TextSize  { get; }
            /// <summary>Size of the data segment.</summary>
            public uint   DataSize  { get; }
            /// <summary>Size of the BSS (zero-initialised) segment.</summary>
            public uint   BssSize   { get; }

            internal IopModule(string name, ushort version, uint textAddr, uint entry, uint gp,
                               uint textSize, uint dataSize, uint bssSize)
            {
                Name = name; Version = version; TextAddr = textAddr; Entry = entry;
                GP = gp; TextSize = textSize; DataSize = dataSize; BssSize = bssSize;
            }
        }

        /// <summary>One EE call-stack frame returned by <see cref="GetStack"/>.</summary>
        public sealed class PineStackFrame
        {
            /// <summary>Function entry point.</summary>
            public uint Entry     { get; }
            /// <summary>Return address / saved PC for this frame.</summary>
            public uint PC        { get; }
            /// <summary>Stack pointer value for this frame.</summary>
            public uint SP        { get; }
            /// <summary>Size of this stack frame in bytes.</summary>
            public uint StackSize { get; }

            internal PineStackFrame(uint entry, uint pc, uint sp, uint stackSize)
            {
                Entry = entry; PC = pc; SP = sp; StackSize = stackSize;
            }
        }

        /// <summary>A breakpoint entry returned by <see cref="ListBreakpoints"/>.</summary>
        public sealed class PineBreakpointEntry
        {
            /// <summary>Breakpoint address.</summary>
            public uint Address { get; }
            /// <summary>Whether the breakpoint is currently enabled.</summary>
            public bool Enabled { get; }
            /// <summary>CPU selector: 0 = EE, 1 = IOP.</summary>
            public byte Cpu     { get; }

            internal PineBreakpointEntry(uint address, bool enabled, byte cpu)
            {
                Address = address; Enabled = enabled; Cpu = cpu;
            }
        }

        /// <summary>One disassembled instruction line returned by <see cref="Disassemble"/>.</summary>
        public sealed class PineDisassemblyLine
        {
            /// <summary>Instruction address.</summary>
            public uint   Address { get; }
            /// <summary>Disassembly text.</summary>
            public string Text    { get; }

            internal PineDisassemblyLine(uint address, string text)
            {
                Address = address; Text = text;
            }
        }

        /// <summary>An EE symbol (function or global variable) returned by <see cref="ListFunctions"/> / <see cref="ListGlobals"/>.</summary>
        public sealed class PineSymbolEntry
        {
            /// <summary>Symbol address.</summary>
            public uint   Address { get; }
            /// <summary>Symbol size in bytes.</summary>
            public uint   Size    { get; }
            /// <summary>Symbol name.</summary>
            public string Name    { get; }

            internal PineSymbolEntry(uint address, uint size, string name)
            {
                Address = address; Size = size; Name = name;
            }
        }

        /// <summary>A local variable or parameter returned by <see cref="GetLocals"/>.</summary>
        public sealed class PineLocalEntry
        {
            /// <summary>Storage class: 0 = global/static, 1 = register, 2 = stack.</summary>
            public byte  StorageType { get; }
            /// <summary>Register number (StorageType=1) or stack-pointer offset (StorageType=2).</summary>
            public int   Value       { get; }
            /// <summary>Variable or parameter name.</summary>
            public string Name       { get; }

            internal PineLocalEntry(byte storageType, int value, string name)
            {
                StorageType = storageType; Value = value; Name = name;
            }
        }

        /// <summary>
        /// A memory watchpoint entry returned by <see cref="ListWatches"/>.
        /// <para>Backed by PCSX2's CBreakPoints MemCheck infrastructure (MEMCHECK_BREAK).</para>
        /// </summary>
        public sealed class PineWatchEntry
        {
            /// <summary>CPU the watchpoint applies to: 0 = EE, 1 = IOP.</summary>
            public byte Cpu   { get; }
            /// <summary>Inclusive start address of the watched range.</summary>
            public uint Start { get; }
            /// <summary>Exclusive end address of the watched range.</summary>
            public uint End   { get; }
            /// <summary>
            /// Condition bitmask: 0x01 = read, 0x02 = write, 0x03 = read+write.
            /// </summary>
            public byte Cond  { get; }

            internal PineWatchEntry(byte cpu, uint start, uint end, byte cond)
            {
                Cpu = cpu; Start = start; End = end; Cond = cond;
            }
        }

        /// <summary>
        /// Creates a new <see cref="PineClient"/>.
        /// </summary>
        /// <param name="host">PCSX2 host address (default <c>"127.0.0.1"</c>).</param>
        /// <param name="slot">PINE slot / TCP port (default <see cref="DefaultSlot"/> = 28011).</param>
        public PineClient(string host = "127.0.0.1", int slot = DefaultSlot)
        {
            _host = host;
            _slot = slot;
        }

        /// <summary>Gets whether the client currently has an open socket connection.</summary>
        public bool IsConnected => _socket?.Connected == true;

        // ═════════════════════════════════════════════════════════════════════
        // Existing PINE API (upstream opcodes 0x00–0x0F)
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Reads <paramref name="length"/> bytes from PCSX2 guest memory starting at
        /// <paramref name="address"/> using batched PINE Read32 commands.
        /// </summary>
        public byte[] ReadBytes(uint address, int length)
        {
            if (length <= 0) return Array.Empty<byte>();

            var result = new byte[length];
            int offset = 0;

            // Handle leading unaligned bytes one at a time.
            while (offset < length && ((address + offset) & 3) != 0)
            {
                result[offset] = ReadOneByte((uint)(address + offset));
                offset++;
            }

            // Batch-read the aligned body using Read32 commands.
            while (offset + 4 <= length)
            {
                int words = Math.Min((length - offset) / 4, MaxRead32PerBatch);
                ReadBatchRead32((uint)(address + offset), words, result, offset);
                offset += words * 4;
            }

            // Handle trailing unaligned bytes one at a time.
            while (offset < length)
            {
                result[offset] = ReadOneByte((uint)(address + offset));
                offset++;
            }

            return result;
        }

        /// <summary>
        /// Writes <paramref name="data"/> to PCSX2 guest memory starting at
        /// <paramref name="address"/> using batched PINE Write8 commands.
        /// </summary>
        public void WriteBytes(uint address, byte[] data)
        {
            if (data == null || data.Length == 0) return;

            int offset = 0;
            while (offset < data.Length)
            {
                int count = Math.Min(data.Length - offset, MaxWrite8PerBatch);
                WriteBatchWrite8((uint)(address + offset), data, offset, count);
                offset += count;
            }
        }

        /// <summary>Returns the current emulator status from PCSX2.</summary>
        public EmuStatus GetStatus()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgStatus), expectedReplySize: 9);
            if (rep[4] != IPC_OK) throw PineFail("MsgStatus");
            return (EmuStatus)ReadU32LE(rep, 5);
        }

        /// <summary>Returns the PCSX2 emulator version string.</summary>
        public string GetVersion() => QueryString(MsgVersion);

        /// <summary>Returns the running game title.</summary>
        public string GetGameTitle() => QueryString(MsgTitle);

        /// <summary>Returns the running game serial ID (e.g. "SCUS-97113").</summary>
        public string GetGameID() => QueryString(MsgID);

        /// <summary>Returns the running game UUID.</summary>
        public string GetGameUUID() => QueryString(MsgUUID);

        /// <summary>Returns the running game version string.</summary>
        public string GetGameVersion() => QueryString(MsgGameVersion);

        /// <summary>Saves a PCSX2 save-state to <paramref name="slot"/>.</summary>
        public void SaveState(byte slot)
        {
            byte[] rep = SendReceive(BuildStateRequest(MsgSaveState, slot), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail($"MsgSaveState slot {slot}");
        }

        /// <summary>Loads a PCSX2 save-state from <paramref name="slot"/>.</summary>
        public void LoadState(byte slot)
        {
            byte[] rep = SendReceive(BuildStateRequest(MsgLoadState, slot), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail($"MsgLoadState slot {slot}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // Extended PINE API (opcodes 0x10–0x17 — requires PCSX2 PR)
        // See PINE_EXTENSION_PROPOSAL.md for the C++ implementation.
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// [Extended PINE] Returns the current EE PC and whether emulation is paused.
        /// Reply: <c>[size(4)][IPC_OK(1)][PC(4)][IsPaused(1)]</c>
        /// </summary>
        /// <param name="isPaused">Set to <c>true</c> if the emulator is currently paused.</param>
        /// <returns>EE program counter (KSEG0-normalised).</returns>
        public uint GetProgramCounter(out bool isPaused)
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgGetProgramCounter), expectedReplySize: 10);
            if (rep[4] != IPC_OK) throw PineFail("MsgGetProgramCounter");
            isPaused = rep[9] != 0;
            return ReadU32LE(rep, 5);
        }

        /// <summary>Convenience overload that ignores the paused flag.</summary>
        public uint GetProgramCounter() => GetProgramCounter(out _);

        /// <summary><see cref="IDebugClient"/> bridge — maps the PINE-specific
        /// <see cref="EmuStatus"/> onto the cross-runtime
        /// <see cref="DebugClientStatus"/>.</summary>
        public DebugClientStatus GetStatusGeneric() => (DebugClientStatus)(uint)GetStatus();

        /// <summary>
        /// [Extended PINE] Pauses EE execution.
        /// </summary>
        public void Pause()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgPause), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgPause");
        }

        /// <summary>
        /// [Extended PINE] Resumes EE execution.
        /// </summary>
        public void Resume()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgResume), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgResume");
        }

        /// <summary>
        /// [Extended PINE] Executes exactly one EE instruction while staying paused,
        /// then returns the new PC.
        /// </summary>
        /// <returns>EE PC after stepping (KSEG0-normalised).</returns>
        /// <exception cref="InvalidOperationException">Thrown if the emulator is not paused.</exception>
        public uint Step()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgStep), expectedReplySize: 9);
            if (rep[4] != IPC_OK) throw PineFail("MsgStep (emulator must be paused)");
            return ReadU32LE(rep, 5);
        }

        /// <summary>
        /// [Extended PINE] Adds a PC execution breakpoint at <paramref name="address"/>.
        /// </summary>
        public void SetBreakpoint(uint address)
        {
            byte[] rep = SendReceive(BuildAddressRequest(MsgSetBreakpoint, address), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail($"MsgSetBreakpoint 0x{address:X8}");
        }

        /// <summary>
        /// [Extended PINE] Removes the breakpoint at <paramref name="address"/>.
        /// Returns <c>false</c> when no breakpoint existed at that address.
        /// </summary>
        public bool ClearBreakpoint(uint address)
        {
            byte[] rep = SendReceive(BuildAddressRequest(MsgClearBreakpoint, address), expectedReplySize: 5);
            return rep[4] == IPC_OK;
        }

        /// <summary>
        /// [Extended PINE] Removes all PC breakpoints.
        /// </summary>
        public void ClearAllBreakpoints()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgClearAllBreakpoints), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgClearAllBreakpoints");
        }

        /// <summary>
        /// [Extended PINE] Returns a snapshot of all 32 EE GPRs (lower 32-bit halves),
        /// PC, HI, and LO.
        /// Reply size: 145 bytes.
        /// </summary>
        public RegisterSnapshot GetRegisters()
        {
            // PINE MsgGetRegisters returns r0–r31 (lower 32), PC, HI, LO.
            // To expose *all* registers to the UI, we augment this with
            // category-wise MsgGetRegister calls (u128) for every EE register set.
            var gprLo32   = new uint[32];
            var gprHi32   = new uint[32];
            var gprUpLo32 = new uint[32];
            var gprUpHi32 = new uint[32];
            var gpr64     = new ulong[32];

            uint pc = 0, hi = 0, lo = 0;

            // First, use the bulk GetRegisters reply when available to populate
            // the basic GPR + PC/HI/LO fields (fast path, works with legacy PCSX2).
            try
            {
                byte[] rep = SendReceive(BuildSingleRequest(MsgGetRegisters), expectedReplySize: 145);
                if (rep[4] == IPC_OK && rep.Length >= 145)
                {
                    for (int i = 0; i < 32; i++)
                    {
                        uint val = ReadU32LE(rep, 5 + i * 4);
                        gprLo32[i] = val;
                        gpr64[i] = val;
                    }
                    pc = ReadU32LE(rep, 5 + 128);
                    hi = ReadU32LE(rep, 5 + 132);
                    lo = ReadU32LE(rep, 5 + 136);
                }
            }
            catch
            {
                // Ignore bulk failure; we'll fall back to per-register reads.
            }

            // Category constants from DebugInterface.h (EECAT_*)
            const byte CatGpr    = 0;
            const byte CatCp0    = 1;
            const byte CatFpr    = 2;
            const byte CatFcr    = 3;
            const byte CatVu0f   = 4;
            const byte CatVu0i   = 5;
            const byte CatGsPriv = 6;

            // Helper to ensure an array has the expected length (defensive for legacy replies).
            static T[] EnsureLength<T>(T[] arr, int len) => arr.Length == len ? arr : new T[len];

            // Fill GPR/PC/HI/LO from GetRegister to capture upper 96 bits too.
            for (int i = 0; i < 35; i++)
            {
                byte[] val = GetRegister(cpuSel: 0, cat: CatGpr, num: (byte)i);
                SplitU128(val, out uint lo32, out uint hi32, out uint upLo32, out uint upHi32,
                    out ulong lo64, out _);
                if (i < 32)
                {
                    gprLo32[i]   = lo32;
                    gprHi32[i]   = hi32;
                    gprUpLo32[i] = upLo32;
                    gprUpHi32[i] = upHi32;
                    gpr64[i]     = lo64;
                }
                else if (i == 32)
                {
                    pc = lo32;
                }
                else if (i == 33)
                {
                    hi = lo32;
                }
                else if (i == 34)
                {
                    lo = lo32;
                }
            }

            // COP0 (32 × 32-bit)
            var cop0 = new uint[32];
            for (int i = 0; i < 32; i++)
            {
                byte[] val = GetRegister(0, CatCp0, (byte)i);
                cop0[i] = ReadU32LE(val, 0);
            }

            // FPR (32 × 32-bit floats as raw bits)
            var fpr = new uint[32];
            uint fprAcc = 0;
            for (int i = 0; i < 32; i++)
            {
                byte[] val = GetRegister(0, CatFpr, (byte)i);
                fpr[i] = ReadU32LE(val, 0);
            }
            // FPR accumulator (optional; ignore failures on older PCSX2 builds).
            try
            {
                byte[] accVal = GetRegister(0, CatFpr, 32);
                fprAcc = ReadU32LE(accVal, 0);
            }
            catch { /* ACC not exposed on older builds */ }

            // FCR (32 × 32-bit control regs; UI uses 0 and 31)
            var fcr = new uint[32];
            for (int i = 0; i < 32; i++)
            {
                byte[] val = GetRegister(0, CatFcr, (byte)i);
                fcr[i] = ReadU32LE(val, 0);
            }

            // VU0 float registers (vf0–vf31, ACC at index 32)
            var vu0fX = new uint[32];
            var vu0fY = new uint[32];
            var vu0fZ = new uint[32];
            var vu0fW = new uint[32];
            uint vu0AccX = 0, vu0AccY = 0, vu0AccZ = 0, vu0AccW = 0;
            uint vu0Q = 0, vu0P = 0, vu0I = 0;

            for (int i = 0; i < 33; i++)
            {
                byte[] val = GetRegister(0, CatVu0f, (byte)i);
                SplitVector(val, out uint x, out uint y, out uint z, out uint w);
                if (i < 32)
                {
                    vu0fX[i] = x; vu0fY[i] = y; vu0fZ[i] = z; vu0fW[i] = w;
                }
                else
                {
                    vu0AccX = x; vu0AccY = y; vu0AccZ = z; vu0AccW = w;
                }
            }

            // VU0 integer/control registers (vi00–vi31)
            var vu0i = new uint[32];
            for (int i = 0; i < 32; i++)
            {
                byte[] val = GetRegister(0, CatVu0i, (byte)i);
                uint raw = ReadU32LE(val, 0);
                vu0i[i] = raw;
                if (i == 21) vu0Q = raw;      // vi21 = Q
                else if (i == 22) vu0I = raw; // vi22 = I
                else if (i == 23) vu0P = raw; // vi23 = P
            }

            // P is not exposed via the VU0 register categories; leave as 0 (not used by PCSX2 debugger).

            // GS privileged registers (19 × 64-bit)
            var gsPriv = new ulong[19];
            for (int i = 0; i < 19; i++)
            {
                byte[] val = GetRegister(0, CatGsPriv, (byte)i);
                SplitU128(val, out _, out _, out _, out _, out ulong lo64, out _);
                gsPriv[i] = lo64;
            }

            return new RegisterSnapshot(
                gpr64,
                EnsureLength(gprLo32, 32),
                EnsureLength(gprHi32, 32),
                EnsureLength(gprUpLo32, 32),
                EnsureLength(gprUpHi32, 32),
                pc,
                hi,
                lo,
                0,   // HI1 not exposed by PCSX2
                0,   // LO1 not exposed by PCSX2
                0,   // SA not exposed by PCSX2
                cop0,
                fpr,
                fprAcc,
                fcr,
                vu0fX,
                vu0fY,
                vu0fZ,
                vu0fW,
                vu0AccX,
                vu0AccY,
                vu0AccZ,
                vu0AccW,
                vu0Q,
                vu0P,
                vu0I,
                vu0i,
                gsPriv
            );
        }

        // ═════════════════════════════════════════════════════════════════════
        // Extended PINE API — opcodes 0x18–0x2D
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// [Extended PINE] Reads one register as a 128-bit little-endian value (16 bytes).
        /// <para>Request: <c>[8][opcode][cpuSel][cat][num]</c></para>
        /// <para>Reply:   <c>[21][IPC_OK][u128 (16 bytes)]</c></para>
        /// </summary>
        /// <param name="cpuSel">CPU selector: 0 = EE, 1 = IOP.</param>
        /// <param name="cat">Register category (architecture-defined).</param>
        /// <param name="num">Register index within the category.</param>
        /// <returns>16-byte little-endian u128 register value.</returns>
        public byte[] GetRegister(byte cpuSel, byte cat, byte num)
        {
            var req = new byte[8];
            WriteU32LE(req, 0, 8u);
            req[4] = MsgGetRegister;
            req[5] = cpuSel;
            req[6] = cat;
            req[7] = num;
            byte[] rep = SendReceive(req, expectedReplySize: 21); // 5 header + 16 u128
            if (rep[4] != IPC_OK) throw PineFail("MsgGetRegister");
            var val = new byte[16];
            Buffer.BlockCopy(rep, 5, val, 0, 16);
            return val;
        }

        /// <summary>
        /// [Extended PINE] Writes one register from a 128-bit little-endian value (16 bytes).
        /// <para>Request: <c>[24][opcode][cpuSel][cat][num][val(16)]</c></para>
        /// </summary>
        /// <param name="cpuSel">CPU selector: 0 = EE, 1 = IOP.</param>
        /// <param name="cat">Register category (architecture-defined).</param>
        /// <param name="num">Register index within the category.</param>
        /// <param name="val128">16-byte little-endian u128 value to write.</param>
        public void SetRegister(byte cpuSel, byte cat, byte num, byte[] val128)
        {
            if (val128 == null || val128.Length != 16)
                throw new ArgumentException("val128 must be exactly 16 bytes", nameof(val128));
            var req = new byte[24];
            WriteU32LE(req, 0, 24u);
            req[4] = MsgSetRegister;
            req[5] = cpuSel;
            req[6] = cat;
            req[7] = num;
            Buffer.BlockCopy(val128, 0, req, 8, 16);
            byte[] rep = SendReceive(req, expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgSetRegister");
        }

        /// <summary>
        /// [Extended PINE] Returns a list of all EE (R5900) OS threads.
        /// </summary>
        public List<PineThread> GetEEThreads()
            => GetThreadList(MsgGetEEThreads, "MsgGetEEThreads");

        /// <summary>
        /// [Extended PINE] Returns a list of all IOP (R3000) OS threads.
        /// </summary>
        public List<PineThread> GetIOPThreads()
            => GetThreadList(MsgGetIOPThreads, "MsgGetIOPThreads");

        private List<PineThread> GetThreadList(byte opcode, string opName)
        {
            byte[] rep = SendReceiveVariable(BuildSingleRequest(opcode));
            if (rep[4] != IPC_OK) throw PineFail(opName);
            if (rep.Length < 9) return new List<PineThread>();

            uint count = ReadU32LE(rep, 5);
            var threads = new List<PineThread>((int)count);
            int pos = 9;
            const int ThreadEntrySize = 22; // TID(4)+PC(4)+Status(1)+Wait(1)+Priority(4)+EntryPoint(4)+StackTop(4)
            for (uint i = 0; i < count && pos + ThreadEntrySize <= rep.Length; i++)
            {
                uint tid      = ReadU32LE(rep, pos);
                uint pc       = ReadU32LE(rep, pos + 4);
                byte status   = rep[pos + 8];
                byte wait     = rep[pos + 9];
                uint priority = ReadU32LE(rep, pos + 10);
                uint entry    = ReadU32LE(rep, pos + 14);
                uint stackTop = ReadU32LE(rep, pos + 18);
                threads.Add(new PineThread(tid, pc, status, wait, priority, entry, stackTop));
                pos += ThreadEntrySize;
            }
            return threads;
        }

        /// <summary>
        /// [Extended PINE] Returns a list of all loaded IOP modules.
        /// </summary>
        public List<IopModule> GetModules()
        {
            byte[] rep = SendReceiveVariable(BuildSingleRequest(MsgGetModules));
            if (rep[4] != IPC_OK) throw PineFail("MsgGetModules");
            if (rep.Length < 9) return new List<IopModule>();

            uint count = ReadU32LE(rep, 5);
            var modules = new List<IopModule>((int)count);
            int pos = 9;
            const int ModuleEntrySize = 58; // Name[32]+Version(2)+TextAddr(4)+Entry(4)+GP(4)+TextSize(4)+DataSize(4)+BssSize(4)
            for (uint i = 0; i < count && pos + ModuleEntrySize <= rep.Length; i++)
            {
                // name: 32 bytes, NUL-padded
                int nameEnd = pos;
                while (nameEnd < pos + 32 && rep[nameEnd] != 0) nameEnd++;
                string name      = Encoding.ASCII.GetString(rep, pos, nameEnd - pos);
                ushort version   = (ushort)(rep[pos + 32] | (rep[pos + 33] << 8));
                uint   textAddr  = ReadU32LE(rep, pos + 34);
                uint   entry     = ReadU32LE(rep, pos + 38);
                uint   gp        = ReadU32LE(rep, pos + 42);
                uint   textSize  = ReadU32LE(rep, pos + 46);
                uint   dataSize  = ReadU32LE(rep, pos + 50);
                uint   bssSize   = ReadU32LE(rep, pos + 54);
                modules.Add(new IopModule(name, version, textAddr, entry, gp, textSize, dataSize, bssSize));
                pos += ModuleEntrySize;
            }
            return modules;
        }

        /// <summary>
        /// [Extended PINE] Walks the EE call stack for the currently running thread and
        /// returns the list of stack frames.
        /// </summary>
        public List<PineStackFrame> GetStack()
        {
            byte[] rep = SendReceiveVariable(BuildSingleRequest(MsgGetStack));
            if (rep[4] != IPC_OK) throw PineFail("MsgGetStack");
            if (rep.Length < 9) return new List<PineStackFrame>();

            uint count = ReadU32LE(rep, 5);
            var frames = new List<PineStackFrame>((int)count);
            int pos = 9;
            const int FrameEntrySize = 16; // entry(4)+pc(4)+sp(4)+stackSize(4)
            for (uint i = 0; i < count && pos + FrameEntrySize <= rep.Length; i++)
            {
                uint entry     = ReadU32LE(rep, pos);
                uint pc        = ReadU32LE(rep, pos + 4);
                uint sp        = ReadU32LE(rep, pos + 8);
                uint stackSize = ReadU32LE(rep, pos + 12);
                frames.Add(new PineStackFrame(entry, pc, sp, stackSize));
                pos += FrameEntrySize;
            }
            return frames;
        }

        /// <summary>
        /// [Extended PINE] Steps into the next EE instruction, following branches and syscalls.
        /// Non-blocking: resumes the CPU with a temporary one-shot breakpoint.
        /// </summary>
        public void StepInto()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgStepInto), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgStepInto (emulator must be paused)");
        }

        /// <summary>
        /// [Extended PINE] Steps over the next EE instruction, treating function calls as atomic.
        /// Non-blocking: resumes the CPU with a temporary one-shot breakpoint.
        /// </summary>
        public void StepOver()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgStepOver), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgStepOver (emulator must be paused)");
        }

        /// <summary>
        /// [Extended PINE] Steps out of the current EE function, stopping at the return address.
        /// Non-blocking: resumes the CPU with a temporary one-shot breakpoint.
        /// </summary>
        public void StepOut()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgStepOut), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgStepOut (emulator must be paused, call stack depth ≥ 2)");
        }

        /// <summary>
        /// [Extended PINE] Looks up the EE function symbol overlapping <paramref name="address"/>.
        /// </summary>
        /// <returns>
        /// A tuple of (symbol start address, symbol name), or <c>null</c> if no symbol
        /// is defined at that address.
        /// </returns>
        public (uint StartAddress, string Name)? GetSymbol(uint address)
        {
            byte[] rep = SendReceiveVariable(BuildAddressRequest(MsgGetSymbol, address));
            if (rep[4] != IPC_OK) return null;
            if (rep.Length < 9) return null;
            uint symStart = ReadU32LE(rep, 5);
            // name follows as NUL-terminated UTF-8
            int nameStart = 9;
            int nameEnd = nameStart;
            while (nameEnd < rep.Length && rep[nameEnd] != 0) nameEnd++;
            string name = Encoding.UTF8.GetString(rep, nameStart, nameEnd - nameStart);
            return (symStart, name);
        }

        /// <summary>
        /// [Extended PINE] Saves the current emulator state to <paramref name="filePath"/>.
        /// </summary>
        public void SaveStateFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("filePath must not be empty", nameof(filePath));
            byte[] pathBytes = Encoding.UTF8.GetBytes(filePath);
            if (pathBytes.Length > 512)
                throw new ArgumentException("filePath must be ≤ 512 bytes when UTF-8 encoded", nameof(filePath));
            ushort pathLen = (ushort)pathBytes.Length;
            var req = new byte[4 + 1 + 2 + pathLen];
            WriteU32LE(req, 0, (uint)req.Length);
            req[4] = MsgSaveStateFile;
            req[5] = (byte)(pathLen & 0xFF);
            req[6] = (byte)(pathLen >> 8);
            Buffer.BlockCopy(pathBytes, 0, req, 7, pathLen);
            byte[] rep = SendReceive(req, expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail($"MsgSaveStateFile \"{filePath}\"");
        }

        /// <summary>
        /// [Extended PINE] Loads emulator state from <paramref name="filePath"/>.
        /// </summary>
        /// <returns><c>true</c> on success, <c>false</c> if the load failed (file not found, etc.).</returns>
        public bool LoadStateFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("filePath must not be empty", nameof(filePath));
            byte[] pathBytes = Encoding.UTF8.GetBytes(filePath);
            if (pathBytes.Length > 512)
                throw new ArgumentException("filePath must be ≤ 512 bytes when UTF-8 encoded", nameof(filePath));
            ushort pathLen = (ushort)pathBytes.Length;
            var req = new byte[4 + 1 + 2 + pathLen];
            WriteU32LE(req, 0, (uint)req.Length);
            req[4] = MsgLoadStateFile;
            req[5] = (byte)(pathLen & 0xFF);
            req[6] = (byte)(pathLen >> 8);
            Buffer.BlockCopy(pathBytes, 0, req, 7, pathLen);
            byte[] rep = SendReceive(req, expectedReplySize: 5);
            return rep[4] == IPC_OK;
        }

        /// <summary>
        /// [Extended PINE] Performs a cold-boot reset of the emulator.
        /// </summary>
        public void Reset()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgReset), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgReset");
        }

        /// <summary>
        /// [Extended PINE] Advances emulation by <paramref name="numFrames"/> video frames,
        /// then pauses. Must be called with the emulator running.
        /// </summary>
        public void FrameAdvance(byte numFrames)
        {
            if (numFrames == 0) throw new ArgumentOutOfRangeException(nameof(numFrames), "numFrames must be ≥ 1");
            var req = new byte[6];
            WriteU32LE(req, 0, 6u);
            req[4] = MsgFrameAdvance;
            req[5] = numFrames;
            byte[] rep = SendReceive(req, expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgFrameAdvance");
        }

        /// <summary>
        /// [Extended PINE] Returns the current emulated frames-per-second.
        /// </summary>
        public float GetFPS()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgGetFPS), expectedReplySize: 9);
            if (rep[4] != IPC_OK) throw PineFail("MsgGetFPS");
            return BitConverter.ToSingle(rep, 5);
        }

        /// <summary>
        /// [Extended PINE] Sets the emulator speed-limiter mode.
        /// </summary>
        /// <param name="mode">0 = Nominal, 1 = Turbo, 2 = Slomo, 3 = Unlimited.</param>
        public void SetLimiterMode(byte mode)
        {
            if (mode > 3) throw new ArgumentOutOfRangeException(nameof(mode), "mode must be 0–3");
            var req = new byte[6];
            WriteU32LE(req, 0, 6u);
            req[4] = MsgSetLimiterMode;
            req[5] = mode;
            byte[] rep = SendReceive(req, expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgSetLimiterMode");
        }

        /// <summary>
        /// [Extended PINE] Lists all active breakpoints.
        /// </summary>
        /// <param name="cpuSel">CPU filter: 0 = EE only, 1 = IOP only, 0xFF = both.</param>
        public List<PineBreakpointEntry> ListBreakpoints(byte cpuSel = 0xFF)
        {
            var req = new byte[6];
            WriteU32LE(req, 0, 6u);
            req[4] = MsgListBreakpoints;
            req[5] = cpuSel;
            byte[] rep = SendReceiveVariable(req);
            if (rep[4] != IPC_OK) throw PineFail("MsgListBreakpoints");
            if (rep.Length < 9) return new List<PineBreakpointEntry>();

            uint count = ReadU32LE(rep, 5);
            var bps = new List<PineBreakpointEntry>((int)count);
            int pos = 9;
            const int BpEntrySize = 6; // addr(4)+enabled(1)+cpu(1)
            for (uint i = 0; i < count && pos + BpEntrySize <= rep.Length; i++)
            {
                uint addr    = ReadU32LE(rep, pos);
                bool enabled = rep[pos + 4] != 0;
                byte cpu     = rep[pos + 5];
                bps.Add(new PineBreakpointEntry(addr, enabled, cpu));
                pos += BpEntrySize;
            }
            return bps;
        }

        /// <summary>
        /// [Extended PINE] Disassembles <paramref name="count"/> instructions at <paramref name="address"/>.
        /// </summary>
        /// <param name="cpuSel">CPU selector: 0 = EE, 1 = IOP.</param>
        /// <param name="address">Starting address.</param>
        /// <param name="count">Number of instructions to disassemble (max 1000).</param>
        public List<PineDisassemblyLine> Disassemble(byte cpuSel, uint address, ushort count)
        {
            var req = new byte[12]; // size(4)+opcode(1)+cpu(1)+addr(4)+count(2)
            WriteU32LE(req, 0, 12u);
            req[4] = MsgDisassemble;
            req[5] = cpuSel;
            WriteU32LE(req, 6, address);
            req[10] = (byte)(count & 0xFF);
            req[11] = (byte)(count >> 8);
            byte[] rep = SendReceiveVariable(req);
            if (rep[4] != IPC_OK) throw PineFail("MsgDisassemble");
            if (rep.Length < 7) return new List<PineDisassemblyLine>();

            ushort returned = (ushort)(rep[5] | (rep[6] << 8));
            var lines = new List<PineDisassemblyLine>(returned);
            int pos = 7;
            for (int i = 0; i < returned; i++)
            {
                if (pos + 5 > rep.Length) break;
                uint lineAddr = ReadU32LE(rep, pos);
                byte textLen  = rep[pos + 4];
                pos += 5;
                if (pos + textLen > rep.Length) break;
                string text = Encoding.UTF8.GetString(rep, pos, textLen);
                pos += textLen;
                lines.Add(new PineDisassemblyLine(lineAddr, text));
            }
            return lines;
        }

        /// <summary>
        /// [Extended PINE] Returns a paginated list of EE function symbols.
        /// </summary>
        /// <param name="offset">Zero-based start index into the full symbol list.</param>
        /// <param name="maxCount">Maximum number of symbols to return (up to 3000).</param>
        /// <returns>A tuple of (total symbol count, symbols in this page).</returns>
        public (uint Total, List<PineSymbolEntry> Symbols) ListFunctions(uint offset = 0, ushort maxCount = 256)
            => ListSymbols(MsgListFunctions, offset, maxCount, "MsgListFunctions");

        /// <summary>
        /// [Extended PINE] Returns a paginated list of EE global variable symbols.
        /// </summary>
        /// <param name="offset">Zero-based start index into the full symbol list.</param>
        /// <param name="maxCount">Maximum number of symbols to return (up to 3000).</param>
        /// <returns>A tuple of (total symbol count, symbols in this page).</returns>
        public (uint Total, List<PineSymbolEntry> Symbols) ListGlobals(uint offset = 0, ushort maxCount = 256)
            => ListSymbols(MsgListGlobals, offset, maxCount, "MsgListGlobals");

        private (uint Total, List<PineSymbolEntry> Symbols) ListSymbols(byte opcode, uint offset, ushort maxCount, string opName)
        {
            var req = new byte[11]; // size(4)+opcode(1)+offset(4)+maxCount(2)
            WriteU32LE(req, 0, 11u);
            req[4] = opcode;
            WriteU32LE(req, 5, offset);
            req[9]  = (byte)(maxCount & 0xFF);
            req[10] = (byte)(maxCount >> 8);
            byte[] rep = SendReceiveVariable(req);
            if (rep[4] != IPC_OK) throw PineFail(opName);
            if (rep.Length < 11) return (0, new List<PineSymbolEntry>());

            uint   total    = ReadU32LE(rep, 5);
            ushort returned = (ushort)(rep[9] | (rep[10] << 8));
            var symbols = new List<PineSymbolEntry>(returned);
            int pos = 11;
            for (int i = 0; i < returned; i++)
            {
                if (pos + 9 > rep.Length) break; // addr(4)+size(4)+nameLen(1)
                uint symAddr = ReadU32LE(rep, pos);
                uint symSize = ReadU32LE(rep, pos + 4);
                byte nameLen = rep[pos + 8];
                pos += 9;
                if (pos + nameLen > rep.Length) break;
                string name = Encoding.UTF8.GetString(rep, pos, nameLen);
                pos += nameLen;
                symbols.Add(new PineSymbolEntry(symAddr, symSize, name));
            }
            return (total, symbols);
        }

        /// <summary>
        /// [Extended PINE] Looks up an EE symbol by name.
        /// </summary>
        /// <returns>A tuple of (address, size) or <c>null</c> if the symbol is not found.</returns>
        public (uint Address, uint Size)? GetSymbolByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("name must not be empty", nameof(name));
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            if (nameBytes.Length > 255)
                throw new ArgumentException("name must be ≤ 255 bytes when UTF-8 encoded", nameof(name));
            byte nameLen = (byte)nameBytes.Length;
            var req = new byte[4 + 1 + 1 + nameLen];
            WriteU32LE(req, 0, (uint)req.Length);
            req[4] = MsgGetSymbolByName;
            req[5] = nameLen;
            Buffer.BlockCopy(nameBytes, 0, req, 6, nameLen);
            byte[] rep = SendReceive(req, expectedReplySize: 13); // 5 header + addr(4) + size(4)
            if (rep[4] != IPC_OK) return null;
            uint addr = ReadU32LE(rep, 5);
            uint size = ReadU32LE(rep, 9);
            return (addr, size);
        }

        /// <summary>
        /// [Extended PINE] Lists local variables and parameters for the function containing
        /// <paramref name="address"/>.
        /// </summary>
        public List<PineLocalEntry> GetLocals(uint address)
        {
            byte[] rep = SendReceiveVariable(BuildAddressRequest(MsgGetLocals, address));
            if (rep[4] != IPC_OK) return new List<PineLocalEntry>();
            if (rep.Length < 9) return new List<PineLocalEntry>();

            uint count = ReadU32LE(rep, 5);
            var locals = new List<PineLocalEntry>((int)count);
            int pos = 9;
            for (uint i = 0; i < count; i++)
            {
                if (pos + 6 > rep.Length) break; // storageType(1)+value(4)+nameLen(1)
                byte storageType = rep[pos];
                int  value       = (int)ReadU32LE(rep, pos + 1);
                byte nameLen     = rep[pos + 5];
                pos += 6;
                if (pos + nameLen > rep.Length) break;
                string localName = Encoding.UTF8.GetString(rep, pos, nameLen);
                pos += nameLen;
                locals.Add(new PineLocalEntry(storageType, value, localName));
            }
            return locals;
        }

        // ═════════════════════════════════════════════════════════════════════
        // Watchpoint / snapshot extension (opcodes 0x2E–0x32)
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// [Extended PINE] Adds a memory watchpoint on <paramref name="cpu"/> for the
        /// address range [<paramref name="start"/>, <paramref name="end"/>) (end is exclusive).
        /// <para>
        /// Backed by PCSX2's CBreakPoints MemCheck infrastructure (MEMCHECK_BREAK).
        /// JIT-only — no effect under the interpreter or HLE.
        /// </para>
        /// </summary>
        /// <param name="cpu">CPU to watch: 0 = EE, 1 = IOP.</param>
        /// <param name="start">Inclusive start address of the range to watch.</param>
        /// <param name="end">Exclusive end address of the range to watch.</param>
        /// <param name="cond">Condition bitmask: 0x01 = read, 0x02 = write, 0x03 = read+write.</param>
        public void AddWatch(byte cpu, uint start, uint end, byte cond)
        {
            // Request: [size(4)][opcode(1)][cpu(1)][start(4)][end(4)][cond(1)]  = 15 bytes
            var req = new byte[15];
            WriteU32LE(req, 0, 15u);
            req[4]  = MsgAddWatch;
            req[5]  = cpu;
            WriteU32LE(req, 6, start);
            WriteU32LE(req, 10, end);
            req[14] = cond;
            byte[] rep = SendReceive(req, expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail($"MsgAddWatch cpu={cpu} 0x{start:X8}–0x{end:X8}");
        }

        /// <summary>
        /// [Extended PINE] Removes the memory watchpoint on <paramref name="cpu"/> for
        /// the address range [<paramref name="start"/>, <paramref name="end"/>).
        /// </summary>
        /// <param name="cpu">CPU selector: 0 = EE, 1 = IOP.</param>
        /// <param name="start">Inclusive start address of the range that was registered.</param>
        /// <param name="end">Exclusive end address of the range that was registered.</param>
        /// <returns><c>true</c> if the watchpoint was found and removed; <c>false</c> if no matching range exists.</returns>
        public bool RemoveWatch(byte cpu, uint start, uint end)
        {
            // Request: [size(4)][opcode(1)][cpu(1)][start(4)][end(4)]  = 14 bytes
            var req = new byte[14];
            WriteU32LE(req, 0, 14u);
            req[4] = MsgRemoveWatch;
            req[5] = cpu;
            WriteU32LE(req, 6, start);
            WriteU32LE(req, 10, end);
            byte[] rep = SendReceive(req, expectedReplySize: 5);
            return rep[4] == IPC_OK;
        }

        /// <summary>
        /// [Extended PINE] Lists all active memory watchpoints.
        /// </summary>
        /// <param name="cpuSel">CPU filter: 0 = EE only, 1 = IOP only, 0xFF = both.</param>
        public List<PineWatchEntry> ListWatches(byte cpuSel = 0xFF)
        {
            // Request: [size(4)][opcode(1)][cpuSel(1)]  = 6 bytes
            var req = new byte[6];
            WriteU32LE(req, 0, 6u);
            req[4] = MsgListWatches;
            req[5] = cpuSel;
            byte[] rep = SendReceiveVariable(req);
            if (rep[4] != IPC_OK) throw PineFail("MsgListWatches");
            if (rep.Length < 9) return new List<PineWatchEntry>();

            uint count = ReadU32LE(rep, 5);
            var watches = new List<PineWatchEntry>((int)count);
            int pos = 9;
            const int WatchEntrySize = 10; // cpu(1)+start(4)+end(4)+cond(1)
            for (uint i = 0; i < count && pos + WatchEntrySize <= rep.Length; i++)
            {
                byte cpu   = rep[pos];
                uint wStart = ReadU32LE(rep, pos + 1);
                uint wEnd   = ReadU32LE(rep, pos + 5);
                byte cond  = rep[pos + 9];
                watches.Add(new PineWatchEntry(cpu, wStart, wEnd, cond));
                pos += WatchEntrySize;
            }
            return watches;
        }

        /// <summary>
        /// [Extended PINE] Clears all active memory watchpoints on all CPUs.
        /// </summary>
        public void ClearAllWatches()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgClearAllWatches), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgClearAllWatches");
        }

        /// <summary>
        /// [Extended PINE] Triggers a screenshot save in PCSX2.
        /// </summary>
        public void SaveScreenshot()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgSaveScreenshot), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgSaveScreenshot");
        }

        // ── GS stream control ─────────────────────────────────────────────────

        /// <summary>Status snapshot returned by <see cref="GSStreamGetStatus"/>.</summary>
        public sealed class GSStreamStatus
        {
            public bool   Active           { get; }
            public ushort Port             { get; }
            public uint   Subscribers      { get; }
            public uint   FramesDelivered  { get; }
            public uint   FramesDropped    { get; }

            internal GSStreamStatus(bool active, ushort port, uint subs, uint delivered, uint dropped)
            {
                Active          = active;
                Port            = port;
                Subscribers     = subs;
                FramesDelivered = delivered;
                FramesDropped   = dropped;
            }
        }

        /// <summary>
        /// [GS stream control] Returns the TCP port on which PCSX2's GSStreamServer
        /// is listening. 0 means the listener is not initialized in this PCSX2
        /// instance (older build, or PINE disabled).
        /// </summary>
        public ushort GSStreamGetPort()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgGSStreamGetPort), expectedReplySize: 7);
            if (rep[4] != IPC_OK) throw PineFail("MsgGSStreamGetPort");
            return ReadU16LE(rep, 5);
        }

        /// <summary>
        /// [GS stream control] Returns runtime statistics for the GSStreamServer:
        /// whether it is initialized, the port it is bound to, current subscriber
        /// count, and frame counters.
        /// </summary>
        public GSStreamStatus GSStreamGetStatus()
        {
            // Reply: status(1) + active(1) + port(2) + subs(4) + delivered(4) + dropped(4) = 16 bytes
            byte[] rep = SendReceive(BuildSingleRequest(MsgGSStreamGetStatus), expectedReplySize: 20);
            if (rep[4] != IPC_OK) throw PineFail("MsgGSStreamGetStatus");
            bool   active     = rep[5] != 0;
            ushort port       = ReadU16LE(rep, 6);
            uint   subs       = ReadU32LE(rep, 8);
            uint   delivered  = ReadU32LE(rep, 12);
            uint   dropped    = ReadU32LE(rep, 16);
            return new GSStreamStatus(active, port, subs, delivered, dropped);
        }

        /// <summary>
        /// [GS stream control] Lightweight liveness probe for the GS stream
        /// control plane. Returns successfully if the PCSX2 build understands
        /// the GS stream opcodes; throws otherwise.
        /// </summary>
        public void GSStreamPing()
        {
            byte[] rep = SendReceive(BuildSingleRequest(MsgGSStreamPing), expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgGSStreamPing");
        }

        // ── Pad / controller input ────────────────────────────────────────────

        /// <summary>Full pad state snapshot returned by <see cref="PadGetState"/>.</summary>
        public sealed class PadStateSnapshot
        {
            /// <summary>PS2 button bitmask; a bit that is <b>clear</b> means the button is <b>pressed</b>.</summary>
            public uint Buttons { get; }
            public byte Lx { get; }
            public byte Ly { get; }
            public byte Rx { get; }
            public byte Ry { get; }
            /// <summary>16 pressure values (0–255) in PadDualshock2::Inputs order.</summary>
            public byte[] Pressures { get; }

            internal PadStateSnapshot(uint buttons, byte lx, byte ly, byte rx, byte ry, byte[] pressures)
            {
                Buttons = buttons; Lx = lx; Ly = ly; Rx = rx; Ry = ry; Pressures = pressures;
            }
        }

        /// <summary>
        /// [Extended PINE] Read the full input state of one pad slot (opcode 0x33).
        /// </summary>
        /// <param name="pad">Unified slot index (0–7).</param>
        public PadStateSnapshot PadGetState(byte pad)
        {
            // Request: [6][0x33][pad]
            var req = new byte[6];
            WriteU32LE(req, 0, 6u);
            req[4] = MsgPadGetState;
            req[5] = pad;

            // Reply: [size(4)][IPC_OK(1)][buttons(4)][lx][ly][rx][ry][pressure×16] = 29 bytes
            byte[] rep = SendReceive(req, expectedReplySize: 29);
            if (rep[4] != IPC_OK) throw PineFail("MsgPadGetState");

            uint buttons = ReadU32LE(rep, 5);
            byte lx = rep[9];
            byte ly = rep[10];
            byte rx = rep[11];
            byte ry = rep[12];
            var pressures = new byte[16];
            Buffer.BlockCopy(rep, 13, pressures, 0, 16);
            return new PadStateSnapshot(buttons, lx, ly, rx, ry, pressures);
        }

        /// <summary>
        /// [Extended PINE] Set a single button/axis on a pad by index and normalized value (opcode 0x34).
        /// Routes through Pad::SetControllerState() so deadzone/pressure logic applies.
        /// </summary>
        /// <param name="pad">Unified slot index (0–7).</param>
        /// <param name="buttonIndex">Index in PadDualshock2::Inputs (0–25).</param>
        /// <param name="value">Normalized value 0.0 (released) – 1.0 (fully pressed).</param>
        public void PadSetButton(byte pad, byte buttonIndex, float value)
        {
            // Request: [11][0x34][pad][button_index][value:f32 LE]
            var req = new byte[11];
            WriteU32LE(req, 0, 11u);
            req[4] = MsgPadSetButton;
            req[5] = pad;
            req[6] = buttonIndex;
            WriteFloatLE(req, 7, value);

            byte[] rep = SendReceive(req, expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgPadSetButton");
        }

        /// <summary>
        /// [Extended PINE] Set raw analog stick positions, bypassing deadzone (opcode 0x35).
        /// </summary>
        /// <param name="pad">Unified slot index (0–7).</param>
        public void PadSetAnalog(byte pad, byte lx, byte ly, byte rx, byte ry)
        {
            // Request: [10][0x35][pad][lx][ly][rx][ry]
            var req = new byte[10];
            WriteU32LE(req, 0, 10u);
            req[4] = MsgPadSetAnalog;
            req[5] = pad;
            req[6] = lx;
            req[7] = ly;
            req[8] = rx;
            req[9] = ry;

            byte[] rep = SendReceive(req, expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgPadSetAnalog");
        }

        /// <summary>
        /// [Extended PINE] Atomically replace the complete input state of one pad (opcode 0x36).
        /// </summary>
        /// <param name="pad">Unified slot index (0–7).</param>
        /// <param name="buttons">PS2 button bitmask (reserved; button state derived from pressures).</param>
        /// <param name="pressures">16 pressure bytes in PadDualshock2::Inputs order. Pressure > 0 ⇒ pressed.</param>
        public void PadSetState(byte pad, uint buttons, byte lx, byte ly, byte rx, byte ry, byte[] pressures)
        {
            if (pressures == null || pressures.Length != 16)
                throw new ArgumentException("pressures must be exactly 16 bytes", nameof(pressures));

            // Request: [30][0x36][pad][buttons:u32][lx][ly][rx][ry][pressure×16]
            var req = new byte[30];
            WriteU32LE(req, 0, 30u);
            req[4] = MsgPadSetState;
            req[5] = pad;
            WriteU32LE(req, 6, buttons);
            req[10] = lx;
            req[11] = ly;
            req[12] = rx;
            req[13] = ry;
            Buffer.BlockCopy(pressures, 0, req, 14, 16);

            byte[] rep = SendReceive(req, expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgPadSetState");
        }

        /// <summary>
        /// [Extended PINE] Query the controller type installed in a pad slot (opcode 0x37).
        /// </summary>
        /// <param name="pad">Unified slot index (0–7).</param>
        /// <returns>Controller type value (0=NotConnected, 1=DualShock2, 2=Guitar, 3=Jogcon, 4=Negcon, 5=Popn).</returns>
        public byte PadGetType(byte pad)
        {
            // Request: [6][0x37][pad]
            var req = new byte[6];
            WriteU32LE(req, 0, 6u);
            req[4] = MsgPadGetType;
            req[5] = pad;

            // Reply: [size(4)][IPC_OK(1)][controller_type(1)] = 6 bytes
            byte[] rep = SendReceive(req, expectedReplySize: 6);
            if (rep[4] != IPC_OK) throw PineFail("MsgPadGetType");
            return rep[5];
        }

        // ── DEV9 network bridge ──────────────────────────────────────────────

        /// <summary>Adapter status snapshot returned by <see cref="NetGetStatus"/>.</summary>
        public sealed class NetStatusSnapshot
        {
            /// <summary>True when EthEnable is set and the adapter has been opened.</summary>
            public bool Enabled { get; }
            /// <summary>Pcsx2Config::DEV9Options::NetApi cast to int (0=Unset, 1=PCAP_Bridged, 2=PCAP_Switched, 3=TAP, 4=Sockets).</summary>
            public byte AdapterType { get; }
            /// <summary>PS2 MAC address (6 bytes, zeroed if adapter not active).</summary>
            public byte[] PS2Mac { get; }
            /// <summary>Last known PS2 IPv4 address (4 bytes, zeroed until a packet is seen).</summary>
            public byte[] PS2IP { get; }

            internal NetStatusSnapshot(bool enabled, byte adapterType, byte[] ps2Mac, byte[] ps2IP)
            {
                Enabled = enabled; AdapterType = adapterType; PS2Mac = ps2Mac; PS2IP = ps2IP;
            }
        }

        /// <summary>Captured packet returned by <see cref="NetRecvCapture"/>.</summary>
        public sealed class CapturedPacketSnapshot
        {
            /// <summary>0 = TX (PS2→Host), 1 = RX (Host→PS2).</summary>
            public byte Direction { get; }
            /// <summary>Monotonic timestamp in microseconds.</summary>
            public ulong TimestampUs { get; }
            /// <summary>Raw Ethernet frame bytes.</summary>
            public byte[] Data { get; }

            internal CapturedPacketSnapshot(byte direction, ulong timestampUs, byte[] data)
            {
                Direction = direction; TimestampUs = timestampUs; Data = data;
            }
        }

        /// <summary>Network statistics snapshot returned by <see cref="NetGetStats"/>.</summary>
        public sealed class NetStatsSnapshot
        {
            public uint TxPackets { get; }
            public uint RxPackets { get; }
            public ulong TxBytes { get; }
            public ulong RxBytes { get; }
            public ushort CaptureQueueSize { get; }
            public uint DroppedCount { get; }

            internal NetStatsSnapshot(uint txPkts, uint rxPkts, ulong txBytes, ulong rxBytes, ushort queueSize, uint dropped)
            {
                TxPackets = txPkts; RxPackets = rxPkts; TxBytes = txBytes; RxBytes = rxBytes;
                CaptureQueueSize = queueSize; DroppedCount = dropped;
            }
        }

        /// <summary>
        /// [Extended PINE] Query DEV9 adapter status (opcode 0x38).
        /// Returns enabled state, adapter type, MAC, and IP.
        /// </summary>
        public NetStatusSnapshot NetGetStatus()
        {
            // Request: [5][0x38]
            byte[] rep = SendReceive(BuildSingleRequest(MsgNetGetStatus), expectedReplySize: 17);
            if (rep[4] != IPC_OK) throw PineFail("MsgNetGetStatus");

            bool enabled = rep[5] != 0;
            byte adapterType = rep[6];
            var mac = new byte[6];
            Buffer.BlockCopy(rep, 7, mac, 0, 6);
            var ip = new byte[4];
            Buffer.BlockCopy(rep, 13, ip, 0, 4);
            return new NetStatusSnapshot(enabled, adapterType, mac, ip);
        }

        /// <summary>
        /// [Extended PINE] Enable/disable packet capture (opcode 0x39).
        /// </summary>
        /// <param name="enable">True to enable, false to disable.</param>
        /// <param name="direction">0 = both TX and RX, 1 = TX only, 2 = RX only.</param>
        /// <param name="maxQueue">Maximum capture queue size (0 → default 1000).</param>
        public void NetCapture(bool enable, byte direction = 0, ushort maxQueue = 0)
        {
            // Request: [9][0x39][enable:u8][direction:u8][max_queue:u16 LE]
            var req = new byte[9];
            WriteU32LE(req, 0, 9u);
            req[4] = MsgNetCapture;
            req[5] = enable ? (byte)1 : (byte)0;
            req[6] = direction;
            WriteU16LE(req, 7, maxQueue);

            byte[] rep = SendReceive(req, expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgNetCapture");
        }

        /// <summary>
        /// [Extended PINE] Drain captured packets from the ring buffer (opcode 0x3A).
        /// </summary>
        /// <param name="maxPackets">Maximum number of packets to drain.</param>
        public CapturedPacketSnapshot[] NetRecvCapture(ushort maxPackets)
        {
            // Request: [7][0x3A][max_packets:u16 LE]
            var req = new byte[7];
            WriteU32LE(req, 0, 7u);
            req[4] = MsgNetRecvCapture;
            WriteU16LE(req, 5, maxPackets);

            // Variable-length reply: [size(4)][IPC_OK(1)][count(2)][per-packet records…]
            byte[] rep = SendReceiveVariable(req);
            if (rep[4] != IPC_OK) throw PineFail("MsgNetRecvCapture");

            ushort count = ReadU16LE(rep, 5);
            var result = new CapturedPacketSnapshot[count];
            int offset = 7;
            for (int i = 0; i < count; i++)
            {
                byte dir = rep[offset]; offset += 1;
                ulong ts = ReadU64LE(rep, offset); offset += 8;
                ushort len = ReadU16LE(rep, offset); offset += 2;
                var data = new byte[len];
                Buffer.BlockCopy(rep, offset, data, 0, len);
                offset += len;
                result[i] = new CapturedPacketSnapshot(dir, ts, data);
            }
            return result;
        }

        /// <summary>
        /// [Extended PINE] Inject a raw Ethernet frame into the PS2 RX path (opcode 0x3B).
        /// </summary>
        /// <param name="frame">Raw Ethernet frame bytes.</param>
        /// <returns>True if the frame was successfully injected.</returns>
        public bool NetInject(byte[] frame)
        {
            // Request: [7+N][0x3B][length:u16 LE][data]
            int totalSize = 5 + 2 + frame.Length;
            var req = new byte[totalSize];
            WriteU32LE(req, 0, (uint)totalSize);
            req[4] = MsgNetInject;
            WriteU16LE(req, 5, (ushort)frame.Length);
            Buffer.BlockCopy(frame, 0, req, 7, frame.Length);

            byte[] rep = SendReceive(req, expectedReplySize: 6);
            if (rep[4] != IPC_OK) throw PineFail("MsgNetInject");
            return rep[5] != 0;
        }

        /// <summary>
        /// [Extended PINE] Inject a raw Ethernet frame out through the TX adapter (opcode 0x3C).
        /// </summary>
        /// <param name="frame">Raw Ethernet frame bytes.</param>
        /// <returns>True if the adapter accepted the frame.</returns>
        public bool NetInjectTx(byte[] frame)
        {
            // Request: [7+N][0x3C][length:u16 LE][data]
            int totalSize = 5 + 2 + frame.Length;
            var req = new byte[totalSize];
            WriteU32LE(req, 0, (uint)totalSize);
            req[4] = MsgNetInjectTx;
            WriteU16LE(req, 5, (ushort)frame.Length);
            Buffer.BlockCopy(frame, 0, req, 7, frame.Length);

            byte[] rep = SendReceive(req, expectedReplySize: 6);
            if (rep[4] != IPC_OK) throw PineFail("MsgNetInjectTx");
            return rep[5] != 0;
        }

        /// <summary>
        /// [Extended PINE] Dissect a raw Ethernet frame and return a text summary (opcode 0x3D).
        /// </summary>
        /// <param name="frame">Raw Ethernet frame bytes.</param>
        /// <returns>Human-readable dissection text.</returns>
        public string NetDisassemble(byte[] frame)
        {
            // Request: [7+N][0x3D][length:u16 LE][data]
            int totalSize = 5 + 2 + frame.Length;
            var req = new byte[totalSize];
            WriteU32LE(req, 0, (uint)totalSize);
            req[4] = MsgNetDisassemble;
            WriteU16LE(req, 5, (ushort)frame.Length);
            Buffer.BlockCopy(frame, 0, req, 7, frame.Length);

            // Variable-length reply: [size(4)][IPC_OK(1)][text_len:u16 LE][text]
            byte[] rep = SendReceiveVariable(req);
            if (rep[4] != IPC_OK) throw PineFail("MsgNetDisassemble");

            ushort textLen = ReadU16LE(rep, 5);
            return Encoding.UTF8.GetString(rep, 7, textLen);
        }

        /// <summary>
        /// [Extended PINE] Set the capture filter (opcode 0x3E).
        /// </summary>
        /// <param name="filterType">0 = none, 1 = by protocol, 2 = by port, 3 = by IP.</param>
        /// <param name="protocol">For filterType 1: 0=all, 1=TCP, 2=UDP, 3=ICMP, 4=ARP, 5=DNS, 6=DHCP.</param>
        /// <param name="port">For filterType 2: TCP/UDP port number.</param>
        /// <param name="ip">For filterType 3: IPv4 address (4 bytes, network byte order).</param>
        public void NetSetFilter(byte filterType, byte protocol = 0, ushort port = 0, byte[]? ip = null)
        {
            int extraSize = filterType switch
            {
                1 => 1,  // protocol (u8)
                2 => 2,  // port (u16 LE)
                3 => 4,  // ip (4 bytes)
                _ => 0,
            };
            int totalSize = 5 + 1 + extraSize;
            var req = new byte[totalSize];
            WriteU32LE(req, 0, (uint)totalSize);
            req[4] = MsgNetSetFilter;
            req[5] = filterType;

            if (filterType == 1)
                req[6] = protocol;
            else if (filterType == 2)
                WriteU16LE(req, 6, port);
            else if (filterType == 3 && ip != null && ip.Length >= 4)
                Buffer.BlockCopy(ip, 0, req, 6, 4);

            byte[] rep = SendReceive(req, expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail("MsgNetSetFilter");
        }

        /// <summary>
        /// [Extended PINE] Return TX/RX counters and capture queue statistics (opcode 0x3F).
        /// </summary>
        public NetStatsSnapshot NetGetStats()
        {
            // Request: [5][0x3F]
            // Reply: [size(4)][IPC_OK(1)][tx_packets(4)][rx_packets(4)][tx_bytes(8)][rx_bytes(8)]
            //        [capture_queue_size(2)][dropped_count(4)] = 35 bytes
            byte[] rep = SendReceive(BuildSingleRequest(MsgNetGetStats), expectedReplySize: 35);
            if (rep[4] != IPC_OK) throw PineFail("MsgNetGetStats");

            uint txPkts     = ReadU32LE(rep, 5);
            uint rxPkts     = ReadU32LE(rep, 9);
            ulong txBytes   = ReadU64LE(rep, 13);
            ulong rxBytes   = ReadU64LE(rep, 21);
            ushort queueSz  = ReadU16LE(rep, 29);
            uint dropped    = ReadU32LE(rep, 31);
            return new NetStatsSnapshot(txPkts, rxPkts, txBytes, rxBytes, queueSz, dropped);
        }

        // ── Bulk memory helpers ───────────────────────────────────────────────

        private byte ReadOneByte(uint address)
        {
            byte[] rep = SendReceive(BuildReadRequest(MsgRead8, address), expectedReplySize: 6);
            if (rep[4] != IPC_OK) throw PineFail($"MsgRead8 0x{address:X8}");
            return rep[5];
        }

        private void ReadBatchRead32(uint baseAddress, int wordCount, byte[] dest, int destOffset)
        {
            // Batch request: [total_size(4)][MsgRead32(1)][addr(4)] × wordCount
            int batchSize = 4 + wordCount * 5;
            var req = new byte[batchSize];
            WriteU32LE(req, 0, (uint)batchSize);
            for (int i = 0; i < wordCount; i++)
            {
                int pos = 4 + i * 5;
                req[pos] = MsgRead32;
                WriteU32LE(req, pos + 1, baseAddress + (uint)(i * 4));
            }

            // Reply: [size(4)][IPC_OK(1)][val(4)] × wordCount
            int expectedReply = 5 + wordCount * 4;
            byte[] rep = SendReceive(req, expectedReply);
            if (rep[4] != IPC_OK) throw PineFail($"Batch MsgRead32 0x{baseAddress:X8}");
            Buffer.BlockCopy(rep, 5, dest, destOffset, wordCount * 4);
        }

        private void WriteBatchWrite8(uint baseAddress, byte[] src, int srcOffset, int count)
        {
            // Batch request: [total_size(4)][MsgWrite8(1)][addr(4)][val(1)] × count
            int batchSize = 4 + count * 6;
            var req = new byte[batchSize];
            WriteU32LE(req, 0, (uint)batchSize);
            for (int i = 0; i < count; i++)
            {
                int pos = 4 + i * 6;
                req[pos] = MsgWrite8;
                WriteU32LE(req, pos + 1, baseAddress + (uint)i);
                req[pos + 5] = src[srcOffset + i];
            }

            byte[] rep = SendReceive(req, expectedReplySize: 5);
            if (rep[4] != IPC_OK) throw PineFail($"Batch MsgWrite8 0x{baseAddress:X8}");
        }

        // ── String query ──────────────────────────────────────────────────────

        private string QueryString(byte opcode)
        {
            byte[] rep = SendReceiveVariable(BuildSingleRequest(opcode));
            if (rep[4] != IPC_OK) return string.Empty;
            if (rep.Length < 9) return string.Empty;
            int strSize = (int)ReadU32LE(rep, 5);
            if (strSize <= 0 || 9 + strSize > rep.Length) return string.Empty;
            // PINE includes the null terminator in strSize; strip it so callers
            // receive a clean managed string without embedded \0 characters.
            return Encoding.UTF8.GetString(rep, 9, strSize).TrimEnd('\0');
        }

        // ── Request builders ──────────────────────────────────────────────────

        /// <summary>Builds a zero-arg command: [5][opcode]</summary>
        private static byte[] BuildSingleRequest(byte opcode)
        {
            var buf = new byte[5];
            WriteU32LE(buf, 0, 5u);
            buf[4] = opcode;
            return buf;
        }

        /// <summary>Builds a read-address command: [9][opcode][addr]</summary>
        private static byte[] BuildReadRequest(byte opcode, uint address)
        {
            var buf = new byte[9];
            WriteU32LE(buf, 0, 9u);
            buf[4] = opcode;
            WriteU32LE(buf, 5, address);
            return buf;
        }

        /// <summary>Builds an address-arg command: [9][opcode][addr]  (breakpoints, etc.)</summary>
        private static byte[] BuildAddressRequest(byte opcode, uint address)
            => BuildReadRequest(opcode, address); // same layout

        /// <summary>Builds a savestate/loadstate command: [6][opcode][slot]</summary>
        private static byte[] BuildStateRequest(byte opcode, byte slot)
        {
            var buf = new byte[6];
            WriteU32LE(buf, 0, 6u);
            buf[4] = opcode;
            buf[5] = slot;
            return buf;
        }

        // ── Transport ─────────────────────────────────────────────────────────

        private byte[] SendReceive(byte[] request, int expectedReplySize)
        {
            lock (_lock)
            {
                EnsureConnected();
                try { return DoSendReceive(request, expectedReplySize); }
                catch (SocketException) { Disconnect(); EnsureConnected(); return DoSendReceive(request, expectedReplySize); }
                catch (IOException)     { Disconnect(); EnsureConnected(); return DoSendReceive(request, expectedReplySize); }
            }
        }

        private byte[] SendReceiveVariable(byte[] request)
        {
            lock (_lock)
            {
                EnsureConnected();
                try { return DoSendReceiveVariable(request); }
                catch (SocketException) { Disconnect(); EnsureConnected(); return DoSendReceiveVariable(request); }
                catch (IOException)     { Disconnect(); EnsureConnected(); return DoSendReceiveVariable(request); }
            }
        }

        private byte[] DoSendReceive(byte[] request, int expectedReplySize)
        {
            var ns = new NetworkStream(_socket!, ownsSocket: false);
            ns.Write(request, 0, request.Length);
            ns.Flush();
            var buf = new byte[expectedReplySize];
            int received = 0;
            while (received < expectedReplySize)
            {
                int n = ns.Read(buf, received, expectedReplySize - received);
                if (n == 0) throw new IOException("Connection closed by PCSX2 PINE server");
                received += n;
            }
            return buf;
        }

        private byte[] DoSendReceiveVariable(byte[] request)
        {
            var ns = new NetworkStream(_socket!, ownsSocket: false);
            ns.Write(request, 0, request.Length);
            ns.Flush();

            var header = new byte[4];
            int got = 0;
            while (got < 4) { int n = ns.Read(header, got, 4 - got); if (n == 0) throw new IOException("PINE connection closed"); got += n; }
            int totalSize = (int)ReadU32LE(header, 0);
            if (totalSize < 4 || totalSize > 1_000_000)
                throw new InvalidDataException($"PINE reply has implausible size {totalSize}");

            var buf = new byte[totalSize];
            Buffer.BlockCopy(header, 0, buf, 0, 4);
            got = 4;
            while (got < totalSize) { int n = ns.Read(buf, got, totalSize - got); if (n == 0) throw new IOException("PINE connection closed"); got += n; }
            return buf;
        }

        private void EnsureConnected()
        {
            if (_socket?.Connected == true) return;
            _socket?.Dispose();
            Connect();
        }

        private void Connect()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _socket.Connect(_host, _slot);
            }
            else
            {
                // Linux / macOS: PINE uses a Unix-domain socket.
                // Path: $XDG_RUNTIME_DIR/pcsx2.sock  (Linux)
                //       $TMPDIR/pcsx2.sock            (macOS)
                //       /tmp/pcsx2.sock               (fallback)
                // Append .<slot> for non-default slots.
                string? runtimeDir = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? Environment.GetEnvironmentVariable("TMPDIR")
                    : Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
                string socketPath = runtimeDir != null
                    ? Path.Combine(runtimeDir, "pcsx2.sock")
                    : "/tmp/pcsx2.sock";
                if (_slot != DefaultSlot)
                    socketPath += $".{_slot}";
                var ep = new UnixDomainSocketEndPoint(socketPath);
                _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                _socket.Connect(ep);
            }
        }

        private void Disconnect()
        {
            try { _socket?.Close(); } catch { /* ignore */ }
            _socket?.Dispose();
            _socket = null;
        }

        // ── Byte-order helpers ────────────────────────────────────────────────

        private static void WriteU32LE(byte[] buf, int offset, uint value)
        {
            buf[offset]     = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >>  8) & 0xFF);
            buf[offset + 2] = (byte)((value >> 16) & 0xFF);
            buf[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static uint ReadU32LE(byte[] buf, int offset)
            => (uint)(buf[offset]
                    | (buf[offset + 1] <<  8)
                    | (buf[offset + 2] << 16)
                    | (buf[offset + 3] << 24));

        private static void WriteFloatLE(byte[] buf, int offset, float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            Buffer.BlockCopy(bytes, 0, buf, offset, 4);
        }

        private static void WriteU16LE(byte[] buf, int offset, ushort value)
        {
            buf[offset]     = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static ushort ReadU16LE(byte[] buf, int offset)
            => (ushort)(buf[offset] | (buf[offset + 1] << 8));

        private static ulong ReadU64LE(byte[] buf, int offset)
            => (ulong)ReadU32LE(buf, offset)
             | ((ulong)ReadU32LE(buf, offset + 4) << 32);

        private static void SplitU128(byte[] buf, out uint lo32, out uint hi32, out uint upLo32, out uint upHi32, out ulong lo64, out ulong hi64)
        {
            lo32    = ReadU32LE(buf, 0);
            hi32    = ReadU32LE(buf, 4);
            upLo32  = ReadU32LE(buf, 8);
            upHi32  = ReadU32LE(buf, 12);
            lo64    = ((ulong)hi32 << 32) | lo32;
            hi64    = ((ulong)upHi32 << 32) | upLo32;
        }

        private static void SplitVector(byte[] buf, out uint x, out uint y, out uint z, out uint w)
        {
            x = ReadU32LE(buf, 0);
            y = ReadU32LE(buf, 4);
            z = ReadU32LE(buf, 8);
            w = ReadU32LE(buf, 12);
        }

        private static InvalidOperationException PineFail(string op)
            => new InvalidOperationException($"PINE IPC_FAIL on {op}");

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose() => Disconnect();
    }
}
