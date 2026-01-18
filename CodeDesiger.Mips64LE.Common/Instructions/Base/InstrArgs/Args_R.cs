using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Mips64LE.Cpu.InstrArgs
{
    public readonly struct Args_R
    {
        public int Rs { get; }
        public int Rt { get; }
        public int Rd { get; }
        public int Shamt { get; }

        public Args_R(int rs, int rt, int rd, int shamt)
        {
            Rs = rs;
            Rt = rt;
            Rd = rd;
            Shamt = shamt;
        }
    }
}
