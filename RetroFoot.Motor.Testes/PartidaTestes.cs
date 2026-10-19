using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Save;
using Xunit;
using Xunit.Abstractions;
using static RetroFoot.Motor.Partida.Memoria;
using static RetroFoot.Motor.Partida.Sorteio;
using Equipa = RetroFoot.Motor.Save.Equipa;

namespace RetroFoot.Motor.Testes;

// As tabelas de DadosPartida foram geradas por gera.c com long double do gcc em x86-64 (a
// propria FPU x87, mantissa de 64 bits, arredondamento ao par): uma conferencia independente
// da aritmetica Extended. (Traduzido de motor/partida/*_test.go.)
public partial class PartidaTestes(ITestOutputHelper saida)
{
    static IEnumerable<string[]> Linhas(string arq) =>
        File.ReadLines(Path.Combine(AppContext.BaseDirectory, "DadosPartida", arq))
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(c => c.Length > 0);

    static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    // o %La do gcc: 0xf.dfdab7f82ece5e7p-2
    static Ext Hex(string s)
    {
        int p = s.IndexOf('p');
        var m = s[2..p];
        int e2 = int.Parse(s[(p + 1)..], CultureInfo.InvariantCulture);
        int k = m.Contains('.') ? m.Length - m.IndexOf('.') - 1 : 0;
        var M = BigInteger.Parse("0" + m.Replace(".", ""), NumberStyles.HexNumber);
        if (M.IsZero) return default;
        int bl = (int)M.GetBitLength();
        Assert.True(bl <= 64);
        return new Ext((ulong)(M << (64 - bl)), (ushort)(e2 - 4 * k - (64 - bl) + 16383 + 63));
    }

    static Jogador Jog(ushort id, byte pos, ushort forca, ushort esc)
    {
        var j = new Jogador { ID = id, Posicao = pos, Forca = forca, Nota = 5, Comportamento = 3, Lesao = 5 };
        MudarEscalacao(j, esc);
        return j;
    }

    static Equipa Eq(params Jogador[] js) => new() { Jogadores = js.ToList() };

    [Fact]
    public void ValorBatedorIgualX87()
    {
        foreach (var c in Linhas("batedor.txt"))
        {
            var j = new Jogador { Posicao = (byte)I(c[0]), Forca = (ushort)I(c[1]), Nota = (ushort)I(c[2]) };
            Assert.Equal((short)I(c[3]), Penaltis.ValorBatedor(j));
        }
    }

    [Fact]
    public void ValorBatedorEmpateParaOPar()
    {
        Assert.Equal(4, Penaltis.ValorBatedor(new Jogador { Posicao = 0, Forca = 6, Nota = 5 }));
        Assert.Equal(2, Penaltis.ValorBatedor(new Jogador { Posicao = 0, Forca = 2, Nota = 5 }));
    }

    [Fact]
    public void ForcaMediaIgualX87()
    {
        foreach (var c in Linhas("media.txt"))
        {
            int s = I(c[0]), n = I(c[1]);
            var e = new Equipa();
            for (int i = 0; i < n; i++) e.Jogadores.Add(new Jogador { Forca = (ushort)(s / n) });
            e.Jogadores[0].Forca += (ushort)(s % n);
            Assert.Equal(I(c[2]), ForcaMedia(e));
        }
    }

    static Equipa Setores(ushort g, ushort d, ushort m, ushort a, double moral)
    {
        var e = new Equipa();
        Poe16(e.B4D, 4, g); Poe16(e.B4D, 6, d); Poe16(e.B4D, 8, m); Poe16(e.B4D, 10, a);
        Reais.PorReal48(e.Moral, moral);
        return e;
    }

