namespace CodeDesigner.Ps2.FileFormats.Zipper.Apache
{
    // -------------------------------------------------------------------------
    //  Confirmed file offsets
    // -------------------------------------------------------------------------

    /// <summary>
    /// Confirmed byte offsets within APACHE00.ZDB (SOCOM II BASCUS-97275SOCOMII).
    /// All offsets verified from live EE RAM capture and raw file analysis.
    /// </summary>
    public static class ApacheZdbOffsets
    {
        // ZDB structural offsets
        public const int MainHeader = 0x000;   // ZDB Type-2 main header, 0xFC bytes
        public const int Section0Entry = 0x0A0;   // "ftscore"  directory entry, 0x5C bytes
        public const int Section1Entry = 0x0FC;   // "zsealetc" directory entry, 0x5C bytes

        // Session context
        /// <summary>DNAS session context blob (0x300 bytes, opaque). data_offset in section table = 0x158.</summary>
        public const int SessionCtxBlob = 0x158;

        /// <summary>
        /// Offset of the 16-byte "key-position" material within the ftscore section data.
        /// = session_ctx + 0x300. For standard APACHE00.ZDB: file offset 0x458.
        /// <b>WARNING:</b> these bytes are part of the Sony DNAS session blob, NOT a raw AES key.
        /// The IOP secrman derives the actual AES session key from the DNAS context block;
        /// it never appears in plaintext in the file.
        /// (Previously annotated as EE gMediusSessionKey @ 0x01E7EF30 — confirmed incorrect.)
        /// </summary>
        public const int SessionKeyOffset = 0x300;

        /// <summary>
        /// Offset of the 16-byte "IV-position" material within the ftscore section data.
        /// = session_ctx + 0x310. For standard APACHE00.ZDB: file offset 0x468.
        /// <b>WARNING:</b> these bytes are part of the Sony DNAS session blob, NOT a raw AES IV.
        /// (Previously annotated as EE gMediusSessionIV @ 0x01E7EF40 — confirmed incorrect.)
        /// </summary>
        public const int SessionIvOffset = 0x310;

        // Absolute file offsets for standard 2-entry APACHE00.ZDB layout (informational).
        // Use SessionKeyOffset / SessionIvOffset relative to ftscore data_offset instead.
        /// <summary>AES key absolute file offset (standard layout: ftscore_offset=0x158, key=0x158+0x300).</summary>
        public const int AesKey = 0x458;
        /// <summary>AES IV  absolute file offset (standard layout: ftscore_offset=0x158, iv=0x158+0x310).</summary>
        public const int AesIv = 0x468;

        // Section payloads
        /// <summary>Section-0 ("ftscore") data_offset in file = 0x158 for standard layout (entries end at 0x158).</summary>
        public const int FTScorePayload = 0x158;

        /// <summary>Section-1 ("zsealetc") data_offset in file = 0x0D10C8.</summary>
        public const int ZSealEtcPayload = 0x0D10C8;

        /// <summary>RSA-2048 Sony signature prefix size preceding each encrypted payload (256 bytes).</summary>
        public const int RsaSigSize = 0x100;

        /// <summary>AES chunk stride: 8 blocks x 16 bytes. IV resets per chunk (not standard CBC).</summary>
        public const int AesChunkStride = 0x080;
    }
}
