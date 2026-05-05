using System;

namespace CodeDesigner.Ps2.Compression
{
    /// <summary>
    /// Patches the captured ZSealEtc IOP secrman binary (code.bin, loaded at IOP 0x00188938)
    /// to bypass the DNAS session-blob validation.
    ///
    /// Module load address: 0x00188938  (code.bin offset = IOP_addr − 0x00188938)
    ///
    /// Three DNAS bypass patches (all little-endian, MIPS R3000):
    ///
    ///   Patch 1 @ code.bin 0x0A28  (IOP 0x00189360, fn 0x001892A0):
    ///     slt at, zero, s2  → addiu at, zero, 0
    ///     Guards the key-byte write loop; setting at=0 forces the branch-not-taken
    ///     path so the loop body is skipped and no DNAS bytes are written.
    ///
    ///   Patch 2 @ code.bin 0x0FA8  (IOP 0x001898E0, fn 0x00189820):
    ///     slt at, zero, s2  → addiu at, zero, 0
    ///     Same pattern as Patch 1 in the parallel halfword-read variant.
    ///
    ///   Patch 3 @ code.bin 0x10FC  (IOP 0x00189A34, fn 0x001899F0):
    ///     beq at, zero, +74 → beq zero, zero, +74
    ///     Bounds-check guard for the 12-byte DNAS key-block loop; making it
    ///     unconditional causes an immediate skip of the entire block loop.
    ///
    /// TODO (Patch 4): Hardcode revival AES key in secrman output buffer.
    ///   After the DNAS loops are bypassed, the key buffer at *(s3+0x24) is zeroed.
    ///   If the EE ISO patches supply the revival_aes.bin key independently this is
    ///   not required.  Revival AES key: D5 F6 C6 D5 7C 66 49 76 68 25 23 D7 57 A8 F6 4B
    /// </summary>
    public static class IopSecrmanPatcher
    {
        // ── Patch 1: fn@0x001892A0 – DNAS key-byte loop guard ───────────────
        // code.bin offset: 0x0A28  (IOP addr 0x00189360)
        // slt at, zero, s2  = 0x0012082A (LE: 2A 08 12 00)
        // addiu at, zero, 0 = 0x24010000 (LE: 00 00 01 24)
        private const int Patch1Offset = 0x0A28;
        private static readonly byte[] Patch1Expected    = { 0x2A, 0x08, 0x12, 0x00 };
        private static readonly byte[] Patch1Replacement = { 0x00, 0x00, 0x01, 0x24 };

        // ── Patch 2: fn@0x00189820 – DNAS halfword-read loop guard ──────────
        // code.bin offset: 0x0FA8  (IOP addr 0x001898E0)
        // slt at, zero, s2  = 0x0012082A (LE: 2A 08 12 00)
        // addiu at, zero, 0 = 0x24010000 (LE: 00 00 01 24)
        private const int Patch2Offset = 0x0FA8;
        private static readonly byte[] Patch2Expected    = { 0x2A, 0x08, 0x12, 0x00 };
        private static readonly byte[] Patch2Replacement = { 0x00, 0x00, 0x01, 0x24 };

        // ── Patch 3: fn@0x001899F0 – DNAS 12-byte block loop guard ──────────
        // code.bin offset: 0x10FC  (IOP addr 0x00189A34)
        // beq at, zero, +74 = 0x1020004A (LE: 4A 00 20 10)
        // beq zero, zero, +74 = 0x1000004A (LE: 4A 00 00 10)
        private const int Patch3Offset = 0x10FC;
        private static readonly byte[] Patch3Expected    = { 0x4A, 0x00, 0x20, 0x10 };
        private static readonly byte[] Patch3Replacement = { 0x4A, 0x00, 0x00, 0x10 };

        /// <summary>
        /// Returns a patched copy of the IOP secrman code binary with DNAS bypass applied.
        /// </summary>
        /// <param name="codebin">
        ///   Raw bytes of ZSealEtc_bin.code.bin (113,792 bytes at IOP load address 0x00188938).
        ///   Only the code bytes — do not include the MWo3 header prefix.
        /// </param>
        /// <param name="aesKey">
        ///   Revival AES-128 key (16 bytes).  Reserved for future Patch 4 (key hardcoding).
        ///   Pass the revival key even if Patch 4 is not yet implemented.
        /// </param>
        /// <param name="aesIv">
        ///   Revival AES-128 IV (16 bytes).  Reserved for future Patch 4.
        /// </param>
        /// <returns>Patched copy; original array is not modified.</returns>
        public static byte[] Patch(byte[] codebin, byte[] aesKey, byte[] aesIv)
        {
            if (codebin is null) throw new ArgumentNullException(nameof(codebin));
            if (aesKey is null || aesKey.Length != 16)
                throw new ArgumentException("aesKey must be exactly 16 bytes.", nameof(aesKey));
            if (aesIv is null || aesIv.Length != 16)
                throw new ArgumentException("aesIv must be exactly 16 bytes.", nameof(aesIv));

            byte[] patched = (byte[])codebin.Clone();

            ApplyPatch(patched, Patch1Offset, Patch1Expected, Patch1Replacement, "Patch1(fn892A0 slt→addiu)");
            ApplyPatch(patched, Patch2Offset, Patch2Expected, Patch2Replacement, "Patch2(fn89820 slt→addiu)");
            ApplyPatch(patched, Patch3Offset, Patch3Expected, Patch3Replacement, "Patch3(fn899F0 beq→always)");

            // TODO Patch 4: write aesKey bytes into secrman output buffer.
            // Leaving this for a future commit once Patch 4 address is confirmed.

            return patched;
        }

        // ── Private helpers ─────────────────────────────────────────────────

        private static void ApplyPatch(
            byte[] data,
            int offset,
            byte[] expected,
            byte[] replacement,
            string name)
        {
            if (offset + 4 > data.Length)
                throw new InvalidOperationException(
                    $"IopSecrmanPatcher: {name} offset 0x{offset:X4} is beyond the end of the " +
                    $"supplied binary ({data.Length} bytes).  Wrong code.bin supplied?");

            for (int i = 0; i < 4; i++)
            {
                if (data[offset + i] != expected[i])
                    throw new InvalidOperationException(
                        $"IopSecrmanPatcher: {name} @ 0x{offset:X4}+{i}: " +
                        $"expected 0x{expected[i]:X2} but found 0x{data[offset + i]:X2}.  " +
                        $"The secrman binary may differ from the expected captured version.");
            }

            Buffer.BlockCopy(replacement, 0, data, offset, 4);
        }
    }
}
