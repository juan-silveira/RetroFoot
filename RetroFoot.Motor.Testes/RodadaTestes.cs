using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;
using Xunit;
using Xunit.Abstractions;
using static RetroFoot.Motor.Rodada.Campos;
using Equipa = RetroFoot.Motor.Save.Equipa;
using TRegras = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.Motor.Testes;

// (Traduzido de motor/rodada/*_test.go.)
public class RodadaTestes(ITestOutputHelper saida)
{
    static Dictionary<string, Jogo98> SavesReais()
    {
        var r = new Dictionary<string, Jogo98>();
        foreach (var a in Arquivos.Saves())
            try { r[a] = Jogo98.Ler(File.ReadAllBytes(a)); } catch { }
        return r;
    }

    static Ext X(double v) => Ext.F64(v);

    [Fact]
    public void ConversoesNosSaves()
    {
        int n = 0;
        foreach (var s in SavesReais().Values)
        {
            Assert.Equal(s.Cabecalho.Real2B46, Ext.Bytes(s.Cabecalho.Real2B46).Bytes());
            foreach (var e in s.Equipas)
            {
                Assert.Equal(e.Moral, Ext.DeReal48(e.Moral).Real48());
                Assert.Equal(e.EstadioReal, Ext.DeReal48(e.EstadioReal).Real48());
                Assert.Equal(e.B79, Ext.Bytes(e.B79).Bytes());
                n++;
            }
        }
        saida.WriteLine($"{n} equipas");
    }

    [Fact]
    public void ParaReal48Arredonda()
    {
        Assert.Equal(new byte[] { 0x81, 1, 0, 0, 0, 0 }, X(1).Add(X(Math.ScaleB(1, -40))).Real48());
        Assert.Equal(new byte[] { 0x81, 0, 0, 0, 0, 0 }, X(1).Add(X(Math.ScaleB(1, -41))).Real48());
    }

    [Fact]
    public void Moral()
    {
        var e = new Equipa { Moral = new byte[] { 0x81, 0, 0, 0, 0, 0 } };
        Mundo.MudarMoral(e, Mundo.Moral2);
        double v = Reais.Real48(e.Moral);
        Assert.True(Math.Abs(v - 1.15) < 1e-9, v.ToString());
        Mundo.MudarMoral(e, Mundo.Moral0);
        Assert.True(Math.Abs(Reais.Real48(e.Moral) - 0.85 * v) < 1e-9);
    }

    static Equipa EquipaTeste(ushort id, int[] forcas, short[] v, byte[] pos)
    {
        var e = new Equipa { ID = id };
        for (int i = 0; i < forcas.Length; i++)
        {
            var j = new Jogador { ID = (ushort)(id * 100 + i), Forca = (ushort)forcas[i], Posicao = pos[i] };
            SetJgW(j, JgContadorV, v[i]);
            e.Jogadores.Add(j);
        }
        return e;
    }

    // seg03:4a41: Random(2) so para goleiros com v > 0 e para todos com v < 0; limites B+-5, 1 e 50
    [Fact]
    public void Evolucao()
    {
        var e = EquipaTeste(1, new[] { 30, 30, 30, 50, 10, 30, 1 }, new short[] { 0, 2, 3, -1, -4, 5, -2 }, new byte[] { 1, 0, 2, 3, 1, 1, 2 });
        var m = new Mundo(new Aleatorio(777));
        var re = new Aleatorio(777);
        var quero = new[] { 30, 30, 31, 50, 10, 30, 1 };
        if (re.Random(2) == 0) quero[1] = 31;
        if (re.Random(2) == 0) quero[3] = 49;
        re.Random(2);
        quero[5] = 31;
        re.Random(2);
        m.EvolucaoEquipa(e, 30);
        Assert.Equal(quero, e.Jogadores.Select(p => (int)p.Forca).ToArray());
        Assert.Equal(re.Semente, m.R.Semente);
    }

