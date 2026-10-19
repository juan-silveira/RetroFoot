using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Save;
using Xunit;
using Xunit.Abstractions;
using static RetroFoot.Motor.Mercado.MercadoJogo;
using Equipa = RetroFoot.Motor.Save.Equipa;

namespace RetroFoot.Motor.Testes;

// (Traduzido de motor/mercado/*_test.go.)
public class MercadoTestes(ITestOutputHelper saida)
{
    static Dictionary<string, Jogo98> SavesReais()
    {
        var r = new Dictionary<string, Jogo98>();
        foreach (var a in Arquivos.Saves())
            try { r[a] = Jogo98.Ler(File.ReadAllBytes(a)); } catch { }
        return r;
    }

    static Jogo98? Ler(string n)
    {
        var a = Arquivos.Locais().Select(p => Path.Combine(p, n)).FirstOrDefault(File.Exists);
        return a != null ? Jogo98.Ler(File.ReadAllBytes(a)) : null;
    }

    static Ext X(double v) => Ext.F64(v);

    static MercadoJogo JogoTeste(double infl, int moeda)
    {
        var s = new Jogo98();
        X(infl).Bytes().CopyTo(s.Cabecalho.Real2B46, 0);
        return new MercadoJogo(s, new Aleatorio(0), moeda);
    }

    [Fact]
    public void Movimento()
    {
        var e = new Equipa { Dinheiro = 1000 };
        MercadoJogo.Movimento(e, 500, TipoBilhetes);
        MercadoJogo.Movimento(e, -200, TipoSalarios);
        MercadoJogo.Movimento(e, -50, TipoJuros);
        MercadoJogo.Movimento(e, 7, 99);
        Assert.Equal(1257, (int)e.Dinheiro);
        Assert.Equal(new[] { 500, 0, 0, 0, -200, 0, -50 }, Balanco(e));
        var f = new Equipa { Dinheiro = 100 };
        MercadoJogo.Movimento(f, -300, TipoComprados);
        Assert.Equal(-200, (int)f.Dinheiro);
        Assert.Equal(0xD4, f.B16[0x3D - 0x16]);
        Assert.Equal(0xFF, f.B16[0x3D - 0x16 + 3]);
    }

    [Fact]
    public void Juros()
    {
        foreach (var (d, j) in new[] { (0, 0), (500000, 25000), (1000000, 50000), (100000, 5000), (10, 0), (30, 2), (50, 2), (70, 4) })
            Assert.Equal(j, MercadoJogo.Juros(d));
    }

    [Fact]
    public void PrecoBancada()
    {
        var j = JogoTeste(2.0, 5);
        foreach (var (n, p) in new (byte, int)[] { (1, 350000), (2, 350000), (5, 350000), (6, 500000), (13, 500000), (14, 600000), (23, 600000) })
            Assert.Equal(p, j.PrecoBancada(new Equipa { Estadio = n }));
        var e = new Equipa { Estadio = 5, Dinheiro = 1000000 };
        j.ConstruirBancada(e);
        Assert.Equal(6, e.Estadio);
        Assert.Equal(500000, (int)e.Dinheiro);
        Assert.Equal(new byte[] { 0x80, 0x9a, 0x99, 0x99, 0x99, 0x19 }, e.EstadioReal);
        Assert.Equal(-500000, Balanco(e)[5]);
        e.Estadio = 24;
        Assert.False(Ampliar(e));
        Assert.False(j.PodeConstruir(e));
    }

    // na 1a temporada, dinheiro = +0x75 + soma do balanco + divida
    [Fact]
    public void BalancoPrimeiraTemporada()
    {
        foreach (var (a, s) in SavesReais())
        {
            if (!s.Equipas.All(e => e.B89.All(b => b == 0))) continue;
            int ok = s.Equipas.Count(e => (int)e.D75 + (int)e.D6D + Balanco(e).Sum() == (int)e.Dinheiro);
            Assert.True(ok * 100 >= 95 * s.Equipas.Count, $"{Path.GetFileName(a)}: {ok} de {s.Equipas.Count}");
        }
    }

