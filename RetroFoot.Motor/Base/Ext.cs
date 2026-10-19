using System;
using System.Buffers.Binary;
using System.Numerics;

namespace RetroFoot.Motor.Base;

// Um real Extended (10 bytes) da FPU 8087, como o Delphi 1 o guarda: 64 bits de mantissa
// com o 1 explicito e 15 bits de expoente (vies 16383) com o sinal no bit de cima. As
// contas sao feitas com 64 bits de mantissa e arredondamento ao par mais proximo (a FPU do
// jogo usa precisao estendida), o que da o mesmo resultado bit a bit para +, -, x, / e
// raiz. (Traduzido de motor/novojogo/ext.go; la com big.Float de 64 bits, aqui com inteiros
// grandes e um unico arredondamento.)
public readonly record struct Ext(ulong Mant, ushort Exp)
{
    // valor = (-1)^sinal x Mant x 2^(Exp - 16383 - 63)
    internal bool Negativo => (Exp & 0x8000) != 0;
    internal int E2 => (Exp & 0x7FFF) - 16383 - 63;

    // ---------------------------------------------------------------- arredondamento
    // valor exato = (-1)^neg x mag x 2^e2 (mag > 0), mais um "resto" (sticky) abaixo de mag
    internal static Ext Arredondar(bool neg, BigInteger mag, int e2, bool resto = false)
    {
        if (mag.IsZero) return default;
        long bits = (long)mag.GetBitLength();
        if (bits > 64)
        {
            int s = (int)(bits - 64);
            var inteiro = mag >> s;
            var sobra = mag - (inteiro << s);
            var meio = BigInteger.One << (s - 1);
            int c = sobra.CompareTo(meio);
            bool sobe = c > 0 || (c == 0 && (resto || !inteiro.IsEven));
            if (c == 0 && resto) sobe = true;
            if (sobe) inteiro += 1;
            e2 += s;
            if (inteiro.GetBitLength() > 64) { inteiro >>= 1; e2++; }
            mag = inteiro;
        }
        else if (bits < 64)
        {
            int s = (int)(64 - bits);
            mag <<= s;
            e2 -= s;
        }
        ushort ex = (ushort)(e2 + 63 + 16383);
        if (neg) ex |= 0x8000;
        return new Ext((ulong)mag, ex);
    }

    static (bool Neg, BigInteger Mag, int E2) Partes(Ext x) => (x.Negativo, new BigInteger(x.Mant), x.E2);

    // ---------------------------------------------------------------- conversoes
    // FILD
    public static Ext Int(long i) => i == 0 ? default : Arredondar(i < 0, BigInteger.Abs(new BigInteger(i)), 0);

    // um double exato (as constantes de 4 bytes, FLD dword, e os resultados em float64)
    public static Ext F64(double d)
    {
        if (d == 0) return default;
        long bits = BitConverter.DoubleToInt64Bits(d);
        bool neg = bits < 0;
        int e = (int)((bits >> 52) & 0x7FF);
        long m = bits & 0xFFFFFFFFFFFFFL;
        if (e == 0) e = 1; else m |= 1L << 52;
        return Arredondar(neg, new BigInteger(m), e - 1075);
    }

    public static Ext F32(float f) => F64(f);

    public static Ext Bytes(ReadOnlySpan<byte> b) => new(BinaryPrimitives.ReadUInt64LittleEndian(b), BinaryPrimitives.ReadUInt16LittleEndian(b[8..]));

    public byte[] Bytes()
    {
        var b = new byte[10];
        BinaryPrimitives.WriteUInt64LittleEndian(b, Mant);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(8), Exp);
        return b;
    }

    // constante de 10 bytes vista no codigo, da palavra de cima para a de baixo
    public static Ext Const(ushort w4, ushort w3, ushort w2, ushort w1, ushort w0) =>
        new((ulong)w3 << 48 | (ulong)w2 << 32 | (ulong)w1 << 16 | w0, w4);

    // so para mostrar e para as funcoes transcendentes
    public double Float64()
    {
        if (Mant == 0) return 0;
        double v = Mant * Math.Pow(2, E2);
        return Negativo ? -v : v;
    }

    // ---------------------------------------------------------------- aritmetica
    public Ext Add(Ext y) => Soma(this, y, false);
    public Ext Sub(Ext y) => Soma(this, y, true);

    static Ext Soma(Ext x, Ext y, bool sub)
    {
        if (y.Mant == 0) return x.Mant == 0 ? default : x;
        if (x.Mant == 0) return sub ? new Ext(y.Mant, (ushort)(y.Exp ^ 0x8000)) : y;
        var (nx, mx, ex) = Partes(x);
        var (ny, my, ey) = Partes(y);
        if (sub) ny = !ny;
        int e = Math.Min(ex, ey);
        var a = mx << (ex - e);
        var b = my << (ey - e);
        if (nx) a = -a;
        if (ny) b = -b;
        var r = a + b;
        if (r.IsZero) return default;
        return Arredondar(r.Sign < 0, BigInteger.Abs(r), e);
    }

    public Ext Mul(Ext y)
    {
        if (Mant == 0 || y.Mant == 0) return default;
        return Arredondar(Negativo != y.Negativo, new BigInteger(Mant) * y.Mant, E2 + y.E2);
    }

    // x / y: a rotina de divisao da biblioteca (25:04b2), que no fim usa FDIV
    public Ext Div(Ext y)
    {
        if (y.Mant == 0) throw new DivideByZeroException();
        if (Mant == 0) return default;
        const int extra = 130;
        var q = BigInteger.DivRem(new BigInteger(Mant) << extra, new BigInteger(y.Mant), out var resto);
        return Arredondar(Negativo != y.Negativo, q, E2 - y.E2 - extra, !resto.IsZero);
    }

    // FSQRT (25:1285)
    public Ext Sqrt()
    {
        if (Mant == 0) return this;
        if (Negativo) throw new ArithmeticException("raiz de negativo");
        // valor = M x 2^e; para expoente par: sqrt(M x 2^(e + 2k)) x 2^(-k)
        int e = E2;
        var m = new BigInteger(Mant);
        const int extra = 140;            // bits a mais (par)
        int k = extra;
        if (((e - k) & 1) != 0) k++;
        var n = m << k;
        var r = Raiz(n);
        bool resto = r * r != n;
        return Arredondar(false, r, (e - k) / 2, resto);
    }

    static BigInteger Raiz(BigInteger n)
    {
        if (n.IsZero) return n;
        var x = BigInteger.One << (int)((n.GetBitLength() + 1) / 2);
        while (true)
        {
            var y = (x + n / x) >> 1;
            if (y >= x) return x;
            x = y;
        }
    }

    // FCOM
    public int Cmp(Ext y)
    {
        var a = Escalar(this, out int ea);
        var b = Escalar(y, out int eb);
        int e = Math.Min(ea, eb);
        return (a << (ea - e)).CompareTo(b << (eb - e));
    }

    static BigInteger Escalar(Ext x, out int e)
    {
        e = x.Mant == 0 ? 0 : x.E2;
        var m = new BigInteger(x.Mant);
        return x.Negativo ? -m : m;
    }

    // Round do Delphi (25:1241): FRNDINT ao par mais proximo e FISTP em 32 bits
    public long Round()
    {
        if (Mant == 0) return 0;
        int e = E2;
        ulong v;
        if (e >= 0) v = Mant << e;
        else if (e <= -65) v = 0;
        else
        {
            int s = -e;
            ulong inteiro, resto, meio;
            if (s == 64) { inteiro = 0; resto = Mant; meio = 1UL << 63; }
            else { inteiro = Mant >> s; resto = Mant & ((1UL << s) - 1); meio = 1UL << (s - 1); }
            if (resto > meio || (resto == meio && (inteiro & 1) == 1)) inteiro++;
            v = inteiro;
        }
        return Negativo ? -(long)v : (long)v;
    }

    // Trunc do Delphi (25:1220): FISTP com arredondamento para zero
    public long Trunc()
    {
        if (Mant == 0) return 0;
        int e = E2;
        ulong v = 0;
        if (e >= 0) v = Mant << e;
        else if (e > -64) v = Mant >> -e;
        return Negativo ? -(long)v : (long)v;
    }

    // Real de 6 bytes em Extended (25:11af), exato
    public static Ext DeReal48(ReadOnlySpan<byte> b)
    {
        if (b[0] == 0) return default;
        ulong m = 0;
        for (int i = 5; i >= 1; i--) m = m << 8 | b[i];
        var x = new Ext((m & ((1UL << 39) - 1)) << 24 | 1UL << 63, (ushort)(b[0] + 0x3F7E));
        if ((m & (1UL << 39)) != 0) x = x with { Exp = (ushort)(x.Exp | 0x8000) };
        return x;
    }

    // Extended em Real de 6 bytes como 25:11d9: os 40 bits de cima da mantissa,
    // arredondados pelo bit seguinte (meio para cima); expoente pequeno demais da zero
    public byte[] Real48()
    {
        var r = new byte[6];
        int e = (Exp & 0x7FFF) - 0x3F7E;
        if (Mant == 0 || e <= 0) return r;
        ulong m = Mant >> 24;
        if ((Mant & (1UL << 23)) != 0)
        {
            m++;
            if (m == 1UL << 40) { m = 1UL << 39; e++; }
        }
        if (e > 255) throw new OverflowException("Real48: estouro (runtime error 8 no jogo)");
        m &= (1UL << 39) - 1;
        if (Negativo) m |= 1UL << 39;
        r[0] = (byte)e;
        for (int i = 1; i <= 5; i++) { r[i] = (byte)m; m >>= 8; }
        return r;
    }

    // a potencia do jogo (14:1194): Exp(expoente x Ln(base)), com Ln e Exp da biblioteca
    // (FYL2X e F2XM1). Aqui as transcendentes sao calculadas em double (53 bits): o
    // resultado pode diferir do original no ultimo bit do Extended (como no Go)
    public static Ext Power(Ext @base, Ext expoente)
    {
        double ln = Math.Log(@base.Float64());
        var p = expoente.Mul(F64(ln));
        return F64(Math.Exp(p.Float64()));
    }

    public override string ToString() => Float64().ToString("R");
}

