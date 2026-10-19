using System;
using System.Collections.Generic;
using RetroFoot.Motor.Regras;
using Xunit;
using R = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.Motor.Testes;

public class RegrasTestes
{
    // calendario() de tools/divisoes/patch_divisoes.py (Berger escrito de outro jeito)
    static (int[][] Opp, int[][] Slot) CalendarioPython(int n)
    {
        int r = 2 * (n - 1), m = n - 1;
        var pares = new List<List<(int, int)>>();
        for (int rr = 0; rr < m; rr++)
        {
            var jogos = new List<(int, int)>();
            int a = rr, b = n - 1;
            jogos.Add(rr % 2 == 0 ? (a, b) : (b, a));
            for (int i = 1; i < n / 2; i++)
            {
                int x = (rr + i) % m, y = ((rr - i) % m + m) % m;
                jogos.Add(i % 2 == 1 ? (x, y) : (y, x));
            }
            pares.Add(jogos);
        }
        for (int rr = 0; rr < m; rr++)
        {
            var j = new List<(int, int)>();
            foreach (var (x, y) in pares[rr]) j.Add((y, x));
            pares.Add(j);
        }
        var opp = new int[n][]; var slot = new int[n][];
        for (int k = 0; k < n; k++) { opp[k] = new int[r]; slot[k] = new int[r]; }
        for (int rr = 0; rr < pares.Count; rr++)
            for (int g = 0; g < pares[rr].Count; g++)
            {
                var (h, a) = pares[rr][g];
                opp[h][rr] = a != 0 ? a : n;
                opp[a][rr] = h != 0 ? -h : -n;
                slot[h][rr] = 2 * g + 1;
                slot[a][rr] = 2 * g + 2;
            }
        return (opp, slot);
    }

    [Fact]
    public void Berger()
    {
        for (int n = 10; n <= 20; n += 2)
        {
            var rg = R.Launcher(n);
            var (opp, slot) = CalendarioPython(n);
            for (int k = 0; k < n; k++)
            {
                var (b, _) = rg.LugaresIniciais(k);
                for (int j = 1; j <= rg.Jornadas; j++)
                {
                    var (pos, fora, _) = rg.Adversario(k, j);
                    int o = opp[k][j - 1];
                    Assert.True((o < 0) == fora && Math.Abs(o) % n == pos, $"N={n} equipa {k} jornada {j}");
                    Assert.Equal(slot[k][j - 1], rg.Lugar(b, j));
                }
            }
        }
    }

    [Fact]
    public void Original()
    {
        var r = new R();
        Assert.True(r.N == 8 && r.Jornadas == 14 && r.Limite == 24 && r.LimiteVendaCPU == 21 && r.SobemDescem == 2 && r.MinimoEquipas == 34);
        Assert.Equal(new byte[] { 0x0A, 0xD7, 0xA3, 0x70, 0x3D, 0x0A, 0xD7, 0xA3, 0xF8, 0x3F }, r.PassoInflacao());
        for (short pos = 0; pos < 8; pos++) Assert.Equal(52 - 2 * pos, r.Escada(52, pos));
        var esperado = new Dictionary<int, string>
        {
            [10] = "1032547698badcfef73f", [12] = "0d86d060080d86d0f73f", [14] = "0b4a3a3e7d6d71b0f73f",
            [16] = "7084327a5bd6ea98f73f", [18] = "54ed8620ba53ed86f73f", [20] = "88f9e3dbb8cb72f1f63f",
        };
        foreach (var (n, h) in esperado) Assert.Equal(h, Convert.ToHexString(R.Launcher(n).PassoInflacao()).ToLowerInvariant());
        Assert.True(R.Launcher(20).SobemDescem == 4 && R.Launcher(14).SobemDescem == 2 && R.Launcher(20).MinimoEquipas == 84);
    }
}