    // depois do 1o jogo, Salarios = -FolhaSalarial nas equipas que nao compraram nem venderam
    [Fact]
    public void SalariosPrimeiroJogo()
    {
        foreach (var (a, s) in SavesReais())
        {
            // so os saves do jogo original (no ELIF20.EXE a equipa ja jogou duas vezes)
            if (s.Cabecalho.G271C != 2 || s.Divisoes.Count == 0 || s.Divisoes[0].Equipas.Count != 8) continue;
            foreach (var e in s.Equipas)
            {
                var b = Balanco(e);
                if (b[4] == 0 || b[1] != 0 || b[3] != 0) continue;
                Assert.True(-FolhaSalarial(e) == b[4], $"{Path.GetFileName(a)} equipa {e.ID}: salarios {b[4]}, folha {FolhaSalarial(e)}, divida {e.D6D}, juros {b[6]}");
                // o humano pode ter pago ou pedido emprestimo depois do jogo
                if (Treinador(s, e.Treinador) is not { Humano: not 0 }) Assert.Equal(MercadoJogo.Juros((int)e.D6D), -b[6]);
            }
        }
    }

    [Fact]
    public void ImpedimentoVenda()
    {
        var e = new Equipa();
        for (int i = 0; i < 13; i++) e.Jogadores.Add(new Jogador { Posicao = (byte)(1 + i % 3) });
        e.Jogadores[0].Posicao = 0;
        e.B83 = new byte[] { 1, 4, 4, 4, 0, 13 };
        Assert.Equal(0, MercadoJogo.ImpedimentoVenda(e, e.Jogadores[1]));
        Assert.Equal(2, MercadoJogo.ImpedimentoVenda(e, new Jogador { Posicao = 1, W36 = 5 }));
        Assert.Equal(3, MercadoJogo.ImpedimentoVenda(e, new Jogador { Posicao = 1, B14 = new byte[] { 0, 0, 1 } }));
        Assert.Equal(4, MercadoJogo.ImpedimentoVenda(e, new Jogador { Posicao = 1, B0C = new byte[] { 0, 0, 0, 0, 0, 0, 1, 0 } }));
        Assert.Equal(5, MercadoJogo.ImpedimentoVenda(e, e.Jogadores[0]));
        e.B83[5] = 11;
        Assert.Equal(1, MercadoJogo.ImpedimentoVenda(e, e.Jogadores[1]));
        e.B83 = new byte[] { 2, 4, 4, 2, 0, 12 };
        Assert.Equal(6, MercadoJogo.ImpedimentoVenda(e, e.Jogadores[1]));
    }

    [Fact]
    public void Emprestimo()
    {
        var j = JogoTeste(2.0, 5);
        new byte[] { 0x20, 0xa1, 0x07, 0 }.CopyTo(j.S.Cabecalho.G2D40, 0x3F);
        var e = new Equipa { Dinheiro = 100 };
        j.PedirEmprestimo(e);
        Assert.Equal(500000u, e.D6D); Assert.Equal(500100u, e.Dinheiro);
        Assert.True(j.PodePagar(e));
        j.PagarEmprestimo(e);
        Assert.Equal(0u, e.D6D); Assert.Equal(100u, e.Dinheiro);
        Assert.False(j.PodePagar(e));
        e = new Equipa { Dinheiro = 2600000, D6D = 1500000 };
        j.EmprestimoCPU(e, 100000);
        Assert.Equal(0u, e.D6D); Assert.Equal(1100000u, e.Dinheiro);
    }

    [Fact]
    public void Power()
    {
        foreach (var (b, x, r) in new[] { (1000.0, 0.04, 1.3182567385564075), (2, 6, 64), (37, 1.04, 42.749377298084) })
            Assert.True(Math.Abs(Partida.Fpu.PotenciaExata(X(b), X(x)).Float64() - r) < 1e-6);
        Assert.Equal(32, Partida.Fpu.PotenciaExata(X(2), X(5)).Round());
    }

