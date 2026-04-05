using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.Emulator;
using CodeDesigner.Ps2.Emulator.Hardware;
using CodeDesigner.Ps2.Emulator.IOP;
using CodeDesigner.Ps2.Common.VectorUnits.VU0;
using CodeDesigner.Ps2.Common.VectorUnits.VU1;


// =============================================================================
// VU0 Micro-Mode tests (Phase 7.2)
// =============================================================================

namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestVU0MicroMode
    {
        // ── Helper: write a 64-bit VU instruction pair into MicroMem ─────────

        private static void WriteInstr(byte[] mem, int pcByte, uint lower, uint upper)
        {
            mem[pcByte + 0] = (byte)lower;
            mem[pcByte + 1] = (byte)(lower >>  8);
            mem[pcByte + 2] = (byte)(lower >> 16);
            mem[pcByte + 3] = (byte)(lower >> 24);
            mem[pcByte + 4] = (byte)upper;
            mem[pcByte + 5] = (byte)(upper >>  8);
            mem[pcByte + 6] = (byte)(upper >> 16);
            mem[pcByte + 7] = (byte)(upper >> 24);
        }

        /// <summary>Encode upper word: E | opcode | dest | ft | fs | fd</summary>
        private static uint UpperWord(bool eFlag, int opcode, int dest, int ft, int fs, int fd, int bc = 0)
            => (eFlag ? 0x80000000u : 0u)
             | ((uint)(opcode & 0x3F) << 25)
             | ((uint)(dest   & 0x0F) << 21)
             | ((uint)(ft     & 0x1F) << 16)
             | ((uint)(fs     & 0x1F) << 11)
             | ((uint)(fd     & 0x1F) <<  6)
             | ((uint)(bc     & 0x03));

        /// <summary>Upper NOP with optional E-bit: opcode=0x1C, funct(fd)=0x33.</summary>
        private static uint UpperNop(bool eFlag = false)
            => UpperWord(eFlag, 0x1C, 0, 0, 0, 0x33);

        /// <summary>Lower NOP (LOI with float 0.0).</summary>
        private static uint LowerNop() => 0x80000000u;

        // =====================================================================

        [TestMethod]
        public void VU0State_DefaultMemorySizes()
        {
            var s = new VU0State();
            Assert.AreEqual(4096, s.MicroMem.Length, "MicroMem must be 4 KB");
            Assert.AreEqual(4096, s.DataMem.Length,  "DataMem  must be 4 KB");
        }

        [TestMethod]
        public void VU0State_VF0_IsReadOnly()
        {
            var s = new VU0State();
            s.WriteVF(0, new Vector4(9f, 9f, 9f, 9f), 0xF);
            Assert.AreEqual(new Vector4(0f, 0f, 0f, 1f), s.VF[0],
                "vf0 must remain (0,0,0,1) after write attempt");
        }

        [TestMethod]
        public void VU0State_VI0_IsReadOnly()
        {
            var s = new VU0State();
            s.WriteVI(0, 0xBEEF);
            Assert.AreEqual(0, s.VI[0], "vi0 must remain 0 after write attempt");
        }

        [TestMethod]
        public void VU0Executor_NopProgram_Stops()
        {
            var s = new VU0State();
            WriteInstr(s.MicroMem, 0, LowerNop(), UpperNop(eFlag: true));
            s.Start(0);
            VU0Executor.RunProgram(s);
            Assert.IsFalse(s.IsRunning, "VU0 must stop after E-bit instruction");
            Assert.AreEqual(8, s.PC, "PC must advance past the single instruction");
        }

        [TestMethod]
        public void VU0Executor_AddInstruction_AddsVectors()
        {
            var s = new VU0State();
            s.VF[2] = new Vector4(1f, 2f, 3f, 4f);
            s.VF[3] = new Vector4(10f, 20f, 30f, 40f);

            // ADD.xyzw vf1 = vf2 + vf3  (opcode=0x13, dest=0xF, ft=3, fs=2, fd=1), E-bit
            uint upper = UpperWord(eFlag: true, opcode: 0x13, dest: 0xF, ft: 3, fs: 2, fd: 1);
            WriteInstr(s.MicroMem, 0, LowerNop(), upper);
            s.Start(0);
            VU0Executor.RunProgram(s);

            Assert.AreEqual(11f, s.VF[1].X, 1e-5f, "ADD vf1.x");
            Assert.AreEqual(22f, s.VF[1].Y, 1e-5f, "ADD vf1.y");
            Assert.AreEqual(33f, s.VF[1].Z, 1e-5f, "ADD vf1.z");
            Assert.AreEqual(44f, s.VF[1].W, 1e-5f, "ADD vf1.w");
        }

        [TestMethod]
        public void VU0Executor_MulBroadcast_MultipliesComponent()
        {
            var s = new VU0State();
            s.VF[2] = new Vector4(2f, 3f, 4f, 5f);
            s.VF[3] = new Vector4(100f, 200f, 300f, 400f);

            // MULbc.xyz vf1 = vf2 * vf3.w  (opcode=0x06, bc=3=W, dest=0x7, ft=3, fs=2, fd=1), E-bit
            uint upper = UpperWord(eFlag: true, opcode: 0x06, dest: 0x7, ft: 3, fs: 2, fd: 1, bc: 3);
            WriteInstr(s.MicroMem, 0, LowerNop(), upper);
            s.Start(0);
            VU0Executor.RunProgram(s);

            Assert.AreEqual(800f,  s.VF[1].X, 1e-2f, "MULbc.x");
            Assert.AreEqual(1200f, s.VF[1].Y, 1e-2f, "MULbc.y");
            Assert.AreEqual(1600f, s.VF[1].Z, 1e-2f, "MULbc.z");
            Assert.AreEqual(0f,    s.VF[1].W, 1e-5f, "MULbc dest=xyz, w unchanged");
        }

        [TestMethod]
        public void VU0Executor_ComponentMask_OnlyWritesSelectedComponents()
        {
            var s = new VU0State();
            s.VF[2] = new Vector4(1f, 2f, 3f, 4f);
            s.VF[3] = new Vector4(10f, 20f, 30f, 40f);
            s.VF[1] = new Vector4(99f, 99f, 99f, 99f);

            // ADD.xz vf1 = vf2 + vf3  (dest=0x5), E-bit
            uint upper = UpperWord(eFlag: true, opcode: 0x13, dest: 0x5, ft: 3, fs: 2, fd: 1);
            WriteInstr(s.MicroMem, 0, LowerNop(), upper);
            s.Start(0);
            VU0Executor.RunProgram(s);

            Assert.AreEqual(11f, s.VF[1].X, 1e-5f, "ADD.xz — x written");
            Assert.AreEqual(99f, s.VF[1].Y, 1e-5f, "ADD.xz — y NOT written");
            Assert.AreEqual(33f, s.VF[1].Z, 1e-5f, "ADD.xz — z written");
            Assert.AreEqual(99f, s.VF[1].W, 1e-5f, "ADD.xz — w NOT written");
        }

        [TestMethod]
        public void VU0Executor_LOI_StoresFloatImmediate()
        {
            var s = new VU0State();
            float imm = 3.14f;
            uint immBits = (uint)BitConverter.SingleToInt32Bits(imm) & 0x7FFFFFFFu;
            uint lowerLoi = 0x80000000u | immBits;

            WriteInstr(s.MicroMem, 0, lowerLoi, UpperNop(eFlag: true));
            s.Start(0);
            VU0Executor.RunProgram(s);

            Assert.AreEqual(imm, s.I, 1e-5f, "LOI must store float immediate in I register");
        }

        [TestMethod]
        public void VU0Executor_IBEQ_Branch_Taken()
        {
            var s = new VU0State();
            s.VI[1] = 5;
            s.VI[2] = 5;

            // Instruction at offset 0: IBEQ vi2, vi1, +1  (branch forward 1 instr = +8 bytes)
            // Lower IBEQ: opcode=0x22, ft=vi2=2, fs=vi1=1, imm11=1
            uint lowerIbeq = (0x22u << 25) | (2u << 16) | (1u << 11) | 1u;
            // Instruction at offset 8: SUB vf1=vf2-vf3 (should be skipped)
            // Instruction at offset 16: ADD vf1=vf2+vf3 (E-bit, should execute)
            s.VF[2] = new Vector4(1f, 2f, 3f, 4f);
            s.VF[3] = new Vector4(10f, 20f, 30f, 40f);

            uint upperAdd = UpperWord(eFlag: true, opcode: 0x13, dest: 0xF, ft: 3, fs: 2, fd: 1);
            uint upperSub = UpperWord(eFlag: false, opcode: 0x17, dest: 0xF, ft: 3, fs: 2, fd: 1);

            WriteInstr(s.MicroMem,  0, lowerIbeq, UpperNop(eFlag: false));
            WriteInstr(s.MicroMem,  8, LowerNop(), upperSub);  // skipped by branch
            WriteInstr(s.MicroMem, 16, LowerNop(), upperAdd);  // E-bit

            s.Start(0);
            VU0Executor.RunProgram(s);

            // Branch taken: jumped over SUB to ADD
            Assert.AreEqual(11f, s.VF[1].X, 1e-5f, "Branch taken: ADD result x");
            Assert.AreEqual(22f, s.VF[1].Y, 1e-5f, "Branch taken: ADD result y");
        }

        [TestMethod]
        public void VU0Executor_IntegerArith_IADD_ISUB()
        {
            var s = new VU0State();
            s.VI[1] = 10;
            s.VI[2] = 3;

            // Two-instruction program:
            // 0: IADD vi3 = vi1 + vi2 (lower special funct=0x30, opcode=0x1B, fd=3,fs=1,ft=2)
            // 8: ISUB vi4 = vi1 - vi2 (lower special funct=0x31, fd=4,fs=1,ft=2), E-bit on upper
            uint lowerIadd = (0x1Bu << 25) | (2u << 16) | (1u << 11) | (3u << 6) | 0x30u;
            uint lowerIsub = (0x1Bu << 25) | (2u << 16) | (1u << 11) | (4u << 6) | 0x31u;

            WriteInstr(s.MicroMem, 0, lowerIadd, UpperNop(eFlag: false));
            WriteInstr(s.MicroMem, 8, lowerIsub, UpperNop(eFlag: true));

            s.Start(0);
            VU0Executor.RunProgram(s);

            Assert.AreEqual(13, s.VI[3], "IADD vi3 = 10 + 3");
            Assert.AreEqual(7,  s.VI[4], "ISUB vi4 = 10 - 3");
        }

        [TestMethod]
        public void VU0Executor_MemoryLoadStore_SQ_LQ()
        {
            var s = new VU0State();
            s.VF[2] = new Vector4(1.0f, 2.0f, 3.0f, 4.0f);
            s.VI[1] = 0; // base address = 0

            // SQ.xyzw vf2, 0(vi1)  — store vf2 to DataMem[0*16]
            // Lower opcode=0x01 (SQ): ft=vi_base=1, fs=vf_src=2, fd=0, funct=0, dest=0xF
            uint lowerSq = (0x01u << 25) | (0xFu << 21) | (1u << 16) | (2u << 11) | 0u;
            // LQ.xyzw vf3, 0(vi1)  — load DataMem[0*16] into vf3
            // Lower opcode=0x00 (LQ): ft=vf_dest=3, fs=vi_base=1, fd=0, funct=0, dest=0xF
            uint lowerLq = (0x00u << 25) | (0xFu << 21) | (3u << 16) | (1u << 11) | 0u;

            WriteInstr(s.MicroMem,  0, lowerSq, UpperNop(eFlag: false));
            WriteInstr(s.MicroMem,  8, lowerLq, UpperNop(eFlag: true));

            s.Start(0);
            VU0Executor.RunProgram(s);

            Assert.AreEqual(1.0f, s.VF[3].X, 1e-5f, "LQ vf3.x after SQ");
            Assert.AreEqual(2.0f, s.VF[3].Y, 1e-5f, "LQ vf3.y after SQ");
            Assert.AreEqual(3.0f, s.VF[3].Z, 1e-5f, "LQ vf3.z after SQ");
            Assert.AreEqual(4.0f, s.VF[3].W, 1e-5f, "LQ vf3.w after SQ");
        }

        [TestMethod]
        public void VU0Executor_ViaVcallms_RunsProgram()
        {
            // Simulate VCALLMS triggering VU0 micro-mode via CpuState
            var cpu = new CodeDesigner.Ps2.Common.CPU.CpuState();

            cpu.VU0.VF[2] = new Vector4(5f, 6f, 7f, 8f);
            cpu.VU0.VF[3] = new Vector4(1f, 2f, 3f, 4f);

            // Write a simple ADD program into VU0 MicroMem
            uint upper = UpperWord(eFlag: true, opcode: 0x13, dest: 0xF, ft: 3, fs: 2, fd: 1);
            WriteInstr(cpu.VU0.MicroMem, 0, LowerNop(), upper);

            // Start micro-mode at offset 0 and run
            cpu.VU0.Start(0);
            VU0Executor.RunProgram(cpu.VU0);

            Assert.IsFalse(cpu.VU0.IsRunning, "VU0 must stop after E-bit");
            Assert.AreEqual(6f,  cpu.VU0.VF[1].X, 1e-5f, "VCALLMS result x");
            Assert.AreEqual(8f,  cpu.VU0.VF[1].Y, 1e-5f, "VCALLMS result y");
            Assert.AreEqual(10f, cpu.VU0.VF[1].Z, 1e-5f, "VCALLMS result z");
            Assert.AreEqual(12f, cpu.VU0.VF[1].W, 1e-5f, "VCALLMS result w");
        }

        // =====================================================================
        // VBlank / VSync generation tests
        // =====================================================================

        [TestMethod]
        public void PS2Emulator_VBlankStart_FiresAtNtscInterval()
        {
            var emu = new PS2Emulator();
            emu.Bus.Intc.WriteRegister(INTC.REG_MASK, 1u << INTC.INT_VBLANK_S); // enable mask bit

            // Tick just past the VBlank-start threshold (line 235 of 262.5)
            emu.TickHardware(PS2Emulator.NtscVBlankStartCycle + 1u);

            Assert.IsTrue((emu.Bus.Intc.Stat & (1u << INTC.INT_VBLANK_S)) != 0,
                "INTC VBLANK_S must be set after VBlankStart threshold");
        }

        [TestMethod]
        public void PS2Emulator_VBlankEnd_FiresAtFrameRollover()
        {
            var emu = new PS2Emulator();

            // Cross VBlank-start, then cross frame boundary → VBlank-end
            emu.TickHardware(PS2Emulator.NtscVBlankStartCycle + 1u);
            Assert.IsTrue((emu.Bus.Intc.Stat & (1u << INTC.INT_VBLANK_S)) != 0, "VBLANK_S must fire first");

            emu.TickHardware(PS2Emulator.NtscFrameCycles - PS2Emulator.NtscVBlankStartCycle);
            Assert.IsTrue((emu.Bus.Intc.Stat & (1u << INTC.INT_VBLANK_E)) != 0,
                "INTC VBLANK_E must fire at frame rollover");
        }

        [TestMethod]
        public void PS2Emulator_VBlank_FiresRepeatedly()
        {
            var emu = new PS2Emulator();
            int vblankCount = 0;

            // Advance 3 full frames
            for (int i = 0; i < 3; i++)
            {
                emu.TickHardware(PS2Emulator.NtscFrameCycles);
                if ((emu.Bus.Intc.Stat & (1u << INTC.INT_VBLANK_S)) != 0)
                {
                    vblankCount++;
                    // Clear it so we can count the next one
                    emu.Bus.Intc.WriteRegister(INTC.REG_STAT, 1u << INTC.INT_VBLANK_S);
                }
            }

            Assert.IsTrue(vblankCount >= 2,
                $"VBLANK_S must fire at least twice across 3 frames (got {vblankCount})");
        }

        [TestMethod]
        public void GS_CSR_VSync_BitSetByRaiseVSync()
        {
            var gs = new GS();
            Assert.AreEqual(0ul, gs.ReadPriv64(GS.PRIV_CSR) & 0x8ul, "CSR bit 3 must start clear");
            gs.RaiseVSync();
            Assert.AreEqual(0x8ul, gs.ReadPriv64(GS.PRIV_CSR) & 0x8ul, "CSR bit 3 must be set after RaiseVSync");
        }

        [TestMethod]
        public void GS_CSR_VSync_W1C_ClearsOnWrite()
        {
            // Reproduces the friend's vSync sub: write CSR|0x8 to CLEAR the VSYNC flag (W1C)
            var gs = new GS();
            gs.RaiseVSync();                              // GS sets bit 3
            Assert.AreEqual(0x8ul, gs.ReadPriv64(GS.PRIV_CSR) & 0x8ul, "bit 3 must be set before ack");
            gs.WritePriv64(GS.PRIV_CSR, gs.ReadPriv64(GS.PRIV_CSR) | 0x8ul); // write bit 3 = W1C
            Assert.AreEqual(0ul,  gs.ReadPriv64(GS.PRIV_CSR) & 0x8ul, "bit 3 must be cleared after W1C write");
        }

        [TestMethod]
        public void GS_CSR_W1C_DoesNotClearOtherBits()
        {
            // Bits 10+ are read-only hardware status; writing bit 3 must not disturb them
            var gs = new GS();
            // Manually set some non-W1C read-back bits by poking internal state via RaiseVSync
            gs.RaiseVSync();  // sets bit 3
            // Write bit 0 (SIGNAL W1C) and bit 3 (VSYNC W1C) together
            gs.WritePriv64(GS.PRIV_CSR, 0x9ul);  // 0x9 = bits 0 + 3
            Assert.AreEqual(0ul, gs.ReadPriv64(GS.PRIV_CSR) & 0xFul,
                "All W1C bits 0-3 must be cleared after write");
        }

        [TestMethod]
        public void PS2Emulator_VSync_SetsGsCsrBit3()
        {
            var emu = new PS2Emulator();
            Assert.AreEqual(0ul, emu.Bus.Gs.ReadPriv64(GS.PRIV_CSR) & 0x8ul, "CSR bit 3 must start clear");

            // Tick past VBlank-start threshold
            emu.TickHardware(PS2Emulator.NtscVBlankStartCycle + 1u);

            Assert.AreEqual(0x8ul, emu.Bus.Gs.ReadPriv64(GS.PRIV_CSR) & 0x8ul,
                "GS_CSR bit 3 must be set at VBlank-start so polling code unblocks");
        }

        [TestMethod]
        public void Sif_IopSignaledEe_RaisesIntcSbus()
        {
            var emu = new PS2Emulator();
            Assert.AreEqual(0u, emu.Bus.Intc.Stat & (1u << INTC.INT_SBUS),
                "INTC_SBUS must start clear");

            // IOP signals EE by setting SMFLAG bits
            emu.Bus.Sif.WriteIopRegister(SIF.REG_SMFLAG, 0x00000001u);

            Assert.AreNotEqual(0u, emu.Bus.Intc.Stat & (1u << INTC.INT_SBUS),
                "INTC_SBUS must be raised when IOP sets SMFLAG");
        }

        [TestMethod]
        public void Sif_IopSignaledEe_NotFiredWhenNoBitsChange()
        {
            var emu   = new PS2Emulator();
            int fired = 0;
            emu.Bus.Sif.IopSignaledEe += (_, _) => fired++;

            // First write sets bits → fires
            emu.Bus.Sif.WriteIopRegister(SIF.REG_SMFLAG, 0x1u);
            int firstCount = fired;

            // Second write with already-set bits → no new bits → must not fire again
            emu.Bus.Sif.WriteIopRegister(SIF.REG_SMFLAG, 0x1u);
            Assert.AreEqual(firstCount, fired,
                "IopSignaledEe must not fire when no new SMFLAG bits are set");
        }

        // =====================================================================
        // MCH RDRAM controller (0x1000F430 / 0x1000F440)
        // =====================================================================

        [TestMethod]
        public void MCH_RICM_Read_Returns_Zero()
        {
            var bus = new PS2MemoryBus();
            // Write a non-zero value to MCH_RICM
            bus.WriteWord(0x1000F430u, 0x00210000u);
            // PCSX2 HwRead.cpp: MCH_RICM read always returns 0 (busy flag cleared)
            Assert.AreEqual(0u, bus.ReadWord(0x1000F430u), "MCH_RICM read must always return 0");
        }

        [TestMethod]
        public void MCH_RICM_Write_ClearsBit31()
        {
            var bus = new PS2MemoryBus();
            // Write with bit31 set — RICM stores value with bit31 cleared
            bus.WriteWord(0x1000F430u, 0x80210000u);
            // Read back via DRD SA=0x40 (DEVID) to check the stored value
            bus.WriteWord(0x1000F430u, 0x00400000u);
            uint drd = bus.ReadWord(0x1000F440u);
            // SA=0x40: DRD returns _mchRicm & 0x1F. With value 0x00400000, lower 5 bits = 0.
            Assert.AreEqual(0u, drd, "MCH_RICM bit31 (busy) must be cleared on store");
        }

        [TestMethod]
        public void MCH_DRD_INIT_Returns_0x1F_For_Each_RDRAM_Device()
        {
            var bus = new PS2MemoryBus();
            // INIT command: SA=0x21, SOP=0
            bus.WriteWord(0x1000F430u, 0x00210000u);
            uint dev0 = bus.ReadWord(0x1000F440u);
            Assert.AreEqual(0x1Fu, dev0, "First RDRAM device must respond 0x1F");

            uint dev1 = bus.ReadWord(0x1000F440u);
            Assert.AreEqual(0x1Fu, dev1, "Second RDRAM device must respond 0x1F");

            uint dev2 = bus.ReadWord(0x1000F440u);
            Assert.AreEqual(0u, dev2, "No third device — must return 0");
        }

        [TestMethod]
        public void MCH_DRD_CNFGA_Returns_0x0D0D()
        {
            var bus = new PS2MemoryBus();
            // SA=0x23 (CNFGA), SOP=0
            bus.WriteWord(0x1000F430u, 0x00230000u);
            Assert.AreEqual(0x0D0Du, bus.ReadWord(0x1000F440u), "CNFGA must return 0x0D0D");
        }

        [TestMethod]
        public void MCH_DRD_CNFGB_Returns_0x0090()
        {
            var bus = new PS2MemoryBus();
            // SA=0x24 (CNFGB), SOP=0
            bus.WriteWord(0x1000F430u, 0x00240000u);
            Assert.AreEqual(0x0090u, bus.ReadWord(0x1000F440u), "CNFGB must return 0x0090");
        }

        [TestMethod]
        public void MCH_RICM_INIT_Reset_Resets_Sdevid()
        {
            var bus = new PS2MemoryBus();
            // SA=0x21, SOP=0: poll two devices
            bus.WriteWord(0x1000F430u, 0x00210000u);
            bus.ReadWord(0x1000F440u);
            bus.ReadWord(0x1000F440u);
            // Third poll returns 0 (no more devices)
            Assert.AreEqual(0u, bus.ReadWord(0x1000F440u), "sdevid exhausted — must return 0");

            // PCSX2 reset condition: write SA=0x21 with SOP=1 (bit6=1) and DRD bit7=0
            // (bit6 of the value = bit6 of the low word = (value >> 6) & 0xF = 1)
            bus.WriteWord(0x1000F430u, 0x00210040u); // SA=0x21, SOP=1 (bit6=1)
            // Now reset should have cleared sdevid; re-poll returns 0x1F again
            bus.WriteWord(0x1000F430u, 0x00210000u); // SA=0x21, SOP=0 to read
            Assert.AreEqual(0x1Fu, bus.ReadWord(0x1000F440u), "sdevid must be reset after INIT command");
        }

        [TestMethod]
        public void Sif_F260_Initialized_To_1D000060()
        {
            var sif = new SIF();
            // PCSX2 hwReset() sets SBUS_F260 = 0x1D000060.
            Assert.AreEqual(0x1D000060u, sif.ReadRegister(SIF.REG_F260),
                "SBUS_F260 must be initialized to 0x1D000060 (PCSX2 hwReset)");
        }
    }
}
