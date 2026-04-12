using System.Security.Cryptography;

namespace CodeDesigner.Ps2.Multiplayer.Crypto;

/// <summary>
/// PS2 Medius custom RC4 stream cipher — per-packet stateless design.
///
/// Each packet is independently encrypted with a fresh state derived from
/// (workingKey + 4-byte packet hash).  There is no persistent keystream state
/// across packet boundaries.
///
/// Algorithm matches Horizon Private Server's RT.Cryptography/RC/PS2_RC4.cs:
///   - Init table reversed: S[i] = 255 - i
///   - Hash scramble pass (cipherIndex += 5, 256 iterations) before key schedule
///   - Key schedule: cipherIndex += 3 (not sequential i), keyIndex wraps at 64
///   - Encrypt: x += 5, y += S[x], swap, keyByte = S[(S[x]+S[y])&0xFF], y-feedback
///   - Decrypt: y += 5, x += S[y], swap, same keyByte lookup, x-feedback
///   - 4-byte hash = SHA1(plaintext)[0..3] with CipherContext in top 3 bits of byte[3]
/// </summary>
public static class PS2_RC4
{
    private const int StateLen = 256;
    private const int KeyLen   = 64;   // working key is always 64 bytes

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>Generate a cryptographically random 64-byte session key.</summary>
    public static byte[] GenerateKey()
    {
        var key = new byte[KeyLen];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    /// <summary>
    /// Decrypt a packet payload in-place.
    /// <paramref name="hash"/> is the 4 bytes from the SCERT encrypted-packet header.
    /// </summary>
    public static void Decrypt(byte[] workingKey, byte[] hash, Span<byte> data)
    {
        var state = new byte[StateLen];
        int x = 0, y = 0;
        SetKey(state, workingKey, hash);
        DecryptBlock(state, ref x, ref y, data);
    }

    /// <summary>
    /// Encrypt <paramref name="plaintext"/> and return ciphertext + 4-byte hash.
    /// </summary>
    public static void Encrypt(byte[] workingKey, CipherContext context, byte[] plaintext,
        out byte[] cipher, out byte[] hash)
    {
        hash   = ComputeHash(plaintext, context);
        cipher = new byte[plaintext.Length];
        plaintext.CopyTo(cipher, 0);

        var state = new byte[StateLen];
        int x = 0, y = 0;
        SetKey(state, workingKey, hash);
        EncryptBlock(state, ref x, ref y, cipher);
    }

    // -------------------------------------------------------------------------
    // Key initialisation
    // -------------------------------------------------------------------------

    private static void SetKey(byte[] state, byte[] key, byte[] hash)
    {
        // Step 1: reversed init table
        for (int i = 0; i < StateLen; i++)
            state[i] = (byte)(StateLen - 1 - i);

        int li = 0, cipherIndex = 0;

        // Step 2: hash scramble pass (if hash is valid/non-null)
        if (hash != null && hash.Length == 4 && IsHashValid(hash))
        {
            int idIndex = 0;
            do
            {
                int v1 = hash[idIndex];
                idIndex = (idIndex + 1) & 3;

                byte temp = state[cipherIndex];
                v1  += li;
                li   = (temp + v1) & 0xFF;

                state[cipherIndex] = state[li];
                state[li]          = temp;

                cipherIndex = (cipherIndex + 5) & 0xFF;
            } while (cipherIndex != 0);   // 256 iterations (gcd(5,256)=1)

            // Reset for key schedule pass
            li          = 0;
            cipherIndex = 0;
        }

        // Step 3: key schedule (cipherIndex advances +3, visits all 256 positions)
        int keyIndex = 0;
        do
        {
            int keyByte    = key[keyIndex] + li;
            keyIndex       = (keyIndex + 1) & (KeyLen - 1);   // wraps at 64

            byte cipherVal = state[cipherIndex];
            li             = (cipherVal + keyByte) & 0xFF;

            state[cipherIndex] = state[li];
            state[li]          = cipherVal;

            cipherIndex = (cipherIndex + 3) & 0xFF;
        } while (cipherIndex != 0);   // 256 iterations (gcd(3,256)=1)
    }

    // -------------------------------------------------------------------------
    // Per-block encrypt / decrypt
    // -------------------------------------------------------------------------

    private static void EncryptBlock(byte[] s, ref int x, ref int y, Span<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            x = (x + 5) & 0xFF;
            y = (y + s[x]) & 0xFF;

            byte temp = s[x]; s[x] = s[y]; s[y] = temp;   // swap

            byte plain   = data[i];
            data[i]      = (byte)(plain ^ s[(s[x] + s[y]) & 0xFF]);

            y = (s[plain] + y) & 0xFF;   // PS2 feedback on plaintext
        }
    }

    private static void DecryptBlock(byte[] s, ref int x, ref int y, Span<byte> data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            // Note: in decrypt, y and x roles are swapped relative to encrypt
            y = (y + 5) & 0xFF;
            int sv  = s[y];          // S_old[y]
            byte a2 = (byte)sv;
            x       = (x + sv) & 0xFF;

            sv      = s[x];          // S_old[x]
            s[y]    = (byte)sv;
            s[x]    = a2;            // swap complete

            byte cipher = data[i];
            int  idx    = (sv + a2) & 0xFF;   // (S_old[x] + S_old[y]) & 0xFF
            byte plain  = (byte)(cipher ^ s[idx]);
            data[i]     = plain;

            x = (s[plain] + x) & 0xFF;   // PS2 feedback on recovered plain
        }
    }

    // -------------------------------------------------------------------------
    // Hash helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Compute the 4-byte packet hash: SHA1(plaintext)[0..3] with cipher context
    /// encoded in the top 3 bits of byte[3].
    /// </summary>
    public static byte[] ComputeHash(byte[] data, CipherContext context)
    {
        var sha    = SHA1.HashData(data);
        var result = new byte[4];
        Array.Copy(sha, result, 4);
        result[3] = (byte)((result[3] & 0x1F) | (((byte)context & 7) << 5));
        return result;
    }

    /// <summary>A hash of all-zeros (or only context bits) means "plaintext, no decryption".</summary>
    public static bool IsHashValid(byte[] hash) =>
        !(hash[0] == 0 && hash[1] == 0 && hash[2] == 0 && (hash[3] & 0x1F) == 0);
}

/// <summary>Cipher context values encoded in the top 3 bits of the packet hash byte[3].</summary>
public enum CipherContext : byte
{
    RsaAuth         = 0,
    RcClientSession = 1,
}
