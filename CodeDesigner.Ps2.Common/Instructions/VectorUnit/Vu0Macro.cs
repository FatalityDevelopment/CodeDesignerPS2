using CodeDesigner.Ps2.Common.Cpu.InstrArgs;
using CodeDesigner.Ps2.Common.Cpu.Instructions;
using CodeDesigner.Ps2.Common.CPU;
using CodeDesigner.Ps2.Common.VectorUnits.VU0;
using System;
using System.Collections.Generic;
using System.Numerics;

/*
VU0 macro mode (COP2, opcode 0x12, CO bit 25 set).

Layout:  010010 1 dest(4) ft(5) fs(5) fd(5) funct(6)
         dest bits 24..21 = x, y, z, w
         funct 0x00-0x3B -> SPECIAL1 table (64 entries)
         funct 0x3C-0x3F -> SPECIAL2 table indexed (fd << 2) | (funct & 3)

Both tables follow PCSX2's R5900OpcodeTables.cpp
(Int_COP2SPECIAL1PrintTable / Int_COP2SPECIAL2PrintTable).
*/

namespace CodeDesigner.Ps2.Common.Instructions.VectorUnitInstructions
{
    /// <summary>Operand layout of a VU0 macro instruction.</summary>
    public enum Vu0Form
    {
        /// <summary><c>vop.dest fd, fs, ft</c></summary>
        FdFsFt,
        /// <summary><c>vopx.dest fd, fs, ftx</c> (broadcast one ft component)</summary>
        FdFsFtBc,
        /// <summary><c>vopq.dest fd, fs, Q</c></summary>
        FdFsQ,
        /// <summary><c>vopi.dest fd, fs, I</c></summary>
        FdFsI,
        /// <summary><c>vopa.dest ACC, fs, ft</c></summary>
        AccFsFt,
        AccFsFtBc,
        AccFsQ,
        AccFsI,
        /// <summary><c>vop.dest ft, fs</c> (move, abs, conversions)</summary>
        FtFs,
        /// <summary><c>vclipw.xyz fs, ftw</c></summary>
        Clip,
        /// <summary><c>vop vid, vis, vit</c> (integer)</summary>
        IdIsIt,
        /// <summary><c>viaddi vit, vis, imm5</c></summary>
        ItIsImm5,
        /// <summary><c>vcallms imm</c></summary>
        CallImm,
        /// <summary><c>vcallmsr vi27</c></summary>
        CallReg,
        None,
        /// <summary><c>vlqi.dest ft, (vis++)</c></summary>
        LoadInc,
        /// <summary><c>vsqi.dest fs, (vit++)</c></summary>
        StoreInc,
        /// <summary><c>vlqd.dest ft, (--vis)</c></summary>
        LoadDec,
        /// <summary><c>vsqd.dest fs, (--vit)</c></summary>
        StoreDec,
        /// <summary><c>vdiv Q, fsfsf, ftftf</c> / <c>vrsqrt</c></summary>
        QFsFt,
        /// <summary><c>vsqrt Q, ftftf</c></summary>
        QFt,
        /// <summary><c>vmtir vit, fsfsf</c></summary>
        ItFsf,
        /// <summary><c>vmfir.dest ft, vis</c></summary>
        FtIs,
        /// <summary><c>vilwr.dest vit, (vis)</c> / <c>viswr</c></summary>
        ItIsMem,
        /// <summary><c>vrnext.dest ft, R</c> / <c>vrget</c></summary>
        FtR,
        /// <summary><c>vrinit R, fsfsf</c> / <c>vrxor</c></summary>
        RFsf,
    }

    /// <summary>Fields of a VU0 macro instruction word.</summary>
    public readonly struct Vu0Fields
    {
        public int Ft { get; init; }
        public int Fs { get; init; }
        public int Fd { get; init; }
        /// <summary>xyzw write mask, bit 0 = x … bit 3 = w.</summary>
        public byte Dest { get; init; }
        /// <summary>fsf (bits 22:21): component selector for fs.</summary>
        public int Fsf { get; init; }
        /// <summary>ftf (bits 24:23): component selector for ft.</summary>
        public int Ftf { get; init; }
        /// <summary>Broadcast component (funct bits 1:0).</summary>
        public int Bc { get; init; }

        public static Vu0Fields From(Args_R a) => new()
        {
            Ft    = a.Rt,
            Fs    = a.Rd,
            Fd    = a.Shamt,
            Dest  = (byte)(((a.Rs >> 3) & 1) | ((a.Rs >> 2) & 1) << 1 | ((a.Rs >> 1) & 1) << 2 | (a.Rs & 1) << 3),
            Fsf   = a.Rs & 3,
            Ftf   = (a.Rs >> 2) & 3,
            Bc    = a.Funct & 3,
        };
    }

    /// <summary>
    /// One VU0 macro-mode mnemonic. Decoding, operand formatting and assembly are
    /// driven by <see cref="Form"/>; execution by a small semantic delegate.
    /// </summary>
    public sealed class Vu0MacroOp : Instruction<Args_R>
    {
        private static readonly char[] Comp = ['x', 'y', 'z', 'w'];

