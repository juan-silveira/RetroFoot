using System;
using System.Numerics;
using RetroFoot.Motor.Base;

namespace RetroFoot.Motor.Partida;

// O erro de execucao que o Delphi da quando a FPU sinaliza operacao invalida (runtime
// error 207) ou divisao por zero (200). O original para o jogo.
public sealed class ErroPontoFlutuante(string motivo) : Exception("erro de ponto flutuante: " + motivo);

// Aritmetica de ponto flutuante como a FPU (x87) faz no jogo classico, para o motor da
// partida. +, -, x, / e raiz em Extended (Ext, 64 bits de mantissa, ao par mais
// proximo). Ln e Exp (runtime seg25:143f e seg25:14b9, FYL2X e F2XM1) sao calculados aqui
// em ponto fixo de 256 bits e arredondados uma vez para 64 (no Go: big.Float de 128 bits).
// A x87 nao garante o ultimo bit dessas instrucoes; a igualdade ai e aproximada (<= 1 ulp
// do Extended), e no motor isso so entra em comparacoes com inteiros sorteados (gol) e num
// arredondamento (publico). (Traduzido de motor/partida/ext.go.)
internal static class Fpu
{
    const int F = 256;   // bits de fracao do ponto fixo
    static readonly BigInteger Um = BigInteger.One << F;

    public static Ext Int(long v) => Ext.Int(v);
    public static Ext F32(float v) => Ext.F32(v);
    public static Ext Bits(ulong mant, ushort expoente) => new(mant, expoente);

    public static Ext Soma(Ext a, Ext b) => a.Add(b);
    public static Ext Subt(Ext a, Ext b) => a.Sub(b);
    public static Ext Mult(Ext a, Ext b) => a.Mul(b);

    public static Ext Divide(Ext a, Ext b)
    {
        if (b.Mant == 0) throw new ErroPontoFlutuante("divisão por zero");
        return a.Div(b);
    }

    // FSQRT (seg25:1285)
    public static Ext Raiz(Ext a)
    {
        if (a.Mant != 0 && a.Negativo) throw new ErroPontoFlutuante("raiz de negativo");
        return a.Sqrt();
    }

    // Round do Delphi (seg25:1241): FRNDINT ao par e FISTP de 32 bits
    public static int Arredondar(Ext x) => (int)x.Round();

    // seg25:11af: Real de 6 bytes para Extended (exato)
    public static Ext DeReal48(ReadOnlySpan<byte> b) => Ext.DeReal48(b);

    // seg25:11d9: Extended para Real de 6 bytes
    public static byte[] ParaReal48(Ext x)
    {
        try { return x.Real48(); }
        catch (OverflowException) { throw new ErroPontoFlutuante("estouro ao converter para Real"); }
    }

    // ---------------------------------------------------------------- Ln, Exp, Power
    static readonly BigInteger Ln2 = Atanh2(Um / 3);                  // ln 2 = 2 atanh(1/3)
    static readonly Ext Ln2Ext = DeFixo(Ln2);                         // FLDLN2
    static readonly Ext Log2eExt = DeFixo((Um << F) / Ln2);           // FLDL2E

    // 2 atanh(s) = ln((1+s)/(1-s)) pela serie, em ponto fixo
    static BigInteger Atanh2(BigInteger s)
    {
        var s2 = s * s >> F;
        var termo = s;
        var total = s;
        for (int k = 3; k < 8000; k += 2)
        {
            termo = termo * s2 >> F;
            if (termo.IsZero) break;
            var t = termo / k;
            if (t.IsZero) break;
            total += t;
        }
        return total * 2;
    }

    static Ext DeFixo(BigInteger v) => v.IsZero ? default : Ext.Arredondar(v.Sign < 0, BigInteger.Abs(v), -F);

    // o valor de um Ext em ponto fixo (trunca os bits abaixo de 2^-F)
    static BigInteger Fixo(Ext x)
    {
        var m = new BigInteger(x.Mant);
        int s = x.E2 + F;
        var v = s >= 0 ? m << s : m >> -s;
        return x.Negativo ? -v : v;
    }

