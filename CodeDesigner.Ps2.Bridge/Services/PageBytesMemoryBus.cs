using System;
using CodeDesigner.Ps2.Common.Interfaces;

namespace CodeDesigner.Ps2.Bridge.Services
{
    /// <summary>
    /// Read-only <see cref="IPs2MemoryBus"/> that exposes a single disassembly
    /// page buffer plus an optional fallback bus. Reads inside the page
    /// <c>[baseAddr, baseAddr + buf.Length)</c> resolve from the in-memory
    /// buffer; reads outside that window delegate to <c>_fallback</c>.
    ///
    /// <para>
    /// Used by <see cref="AssemblerService"/> so the analyzer can resolve
    /// strings/pointers that live inside the currently disassembled page
    /// even when no mirror bus is attached (e.g. PCSX2 PINE mode without
    /// the in-process analysis emulator). This keeps comment-column string
    /// resolution working without re-introducing arbitrary live PINE peeks.
    /// </para>
    /// </summary>
    internal sealed class PageBytesMemoryBus : IPs2MemoryBus
    {
        private readonly byte[] _buf;
        private readonly uint _baseAddr;
        private readonly uint _endAddrExclusive;
        private readonly IPs2MemoryBus? _fallback;

        public PageBytesMemoryBus(byte[] buf, uint baseAddr, IPs2MemoryBus? fallback)
        {
            _buf = buf;
            _baseAddr = baseAddr;
            _endAddrExclusive = unchecked(baseAddr + (uint)buf.Length);
            _fallback = fallback;
        }

        public byte[] Ram        => _fallback?.Ram        ?? Array.Empty<byte>();
        public byte[] Bios       => _fallback?.Bios       ?? Array.Empty<byte>();
        public byte[] Scratchpad => _fallback?.Scratchpad ?? Array.Empty<byte>();

        private bool InPage(uint vaddr, int size) =>
            vaddr >= _baseAddr && (vaddr + (uint)size) <= _endAddrExclusive;

        public byte ReadByte(uint vaddr)
        {
            if (InPage(vaddr, 1))
                return _buf[vaddr - _baseAddr];
            if (_fallback is not null) return _fallback.ReadByte(vaddr);
            throw new InvalidOperationException("Address outside page and no fallback bus");
        }

        public ushort ReadHalf(uint vaddr)
        {
            if (InPage(vaddr, 2))
                return BitConverter.ToUInt16(_buf, (int)(vaddr - _baseAddr));
            if (_fallback is not null) return _fallback.ReadHalf(vaddr);
            throw new InvalidOperationException("Address outside page and no fallback bus");
        }

        public uint ReadWord(uint vaddr)
        {
            if (InPage(vaddr, 4))
                return BitConverter.ToUInt32(_buf, (int)(vaddr - _baseAddr));
            if (_fallback is not null) return _fallback.ReadWord(vaddr);
            throw new InvalidOperationException("Address outside page and no fallback bus");
        }

        public ulong ReadDoubleWord(uint vaddr)
        {
            if (InPage(vaddr, 8))
                return BitConverter.ToUInt64(_buf, (int)(vaddr - _baseAddr));
            if (_fallback is not null) return _fallback.ReadDoubleWord(vaddr);
            throw new InvalidOperationException("Address outside page and no fallback bus");
        }

        public void ReadQuadWord(uint vaddr, out ulong lo, out ulong hi)
        {
            if (InPage(vaddr, 16))
            {
                int off = (int)(vaddr - _baseAddr);
                lo = BitConverter.ToUInt64(_buf, off);
                hi = BitConverter.ToUInt64(_buf, off + 8);
                return;
            }
            if (_fallback is not null) { _fallback.ReadQuadWord(vaddr, out lo, out hi); return; }
            throw new InvalidOperationException("Address outside page and no fallback bus");
        }

        // Writes are not supported — this bus is read-only.
        public void WriteByte(uint vaddr, byte value)        => throw new NotSupportedException();
        public void WriteHalf(uint vaddr, ushort value)      => throw new NotSupportedException();
        public void WriteWord(uint vaddr, uint value)        => throw new NotSupportedException();
        public void WriteDoubleWord(uint vaddr, ulong value) => throw new NotSupportedException();
        public void WriteQuadWord(uint vaddr, ulong lo, ulong hi) => throw new NotSupportedException();
    }
}
