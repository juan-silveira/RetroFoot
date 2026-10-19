using System;
using System.Buffers.Binary;

namespace RetroFoot.Motor.Base;

// Os formatos de ponto flutuante do Delphi 1 que o jogo classico grava nos saves: o Real de
// 6 bytes (Turbo Pascal) e o Extended de 10 bytes (8087).
public static class Reais
{
    // Real de 6 bytes: expoente (vies 129) no 1o byte, 39 bits de mantissa com o 1
    // implicito, bit 47 = sinal. Expoente 0 = zero.
    public static double Real48(ReadOnlySpan<byte> b)
    {
        if (b[0] == 0) return 0;
        ulong m = 0;
        for (int i = 5; i >= 1; i--) m = m << 8 | b[i];
        double sinal = (m & (1UL << 39)) != 0 ? -1 : 1;
        m &= (1UL << 39) - 1;
        return sinal * (1 + m / (double)(1UL << 39)) * Math.Pow(2, b[0] - 129);
    }

    // grava v como Real de 6 bytes (arredonda a mantissa)
    public static void PorReal48(Span<byte> b, double v)
    {
        if (v == 0) { b[..6].Clear(); return; }
        var (mant, e) = Frexp(Math.Abs(v));
        mant *= 2;
        e--;
        ulong m = (ulong)Math.Round((mant - 1) * (1UL << 39), MidpointRounding.AwayFromZero);
        if (m >= 1UL << 39) { m = 0; e++; }
        if (v < 0) m |= 1UL << 39;
        b[0] = (byte)(e + 129);
        for (int i = 1; i <= 5; i++) { b[i] = (byte)m; m >>= 8; }
    }

    // Extended de 10 bytes: 64 bits de mantissa com o 1 explicito, 15 bits de expoente
    // (vies 16383) e sinal.
    public static double Ext80(ReadOnlySpan<byte> b)
    {
        ulong m = BinaryPrimitives.ReadUInt64LittleEndian(b);
        ushort ex = BinaryPrimitives.ReadUInt16LittleEndian(b[8..]);
        if (m == 0) return 0;
        double v = m / Math.Pow(2, 63) * Math.Pow(2, (ex & 0x7FFF) - 16383);
        return (ex & 0x8000) != 0 ? -v : v;
    }

    public static void PorExt80(Span<byte> b, double v)
    {
        if (v == 0) { b[..10].Clear(); return; }
        var (mant, e) = Frexp(Math.Abs(v));
        mant *= 2;
        e--;
        ulong m = (ulong)(mant * Math.Pow(2, 63));
        BinaryPrimitives.WriteUInt64LittleEndian(b, m);
        ushort ex = (ushort)(e + 16383);
        if (v < 0) ex |= 0x8000;
        BinaryPrimitives.WriteUInt16LittleEndian(b[8..], ex);
    }

    // math.Frexp do Go: v = mant x 2^e, mant em [0,5; 1)
    internal static (double Mant, int Exp) Frexp(double v)
    {
        if (v == 0 || double.IsInfinity(v) || double.IsNaN(v)) return (v, 0);
        long bits = BitConverter.DoubleToInt64Bits(v);
        int e = (int)((bits >> 52) & 0x7FF);
        if (e == 0)
        {
            // subnormal: normaliza antes
            v *= Math.Pow(2, 64);
            bits = BitConverter.DoubleToInt64Bits(v);
            e = (int)((bits >> 52) & 0x7FF) - 64;
        }
        int exp = e - 1022;
        bits &= ~(0x7FFL << 52);
        bits |= 1022L << 52;
        return (BitConverter.Int64BitsToDouble(bits), exp);
    }
}
