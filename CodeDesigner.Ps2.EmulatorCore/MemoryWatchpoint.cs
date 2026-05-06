namespace CodeDesigner.Ps2.EmulatorCore
{
    /// <summary>Specifies which memory accesses a watchpoint monitors.</summary>
    public enum WatchpointType
    {
        Write,
        Read,
        ReadWrite,
    }

    /// <summary>
    /// Describes a single active memory watchpoint.
    /// Watchpoints pause the emulator when the monitored address range is
    /// accessed in the specified way (currently Write is supported for the
    /// internal emulator; Read/ReadWrite may be added in a future revision).
    /// </summary>
    public sealed class MemoryWatchpoint
    {
        /// <summary>Unique string identifier returned to the client (e.g. "wp-0").</summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>Virtual EE address as supplied by the caller.</summary>
        public uint VirtualAddress { get; init; }

        /// <summary>Inclusive physical start address of the watched range.</summary>
        public uint PhysStart { get; init; }

        /// <summary>Exclusive physical end address of the watched range (PhysStart + Size).</summary>
        public uint PhysEnd { get; init; }

        /// <summary>Size in bytes of the watched range.</summary>
        public int Size { get; init; }

        /// <summary>Which access type triggers this watchpoint.</summary>
        public WatchpointType Type { get; init; }
    }

    /// <summary>
    /// Recorded when a watchpoint fires during an EE instruction.
    /// The PC field captures the program counter of the instruction
    /// that performed the triggering memory access.
    /// </summary>
    public sealed class WatchpointHitInfo
    {
        /// <summary>ID of the watchpoint that fired.</summary>
        public string WatchpointId { get; init; } = string.Empty;

        /// <summary>Physical EE address that was accessed.</summary>
        public uint PhysAddress { get; init; }

        /// <summary>Size (in bytes) of the access that triggered the watchpoint.</summary>
        public int Size { get; init; }

        /// <summary>EE program counter of the instruction that caused the hit.</summary>
        public uint PC { get; init; }

        /// <summary>Type of access that triggered the watchpoint.</summary>
        public WatchpointType Type { get; init; }
    }
}
