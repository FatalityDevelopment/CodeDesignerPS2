using System;

namespace CodeDesigner.Ps2.Common.CPU.COP1
{
    /// <summary>
    /// Floating Point Coprocessor (COP1/FPU) for the Emotion Engine
    /// Manages FPU control registers and floating-point exception handling
    /// </summary>
    public sealed class COP1State
    {
        // FPU Control Registers (FCR)
        // FCR0 = Implementation and Revision (read-only)
        // FCR31 = Control/Status Register (FCSR)
        private readonly uint[] _controlRegisters = new uint[32];

        public COP1State()
        {
            InitializeRegisters();
        }

        private void InitializeRegisters()
        {
            // FCR0 - FPU Implementation and Revision register
            // Format: [31:24] = Reserved, [23:16] = ProcessorID, [15:8] = Revision, [7:0] = Implementation
            // MIPS64/PS2 EE FPU identifier (example value)
            _controlRegisters[0] = 0x00000B00; // Implementation 0x00, Revision 0x0B
            
            // FCR31 - FPU Control/Status Register (FCSR)
            // Initial state: all exceptions disabled, round to nearest
            _controlRegisters[31] = 0x00000000;
        }

        /// <summary>
        /// Read a control register (FCR)
        /// </summary>
        public uint ReadControlRegister(int register)
        {
            if (register < 0 || register >= 32)
                throw new ArgumentOutOfRangeException(nameof(register));

            return _controlRegisters[register];
        }

        /// <summary>
        /// Write a control register (FCR)
        /// </summary>
        public void WriteControlRegister(int register, uint value)
        {
            if (register < 0 || register >= 32)
                throw new ArgumentOutOfRangeException(nameof(register));

            // FCR0 is read-only (implementation register)
            if (register == 0)
                return;

            // FCR31 is the main control/status register
            if (register == 31)
            {
                // Mask off read-only bits and reserved bits
                // Bits [22:18] = Cause bits (read-only, cleared by writing 0)
                // Bits [17:12] = Enable bits (writable)
                // Bits [11:7] = Flag bits (writable)
                // Bits [6:2] = Exception bits (read-only)
                // Bits [1:0] = Rounding mode (writable)
                
                uint readOnlyMask = 0xFF80007C; // Protect read-only bits
                uint oldValue = _controlRegisters[31];
                
                // Preserve read-only bits, update writable bits
                _controlRegisters[31] = (oldValue & readOnlyMask) | (value & ~readOnlyMask);
                
                // Handle cause bits (can be cleared by writing 0)
                uint causeBits = (oldValue >> 18) & 0x1F;
                uint clearCause = (~value >> 18) & 0x1F;
                causeBits &= ~clearCause;
                _controlRegisters[31] = (_controlRegisters[31] & ~(0x1Fu << 18)) | (causeBits << 18);
            }
            else
            {
                // Other FCR registers (most are reserved/unused)
                _controlRegisters[register] = value;
            }
        }

        /// <summary>
        /// Get the current rounding mode from FCSR (bits 1:0)
        /// 0 = Round to nearest (RN)
        /// 1 = Round toward zero (RZ)
        /// 2 = Round toward positive infinity (RP)
        /// 3 = Round toward negative infinity (RM)
        /// </summary>
        public int GetRoundingMode()
        {
            return (int)(_controlRegisters[31] & 0x3);
        }

        /// <summary>
        /// Set the rounding mode in FCSR
        /// </summary>
        public void SetRoundingMode(int mode)
        {
            if (mode < 0 || mode > 3)
                throw new ArgumentOutOfRangeException(nameof(mode));

            _controlRegisters[31] = (_controlRegisters[31] & ~0x3u) | (uint)mode;
        }

        /// <summary>
        /// Check if a specific exception is enabled (bits 17:12)
        /// Bit 17 = Invalid Operation
        /// Bit 16 = Division by Zero
        /// Bit 15 = Overflow
        /// Bit 14 = Underflow
        /// Bit 13 = Inexact
        /// Bit 12 = Unimplemented Operation
        /// </summary>
        public bool IsExceptionEnabled(FpuException exception)
        {
            int bit = 12 + (int)exception;
            return (_controlRegisters[31] & (1u << bit)) != 0;
        }

        /// <summary>
        /// Set exception enable bit
        /// </summary>
        public void SetExceptionEnable(FpuException exception, bool enabled)
        {
            int bit = 12 + (int)exception;
            if (enabled)
                _controlRegisters[31] |= (1u << bit);
            else
                _controlRegisters[31] &= ~(1u << bit);
        }

