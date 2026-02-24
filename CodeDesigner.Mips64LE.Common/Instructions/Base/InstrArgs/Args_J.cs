using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Mips64LE.Common.Cpu.InstrArgs
{
    public readonly struct Args_J
    {
        public uint Addr { get; }

        public Args_J(uint addr)
        {
            Addr = addr;
        }
    }
}