        /// <summary>SPECIAL1 funct, or 0x3C + (index &amp; 3) with <see cref="Special2Index"/> for SPECIAL2.</summary>
        public int? Special2Index { get; init; }
        public Vu0Form Form { get; init; }
        /// <summary>True when the dest mask is part of the mnemonic (<c>vadd.xyzw</c>).</summary>
        public bool HasDest { get; init; }
        /// <summary>Fixed dest mask for ops that only exist in one form (<c>vopmula.xyz</c>, <c>vclipw.xyz</c>).</summary>
        public byte? FixedDest { get; init; }
        public Action<CpuState, Vu0Fields>? Semantics { get; init; }

        public override void Exec(CpuState cpu, Args_R args)
        {
            if (Form == Vu0Form.CallImm)
            {
                cpu.VU0.Start((ushort)CallTarget(args));
                VU0Executor.RunProgram(cpu.VU0);
                return;
            }
            // vcallmsr starts at CMSAR0, which this VU0 model doesn't keep.
            Semantics?.Invoke(cpu, Vu0Fields.From(args));
        }

        public override uint Asm(int[] args) =>
            throw new NotSupportedException("Assemble VU0 macro instructions through MipsAssembler");

        public override string Disasm(Args_R args, uint? pc = null)
        {
            var f = Vu0Fields.From(args);
            string mnemonic = HasDest ? Name + DestSuffix(f.Dest) : Name;
            string ops = Operands(f, args);
            return ops.Length == 0 ? mnemonic : $"{mnemonic} {ops}";
        }

        public override string Analyze(Args_R args, CpuState? cpu = null, uint? pc = null)
        {
            var f = Vu0Fields.From(args);
            string d = HasDest ? DestSuffix(f.Dest) : string.Empty;
            string fd = $"vf{f.Fd}{d}", ft = $"vf{f.Ft}{d}";
            string fs = $"vf{f.Fs}", ftb = $"vf{f.Ft}";
            return Form switch
            {
                Vu0Form.FdFsFt    => $"{fd} = {Expr(fs, ftb)}",
                Vu0Form.FdFsFtBc  => $"{fd} = {Expr(fs, $"{ftb}.{Comp[f.Bc]}")}",
                Vu0Form.FdFsQ     => $"{fd} = {Expr(fs, "Q")}",
                Vu0Form.FdFsI     => $"{fd} = {Expr(fs, "I")}",
                Vu0Form.AccFsFt   => $"ACC{d} = {Expr(fs, ftb)}",
                Vu0Form.AccFsFtBc => $"ACC{d} = {Expr(fs, $"{ftb}.{Comp[f.Bc]}")}",
                Vu0Form.AccFsQ    => $"ACC{d} = {Expr(fs, "Q")}",
                Vu0Form.AccFsI    => $"ACC{d} = {Expr(fs, "I")}",
                Vu0Form.FtFs      => $"{ft} = {Name}({fs})",
                Vu0Form.CallImm   => $"VU0 microprogram at 0x{CallTarget(args):X4}",
                Vu0Form.CallReg   => "VU0 microprogram at CMSAR0",
                Vu0Form.QFsFt     => $"Q = {Expr($"{fs}.{Comp[f.Fsf]}", $"{ftb}.{Comp[f.Ftf]}")}",
                Vu0Form.QFt       => $"Q = sqrt({ftb}.{Comp[f.Ftf]})",
                _                 => string.Empty,
            };
        }

        /// <summary>Equation body from the op's description; "a op b" for the common arithmetic ops.</summary>
        private string Expr(string a, string b)
        {
            string op = Name.StartsWith("vmadd") ? "madd"
                : Name.StartsWith("vmsub") ? "msub"
                : Name.StartsWith("vadd") ? "+"
                : Name.StartsWith("vsub") ? "-"
                : Name.StartsWith("vmul") ? "*"
                : Name.StartsWith("vdiv") ? "/"
                : Name.StartsWith("vmax") ? "max"
                : Name.StartsWith("vmini") ? "min"
                : Name.StartsWith("vrsqrt") ? "rsqrt"
                : Name;
            return op switch
            {
                "+" or "-" or "*" or "/" => $"{a} {op} {b}",
                "madd" => $"ACC + {a} * {b}",
                "msub" => $"ACC - {a} * {b}",
                "rsqrt" => $"{a} / sqrt({b})",
                _ => $"{op}({a}, {b})",
            };
        }

        internal static uint CallTarget(Args_R a) => (uint)((a.Rt << 10) | (a.Rd << 5) | a.Shamt) * 8;

