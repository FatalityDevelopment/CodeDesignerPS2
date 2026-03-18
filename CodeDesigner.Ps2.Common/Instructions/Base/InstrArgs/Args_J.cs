using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Ps2.Common.Cpu.InstrArgs
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
