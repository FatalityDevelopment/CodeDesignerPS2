using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeDesigner.Mips64LE.Cpu.Instructions.EE
{
    public class SystemCallException : Exception
    {
        public SystemCallException(string message) : base(message) { }
    }

    public class BreakpointException : Exception
    {
        public BreakpointException(string message) : base(message) { }
    }

    public class TrapException : Exception
    {
        public TrapException(string message) : base(message) { }
    }
}
