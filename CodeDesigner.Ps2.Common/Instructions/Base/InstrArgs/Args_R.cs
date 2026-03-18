using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Ps2.Common.Cpu.InstrArgs
{
    public readonly struct Args_R
    {
        public int Rs { get; }
        public int Rt { get; }
        public int Rd { get; }
        public int Shamt { get; }
        /// <summary>Bits [5:0] of the instruction word (funct / sel field).</summary>
        public int Funct { get; }

        public Args_R(int rs, int rt, int rd, int shamt, int funct = 0)
        {
            Rs = rs;
            Rt = rt;
            Rd = rd;
            Shamt = shamt;
            Funct = funct;
        }
    }
}