    // a lista de cada divisao no save esta na ordem da classificacao: ordenar nao muda nada
    [Fact]
    public void ClassificacaoNosSaves()
    {
        foreach (var (a, s) in SavesReais())
        {
            var m = new Mundo(s, 0);
            var antes = s.Divisoes.Take(4).Select(d => d.Equipas.ToList()).ToList();
            m.OrdenarClassificacao();
            for (int d = 0; d < antes.Count; d++) Assert.Equal(antes[d], s.Divisoes[d].Equipas);
        }
    }

    [Fact]
    public void EmOrdemClassificacao()
    {
        Equipa Mk(short pts, short gp, short gc)
        {
            var e = new Equipa();
            SetEqW(e, EqPontos, pts); SetEqW(e, EqGolosPro, gp); SetEqW(e, EqGolosContra, gc);
            return e;
        }
        Assert.True(Mundo.EmOrdemClassificacao(Mk(10, 5, 5), Mk(9, 20, 0)));
        Assert.False(Mundo.EmOrdemClassificacao(Mk(9, 20, 0), Mk(10, 5, 5)));
        Assert.True(Mundo.EmOrdemClassificacao(Mk(10, 8, 2), Mk(10, 9, 4)));
        Assert.True(Mundo.EmOrdemClassificacao(Mk(10, 9, 4), Mk(10, 6, 1)));
        Assert.False(Mundo.EmOrdemClassificacao(Mk(10, 6, 1), Mk(10, 9, 4)));
        Assert.True(Mundo.EmOrdemClassificacao(Mk(10, 6, 1), Mk(10, 6, 1)));
    }

    [Fact]
    public void Inflacao()
    {
        foreach (int ano in new[] { 2026, 2075 })
        {
            var s = new Jogo98();
            s.Cabecalho.Ano = ano;
            X(2).Bytes().CopyTo(s.Cabecalho.Real2B46, 0);
            var m = new Mundo(new Aleatorio(99), s);
            var re = new Aleatorio(99);
            double r1 = re.Real(), r2 = re.Real(), r3 = re.Real();
            double quero = ano % 100 >= 50 ? 2 + 0.01 * (r1 - r2 - r3) : 2 + 0.01 * (r1 + r2 - r3);
            m.AtualizarInflacao();
            Assert.True(Math.Abs(m.Inflacao.Float64() - quero) <= 1e-15);
        }
        var s2 = new Jogo98();
        X(9.999).Bytes().CopyTo(s2.Cabecalho.Real2B46, 0);
        s2.Cabecalho.Ano = 2000;
        var m2 = new Mundo(new Aleatorio(1), s2);
        for (int i = 0; i < 100; i++) m2.AtualizarInflacao();
        Assert.True(m2.Inflacao.Float64() <= 10);
    }

    [Fact]
    public void LimiteTaca()
    {
        foreach (var (j, q) in new (short, short)[] { (2, 64), (4, 32), (6, 16), (8, 8), (10, 4), (12, 2), (14, 1) })
            Assert.Equal(q, new Mundo(new Aleatorio(0)) { Jornada = j }.LimiteTaca());
        foreach (var (j, q) in new (short, short)[] { (2, 0), (4, 0), (6, 0), (8, -32768), (10, 16384), (26, 64), (38, 1) })
            Assert.Equal(q, new Mundo(new Aleatorio(0), null, TRegras.Launcher(20)) { Jornada = j }.LimiteTaca());
    }

    [Fact]
    public void PodeVender()
    {
        var e = new Equipa();
        for (int i = 0; i < 16; i++) e.Jogadores.Add(new Jogador { Posicao = (byte)(i % 4) });
        e.B83 = new byte[] { 1, 5, 5, 5, 0, 16 };
        Assert.Equal(5, Mundo.PodeVender(e, e.Jogadores[0]));
        Assert.Equal(0, Mundo.PodeVender(e, e.Jogadores[1]));
        e.Jogadores[1].W36 = 7;
        Assert.Equal(2, Mundo.PodeVender(e, e.Jogadores[1]));
        e.B83[5] = 11;
        Assert.Equal(1, Mundo.PodeVender(e, e.Jogadores[2]));
    }

