using System;

namespace RetroFoot.Motor.Regras;

// O calendario do campeonato de cada divisao. Com 8 equipas (o original) sao duas tabelas
// fixas do segmento de dados, indexadas pela posicao da equipa na divisao quando a
// temporada comecou: DS:1838 (adversario de cada jornada) e DS:17C8 (o lugar da equipa na
// lista de jogos de cada jornada, copiado para +0x22 da equipa por seg03:38a9). Com N > 8
// (launcher) e o metodo de Berger com o returno espelhado, calculado na hora
// (tools/divisoes/calend.asm, nucleo.inc, helper11.asm).
public readonly partial record struct Regras
{
    // DS:17C8: 8 linhas de 14 bytes, uma por posicao da equipa no comeco da temporada
    static readonly byte[][] TabelaLugar =
    {
        new byte[] { 3, 4, 5, 8, 2, 6, 7, 4, 3, 6, 7, 1, 5, 8 },
        new byte[] { 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2 },
        new byte[] { 4, 5, 8, 1, 7, 3, 6, 3, 6, 7, 2, 8, 4, 5 },
        new byte[] { 5, 8, 2, 7, 6, 4, 3, 6, 7, 1, 8, 5, 3, 4 },
        new byte[] { 8, 1, 7, 6, 3, 5, 4, 7, 2, 8, 5, 4, 6, 3 },
        new byte[] { 2, 7, 6, 3, 4, 8, 5, 1, 8, 5, 4, 3, 7, 6 },
        new byte[] { 7, 6, 3, 4, 5, 1, 8, 8, 5, 4, 3, 6, 2, 7 },
        new byte[] { 6, 3, 4, 5, 8, 7, 2, 5, 4, 3, 6, 7, 8, 1 },
    };

    // DS:1838: na jornada r o adversario e a equipa da posicao |b| mod 8; b < 0 = fora
    static readonly sbyte[][] TabelaAdversario =
    {
        new sbyte[] { 2, -7, 5, -3, -1, -4, 6, -2, 7, -5, 3, 1, 4, -6 },
        new sbyte[] { 5, -4, 3, -2, 8, -6, 7, -5, 4, -3, 2, -8, 6, -7 },
        new sbyte[] { -8, 6, -4, 1, 7, 3, -5, 8, -6, 4, -1, -7, -3, 5 },
        new sbyte[] { 7, -5, -1, 8, -6, -2, 4, -7, 5, 1, -8, 6, 2, -4 },
        new sbyte[] { -6, 1, 2, -7, 5, 8, -3, 6, -1, -2, 7, -5, -8, 3 },
        new sbyte[] { -1, 3, -8, 6, -4, -7, 2, 1, -3, 8, -6, 4, 7, -2 },
        new sbyte[] { 4, -2, 7, -5, 3, 1, -8, -4, 2, -7, 5, -3, -1, 8 },
        new sbyte[] { -3, 8, -6, 4, -2, 5, -1, 3, -8, 6, -4, 2, -5, 1 },
    };

    // o "nucleo" de nucleo.inc: na jornada rr (0 a R-1), a equipa k (0 a N-1) joga contra
    // adv, no jogo g da jornada, e casa diz se e mandante
    static (int Adv, int G, bool Casa) Berger(int n, int rr, int k)
    {
        int m = n - 1, h = n / 2 - 1;
        int adv, g;
        bool casa, returno = false;
        if (rr >= m) { rr -= m; returno = true; }
        if (k == n - 1) { adv = rr; g = 0; casa = (rr & 1) == 1; }
        else if (k == rr) { adv = n - 1; g = 0; casa = (rr & 1) == 0; }
        else
        {
            int d = k - rr;
            if (d < 0) d += m;
            if (d <= h)
            {
                g = d; casa = (d & 1) == 1;
                adv = rr - d;
                if (adv < 0) adv += m;
            }
            else
            {
                g = m - d; casa = (g & 1) == 0;
                adv = rr + g;
                if (adv >= m) adv -= m;
            }
        }
        if (returno) casa = !casa;
        return (adv, g, casa);
    }

    // a entrada da jornada j (1 a R) do calendario da equipa na posicao idx da divisao
    // (seg03:1d20): a posicao do adversario e se joga fora; Ok = false sem linha na tabela
    public (int Pos, bool Fora, bool Ok) Adversario(int idx, int j)
    {
        if (N == 8)
        {
            if (idx < 0 || idx >= TabelaAdversario.Length) return (0, false, false);
            int b = TabelaAdversario[idx][j - 1];
            bool fora = b < 0;
            if (b < 0) b = -b;
            return (b % 8, fora, true);
        }
        var (adv, _, casa) = Berger(N, j - 1, idx);
        return (adv, !casa, true);
    }

    // os 14 bytes que seg03:38a9 poe em +0x22 da equipa na posicao idx no comeco da
    // temporada: a linha idx de DS:17C8 (8 equipas); com N > 8, so o numero da equipa
    public (byte[] Bloco, bool Ok) LugaresIniciais(int idx)
    {
        var b = new byte[14];
        if (N == 8)
        {
            if (idx < 0 || idx >= TabelaLugar.Length) return (b, false);
            return ((byte[])TabelaLugar[idx].Clone(), true);
        }
        b[0] = (byte)idx;
        return (b, true);
    }

    // o lugar da equipa na lista de jogos da jornada j (seg11:1a61): com 8 equipas o byte
    // +0x21 + j; com N > 8, 2g + 1 para o mandante e 2g + 2 para o visitante
    public byte Lugar(ReadOnlySpan<byte> bloco, int j)
    {
        if (N == 8) return bloco[j - 1];
        var (_, g, casa) = Berger(N, j - 1, bloco[0]);
        int l = 2 * g + 1;
        if (!casa) l++;
        return (byte)l;
    }
}
