using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using CodeDesigner.Ps2.Emulator;
using CodeDesigner.Ps2.Emulator.Hardware;
using CodeDesigner.Ps2.Emulator.IOP;
using CodeDesigner.Ps2.Common.Cpu.Instructions.EE;


namespace CodeDesigner.Ps2.TestProject
{
    [TestClass]
    public class TestSio2
    {
        // =====================================================================
        // PadState unit tests
        // =====================================================================

        [TestMethod]
        public void PadState_InitialState_AllButtonsReleasedAndSticksCentred()
        {
            var pad = new PadState();
            Assert.AreEqual(0xFFFF, pad.Buttons, "All buttons must be released (0xFFFF) initially");
            Assert.AreEqual(0x80, pad.RightX, "RightX must be 0x80 (centre) initially");
            Assert.AreEqual(0x80, pad.RightY, "RightY must be 0x80 (centre) initially");
            Assert.AreEqual(0x80, pad.LeftX,  "LeftX must be 0x80 (centre) initially");
            Assert.AreEqual(0x80, pad.LeftY,  "LeftY must be 0x80 (centre) initially");
        }

        [TestMethod]
        public void PadState_Press_ClearsButtonBit()
        {
            var pad = new PadState();
            pad.Press(PadState.BTN_CROSS);
            Assert.AreEqual(0, pad.Buttons & PadState.BTN_CROSS, "BTN_CROSS bit must be 0 after Press");
        }

        [TestMethod]
        public void PadState_Release_SetsButtonBit()
        {
            var pad = new PadState();
            pad.Press(PadState.BTN_CROSS);
            pad.Release(PadState.BTN_CROSS);
            Assert.AreNotEqual(0, pad.Buttons & PadState.BTN_CROSS, "BTN_CROSS bit must be 1 after Release");
        }

        [TestMethod]
        public void PadState_Press_MultipleMask_ClearsAllBits()
        {
            var pad = new PadState();
            ushort mask = (ushort)(PadState.BTN_START | PadState.BTN_CROSS | PadState.BTN_CIRCLE);
            pad.Press(mask);
            Assert.AreEqual(0, pad.Buttons & mask, "All masked bits must be 0 after Press");
        }

        [TestMethod]
        public void PadState_ProcessCommand_UnknownCommand_ReturnsAllFF()
        {
            var pad = new PadState();
            byte[] resp = pad.ProcessCommand(new byte[] { 0x01, 0x44 }, 4);
            Assert.AreEqual(4, resp.Length, "Response must have requested length");
            foreach (byte b in resp)
                Assert.AreEqual(0xFF, b, "Unknown command must return 0xFF for every byte");
        }

        [TestMethod]
        public void PadState_ProcessCommand_Poll_HasCorrectDeviceIdAndAck()
        {
            var pad = new PadState();
            byte[] resp = pad.ProcessCommand(new byte[] { PadState.AddrPad, PadState.CmdPoll }, 9);
            Assert.AreEqual(9, resp.Length);
            Assert.AreEqual(PadState.DeviceId, resp[1], "Response[1] must be DualShock2 device ID 0x73");
            Assert.AreEqual(PadState.AckByte,  resp[2], "Response[2] must be acknowledge byte 0x5A");
        }

        [TestMethod]
        public void PadState_ProcessCommand_Poll_AllButtonsReleasedByDefault()
        {
            var pad = new PadState();
            byte[] resp = pad.ProcessCommand(new byte[] { PadState.AddrPad, PadState.CmdPoll, 0, 0, 0 }, 9);
            // All buttons released → both button bytes must be 0xFF (active-low)
            Assert.AreEqual(0xFF, resp[3], "Button byte 0 must be 0xFF (all released)");
            Assert.AreEqual(0xFF, resp[4], "Button byte 1 must be 0xFF (all released)");
        }