    [Fact]
    public void FimDeJogo()
    {
        var s = new Jogo98();
        X(2).Bytes().CopyTo(s.Cabecalho.Real2B46, 0);
        s.Treinadores.Add(new Treinador { ID = 1, W0C = 3 });
        s.Treinadores.Add(new Treinador { ID = 2, W0C = 20 });
        var casa = EquipaTeste(10, new[] { 30, 30 }, new short[] { 0, 0 }, new byte[] { 0, 1 });
        var fora = EquipaTeste(20, new[] { 30, 30 }, new short[] { 0, 0 }, new byte[] { 0, 1 });
        casa.Treinador = 1; fora.Treinador = 2;
        casa.Moral = new byte[] { 0x81, 0, 0, 0, 0, 0 }; fora.Moral = new byte[] { 0x81, 0, 0, 0, 0, 0 };
        casa.Jogadores[0].Salario = 1000; fora.Jogadores[0].Salario = 500;
        casa.Jogadores[1].Suspensao = 2;
        fora.D6D = 1000000;
        casa.Calendario = new() { new EntradaCalendario { W0A = 1, W0C = 0 }, new EntradaCalendario { W0A = 0xFFFF, W0C = 0xFFFF } };
        fora.Calendario = new() { new EntradaCalendario { W0A = 0xFFFF, W0C = 0xFFFF } };
        s.Equipas.Add(casa); s.Equipas.Add(fora);
        s.Arbitros.Add(new Arbitro { ID = 5 });
        var m = new Mundo(s, 1);
        SetEqW(casa, EqGolosJogo, 3);
        SetEqW(fora, EqGolosJogo, 1);
        var j = new Jogo { Taca = 0, Casa = 10, Fora = 20, D40 = 12345, Arbitro = 5 };
        X(7).Bytes().CopyTo(j.B36, 0);
        m.FimDeJogo(j);
        Assert.Equal(new short[] { 1, 1, 3, 0 }, new[] { EqW(casa, EqVitorias), EqW(fora, EqDerrotas), EqW(casa, EqPontos), EqW(fora, EqPontos) });
        Assert.Equal(new short[] { 3, 1, 1, 3 }, new[] { EqW(casa, EqGolosPro), EqW(casa, EqGolosContra), EqW(fora, EqGolosPro), EqW(fora, EqGolosContra) });
        Assert.Equal(2u, s.Treinadores[0].D0E); Assert.Equal(0u, s.Treinadores[1].D0E);
        Assert.Equal(4, s.Treinadores[0].W0C); Assert.Equal(21, s.Treinadores[1].W0C);
        Assert.Equal(12345 * 7 - 1000, (int)casa.Dinheiro);
        Assert.Equal(12345 * 7, Mundo.Saldo(casa, Mundo.BalBilheteira));
        Assert.Equal(-1000, Mundo.Saldo(casa, Mundo.BalSalarios));
        Assert.Equal(-500 - 50000, (int)fora.Dinheiro);
        Assert.Equal(-50000, Mundo.Saldo(fora, Mundo.BalJuros));
        Assert.Equal(1, casa.Jogadores[1].Suspensao);
        Assert.Equal((ushort)3, casa.Calendario[1].W0A); Assert.Equal((ushort)1, casa.Calendario[1].W0C);
        Assert.Equal(12345u, casa.Calendario[1].D10); Assert.Equal((ushort)1, fora.Calendario[0].W0A);
        Assert.Single(casa.Eventos2);
        Assert.Equal((ushort)20, casa.Eventos2[0].W0A); Assert.Equal(3, casa.Eventos2[0].B04); Assert.Equal(1, casa.Eventos2[0].B05);
        Assert.Equal(1, s.Arbitros[0].B06); Assert.Equal(0, j.Casa);
        Assert.True(Math.Abs(Reais.Real48(fora.Moral) - 0.85) < 1e-9);
    }

