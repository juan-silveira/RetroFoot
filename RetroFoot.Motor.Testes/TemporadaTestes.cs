using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Save;
using RetroFoot.Motor.Temporada;
using Xunit;
using Xunit.Abstractions;
using TRegras = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.Motor.Testes;

// (Traduzido de motor/temporada/temporada_test.go.)
public class TemporadaTestes(ITestOutputHelper saida)
{
    // so os do jogo original (5 divisoes, as 4 de cima do mesmo tamanho)
    static Dictionary<string, Jogo98> Saves()
    {
        var r = new Dictionary<string, Jogo98>();
        foreach (var a in Arquivos.Saves())
        {
            Jogo98 s;
            try { s = Jogo98.Ler(File.ReadAllBytes(a)); } catch { continue; }
            if (s.Divisoes.Count != 5 || s.Divisoes.Take(4).Any(d => d.Equipas.Count != s.Divisoes[0].Equipas.Count)) continue;
            r[a] = s;
        }
        return r;
    }

    [Fact]
    public void TacaNaJornada()
    {
        foreach (var (n, js) in new[] { (45, new[] { 4, 6, 8, 10, 12, 14 }), (32, new[] { 6, 8, 10, 12, 14 }), (128, new[] { 2, 4, 6, 8, 10, 12, 14 }), (65, new[] { 2, 4, 6, 8, 10, 12, 14 }) })
            Assert.Equal(js, Enumerable.Range(1, 7).Select(i => 2 * i).Where(j => Epoca.TacaNaJornada(TRegras.Original, n, j)).ToArray());
    }

    [Fact]
    public void Fases()
    {
        foreach (var (n, p, jogos) in new[] { (45, 32, 13), (32, 16, 16), (2, 1, 1), (3, 2, 1), (82, 64, 18), (33, 32, 1) })
            Assert.Equal((p, jogos), Epoca.TamanhoDaFase(n));
        Assert.Equal("32 avos de final", Epoca.NomeDaFase(32));
        Assert.Equal("FINAL", Epoca.NomeDaFase(1));
        Assert.Equal("Meias-finais", Epoca.NomeDaFase(3));
    }

    // as vivas na Taca sao as das fases ja jogadas
    [Fact]
    public void VivasNaTaca()
    {
        foreach (var (a, s) in Saves())
        {
            if (s.Cabecalho.G271E != 0 && s.Cabecalho.G271A != 0) continue;
            int j = s.Cabecalho.G271C, n = s.Equipas.Count, vivas = n;
            var rg = TRegras.DoSave(s, TRegras.Original);
            for (int k = 2; k < j; k += 2)
                if (Epoca.TacaNaJornada(rg, n, k) && vivas > 1) vivas = Epoca.TamanhoDaFase(vivas).P;
            Assert.True(s.OutrasEquipas.Count == vivas, $"{Path.GetFileName(a)}: {s.OutrasEquipas.Count} vivas, quer {vivas}");
        }
    }

