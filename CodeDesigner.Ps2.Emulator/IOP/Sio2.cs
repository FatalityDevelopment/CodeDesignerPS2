using System;
using System.Collections.Generic;

namespace CodeDesigner.Ps2.Emulator.IOP
{
    /// <summary>
    /// Emulates the PS2 IOP SIO2 (Serial I/O 2) controller.
    ///
    /// The SIO2 is the IOP peripheral that handles communication with
    /// DualShock 2 controllers and memory cards via a serial protocol.
    ///
    /// Memory-mapped registers (byte offsets from <see cref="RegBase"/> = 0x1F808200):
    /// <list type="table">
    ///   <item><term>+0x00–+0x3F</term><description>SEND3[0..15] — 16 × uint32 transfer descriptors</description></item>
    ///   <item><term>+0x40</term><description>SEND1 port 0 (SIO1 baud/parameter)</description></item>
    ///   <item><term>+0x44</term><description>SEND1 port 1</description></item>
    ///   <item><term>+0x48</term><description>SEND2 port 0</description></item>
    ///   <item><term>+0x4C</term><description>SEND2 port 1</description></item>
    ///   <item><term>+0x60</term><description>FIFO_IN — write one command byte per access</description></item>
    ///   <item><term>+0x64</term><description>FIFO_OUT — read one response byte per access</description></item>
    ///   <item><term>+0x68</term><description>CTRL — control register (bit 0 = start transfer, bit 2 = reset)</description></item>
    ///   <item><term>+0x70</term><description>RECV1 — transfer status (0x1D000 on success)</description></item>
    ///   <item><term>+0x74</term><description>RECV2 — device presence flags</description></item>
    ///   <item><term>+0x78</term><description>RECV3 — reserved</description></item>
    /// </list>
    ///
    /// SEND3 descriptor bit layout:
    /// <list type="bullet">
    ///   <item>bits[1:0]  = port (0 or 1)</item>
    ///   <item>bits[5:2]  = slot (0–3, for multitap)</item>
    ///   <item>bits[16:8] = send length (number of command bytes)</item>
    ///   <item>bits[26:18]= receive length (expected response bytes)</item>
    /// </list>
    ///
    /// Transfer sequence:
    /// <list type="number">
    ///   <item>IOP fills SEND3[0..n] with transfer descriptors (zero terminates the list).</item>
    ///   <item>IOP writes command bytes one at a time to FIFO_IN.</item>
    ///   <item>IOP writes CTRL bit 0 (START) to trigger all pending descriptors.</item>
    ///   <item>SIO2 processes each non-zero descriptor, routing bytes through the attached device.</item>
    ///   <item>IOP reads response bytes one at a time from FIFO_OUT.</item>
    ///   <item><see cref="TransferComplete"/> is raised; IOP INTC bit 17 is asserted by the bus.</item>
    /// </list>
    ///
    /// Reference: PCSX2 pcsx2/SIO/Sio2.cpp, pcsx2/SIO/Pad/PadDualshock2.cpp.
    /// </summary>
    public sealed class Sio2
    {
        // ── Register base and window ──────────────────────────────────────────

        /// <summary>SIO2 register base address in IOP address space.</summary>
        public const uint RegBase = 0x1F808200u;

        /// <summary>Size of the SIO2 register window (128 bytes).</summary>
        public const uint RegSize = 0x80u;

        // ── Register offsets (relative to RegBase) ────────────────────────────

        private const uint OFF_SEND3_BASE = 0x00u;  // 16 × uint32 (0x00–0x3C)
        private const uint OFF_SEND1_P0   = 0x40u;
        private const uint OFF_SEND1_P1   = 0x44u;
        private const uint OFF_SEND2_P0   = 0x48u;
        private const uint OFF_SEND2_P1   = 0x4Cu;
        private const uint OFF_FIFO_IN    = 0x60u;
        private const uint OFF_FIFO_OUT   = 0x64u;
        private const uint OFF_CTRL       = 0x68u;
        private const uint OFF_RECV1      = 0x70u;
        private const uint OFF_RECV2      = 0x74u;
        private const uint OFF_RECV3      = 0x78u;

        // ── SEND3 descriptor field masks ──────────────────────────────────────

