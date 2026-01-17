using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Mips64LE.Cpu.InstrArgs
{
    public readonly struct JArgs
    {
        public uint Addr { get; }

        public JArgs(uint addr)
        {
            Addr = addr;
        }
    }
}