    [Fact]
    public void ChanceDeGolIgualX87()
    {
        int n = 0;
        foreach (var c in Linhas("chance.txt"))
        {
            var v = c[..8].Select(x => (ushort)I(x)).ToArray();
            double moral = double.Parse(c[8], CultureInfo.InvariantCulture);
            var e = Setores(v[0], v[1], v[2], v[3], moral);
            var adv = Setores(v[4], v[5], v[6], v[7], 1);
            Assert.Equal(0, Eventos.ForcaRelativa(e, adv).Cmp(Hex(c[9])));
            Assert.Equal(0, Eventos.ArgumentoLn(e, adv).Cmp(Hex(c[10])));
            // Q passa por Ln/Exp e o moral do teste vira Real48
            double q = Eventos.ChanceDeGol(e, adv).Float64(), w = Hex(c[11]).Float64();
            Assert.True(Math.Abs(q - w) <= 1e-9 * Math.Max(1, Math.Abs(w)), $"{string.Join(' ', v)}: Q = {q}, x87 {w}");
            n++;
        }
        saida.WriteLine($"{n} linhas");
    }

    [Fact]
    public void ParaReal48IgualAReais()
    {
        var rnd = new Random(1);
        var quer = new byte[6];
        for (int i = 0; i < 20000; i++)
        {
            double x = Math.ScaleB(rnd.NextDouble() + 0.5, rnd.Next(60) - 30);
            if (i % 2 == 1) x = -x;
            Reais.PorReal48(quer, x);
            Assert.Equal(quer, Fpu.ParaReal48(Ext.F64(x)));
            Assert.Equal(quer, Fpu.ParaReal48(Fpu.DeReal48(quer)));
        }
    }

    [Fact]
    public void Arredondar()
    {
        (double x, int n)[] casos = { (0.5, 0), (1.5, 2), (2.5, 2), (-0.5, 0), (-1.5, -2), (-2.5, -2), (2.4999, 2), (2.5001, 3), (-7.2, -7), (1e6 + 0.5, 1000000) };
        foreach (var (x, n) in casos) Assert.Equal(n, Fpu.Arredondar(Ext.F64(x)));
    }

    [Fact]
    public void LnExpPerto()
    {
        foreach (var x in new[] { 0.001, 0.5, 1, 1.0000001, 2, 3.75, 100, 12345.678 })
        {
            double l = Fpu.Ln(Ext.F64(x)).Float64();
            Assert.True(Math.Abs(l - Math.Log(x)) <= 1e-15 * Math.Max(1, Math.Abs(l)), $"Ln({x}) = {l}");
            double p = Fpu.Potencia(Ext.F64(x), PreJogo.C104).Float64(), w = Math.Pow(x, 1.04);
            Assert.True(Math.Abs(p - w) <= 1e-14 * w, $"Power({x}) = {p}, {w}");
        }
        Assert.Equal(1.04, PreJogo.C104.Float64());
        Assert.Equal(0.3, PreJogo.C03.Float64());
        Assert.True(Math.Abs(Reais.Real48(PreJogo.R48_09) - 0.9) < 1e-11);
    }

    // peso 0 nunca e sorteado: Fair Play nunca e expulso, Lesao 0 nunca se lesiona, goleiro nunca marca
    [Fact]
    public void PesoZeroNuncaSai()
    {
        var r = new Aleatorio(7);
        var e = Eq(Jog(1, 0, 30, EscTitular), Jog(2, 1, 30, EscTitular));
        foreach (var j in e.Jogadores) { j.Comportamento = 0; j.Lesao = 0; }
        for (int i = 0; i < 500; i++)
        {
            Assert.False(Eventos.Expulsao(r, e).Ok);
            Assert.False(Eventos.Lesao(r, e).Ok);
        }
        var g = Eq(Jog(1, 0, 50, EscTitular));
        for (int i = 0; i < 500; i++) Assert.Null(SorteiaJogador(r, Elenco(g), 1, PesoGol));
    }

    // peso de gol em 16 bits: 9 x 9999 = 89991 -> 24455; x 10 = 244550 -> 47942 = -17594
    [Fact]
    public void PesoDeGolEstoura() =>
        Assert.Equal(-17594, PesoDoJogador(new Jogador { Posicao = 3, Forca = 9999, Nota = 10 }, PesoGol));