    [Fact]
    public void Pot()
    {
        Assert.Equal(8, Partida.Fpu.PotenciaExata(X(2), X(3)).Float64());
        Assert.True(Math.Abs(Partida.Fpu.PotenciaExata(Mundo.C1000, Mundo.C0_04).Float64() - Math.Pow(1000, 0.04)) <= 1e-15);
        Assert.True(Math.Abs(Partida.Fpu.PotenciaExata(X(37), Mercado.Constantes.C1_04).Float64() - Math.Pow(37, 1.04)) <= 1e-12);
    }

    // os saves gravados no meio de uma jornada tem os jogos criados por seg03:0875: o preco
    // do bilhete e a influencia do arbitro dependem so do save
    [Fact]
    public void PrecoEArbitroNosSaves()
    {
        int conferidos = 0, difPreco = 0;
        foreach (var (a, s) in SavesReais())
        {
            var m = new Mundo(s, 0);
            for (int i = 0; i < s.Jogos.Count; i++)
            {
                if (s.Jogos[i].Taca != 2) continue;
                var lista = new List<Equipa>();
                int k = i + 1;
                for (; k < s.Jogos.Count && s.Jogos[k].Taca != 2; k++) { lista.Add(m.Equipa(s.Jogos[k].Casa)!); lista.Add(m.Equipa(s.Jogos[k].Fora)!); }
                if (lista.Count == 0 || s.Jogos[i + 1].Taca != 0) continue;
                short somaF = 0;
                var somaX = X(0);
                foreach (var e in lista) { somaF += Mundo.MediaForca(e); somaX = somaX.Add(Ext.Bytes(e.B79)); }
                var cont = Ext.Int(lista.Count);
                var p = m.PrecoBilhete(somaX.Div(cont), 0, (short)Ext.Int(somaF).Div(cont).Round()).Bytes();
                for (int j = i; j < k; j++)
                {
                    if (!p.AsSpan().SequenceEqual(s.Jogos[j].B36)) difPreco++;
                    conferidos++;
                    var g = s.Jogos[j];
                    if (g.Arbitro == 0 || g.Casa == 0) continue;
                    var arb = s.Arbitros.First(x => x.ID == g.Arbitro);
                    Assert.Equal(g.W4C, (ushort)(Partida.PreJogo.InfluenciaDoArbitro(arb, m.Equipa(g.Casa)!) + 2));
                    Assert.Equal(g.W4E, (ushort)Partida.PreJogo.InfluenciaDoArbitro(arb, m.Equipa(g.Fora)!));
                }
                i = k - 1;
            }
        }
        saida.WriteLine($"{conferidos} jogos conferidos, {difPreco} com preco diferente");
        Assert.True(difPreco * 20 <= conferidos);
    }

