namespace CodeDesigner.Ps2.Multiplayer.Crypto;

/// <summary>
/// PS2 Medius custom RC4 stream cipher.
///
/// Differences from standard RC4:
///   - Init table is reversed: engineState[i] = 255 - i  (not 0,1,2...255)
///   - Key schedule treats key as 64-byte max (keyIndex &amp;= 0x3F)
///   - Encrypt: x increments by 5 (not 1) each byte
///   - Encrypt: extra feedback step after XOR: y = (e[plain] + y) &amp; 0xFF
///   - Decrypt is the algebraic inverse of encrypt
/// </summary>
public sealed class PS2_RC4
{
    private const int StateLen = 256;
    private const int KeyLen   = 64;  // key is always 64 bytes

    private readonly byte[] _state = new byte[StateLen];
    private int _x, _y;

    // -------------------------------------------------------------------------
    // Initialise with a 64-byte key
    // -------------------------------------------------------------------------

    public void Init(ReadOnlySpan<byte> key)
    {
        // Step 1: reversed init table
        for (int i = 0; i < StateLen; i++)
            _state[i] = (byte)(StateLen - 1 - i);   // 255, 254, ..., 0

        // Step 2: key schedule — key index wraps at 64
        int j  = 0;
        int ki = 0;
        for (int i = 0; i < StateLen; i++)
        {
            j  = (j + _state[i] + key[ki]) & 0xFF;
            ki = (ki + 1) & (KeyLen - 1);            // & 0x3F

            (_state[i], _state[j]) = (_state[j], _state[i]);
        }

        _x = 0;
        _y = 0;
    }

    // -------------------------------------------------------------------------
    // Encrypt data in-place
    // -------------------------------------------------------------------------

    public void Encrypt(Span<byte> data)
    {
        var e = _state;
        int x = _x, y = _y;

        for (int i = 0; i < data.Length; i++)
        {
            x = (x + 5) & 0xFF;            // +5 per byte (not +1)
            y = (y + e[x]) & 0xFF;
            (e[x], e[y]) = (e[y], e[x]);

            byte plain   = data[i];
            byte keyByte = e[(e[x] + e[y]) & 0xFF];
            byte cipher  = (byte)(plain ^ keyByte);
            data[i] = cipher;

            y = (e[plain] + y) & 0xFF;     // PS2-specific feedback on plaintext
        }

        _x = x;
        _y = y;
    }

    // -------------------------------------------------------------------------
    // Decrypt data in-place (algebraic inverse of Encrypt)
    // -------------------------------------------------------------------------

    public void Decrypt(Span<byte> data)
    {
        var e = _state;
        int x = _x, y = _y;

        for (int i = 0; i < data.Length; i++)
        {
            x = (x + 5) & 0xFF;
            y = (y + e[x]) & 0xFF;
            (e[x], e[y]) = (e[y], e[x]);

            byte keyByte = e[(e[x] + e[y]) & 0xFF];
            byte plain   = (byte)(data[i] ^ keyByte);
            data[i] = plain;

            y = (e[plain] + y) & 0xFF;     // same feedback — uses recovered plain
        }

        _x = x;
        _y = y;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>Generate a cryptographically random 64-byte session key.</summary>
    public static byte[] GenerateKey()
    {
        var key = new byte[KeyLen];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);
        return key;
    }
}
