using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Mips64LE.Cpu.InstrArgs
{
    public readonly struct IArgs
    {
        public int Rs { get; }
        public int Rt { get; }
        public int Imm { get; }

        public IArgs(int rs, int rt, int imm)
        {
            Rs = rs;
            Rt = rt;
            Imm = imm;
        }
    }
}