    // uma jornada inteira a partir de cada save real (gols sorteados pelo gancho)
    [Fact]
    public void JornadaNosSaves()
    {
        int n = 0;
        foreach (var (a, s) in SavesReais())
        {
            if (s.Divisoes.Count < 5 || s.Divisoes[0].Equipas.Count != 8 || s.Cabecalho.G271C > 14) continue;
            var m = new Mundo(s, 12345);
            var jogosAntes = new Dictionary<ushort, int>();
            for (int d = 0; d < 4; d++)
                foreach (var id in s.Divisoes[d].Equipas)
                {
                    var e = m.Equipa(id)!;
                    jogosAntes[id] = EqW(e, EqVitorias) + EqW(e, EqEmpates) + EqW(e, EqDerrotas);
                }
            var emJogo = new HashSet<ushort>();
            m.G.Partida = (m, titulo, taca) =>
            {
                foreach (var j in m.S.Jogos)
                {
                    if (j.Casa == 0) continue;
                    emJogo.Add(j.Casa); emJogo.Add(j.Fora);
                    SetEqW(m.Equipa(j.Casa)!, EqGolosJogo, (short)m.R.Random(4));
                    SetEqW(m.Equipa(j.Fora)!, EqGolosJogo, (short)m.R.Random(3));
                }
                return false;
            };
            if (m.JornadaJogada) continue;
            m.JogarJornada();
            var nome = Path.GetFileName(a);
            Assert.Empty(s.Jogos);
            for (int d = 0; d < 4; d++)
            {
                var l = s.Divisoes[d].Equipas;
                for (int i = 0; i < l.Count; i++)
                {
                    var e = m.Equipa(l[i])!;
                    int depois = EqW(e, EqVitorias) + EqW(e, EqEmpates) + EqW(e, EqDerrotas);
                    if (emJogo.Contains(l[i])) Assert.True(depois == jogosAntes[l[i]] + 1, $"{nome}: equipa {l[i]}");
                    if (i > 0) Assert.True(Mundo.EmOrdemClassificacao(m.Equipa(l[i - 1])!, e), $"{nome}: divisao {d} pos {i}");
                }
            }
            var b = s.Gravar();
            Assert.Equal(b, Jogo98.Ler(b).Gravar());
            saida.WriteLine($"{nome}: jornada {m.Jornada} jogada, inflacao {m.Inflacao.Float64():F6}");
            n++;
        }
        Assert.True(n > 0);
    }

    // ---------- escalacao ----------

    [Fact]
    public void EmbaralharConta()
    {
        var r = new Aleatorio(12345);
        var l = new List<int> { 10, 20, 30, 40 };
        Listas.Embaralhar(r, l);
        Assert.Equal(new[] { 20, 40, 10, 30 }, l);
        Assert.Equal(0x5208c69du, r.Semente);
    }

    [Fact]
    public void EmbaralharVazio()
    {
        var r = new Aleatorio(7);
        Listas.Embaralhar(r, new List<int>());
        Assert.Equal(7u, r.Semente);
        Listas.Embaralhar(r, new List<int> { 5 });
        Assert.NotEqual(7u, r.Semente);
    }

    static Jogador EscJog(ushort id, byte pos, ushort forca, short golos)
    {
        var j = new Jogador { ID = id, Posicao = pos, Forca = forca, NomeCifrado = new byte[] { 0 } };
        SetJgW(j, JgGolos14, golos);
        return j;
    }

    [Fact]
    public void OrdenarForcaGolos()
    {
        var e = new Equipa { Jogadores = new() { EscJog(1, 1, 10, 0), EscJog(2, 1, 12, 1), EscJog(3, 2, 13, 0), EscJog(4, 3, 10, 0), EscJog(5, 0, 20, 0), EscJog(6, 2, 9, 4) } };
        Escalacao.OrdenarPorForcaGolos(e);
        Assert.Equal(new ushort[] { 5, 2, 3, 6, 1, 4 }, e.Jogadores.Select(j => j.ID).ToArray());
    }

    [Fact]
    public void FormacoesHabilitadas()
    {
        var js = new List<Jogador>();
        void Add(byte pos, int n) { for (int i = 0; i < n; i++) js.Add(EscJog((ushort)(js.Count + 1), pos, 10, 0)); }
        Add(0, 2); Add(1, 5); Add(2, 6); Add(3, 3);
        js[7].Suspensao = 1;
        js[15].JogosLesao = 2;
        var h = Escalacao.FormacoesHabilitadas(new Equipa { Jogadores = js });
        var cinza = new HashSet<string> { "3-3-4", "3-4-3", "4-2-4", "4-3-3", "5-2-3", "6-3-1", "6-4-0" };
        for (int i = 0; i < 12; i++) Assert.True(h[i] != cinza.Contains(Escalacao.Formacoes[i].Nome), Escalacao.Formacoes[i].Nome);
    }

