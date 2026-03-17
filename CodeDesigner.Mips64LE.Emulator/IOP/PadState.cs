using System;

namespace CodeDesigner.Mips64LE.Common.Emulator.IOP
{
    /// <summary>
    /// Represents the state of a DualShock 2 controller attached to an SIO2 port.
    ///
    /// Button state is stored as a 16-bit value where <b>0 = pressed</b>
    /// (active-low, matching the PS2 wire protocol).  The default value
    /// <c>0xFFFF</c> means all buttons released.
    ///
    /// Button bit layout (both bytes active-low):
    /// <list type="table">
    ///   <item><term>bit 0  (Buttons byte 0)</term><description>Select</description></item>
    ///   <item><term>bit 1  (Buttons byte 0)</term><description>L3</description></item>
    ///   <item><term>bit 2  (Buttons byte 0)</term><description>R3</description></item>
    ///   <item><term>bit 3  (Buttons byte 0)</term><description>Start</description></item>
    ///   <item><term>bit 4  (Buttons byte 0)</term><description>Up</description></item>
    ///   <item><term>bit 5  (Buttons byte 0)</term><description>Right</description></item>
    ///   <item><term>bit 6  (Buttons byte 0)</term><description>Down</description></item>
    ///   <item><term>bit 7  (Buttons byte 0)</term><description>Left</description></item>
    ///   <item><term>bit 8  (Buttons byte 1)</term><description>L2</description></item>
    ///   <item><term>bit 9  (Buttons byte 1)</term><description>R2</description></item>
    ///   <item><term>bit 10 (Buttons byte 1)</term><description>L1</description></item>
    ///   <item><term>bit 11 (Buttons byte 1)</term><description>R1</description></item>
    ///   <item><term>bit 12 (Buttons byte 1)</term><description>Triangle</description></item>
    ///   <item><term>bit 13 (Buttons byte 1)</term><description>Circle</description></item>
    ///   <item><term>bit 14 (Buttons byte 1)</term><description>Cross</description></item>
    ///   <item><term>bit 15 (Buttons byte 1)</term><description>Square</description></item>
    /// </list>
    ///
    /// DualShock 2 poll response (command 0x42, 9 bytes):
    /// <code>
    ///   Host  → 0x01 0x42 0x00 0x00 0x00 0x00 0x00 0x00 0x00
    ///   Device← 0xFF 0x73 0x5A btL  btH  RxX  RxY  LxX  LxY
    /// </code>
    ///
    /// Reference: PCSX2 pcsx2/SIO/Pad/PadDualshock2.cpp.
    /// </summary>
    public sealed class PadState
    {
        // ── DualShock 2 device constants ──────────────────────────────────────

        /// <summary>Device ID byte returned in the poll response (0x73 = DualShock 2 analog).</summary>
        public const byte DeviceId = 0x73;

        /// <summary>Protocol acknowledge byte (constant 0x5A).</summary>
        public const byte AckByte  = 0x5A;

        /// <summary>Minimum number of bytes in a DualShock 2 poll response (9 bytes).</summary>
        public const int PollResponseMinSize = 9;

        // ── PS2 pad command bytes ─────────────────────────────────────────────

        /// <summary>Address byte that selects the pad slot (sent as the first command byte).</summary>
        public const byte AddrPad = 0x01;

        /// <summary>Poll / read-data command byte.</summary>
        public const byte CmdPoll = 0x42;

        // ── Button bit masks ──────────────────────────────────────────────────

        /// <summary>Button bit for Select.</summary>
        public const ushort BTN_SELECT   = 1 << 0;

        /// <summary>Button bit for L3 (left stick click).</summary>
        public const ushort BTN_L3       = 1 << 1;

        /// <summary>Button bit for R3 (right stick click).</summary>
        public const ushort BTN_R3       = 1 << 2;

        /// <summary>Button bit for Start.</summary>
        public const ushort BTN_START    = 1 << 3;

        /// <summary>Button bit for D-pad Up.</summary>
        public const ushort BTN_UP       = 1 << 4;

        /// <summary>Button bit for D-pad Right.</summary>
        public const ushort BTN_RIGHT    = 1 << 5;

        /// <summary>Button bit for D-pad Down.</summary>
        public const ushort BTN_DOWN     = 1 << 6;

        /// <summary>Button bit for D-pad Left.</summary>
        public const ushort BTN_LEFT     = 1 << 7;

        /// <summary>Button bit for L2.</summary>
        public const ushort BTN_L2       = 1 << 8;

        /// <summary>Button bit for R2.</summary>
        public const ushort BTN_R2       = 1 << 9;