    // ln(x), x > 0, em ponto fixo: x = m 2^e com m em [0,5; 1) e ln m = 2 atanh((m-1)/(m+1))
    static BigInteger LnFixo(Ext x)
    {
        int e = x.E2 + 64;
        var m = new BigInteger(x.Mant) << (F - 64);
        var s = ((m - Um) << F) / (m + Um);
        return Atanh2(s) + e * Ln2;
    }

    // e^y em ponto fixo (devolve a mantissa e o expoente de 2): y = k ln2 + r
    static (BigInteger Mant, int K) ExpFixo(BigInteger y)
    {
        var k = (int)BigInteger.Divide(y + (y.Sign >= 0 ? Ln2 / 2 : -Ln2 / 2), Ln2);
        var r = y - k * Ln2;
        var total = Um;
        var termo = Um;
        for (int n = 1; n < 4000; n++)
        {
            termo = termo * r / n >> F;
            if (termo.IsZero) break;
            total += termo;
        }
        return (total, k);
    }

    // Ln do Delphi (seg25:1295 -> 143f): FLDLN2 e FYL2X, ou seja ln2 x log2(x), arredondado
    // uma vez. Ln de zero ou de negativo e operacao invalida na FPU.
    public static Ext Ln(Ext x)
    {
        if (x.Mant == 0 || x.Negativo) throw new ErroPontoFlutuante("Ln de valor não positivo");
        if (x == Um1) return default;                      // FYL2X de 1 da 0 exato
        var log2 = (LnFixo(x) << F) / Ln2;                 // log2(x) em ponto fixo
        var p = new BigInteger(Ln2Ext.Mant) * log2;        // ln2Ext x log2, exato
        return p.IsZero ? default : Ext.Arredondar(p.Sign < 0, BigInteger.Abs(p), Ln2Ext.E2 - F);
    }

    // Exp do Delphi (seg25:1299 -> 14b9): t = x log2(e) (arredondado), depois 2^t
    public static Ext Exp(Ext x)
    {
        var t = x.Mul(Log2eExt);
        var y = Fixo(t) * Ln2 >> F;                        // t ln2
        var (mant, k) = ExpFixo(y);
        return Ext.Arredondar(false, mant, k - F);
    }

    // seg14:1194: Power(base, expoente) = Exp(expoente x Ln(base))
    static readonly Ext Um1 = Ext.F32(1);

    // Power como o Go faz na rodada e no mercado: Ln e Exp corretamente arredondados para o
    // Extended (o resultado correto mais proximo; o 8087 erra no maximo 1 no ultimo bit).
    // Base <= 0 da 0 (no original, erro de ponto flutuante).
    public static Ext LnExato(Ext x) =>
        x.Mant == 0 || x.Negativo ? throw new ErroPontoFlutuante("Ln de valor não positivo") : x == Um1 ? default : DeFixo(LnFixo(x));

    public static Ext ExpExato(Ext x)
    {
        var (mant, k) = ExpFixo(Fixo(x));
        return Ext.Arredondar(false, mant, k - F);
    }

    public static Ext PotenciaExata(Ext @base, Ext expoente) =>
        @base.Mant == 0 || @base.Negativo ? default : ExpExato(LnExato(@base).Mul(expoente));

    // No original, base <= 0 e o Ln de 0 ou de negativo: "Invalid floating point operation" e
    // o jogo para. Acontece no Q do gol quando a equipa fica sem medios e sem avancados em
    // campo (ln(1 + 0) = 0) e no publico com moral 0. Aqui da 0, como o PotenciaExata; com
    // base positiva nada muda.
    public static Ext Potencia(Ext @base, Ext expoente) =>
        @base.Mant == 0 || @base.Negativo ? default : Exp(expoente.Mul(Ln(@base)));
}