    [Fact]
    public void GoleiroPrimeiroDaLista()
    {
        var e = Eq(Jog(1, 0, 40, EscReserva), Jog(2, 0, 20, EscTitular), Jog(3, 1, 30, EscTitular));
        Assert.Equal(1, Penaltis.Goleiro(e)!.ID);
        e.Jogadores[1].Forca = 45;
        Assert.Equal(2, Penaltis.Goleiro(e)!.ID);
    }

    [Fact]
    public void SubstituicaoAutomatica()
    {
        Equipa Novo()
        {
            var e = Eq(Jog(1, 0, 30, EscTitular), Jog(2, 1, 30, EscTitular), Jog(3, 2, 30, EscTitular),
                Jog(4, 3, 30, EscTitular), Jog(5, 0, 30, EscReserva), Jog(6, 2, 30, EscReserva));
            MudarSubstituicoes(e, 2);
            return e;
        }
        var e = Novo();
        MudarEscalacao(e.Jogadores[1], EscFora);
        Assert.True(Eventos.SubstituicaoAutomatica(e, e.Jogadores[1], Eventos.SubLesao));
        Assert.Equal(EscTitular, Escalacao(e.Jogadores[5]));
        Assert.Equal(1, Substituicoes(e));
        Assert.Equal(1, Historial(e.Jogadores[5], HistJogos));

        e = Novo();
        MudarEscalacao(e.Jogadores[2], EscFora);
        Assert.False(Eventos.SubstituicaoAutomatica(e, e.Jogadores[2], Eventos.SubExpulsao));

        e = Novo();
        MudarEscalacao(e.Jogadores[0], EscFora);
        Assert.True(Eventos.SubstituicaoAutomatica(e, e.Jogadores[0], Eventos.SubExpulsao));
        Assert.Equal(EscTitular, Escalacao(e.Jogadores[4]));
        Assert.Equal(EscFora, Escalacao(e.Jogadores[3]));

        e = Novo();
        MudarSubstituicoes(e, 0);
        Assert.False(Eventos.SubstituicaoAutomatica(e, e.Jogadores[1], Eventos.SubLesao));
    }

    [Fact]
    public void OrdenarBatedores()
    {
        var e = Eq(Jog(1, 0, 50, EscTitular), Jog(2, 3, 10, EscReserva), Jog(3, 3, 20, EscTitular), Jog(4, 1, 30, EscTitular));
        Penaltis.OrdenarBatedores(e);
        Assert.Equal(new ushort[] { 3, 4, 1, 2 }, e.Jogadores.Select(j => j.ID).ToArray());
    }

    [Fact]
    public void DesempateRapido()
    {
        for (uint s = 0; s < 50; s++)
        {
            var jg = new JogoAtual();
            jg.Equipa[0] = Eq(Jog(1, 0, 40, EscTitular));
            jg.Equipa[1] = Eq(Jog(2, 0, 10, EscTitular));
            var d = Penaltis.DesempateTaca(new Aleatorio(s), jg, false, 1, 14);
            var r = new Aleatorio(s);
            int sc = r.Random(40) + r.Random(40) + r.Random(40);
            int sf = r.Random(10) + r.Random(10) + r.Random(10);
            Assert.True(d.Rapido);
            Assert.Equal(sc >= sf, d.PassaVisitante);
        }
    }

