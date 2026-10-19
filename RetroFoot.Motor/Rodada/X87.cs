using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Partida;

namespace RetroFoot.Motor.Rodada;

// As contas da FPU como o Delphi 1 as deixa (64 bits de mantissa, ao par mais proximo):
// +, -, x, / e raiz exatas no Ext; Power com Ln e Exp corretamente arredondados.
// (Traduzido de motor/rodada/ext.go.)
internal static class X87
{
    public static Ext X(double v) => Ext.F64(v);
    public static Ext Xi(long v) => Ext.Int(v);
    public static int Arred(Ext x) => (int)x.Round();

    // seg14:1194: Power(base, expoente); base <= 0 da 0 (no original, erro de execucao)
    public static Ext Pot(Ext @base, Ext expoente) => Fpu.PotenciaExata(@base, expoente);

    public static Ext DeExt80(byte[] b, int o = 0) => Ext.Bytes(b.AsSpan(o, 10));
    public static void ParaExt80(Ext x, byte[] b, int o = 0) => x.Bytes().CopyTo(b, o);

    public static Ext DeReal48(byte[] b, int o = 0) => Ext.DeReal48(b.AsSpan(o, 6));
    public static void ParaReal48(Ext x, byte[] b, int o = 0) => x.Real48().CopyTo(b, o);
    // arredonda para Real48 e volta (o que o jogo faz quando guarda num Real)
    public static Ext Real48(Ext x) => Ext.DeReal48(x.Real48());

    // constante Extended do codigo (10 bytes, como no EXE)
    public static Ext Const(params byte[] b) => Ext.Bytes(b);

    // constante Real48 empurrada nos 3 registos AX, BX, DX
    public static Ext Real48Regs(ushort ax, ushort bx, ushort dx)
    {
        var b = new byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(b, ax);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), bx);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(4), dx);
        return Ext.DeReal48(b);
    }

    // seg14:10d1: x (Real48) <- a x + (1 - a) b, em Extended, arredondado para Real48
    public static void MediaPonderada(byte[] x, Ext a, Ext b) =>
        ParaReal48(DeReal48(x).Mul(a).Add(X(1).Sub(a).Mul(b)), x);

    // seg14:115e: o mesmo com x Extended de 10 bytes
    public static void MediaPonderadaExt(byte[] x, Ext a, Ext b) =>
        ParaExt80(DeExt80(x).Mul(a).Add(X(1).Sub(a).Mul(b)), x);
}

public static class Listas
{
    // seg14:0f68: para i = 1 ate 3 x Count (16 bits com sinal), r = Random(Count), tira o
    // item r e o poe no fim. Lista vazia: nao chama o Random.
    public static void Embaralhar<T>(Aleatorio r, List<T> l)
    {
        short total = (short)(3 * (short)l.Count);
        for (short i = 1; i <= total; i++)
        {
            int k = r.Random(l.Count);
            var x = l[k];
            l.RemoveAt(k);
            l.Add(x);
        }
    }

    // seg14:0e0d: bolha; quando emOrdem(item j-1, item j) e falso, os dois trocam
    public static void Ordenar<T>(List<T> l, Func<T, T, bool> emOrdem)
    {
        short n = (short)l.Count;
        for (short i = 1; i <= n - 1; i++)
            for (short j = (short)(n - 1); j >= i; j--)
                if (!emOrdem(l[j - 1], l[j])) (l[j - 1], l[j]) = (l[j], l[j - 1]);
    }
}
