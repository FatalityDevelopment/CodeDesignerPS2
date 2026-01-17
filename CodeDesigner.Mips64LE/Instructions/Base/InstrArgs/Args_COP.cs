using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Mips64LE.Cpu.InstrArgs
{
    public readonly struct Args_COP
    {
        public int Rt { get; }
        public int Rd { get; }

        public Args_COP(int rt, int rd)
        {
            Rt = rt;
            Rd = rd;
        }
    }
}
