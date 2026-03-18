using CodeDesigner.Ps2.CheatEngine.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeDesigner.Ps2.CheatEngine
{
    /// <summary>
    /// Applies PS2 GameShark/Action Replay-style cheat codes against an
    /// <see cref="IByteEditor"/> that abstracts the underlying memory backend
    /// (e.g. the live PS2 emulator RAM via <see cref="PS2ByteEditor"/>).
    /// </summary>
    public class CheatEngine
    {
        private CheatList? _cheatList;
        private readonly IByteEditor _byteEditor;
        private readonly int _memoryOffset;
        private readonly List<CheatTimer> _cheatTimers = new List<CheatTimer>();

        public CheatEngine(IByteEditor byteEditor, int memoryOffset = 0x20000000)
        {
            _byteEditor   = byteEditor;
            _memoryOffset = memoryOffset;
        }

        public CheatEngine(IByteEditor byteEditor, CheatList cheatList, int memoryOffset = 0x20000000)
        {
            _byteEditor   = byteEditor;
            _cheatList    = cheatList;
            _memoryOffset = memoryOffset;
        }

        public void PatchMemory(CheatList? cheatList = null)
        {
            _cheatList = _cheatList ?? cheatList;
            if (_cheatList == null) return;

            foreach (var cheatBlock in _cheatList.Parse())
            {
                PatchMemory(cheatBlock);
            }
        }

        public void PatchMemory(CheatBlock cheatBlock)
        {
            for (int i = 0; i < cheatBlock.Cheats.Count;)
            {
                i += ApplyCheat(cheatBlock, cheatBlock.Cheats[i]);
            }
        }

        public int ApplyCheat(CheatBlock cheatBlock, Cheat cheat)
        {
            switch (cheat.CheatType)
            {
                case CheatType._8BitWrite:       return _8BitWrite(cheat);
                case CheatType._16BitWrite:      return _16BitWrite(cheat);
                case CheatType._32BitWrite:      return _32BitWrite(cheat);
                case CheatType._copyBytes:       return CopyBytes(cheatBlock, cheat);
                case CheatType._pointerWrite:    return PointerWrite(cheatBlock, cheat);
                case CheatType._timer:           return Timer(cheatBlock, cheat);
                case CheatType._32BitCondition:  return _32BitCondition(cheatBlock, cheat);
                case CheatType._16BitCondition:  return _16BitCondition(cheatBlock, cheat);
                default: return 1;
            }
        }

        private int _8BitWrite(Cheat cheat)
        {
            _byteEditor.Write(cheat.Address + _memoryOffset, cheat.Data.Take(1).ToArray());
            return 1;
        }

        private int _16BitWrite(Cheat cheat)
        {
            _byteEditor.Write(cheat.Address + _memoryOffset, cheat.Data.Take(2).ToArray());
            return 1;
        }

        private int _32BitWrite(Cheat cheat)
        {
            _byteEditor.Write(cheat.Address + _memoryOffset, cheat.Data);
            return 1;
        }

        private int CopyBytes(CheatBlock cheatBlock, Cheat cheat)
        {
            int copyLength = BitConverter.ToInt32(cheat.Data, 0);
            byte[] bytesToCopy = _byteEditor.Read(cheat.Address + _memoryOffset, copyLength);
            _byteEditor.Write(cheatBlock.Cheats[cheat.Position + 1].Address + _memoryOffset, bytesToCopy);
            return 2;
        }

        private int PointerWrite(CheatBlock cheatBlock, Cheat cheat)
        {
            // Read the pointer, then write to the dereferenced address
            byte[] ptrBytes = _byteEditor.Read(cheat.Address + _memoryOffset, 4);
            // Pointer dereference not yet implemented; advance past this cheat
            return 1;
        }

        private int Timer(CheatBlock cheatBlock, Cheat cheat)
        {
            var cheatTimer = _cheatTimers.SingleOrDefault(c => c.Cheat == cheat);

            if (cheatTimer == null)
            {
                var timerIntervalType = (CheatTimerIntervalType)cheat.Data[0];
                // Pad the 3-byte interval field to 4 bytes for BitConverter
                var intervalBytes = new byte[4];
                Array.Copy(cheat.Data, 1, intervalBytes, 0, Math.Min(3, cheat.Data.Length - 1));
                int timerInterval = BitConverter.ToInt32(intervalBytes, 0);
                cheatTimer = new CheatTimer(cheat, timerIntervalType, timerInterval);
                _cheatTimers.Add(cheatTimer);
            }

            if (cheatTimer.IsIntervalCriteriaMet())
                return 1;

            return cheatBlock.Cheats.Count;
        }

        private int _16BitCondition(CheatBlock cheatBlock, Cheat cheat)
        {
            var data = _byteEditor.Read(cheat.Address + _memoryOffset, 2);
            bool met = cheat.Data.Take(2).SequenceEqual(data);
            return met ? 1 : cheatBlock.Cheats.Count;
        }

        private int _32BitCondition(CheatBlock cheatBlock, Cheat cheat)
        {
            var data = _byteEditor.Read(cheat.Address + _memoryOffset, 4);
            bool met = cheat.Data.SequenceEqual(data);
            return met ? 1 : cheatBlock.Cheats.Count;
        }
    }
}