    // num save da 1a jornada sem jogos: +0x22 e uma linha de DS:17C8 e o calendario e o de 3:1d20
    [Fact]
    public void CalendarioDaPrimeiraJornada()
    {
        int n = 0;
        foreach (var (a, s) in Saves())
        {
            if (s.Cabecalho.G271C != 1 || s.Equipas.Any(e => e.Calendario.Any(c => (short)c.W0A >= 0))) continue;
            n++;
            var rg = TRegras.DoSave(s, TRegras.Original);
            for (int d = 0; d < s.Divisoes.Count; d++)
            {
                var div = s.Divisoes[d];
                if (d == Epoca.Distrital)
                {
                    Assert.All(div.Equipas, id => Assert.Empty(MercadoJogo.Equipa(s, id)!.Calendario));
                    continue;
                }
                var inicio = new List<ushort>(new ushort[rg.N]);
                var pos = new Dictionary<ushort, int>();
                foreach (var id in div.Equipas)
                {
                    var e = MercadoJogo.Equipa(s, id)!;
                    int idx = -1;
                    for (int r = 0; r < rg.N; r++)
                        if (rg.LugaresIniciais(r).Bloco.AsSpan(0, 14).SequenceEqual(e.B16.AsSpan(0x22 - 0x16, 14))) idx = r;
                    Assert.True(idx >= 0 && inicio[idx] == 0, $"{Path.GetFileName(a)}: equipa {id}");
                    inicio[idx] = id; pos[id] = idx;
                }
                foreach (var id in div.Equipas)
                {
                    var e = MercadoJogo.Equipa(s, id)!;
                    var q = Epoca.Calendario(rg, pos[id], inicio);
                    Assert.Equal(q.Count, e.Calendario.Count);
                    for (int i = 0; i < q.Count; i++)
                    {
                        var (x, y) = (q[i], e.Calendario[i]);
                        Assert.True(x.W04 == y.W04 && x.W08 == y.W08 && x.W0A == y.W0A && x.W0C == y.W0C && x.W0E == y.W0E && x.D10 == y.D10 && x.B06 == y.B06 && x.B07 == y.B07,
                            $"{Path.GetFileName(a)}: equipa {id} entrada {i}");
                    }
                    Assert.All(e.B16.Take(0x22 - 0x16), b => Assert.Equal(0, b));
                    Assert.Equal(0, e.B16[0x30 - 0x16]);
                }
            }
        }
        saida.WriteLine($"{n} saves conferidos");
    }

    [Fact]
    public void NovaTemporadaRefaz()
    {
        foreach (var (a, s) in Saves())
        {
            if (s.Cabecalho.G271C != 1) continue;
            var tp = new Epoca(s, new Aleatorio(1));
            uint semente = tp.G.Semente;
            tp.NovaTemporada();
            Assert.Equal(semente, tp.G.Semente);
            Assert.Equal(s.Equipas.Count, s.OutrasEquipas.Count);
            Assert.All(s.Equipas.SelectMany(e => e.Jogadores), p => Assert.Equal(new byte[3], p.B14));
        }
    }

    // fim de temporada: tamanhos mantidos, K sobem e K descem, contadores, campeoes, ano, premios
    [Fact]
    public void FimDeTemporada()
    {
        int n = 0;
        foreach (var (a, s) in Saves())
        {
            if (s.OutrasEquipas.Count < 1) continue;
            n++;
            var tp = new Epoca(s, new Aleatorio(12345));
            var div0 = s.Divisoes[0].Equipas.ToList();
            var div3 = s.Divisoes[3].Equipas.ToList();
            var dist = s.Divisoes[4].Equipas.ToList();
            var tam = s.Divisoes.Select(d => d.Equipas.Count).ToList();
            var camp = MercadoJogo.Equipa(s, div0[0])!;
            ushort c1 = (ushort)camp.DC1;
            int din = (int)camp.Dinheiro;
            int ano = s.Cabecalho.Ano, ncamp = s.Campeoes.Count;
            var r = tp.FimDeTemporada();
            Assert.Equal(tam, s.Divisoes.Select(d => d.Equipas.Count).ToList());
            int N = tp.Regras.N, K = tp.Regras.SobemDescem;
            Assert.Equal(div0.Skip(N - K), s.Divisoes[1].Equipas.Skip(N - 2 * K).Take(K));
            Assert.Equal(div3.Skip(N - K), s.Divisoes[4].Equipas.Skip(dist.Count - K));
            Assert.Equal(K, r.Subiram[4].Distinct().Count());
            Assert.Equal(c1 + 1, (ushort)camp.DC1);
            Assert.Equal(ano + 1, s.Cabecalho.Ano);
            Assert.Equal(Math.Min(ncamp + 1, Epoca.LimiteCampeoes), s.Campeoes.Count);
            Assert.Equal((ushort)ano, s.Campeoes[^1].W[0]);
            Assert.Equal(camp.ID, s.Campeoes[^1].W[1]);
            Assert.True((int)camp.Dinheiro - din >= Moeda.DoSave(s).PremioCampeao);
        }
        saida.WriteLine($"{n} saves");
    }
}
