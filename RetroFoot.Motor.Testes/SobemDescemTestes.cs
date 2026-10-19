using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.NovoJogo;
using RetroFoot.Motor.Save;
using RetroFoot.Motor.Temporada;
using Xunit;
using Xunit.Abstractions;
using R = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.Motor.Testes;

// A opcao "Sobem e descem por divisao" (Regras.SobemDescemEscolhido): o automatico igual ao
// de antes, o K escolhido limitado pelo maximo, a marca no save e o fim de temporada com K = 1 a 4.
public class SobemDescemTestes(ITestOutputHelper saida)
{
    static readonly int[] Ns = { 8, 10, 12, 14, 16, 18, 20 };

    [Fact]
    public void RegrasDoK()
    {
        Assert.Equal(2, R.Original.SobemDescem);
        Assert.Equal(34, R.Original.MinimoEquipas);
        foreach (var n in Ns)
        {
            // automatico: 2, ou 4 com N >= 16 (o de antes)
            var a = R.Launcher(n);
            Assert.Equal(n >= 16 ? 4 : 2, a.SobemDescem);
            Assert.Equal(4 * n + a.SobemDescem, a.MinimoEquipas);
            Assert.Equal(n == 8 ? 3 : 4, R.MaximoSobemDescem(n));
            for (int k = 1; k <= 4; k++)
            {
                var r = a with { SobemDescemEscolhido = k };
                Assert.Equal(Math.Min(k, R.MaximoSobemDescem(n)), r.SobemDescem);
                Assert.Equal(4 * n + r.SobemDescem, r.MinimoEquipas);
                Assert.True(2 * r.SobemDescem < n);
            }
        }
    }

    // so os do jogo original (5 divisoes, as 4 de cima do mesmo tamanho)
    static List<(string Nome, byte[] Bytes)> Saves()
    {
        var r = new List<(string, byte[])>();
        foreach (var a in Arquivos.Saves())
        {
            var b = File.ReadAllBytes(a);
            Jogo98 s;
            try { s = Jogo98.Ler(b); } catch { continue; }
            if (s.Divisoes.Count != 5 || s.Divisoes.Take(4).Any(d => d.Equipas.Count != s.Divisoes[0].Equipas.Count)) continue;
            r.Add((Path.GetFileName(a), b));
        }
        return r;
    }

    [Fact]
    public void MarcaNoSave()
    {
        int n = 0;
        foreach (var (nome, b) in Saves())
        {
            n++;
            var s = Jogo98.Ler(b);
            // nenhum save sem a opcao tem a marca: o automatico continua
            Assert.Equal(0, R.SobemDescemDoSave(s));
            Assert.Equal(0, R.DoSave(s, R.Launcher(20) with { SobemDescemEscolhido = 3 }).SobemDescemEscolhido);
            for (int k = 1; k <= 4; k++)
            {
                var m = Jogo98.Ler(b);
                Assert.True(R.MarcarSobemDescem(m, k));
                var lido = Jogo98.Ler(m.Gravar());
                Assert.Equal(k, R.SobemDescemDoSave(lido));
                Assert.Equal(k, R.DoSave(lido, R.Original).SobemDescemEscolhido);
                // so o byte da marca muda; sem ela, o save volta igual
                Assert.True(R.MarcarSobemDescem(lido, 0));
                Assert.True(lido.Gravar().AsSpan().SequenceEqual(b), nome);
            }
        }
        saida.WriteLine($"{n} saves");
    }

