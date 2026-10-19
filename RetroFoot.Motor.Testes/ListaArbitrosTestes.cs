using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.NovoJogo;
using Xunit;

namespace RetroFoot.Motor.Testes;

public class ListaArbitrosTestes
{
    static byte[] T(string s) => Encoding.Latin1.GetBytes(s);
    static string S(byte[] b) => Encoding.Latin1.GetString(b);

    static readonly List<byte[]> Original = new() { T("BRA Arnaldo Cezar Coelho"), T("ESC Hugh Dallas"), T("POR Vitor Pereira") };

    [Fact]
    public void FifaSubstituiOsDoMesmoPais()
    {
        var fifa = new List<(string, string)> { ("BRA", "Wilton Sampaio"), ("POR", "Artur Soares Dias") };
        var l = ListaArbitros.Juntar(Original, fifa).Select(S).ToList();
        Assert.Equal(new[] { "BRA Wilton Sampaio", "POR Artur Soares Dias", "ESC Hugh Dallas" }, l);
    }

    [Fact]
    public void NomeEmLatin1ComNoMaximo40Letras()
    {
        Assert.Equal("POL Szymon Marciniak", S(ListaArbitros.Texto("POL", "Szymon Marciniak")));
        Assert.Equal("POL Pawel Raczkowski", S(ListaArbitros.Texto("POL", "Paweł Raczkowski")));
        Assert.Equal("TUR Halil Umut Meler", S(ListaArbitros.Texto("TUR", "Halil Umut Meler")));
        var longo = ListaArbitros.Texto("BRA", "Fulano Beltrano Sicrano de Tal da Silva Santos Pereira");
        Assert.True(Dados.PartirTxe(longo, 99).Nome.Length <= ListaArbitros.MaximoNome);
        Assert.Equal("BRA Fulano Beltrano Sicrano de Tal da Silva", S(longo));
    }

    static List<Pais> Paises(params (string Codigo, int Vagas, bool Sel)[] p) =>
        p.Select(x => new Pais { Codigo = x.Codigo, Vagas = x.Vagas, Selecionado = x.Sel }).ToList();

    [Fact]
    public void UmSoPaisFicaSoComOsDele()
    {
        var lista = Enumerable.Range(1, 6).Select(i => T("BRA Arbitro " + i)).Concat(new[] { T("ESC Hugh Dallas") }).ToList();
        // 10 equipas: precisa de 6
        var so = ListaArbitros.SoDoPaisEscolhido(lista, Paises(("BRA", 10, true), ("ESC", 8, false)));
        Assert.Equal(6, so.Count);
        Assert.All(so, t => Assert.StartsWith("BRA ", S(t)));
        // dois paises: a lista toda
        Assert.Equal(7, ListaArbitros.SoDoPaisEscolhido(lista, Paises(("BRA", 10, true), ("ESC", 8, true))).Count);
        // poucos arbitros para as equipas do pais: os dele repetem-se, nunca um estrangeiro
        var rep = ListaArbitros.SoDoPaisEscolhido(lista, Paises(("BRA", 20, true)));
        Assert.Equal(11, rep.Count);
        Assert.All(rep, t => Assert.StartsWith("BRA ", S(t)));
        Assert.Equal(new[] { T("ESC Hugh Dallas"), T("ESC Hugh Dallas") }, ListaArbitros.SoDoPaisEscolhido(lista, Paises(("ESC", 2, true))));
        // pais sem nenhum arbitro: a lista toda
        Assert.Equal(7, ListaArbitros.SoDoPaisEscolhido(lista, Paises(("POR", 10, true))).Count);
    }

    // so o Brasil, 20 equipas por divisao e o maximo de equipas (128, com as do Brasil
    // copiadas ate passar disso): so arbitros do Brasil, e da lista (sem repetir)
    [Fact]
    public void SoBrasilCom128Equipas()
    {
        var assets = AcharAssets();
        if (assets == null) return;
        var d = RetroFoot.Motor.Arquivos.PastaDados.Ler(Path.Combine(assets, "Dados"));
        var bra = d.Equipas.Where(a => d.PaisDoArquivo(a.Eft.Pais) == "BRA").ToList();
        Assert.NotEmpty(bra);
        for (int i = 0; d.Equipas.Count(a => d.PaisDoArquivo(a.Eft.Pais) == "BRA") < 150; i++)
            d.Equipas.Add(new Arquivo($"ZZ{i:D3}" + bra[i % bra.Count].Nome, bra[i % bra.Count].Eft));
        var regras = Regras.Regras.Launcher(20);
        var sel = d.ContarEquipas();
        sel.Minimo = regras.MinimoEquipas;
        sel.Escolher("BRA");
        sel.Confirmar(new Aleatorio(1));
        Assert.Equal(SelecaoPaises.MaximoEquipas, sel.Escolhidas);
        var original = d.Arbitro;
        d.Arbitro = ListaArbitros.SoDoPaisEscolhido(original, d.Paises);
        var e = Criacao.Criar(d, new Opcoes { Moeda = Tabelas.MoedaReais, Ano = 2026, Humanos = new() { "Teste" }, Regras = regras }, new Aleatorio(7));
        Assert.Equal(128, e.Equipas.Count);
        Assert.All(e.Arbitros, a => Assert.Equal("BRA", a.Pais));
        Assert.True(e.Arbitros.Count >= 64);
        Assert.Equal(e.Arbitros.Count, e.Arbitros.Select(a => S(a.Nome)).Distinct().Count());
        e.IniciarTemporada();
        e.PrepararJornada(1, new Aleatorio(9));
    }

    // o arbitros.tsv do jogo: so paises do paises.tsv, nomes que cabem, e um jogo novo so com
    // um pais (se o jogo original estiver na maquina) so com arbitros desse pais (os paises
    // com equipas bastantes para 4 divisoes de 8)
    [Fact]
    public void ArquivoDoJogo()
    {
        var assets = AcharAssets();
        if (assets == null) return;
        var codigos = File.ReadAllLines(Path.Combine(assets, "paises.tsv")).Select(l => l.Split('\t')[0]).ToHashSet();
        var fifa = ListaArbitros.LerTsv(File.ReadAllLines(Path.Combine(assets, "arbitros.tsv")));
        Assert.NotEmpty(fifa);
        Assert.All(fifa, a => Assert.Contains(a.Pais, codigos));
        Assert.Equal(fifa.Count, fifa.Distinct().Count());

        var raiz = Arquivos.Raiz();
        if (raiz == null) return;
        foreach (var pais in new[] { "BRA", "ING", "POR", "ITA" })
        {
            var d = Dados.Ler(raiz);
            var sel = d.ContarEquipas();
            sel.Escolher(pais);
            sel.Confirmar(new Aleatorio(1));
            d.Arbitro = ListaArbitros.ParaJogoNovo(d.Arbitro, fifa, d.Paises);
            var e = Criacao.Criar(d, new Opcoes { Moeda = Tabelas.MoedaReais, Ano = 2026, Humanos = new() { "Teste" }, Regras = Regras.Regras.Original }, new Aleatorio(7));
            Assert.All(e.Arbitros, a => Assert.Equal(pais, a.Pais));
        }
    }

    static string? AcharAssets()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "RetroFoot.App", "Assets");
            if (File.Exists(Path.Combine(p, "arbitros.tsv"))) return p;
        }
        return null;
    }
}