        private string Operands(Vu0Fields f, Args_R a)
        {
            string Vf(int r) => $"vf{r}";
            string Vi(int r) => $"vi{r}";
            return Form switch
            {
                Vu0Form.FdFsFt    => $"{Vf(f.Fd)}, {Vf(f.Fs)}, {Vf(f.Ft)}",
                Vu0Form.FdFsFtBc  => $"{Vf(f.Fd)}, {Vf(f.Fs)}, {Vf(f.Ft)}{Comp[f.Bc]}",
                Vu0Form.FdFsQ     => $"{Vf(f.Fd)}, {Vf(f.Fs)}, Q",
                Vu0Form.FdFsI     => $"{Vf(f.Fd)}, {Vf(f.Fs)}, I",
                Vu0Form.AccFsFt   => $"ACC, {Vf(f.Fs)}, {Vf(f.Ft)}",
                Vu0Form.AccFsFtBc => $"ACC, {Vf(f.Fs)}, {Vf(f.Ft)}{Comp[f.Bc]}",
                Vu0Form.AccFsQ    => $"ACC, {Vf(f.Fs)}, Q",
                Vu0Form.AccFsI    => $"ACC, {Vf(f.Fs)}, I",
                Vu0Form.FtFs      => $"{Vf(f.Ft)}, {Vf(f.Fs)}",
                Vu0Form.Clip      => $"{Vf(f.Fs)}, {Vf(f.Ft)}w",
                Vu0Form.IdIsIt    => $"{Vi(f.Fd)}, {Vi(f.Fs)}, {Vi(f.Ft)}",
                Vu0Form.ItIsImm5  => $"{Vi(f.Ft)}, {Vi(f.Fs)}, {Imm5(f.Fd)}",
                Vu0Form.CallImm   => $"0x{CallTarget(a):X}",
                Vu0Form.CallReg   => "vi27",
                Vu0Form.LoadInc   => $"{Vf(f.Ft)}, ({Vi(f.Fs)}++)",
                Vu0Form.StoreInc  => $"{Vf(f.Fs)}, ({Vi(f.Ft)}++)",
                Vu0Form.LoadDec   => $"{Vf(f.Ft)}, (--{Vi(f.Fs)})",
                Vu0Form.StoreDec  => $"{Vf(f.Fs)}, (--{Vi(f.Ft)})",
                Vu0Form.QFsFt     => $"Q, {Vf(f.Fs)}{Comp[f.Fsf]}, {Vf(f.Ft)}{Comp[f.Ftf]}",
                Vu0Form.QFt       => $"Q, {Vf(f.Ft)}{Comp[f.Ftf]}",
                Vu0Form.ItFsf     => $"{Vi(f.Ft)}, {Vf(f.Fs)}{Comp[f.Fsf]}",
                Vu0Form.FtIs      => $"{Vf(f.Ft)}, {Vi(f.Fs)}",
                Vu0Form.ItIsMem   => $"{Vi(f.Ft)}, ({Vi(f.Fs)})",
                Vu0Form.FtR       => $"{Vf(f.Ft)}, R",
                Vu0Form.RFsf      => $"R, {Vf(f.Fs)}{Comp[f.Fsf]}",
                _                 => string.Empty,
            };
        }

        /// <summary>Signed 5-bit immediate of <c>viaddi</c>.</summary>
        internal static int Imm5(int field) => (field & 0x10) != 0 ? field - 0x20 : field;

        internal static string DestSuffix(byte dest)
        {
            if (dest == 0) return string.Empty;
            var chars = new char[5];
            int n = 0;
            chars[n++] = '.';
            for (int i = 0; i < 4; i++)
                if ((dest & (1 << i)) != 0) chars[n++] = Comp[i];
            return new string(chars, 0, n);
        }
    }

    /// <summary>
    /// VU0 macro-mode decode tables and assembler support.
    /// </summary>
    public static class Vu0Macro
    {
        private static readonly Vu0MacroOp?[] Special1 = new Vu0MacroOp?[64];
        private static readonly Vu0MacroOp?[] Special2 = new Vu0MacroOp?[128];
        private static readonly Dictionary<string, Vu0MacroOp> ByName = new(StringComparer.OrdinalIgnoreCase);
        private static readonly char[] Comp = ['x', 'y', 'z', 'w'];