        [TestMethod]
        public void PadState_ProcessCommand_Poll_ReflectsPressedButton()
        {
            var pad = new PadState();
            pad.Press(PadState.BTN_CROSS); // bit 14 → byte 1, bit 6
            byte[] resp = pad.ProcessCommand(new byte[] { PadState.AddrPad, PadState.CmdPoll, 0, 0, 0 }, 9);
            // BTN_CROSS is bit 14 → high byte (resp[4]), bit 6
            Assert.AreEqual(0, resp[4] & (1 << 6), "Cross button bit must be 0 (pressed) in response byte 1");
        }

        [TestMethod]
        public void PadState_ProcessCommand_Poll_ReflectsAnalogStickValues()
        {
            var pad = new PadState();
            pad.RightX = 0x10;
            pad.RightY = 0x20;
            pad.LeftX  = 0x30;
            pad.LeftY  = 0x40;
            byte[] resp = pad.ProcessCommand(new byte[] { PadState.AddrPad, PadState.CmdPoll, 0, 0, 0 }, 9);
            Assert.AreEqual(0x10, resp[5], "RightX must appear at resp[5]");
            Assert.AreEqual(0x20, resp[6], "RightY must appear at resp[6]");
            Assert.AreEqual(0x30, resp[7], "LeftX must appear at resp[7]");
            Assert.AreEqual(0x40, resp[8], "LeftY must appear at resp[8]");
        }

        [TestMethod]
        public void PadState_ProcessCommand_Poll_TruncatesResponseToRequestedLength()
        {
            var pad = new PadState();
            byte[] resp = pad.ProcessCommand(new byte[] { PadState.AddrPad, PadState.CmdPoll }, 4);
            Assert.AreEqual(4, resp.Length, "Response must be truncated to the requested length");
            Assert.AreEqual(PadState.DeviceId, resp[1]);
        }

        // =====================================================================
        // Sio2 unit tests
        // =====================================================================

        [TestMethod]
        public void Sio2_Send3_ReadWrite_RoundTrip()
        {
            var sio2 = new Sio2();
            // Write to SEND3[0] at offset 0x00, SEND3[3] at offset 0x0C
            sio2.WriteRegister(0x00u, 0xDEADBEEFu);
            sio2.WriteRegister(0x0Cu, 0x12345678u);
            Assert.AreEqual(0xDEADBEEFu, sio2.ReadRegister(0x00u), "SEND3[0] round-trip");
            Assert.AreEqual(0x12345678u, sio2.ReadRegister(0x0Cu), "SEND3[3] round-trip");
        }

        [TestMethod]
        public void Sio2_Send1Send2_ReadWrite_RoundTrip()
        {
            var sio2 = new Sio2();
            sio2.WriteRegister(0x40u, 0x11111111u);
            sio2.WriteRegister(0x44u, 0x22222222u);
            sio2.WriteRegister(0x48u, 0x33333333u);
            sio2.WriteRegister(0x4Cu, 0x44444444u);
            Assert.AreEqual(0x11111111u, sio2.ReadRegister(0x40u), "SEND1 port0");
            Assert.AreEqual(0x22222222u, sio2.ReadRegister(0x44u), "SEND1 port1");
            Assert.AreEqual(0x33333333u, sio2.ReadRegister(0x48u), "SEND2 port0");
            Assert.AreEqual(0x44444444u, sio2.ReadRegister(0x4Cu), "SEND2 port1");
        }

        [TestMethod]
        public void Sio2_FifoOut_ReturnsFF_WhenEmpty()
        {
            var sio2 = new Sio2();
            uint val = sio2.ReadRegister(0x64u); // FIFO_OUT offset
            Assert.AreEqual(0xFFu, val, "FIFO_OUT must return 0xFF when empty");
        }