    // no comeco de um jogo novo o salario de cada jogador e Salario(jogador, +0x79) (ou 1,0)
    [Fact]
    public void SalarioNoInicioDoJogo()
    {
        int n = 0;
        foreach (var (a, s) in SavesReais())
        {
            if (s.Cabecalho.G271C != 1 || s.Campeoes.Count != 0 || Ext.Bytes(s.Cabecalho.Real2B46).Float64() != 2) continue;
            var j = new MercadoJogo(s, new Aleatorio(0));
            int ok = 0, tot = 0;
            foreach (var e in s.Equipas)
            {
                var vc = Ext.Bytes(e.B79);
                foreach (var p in e.Jogadores)
                {
                    tot++;
                    if (j.Salario(p, vc) == (int)p.Salario || j.Salario(p, X(1)) == (int)p.Salario) ok++;
                }
            }
            n++;
            Assert.True(ok == tot, $"{Path.GetFileName(a)}: {ok} de {tot}");
        }
        saida.WriteLine($"{n} saves");
    }

    [Fact]
    public void MoedaDoSave()
    {
        var s = new Jogo98();
        var r = s.Cabecalho.G2D40;
        new byte[] { 5, (byte)'R', (byte)'e', (byte)'a', (byte)'i', (byte)'s' }.CopyTo(r, 0);
        void Put(int o, int v) => BitConverter.GetBytes(v).CopyTo(r, o);
        Put(0x3F, 500000); Put(0x43, 4); Put(0x47, 5); r[0x4B] = 1;
        Put(0x4F, 20000000); Put(0x53, 6000000); Put(0x57, 3000000);
        new byte[] { 0, 0, 0, 0, 0, 0, 0, 0x80, 0, 0x40 }.CopyTo(r, 0x5B);
        var m = Moeda.DoSave(s);
        Assert.Equal("Reais", m.Nome); Assert.Equal(5, m.Fator);
        Assert.Equal(20000000, m.PremioCampeao); Assert.Equal(3000000, m.PremioMarcador);
        Assert.Equal(2, m.Arredonda.Float64());
    }

    // +0x83..+0x88 contam so quem nao esta de saida; +0xC7/+0xC9 os que chegam
    [Fact]
    public void ContadoresDoPlantel()
    {
        foreach (var (nome, s) in SavesReais())
        {
            var j = new MercadoJogo(s, new Aleatorio(0));
            var chegam = new Dictionary<ushort, int>();
            var chegamEst = new Dictionary<ushort, int>();
            foreach (var e in s.Equipas)
            {
                var c = new int[6];
                foreach (var p in e.Jogadores)
                {
                    if (p.W36 != 0)
                    {
                        chegam[p.W36] = chegam.GetValueOrDefault(p.W36) + 1;
                        if (Equipa(s, p.W36) is { } d && j.Estrangeiro(Texto(d.Pais), Texto(p.Pais)) == 2)
                            chegamEst[p.W36] = chegamEst.GetValueOrDefault(p.W36) + 1;
                        continue;
                    }
                    c[p.Posicao]++;
                    if (p.B35 == 2) c[4]++;
                    c[5]++;
                }
                for (int i = 0; i < 6; i++) Assert.True(e.B83[i] == c[i], $"{Path.GetFileName(nome)}: equipa {e.ID} +0x{0x83 + i:X}");
            }
            bool original = s.Divisoes.Count > 0 && s.Divisoes[0].Equipas.Count == 8;
            if (!original) continue;   // ELIF20.EXE: um time comprou 6 estrangeiros sem +0xC9 subir
            foreach (var e in s.Equipas)
            {
                Assert.Equal(chegam.GetValueOrDefault(e.ID), e.WC7);
                Assert.Equal(chegamEst.GetValueOrDefault(e.ID), e.WC9);
            }
        }
    }

    [Fact]
    public void ObservadosETransferencias()
    {
        foreach (var (nome, s) in SavesReais())
        {
            var jog = s.Equipas.SelectMany(e => e.Jogadores).Select(p => p.ID).ToHashSet();
            foreach (var l in s.Ligacoes)
            {
                Assert.NotNull(Treinador(s, l.Equipa));
                Assert.True(l.Outras.Count <= 15);
                Assert.All(l.Outras, id => Assert.Contains(id, jog));
            }
            Assert.True(s.Transferencias.Count <= LimiteTransferencias);
            foreach (var tr in s.Transferencias)
            {
                Assert.True(tr.B04 <= 1 && tr.B05 <= 1 && Equipa(s, tr.W0A) != null);
                Assert.NotNull(EquipaDoJogador(s, tr.W06).E);
            }
            foreach (var e in s.Equipas)
                foreach (var p in e.Jogadores)
                    if (p.W36 != 0) Assert.Contains(MercadoJogo.ImpedimentoVenda(e, p), new[] { NaoVendeMaisJogadores, JaVendido });
        }
    }