        static Vu0Macro()
        {
            // ── SPECIAL1 ────────────────────────────────────────────────────
            string[] bcOps1 = ["vadd", "vsub", "vmadd", "vmsub", "vmax", "vmini", "vmul"];
            for (int op = 0; op < bcOps1.Length; op++)
                for (int bc = 0; bc < 4; bc++)
                    S1(op * 4 + bc, bcOps1[op] + Comp[bc], Vu0Form.FdFsFtBc, true, ArithBc(bcOps1[op]));

            S1(0x1C, "vmulq",  Vu0Form.FdFsQ, true, Arith("vmul", Src.Q));
            S1(0x1D, "vmaxi",  Vu0Form.FdFsI, true, Arith("vmax", Src.I));
            S1(0x1E, "vmuli",  Vu0Form.FdFsI, true, Arith("vmul", Src.I));
            S1(0x1F, "vminii", Vu0Form.FdFsI, true, Arith("vmini", Src.I));
            S1(0x20, "vaddq",  Vu0Form.FdFsQ, true, Arith("vadd", Src.Q));
            S1(0x21, "vmaddq", Vu0Form.FdFsQ, true, Arith("vmadd", Src.Q));
            S1(0x22, "vaddi",  Vu0Form.FdFsI, true, Arith("vadd", Src.I));
            S1(0x23, "vmaddi", Vu0Form.FdFsI, true, Arith("vmadd", Src.I));
            S1(0x24, "vsubq",  Vu0Form.FdFsQ, true, Arith("vsub", Src.Q));
            S1(0x25, "vmsubq", Vu0Form.FdFsQ, true, Arith("vmsub", Src.Q));
            S1(0x26, "vsubi",  Vu0Form.FdFsI, true, Arith("vsub", Src.I));
            S1(0x27, "vmsubi", Vu0Form.FdFsI, true, Arith("vmsub", Src.I));
            S1(0x28, "vadd",   Vu0Form.FdFsFt, true, Arith("vadd", Src.Ft));
            S1(0x29, "vmadd",  Vu0Form.FdFsFt, true, Arith("vmadd", Src.Ft));
            S1(0x2A, "vmul",   Vu0Form.FdFsFt, true, Arith("vmul", Src.Ft));
            S1(0x2B, "vmax",   Vu0Form.FdFsFt, true, Arith("vmax", Src.Ft));
            S1(0x2C, "vsub",   Vu0Form.FdFsFt, true, Arith("vsub", Src.Ft));
            S1(0x2D, "vmsub",  Vu0Form.FdFsFt, true, Arith("vmsub", Src.Ft));
            S1(0x2E, "vopmsub", Vu0Form.FdFsFt, true, OpMsub, fixedDest: 0x7);
            S1(0x2F, "vmini",  Vu0Form.FdFsFt, true, Arith("vmini", Src.Ft));
            S1(0x30, "viadd",  Vu0Form.IdIsIt, false, (c, f) => c.VU0.WriteVI(f.Fd & 0xF, (ushort)(Vi(c, f.Fs) + Vi(c, f.Ft))));
            S1(0x31, "visub",  Vu0Form.IdIsIt, false, (c, f) => c.VU0.WriteVI(f.Fd & 0xF, (ushort)(Vi(c, f.Fs) - Vi(c, f.Ft))));
            S1(0x32, "viaddi", Vu0Form.ItIsImm5, false, (c, f) => c.VU0.WriteVI(f.Ft & 0xF, (ushort)(Vi(c, f.Fs) + Vu0MacroOp.Imm5(f.Fd))));
            S1(0x34, "viand",  Vu0Form.IdIsIt, false, (c, f) => c.VU0.WriteVI(f.Fd & 0xF, (ushort)(Vi(c, f.Fs) & Vi(c, f.Ft))));
            S1(0x35, "vior",   Vu0Form.IdIsIt, false, (c, f) => c.VU0.WriteVI(f.Fd & 0xF, (ushort)(Vi(c, f.Fs) | Vi(c, f.Ft))));
            S1(0x38, "vcallms",  Vu0Form.CallImm, false, null);
            S1(0x39, "vcallmsr", Vu0Form.CallReg, false, null);

            // ── SPECIAL2 ────────────────────────────────────────────────────
            string[] bcOps2 = ["vadda", "vsuba", "vmadda", "vmsuba"];
            for (int op = 0; op < bcOps2.Length; op++)
                for (int bc = 0; bc < 4; bc++)
                    S2(op * 4 + bc, bcOps2[op] + Comp[bc], Vu0Form.AccFsFtBc, true, AccBc(bcOps2[op]));

            S2(0x10, "vitof0",  Vu0Form.FtFs, true, Itof(0));
            S2(0x11, "vitof4",  Vu0Form.FtFs, true, Itof(4));
            S2(0x12, "vitof12", Vu0Form.FtFs, true, Itof(12));
            S2(0x13, "vitof15", Vu0Form.FtFs, true, Itof(15));
            S2(0x14, "vftoi0",  Vu0Form.FtFs, true, Ftoi(0));
            S2(0x15, "vftoi4",  Vu0Form.FtFs, true, Ftoi(4));
            S2(0x16, "vftoi12", Vu0Form.FtFs, true, Ftoi(12));
            S2(0x17, "vftoi15", Vu0Form.FtFs, true, Ftoi(15));
            for (int bc = 0; bc < 4; bc++)
                S2(0x18 + bc, "vmula" + Comp[bc], Vu0Form.AccFsFtBc, true, AccBc("vmula"));
            S2(0x1C, "vmulaq",  Vu0Form.AccFsQ, true, Acc("vmula", Src.Q));
            S2(0x1D, "vabs",    Vu0Form.FtFs, true, (c, f) => WriteFt(c, f, Vector4.Abs(Vf(c, f.Fs))));
            S2(0x1E, "vmulai",  Vu0Form.AccFsI, true, Acc("vmula", Src.I));
            S2(0x1F, "vclipw",  Vu0Form.Clip, true, ClipW, fixedDest: 0x7);
            S2(0x20, "vaddaq",  Vu0Form.AccFsQ, true, Acc("vadda", Src.Q));
            S2(0x21, "vmaddaq", Vu0Form.AccFsQ, true, Acc("vmadda", Src.Q));
            S2(0x22, "vaddai",  Vu0Form.AccFsI, true, Acc("vadda", Src.I));
            S2(0x23, "vmaddai", Vu0Form.AccFsI, true, Acc("vmadda", Src.I));
            S2(0x24, "vsubaq",  Vu0Form.AccFsQ, true, Acc("vsuba", Src.Q));
            S2(0x25, "vmsubaq", Vu0Form.AccFsQ, true, Acc("vmsuba", Src.Q));
            S2(0x26, "vsubai",  Vu0Form.AccFsI, true, Acc("vsuba", Src.I));
            S2(0x27, "vmsubai", Vu0Form.AccFsI, true, Acc("vmsuba", Src.I));
            S2(0x28, "vadda",   Vu0Form.AccFsFt, true, Acc("vadda", Src.Ft));
            S2(0x29, "vmadda",  Vu0Form.AccFsFt, true, Acc("vmadda", Src.Ft));
            S2(0x2A, "vmula",   Vu0Form.AccFsFt, true, Acc("vmula", Src.Ft));
            S2(0x2C, "vsuba",   Vu0Form.AccFsFt, true, Acc("vsuba", Src.Ft));
            S2(0x2D, "vmsuba",  Vu0Form.AccFsFt, true, Acc("vmsuba", Src.Ft));
            S2(0x2E, "vopmula", Vu0Form.AccFsFt, true, OpMula, fixedDest: 0x7);
            S2(0x2F, "vnop",    Vu0Form.None, false, null);
            S2(0x30, "vmove",   Vu0Form.FtFs, true, (c, f) => WriteFt(c, f, Vf(c, f.Fs)));
            S2(0x31, "vmr32",   Vu0Form.FtFs, true, (c, f) => { var v = Vf(c, f.Fs); WriteFt(c, f, new Vector4(v.Y, v.Z, v.W, v.X)); });
            S2(0x34, "vlqi",    Vu0Form.LoadInc,  true, (c, f) => { WriteFt(c, f, LoadQ(c, Vi(c, f.Fs))); IncVi(c, f.Fs, 1); });
            S2(0x35, "vsqi",    Vu0Form.StoreInc, true, (c, f) => { StoreQ(c, Vi(c, f.Ft), Vf(c, f.Fs), f.Dest); IncVi(c, f.Ft, 1); });
            S2(0x36, "vlqd",    Vu0Form.LoadDec,  true, (c, f) => { IncVi(c, f.Fs, -1); WriteFt(c, f, LoadQ(c, Vi(c, f.Fs))); });
            S2(0x37, "vsqd",    Vu0Form.StoreDec, true, (c, f) => { IncVi(c, f.Ft, -1); StoreQ(c, Vi(c, f.Ft), Vf(c, f.Fs), f.Dest); });
            S2(0x38, "vdiv",    Vu0Form.QFsFt, false, (c, f) => c.VU0.Q = Get(Vf(c, f.Fs), f.Fsf) / Get(Vf(c, f.Ft), f.Ftf));
            S2(0x39, "vsqrt",   Vu0Form.QFt,   false, (c, f) => c.VU0.Q = MathF.Sqrt(MathF.Abs(Get(Vf(c, f.Ft), f.Ftf))));
            S2(0x3A, "vrsqrt",  Vu0Form.QFsFt, false, (c, f) => c.VU0.Q = Get(Vf(c, f.Fs), f.Fsf) / MathF.Sqrt(MathF.Abs(Get(Vf(c, f.Ft), f.Ftf))));
            S2(0x3B, "vwaitq",  Vu0Form.None,  false, null);
            S2(0x3C, "vmtir",   Vu0Form.ItFsf, false, (c, f) => c.VU0.WriteVI(f.Ft & 0xF, (ushort)BitConverter.SingleToUInt32Bits(Get(Vf(c, f.Fs), f.Fsf))));
            S2(0x3D, "vmfir",   Vu0Form.FtIs,  true, (c, f) => WriteFt(c, f, new Vector4(BitConverter.Int32BitsToSingle((short)Vi(c, f.Fs)))));
            S2(0x3E, "vilwr",   Vu0Form.ItIsMem, true, (c, f) => c.VU0.WriteVI(f.Ft & 0xF, LoadVi(c, Vi(c, f.Fs), f.Dest)));
            S2(0x3F, "viswr",   Vu0Form.ItIsMem, true, (c, f) => StoreVi(c, Vi(c, f.Fs), f.Dest, Vi(c, f.Ft)));
            S2(0x40, "vrnext",  Vu0Form.FtR,  true, (c, f) => { c.VU0.RngState = VU0Helpers.NextRng(c.VU0.RngState); WriteFt(c, f, new Vector4(BitConverter.UInt32BitsToSingle(c.VU0.RngState))); });
            S2(0x41, "vrget",   Vu0Form.FtR,  true, (c, f) => WriteFt(c, f, new Vector4(BitConverter.UInt32BitsToSingle(c.VU0.RngState))));
            S2(0x42, "vrinit",  Vu0Form.RFsf, false, (c, f) => c.VU0.RngState = 0x3F800000u | (Bits(Vf(c, f.Fs), f.Fsf) & 0x7FFFFFu));
            S2(0x43, "vrxor",   Vu0Form.RFsf, false, (c, f) => c.VU0.RngState = 0x3F800000u | ((c.VU0.RngState ^ Bits(Vf(c, f.Fs), f.Fsf)) & 0x7FFFFFu));
        }

