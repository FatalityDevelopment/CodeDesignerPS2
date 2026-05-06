using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodeDesigner.Ps2.Save
{

    // -------------------------------------------------------------------------
    //  High-level save file
    // -------------------------------------------------------------------------

    /// <summary>
    /// A decrypted SOCOM II memory card save file.
    ///
    /// Quick usage:
    /// <code>
    ///   // Load and inspect
    ///   var save = Socom2Save.Load("SaveGame0");
    ///   Console.WriteLine($"Title: {save.Title}");
    ///   Console.WriteLine($"SlotIndex: {save.Header.SlotIndex}");
    ///
    ///   // Modify and write back
    ///   save.Title = "REVIVAL SAVE";
    ///   save.Save("SaveGame0_modified");
    ///
    ///   // Verify roundtrip
    ///   save.VerifyRoundtrip("SaveGame0");
    /// </code>
    /// </summary>
    public sealed class Socom2Save
    {
        // ── Known offsets (all in decrypted space) ─────────────────────────
        private const int TitleOffset = 0x0FC;   // null-terminated ASCII title
        private const int TitleMaxBytes = 64;       // practical limit before overflow risk

        // 0x200–0x20B: 12-byte string-table header
        //   [0x200] LE32 version/type (= 1)
        //   [0x204] LE32 string data size in bytes (= 0x26F = 623 for SaveGame0)
        //   [0x208] LE32 total block size          (= 0x280 = 640)
        // 0x20C: first null-terminated field name ("state\0database\0...")
        private const int StringTableBase = 0x020C;  // null-separated field name strings (after 12-byte header)
        private const int RecordTableBase = 0x0480;   // 16-byte data records
        private const int PointerTableBase = 0x00A0;   // EE RAM pointer pairs
        private const int ModuleNamesBase = 0x0064;   // null-terminated module name strings

        // ── State ──────────────────────────────────────────────────────────
        private byte[] _decrypted;

        // ── Public properties ──────────────────────────────────────────────

        /// <summary>Parsed save header fields.</summary>
        public Socom2SaveHeader Header { get; private set; } = new();

        /// <summary>
        /// Display title read from offset 0x0FC.
        /// Setting this patches the decrypted buffer in place.
        /// Maximum safe length is 63 printable ASCII characters.
        /// </summary>
        public string Title
        {
            get => ReadCStr(_decrypted, TitleOffset);
            set => PatchTitle(value);
        }

        /// <summary>Raw decrypted bytes (read-only view for inspection).</summary>
        public ReadOnlySpan<byte> Decrypted => _decrypted;

        /// <summary>Decrypted file size in bytes.</summary>
        public int Size => _decrypted.Length;

        // ── Factory methods ────────────────────────────────────────────────

        /// <summary>Load a save file from disk, decrypt it, and parse headers.</summary>
        public static Socom2Save Load(string filePath)
        {
            byte[] raw = File.ReadAllBytes(filePath);
            return FromBytes(raw);
        }

        /// <summary>Create a Socom2Save from raw (encrypted) bytes.</summary>
        public static Socom2Save FromBytes(byte[] encryptedBytes)
        {
            byte[] dec = Socom2SaveCipher.Decrypt(encryptedBytes);
            var save = new Socom2Save { _decrypted = dec };
            save.Header = Socom2SaveHeader.Parse(dec);
            return save;
        }

        /// <summary>
        /// Create a Socom2Save from already-decrypted bytes
        /// (e.g. from a prior call to <see cref="Socom2SaveCipher.Decrypt"/>).
        /// </summary>
        public static Socom2Save FromDecryptedBytes(byte[] decryptedBytes)
        {
            var save = new Socom2Save { _decrypted = (byte[])decryptedBytes.Clone() };
            save.Header = Socom2SaveHeader.Parse(save._decrypted);
            return save;
        }

        // ── Serialize ─────────────────────────────────────────────────────

        /// <summary>Encrypt and write the (possibly modified) save file to disk.</summary>
        public void Save(string filePath)
        {
            byte[] enc = Socom2SaveCipher.Encrypt(_decrypted);
            File.WriteAllBytes(filePath, enc);
        }

        /// <summary>Encrypt the current state and return the raw bytes.</summary>
        public byte[] ToEncryptedBytes() => Socom2SaveCipher.Encrypt(_decrypted);

        // ── Modification helpers ───────────────────────────────────────────

        /// <summary>
        /// Writes a raw 32-bit LE integer at <paramref name="decryptedOffset"/>
        /// in the decrypted buffer.
        /// </summary>
        public void WriteInt32(int decryptedOffset, int value)
        {
            if (decryptedOffset < 0 || decryptedOffset + 4 > _decrypted.Length)
                throw new ArgumentOutOfRangeException(nameof(decryptedOffset));

            _decrypted[decryptedOffset] = (byte)value;
            _decrypted[decryptedOffset + 1] = (byte)(value >> 8);
            _decrypted[decryptedOffset + 2] = (byte)(value >> 16);
            _decrypted[decryptedOffset + 3] = (byte)(value >> 24);
        }

        /// <summary>Reads a raw 32-bit LE integer from the decrypted buffer.</summary>
        public int ReadInt32(int decryptedOffset)
        {
            if (decryptedOffset < 0 || decryptedOffset + 4 > _decrypted.Length)
                throw new ArgumentOutOfRangeException(nameof(decryptedOffset));

            return _decrypted[decryptedOffset]
                 | (_decrypted[decryptedOffset + 1] << 8)
                 | (_decrypted[decryptedOffset + 2] << 16)
                 | (_decrypted[decryptedOffset + 3] << 24);
        }

        // ── Inspection helpers ─────────────────────────────────────────────

        /// <summary>Returns all data records from the record table at 0x480.</summary>
        public IReadOnlyList<Socom2DataRecord> GetDataRecords()
        {
            var records = new List<Socom2DataRecord>();
            int i = RecordTableBase;

            while (i + Socom2DataRecord.RecordSize <= _decrypted.Length)
            {
                // Sentinel: 0xAF padding bytes mark end of table
                if (_decrypted[i] == 0xAF)
                    break;

                records.Add(Socom2DataRecord.Parse(_decrypted, i, StringTableBase));
                i += Socom2DataRecord.RecordSize;
            }

            return records;
        }

        /// <summary>Returns all module name strings from offset 0x64.</summary>
        public IReadOnlyList<string> GetModuleNames()
        {
            var names = new List<string>();
            int i = ModuleNamesBase;

            while (i < PointerTableBase && i < _decrypted.Length)
            {
                if (_decrypted[i] == 0) { i++; continue; }
                string name = ReadCStr(_decrypted, i);
                if (name.Length > 0) names.Add(name);
                i += name.Length + 1;
            }

            return names;
        }

        /// <summary>Returns a hex dump of a region of the decrypted buffer.</summary>
        public string HexDump(int offset, int length)
        {
            length = Math.Min(length, _decrypted.Length - offset);
            var sb = new StringBuilder();

            for (int row = 0; row < length; row += 16)
            {
                int rowLen = Math.Min(16, length - row);
                sb.Append($"{offset + row:X4}: ");
                for (int j = 0; j < rowLen; j++)
                    sb.Append($"{_decrypted[offset + row + j]:X2} ");
                for (int j = rowLen; j < 16; j++)
                    sb.Append("   ");
                sb.Append(' ');
                for (int j = 0; j < rowLen; j++)
                {
                    byte b = _decrypted[offset + row + j];
                    sb.Append(b >= 32 && b < 127 ? (char)b : '.');
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>Returns a diagnostic summary of the save file.</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Size:         {Size} bytes (0x{Size:X})");
            sb.AppendLine($"Title:        {Title}");
            sb.AppendLine($"SaveType:     0x{Header.SaveType:X8}");
            sb.AppendLine($"SlotIndex:    0x{Header.SlotIndex:X8}  ({Header.SlotIndex})");
            sb.AppendLine($"ModuleCount:  {Header.ModuleCount}");
            sb.AppendLine($"DataSize:     0x{Header.DataSize:X}");
            sb.AppendLine();

            var modules = GetModuleNames();
            if (modules.Count > 0)
            {
                sb.AppendLine("Module names:");
                foreach (string m in modules) sb.AppendLine($"  {m}");
                sb.AppendLine();
            }

            var records = GetDataRecords();
            if (records.Count > 0)
            {
                sb.AppendLine($"Data records ({records.Count}):");
                foreach (var r in records)
                    sb.AppendLine($"  [{r.FieldName,-36}] type=0x{r.Type:X2} " +
                                  $"flags=0x{r.Flags:X2} cnt={r.Count,3} " +
                                  $"val@0x{r.ValOffset:X4} str@0x{r.StrOffset:X4}");
            }

            return sb.ToString();
        }

        // ── Roundtrip verification ─────────────────────────────────────────

        /// <summary>
        /// Encrypt the current state and verify it matches the original file on disk.
        /// Throws <see cref="InvalidDataException"/> on mismatch.
        /// </summary>
        public void VerifyRoundtrip(string originalFilePath)
        {
            byte[] original = File.ReadAllBytes(originalFilePath);
            Socom2SaveCipher.VerifyRoundtrip(original);
        }

        // ── Private helpers ────────────────────────────────────────────────

        private static string ReadCStr(byte[] data, int offset)
        {
            int end = offset;
            while (end < data.Length && data[end] != 0) end++;
            return Encoding.Latin1.GetString(data, offset, end - offset);
        }

        /// <summary>
        /// Patches the title string in the decrypted buffer.
        /// The title lives at 0x0FC; the original 'This game is a dummy game.' is
        /// 27 bytes including its null terminator. If the new title is a different
        /// length the buffer is resized accordingly.
        /// </summary>
        private void PatchTitle(string newTitle)
        {
            if (newTitle == null) throw new ArgumentNullException(nameof(newTitle));

            // Clamp to safe length — beyond 63 chars the game may overflow display buffers
            if (newTitle.Length > TitleMaxBytes - 1)
                newTitle = newTitle[..(TitleMaxBytes - 1)];

            byte[] newBytes = Encoding.Latin1.GetBytes(newTitle + '\0');

            // Find existing title end
            int oldEnd = TitleOffset;
            while (oldEnd < _decrypted.Length && _decrypted[oldEnd] != 0) oldEnd++;
            oldEnd++; // include null terminator

            int delta = newBytes.Length - (oldEnd - TitleOffset);

            if (delta == 0)
            {
                // Same length — patch in place
                Array.Copy(newBytes, 0, _decrypted, TitleOffset, newBytes.Length);
            }
            else
            {
                // Resize: preserve data before title and after old title end
                byte[] resized = new byte[_decrypted.Length + delta];
                Array.Copy(_decrypted, 0, resized, 0, TitleOffset);
                Array.Copy(newBytes, 0, resized, TitleOffset, newBytes.Length);
                Array.Copy(_decrypted, oldEnd, resized, TitleOffset + newBytes.Length,
                           _decrypted.Length - oldEnd);
                _decrypted = resized;
            }
        }
    }
}