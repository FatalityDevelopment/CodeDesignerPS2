using System.Security.Cryptography;

namespace CodeDesigner.Ps2.Multiplayer.Crypto;

/// <summary>
/// DNAS response encryption — C# port of DNASrep's connect.php encrypt3n().
///
/// The PS2 DNAS "connect" exchange uses a custom Triple-DES CBC-like scheme:
///
///   encrypt3n(data, offset, length, key1, key2, key3, xorSeed)
///     Processes the data 8 bytes at a time (DES block size).
///     For each 8-byte block:
///       1. XOR the block with the running XOR-seed (previous ciphertext or initial seed).
///       2. DES-ECB encrypt with key1.
///       3. DES-ECB decrypt with key2.
///       4. DES-ECB encrypt with key3.
///       5. The result becomes both the output block and the new XOR-seed for the next block.
///
/// Two passes are applied to every "connect" response:
///
///   Pass A — per-request dynamic keys (derived from the client's request body):
///     Keys derived from SHA1(body[0x34..0x134]) and SHA1(body[0x48..0x134]).
///     Applied at offset 0xC8 for 0x20 bytes.
///
///   Pass B — hardcoded envelope keys (same for all games):
///     Applied at offset 0x28 for 0x120 bytes.
///
/// Reference: https://github.com/corbin-zip/DNASrep/blob/master/www/dnas/us-gw/connect.php
/// </summary>
public static class DnasCrypto
{
    // ── Hardcoded envelope keys (same across all games / regions) ────────────
    private static readonly byte[] EnvKey1   = FromHex("eb711416cb0ab016");
    private static readonly byte[] EnvKey2   = FromHex("ae190174b5ce6339");
    private static readonly byte[] EnvKey3   = FromHex("7b01b91880145e34");
    private static readonly byte[] EnvXorSeed = FromHex("c510a6400a9b022f");

    // ── Offsets / lengths ────────────────────────────────────────────────────
    private const int PassAOffset = 0xC8;
    private const int PassALength = 0x20;
    private const int PassBOffset = 0x28;
    private const int PassBLength = 0x120;

    // ── SHA1 source slices inside the request body ───────────────────────────
    private const int Chksum1Offset = 0x34;
    private const int Chksum1Length = 0x100;   // body[0x34..0x134]
    private const int Chksum2Offset = 0x48;
    private const int Chksum2Length = 0xEC;    // body[0x48..0x134]

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Derive the per-request 3DES keys from the client's DNAS request body,
    /// then apply both encryption passes to a copy of <paramref name="response"/>
    /// and return the encrypted bytes.
    /// </summary>
    public static byte[] EncryptConnectResponse(byte[] request, byte[] response)
    {
        if (request.Length < Chksum2Offset + Chksum2Length)
        {
            // Request too short — return the raw response unmodified so we at
            // least send something rather than silently dropping it.
            return response;
        }

        // ── Key derivation ───────────────────────────────────────────────────
        // PHP:  $chksum1 = sha1(substr($packet, 0x34, 0x100));
        //        $chksum2 = sha1(substr($packet, 0x48,  0xec));
        //        $fullkey  = substr($chksum2, 0, 0x14*2) . substr($chksum1, 0, 0x0c*2);
        //
        // sha1() in PHP returns a 40-character lowercase hex string.
        // We reproduce that as a 64-character hex string (fullkey),
        // then unpack every 16 hex chars into 8 raw bytes for each key.

        string chksum1Hex = SHA1Hex(request, Chksum1Offset, Chksum1Length);
        string chksum2Hex = SHA1Hex(request, Chksum2Offset, Chksum2Length);

        // fullkey = chksum2[0..40] + chksum1[0..24]  (total 64 hex chars = 32 bytes)
        string fullKey = chksum2Hex[..40] + chksum1Hex[..24];

        byte[] desKey1  = FromHex(fullKey[0..16]);
        byte[] desKey2  = FromHex(fullKey[16..32]);
        byte[] desKey3  = FromHex(fullKey[32..48]);
        byte[] xorSeed  = FromHex(fullKey[48..64]);

        // ── Apply encryption ─────────────────────────────────────────────────
        var result = (byte[])response.Clone();

        // Pass A: dynamic keys at [0xC8 .. 0xC8+0x20)
        if (result.Length >= PassAOffset + PassALength)
            Encrypt3n(result, PassAOffset, PassALength, desKey1, desKey2, desKey3, xorSeed);

        // Pass B: hardcoded envelope keys at [0x28 .. 0x28+0x120)
        if (result.Length >= PassBOffset + PassBLength)
            Encrypt3n(result, PassBOffset, PassBLength, EnvKey1, EnvKey2, EnvKey3, EnvXorSeed);

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Core encrypt3n — matches PHP's encrypt3n() exactly
    // ─────────────────────────────────────────────────────────────────────────

    private static void Encrypt3n(
        byte[] data, int offset, int length,
        byte[] key1, byte[] key2, byte[] key3, byte[] xorSeed)
    {
        // Each DES block is 8 bytes; process in 8-byte chunks.
        // The running XOR-seed starts as the supplied xorSeed and is updated
        // to the ciphertext output of each block (CBC-like feedback on output).
        byte[] seed = (byte[])xorSeed.Clone();

        for (int i = 0; i < length; i += 8)
        {
            // 1. XOR with current seed
            var block = new byte[8];
            for (int t = 0; t < 8; t++)
                block[t] = (byte)(data[offset + i + t] ^ seed[t]);

            // 2. Triple-DES: E(key1) → D(key2) → E(key3)
            block = DesEcbEncrypt(block, key1);
            block = DesEcbDecrypt(block, key2);
            block = DesEcbEncrypt(block, key3);

            // 3. Write back and update seed
            for (int t = 0; t < 8; t++)
                data[offset + i + t] = block[t];

            seed = block;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DES-ECB helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static byte[] DesEcbEncrypt(byte[] block, byte[] key)
    {
        using var des = DES.Create();
        des.Mode    = CipherMode.ECB;
        des.Padding = PaddingMode.None;
        des.Key     = key;

        using var enc = des.CreateEncryptor();
        return enc.TransformFinalBlock(block, 0, block.Length);
    }

    private static byte[] DesEcbDecrypt(byte[] block, byte[] key)
    {
        using var des = DES.Create();
        des.Mode    = CipherMode.ECB;
        des.Padding = PaddingMode.None;
        des.Key     = key;

        using var dec = des.CreateDecryptor();
        return dec.TransformFinalBlock(block, 0, block.Length);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Utility
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>SHA1 of a slice of <paramref name="data"/>, returned as a lowercase hex string.</summary>
    private static string SHA1Hex(byte[] data, int offset, int length)
    {
        var hash = SHA1.HashData(data.AsSpan(offset, length));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static byte[] FromHex(string hex)
    {
        if (hex.Length % 2 != 0)
            throw new ArgumentException("Hex string length must be even.", nameof(hex));
        var result = new byte[hex.Length / 2];
        for (int i = 0; i < result.Length; i++)
            result[i] = byte.Parse(hex.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber);
        return result;
    }
}