        // ── Registration ────────────────────────────────────────────────────

        private static void S1(int funct, string name, Vu0Form form, bool hasDest,
            Action<CpuState, Vu0Fields>? semantics, byte? fixedDest = null)
        {
            var op = new Vu0MacroOp
            {
                Opcode = 0x12, Funct = funct, Name = name, Form = form, HasDest = hasDest,
                FixedDest = fixedDest, Semantics = semantics, ArgType = InstrArgType.R,
                Description = $"VU0 macro {name}",
            };
            Special1[funct] = op;
            ByName[name] = op;
        }

        private static void S2(int index, string name, Vu0Form form, bool hasDest,
            Action<CpuState, Vu0Fields>? semantics, byte? fixedDest = null)
        {
            var op = new Vu0MacroOp
            {
                Opcode = 0x12, Funct = 0x3C | (index & 3), Special2Index = index, Name = name, Form = form,
                HasDest = hasDest, FixedDest = fixedDest, Semantics = semantics, ArgType = InstrArgType.R,
                Description = $"VU0 macro {name}",
            };
            Special2[index] = op;
            ByName[name] = op;
        }

        // ── Lookup ──────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the macro op for a COP2 word with the CO bit set, or null for an
        /// unassigned slot.
        /// </summary>
        public static Vu0MacroOp? Lookup(uint word)
        {
            if ((word >> 26) != 0x12 || (word & (1u << 25)) == 0) return null;
            int funct = (int)(word & 0x3F);
            if (funct >= 0x3C)
                return Special2[(int)((word & 3) | ((word >> 4) & 0x7C))];
            return Special1[funct];
        }