        /// <summary>Button bit for L1.</summary>
        public const ushort BTN_L1       = 1 << 10;

        /// <summary>Button bit for R1.</summary>
        public const ushort BTN_R1       = 1 << 11;

        /// <summary>Button bit for Triangle.</summary>
        public const ushort BTN_TRIANGLE = 1 << 12;

        /// <summary>Button bit for Circle.</summary>
        public const ushort BTN_CIRCLE   = 1 << 13;

        /// <summary>Button bit for Cross (×).</summary>
        public const ushort BTN_CROSS    = 1 << 14;

        /// <summary>Button bit for Square.</summary>
        public const ushort BTN_SQUARE   = 1 << 15;

        // ── State ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Current button state word.  Each bit is <c>0</c> when the corresponding
        /// button is pressed and <c>1</c> when it is released (active-low, PS2 protocol).
        /// Default <c>0xFFFF</c> = all buttons released.
        /// </summary>
        public ushort Buttons { get; set; } = 0xFFFF;

        /// <summary>Right analog stick X axis (0x00 = full left, 0x80 = centre, 0xFF = full right).</summary>
        public byte RightX { get; set; } = 0x80;

        /// <summary>Right analog stick Y axis (0x00 = full up, 0x80 = centre, 0xFF = full down).</summary>
        public byte RightY { get; set; } = 0x80;

        /// <summary>Left analog stick X axis (0x00 = full left, 0x80 = centre, 0xFF = full right).</summary>
        public byte LeftX { get; set; } = 0x80;

        /// <summary>Left analog stick Y axis (0x00 = full up, 0x80 = centre, 0xFF = full down).</summary>
        public byte LeftY { get; set; } = 0x80;

        // ── Button helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Press the button(s) specified by <paramref name="mask"/> (clears their bits to 0).
        /// </summary>
        public void Press(ushort mask)   => Buttons = (ushort)(Buttons & ~mask);

        /// <summary>
        /// Release the button(s) specified by <paramref name="mask"/> (sets their bits to 1).
        /// </summary>
        public void Release(ushort mask) => Buttons = (ushort)(Buttons |  mask);

        // ── Device protocol ───────────────────────────────────────────────────

        /// <summary>
        /// Process a SIO2 command sequence and return the response byte array.
        ///
        /// Supports command 0x42 (poll / read data).  All other commands return
        /// an all-0xFF array of <paramref name="responseLength"/> bytes.
        /// </summary>
        /// <param name="cmd">Command bytes sent by the IOP via FIFO_IN.</param>
        /// <param name="responseLength">Expected number of response bytes (from SEND3 recvLen).</param>
        /// <returns>Response bytes to be enqueued in the SIO2 FIFO_OUT.</returns>
        public byte[] ProcessCommand(byte[] cmd, int responseLength)
        {
            if (cmd.Length >= 2 && cmd[0] == AddrPad && cmd[1] == CmdPoll)
                return BuildPollResponse(responseLength);

            // Unknown command — return all 0xFF.
            byte[] fallback = new byte[responseLength];
            for (int i = 0; i < responseLength; i++)
                fallback[i] = 0xFF;
            return fallback;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Build the DualShock 2 analog poll response.
        /// The response is always at least 9 bytes; extra bytes are 0xFF.
        /// </summary>
        private byte[] BuildPollResponse(int length)
        {
            // DualShock 2 response layout:
            //   [0] 0xFF  — leading idle (mirrors host's first clock byte)
            //   [1] 0x73  — device ID (DualShock 2 analog mode)
            //   [2] 0x5A  — protocol acknowledge
            //   [3] btL   — button byte 0 (Select/L3/R3/Start/Up/Right/Down/Left), active-low
            //   [4] btH   — button byte 1 (L2/R2/L1/R1/Triangle/Circle/Cross/Square), active-low
            //   [5] RxX   — right stick X
            //   [6] RxY   — right stick Y
            //   [7] LxX   — left stick X
            //   [8] LxY   — left stick Y

            int    size = Math.Max(length, PollResponseMinSize);
            byte[] resp = new byte[size];
            for (int i = 0; i < size; i++) resp[i] = 0xFF;

            resp[1] = DeviceId;
            resp[2] = AckByte;
            resp[3] = (byte)(Buttons & 0xFF);
            resp[4] = (byte)(Buttons >> 8);
            resp[5] = RightX;
            resp[6] = RightY;
            resp[7] = LeftX;
            resp[8] = LeftY;

            // Return only the requested length.
            if (size == length)
                return resp;

            byte[] trimmed = new byte[length];
            Array.Copy(resp, trimmed, Math.Min(length, size));
            return trimmed;
        }
    }
}
