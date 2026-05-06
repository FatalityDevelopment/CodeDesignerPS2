using System.Security.Cryptography;

namespace CodeDesigner.Ps2.FileFormats.Zipper.ZDB
{
    // -------------------------------------------------------------------------
    //  Pipeline options  (pass to DecryptSection / EncryptSection)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Controls which pipeline stages are active.
    /// Toggle individual flags for debugging — e.g. disable zlib to inspect
    /// the raw AES output, or disable RSA to skip signature work entirely.
    /// </summary>
    public sealed class ZdbPipelineOptions
    {
        // ---- Singleton presets ----

        /// <summary>Full pipeline: vtable transforms + AES + zlib + RSA verify/sign.</summary>
        public static readonly ZdbPipelineOptions Full = new();

        /// <summary>
        /// Skip zlib — DecryptSection returns raw AES output (should start 0x78 0xDA).
        /// Useful when you want to inspect the compressed stream directly.
        /// </summary>
        public static readonly ZdbPipelineOptions NoZlib = new() { EnableZlib = false };

        /// <summary>
        /// Skip RSA — no signature verification on decrypt, zeroed prefix on encrypt.
        /// Useful when testing crypto without needing a valid signing keypair.
        /// </summary>
        public static readonly ZdbPipelineOptions NoRsa = new() { EnableRsaVerify = false, EnableRsaSign = false };

        /// <summary>
        /// Raw AES only — no vtable transforms, no zlib, no RSA.
        /// Useful for isolating the AES layer.
        /// </summary>
        public static readonly ZdbPipelineOptions AesOnly = new()
        {
            EnableOuterTransforms = false,
            EnableZlib = false,
            EnableRsaVerify = false,
            EnableRsaSign = false,
        };

        // ---- Individual flags ----

        /// <summary>
        /// Apply vtable outer transforms before/after AES (Layer 2 of ZDB pipeline).
        /// Default: true. Disable to skip the per-word transform step.
        /// </summary>
        public bool EnableOuterTransforms { get; init; } = true;

        /// <summary>
        /// Decompress (decrypt) or compress (encrypt) with zlib after/before AES.
        /// Confirmed from live capture: AES output is always a 0x78 0xDA zlib stream.
        /// Default: true. Disable to get the raw zlib stream without decompressing.
        /// </summary>
        public bool EnableZlib { get; init; } = true;

        /// <summary>
        /// Verify the 256-byte RSA-2048 signature prefix on decrypt using Sony's
        /// public key extracted from DNAS.BIN @ 0x94920.
        /// Default: true. Disable to skip verification (e.g. for modified ZDBs).
        /// Note: a failed verify throws <see cref="CryptographicException"/> by default;
        /// set <see cref="RsaVerifyThrowOnFailure"/> = false to demote to a warning.
        /// </summary>
        public bool EnableRsaVerify { get; init; } = true;

        /// <summary>
        /// If RSA verify fails, throw instead of continuing.
        /// Default: true (strict). Set false to log and continue — useful when
        /// testing re-encrypted ZDBs before the public key swap is in place.
        /// </summary>
        public bool RsaVerifyThrowOnFailure { get; init; } = true;

        /// <summary>
        /// Sign the section payload on encrypt and write the 256-byte prefix.
        /// Requires <see cref="SigningKey"/> to be set.
        /// Default: false (zeroed prefix). Enable once you have a custom keypair
        /// and have patched DNAS.BIN to use the matching public key.
        /// </summary>
        public bool EnableRsaSign { get; init; } = false;

        /// <summary>
        /// RSA-2048 private key used for signing on encrypt.
        /// Only needed when <see cref="EnableRsaSign"/> = true.
        /// Generate once with <see cref="ZdbRsa.GenerateSigningKeyPair"/> and store
        /// the matching public key into DNAS.BIN @ offset 0x94941.
        /// </summary>
        public RSA? SigningKey { get; init; } = null;
    }
}