        /// <summary>True when <paramref name="mnemonic"/> (without a dest suffix) is a VU0 macro op.</summary>
        public static bool IsMnemonic(string mnemonic) => ByName.ContainsKey(SplitDest(mnemonic, out _));

        // ── Assembly ────────────────────────────────────────────────────────

        /// <summary>
        /// Encodes a VU0 macro instruction written the way <see cref="Vu0MacroOp.Disasm"/>
        /// prints it, e.g. <c>vadd.xyz vf1, vf2, vf3</c>, <c>vmulx.w vf4, vf4, vf5x</c>,
        /// <c>vdiv Q, vf1x, vf2y</c>, <c>vcallms 0x1F8</c>, <c>vlqi.xyzw vf1, (vi2++)</c>.
        /// </summary>
        public static uint Assemble(string mnemonic, string[] operands)
        {
            string name = SplitDest(mnemonic, out string? destText);
            if (!ByName.TryGetValue(name, out var op))
                throw new ArgumentException($"Unknown VU0 macro instruction: {mnemonic}");

            byte dest = op.FixedDest ?? (op.HasDest ? (byte)0xF : (byte)0);
            if (destText != null)
            {
                if (!op.HasDest) throw new ArgumentException($"{name} takes no .xyzw suffix");
                dest = ParseDest(destText, mnemonic);
            }

            int ft = 0, fs = 0, fd = 0, fsf = 0, ftf = 0;
            void Need(int n)
            {
                if (operands.Length != n)
                    throw new ArgumentException($"Expected {n} operand{(n == 1 ? "" : "s")} for {name}, got {operands.Length}");
            }

            switch (op.Form)
            {
                case Vu0Form.FdFsFt:
                    Need(3); fd = Vf(operands[0]); fs = Vf(operands[1]); ft = Vf(operands[2]); break;
                case Vu0Form.FdFsFtBc:
                    Need(3); fd = Vf(operands[0]); fs = Vf(operands[1]); ft = VfComp(operands[2], out _); break;
                case Vu0Form.FdFsQ:
                case Vu0Form.FdFsI:
                    Need(3); fd = Vf(operands[0]); fs = Vf(operands[1]); break;
                case Vu0Form.AccFsFt:
                    Need(3); fs = Vf(operands[1]); ft = Vf(operands[2]); break;
                case Vu0Form.AccFsFtBc:
                    Need(3); fs = Vf(operands[1]); ft = VfComp(operands[2], out _); break;
                case Vu0Form.AccFsQ:
                case Vu0Form.AccFsI:
                    Need(3); fs = Vf(operands[1]); break;
                case Vu0Form.FtFs:
                    Need(2); ft = Vf(operands[0]); fs = Vf(operands[1]); break;
                case Vu0Form.Clip:
                    Need(2); fs = Vf(operands[0]); ft = VfComp(operands[1], out _); break;
                case Vu0Form.IdIsIt:
                    Need(3); fd = Vi(operands[0]); fs = Vi(operands[1]); ft = Vi(operands[2]); break;
                case Vu0Form.ItIsImm5:
                {
                    Need(3); ft = Vi(operands[0]); fs = Vi(operands[1]);
                    long imm = MipsAssembler.ParseImmediate(operands[2]);
                    if (imm < -16 || imm > 15) throw new ArgumentException($"viaddi immediate {operands[2]} out of range (-16..15)");
                    fd = (int)imm & 0x1F;
                    break;
                }
                case Vu0Form.CallImm:
                {
                    Need(1);
                    long addr = MipsAssembler.ParseImmediate(operands[0]);
                    if (addr < 0 || addr > 0x3FFF8 || (addr & 7) != 0)
                        throw new ArgumentException($"vcallms address {operands[0]} must be a multiple of 8 below 0x40000");
                    uint imm15 = (uint)(addr / 8);
                    return 0x48000000u | (1u << 25) | (imm15 << 6) | 0x38u;
                }
                case Vu0Form.CallReg:
                    if (operands.Length > 1) throw new ArgumentException("vcallmsr takes at most one operand (vi27)");
                    fs = 27; break;
                case Vu0Form.None:
                    Need(0); break;
                case Vu0Form.LoadInc:
                    Need(2); ft = Vf(operands[0]); fs = Vi(Strip(operands[1], "(", "++)")); break;
                case Vu0Form.StoreInc:
                    Need(2); fs = Vf(operands[0]); ft = Vi(Strip(operands[1], "(", "++)")); break;
                case Vu0Form.LoadDec:
                    Need(2); ft = Vf(operands[0]); fs = Vi(Strip(operands[1], "(--", ")")); break;
                case Vu0Form.StoreDec:
                    Need(2); fs = Vf(operands[0]); ft = Vi(Strip(operands[1], "(--", ")")); break;
                case Vu0Form.QFsFt:
                    Need(3); fs = VfComp(operands[1], out fsf); ft = VfComp(operands[2], out ftf); break;
                case Vu0Form.QFt:
                    Need(2); ft = VfComp(operands[1], out ftf); break;
                case Vu0Form.ItFsf:
                    Need(2); ft = Vi(operands[0]); fs = VfComp(operands[1], out fsf); break;
                case Vu0Form.FtIs:
                    Need(2); ft = Vf(operands[0]); fs = Vi(operands[1]); break;
                case Vu0Form.ItIsMem:
                    Need(2); ft = Vi(operands[0]); fs = Vi(Strip(operands[1], "(", ")")); break;
                case Vu0Form.FtR:
                    Need(2); ft = Vf(operands[0]); break;
                case Vu0Form.RFsf:
                    Need(2); fs = VfComp(operands[1], out fsf); break;
            }

            // The dest field doubles as fsf/ftf for the Q / R / vmtir forms.
            uint destBits = op.Form is Vu0Form.QFsFt or Vu0Form.QFt or Vu0Form.ItFsf or Vu0Form.RFsf
                ? (uint)((ftf << 2) | fsf)
                : (uint)(((dest & 1) << 3) | ((dest >> 1 & 1) << 2) | ((dest >> 2 & 1) << 1) | (dest >> 3 & 1));

            uint word = 0x48000000u | (1u << 25) | (destBits << 21) | ((uint)ft << 16) | ((uint)fs << 11);
            if (op.Special2Index is int idx)
                return word | ((uint)(idx >> 2) << 6) | 0x3Cu | (uint)(idx & 3);
            return word | ((uint)fd << 6) | (uint)op.Funct!.Value;
        }

