using System;

namespace CodeDesigner.Mips64LE.Common.CPU.COP0
{
    /// <summary>
    /// System Control Coprocessor (COP0) for the Emotion Engine
    /// Manages system control registers, exceptions, TLB, and cache operations
    /// </summary>
    public sealed class COP0State
    {
        // COP0 Registers (32 registers)
        private readonly ulong[] _registers = new ulong[32];
        private readonly uint[] _controlRegisters = new uint[32];

        public TLBState TLB { get; }
        public CacheState Cache { get; }

        public COP0State()
        {
            TLB = new TLBState();
            Cache = new CacheState();
            
            // Initialize default values
            InitializeRegisters();
        }

        private void InitializeRegisters()
        {
            // PRId (Register 15) - Processor ID
            // EE Core = 0x2E20 (example value)
            WriteRegister32(15, 0x00002E20);
            
            // Status (Register 12) - Initial state
            WriteRegister32(12, 0x00400000); // BEV bit set
            
            // Random register (Register 1) - Initialize to max TLB entries
            WriteRegister32(1, (uint)(TLB.NumEntries - 1));
        }

        /// <summary>
        /// Read a 32-bit value from a COP0 register
        /// </summary>
        public uint ReadRegister32(int register)
        {
            if (register < 0 || register >= 32)
                throw new ArgumentOutOfRangeException(nameof(register));

            return (uint)(_registers[register] & 0xFFFFFFFF);
        }

        /// <summary>
        /// Write a 32-bit value to a COP0 register
        /// </summary>
        public void WriteRegister32(int register, uint value)
        {
            if (register < 0 || register >= 32)
                throw new ArgumentOutOfRangeException(nameof(register));

            // Register 0 is read-only on some implementations
            if (register == 0)
                return; // Index register can be written

            // Handle special register behavior
            switch (register)
            {
                case 1: // Random - read-only, auto-incremented
                    // Don't allow direct writes to Random register
                    break;
                    
                case 9: // Count - timer register
                    _registers[register] = value;
                    break;
                    
                case 11: // Compare - clears timer interrupt when written
                    _registers[register] = value;
                    // Clear timer interrupt bit in Cause register
                    uint cause = ReadRegister32(13);
                    cause &= ~(1u << 15); // Clear IP7 (timer interrupt)
                    _registers[13] = cause;
                    break;
                    
                default:
                    _registers[register] = value;
                    break;
            }
        }

        /// <summary>
        /// Read a 64-bit value from a COP0 register
        /// </summary>
        public ulong ReadRegister64(int register)
        {
            if (register < 0 || register >= 32)
                throw new ArgumentOutOfRangeException(nameof(register));

            return _registers[register];
        }

        /// <summary>
        /// Write a 64-bit value to a COP0 register
        /// </summary>
        public void WriteRegister64(int register, ulong value)
        {
            if (register < 0 || register >= 32)
                throw new ArgumentOutOfRangeException(nameof(register));

            // Handle special registers
            switch (register)
            {
                case 1: // Random - read-only
                    break;
                    
                default:
                    _registers[register] = value;
                    break;
            }
        }

        /// <summary>
        /// Read a control register (implementation-specific)
        /// </summary>
        public uint ReadControlRegister(int register)
        {
            if (register < 0 || register >= 32)
                throw new ArgumentOutOfRangeException(nameof(register));

            return _controlRegisters[register];
        }

        /// <summary>
        /// Write a control register (implementation-specific)
        /// </summary>
        public void WriteControlRegister(int register, uint value)
        {
            if (register < 0 || register >= 32)
                throw new ArgumentOutOfRangeException(nameof(register));

            _controlRegisters[register] = value;
        }

        /// <summary>
        /// Update the Random register (called on each instruction)
        /// </summary>
        public void UpdateRandom()
        {
            uint wired = ReadRegister32(6); // Wired register
            uint random = ReadRegister32(1);

            // Decrement Random, wrapping at Wired boundary
            if (random <= wired)
                random = (uint)(TLB.NumEntries - 1);
            else
                random--;

            _registers[1] = random;
        }

        /// <summary>
        /// Get register name for debugging
        /// </summary>
        public static string GetRegisterName(int register)
        {
            return register switch
            {
                0 => "Index",
                1 => "Random",
                2 => "EntryLo0",
                3 => "EntryLo1",
                4 => "Context",
                5 => "PageMask",
                6 => "Wired",
                8 => "BadVAddr",
                9 => "Count",
                10 => "EntryHi",
                11 => "Compare",
                12 => "Status",
                13 => "Cause",
                14 => "EPC",
                15 => "PRId",
                16 => "Config",
                18 => "WatchLo",
                19 => "WatchHi",
                22 => "Debug",
                23 => "DEPC",
                24 => "Perf",
                25 => "ECC",
                26 => "CacheErr",
                27 => "TagLo",
                28 => "TagHi",
                29 => "ErrorEPC",
                _ => $"COP0_{register}"
            };
        }
    }
}