        [TestMethod]
        public void Sio2_Transfer_NoPad_FifoOut_AllFF()
        {
            var sio2 = new Sio2();
            // SEND3[0]: port=0, sendLen=3, recvLen=3  (bits[16:8]=3, bits[26:18]=3)
            sio2.WriteRegister(0x00u, BuildSend3(port: 0, sendLen: 3, recvLen: 3));
            sio2.WriteRegister(0x60u, 0x01u); // FIFO_IN
            sio2.WriteRegister(0x60u, 0x42u);
            sio2.WriteRegister(0x60u, 0x00u);
            sio2.WriteRegister(0x68u, 0x01u); // CTRL START

            uint b0 = sio2.ReadRegister(0x64u);
            uint b1 = sio2.ReadRegister(0x64u);
            uint b2 = sio2.ReadRegister(0x64u);
            Assert.AreEqual(0xFFu, b0, "No-device response byte 0 must be 0xFF");
            Assert.AreEqual(0xFFu, b1, "No-device response byte 1 must be 0xFF");
            Assert.AreEqual(0xFFu, b2, "No-device response byte 2 must be 0xFF");
        }

        [TestMethod]
        public void Sio2_Transfer_WithPad_FifoOut_HasPollResponse()
        {
            var sio2 = new Sio2();
            var pad  = new PadState();
            sio2.Port0 = pad;

            // SEND3[0]: port=0, sendLen=2 (addr+cmd), recvLen=9
            sio2.WriteRegister(0x00u, BuildSend3(port: 0, sendLen: 2, recvLen: 9));
            sio2.WriteRegister(0x60u, PadState.AddrPad);
            sio2.WriteRegister(0x60u, PadState.CmdPoll);
            sio2.WriteRegister(0x68u, 0x01u); // CTRL START

            // Skip leading 0xFF
            sio2.ReadRegister(0x64u);
            uint devId = sio2.ReadRegister(0x64u);
            uint ack   = sio2.ReadRegister(0x64u);
            Assert.AreEqual((uint)PadState.DeviceId, devId, "Device ID must be 0x73");
            Assert.AreEqual((uint)PadState.AckByte,  ack,   "Ack byte must be 0x5A");
        }

        [TestMethod]
        public void Sio2_Transfer_TransferComplete_EventFires()
        {
            var sio2 = new Sio2();
            sio2.WriteRegister(0x00u, BuildSend3(port: 0, sendLen: 1, recvLen: 1));
            sio2.WriteRegister(0x60u, 0x00u);

            bool fired = false;
            sio2.TransferComplete += (_, _) => fired = true;

            sio2.WriteRegister(0x68u, 0x01u); // CTRL START
            Assert.IsTrue(fired, "TransferComplete must fire when transfer is executed");
        }

        [TestMethod]
        public void Sio2_Transfer_Recv1_IsSuccessValueAfterTransfer()
        {
            var sio2 = new Sio2();
            sio2.WriteRegister(0x00u, BuildSend3(port: 0, sendLen: 1, recvLen: 1));
            sio2.WriteRegister(0x60u, 0x00u);
            sio2.WriteRegister(0x68u, 0x01u); // CTRL START

            Assert.AreEqual(Sio2.RECV1_SUCCESS, sio2.ReadRegister(0x70u),
                "RECV1 must equal RECV1_SUCCESS after a completed transfer");
        }

        [TestMethod]
        public void Sio2_Ctrl_Reset_ClearsFifos_And_ResetsSend3Head()
        {
            var sio2 = new Sio2();
            var pad  = new PadState();
            sio2.Port0 = pad;

            // First transfer to populate FIFO_OUT
            sio2.WriteRegister(0x00u, BuildSend3(port: 0, sendLen: 2, recvLen: 9));
            sio2.WriteRegister(0x60u, PadState.AddrPad);
            sio2.WriteRegister(0x60u, PadState.CmdPoll);
            sio2.WriteRegister(0x68u, 0x01u); // START

            // Write CTRL RESET — should clear FIFO_OUT so it reads back 0xFF
            sio2.WriteRegister(0x68u, 0x04u); // CTRL RESET (bit 2)
            uint val = sio2.ReadRegister(0x64u); // FIFO_OUT
            Assert.AreEqual(0xFFu, val, "FIFO_OUT must return 0xFF after CTRL RESET");
        }