        private static string SplitDest(string mnemonic, out string? dest)
        {
            int dot = mnemonic.IndexOf('.');
            if (dot < 0) { dest = null; return mnemonic.ToLowerInvariant(); }
            dest = mnemonic[(dot + 1)..].ToLowerInvariant();
            return mnemonic[..dot].ToLowerInvariant();
        }

        private static byte ParseDest(string text, string mnemonic)
        {
            byte mask = 0;
            int last = -1;
            foreach (char ch in text)
            {
                int i = Array.IndexOf(Comp, ch);
                if (i <= last) throw new ArgumentException($"Bad dest mask in {mnemonic} (use x, y, z, w in order)");
                mask |= (byte)(1 << i);
                last = i;
            }
            if (mask == 0) throw new ArgumentException($"Empty dest mask in {mnemonic}");
            return mask;
        }

        private static string Strip(string text, string prefix, string suffix)
        {
            string t = text.Trim().Replace(" ", "");
            if (!t.StartsWith(prefix, StringComparison.Ordinal) || !t.EndsWith(suffix, StringComparison.Ordinal))
                throw new ArgumentException($"Expected {prefix}vi{suffix}, got '{text}'");
            return t[prefix.Length..^suffix.Length];
        }

        private static int Vf(string text)
        {
            string t = text.Trim().TrimStart('$').ToLowerInvariant();
            if (t.StartsWith("vf") && int.TryParse(t[2..], out int n) && n is >= 0 and < 32) return n;
            throw new ArgumentException($"Expected a VU0 float register vf0-vf31, got '{text}'");
        }

        private static int VfComp(string text, out int comp)
        {
            string t = text.Trim().TrimStart('$').ToLowerInvariant();
            if (t.Length > 2)
            {
                comp = Array.IndexOf(Comp, t[^1]);
                if (comp >= 0) return Vf(t[..^1]);
            }
            throw new ArgumentException($"Expected a VU0 register with a component (e.g. vf3x), got '{text}'");
        }

        private static int Vi(string text)
        {
            string t = text.Trim().TrimStart('$').ToLowerInvariant();
            if (t.StartsWith("vi") && int.TryParse(t[2..], out int n) && n is >= 0 and < 32) return n;
            throw new ArgumentException($"Expected a VU0 integer register vi0-vi31, got '{text}'");
        }

        // ── Semantics ───────────────────────────────────────────────────────

        private enum Src { Ft, Q, I }

        private static Vector4 Vf(CpuState c, int r) => c.VU0.ReadVF(r & 0x1F);
        private static ushort Vi(CpuState c, int r) => c.VU0.ReadVI(r & 0xF);
        private static float Get(Vector4 v, int comp) => VU0Helpers.GetComponent(v, comp);
        private static uint Bits(Vector4 v, int comp) => VU0Helpers.GetComponentBits(v, comp);
        private static void WriteFt(CpuState c, Vu0Fields f, Vector4 v) => c.VU0.WriteVF(f.Ft, v, f.Dest);
        private static void IncVi(CpuState c, int r, int delta) => c.VU0.WriteVI(r & 0xF, (ushort)(Vi(c, r) + delta));

        private static Vector4 Operand(CpuState c, Vu0Fields f, Src src) => src switch
        {
            Src.Q => new Vector4(c.VU0.Q),
            Src.I => new Vector4(c.VU0.I),
            _     => Vf(c, f.Ft),
        };