    // leilao e compra mantem o dinheiro total e os contadores coerentes
    [Fact]
    public void LeilaoECompraConservam()
    {
        foreach (var (nome, s) in SavesReais())
        {
            var j = new MercadoJogo(s, new Aleatorio(12345), 5);
            long Total() => s.Equipas.Sum(e => (long)(int)e.Dinheiro);
            long antes = Total();
            int n = s.Transferencias.Count;
            var e0 = s.Equipas[0];
            var cand = e0.Jogadores.Find(p => MercadoJogo.ImpedimentoVenda(e0, p) == 0);
            if (cand == null) continue;
            ushort id = cand.ID, vend = e0.ID;
            bool vendido = j.Leiloar(vend, id, 0, null);
            Assert.Equal(antes, Total());
            if (vendido)
            {
                var c = EquipaDoJogador(s, id).E;
                Assert.True(c != null && c.ID != vend && s.Transferencias.Count == Math.Min(n + 1, LimiteTransferencias));
            }
            var comp = s.Equipas[1];
            bool feito = false;
            foreach (var v in s.Equipas.ToList())
            {
                if (feito || v.ID == comp.ID) continue;
                for (int pk = 0; pk < v.Jogadores.Count; pk++)
                {
                    int preco = j.PrecoParaComprar(comp, v, v.Jogadores[pk]);
                    if (preco <= 0) continue;
                    ushort pid = v.Jogadores[pk].ID;
                    j.Comprar(comp, v, pk, preco, true);
                    Assert.Equal(antes, Total());
                    j.ExecutarTransferenciasPendentes();
                    Assert.Equal(comp.ID, EquipaDoJogador(s, pid).E?.ID);
                    feito = true;
                    break;
                }
            }
        }
    }

    [Fact]
    public void RegistrarLance()
    {
        var l = new Leilao { Base = 10000 };
        l.RegistrarLance(1, 9999);
        l.RegistrarLance(2, 12345);
        l.RegistrarLance(3, 12999);
        l.RegistrarLance(4, 13000);
        Assert.Equal(4, l.Vencedor);
        Assert.Equal(13000, l.Melhor);
    }

    // 3:6e48: o +0x79 de uma jornada para a seguinte, byte a byte, em 1-2.e98 -> 1-3.e98
    [Fact]
    public void AtualizarValorDoClube()
    {
        var a = Ler("1-2.e98"); var b = Ler("1-3.e98");
        if (a == null || b == null) return;
        int certas = 0;
        for (int d = 0; d < 4; d++)
        {
            var c = Jogo98.Ler(b.Gravar());
            foreach (var id in c.Divisoes[d].Equipas) Equipa(c, id)!.B79 = (byte[])Equipa(a, id)!.B79.Clone();
            new MercadoJogo(c, new Aleatorio(0)).AtualizarValorDoClube(d);
            int ok = c.Divisoes[d].Equipas.Count(id => Equipa(c, id)!.B79.AsSpan().SequenceEqual(Equipa(b, id)!.B79));
            saida.WriteLine($"divisao {d}: {ok} de {c.Divisoes[d].Equipas.Count}");
            if (ok == c.Divisoes[d].Equipas.Count) certas++;
        }
        Assert.Equal(4, certas);
    }

