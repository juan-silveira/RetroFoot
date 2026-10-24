using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using TRegras = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.Motor.Testes;

// O jogador automatico (Soak.cs) jogando temporadas inteiras. Por padrao so o caso curto
// (1 temporada, original, 8 equipas); RETROFOOT_SOAK=1 liga todos (3 temporadas cada);
// RETROFOOT_SOAK_TEMPORADAS=n e RETROFOOT_SOAK_SEMENTES=a,b,c mudam o tamanho.
public class SoakTestes(ITestOutputHelper saida)
{
    static bool Longo => Environment.GetEnvironmentVariable("RETROFOOT_SOAK") == "1";
    static int Temporadas => int.TryParse(Environment.GetEnvironmentVariable("RETROFOOT_SOAK_TEMPORADAS"), out var t) ? t : 3;
    static int[] Sementes => (Environment.GetEnvironmentVariable("RETROFOOT_SOAK_SEMENTES") ?? "1")
        .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();

    static TRegras Mod(int n) => TRegras.Launcher(n) with { DesempateVitorias = true };

    public static IEnumerable<object[]> Configs()
    {
        yield return new object[] { "orig8-1h" };
        yield return new object[] { "orig8-2h" };
        yield return new object[] { "mod8-2h" };
        yield return new object[] { "mod16-1h" };
        yield return new object[] { "mod16-2h" };
        yield return new object[] { "mod20-1h" };
        yield return new object[] { "mod20-2h" };
    }

    static Soak.Config Config(string nome, int semente, int temporadas) => nome switch
    {
        "orig8-1h" => new(nome, TRegras.Original, 1, Temporadas: temporadas, Semente: semente),
        "orig8-2h" => new(nome, TRegras.Original, 2, DivisoesDiferentes: true, Temporadas: temporadas, Semente: semente),
        "mod8-2h" => new(nome, Mod(8), 2, true, true, true, temporadas, semente),
        "mod16-1h" => new(nome, Mod(16), 1, true, true, false, temporadas, semente),
        "mod16-2h" => new(nome, Mod(16), 2, true, true, true, temporadas, semente),
        "mod20-1h" => new(nome, Mod(20), 1, true, true, false, temporadas, semente),
        "mod20-2h" => new(nome, Mod(20), 2, true, true, true, temporadas, semente),
        _ => throw new ArgumentException(nome),
    };

    void Rodar(Soak.Config c)
    {
        var raiz = Arquivos.Raiz();
        if (raiz == null) { saida.WriteLine("jogo original nao encontrado"); return; }
        var s = new Soak(c, raiz);
        var t0 = DateTime.UtcNow;
        s.Jogar();
        foreach (var l in s.Log) saida.WriteLine(l);
        saida.WriteLine($"{c.Nome} semente {c.Semente}: {s.Temporadas} temporadas, {s.Jornadas} jornadas, {s.Recargas} recargas, " +
            $"{s.Leiloes} leiloes, {s.Vendas} vendas, {s.Compras} compras, {s.Emprestimos} emprestimos, {s.Propostas} propostas, " +
            $"{(DateTime.UtcNow - t0).TotalSeconds:F1}s");
        foreach (var p in s.Problemas) saida.WriteLine(p);
        Assert.True(s.Problemas.Count == 0, string.Join("\n", s.Problemas.Take(20)));
        Assert.True(s.Temporadas >= c.Temporadas, $"so {s.Temporadas} temporadas");
    }

    [Fact]
    public void Curto() => Rodar(Config("orig8-1h", 1, 1));

    // RETROFOOT_PRECOS=1: o valor de mercado (MercadoJogo.ValorDeMercado) contra o que os times
    // pagaram de verdade em cada transferencia (leilao e compra), em temporadas inteiras
    [Theory]
    [InlineData("orig8-1h")]
    [InlineData("mod16-1h")]
    public void PrecosDeMercado(string nome)
    {
        if (Environment.GetEnvironmentVariable("RETROFOOT_PRECOS") != "1") return;
        var raiz = Arquivos.Raiz();
        if (raiz == null) return;
        var s = new Soak(Config(nome, 3, Temporadas), raiz);
        s.Jogar();
        foreach (var tipo in s.Precos.Select(x => x.Tipo).Distinct().OrderBy(x => x))
        {
            var r = s.Precos.Where(x => x.Tipo == tipo && x.Valor > 0).Select(x => (double)x.Preco / x.Valor).OrderBy(x => x).ToList();
            if (r.Count == 0) continue;
            double Q(double q) => r[(int)Math.Min(r.Count - 1, Math.Round(q * (r.Count - 1)))];
            saida.WriteLine($"{nome} tipo {tipo}: {r.Count} transferencias; preco/valor de mercado: min {r[0]:F2} q1 {Q(0.25):F2} mediana {Q(0.5):F2} q3 {Q(0.75):F2} max {r[^1]:F2}");
        }
        foreach (var x in s.Precos.Take(15)) saida.WriteLine($"  forca {x.Forca} tipo {x.Tipo}: valor {x.Valor:N0} preco {x.Preco:N0}");
    }

    // uma temporada da Modificada com os acrescimos (Regras.Acrescimos)
    [Fact]
    public void CurtoAcrescimos() => Rodar(new("mod8-acr", Mod(8) with { Acrescimos = true }, 1, true, true, false, 1, 1));

    // duas temporadas da Modificada com o K escolhido (Regras.SobemDescemEscolhido; 4 com 8
    // equipas vale 3), com recargas do save (a marca do K tem de sobreviver)
    [Theory]
    [InlineData(8, 1)]
    [InlineData(8, 4)]
    [InlineData(10, 3)]
    [InlineData(12, 4)]
    public void CurtoSobemDescem(int n, int k) =>
        Rodar(new($"mod{n}-k{k}", Mod(n) with { SobemDescemEscolhido = k }, 1, false, true, false, 2, 1, Recarregar: 0.2));

    [Theory]
    [MemberData(nameof(Configs))]
    public void Longo_(string nome)
    {
        if (!Longo) return;
        foreach (var sem in Sementes) Rodar(Config(nome, sem, Temporadas));
    }
}