        [TestMethod]
        public void Sio2_MultipleSend3Descriptors_ProcessedInSequence()
        {
            var sio2 = new Sio2();
            var pad0 = new PadState();
            var pad1 = new PadState();
            pad0.Press(PadState.BTN_START);   // Start pressed on port 0
            pad1.Press(PadState.BTN_CROSS);   // Cross pressed on port 1
            sio2.Port0 = pad0;
            sio2.Port1 = pad1;

            // Two descriptors: port 0 and port 1, each poll 2 cmd → 9 recv
            sio2.WriteRegister(0x00u, BuildSend3(port: 0, sendLen: 2, recvLen: 9));
            sio2.WriteRegister(0x04u, BuildSend3(port: 1, sendLen: 2, recvLen: 9));

            // FIFO_IN: cmd for port 0 then port 1
            sio2.WriteRegister(0x60u, PadState.AddrPad); sio2.WriteRegister(0x60u, PadState.CmdPoll);
            sio2.WriteRegister(0x60u, PadState.AddrPad); sio2.WriteRegister(0x60u, PadState.CmdPoll);

            sio2.WriteRegister(0x68u, 0x01u); // START

            // Port 0 response: 9 bytes — skip 0xFF, devId, ack; read button bytes
            byte[] out0 = new byte[9];
            for (int i = 0; i < 9; i++)
                out0[i] = (byte)sio2.ReadRegister(0x64u);

            // Port 1 response: 9 bytes
            byte[] out1 = new byte[9];
            for (int i = 0; i < 9; i++)
                out1[i] = (byte)sio2.ReadRegister(0x64u);

            // BTN_START is bit 3 in button byte 0 → out0[3] low nibble, bit 3 = 0 when pressed
            Assert.AreEqual(0, out0[3] & (PadState.BTN_START & 0xFF),
                "Port 0 button byte 0: Start bit must be 0 (pressed)");
            // BTN_CROSS is bit 14 in the word → bit 6 of button byte 1 → out1[4] bit 6 = 0 when pressed
            Assert.AreEqual(0, out1[4] & (int)((PadState.BTN_CROSS >> 8) & 0xFF),
                "Port 1 button byte 1: Cross bit must be 0 (pressed)");
        }

        // =====================================================================
        // MemoryCard unit tests
        // =====================================================================

        [TestMethod]
        public void MemoryCard_CreateBlank_HasCorrectSize()
        {
            var mc = MemoryCard.CreateBlank();
            byte[] data = mc.GetData();
            Assert.AreEqual(MemoryCard.TotalFileSize, data.Length,
                "Blank memory card must be exactly TotalFileSize bytes");
        }

        [TestMethod]
        public void MemoryCard_CreateBlank_SuperblockContainsMagic()
        {
            var mc = MemoryCard.CreateBlank();
            byte[] data = mc.GetData();
            string magic = System.Text.Encoding.ASCII.GetString(data, 0, MemoryCard.SuperblockMagic.Length);
            Assert.AreEqual(MemoryCard.SuperblockMagic, magic,
                "Page 0 must begin with the PS2 memory-card superblock magic string");
        }

        [TestMethod]
        public void MemoryCard_ConstructFromData_RejectsWrongSize()
        {
            Assert.ThrowsException<ArgumentException>(
                () => new MemoryCard(new byte[1024]),
                "Constructor must throw for wrong-sized data");
        }

        [TestMethod]
        public void MemoryCard_ConstructFromData_ClonesInput()
        {
            byte[] src = new byte[MemoryCard.TotalFileSize];
            src[0] = 0xAA;
            var mc = new MemoryCard(src);
            src[0] = 0xBB;   // mutate source after construction
            byte[] copy = mc.GetData();
            Assert.AreEqual(0xAA, copy[0], "MemoryCard must clone the input array");
        }

