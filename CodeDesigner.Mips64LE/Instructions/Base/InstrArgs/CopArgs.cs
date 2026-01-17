using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Mips64LE.Cpu.InstrArgs
{
    public readonly struct CopArgs
    {
        public int Rt { get; }
        public int Rd { get; }

        public CopArgs(int rt, int rd)
        {
            Rt = rt;
            Rd = rd;
        }
    }
}
