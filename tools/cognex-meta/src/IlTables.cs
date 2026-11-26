#if COGNEX_META
namespace CognexMeta;

/// <summary>
/// Minimal ECMA-335 opcode operand-size table, enough to walk a method body and
/// pick up `ldc.*` constants feeding stores. -1 means the opcode is not valid/known,
/// in which case the walk of that body stops and whatever was collected is kept.
/// </summary>
internal static class IlTables
{
    private static readonly int[] One = new int[256];
    private static readonly int[] Two = new int[256];

    static IlTables()
    {
        for (int i = 0; i < 256; i++) { One[i] = -1; Two[i] = -1; }

        Set(One, 0x00, 0x01, 0);      // nop, break
        Set(One, 0x02, 0x0D, 0);      // ldarg.0-3, ldloc.0-3, stloc.0-3
        Set(One, 0x0E, 0x13, 1);      // ldarg.s .. stloc.s
        Set(One, 0x14, 0x1E, 0);      // ldnull, ldc.i4.m1..8
        One[0x1F] = 1;                // ldc.i4.s
        One[0x20] = 4;                // ldc.i4
        One[0x21] = 8;                // ldc.i8
        One[0x22] = 4;                // ldc.r4
        One[0x23] = 8;                // ldc.r8
        One[0x25] = 0; One[0x26] = 0; // dup, pop
        One[0x27] = 4;                // jmp
        One[0x28] = 4; One[0x29] = 4; // call, calli
        One[0x2A] = 0;                // ret
        Set(One, 0x2B, 0x37, 1);      // short branches
        Set(One, 0x38, 0x44, 4);      // long branches
        Set(One, 0x46, 0x57, 0);      // ldind/stind/arith
        Set(One, 0x58, 0x66, 0);      // arith/logic
        Set(One, 0x67, 0x6E, 0);      // conv.*
        Set(One, 0x6F, 0x75, 4);      // callvirt, cpobj, ldobj, ldstr, newobj, castclass, isinst
        One[0x76] = 0;                // conv.r.un
        One[0x79] = 4;                // unbox
        One[0x7A] = 0;                // throw
        Set(One, 0x7B, 0x81, 4);      // ldfld..stobj
        Set(One, 0x82, 0x8B, 0);      // conv.ovf.*.un
        One[0x8C] = 4; One[0x8D] = 4; // box, newarr
        One[0x8E] = 0;                // ldlen
        One[0x8F] = 4;                // ldelema
        Set(One, 0x90, 0xA2, 0);      // ldelem/stelem
        Set(One, 0xA3, 0xA5, 4);      // ldelem, stelem, unbox.any
        Set(One, 0xB3, 0xBA, 0);      // conv.ovf.*
        One[0xC2] = 4;                // refanyval
        One[0xC3] = 0;                // ckfinite
        One[0xC6] = 4;                // mkrefany
        One[0xD0] = 4;                // ldtoken
        Set(One, 0xD1, 0xDC, 0);      // conv.*, add.ovf..sub.ovf.un, endfinally
        One[0xDD] = 4;                // leave
        One[0xDE] = 1;                // leave.s
        One[0xDF] = 0; One[0xE0] = 0; // stind.i, conv.u

        Set(Two, 0x00, 0x05, 0);      // arglist, ceq, cgt, cgt.un, clt, clt.un
        Set(Two, 0x06, 0x07, 4);      // ldftn, ldvirtftn
        Set(Two, 0x09, 0x0E, 2);      // ldarg, ldarga, starg, ldloc, ldloca, stloc
        Two[0x0F] = 0;                // localloc
        Two[0x11] = 0;                // endfilter
        Two[0x12] = 1;                // unaligned.
        Set(Two, 0x13, 0x14, 0);      // volatile., tail.
        Set(Two, 0x15, 0x16, 4);      // initobj, constrained.
        Set(Two, 0x17, 0x18, 0);      // cpblk, initblk
        Two[0x19] = 1;                // no.
        Two[0x1A] = 0;                // rethrow
        Two[0x1C] = 4;                // sizeof
        Set(Two, 0x1D, 0x1E, 0);      // refanytype, readonly.
    }

    private static void Set(int[] t, int from, int to, int size)
    {
        for (int i = from; i <= to; i++) t[i] = size;
    }

    public static int OneByteOperandSize(byte op) => One[op];
    public static int TwoByteOperandSize(byte op2) => Two[op2];
}
#endif