        [TestMethod]
        public void MemoryCard_ReadPage_RoundTrips_WriteRead()
        {
            var mc = MemoryCard.CreateBlank();
            byte[] page = new byte[MemoryCard.PageDataSize];
            for (int i = 0; i < page.Length; i++) page[i] = (byte)(i & 0xFF);

            mc.WritePage(1, page);

            byte[] readBack = new byte[MemoryCard.PageDataSize];
            mc.ReadPage(1, readBack);

            for (int i = 0; i < page.Length; i++)
                Assert.AreEqual(page[i], readBack[i], $"Page byte [{i}] must round-trip");
        }

        [TestMethod]
        public void MemoryCard_ErasePage_FillsWithFF()
        {
            var mc = MemoryCard.CreateBlank();
            byte[] page = new byte[MemoryCard.PageDataSize];
            for (int i = 0; i < page.Length; i++) page[i] = 0x12;
            mc.WritePage(5, page);

            mc.ErasePage(5);

            byte[] readBack = new byte[MemoryCard.PageDataSize];
            mc.ReadPage(5, readBack);
            for (int i = 0; i < readBack.Length; i++)
                Assert.AreEqual(0xFF, readBack[i], $"Erased page byte [{i}] must be 0xFF");
        }

        [TestMethod]
        public void MemoryCard_ProcessCommand_ResetCommand_ReturnsAck()
        {
            var mc = MemoryCard.CreateBlank();
            byte[] cmd = { MemoryCard.AddrSlot0, MemoryCard.CmdReset, 0x00 };
            byte[] resp = mc.ProcessCommand(cmd, 4);

            Assert.AreEqual(0xFF, resp[0], "RESET response[0] must be 0xFF");
            Assert.AreEqual(0x00, resp[1], "RESET response[1] must be 0x00");
            Assert.AreEqual(MemoryCard.RespAck, resp[2], "RESET response[2] must be ACK (0x2B)");
            Assert.AreEqual(MemoryCard.RespTerm, resp[3], "RESET response[3] must be TERM (0x47)");
        }