        private const uint SEND3_PORT_MASK  = 0x3u;    // bits[1:0]
        private const int  SEND3_SEND_SHIFT = 8;
        private const uint SEND3_SEND_MASK  = 0x1FFu;  // bits[16:8] (9 bits)
        private const int  SEND3_RECV_SHIFT = 18;
        private const uint SEND3_RECV_MASK  = 0x1FFu;  // bits[26:18] (9 bits)

        // ── CTRL bits ─────────────────────────────────────────────────────────

        private const uint CTRL_START = 1u << 0;   // begin transfer
        private const uint CTRL_RESET = 1u << 2;   // reset FIFOs and state

        // ── RECV1 success value ───────────────────────────────────────────────

        /// <summary>Value placed in RECV1 after a successful transfer.</summary>
        public const uint RECV1_SUCCESS = 0x1D000u;

        // ── State ─────────────────────────────────────────────────────────────

        /// <summary>16 SEND3 transfer descriptors written by the IOP before a transfer.</summary>
        private readonly uint[] _send3 = new uint[16];

        private uint _send1Port0;
        private uint _send1Port1;
        private uint _send2Port0;
        private uint _send2Port1;
        private uint _ctrl;
        private uint _recv1;
        private uint _recv2;

        // Index of the next SEND3 entry to process; reset to 0 by CTRL_RESET.
        private int _send3Head;

        // Input FIFO: IOP writes command bytes here before triggering a transfer.
        private readonly Queue<byte> _fifoIn  = new Queue<byte>(64);

        // Output FIFO: IOP reads response bytes from here after a transfer.
        private readonly Queue<byte> _fifoOut = new Queue<byte>(64);

        // ── Connected devices ─────────────────────────────────────────────────

        /// <summary>
        /// Pad (or other device) attached to SIO2 port 0.  When <c>null</c>,
        /// port 0 returns all-0xFF (no device connected).
        /// </summary>
        public PadState? Port0 { get; set; }

        /// <summary>
        /// Pad (or other device) attached to SIO2 port 1.  When <c>null</c>,
        /// port 1 returns all-0xFF (no device connected).
        /// </summary>
        public PadState? Port1 { get; set; }

        /// <summary>
        /// Memory card attached to SIO2 port 0 (slot A).  When <c>null</c>,
        /// memory-card commands on port 0 return all-0xFF (no card inserted).
        /// </summary>
        public MemoryCard? MemCard0 { get; set; }

        /// <summary>
        /// Memory card attached to SIO2 port 1 (slot B).  When <c>null</c>,
        /// memory-card commands on port 1 return all-0xFF (no card inserted).
        /// </summary>
        public MemoryCard? MemCard1 { get; set; }

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>
        /// Raised when a SIO2 transfer completes (after CTRL START bit is written).
        /// <see cref="IopMemoryBus"/> wires this event to assert IOP INTC bit 17.
        /// </summary>
        public event EventHandler? TransferComplete;

        // ── Register I/O ──────────────────────────────────────────────────────

        /// <summary>
        /// Read a 32-bit register at <paramref name="offset"/> (relative to <see cref="RegBase"/>).
        /// </summary>
        public uint ReadRegister(uint offset)
        {
            // SEND3 array occupies offsets 0x00–0x3C (16 × 4 bytes)
            if (offset < 0x40u && (offset & 3u) == 0u)
                return _send3[offset >> 2];

            return offset switch
            {
                OFF_SEND1_P0 => _send1Port0,
                OFF_SEND1_P1 => _send1Port1,
                OFF_SEND2_P0 => _send2Port0,
                OFF_SEND2_P1 => _send2Port1,
                OFF_FIFO_OUT => DequeueFromFifoOut(),
                OFF_CTRL     => _ctrl,
                OFF_RECV1    => _recv1,
                OFF_RECV2    => _recv2,
                OFF_RECV3    => 0u,
                _            => 0u
            };
        }

