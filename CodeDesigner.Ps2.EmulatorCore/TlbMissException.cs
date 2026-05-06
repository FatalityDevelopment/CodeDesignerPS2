using System;

namespace CodeDesigner.Ps2.EmulatorCore
{
    /// <summary>
    /// Thrown by <see cref="PS2MemoryBus"/> when a user-space (KUSEG) address has no
    /// matching TLB entry.  The <see cref="PS2Emulator"/> catches this and dispatches
    /// the appropriate COP0 TLB-miss exception (ExcCode 2 for load/fetch, 3 for store).
    /// </summary>
    internal sealed class TlbMissException : Exception
    {
        /// <summary>Virtual address that caused the miss.</summary>
        public uint VirtualAddress { get; }

        /// <summary>True if the miss was triggered by a store operation.</summary>
        public bool IsStore { get; }

        public TlbMissException(uint virtualAddress, bool isStore)
            : base($"TLB miss at 0x{virtualAddress:X8} ({(isStore ? "store" : "load/fetch")})")
        {
            VirtualAddress = virtualAddress;
            IsStore = isStore;
        }
    }
}