    [Fact]
    public void DesempateCobrancas()
    {
        for (uint s = 0; s < 200; s++)
        {
            var jg = new JogoAtual { Equipa = { [0] = new Equipa(), [1] = new Equipa() } };
            for (int i = 0; i < 11; i++)
            {
                jg.Equipa[0]!.Jogadores.Add(Jog((ushort)(i + 1), (byte)(i % 4), 30, EscTitular));
                jg.Equipa[1]!.Jogadores.Add(Jog((ushort)(i + 100), (byte)(i % 4), 30, EscTitular));
            }
            var d = Penaltis.DesempateTaca(new Aleatorio(s), jg, true, 1, 14);
            Assert.False(d.Rapido);
            Assert.NotEqual(d.Golos[0], d.Golos[1]);
            Assert.Equal(d.Golos[1] > d.Golos[0], d.PassaVisitante);
            int nc = d.Cobrancas.Count(c => c.Lado == 0), nf = d.Cobrancas.Count(c => c.Lado == 1);
            Assert.True(nc >= 3 && nc - nf >= 0 && nc - nf <= 1, $"semente {s}: {nc} x {nf}");
        }
    }

    [Fact]
    public void SorteiaJogadorProporcional()
    {
        var r = new Aleatorio(0);
        var e = new Equipa();
        for (int i = 0; i < 3; i++) e.Jogadores.Add(new Jogador { ID = (ushort)(i + 1), Comportamento = 5 });
        MudarEscalacao(e.Jogadores[0], EscTitular);
        int n = 0;
        for (int i = 0; i < 3000; i++) if (SorteiaJogador(r, Elenco(e), 1, PesoComportamento) != null) n++;
        Assert.True(n / 100 == 9 || n / 100 == 10, n.ToString());
    }

    // ---------- saves reais ----------

    static List<Jogo98> Saves()
    {
        var r = new List<Jogo98>();
        foreach (var a in Arquivos.Saves())
            try { r.Add(Jogo98.Ler(File.ReadAllBytes(a))); } catch { }
        return r;
    }

    // a influencia do arbitro gravada em cada jogo (+0x4C = casa + 2, +0x4E visitante)
    [Fact]
    public void InfluenciaDoArbitroNosSaves()
    {
        int n = 0;
        foreach (var s in Saves())
        {
            var eq = s.Equipas.ToDictionary(e => e.ID);
            var ar = s.Arbitros.ToDictionary(a => a.ID);
            foreach (var g in s.Jogos)
            {
                if (g.Casa == 0 || g.Fora == 0 || g.Arbitro == 0) continue;
                var a = ar[g.Arbitro];
                Assert.Equal(g.W4C, (ushort)(PreJogo.InfluenciaDoArbitro(a, eq[g.Casa]) + 2));
                Assert.Equal(g.W4E, (ushort)PreJogo.InfluenciaDoArbitro(a, eq[g.Fora]));
                n++;
            }
        }
        saida.WriteLine($"{n} jogos conferidos");
    }

    // uma escalacao qualquer (a de verdade e seg03:4b73): 4-4-2 na ordem da lista, linha
    // completada com quem sobrar, e ate 5 reservas
    static void PrepararSimples(Equipa e)
    {
        ZerarPartida(e);
        var lim = new[] { 1, 4, 4, 2 };
        var n = new int[4];
        int l = 0, r = 0;
        var aptos = Elenco(e).Where(j => j.Suspensao == 0 && j.JogosLesao == 0 && j.Posicao < 4).ToList();
        foreach (var j in Elenco(e)) MudarEscalacao(j, EscFora);
        foreach (var j in aptos)
            if (n[j.Posicao] < lim[j.Posicao]) { n[j.Posicao]++; if (j.Posicao != 0) l++; MudarEscalacao(j, EscTitular); }
        foreach (var j in aptos)
            if (Escalacao(j) == EscFora && j.Posicao != 0 && l < 10) { l++; MudarEscalacao(j, EscTitular); }
        foreach (var j in aptos)
            if (Escalacao(j) == EscFora && r < 5) { r++; MudarEscalacao(j, EscReserva); }
        MudarSubstituicoes(e, (short)Math.Min(r, 3));
        ForcaDosSetores(e);
    }

