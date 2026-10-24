using System;
using System.Collections.Generic;
using CodeDesigner.Ps2.Common.Interfaces;

namespace CodeDesigner.Ps2.Common
{
    /// <summary>
    /// Memory bus for the static analyzer: reads fall through to an optional
    /// backing bus, writes land in a private byte overlay. Simulated stores can
    /// never reach real (or emulated) memory, and a later simulated load from
    /// the same address sees the stored value (e.g. a spill and reload via $sp).
    /// </summary>
    internal sealed class AnalysisOverlayBus : IPs2MemoryBus
    {
        private readonly IPs2MemoryBus? _backing;
        private readonly Dictionary<uint, byte> _overlay = new();

        /// <param name="primary">Memory read first (page bytes / local mirror).</param>
        /// <param name="fallback">Read when <paramref name="primary"/> can't serve an address (e.g. the live client).</param>
        public AnalysisOverlayBus(IPs2MemoryBus? primary, IPs2MemoryBus? fallback = null)
        {
            _backing = primary == null ? fallback
                : fallback == null || ReferenceEquals(primary, fallback) ? primary
                : new FallbackBus(primary, fallback);
        }

        /// <summary>Reads <c>primary</c>, and <c>fallback</c> for any address <c>primary</c> throws on.</summary>
        private sealed class FallbackBus(IPs2MemoryBus primary, IPs2MemoryBus fallback) : IPs2MemoryBus
        {
            public byte[] Ram => primary.Ram;
            public byte[] Bios => primary.Bios;
            public byte[] Scratchpad => primary.Scratchpad;

            private static T Try<T>(Func<T> first, Func<T> second)
            {
                try { return first(); }
                catch { return second(); }
            }

            public byte ReadByte(uint a)         => Try(() => primary.ReadByte(a), () => fallback.ReadByte(a));
            public ushort ReadHalf(uint a)       => Try(() => primary.ReadHalf(a), () => fallback.ReadHalf(a));
            public uint ReadWord(uint a)         => Try(() => primary.ReadWord(a), () => fallback.ReadWord(a));
            public ulong ReadDoubleWord(uint a)  => Try(() => primary.ReadDoubleWord(a), () => fallback.ReadDoubleWord(a));
            public void ReadQuadWord(uint a, out ulong lo, out ulong hi)
            {
                try { primary.ReadQuadWord(a, out lo, out hi); }
                catch { fallback.ReadQuadWord(a, out lo, out hi); }
            }
            public void WriteByte(uint a, byte v) => throw new NotSupportedException();
            public void WriteHalf(uint a, ushort v) => throw new NotSupportedException();
            public void WriteWord(uint a, uint v) => throw new NotSupportedException();
            public void WriteDoubleWord(uint a, ulong v) => throw new NotSupportedException();
            public void WriteQuadWord(uint a, ulong lo, ulong hi) => throw new NotSupportedException();
        }

        public byte[] Ram        => _backing?.Ram        ?? Array.Empty<byte>();
        public byte[] Bios       => _backing?.Bios       ?? Array.Empty<byte>();
        public byte[] Scratchpad => _backing?.Scratchpad ?? Array.Empty<byte>();

        public byte ReadByte(uint vaddr)
        {
            if (_overlay.TryGetValue(vaddr, out byte b)) return b;
            if (_backing is null) throw new InvalidOperationException("No backing bus for analysis read");
            return _backing.ReadByte(vaddr);
        }

        private ulong ReadLe(uint vaddr, int size)
        {
            bool overlaid = false;
            for (int i = 0; i < size && !overlaid; i++) overlaid = _overlay.ContainsKey(vaddr + (uint)i);
            if (!overlaid && _backing is not null)
            {
                return size switch
                {
                    2 => _backing.ReadHalf(vaddr),
                    4 => _backing.ReadWord(vaddr),
                    _ => _backing.ReadDoubleWord(vaddr),
                };
            }
            ulong value = 0;
            for (int i = 0; i < size; i++) value |= (ulong)ReadByte(vaddr + (uint)i) << (8 * i);
            return value;
        }

        private void WriteLe(uint vaddr, ulong value, int size)
        {
            for (int i = 0; i < size; i++) _overlay[vaddr + (uint)i] = (byte)(value >> (8 * i));
        }

        public ushort ReadHalf(uint vaddr)       => (ushort)ReadLe(vaddr, 2);
        public uint   ReadWord(uint vaddr)       => (uint)ReadLe(vaddr, 4);
        public ulong  ReadDoubleWord(uint vaddr) => ReadLe(vaddr, 8);

        public void ReadQuadWord(uint vaddr, out ulong lo, out ulong hi)
        {
            lo = ReadLe(vaddr, 8);
            hi = ReadLe(vaddr + 8, 8);
        }

        public void WriteByte(uint vaddr, byte value)        => _overlay[vaddr] = value;
        public void WriteHalf(uint vaddr, ushort value)      => WriteLe(vaddr, value, 2);
        public void WriteWord(uint vaddr, uint value)        => WriteLe(vaddr, value, 4);
        public void WriteDoubleWord(uint vaddr, ulong value) => WriteLe(vaddr, value, 8);

        public void WriteQuadWord(uint vaddr, ulong lo, ulong hi)
        {
            WriteLe(vaddr, lo, 8);
            WriteLe(vaddr + 8, hi, 8);
        }
    }
}