        /// <summary>
        /// Write a 32-bit register at <paramref name="offset"/> (relative to <see cref="RegBase"/>).
        /// Writing a byte to FIFO_IN enqueues only the low byte of <paramref name="value"/>.
        /// </summary>
        public void WriteRegister(uint offset, uint value)
        {
            // SEND3 array occupies offsets 0x00–0x3C
            if (offset < 0x40u && (offset & 3u) == 0u)
            {
                _send3[offset >> 2] = value;
                return;
            }

            switch (offset)
            {
                case OFF_SEND1_P0:
                    _send1Port0 = value;
                    break;
                case OFF_SEND1_P1:
                    _send1Port1 = value;
                    break;
                case OFF_SEND2_P0:
                    _send2Port0 = value;
                    break;
                case OFF_SEND2_P1:
                    _send2Port1 = value;
                    break;
                case OFF_FIFO_IN:
                    // The FIFO_IN port accepts one byte per access.
                    _fifoIn.Enqueue((byte)(value & 0xFFu));
                    break;
                case OFF_CTRL:
                    _ctrl = value;
                    if ((value & CTRL_RESET) != 0)
                        ResetState();
                    if ((value & CTRL_START) != 0)
                        ExecuteTransfer();
                    break;
            }
        }

        // ── Internal transfer execution ───────────────────────────────────────

        private uint DequeueFromFifoOut()
            => _fifoOut.Count > 0 ? _fifoOut.Dequeue() : 0xFFu;

        private void ResetState()
        {
            _fifoIn.Clear();
            _fifoOut.Clear();
            _send3Head = 0;
            _recv1 = 0u;
            _recv2 = 0u;
        }

        /// <summary>
        /// Process all pending (non-zero) SEND3 descriptors starting from <c>_send3Head</c>.
        /// Called when CTRL bit 0 (START) is written.
        /// </summary>
        private void ExecuteTransfer()
        {
            while (_send3Head < _send3.Length)
            {
                uint desc = _send3[_send3Head];
                if (desc == 0u) break;

                ProcessDescriptor(desc);
                _send3Head++;
            }

            _recv1 = RECV1_SUCCESS;
            _ctrl &= ~CTRL_START;
            TransferComplete?.Invoke(this, EventArgs.Empty);
        }

        private void ProcessDescriptor(uint desc)
        {
            int port    = (int)(desc & SEND3_PORT_MASK);
            int sendLen = (int)((desc >> SEND3_SEND_SHIFT) & SEND3_SEND_MASK);
            int recvLen = (int)((desc >> SEND3_RECV_SHIFT) & SEND3_RECV_MASK);

            // Collect command bytes from the input FIFO.
            byte[] cmd = new byte[sendLen];
            for (int i = 0; i < sendLen; i++)
                cmd[i] = _fifoIn.Count > 0 ? _fifoIn.Dequeue() : (byte)0xFFu;

            // Route to the appropriate device based on the address byte.
            // Pads use address byte 0x01; memory cards use 0x81 (or 0x82).
            byte addrByte = cmd.Length > 0 ? cmd[0] : (byte)0xFF;
            byte[] response;

            if (addrByte == MemoryCard.AddrSlot0 || addrByte == MemoryCard.AddrSlot1)
            {
                // Memory-card command: route to the card on the selected port.
                MemoryCard? mc = port == 0 ? MemCard0 : MemCard1;
                response = mc != null
                    ? mc.ProcessCommand(cmd, recvLen)
                    : BuildNoDeviceResponse(recvLen);

                if (mc != null)
                    _recv2 |= 0x0011u;
            }
            else
            {
                // Pad (or unknown) command.
                PadState? pad = port == 0 ? Port0 : Port1;
                response = pad != null
                    ? pad.ProcessCommand(cmd, recvLen)
                    : BuildNoDeviceResponse(recvLen);

                if (pad != null)
                    _recv2 |= 0x1100u;
            }

            // Push response bytes into the output FIFO.
            int filled = Math.Min(response.Length, recvLen);
            for (int i = 0; i < filled; i++)
                _fifoOut.Enqueue(response[i]);

            // Pad with 0xFF when the response is shorter than expected.
            for (int i = filled; i < recvLen; i++)
                _fifoOut.Enqueue(0xFF);
        }

        private static byte[] BuildNoDeviceResponse(int length)
        {
            byte[] resp = new byte[length];
            for (int i = 0; i < length; i++)
                resp[i] = 0xFF;
            return resp;
        }
    }
}