    (int jogos, int gols, int pen, int exp, int les, int anul, byte[] save) JogarSave(byte[] b, uint semente, bool amarelos = false,
        bool extras = true, List<string>? lances = null, List<LanceExtra>? anotados = null, bool acrescimos = false,
        List<JogoAtual>? finalizados = null)
    {
        var s = Jogo98.Ler(b);
        var eq = s.Equipas.ToDictionary(e => e.ID);
        var ar = s.Arbitros.ToDictionary(a => a.ID);
        var tr = s.Treinadores.ToDictionary(t => t.ID);
        var jn = new Jornada { R = new Aleatorio(semente), Amarelos = amarelos, RegistarExtras = extras, SubstituirNoIntervalo = true, Acrescimos = acrescimos };
        // o humano: na lesao entra o 1o reserva (o roteiro do oraculo); no intervalo troca o
        // ultimo titular de linha pelo ultimo reserva de linha
        jn.Ganchos.Substituicao = (jg, lado, saiu, motivo) =>
        {
            var e = jg.Equipa[lado]!;
            if (motivo == 4)
            {
                var t = e.Jogadores.LastOrDefault(j => Escalacao(j) == EscTitular && j.Posicao != 0);
                var r = e.Jogadores.LastOrDefault(j => Escalacao(j) == EscReserva && j.Posicao != 0);
                if (t == null || r == null || Substituicoes(e) <= 0) return;
                MudarEscalacao(t, EscFora); MudarEscalacao(r, EscTitular);
                MudarSubstituicoes(e, (short)(Substituicoes(e) - 1));
                ForcaDosSetores(e);
                return;
            }
            if (saiu != null) RetroFoot.Motor.Ciclo.JogoEmCurso.SubstituicaoForcada(e, saiu, 0);
        };
        foreach (var g in s.Jogos)
        {
            var jg = new JogoAtual { Dados = g, Arbitro = ar.GetValueOrDefault(g.Arbitro) };
            jg.Equipa[0] = eq.GetValueOrDefault(g.Casa);
            jg.Equipa[1] = eq.GetValueOrDefault(g.Fora);
            for (int l = 0; l < 2; l++)
                if (jg.Equipa[l] is { } e) { jg.Treinador[l] = tr.GetValueOrDefault(e.Treinador); PrepararSimples(e); }
            jn.Jogos.Add(jg);
            if (g.Taca == TipoJogo.Taca) jn.Taca = true;
        }
        int jogos = 0, gols = 0, pen = 0, exp = 0, les = 0, anul = 0;
        jn.Finalizar = jg =>
        {
            if (jg.Equipa[0] == null || jg.Equipa[1] == null) return;
            jogos++;
            var gl = new short[2];
            foreach (var l in jg.Lances)
                switch (l.Tipo)
                {
                    case TipoLance.Gol: gl[l.Lado]++; gols++; break;
                    case TipoLance.Penalti: gl[l.Lado]++; gols++; pen++; break;
                    case TipoLance.Expulsao: exp++; break;
                    case TipoLance.Lesao: les++; break;
                    case TipoLance.GolAnulado: anul++; break;
                }
            for (int l = 0; l < 2; l++)
            {
                Assert.Equal(gl[l], Golos(jg.Equipa[l]!));
                Assert.True(Anulados(jg.Equipa[l]!) >= 0);
                Assert.True(Penaltis.Titulares(jg.Equipa[l]!).Count <= 11);
            }
            for (int i = 1; i < jg.Lances.Count; i++) Assert.True(jg.Lances[i].Minuto >= jg.Lances[i - 1].Minuto);
            foreach (var l in jg.Lances) lances?.Add($"{jg.Dados.Casa}-{jg.Dados.Fora} {l.Lado} {l.Minuto} {l.Tipo} {l.JogadorID} {l.Posicao} {l.SegundoAmarelo}");
            if (!extras) Assert.Empty(jg.Extras);
            anotados?.AddRange(jg.Extras);
            finalizados?.Add(jg);
            if (jg.Dados.Taca == TipoJogo.Taca && jg.Empatado) Penaltis.DesempateTaca(jn.R, jg, false, 1, 14);
        };
        jn.Jogar();
        return (jogos, gols, pen, exp, les, anul, s.Gravar());
    }

