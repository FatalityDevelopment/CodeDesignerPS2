using System;
using CodeDesigner.Ps2.CheatEngine;
using CodeDesigner.Ps2.Common.Interfaces;

namespace CodeDesigner.Ps2.Bridge
{
    /// <summary>
    /// Adapts an <see cref="IByteEditor"/> (PINE or built-in emulator) to the
    /// <see cref="IPs2MemoryBus"/> interface expected by <see cref="CodeDesigner.Ps2.Common.MipsAnalyzer"/>.
    /// <para>
    /// Only the read operations required by the analyzer are implemented.
    /// Write methods and raw array properties are not supported and will throw
    /// <see cref="NotSupportedException"/> if called.
    /// </para>
    /// </summary>
    internal sealed class ByteEditorMemoryBusAdapter : IPs2MemoryBus
    {
        private readonly IByteEditor _editor;

        public ByteEditorMemoryBusAdapter(IByteEditor editor)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        }

        // ── IPs2MemoryBus — raw arrays (not supported in adapter) ──────────────

        public byte[] Ram        => throw new NotSupportedException("Raw RAM array not available via IByteEditor adapter.");
        public byte[] Bios       => throw new NotSupportedException("Raw BIOS array not available via IByteEditor adapter.");
        public byte[] Scratchpad => throw new NotSupportedException("Raw Scratchpad array not available via IByteEditor adapter.");

        // ── IPs2MemoryBus — reads ──────────────────────────────────────────────

        public byte ReadByte(uint vaddr)
        {
            // Cast to int with unchecked semantics: IByteEditor.Read accepts a signed int address,
            // but both PS2ByteEditor and PinePcsx2ByteEditor cast it back to uint internally, so
            // the bit pattern is preserved correctly for addresses >= 0x80000000.
            byte[] data = _editor.Read(unchecked((int)vaddr), 1);
            return data.Length > 0 ? data[0] : (byte)0;
        }

        public ushort ReadHalf(uint vaddr)
        {
            byte[] data = _editor.Read(unchecked((int)vaddr), 2);
            return data.Length >= 2 ? BitConverter.ToUInt16(data, 0) : (ushort)0;
        }

        public uint ReadWord(uint vaddr)
        {
            byte[] data = _editor.Read(unchecked((int)vaddr), 4);
            return data.Length >= 4 ? BitConverter.ToUInt32(data, 0) : 0u;
        }

        public ulong ReadDoubleWord(uint vaddr)
        {
            byte[] data = _editor.Read(unchecked((int)vaddr), 8);
            return data.Length >= 8 ? BitConverter.ToUInt64(data, 0) : 0UL;
        }

        public void ReadQuadWord(uint vaddr, out ulong lo, out ulong hi)
        {
            byte[] data = _editor.Read(unchecked((int)vaddr), 16);
            lo = data.Length >= 8  ? BitConverter.ToUInt64(data, 0) : 0UL;
            hi = data.Length >= 16 ? BitConverter.ToUInt64(data, 8) : 0UL;
        }

        // ── IPs2MemoryBus — writes (not supported) ────────────────────────────

        public void WriteByte(uint vaddr, byte value)
            => throw new NotSupportedException("Write operations are not supported on the read-only analyzer bus adapter.");

        public void WriteHalf(uint vaddr, ushort value)
            => throw new NotSupportedException("Write operations are not supported on the read-only analyzer bus adapter.");

        public void WriteWord(uint vaddr, uint value)
            => throw new NotSupportedException("Write operations are not supported on the read-only analyzer bus adapter.");

        public void WriteDoubleWord(uint vaddr, ulong value)
            => throw new NotSupportedException("Write operations are not supported on the read-only analyzer bus adapter.");

        public void WriteQuadWord(uint vaddr, ulong lo, ulong hi)
            => throw new NotSupportedException("Write operations are not supported on the read-only analyzer bus adapter.");
    }
}