        /// <summary>
        /// Get exception flags (bits 11:7)
        /// </summary>
        public uint GetExceptionFlags()
        {
            return (_controlRegisters[31] >> 7) & 0x1F;
        }

        /// <summary>
        /// Set exception flag
        /// </summary>
        public void SetExceptionFlag(FpuException exception, bool set)
        {
            int bit = 7 + (int)exception;
            if (set)
                _controlRegisters[31] |= (1u << bit);
            else
                _controlRegisters[31] &= ~(1u << bit);
        }

        /// <summary>
        /// Clear all exception flags
        /// </summary>
        public void ClearExceptionFlags()
        {
            _controlRegisters[31] &= ~(0x1Fu << 7);
        }

        /// <summary>
        /// Record a FPU exceptional condition in FCSR.
        /// Sets the Cause bit (bits [22:18]) and the sticky Flag bit (bits [11:7])
        /// for the given exception.  The Cause bits are cleared at the start of each
        /// FPU instruction by <see cref="ClearCauseBits"/>; Flag bits persist until
        /// explicitly cleared by software (write to FCR31).
        /// Reference: PCSX2 pcsx2/FPU.cpp FPU_OVERFLOW / FPU_DIVZERO helpers.
        /// </summary>
        public void RaiseFpuFlag(FpuException exception)
        {
            SetExceptionCause(exception, true);
            SetExceptionFlag(exception, true);
        }

        /// <summary>
        /// Clear the five Cause bits ([22:18]) in FCSR at the start of each FPU
        /// operation so they reflect only the most recent instruction.
        /// </summary>
        public void ClearCauseBits()
        {
            _controlRegisters[31] &= ~(0x1Fu << 18);
        }

        /// <summary>
        /// Get exception cause bits (bits 22:18) - read-only, set by hardware
        /// </summary>
        public uint GetExceptionCause()
        {
            return (_controlRegisters[31] >> 18) & 0x1F;
        }

        /// <summary>
        /// Set exception cause bit (internal use by FPU operations)
        /// </summary>
        internal void SetExceptionCause(FpuException exception, bool set)
        {
            int bit = 18 + (int)exception;
            if (set)
                _controlRegisters[31] |= (1u << bit);
            else
                _controlRegisters[31] &= ~(1u << bit);
        }

        /// <summary>
        /// Get the condition code (bit 23) - used by branch instructions
        /// </summary>
        public bool GetConditionCode()
        {
            return (_controlRegisters[31] & (1u << 23)) != 0;
        }

        /// <summary>
        /// Set the condition code (bit 23)
        /// </summary>
        public void SetConditionCode(bool value)
        {
            if (value)
                _controlRegisters[31] |= (1u << 23);
            else
                _controlRegisters[31] &= ~(1u << 23);
        }

        /// <summary>
        /// Get the flush-to-zero mode (bit 24) - PS2 specific
        /// </summary>
        public bool GetFlushToZero()
        {
            return (_controlRegisters[31] & (1u << 24)) != 0;
        }

        /// <summary>
        /// Set flush-to-zero mode
        /// </summary>
        public void SetFlushToZero(bool value)
        {
            if (value)
                _controlRegisters[31] |= (1u << 24);
            else
                _controlRegisters[31] &= ~(1u << 24);
        }

        /// <summary>
        /// Get control register name for debugging
        /// </summary>
        public static string GetControlRegisterName(int register)
        {
            return register switch
            {
                0 => "FCR0 (FIR - FPU Implementation)",
                31 => "FCR31 (FCSR - Control/Status)",
                _ => $"FCR{register}"
            };
        }
    }

    /// <summary>
    /// FPU Exception types
    /// </summary>
    public enum FpuException
    {
        /// <summary>
        /// Unimplemented operation (bit offset 0)
        /// </summary>
        Unimplemented = 0,

        /// <summary>
        /// Inexact result (bit offset 1)
        /// </summary>
        Inexact = 1,

        /// <summary>
        /// Underflow (bit offset 2)
        /// </summary>
        Underflow = 2,

        /// <summary>
        /// Overflow (bit offset 3)
        /// </summary>
        Overflow = 3,

        /// <summary>
        /// Division by zero (bit offset 4)
        /// </summary>
        DivisionByZero = 4,

        /// <summary>
        /// Invalid operation (bit offset 5)
        /// </summary>
        InvalidOperation = 5
    }
}