    // joga a rodada preparada de cada save: lances x placar, titulares, e a mesma semente da o mesmo save
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JornadaNosSaves(bool amarelos)
    {
        int jogos = 0, gols = 0, pen = 0, exp = 0, les = 0, anul = 0, i = 0;
        foreach (var s in Saves())
        {
            var b = s.Gravar();
            var r1 = JogarSave(b, (uint)(1000 + i), amarelos);
            var r2 = JogarSave(b, (uint)(1000 + i), amarelos);
            Assert.Equal(r1.save, r2.save);
            jogos += r1.jogos; gols += r1.gols; pen += r1.pen; exp += r1.exp; les += r1.les; anul += r1.anul;
            i++;
        }
        Assert.True(jogos > 0);
        double m = (double)gols / jogos;
        saida.WriteLine($"{jogos} jogos: {m:F2} gols/jogo, {pen} penaltis, {exp} expulsoes, {les} lesoes, {anul} anulados");
        Assert.InRange(m, 1, 6);
    }

    // os lances extras (substituicoes e penaltis sem gol, Extras.cs) sao so anotados: com e
    // sem eles, o mesmo save byte a byte e os mesmos lances
    [Fact]
    public void ExtrasNaoMudamOJogo()
    {
        int subs = 0, pens = 0, intervalo = 0, i = 0;
        foreach (var s in Saves())
        {
            var b = s.Gravar();
            for (uint k = 0; k < 4; k++)
            {
                var l1 = new List<string>(); var l2 = new List<string>(); var ex = new List<LanceExtra>();
                var r1 = JogarSave(b, (uint)(2000 + 10 * i + k), false, true, l1, ex);
                var r2 = JogarSave(b, (uint)(2000 + 10 * i + k), false, false, l2);
                Assert.Equal(r2.save, r1.save);
                Assert.Equal(l2, l1);
                foreach (var x in ex)
                    if (x.Tipo == TipoExtra.Substituicao)
                    {
                        Assert.NotNull(x.Saiu); Assert.NotNull(x.Entrou); Assert.NotEqual(x.Saiu!.ID, x.Entrou!.ID);
                        subs++;
                        if (x.Intervalo) intervalo++;
                    }
                    else
                    {
                        Assert.Equal(TipoExtra.PenaltiFalhado, x.Tipo);
                        Assert.InRange(x.Resultado, ResultadoPenalti.Defesa, ResultadoPenalti.PorCima);
                        Assert.NotNull(x.Batedor); Assert.NotNull(x.Goleiro);
                        pens++;
                    }
            }
            i++;
        }
        saida.WriteLine($"{subs} substituicoes ({intervalo} no intervalo), {pens} penaltis sem gol");
        Assert.True(subs > 0 && pens > 0);
    }

    // o valor do batedor de um defensor com forca editada (31998): no original (2 x 31998) estoura
    // os 16 bits e fica negativo (ele bate pior que um de forca 10); na versao Atual fica no maximo
    [Fact]
    public void BatedorComForcaEnormeNaoEstouraNaVersaoAtual()
    {
        var j = new RetroFoot.Motor.Save.Jogador { Posicao = 1, Forca = 31998, Nota = 5 };
        Assert.True(RetroFoot.Motor.Partida.Penaltis.ValorBatedor(j, false) < 0);
        Assert.Equal(short.MaxValue, RetroFoot.Motor.Partida.Penaltis.ValorBatedor(j, true));
        var normal = new RetroFoot.Motor.Save.Jogador { Posicao = 1, Forca = 10, Nota = 5 };
        Assert.Equal(RetroFoot.Motor.Partida.Penaltis.ValorBatedor(normal, false), RetroFoot.Motor.Partida.Penaltis.ValorBatedor(normal, true));
    }
}