    static bool Disp(Jogador j) => j.JogosLesao == 0 && j.Suspensao == 0;

    static (int[] Tit, int[] Res, int[] Disp) Conta(Equipa e)
    {
        int[] tit = new int[4], res = new int[4], disp = new int[4];
        foreach (var j in e.Jogadores)
        {
            short esc = JgW(j, JgEscalacao);
            if (Disp(j)) disp[j.Posicao]++;
            else Assert.Equal(0, esc);
            if (esc == 1) tit[j.Posicao]++;
            if (esc == 2) res[j.Posicao]++;
        }
        return (tit, res, disp);
    }

    [Fact]
    public void EscalacaoCPUSaves()
    {
        var r = new Aleatorio(0x1234567);
        int n = 0;
        foreach (var s in SavesReais().Values)
            foreach (var e in s.Equipas)
            {
                if (e.Jogadores.Count == 0 || e.Jogadores.Any(j => j.Posicao > 3)) continue;
                Escalacao.LimparEscalacao(e);
                var ids = e.Jogadores.Select(j => j.ID).OrderBy(x => x).ToList();
                Escalacao.EscalacaoCPU(r, e);
                n++;
                Assert.Equal(ids, e.Jogadores.Select(j => j.ID).OrderBy(x => x).ToList());
                var (tit, res, disp) = Conta(e);
                int linhaDisp = disp[1] + disp[2] + disp[3], linhaTit = tit[1] + tit[2] + tit[3];
                Assert.Equal(Math.Min(1, disp[0]), tit[0]);
                Assert.Equal(Math.Min(10, linhaDisp), linhaTit);
                int gkRes = disp[0] >= 2 ? 1 : 0, tot = res.Sum();
                Assert.Equal(gkRes, res[0]);
                Assert.Equal(gkRes + Math.Min(5 - gkRes, linhaDisp - linhaTit), tot);
                Assert.Equal(Math.Min(tot, 3), EqW(e, EqSubst));
            }
        saida.WriteLine($"{n} equipas escaladas");
    }

    [Fact]
    public void F5Saves()
    {
        var r = new Aleatorio(99);
        int n = 0;
        foreach (var s in SavesReais().Values)
            foreach (var e in s.Equipas)
            {
                if (!Escalacao.FormacaoPermitida(e, 1, 4, 4, 2)) continue;
                Escalacao.EscalarFormacao(r, e, 5);
                n++;
                var (tit, res, _) = Conta(e);
                Assert.Equal(new[] { 1, 4, 4, 2 }, tit);
                Assert.True(res.Sum() <= 5);
                Assert.Equal(Math.Min(res.Sum(), 3), EqW(e, EqSubst));
                for (int i = 1; i < e.Jogadores.Count; i++)
                {
                    var a = e.Jogadores[i - 1]; var b = e.Jogadores[i];
                    Assert.False(a.Posicao > b.Posicao || (a.Posicao == b.Posicao &&
                        Partida.Jornada.NomeNaTela(a).AsSpan().SequenceCompareTo(Partida.Jornada.NomeNaTela(b)) > 0));
                }
            }
        saida.WriteLine($"{n} equipas em 4-4-2");
    }

    // seg04:34e6: 3 embaralhamentos (9 x Count chamadas a Random(Count))
    [Fact]
    public void AutomaticoRandom()
    {
        var e = SavesReais().Values.First().Equipas[0];
        int n = e.Jogadores.Count;
        var r1 = new Aleatorio(5);
        Escalacao.Automatico(r1, e);
        var r2 = new Aleatorio(5);
        for (int i = 0; i < 9 * n; i++) r2.Random(n);
        Assert.Equal(r2.Semente, r1.Semente);
        var (tit, _, _) = Conta(e);
        Assert.True(tit[0] <= 1 && tit[1] + tit[2] + tit[3] <= 10);
    }
}
