namespace CodeDesigner.Ps2.FileFormats.Zipper.ZDB
{
    // -------------------------------------------------------------------------
    //  Outer vtable transforms (EE 0x00562198, all 16 confirmed)
    // -------------------------------------------------------------------------

    /// <summary>
    /// All 16 outer transform functions from the ZDB vtable at EE 0x00562198 (post-patch).
    ///
    /// Byte permutation notation (little-endian word, b0=LSB):
    ///   bpermc([b3,b2,b1,b0]) -> [b2,b0,b3,b1]  = (b2 LSL 24)|(b0 LSL 16)|(b3 LSL 8)|b1
    ///   bperm7([b3,b2,b1,b0]) -> [b1,b3,b0,b2]  = (b1 LSL 24)|(b3 LSL 16)|(b0 LSL 8)|b2
    ///   bpermc and bperm7 are mutual inverses.
    ///
    /// XOR1 = 0x09F8ED17,  XOR2 = 0xA95128C3
    /// </summary>
    public static class ZdbVtable
    {
        public const uint XOR1 = 0x09F8ED17;
        public const uint XOR2 = 0xA95128C3;

        // ------------------------------------------------------------------
        // Bit-rotation and byte-permutation helpers
        // ------------------------------------------------------------------

        public static uint Rol32(uint w, int n) => (w << n) | (w >> (32 - n));

        /// <summary>bpermc: [b3,b2,b1,b0]->[b2,b0,b3,b1]. Vtable[C] EE 0x005419D0.</summary>
        public static uint Bpermc(uint w)
        {
            byte b0 = (byte)w, b1 = (byte)(w >> 8), b2 = (byte)(w >> 16), b3 = (byte)(w >> 24);
            return ((uint)b2 << 24) | ((uint)b0 << 16) | ((uint)b3 << 8) | b1;
        }

        /// <summary>bperm7: [b3,b2,b1,b0]->[b1,b3,b0,b2]. Used inside Vtable[7] EE 0x00534370.</summary>
        public static uint Bperm7(uint w)
        {
            byte b0 = (byte)w, b1 = (byte)(w >> 8), b2 = (byte)(w >> 16), b3 = (byte)(w >> 24);
            return ((uint)b1 << 24) | ((uint)b3 << 16) | ((uint)b0 << 8) | b2;
        }

        // ------------------------------------------------------------------
        // Forward transforms (decrypt direction: ZDB raw -> AES input)
        // ------------------------------------------------------------------

        public static uint Vt0(uint w) => w ^ XOR1;                                   // EE 0x00534498
        public static uint Vt1(uint w) => w ^ XOR2;                                   // EE 0x00534538
        public static uint Vt2(uint w) => Rol32(w, 19);                               // EE 0x005345D8
        public static uint Vt3(uint w) => Rol32(w, 24);                               // EE 0x00534698  (= ROR8)
        public static uint Vt4(uint w) => Rol32(w, 16);                               // EE 0x00534758
        public static uint Vt5(uint w) => Rol32(w ^ XOR1, 19);                        // EE 0x00534188
        public static uint Vt6(uint w) => Rol32(w ^ XOR2, 16);                        // EE 0x00534260
        public static uint Vt7(uint w) => Bperm7(Rol32(w ^ XOR1, 11));                // EE 0x00534370  ROL24+ROL19=ROL11
        public static uint Vt8(uint w) => w ^ XOR1;                                   // EE 0x00536D20  alias of Vt0
        public static uint Vt9(uint w) => w ^ XOR2;                                   // EE 0x00536DC0  alias of Vt1
        public static uint VtA(uint w) => Rol32(w, 13);                               // EE 0x00541870
        public static uint VtB(uint w) => Rol32(w, 8);                                // EE 0x00541920
        public static uint VtC(uint w) => Bpermc(w);                                  // EE 0x005419D0
        public static uint VtD(uint w) => Rol32(w, 13) ^ XOR1;                        // EE 0x00536A48
        public static uint VtE(uint w) => Bpermc(Rol32(w, 8) ^ XOR2);                // EE 0x00536B08
        public static uint VtF(uint w) => Rol32(Bpermc(w), 21) ^ XOR1;               // EE 0x00536C08  bpermc->ROL13->ROL8=ROL21->XOR1

        // ------------------------------------------------------------------
        // Inverse transforms (encrypt direction: plaintext -> ZDB raw)
        // ------------------------------------------------------------------

        public static uint Iv0(uint w) => w ^ XOR1;                                   // XOR self-inverse
        public static uint Iv1(uint w) => w ^ XOR2;
        public static uint Iv2(uint w) => Rol32(w, 13);                               // ROR19 = ROL13
        public static uint Iv3(uint w) => Rol32(w, 8);                                // ROR24 = ROL8
        public static uint Iv4(uint w) => Rol32(w, 16);                               // ROL16 self-inverse
        public static uint Iv5(uint w) => Rol32(w, 13) ^ XOR1;                        // ROR19->XOR1
        public static uint Iv6(uint w) => Rol32(w, 16) ^ XOR2;                        // ROL16->XOR2
        public static uint Iv7(uint w) => Rol32(Bpermc(w), 21) ^ XOR1;               // inv(bperm7(ROL11(w^XOR1))) = inv_bperm7->ROR11->XOR1 = bpermc->ROL21->XOR1
        public static uint Iv8(uint w) => w ^ XOR1;
        public static uint Iv9(uint w) => w ^ XOR2;
        public static uint IvA(uint w) => Rol32(w, 19);                               // ROR13 = ROL19
        public static uint IvB(uint w) => Rol32(w, 24);                               // ROR8  = ROL24
        public static uint IvC(uint w) => Bperm7(w);                                  // inv_bpermc = bperm7
        public static uint IvD(uint w) => Rol32(w ^ XOR1, 19);                        // XOR1->ROR13 = XOR1->ROL19
        public static uint IvE(uint w) => Rol32(Bperm7(w) ^ XOR2, 24);               // inv_bpermc->XOR2->ROR8 = bperm7->XOR2->ROL24
        public static uint IvF(uint w) => Bperm7(Rol32(w ^ XOR1, 11));               // XOR1->ROR21->inv_bpermc = XOR1->ROL11->bperm7. Verify: ivF==vt7

        // ------------------------------------------------------------------
        // Dispatch tables (0x0-0xF)
        // ------------------------------------------------------------------

        private static readonly Func<uint, uint>[] _fwd =
        {
            Vt0, Vt1, Vt2, Vt3, Vt4, Vt5, Vt6, Vt7,
            Vt8, Vt9, VtA, VtB, VtC, VtD, VtE, VtF,
        };

        private static readonly Func<uint, uint>[] _inv =
        {
            Iv0, Iv1, Iv2, Iv3, Iv4, Iv5, Iv6, Iv7,
            Iv8, Iv9, IvA, IvB, IvC, IvD, IvE, IvF,
        };

        /// <summary>Applies forward transform index (0–F) to w.</summary>
        public static uint ApplyForward(int idx, uint w) => _fwd[idx & 0xF](w);

        /// <summary>Applies inverse transform index (0–F) to w.</summary>
        public static uint ApplyInverse(int idx, uint w) => _inv[idx & 0xF](w);
    }
}