    // o preco das compras diretas entre dois saves seguidos e o registado no TLastTransfer;
    // os leiloes conferem o salario novo
    [Fact]
    public void PrecoDasComprasReais()
    {
        int compras = 0, leiloes = 0;
        foreach (var (pa, pb) in new[] { ("1-3.e98", "1-5.e98"), ("1-5.e98", "1-6.e98") })
        {
            var a = Ler(pa); var b = Ler(pb);
            if (a == null || b == null) return;
            var ja = new MercadoJogo(a, new Aleatorio(0), 5); var jb = new MercadoJogo(b, new Aleatorio(0), 5);
            foreach (var tr in b.Transferencias.Skip(a.Transferencias.Count))
            {
                var (v, k) = EquipaDoJogador(a, tr.W06);
                if (tr.B04 == 1)
                {
                    var c = Equipa(a, tr.W0A);
                    Assert.Equal((int)tr.D0C, ja.PrecoDeVenda(v!, c, v!.Jogadores[k], ForcaMedia(v)));
                    compras++;
                    continue;
                }
                var (cb, kc) = EquipaDoJogador(b, tr.W06);
                var vb = Equipa(b, v!.ID)!;
                Assert.Equal((int)cb!.Jogadores[kc].Salario, jb.Salario(cb.Jogadores[kc], Ext.Bytes(vb.B79)));
                leiloes++;
            }
        }
        saida.WriteLine($"{compras} compras e {leiloes} leiloes conferidos");
        Assert.True(compras + leiloes > 0);
    }

    [Fact]
    public void AntesNoRanking()
    {
        var a = new Treinador { D16 = 2, D0E = 10 };
        var b = new Treinador { D16 = 1 | 5 << 16, D0E = 99 };
        var c = new Treinador { D16 = 1 | 5 << 16, D0E = 50 };
        Assert.True(MercadoJogo.AntesNoRanking(a, b)); Assert.False(MercadoJogo.AntesNoRanking(b, a));
        Assert.True(MercadoJogo.AntesNoRanking(b, c)); Assert.False(MercadoJogo.AntesNoRanking(c, b));
        Assert.True(MercadoJogo.AntesNoRanking(c, c));
    }

    [Fact]
    public void DeveDespedir()
    {
        var j = new MercadoJogo(new Jogo98(), new Aleatorio(0), 5);
        var e = new Equipa { ID = 1, Moral = Constantes.Ext10(0x3ffe, 0xa666666666666666).Real48() };
        var tr = new Treinador { W0C = 10 };
        Assert.True(j.DeveDespedir(tr, e));
        tr.W0C = 9;
        Assert.False(j.DeveDespedir(tr, e));
        tr.W0C = 10;
        e.Moral = X(0.66).Real48();
        Assert.False(j.DeveDespedir(tr, e));
    }

    // num save real: a chicotada troca o treinador, poe moral 1,0, zera os jogos do novo e
    // registra os eventos; se o novo vinha de outra equipa, ela fica com o antigo
    [Fact]
    public void ChicotadaEmSave()
    {
        int feitos = 0;
        foreach (var (a, s) in SavesReais())
        {
            var j = new MercadoJogo(s, new Aleatorio(0));
            foreach (var e in s.Equipas)
            {
                var antigo = Treinador(s, e.Treinador);
                if (antigo == null || antigo.Humano != 0) continue;
                ushort idAntigo = antigo.ID;
                int evAntigo = antigo.Eventos.Count;
                var it = new Interacao { MostrarChicotadas = 2 };
                Assert.False(j.ChicotadaPsicologica(e, 0, it));
                if (e.Treinador == idAntigo) continue;
                feitos++;
                var novo = Treinador(s, e.Treinador)!;
                Assert.Equal(MoralUm, e.Moral);
                Assert.Equal(0, novo.W0C);
                var ult = novo.Eventos[^1];
                Assert.True(ult.W09 == e.ID && (ult.B04 == EventoContratado || ult.B04 == EventoPromovido));
                Assert.True(antigo.Eventos.Count > evAntigo || evAntigo >= LimiteEventosTreinador);
                Assert.Equal(3, it.Chicotadas.Count);
                Assert.Equal(new Rodada.Chicotada(idAntigo, e.ID, 1), it.Chicotadas[0]);
                if (EquipaDoTreinador(s, idAntigo) is { } outra)
                {
                    Assert.Equal(outra.ID, it.Chicotadas[1].Equipa);
                    Assert.Equal(MoralUm, outra.Moral);
                }
                break;
            }
        }
        saida.WriteLine($"{feitos} chicotadas");
    }
}