        [TestMethod]
        public void MemoryCard_ProcessCommand_GetSpec_ReturnsAckAndSpecBytes()
        {
            var mc = MemoryCard.CreateBlank();
            // spec set 1: geometry parameters
            byte[] cmd  = { MemoryCard.AddrSlot0, MemoryCard.CmdGetSpec, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            byte[] resp = mc.ProcessCommand(cmd, 14);

            Assert.AreEqual(0xFF,              resp[0], "GetSpec response[0] = 0xFF");
            Assert.AreEqual(0x00,              resp[1], "GetSpec response[1] = 0x00");
            Assert.AreEqual(MemoryCard.RespAck, resp[2], "GetSpec response[2] = ACK");
            Assert.AreEqual(MemoryCard.RespOk1, resp[5], "GetSpec response[5] = OK1");
            Assert.AreEqual(MemoryCard.RespOk2, resp[6], "GetSpec response[6] = OK2");
            // response[7] = pageDataSize/128 = 4
            Assert.AreEqual(4, resp[7], "GetSpec spec[0] must be pageDataSize/128 = 4");
        }

        [TestMethod]
        public void MemoryCard_ProcessCommand_ReadPage_TwoPhaseTransaction()
        {
            var mc = MemoryCard.CreateBlank();

            // Write a known pattern into page 2.
            byte[] writeBuf = new byte[MemoryCard.PageDataSize];
            for (int i = 0; i < writeBuf.Length; i++) writeBuf[i] = (byte)(i ^ 0xA5);
            mc.WritePage(2, writeBuf);

            // Phase 1: send READ command header.
            byte[] hdrCmd  = { MemoryCard.AddrSlot0, MemoryCard.CmdRead, 0x00, 0x00, 0x02, 0x5A, 0x5D };
            byte[] hdrResp = mc.ProcessCommand(hdrCmd, 8);

            Assert.AreEqual(0xFF,               hdrResp[0], "Read header[0] = 0xFF");
            Assert.AreEqual(MemoryCard.RespAck, hdrResp[2], "Read header[2] = ACK");
            Assert.AreEqual(0x00,               hdrResp[4], "Read header[4] = addrH echo (0x00)");
            Assert.AreEqual(0x02,               hdrResp[5], "Read header[5] = addrL echo (0x02)");
            Assert.AreEqual(MemoryCard.RespOk1, hdrResp[6], "Read header[6] = OK1");
            Assert.AreEqual(MemoryCard.RespOk2, hdrResp[7], "Read header[7] = OK2");

            // Phase 2: send dummy bytes; receive page data + XOR + TERM.
            byte[] dataCmd  = new byte[514]; // 512 dummy + xor + term
            byte[] dataResp = mc.ProcessCommand(dataCmd, 514);

            for (int i = 0; i < MemoryCard.PageDataSize; i++)
                Assert.AreEqual(writeBuf[i], dataResp[i], $"Read data byte [{i}] must match written pattern");
            Assert.AreEqual(MemoryCard.RespTerm, dataResp[513], "Last byte of read data phase must be TERM");
        }

        [TestMethod]
        public void MemoryCard_ProcessCommand_WritePage_TwoPhaseTransaction()
        {
            var mc = MemoryCard.CreateBlank();

            // Phase 1: send WRITE command header.
            byte[] hdrCmd  = { MemoryCard.AddrSlot0, MemoryCard.CmdWrite, 0x00, 0x00, 0x03, 0x5A, 0x5D };
            byte[] hdrResp = mc.ProcessCommand(hdrCmd, 8);

            Assert.AreEqual(MemoryCard.RespAck, hdrResp[2], "Write header[2] = ACK");
            Assert.AreEqual(0x03,               hdrResp[5], "Write header[5] = addrL echo (0x03)");

            // Phase 2: send page data.
            byte[] dataCmd = new byte[514];
            for (int i = 0; i < MemoryCard.PageDataSize; i++) dataCmd[i] = (byte)(i & 0xFF);
            mc.ProcessCommand(dataCmd, 514);

            // Verify the data was stored.
            byte[] readBack = new byte[MemoryCard.PageDataSize];
            mc.ReadPage(3, readBack);
            for (int i = 0; i < MemoryCard.PageDataSize; i++)
                Assert.AreEqual(dataCmd[i], readBack[i], $"Written byte [{i}] must persist");
        }

        [TestMethod]
        public void MemoryCard_ProcessCommand_EraseCluster_ErasesCorrectPages()
        {
            var mc = MemoryCard.CreateBlank();

            // Write a known pattern to pages 4 and 5 (cluster 2).
            byte[] pat = new byte[MemoryCard.PageDataSize];
            for (int i = 0; i < pat.Length; i++) pat[i] = 0x55;
            mc.WritePage(4, pat);
            mc.WritePage(5, pat);

            // Send ERASE command targeting page 4 (cluster 2).
            byte[] cmd  = { MemoryCard.AddrSlot0, MemoryCard.CmdErase, 0x00, 0x00, 0x04, 0x5A, 0x5D };
            byte[] resp = mc.ProcessCommand(cmd, 9);

            Assert.AreEqual(MemoryCard.RespAck,  resp[2], "Erase response[2] = ACK");
            Assert.AreEqual(MemoryCard.RespTerm, resp[8], "Erase response[8] = TERM");

            // Both pages in the cluster must now be 0xFF.
            byte[] p4 = new byte[MemoryCard.PageDataSize];
            byte[] p5 = new byte[MemoryCard.PageDataSize];
            mc.ReadPage(4, p4);
            mc.ReadPage(5, p5);
            Assert.AreEqual(0xFF, p4[0],  "Page 4 byte 0 must be 0xFF after erase");
            Assert.AreEqual(0xFF, p5[255], "Page 5 byte 255 must be 0xFF after erase");
        }

        [TestMethod]
        public void Sio2_MemCard_NoDeviceConnected_ReturnsFF()
        {
            var sio2 = new Sio2();
            // No MemCard0 assigned — expect all-0xFF response.
            sio2.WriteRegister(0x00u, BuildSend3(port: 0, sendLen: 3, recvLen: 4));
            sio2.WriteRegister(0x60u, MemoryCard.AddrSlot0);
            sio2.WriteRegister(0x60u, MemoryCard.CmdReset);
            sio2.WriteRegister(0x60u, 0x00u);
            sio2.WriteRegister(0x68u, 0x01u); // CTRL START

            uint b0 = sio2.ReadRegister(0x64u);
            Assert.AreEqual(0xFFu, b0, "No-MC response must be 0xFF");
        }

        [TestMethod]
        public void Sio2_MemCard0_ResetCommand_ViaFifo()
        {
            var sio2 = new Sio2();
            var mc   = MemoryCard.CreateBlank();
            sio2.MemCard0 = mc;

            sio2.WriteRegister(0x00u, BuildSend3(port: 0, sendLen: 3, recvLen: 4));
            sio2.WriteRegister(0x60u, MemoryCard.AddrSlot0);
            sio2.WriteRegister(0x60u, MemoryCard.CmdReset);
            sio2.WriteRegister(0x60u, 0x00u);
            sio2.WriteRegister(0x68u, 0x01u); // CTRL START

            uint b0 = sio2.ReadRegister(0x64u);
            uint b2 = sio2.ReadRegister(0x64u);  // skip b1
            sio2.ReadRegister(0x64u);             // skip b2 (ACK)
            uint b3 = sio2.ReadRegister(0x64u);

            Assert.AreEqual(0xFFu,                       b0, "SIO2 MC RESET response[0] = 0xFF");
            Assert.AreEqual((uint)MemoryCard.RespTerm,   b3, "SIO2 MC RESET response[3] = TERM");
        }

        [TestMethod]
        public void Sio2_MemCard_DoesNotInterfereWithPad()
        {
            var sio2 = new Sio2();
            var pad  = new PadState();
            var mc   = MemoryCard.CreateBlank();
            sio2.Port0    = pad;
            sio2.MemCard0 = mc;

            // Pad poll on port 0.
            sio2.WriteRegister(0x00u, BuildSend3(port: 0, sendLen: 2, recvLen: 9));
            sio2.WriteRegister(0x60u, PadState.AddrPad);
            sio2.WriteRegister(0x60u, PadState.CmdPoll);
            sio2.WriteRegister(0x68u, 0x01u); // START

            sio2.ReadRegister(0x64u); // skip leading 0xFF
            uint devId = sio2.ReadRegister(0x64u);
            Assert.AreEqual((uint)PadState.DeviceId, devId,
                "Pad poll must still return DualShock2 device ID when a MC is also wired");
        }

        [TestMethod]
        public void DualCpuScheduler_MemCard0_PropertyRoundTrip()
        {
            var scheduler = new DualCpuScheduler();
            var mc        = MemoryCard.CreateBlank();

            scheduler.MemCard0 = mc;
            Assert.AreSame(mc, scheduler.MemCard0, "MemCard0 property round-trip");
            Assert.AreSame(mc, scheduler.Sio2.MemCard0, "Sio2.MemCard0 must reflect scheduler.MemCard0");

            scheduler.MemCard0 = null;
            Assert.IsNull(scheduler.MemCard0, "MemCard0 must be settable to null");
        }

        [TestMethod]
        public void DualCpuScheduler_MemCard1_PropertyRoundTrip()
        {
            var scheduler = new DualCpuScheduler();
            var mc        = MemoryCard.CreateBlank();

            scheduler.MemCard1 = mc;
            Assert.AreSame(mc, scheduler.MemCard1, "MemCard1 property round-trip");
            Assert.AreSame(mc, scheduler.Sio2.MemCard1, "Sio2.MemCard1 must reflect scheduler.MemCard1");
        }

        private static uint BuildSend3(int port, int sendLen, int recvLen)
            => (uint)(port & 0x3)
             | ((uint)(sendLen & 0x1FF) << 8)
             | ((uint)(recvLen & 0x1FF) << 18);

        // ── Minimal ISO-9660 builder helper ──────────────────────────────────


        /// <summary>
        /// Build a minimal valid ISO-9660 image with one file in the root directory.
        /// Layout: sectors 0–15 = system area (zeros); sector 16 = PVD;
        /// sector 17 = root directory; sector 18 = file data.
        /// </summary>
        private static byte[] BuildMinimalIso(string fileName, string fileContent)
        {
            const int SectorSize = 2048;
            const int RootDirLba = 17;
            const int FileLba    = 18;

            byte[] iso = new byte[SectorSize * 19]; // sectors 0–18

            // ── Sector 16: Primary Volume Descriptor ──────────────────────────
            int pvd = 16 * SectorSize;
            iso[pvd + 0] = 1;       // PVD type
            iso[pvd + 1] = (byte)'C';
            iso[pvd + 2] = (byte)'D';
            iso[pvd + 3] = (byte)'0';
            iso[pvd + 4] = (byte)'0';
            iso[pvd + 5] = (byte)'1';
            iso[pvd + 6] = 1;       // descriptor version

            // Root Directory Record at PVD offset 156
            int rr = pvd + 156;
            iso[rr + 0] = 34;       // record length
            // LBA LE
            iso[rr + 2] = (byte)(RootDirLba & 0xFF);
            // Size LE (one sector = 2048 bytes = 0x800)
            iso[rr + 10] = 0x00;
            iso[rr + 11] = 0x08;
            // File flags: directory
            iso[rr + 25] = 2;
            // Name length = 1, name = 0x00 (".")
            iso[rr + 32] = 1;
            iso[rr + 33] = 0;

            // ── Sector 17: Root directory ─────────────────────────────────────
            int dir = RootDirLba * SectorSize;
            int pos = dir;

            // "." entry
            iso[pos + 0]  = 34;
            iso[pos + 2]  = (byte)(RootDirLba & 0xFF);
            iso[pos + 10] = 0x00; iso[pos + 11] = 0x08;
            iso[pos + 25] = 2;
            iso[pos + 32] = 1;
            iso[pos + 33] = 0x00; // "."
            pos += 34;

            // ".." entry
            iso[pos + 0]  = 34;
            iso[pos + 2]  = (byte)(RootDirLba & 0xFF);
            iso[pos + 10] = 0x00; iso[pos + 11] = 0x08;
            iso[pos + 25] = 2;
            iso[pos + 32] = 1;
            iso[pos + 33] = 0x01; // ".."
            pos += 34;

            // File entry
            string nameWithVer = fileName.ToUpperInvariant() + ";1";
            int    nameLen     = nameWithVer.Length;
            int    recLen      = 33 + nameLen;
            if ((recLen & 1) != 0) recLen++; // pad to even length

            byte[] fileBytes = System.Text.Encoding.Latin1.GetBytes(fileContent);
            uint   fileSize  = (uint)fileBytes.Length;

            iso[pos + 0]  = (byte)recLen;
            iso[pos + 2]  = (byte)(FileLba & 0xFF);
            // Size LE
            iso[pos + 10] = (byte)(fileSize & 0xFF);
            iso[pos + 11] = (byte)((fileSize >> 8) & 0xFF);
            iso[pos + 12] = (byte)((fileSize >> 16) & 0xFF);
            iso[pos + 13] = (byte)((fileSize >> 24) & 0xFF);
            iso[pos + 25] = 0; // not a directory
            iso[pos + 32] = (byte)nameLen;
            for (int i = 0; i < nameLen; i++)
                iso[pos + 33 + i] = (byte)nameWithVer[i];

            // ── Sector 18: File data ──────────────────────────────────────────
            int fileBase = FileLba * SectorSize;
            Array.Copy(fileBytes, 0, iso, fileBase, fileBytes.Length);

            return iso;
        }

    }
}