    // o jogo novo com o K escolhido leva a marca; o automatico nao (o save fica igual)
    [Fact]
    public void JogoNovo()
    {
        var raiz = Arquivos.Raiz();
        if (raiz == null) { saida.WriteLine("jogo original nao encontrado"); return; }
        byte[] Criar(R regras)
        {
            var d = Dados.Ler(raiz);
            var sel = d.ContarEquipas();
            sel.Minimo = regras.MinimoEquipas;
            sel.Todos();
            sel.Confirmar(new Aleatorio(0x12345678));
            var g = new Aleatorio(0x2A0B1307);
            var e = Criacao.Criar(d, new Opcoes { Moeda = NovoJogo.Tabelas.MoedaReais, Ano = 2026, Humanos = new() { "Teste" }, Regras = regras }, g);
            e.IniciarTemporada();
            e.PrepararJornada(1, g);
            return e.Save(Exportar.DataHora(new DateTime(2026, 10, 17, 9, 0, 0))).Gravar();
        }
        var auto = Criar(R.Launcher(10));
        Assert.Equal(0, R.SobemDescemDoSave(Jogo98.Ler(auto)));
        var tres = Criar(R.Launcher(10) with { SobemDescemEscolhido = 3 });
        var s3 = Jogo98.Ler(tres);
        Assert.Equal(3, R.SobemDescemDoSave(s3));
        Assert.Equal(3, R.DoSave(s3, R.Launcher(10)).SobemDescem);
        // fora a marca, o mesmo save do automatico
        R.MarcarSobemDescem(s3, 0);
        Assert.True(s3.Gravar().AsSpan().SequenceEqual(auto));
    }

    // fim de temporada com K = 1 a 4 (4 vira 3 com 8 equipas): tamanhos mantidos, exatamente K
    // sobem e K descem entre cada par de divisoes e K do Distrital por sorteio
    [Fact]
    public void FimDeTemporadaComK()
    {
        int casos = 0;
        var porN = new HashSet<int>();
        foreach (var (nome, b) in Saves())
        {
            var s0 = Jogo98.Ler(b);
            if (s0.OutrasEquipas.Count < 1) continue;
            for (int k = 1; k <= 4; k++)
            {
                var s = Jogo98.Ler(b);
                R.MarcarSobemDescem(s, k);
                var tp = new Epoca(s, new Aleatorio(12345 + (uint)k));
                int N = tp.Regras.N, K = tp.Regras.SobemDescem;
                Assert.Equal(Math.Min(k, R.MaximoSobemDescem(N)), K);
                var antes = s.Divisoes.Select(d => d.Equipas.ToList()).ToList();
                var r = tp.FimDeTemporada();
                var depois = s.Divisoes.Select(d => d.Equipas.ToList()).ToList();
                Assert.Equal(antes.Select(l => l.Count), depois.Select(l => l.Count));
                for (int d = 0; d < 4; d++)
                {
                    var desce = antes[d].Skip(N - K).ToHashSet();
                    Assert.True(desce.SetEquals(r.Desceram[d]), $"{nome} K={K}: desceram da {d + 1}a");
                    Assert.True(desce.IsSubsetOf(depois[d + 1]), $"{nome} K={K}: na {d + 2}a");
                    Assert.Equal(K, r.Subiram[d + 1].Distinct().Count());
                    if (d < 3) Assert.True(antes[d + 1].Take(K).ToHashSet().SetEquals(r.Subiram[d + 1]), $"{nome} K={K}: subiram da {d + 2}a");
                    var fica = antes[d].Skip(d == 0 ? 0 : K).Take(N - K - (d == 0 ? 0 : K)).ToHashSet();
                    Assert.True(fica.IsSubsetOf(depois[d]) && depois[d].Count(fica.Contains) == fica.Count, $"{nome} K={K}: ficaram na {d + 1}a");
                }
                // do Distrital: K dos que la estavam, todos diferentes
                Assert.True(r.Subiram[4].All(antes[4].Contains));
                Assert.True(r.Subiram[4].ToHashSet().SetEquals(depois[3].Skip(N - K)), $"{nome} K={K}: no fim da 4a");
                Assert.Equal(s.Equipas.Count, depois.SelectMany(l => l).Distinct().Count());
                casos++;
                porN.Add(N);
            }
        }
        saida.WriteLine($"{casos} casos; N = {string.Join(", ", porN.OrderBy(x => x))}");
        Assert.True(casos > 0);
    }
}