        private static Vector4 Apply(string op, CpuState c, Vector4 a, Vector4 b) => op switch
        {
            "vadd" or "vadda"   => a + b,
            "vsub" or "vsuba"   => a - b,
            "vmul" or "vmula"   => a * b,
            "vmadd" or "vmadda" => c.VU0.ReadACC() + a * b,
            "vmsub" or "vmsuba" => c.VU0.ReadACC() - a * b,
            "vmax"              => Vector4.Max(a, b),
            "vmini"             => Vector4.Min(a, b),
            _                   => a,
        };

        private static Action<CpuState, Vu0Fields> Arith(string op, Src src) =>
            (c, f) => c.VU0.WriteVF(f.Fd, Apply(op, c, Vf(c, f.Fs), Operand(c, f, src)), f.Dest);

        private static Action<CpuState, Vu0Fields> ArithBc(string op) =>
            (c, f) => c.VU0.WriteVF(f.Fd, Apply(op, c, Vf(c, f.Fs), new Vector4(Get(Vf(c, f.Ft), f.Bc))), f.Dest);

        private static Action<CpuState, Vu0Fields> Acc(string op, Src src) =>
            (c, f) => c.VU0.WriteACC(Apply(op, c, Vf(c, f.Fs), Operand(c, f, src)), f.Dest);

        private static Action<CpuState, Vu0Fields> AccBc(string op) =>
            (c, f) => c.VU0.WriteACC(Apply(op, c, Vf(c, f.Fs), new Vector4(Get(Vf(c, f.Ft), f.Bc))), f.Dest);

        private static Vector4 Cross(Vector4 s, Vector4 t) => new(s.Y * t.Z, s.Z * t.X, s.X * t.Y, 0);

        private static void OpMula(CpuState c, Vu0Fields f) =>
            c.VU0.WriteACC(Cross(Vf(c, f.Fs), Vf(c, f.Ft)), 0x7);

        private static void OpMsub(CpuState c, Vu0Fields f) =>
            c.VU0.WriteVF(f.Fd, c.VU0.ReadACC() - Cross(Vf(c, f.Fs), Vf(c, f.Ft)), 0x7);

        private static Action<CpuState, Vu0Fields> Itof(int shift) => (c, f) =>
        {
            var v = Vf(c, f.Fs);
            float scale = 1f / (1 << shift);
            WriteFt(c, f, new Vector4(
                BitConverter.SingleToInt32Bits(v.X) * scale, BitConverter.SingleToInt32Bits(v.Y) * scale,
                BitConverter.SingleToInt32Bits(v.Z) * scale, BitConverter.SingleToInt32Bits(v.W) * scale));
        };

        private static Action<CpuState, Vu0Fields> Ftoi(int shift) => (c, f) =>
        {
            var v = Vf(c, f.Fs);
            float scale = 1 << shift;
            static float AsBits(float x)
            {
                double d = Math.Clamp((double)x, int.MinValue, int.MaxValue);
                return BitConverter.Int32BitsToSingle(float.IsNaN(x) ? 0 : (int)d);
            }
            WriteFt(c, f, new Vector4(AsBits(v.X * scale), AsBits(v.Y * scale), AsBits(v.Z * scale), AsBits(v.W * scale)));
        };

        private static void ClipW(CpuState c, Vu0Fields f)
        {
            var s = Vf(c, f.Fs);
            float w = MathF.Abs(Get(Vf(c, f.Ft), 3));
            uint flags = 0;
            if (s.X > +w) flags |= 0x01;
            if (s.X < -w) flags |= 0x02;
            if (s.Y > +w) flags |= 0x04;
            if (s.Y < -w) flags |= 0x08;
            if (s.Z > +w) flags |= 0x10;
            if (s.Z < -w) flags |= 0x20;
            c.VU0.ClipFlags = ((c.VU0.ClipFlags << 6) | flags) & 0xFFFFFFu;
        }

        private static Vector4 LoadQ(CpuState c, ushort vi)
        {
            int a = (vi * 16) & (c.VU0.DataMem.Length - 1);
            return new Vector4(
                BitConverter.ToSingle(c.VU0.DataMem, a), BitConverter.ToSingle(c.VU0.DataMem, a + 4),
                BitConverter.ToSingle(c.VU0.DataMem, a + 8), BitConverter.ToSingle(c.VU0.DataMem, a + 12));
        }

        private static void StoreQ(CpuState c, ushort vi, Vector4 v, byte dest)
        {
            int a = (vi * 16) & (c.VU0.DataMem.Length - 1);
            for (int i = 0; i < 4; i++)
                if ((dest & (1 << i)) != 0)
                    BitConverter.TryWriteBytes(c.VU0.DataMem.AsSpan(a + i * 4, 4), Get(v, i));
        }

        private static int FirstComp(byte dest) => (dest & 1) != 0 ? 0 : (dest & 2) != 0 ? 1 : (dest & 4) != 0 ? 2 : 3;

        private static ushort LoadVi(CpuState c, ushort vi, byte dest)
        {
            int a = ((vi * 16) & (c.VU0.DataMem.Length - 1)) + FirstComp(dest) * 4;
            return BitConverter.ToUInt16(c.VU0.DataMem, a);
        }

        private static void StoreVi(CpuState c, ushort vi, byte dest, ushort value)
        {
            int a = (vi * 16) & (c.VU0.DataMem.Length - 1);
            for (int i = 0; i < 4; i++)
                if ((dest & (1 << i)) != 0)
                    BitConverter.TryWriteBytes(c.VU0.DataMem.AsSpan(a + i * 4, 4), (uint)value);
        }
    }
}
