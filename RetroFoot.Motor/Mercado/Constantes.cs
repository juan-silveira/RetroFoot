using RetroFoot.Motor.Base;

namespace RetroFoot.Motor.Mercado;

// constantes Extended do codigo (expoente com sinal, mantissa)
public static class Constantes
{
    public static Ext Ext10(ushort exp, ulong mant) => new(mant, exp);

    public static readonly Ext C1_04 = Ext10(0x3fff, 0x851eb851eb851eb8);   // 1,04 (seg03:b1ac, 8037)
    public static readonly Ext C0_2 = Ext10(0x3ffc, 0xcccccccccccccccd);
    public static readonly Ext C005 = Ext10(0x3ffa, 0xcccccccccccccccd);    // 0,05
    public static readonly Ext C01 = Ext10(0x3ffb, 0xcccccccccccccccd);     // 0,1
    public static readonly Ext C08 = Ext10(0x3ffe, 0xcccccccccccccccd);     // 0,8
    public static readonly Ext C09 = Ext10(0x3ffe, 0xe666666666666666);     // 0,9
    public static readonly Ext C11 = Ext10(0x3fff, 0x8ccccccccccccccd);     // 1,1
    public static readonly Ext C115 = Ext10(0x3fff, 0x9333333333333333);    // 1,15
    public static readonly Ext C12 = Ext10(0x3fff, 0x999999999999999a);     // 1,2
    public static readonly Ext C13 = Ext10(0x3fff, 0xa666666666666666);     // 1,3
    public static readonly Ext C16 = Ext10(0x3fff, 0xcccccccccccccccd);     // 1,6
    public static readonly Ext C195 = Ext10(0x3fff, 0xf99999999999999a);    // 1,95
}
