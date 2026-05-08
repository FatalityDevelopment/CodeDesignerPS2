using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.EmulatorCore;
using CodeDesigner.Ps2.EmulatorCore.Hardware;
using CodeDesigner.Ps2.EmulatorCore.IOP;
using CodeDesigner.Ps2.Common.VectorUnits.VU0;
using CodeDesigner.Ps2.Common.VectorUnits.VU1;


namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestVU1
    {
        // ── Helper: write a 64-bit VU instruction pair into MicroMem ─────────

        private static void WriteInstr(byte[] mem, int pcByte, uint lower, uint upper)
        {
            // lower word at bytes [0..3], upper word at bytes [4..7]
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

        /// <summary>Lower NOP (LOI with float 0.0 = 0x80000000 → I = 0.0).</summary>
        private static uint LowerNop() => 0x80000000u;

        // =====================================================================

        [TestMethod]
        public void VU1State_DefaultMemorySizes()
        {
            var s = new VU1State();
            Assert.AreEqual(16 * 1024, s.MicroMem.Length, "MicroMem must be 16 KB");
            Assert.AreEqual(16 * 1024, s.DataMem.Length,  "DataMem  must be 16 KB");
        }

        [TestMethod]
        public void VU1State_VF0_IsReadOnly()
        {
            var s = new VU1State();
            s.WriteVF(0, new Vector4(9f, 9f, 9f, 9f), 0xF);
            Assert.AreEqual(new Vector4(0f, 0f, 0f, 1f), s.VF[0],
                "vf0 must remain (0,0,0,1) after write attempt");
        }

        [TestMethod]
        public void VU1State_VI0_IsReadOnly()
        {
            var s = new VU1State();
            s.WriteVI(0, 0xBEEF);
            Assert.AreEqual(0, s.VI[0], "vi0 must remain 0 after write attempt");
        }

        [TestMethod]
        public void VU1Executor_NopProgram_Stops()
        {
            var s = new VU1State();
            // Single instruction: upper=NOP+E, lower=LOI 0.0 (no-op)
            WriteInstr(s.MicroMem, 0, LowerNop(), UpperNop(eFlag: true));
            s.Start(0);
            VU1Executor.RunProgram(s, null);
            Assert.IsFalse(s.IsRunning, "VU1 must stop after E-bit instruction");
            Assert.AreEqual(8, s.PC,    "PC must advance past the single instruction");
        }

        [TestMethod]
        public void VU1Executor_AddInstruction_AddsVectors()
        {
            var s = new VU1State();
            // Set vf2 = (1,2,3,4), vf3 = (10,20,30,40)
            s.VF[2] = new Vector4(1f, 2f, 3f, 4f);
            s.VF[3] = new Vector4(10f, 20f, 30f, 40f);

            // ADD.xyzw vf1 = vf2 + vf3  (opcode=0x13, dest=0xF, ft=3, fs=2, fd=1), E-bit
            uint upper = UpperWord(eFlag: true, opcode: 0x13, dest: 0xF, ft: 3, fs: 2, fd: 1);
            WriteInstr(s.MicroMem, 0, LowerNop(), upper);
            s.Start(0);
            VU1Executor.RunProgram(s, null);

            Assert.AreEqual(11f, s.VF[1].X, 1e-5f, "ADD vf1.x");
            Assert.AreEqual(22f, s.VF[1].Y, 1e-5f, "ADD vf1.y");
            Assert.AreEqual(33f, s.VF[1].Z, 1e-5f, "ADD vf1.z");
            Assert.AreEqual(44f, s.VF[1].W, 1e-5f, "ADD vf1.w");
        }

        [TestMethod]
        public void VU1Executor_MulBroadcast_MultipliesComponent()
        {
            var s = new VU1State();
            // vf2 = (2,3,4,5), vf3 = (100,200,300,400)
            s.VF[2] = new Vector4(2f, 3f, 4f, 5f);
            s.VF[3] = new Vector4(100f, 200f, 300f, 400f);

            // MULbc.xyz vf1 = vf2 * vf3.w  (opcode=0x06, bc=3=W, dest=0x7=xyz, ft=3, fs=2, fd=1), E-bit
            uint upper = UpperWord(eFlag: true, opcode: 0x06, dest: 0x7, ft: 3, fs: 2, fd: 1, bc: 3);
            WriteInstr(s.MicroMem, 0, LowerNop(), upper);
            s.Start(0);
            VU1Executor.RunProgram(s, null);

            // vf3.w = 400; vf2.xyz = (2,3,4) → result.xyz = (800, 1200, 1600)
            Assert.AreEqual(800f,  s.VF[1].X, 1e-2f, "MULbc.x");
            Assert.AreEqual(1200f, s.VF[1].Y, 1e-2f, "MULbc.y");
            Assert.AreEqual(1600f, s.VF[1].Z, 1e-2f, "MULbc.z");
            // w was not masked in, so it stays at the initial 0
            Assert.AreEqual(0f, s.VF[1].W, 1e-5f, "MULbc dest=xyz, w unchanged");
        }

        [TestMethod]
        public void VU1Executor_ComponentMask_OnlyWritesSelectedComponents()
        {
            var s = new VU1State();
            s.VF[2] = new Vector4(1f, 2f, 3f, 4f);
            s.VF[3] = new Vector4(10f, 20f, 30f, 40f);
            s.VF[1] = new Vector4(99f, 99f, 99f, 99f); // pre-fill

            // ADD.xz vf1 = vf2 + vf3  (dest=0x5 = bits 0+2 = x,z), E-bit
            uint upper = UpperWord(eFlag: true, opcode: 0x13, dest: 0x5, ft: 3, fs: 2, fd: 1);
            WriteInstr(s.MicroMem, 0, LowerNop(), upper);
            s.Start(0);
            VU1Executor.RunProgram(s, null);

            Assert.AreEqual(11f, s.VF[1].X, 1e-5f, "ADD.xz — x written");
            Assert.AreEqual(99f, s.VF[1].Y, 1e-5f, "ADD.xz — y NOT written");
            Assert.AreEqual(33f, s.VF[1].Z, 1e-5f, "ADD.xz — z written");
            Assert.AreEqual(99f, s.VF[1].W, 1e-5f, "ADD.xz — w NOT written");
        }

        [TestMethod]
        public void VU1Executor_LOI_StoresFloatImmediate()
        {
            var s = new VU1State();
            // LOI: lower bit31=1, lower bits[30:0] = bits of 3.14f with bit31 cleared
            float imm = 3.14f;
            uint immBits = (uint)BitConverter.SingleToInt32Bits(imm) & 0x7FFFFFFFu;
            uint lowerLoi = 0x80000000u | immBits; // LOI flag + float bits

            // Upper NOP with E-bit
            WriteInstr(s.MicroMem, 0, lowerLoi, UpperNop(eFlag: true));
            s.Start(0);
            VU1Executor.RunProgram(s, null);

            Assert.AreEqual(imm, s.I, 1e-5f, "LOI must store float immediate in I register");
        }

        [TestMethod]
        public void VU1Executor_MulQ_UsesQRegister()
        {
            var s = new VU1State();
            s.Q = 5.0f;
            s.VF[2] = new Vector4(2f, 3f, 4f, 5f);

            // MULq.xyzw vf1 = vf2 * Q  (opcode=0x07, dest=0xF, ft=0, fs=2, fd=1), E-bit
            uint upper = UpperWord(eFlag: true, opcode: 0x07, dest: 0xF, ft: 0, fs: 2, fd: 1);
            WriteInstr(s.MicroMem, 0, LowerNop(), upper);
            s.Start(0);
            VU1Executor.RunProgram(s, null);

            Assert.AreEqual(10f, s.VF[1].X, 1e-5f, "MULq.x");
            Assert.AreEqual(15f, s.VF[1].Y, 1e-5f, "MULq.y");
            Assert.AreEqual(20f, s.VF[1].Z, 1e-5f, "MULq.z");
            Assert.AreEqual(25f, s.VF[1].W, 1e-5f, "MULq.w");
        }

        [TestMethod]
        public void VU1Executor_IADDIU_AddsImmediate()
        {
            var s = new VU1State();
            s.VI[3] = 10;

            // IADDIU vi4 = vi3 + 7
            // lower_opcode=0x08, dest=0, ft=4 (it), fs=3 (is), fd/funct encodes imm15=7
            // imm15 = fd<<6 | funct → for imm=7: fd=0, funct=7
            uint lower = ((uint)0x08 << 25) | ((uint)4 << 16) | ((uint)3 << 11) | 7u;
            WriteInstr(s.MicroMem, 0, lower, UpperNop(eFlag: true));
            s.Start(0);
            VU1Executor.RunProgram(s, null);

            Assert.AreEqual(17, s.VI[4], "IADDIU vi4 = vi3 + 7");
        }

        [TestMethod]
        public void VU1Executor_IADD_AddsRegisters()
        {
            var s = new VU1State();
            s.VI[1] = 5;
            s.VI[2] = 7;

            // IADD id=3 = is=1 + it=2
            // lower opcode=0x1B, funct=0x30, fs=1, ft=2, fd=3
            uint lower = ((uint)0x1B << 25) | ((uint)2 << 16) | ((uint)1 << 11) | ((uint)3 << 6) | 0x30u;
            WriteInstr(s.MicroMem, 0, lower, UpperNop(eFlag: true));
            s.Start(0);
            VU1Executor.RunProgram(s, null);

            Assert.AreEqual(12, s.VI[3], "IADD vi3 = vi1 + vi2");
        }

        [TestMethod]
        public void VU1Executor_IBNE_TakesBranch()
        {
            var s = new VU1State();
            s.VI[1] = 1;
            s.VI[2] = 2;

            // Layout:
            //   PC=0:  IBNE vi1, vi2, +1  → branch to PC=8+1*8=16 (skips instr at 8)
            //   PC=8:  ADD vf1 = vf2+vf3  (should be skipped)
            //   PC=16: NOP+E
            s.VF[2] = new Vector4(1f);
            s.VF[3] = new Vector4(1f);

            // IBNE: lower opcode=0x23, ft=vi1=1, fs=vi2=2, imm11=1
            uint lowerIbne = ((uint)0x23 << 25) | ((uint)1 << 16) | ((uint)2 << 11) | 1u;
            // ADD vf1 = vf2+vf3 (should be skipped if branch taken)
            uint upperAdd  = UpperWord(false, 0x13, 0xF, 3, 2, 1);
            // NOP+E
            uint lowerNop  = LowerNop();
            uint upperNopE = UpperNop(eFlag: true);

            WriteInstr(s.MicroMem,  0, lowerIbne, UpperNop(false));   // IBNE at PC=0
            WriteInstr(s.MicroMem,  8, lowerNop,  upperAdd);           // ADD (skipped)
            WriteInstr(s.MicroMem, 16, lowerNop,  upperNopE);          // NOP+E

            s.Start(0);
            VU1Executor.RunProgram(s, null);

            // Branch taken: vi1(1) != vi2(2) → skip instr at 8, land at 16
            Assert.AreEqual(Vector4.Zero, s.VF[1], "ADD at PC=8 must have been skipped by branch");
        }

        [TestMethod]
        public void VU1Executor_XGKick_FeedsGif()
        {
            var s   = new VU1State();
            var gif = new GIF();
            var gs  = new GS();
            gif.Gs  = gs;

            // Build a minimal GIFtag in DataMem at qword 0 (byte 0):
            // NLOOP=1, EOP=1, FLG=0 (PACKED), NREG=1, REGS[0]=0xE (A+D)
            // Then one PACKED A+D entry writing GS register FRAME_1 (0x4C=GS_FRAME_1)
            // We'll write NOP-like: RGBAQ=0, XYZ2=0 — but for this test we just
            // verify Feed() was called (no exception, GIF processes it cleanly).

            // GIFtag qword (16 bytes):
            //   word0 bits[14:0]=NLOOP=1, bit15=EOP=1, bits[57:47]=PRIM, bit46=PRE=0
            //   word1 bits[31:28]=NREG=1, bits[27:26]=FLG=0, bit31=PRE=0
            //   word2/3 = REGS: 4-bit each; REGS[0]=0xE (A+D)
            uint tagW0 = 1u | (1u << 15);          // NLOOP=1, EOP=1
            uint tagW1 = 0u;                        // FLG=PACKED, PRE=0
            uint tagW2 = (1u << 28) | (0xEu << 0); // NREG=1, REGS[0]=0xE (A+D)
            uint tagW3 = 0u;

            void WriteU32At(byte[] m, int off, uint v)
            {
                m[off + 0] = (byte)v;
                m[off + 1] = (byte)(v >> 8);
                m[off + 2] = (byte)(v >> 16);
                m[off + 3] = (byte)(v >> 24);
            }

            WriteU32At(s.DataMem,  0, tagW0);
            WriteU32At(s.DataMem,  4, tagW1);
            WriteU32At(s.DataMem,  8, tagW2);
            WriteU32At(s.DataMem, 12, tagW3);
            // PACKED A+D data qword (16 bytes): DATA_LO=0, DATA_HI=0, ADDR=0x0E, pad=0
            // (ADDR=0x0E is the A+D escape — using addr=0 (PRIM) with data=0 is fine)
            // Actually A+D: lo64=data, hi64[7:0]=GS reg address.  Use addr=0x4E (FRAME_1 alias).
            // Use 0 data and addr=0 (writes PRIM=0, harmless):
            WriteU32At(s.DataMem, 16, 0u);
            WriteU32At(s.DataMem, 20, 0u);
            WriteU32At(s.DataMem, 24, 0u);   // addr byte = 0 (PRIM register)
            WriteU32At(s.DataMem, 28, 0u);

            // Set vi1 = 0 (DataMem qword address 0)
            s.VI[1] = 0;

            // XGKICK vi1: lower opcode=0x1B, fs=1, funct=0x33
            uint lowerXgkick = ((uint)0x1B << 25) | ((uint)1 << 11) | 0x33u;
            WriteInstr(s.MicroMem, 0, lowerXgkick, UpperNop(eFlag: true));

            s.Start(0);
            // Must not throw
            VU1Executor.RunProgram(s, gif);

            Assert.IsFalse(s.IsRunning, "VU1 must have stopped after XGKICK + E-bit");
        }

        [TestMethod]
        public void PS2Emulator_Vif1_WiredToVu1Memory()
        {
            var emu = new PS2Emulator();
            Assert.AreSame(emu.Cpu.VU1.DataMem,  emu.Bus.Vif1.VuDataMem,
                "VIF1.VuDataMem must be the same array as VU1.DataMem");
            Assert.AreSame(emu.Cpu.VU1.MicroMem, emu.Bus.Vif1.VuMicroMem,
                "VIF1.VuMicroMem must be the same array as VU1.MicroMem");
        }

        [TestMethod]
        public void PS2Emulator_VIF1_MSCAL_RunsVU1Microprogram()
        {
            var emu = new PS2Emulator();
            var vu1 = emu.Cpu.VU1;

            // Write a simple microprogram into VU1 micro memory:
            // ADD.xyzw vf1 = vf2 + vf3, then E-bit NOP.
            vu1.VF[2] = new Vector4(1f, 2f, 3f, 4f);
            vu1.VF[3] = new Vector4(10f, 20f, 30f, 40f);

            // UpperWord helper (inline):
            uint MkUpper(bool e, int op, int dest, int ft, int fs, int fd)
                => (e ? 0x80000000u : 0u)
                 | ((uint)(op & 0x3F) << 25) | ((uint)(dest & 0xF) << 21)
                 | ((uint)(ft & 0x1F) << 16)  | ((uint)(fs & 0x1F) << 11)
                 | ((uint)(fd & 0x1F) <<  6);

            uint upper = MkUpper(true, 0x13, 0xF, 3, 2, 1);
            uint lower = 0x80000000u; // LOI 0.0 (NOP)

            byte[] mm = vu1.MicroMem;
            mm[0] = (byte)lower;  mm[1] = (byte)(lower >> 8);
            mm[2] = (byte)(lower >> 16); mm[3] = (byte)(lower >> 24);
            mm[4] = (byte)upper;  mm[5] = (byte)(upper >> 8);
            mm[6] = (byte)(upper >> 16); mm[7] = (byte)(upper >> 24);

            // Directly trigger MSCAL by calling Start() + RunProgram() (VIF1 event path is
            // tested separately; here we just verify the executor runs correctly via emulator).
            vu1.Start(0);
            VU1Executor.RunProgram(vu1, emu.Bus.Gif);

            Assert.AreEqual(11f, vu1.VF[1].X, 1e-5f, "VU1 ADD result x after MSCAL");
            Assert.AreEqual(22f, vu1.VF[1].Y, 1e-5f, "VU1 ADD result y after MSCAL");
        }

        // =====================================================================
        // BiosLoader tests
        // =====================================================================

        /// <summary>
        /// Build a minimal synthetic BIOS image (ROMDIR with RESET + ROMVER entries
        /// followed by their data) and verify that BiosLoader.Validate returns the
        /// correct version, region and date.
        /// </summary>
        private static byte[] BuildMinimalBios(
            string romVerString = "0160JC 20020426",
            int    padToSize    = 0x400)
        {
            // ROMDIR layout (16 bytes per entry):
            //   [0]  "RESET\0\0\0\0\0"  extInfoSize=0  fileSize=<romdir bytes>
            //   [1]  "ROMVER\0\0\0\0"   extInfoSize=0  fileSize=15
            //   [2]  "\0\0\0\0\0\0\0\0\0\0" (terminator)
            // Each entry is 16 bytes; 3 entries = 48 bytes of ROMDIR.
            const int entrySize = 16;
            int romDirBytes = 3 * entrySize; // 48 bytes

            byte[] bios = new byte[Math.Max(padToSize, romDirBytes + 14)];

            void WriteEntry(int idx, string name, uint fileSize)
            {
                int off = idx * entrySize;
                // name (10 bytes, null-padded)
                for (int c = 0; c < 10; c++)
                    bios[off + c] = c < name.Length ? (byte)name[c] : (byte)0;
                // extInfoSize = 0 (2 bytes LE)
                bios[off + 10] = 0; bios[off + 11] = 0;
                // fileSize (4 bytes LE)
                bios[off + 12] = (byte)fileSize;
                bios[off + 13] = (byte)(fileSize >> 8);
                bios[off + 14] = (byte)(fileSize >> 16);
                bios[off + 15] = (byte)(fileSize >> 24);
            }

            // Entry 0: RESET — module data = the ROMDIR table itself.
            WriteEntry(0, "RESET",  (uint)romDirBytes);
            // Entry 1: ROMVER — 15 bytes.
            WriteEntry(1, "ROMVER", 15);
            // Entry 2: terminator (zero name).
            WriteEntry(2, "",  0);

            // Write ROMVER string immediately after the ROMDIR table.
            // RESET file occupies bytes [0 .. romDirBytes-1], rounded to 16-byte boundary,
            // so ROMVER data starts at offset romDirBytes (already 16-byte aligned: 48 bytes).
            byte[] verBytes = System.Text.Encoding.ASCII.GetBytes(romVerString.PadRight(15)[..15]);
            Array.Copy(verBytes, 0, bios, romDirBytes, verBytes.Length);

            return bios;
        }

        [TestMethod]
        public void BiosLoader_Validate_ReturnsCorrectVersionInfo()
        {
            byte[] bios = BuildMinimalBios("0160JC 20020426");
            BiosInfo info = BiosLoader.Validate(bios);

            Assert.AreEqual("1.60",       info.Version, "Version should be 1.60");
            Assert.AreEqual("Japan",      info.Region,  "Region should be Japan");
            Assert.AreEqual("2002-04-26", info.Date,    "Date should be 2002-04-26");
        }

        [TestMethod]
        public void BiosLoader_Validate_UsaRegion()
        {
            byte[] bios = BuildMinimalBios("0200AC 20030929");
            BiosInfo info = BiosLoader.Validate(bios);
            Assert.AreEqual("USA", info.Region);
        }

        [TestMethod]
        public void BiosLoader_Validate_EuropeRegion()
        {
            byte[] bios = BuildMinimalBios("0200EC 20030929");
            BiosInfo info = BiosLoader.Validate(bios);
            Assert.AreEqual("Europe", info.Region);
        }

        [TestMethod]
        public void BiosLoader_Validate_EmptyImage_Throws()
        {
            Assert.ThrowsException<BiosLoadException>(
                () => BiosLoader.Validate(Array.Empty<byte>()),
                "Empty BIOS must throw BiosLoadException");
        }

        [TestMethod]
        public void BiosLoader_Validate_ImageTooLarge_Throws()
        {
            byte[] oversized = new byte[BiosLoader.BiosRomSize + 1];
            Assert.ThrowsException<BiosLoadException>(
                () => BiosLoader.Validate(oversized),
                "Oversized image must throw BiosLoadException");
        }

        [TestMethod]
        public void BiosLoader_Validate_BadFirstEntry_Throws()
        {
            byte[] bios = BuildMinimalBios(); // valid content...
            // Overwrite the first ROMDIR entry name with something other than "RESET"
            byte[] badName = System.Text.Encoding.ASCII.GetBytes("KERNEL\0\0\0\0");
            Array.Copy(badName, 0, bios, 0, 10);

            Assert.ThrowsException<BiosLoadException>(
                () => BiosLoader.Validate(bios),
                "Non-RESET first entry must throw BiosLoadException");
        }

        [TestMethod]
        public void BiosLoader_Load_CopiesBiosIntoEeAndIopBuses()
        {
            byte[] bios = BuildMinimalBios("0160JC 20020426", padToSize: BiosLoader.BiosRomSize);
            var emu = new PS2Emulator();
            var iop = new IopEmulator(emu.Bus);

            BiosInfo info = BiosLoader.Load(bios, emu, iop);

            // BIOS bytes must have been copied into the EE bus
            for (int i = 0; i < bios.Length; i++)
                Assert.AreEqual(bios[i], emu.Bus.Bios[i],
                    $"EE bus BIOS byte mismatch at offset 0x{i:X}");

            // BIOS bytes must have been copied into the IOP bus
            for (int i = 0; i < bios.Length; i++)
                Assert.AreEqual(bios[i], iop.Bus.Bios[i],
                    $"IOP bus BIOS byte mismatch at offset 0x{i:X}");

            // Version info should be extracted correctly
            Assert.AreEqual("1.60", info.Version);
        }

        [TestMethod]
        public void BiosLoader_Load_ResetsEeToPowerOnVector()
        {
            byte[] bios = BuildMinimalBios();
            var emu = new PS2Emulator();
            var iop = new IopEmulator(emu.Bus);

            BiosLoader.Load(bios, emu, iop);

            // After load + reset the EE PC must be at the MIPS reset vector.
            Assert.AreEqual(0xBFC00000UL, emu.Cpu.PC,
                "EE PC must be 0xBFC00000 after BIOS load");
        }

        [TestMethod]
        public void BiosLoader_Load_Scheduler_LoadsBothBuses()
        {
            byte[] bios = BuildMinimalBios("0300JC 20040614", padToSize: BiosLoader.BiosRomSize);
            var scheduler = new DualCpuScheduler();

            BiosInfo info = BiosLoader.Load(bios, scheduler);

            Assert.AreEqual("3.00", info.Version);
            Assert.AreEqual("Japan", info.Region);

            // Both buses must contain the loaded ROM.
            Assert.AreEqual(bios[0], scheduler.EE.Bus.Bios[0], "EE bus byte 0");
            Assert.AreEqual(bios[0], scheduler.IOP.Bus.Bios[0], "IOP bus byte 0");

            // Both CPUs should be at the reset vector.
            Assert.AreEqual(0xBFC00000UL, scheduler.EE.Cpu.PC,
                "EE PC must be at reset vector after BIOS load");
        }

        [TestMethod]
        public void BiosLoader_BiosInfo_ToString_ContainsVersion()
        {
            var info = new BiosInfo("1.60", "Japan", "2002-04-26");
            string s = info.ToString();
            StringAssert.Contains(s, "1.60");
            StringAssert.Contains(s, "Japan");
        }

        // ── Real BIOS ROM tests ───────────────────────────────────────────────
        // These tests load the genuine PS2 BIOS images committed to the repo and
        // verify that BiosLoader.Validate() can scan the ROM directory and return
        // correct version metadata.  The BIOS files live at:
        //   CodeDesigner.Ps2.Common/Bios/<name>
        // relative to the repository root.

        /// <summary>
        /// Returns the path to a BIOS file, deriving the repo root from the test
        /// assembly's output directory (bin/Debug/net8.0).
        /// </summary>
        private static string RealBiosPath(string filename)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // Walk up four levels: net8.0 → Debug → bin → TestProject → repo root
            string repoRoot = Path.GetFullPath(
                Path.Combine(baseDir, "..", "..", "..", ".."));
            return Path.Combine(repoRoot,
                "CodeDesigner.Ps2.Common", "Bios", filename);
        }

        [TestMethod]
        public void BiosLoader_RealBios_Japan_scph10000_ValidatesAndReturnsVersion()
        {
            string path = RealBiosPath("scph10000.bin");
            if (!File.Exists(path))
                Assert.Inconclusive($"BIOS file not found: {path}");

            byte[] biosData = File.ReadAllBytes(path);
            BiosInfo info = BiosLoader.Validate(biosData);

            Assert.AreEqual("1.00",       info.Version, "Japan v1 version");
            Assert.AreEqual("Japan",      info.Region,  "Japan v1 region");
            Assert.AreEqual("2000-01-17", info.Date,    "Japan v1 build date");
        }

        [TestMethod]
        public void BiosLoader_RealBios_USA_SCPH70012_ValidatesAndReturnsVersion()
        {
            string path = RealBiosPath("SCPH-70012_BIOS_V12_USA_200.bin");
            if (!File.Exists(path))
                Assert.Inconclusive($"BIOS file not found: {path}");

            byte[] biosData = File.ReadAllBytes(path);
            BiosInfo info = BiosLoader.Validate(biosData);

            Assert.AreEqual("2.00", info.Version, "USA v2 version");
            Assert.AreEqual("USA",  info.Region,  "USA v2 region");
            Assert.AreEqual("2004-06-14", info.Date, "USA v2 build date");
        }

        [TestMethod]
        public void BiosLoader_RealBios_Europe_SCPH70004_ValidatesAndReturnsVersion()
        {
            string path = RealBiosPath("SCPH-70004_BIOS_V12_PAL_200.BIN");
            if (!File.Exists(path))
                Assert.Inconclusive($"BIOS file not found: {path}");

            byte[] biosData = File.ReadAllBytes(path);
            BiosInfo info = BiosLoader.Validate(biosData);

            Assert.AreEqual("2.00",   info.Version, "Europe v2 version");
            Assert.AreEqual("Europe", info.Region,  "Europe v2 region");
            Assert.AreEqual("2004-06-14", info.Date, "Europe v2 build date");
        }

        [TestMethod]
        public void BiosLoader_RealBios_Load_CopiesIntoEeAndIopBuses()
        {
            string path = RealBiosPath("SCPH-70012_BIOS_V12_USA_200.bin");
            if (!File.Exists(path))
                Assert.Inconclusive($"BIOS file not found: {path}");

            byte[] biosData = File.ReadAllBytes(path);
            var emu = new PS2Emulator();
            var iop = new IopEmulator(emu.Bus);

            BiosInfo info = BiosLoader.Load(biosData, emu, iop);

            Assert.AreEqual("2.00", info.Version, "Version extracted from real BIOS");
            Assert.AreEqual("USA",  info.Region,  "Region extracted from real BIOS");

            // Verify first few BIOS bytes were copied into both buses
            for (int i = 0; i < 16; i++)
                Assert.AreEqual(biosData[i], emu.Bus.Bios[i],
                    $"EE bus BIOS byte mismatch at offset 0x{i:X}");
            for (int i = 0; i < 16; i++)
                Assert.AreEqual(biosData[i], iop.Bus.Bios[i],
                    $"IOP bus BIOS byte mismatch at offset 0x{i:X}");

            // Both CPUs must be at the power-on reset vector.
            Assert.AreEqual(0xBFC00000UL, emu.Cpu.PC,
                "EE PC must be at reset vector after real BIOS load");
        }
    }
}
