using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RetroFoot.Motor.Arquivos;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.NovoJogo;
using Xunit;
using Xunit.Abstractions;
using Dados = RetroFoot.Motor.NovoJogo.Dados;

namespace RetroFoot.Motor.Testes;

// A pasta de dados do RetroFoot (Motor/Arquivos): .EFT <-> JSON byte a byte, .TXE <-> texto, a
// leitura igual a da pasta do jogo classico e a conversao dos arquivos deixados na pasta.
public class PastaDadosTestes(ITestOutputHelper saida)
{
    static string Jogo() => Arquivos.Raiz() ?? throw new InvalidOperationException("jogo original não encontrado");

    static string[] Eqs() => Directory.GetFiles(Path.Combine(Jogo(), "EQUIPAS"), "*.EFT", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive });

    static string Temp()
    {
        var p = Path.Combine(Path.GetTempPath(), "retrofoot-teste-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    // o COUNTRY.TXE de um patch soma ao countries.tsv: os nomes do patch valem, os paises
    // novos entram e os que o patch nao traz ficam (antes o arquivo era trocado inteiro)
    [Fact]
    public void PatchDePaisesSomaAoQueHa()
    {
        var raiz = Temp();
        var origem = Temp();
        try
        {
            var arq = Path.Combine(raiz, PastaDados.Paises);
            TextosDados.GravarPaises(arq, new[] { new TextosDados.Pais("BRA", "Brasil"), new TextosDados.Pais("IDN", "Indonésia") });
            var txe = Path.Combine(origem, "COUNTRY.TXE");
            File.WriteAllBytes(txe, TextosDados.Txe("Country", new[] { "BRA BRASIL", "XYZ TERRA" }));
            PastaDados.Importar(txe, raiz);
            var l = TextosDados.LerPaises(arq);
            Assert.Equal(new[] { "BRA", "IDN", "XYZ" }, l.Select(p => p.Codigo));
            Assert.Equal("BRASIL", l[0].Nome);
            Assert.Equal("Indonésia", l[1].Nome);
        }
        finally { Directory.Delete(raiz, true); Directory.Delete(origem, true); }
    }

    // o catalogo completa countries.tsv so com o que falta, sem mexer no resto; a 2a vez nao muda nada
    [Fact]
    public void CatalogoCompletaPaisesSemRepetir()
    {
        var raiz = Temp();
        try
        {
            var arq = Path.Combine(raiz, PastaDados.Paises);
            TextosDados.GravarPaises(arq, new[] { new TextosDados.Pais("BRA", "BRASIL"), new TextosDados.Pais("ZZZ", "Meu país") });
            int n = PastaDados.CompletarPaises(raiz);
            var l = TextosDados.LerPaises(arq);
            Assert.Equal(Dados.Catalogo.Count + 1, l.Count);
            Assert.Equal(Dados.Catalogo.Count - 1, n);
            Assert.Equal("BRASIL", l[0].Nome);
            Assert.Equal("ZZZ", l[1].Codigo);
            Assert.Contains(l, p => p.Codigo == "IDN");
            Assert.Contains(l, p => p.Codigo == "KOS");
            var antes = File.GetLastWriteTimeUtc(arq);
            Assert.Equal(0, PastaDados.CompletarPaises(raiz));
            Assert.Equal(antes, File.GetLastWriteTimeUtc(arq));
        }
        finally { Directory.Delete(raiz, true); }
    }

    // um pais das equipas fora da lista nao para mais o jogo novo: entra com o nome do
    // catalogo do jogo ou, fora dele, com o proprio codigo
    [Fact]
    public void PaisQueFaltaEntraNaLista()
    {
        var d = new Dados();
        Assert.Equal("IDN", d.PaisDoArquivo("idn"));
        Assert.Equal("Indonésia", Encoding.Latin1.GetString(Dados.AcharPais(d.Paises, "IDN")!.Nome));
        Assert.Equal("QQQ", d.PaisDoArquivo("QQQ"));
        Assert.Equal("QQQ", Encoding.Latin1.GetString(Dados.AcharPais(d.Paises, "QQQ")!.Nome));
        Assert.Equal(2, d.Paises.Count);
    }

    [Fact]
    public void EftJsonEftIgual()
    {
        var arqs = Eqs();
        Assert.True(arqs.Length >= 200);
        foreach (var a in arqs)
        {
            var b = File.ReadAllBytes(a);
            var json = EquipaJson.DeEft(b, Path.GetFileName(a));
            var l = EquipaJson.ParaEft(json);
            Assert.Equal(Path.GetFileName(a), l.Arquivo);
            Assert.True(l.Bytes.AsSpan().SequenceEqual(b), $"{a}: a volta deu outro .EFT");
            // e o JSON gravado de novo e o mesmo texto (ordem fixa)
            Assert.Equal(json, EquipaJson.DeEft(l.Bytes, l.Arquivo));
        }
        saida.WriteLine($"{arqs.Length} equipas: .EFT -> JSON -> .EFT iguais");
    }

    // bytes fora do normal (reservado, 4o byte das cores, sobra no fim) tambem voltam iguais
    [Fact]
    public void EftEstranhoVoltaIgual()
    {
        var a = Eqs()[0];
        var b = File.ReadAllBytes(a).ToList();
        b[10] = 0x7F;
        var e = Eft.Decodificar(b.ToArray());
        int cores = 0x32 + 1 + e.NomeCompleto.Length + 1 + e.NomeAbreviado.Length;
        b[cores + 3] = 9;
        b.AddRange(new byte[] { 1, 2, 3 });
        var x = b.ToArray();
        var json = EquipaJson.DeEft(x, "X.EFT");
        Assert.Contains("\"eft\"", json);
        Assert.True(EquipaJson.ParaEft(json).Bytes.AsSpan().SequenceEqual(x));
    }

    [Fact]
    public void JsonEditadoAMao()
    {
        var json = """
        {
          // comentario
          "name": "Clube Łódź", "shortName": "Łódź", "country": "pol", "level": 7,
          "textColor": "#ffffff", "backgroundColor": "#C00000", "coach": "Treinador",
          "players": [
            { "name": "Um", "position": "GK", "country": "POL" },
            { "name": "Dois", "position": "d", "country": "POL" },
            { "name": "Tres", "position": 2, "country": "BRA" },
            { "name": "Quatro", "position": "Avançado", "country": "POL" },
          ],
        }
        """;
        var l = EquipaJson.ParaEft(json, "/x/teams/POL/lodz.json");
        Assert.Equal("LODZ.EFT", l.Arquivo);
        var e = Eft.Decodificar(l.Bytes);
        Assert.Equal("Clube Lódz", Encoding.Latin1.GetString(e.NomeCompleto));
        Assert.Equal(new[] { 0, 1, 2, 3 }, e.Jogadores.Select(j => j.Posicao));
        Assert.Equal(0xFFFFFFu, e.CorLetra);
        Assert.Equal("sc-palmeiras", EquipaJson.Slug("SC Palmeiras"));
        Assert.Equal("sao-paulo-fc", EquipaJson.Slug(" São Paulo F.C. "));
    }

    // a pasta do jogo classico importada para a pasta nova da os mesmos Dados e o mesmo jogo novo
    [Fact]
    public void PastaNovaIgualAOriginal()
    {
        var raiz = Temp();
        try
        {
            var r = PastaDados.Importar(Jogo(), raiz);
            Assert.Empty(r.Erros);
            var velho = Dados.Ler(Jogo());
            var novo = Dados.Ler(raiz);
            Assert.True(novo.ArbitrosProntos);
            Assert.Equal(velho.Paises.Select(p => p.Codigo + "=" + Convert.ToHexString(p.Nome)), novo.Paises.Select(p => p.Codigo + "=" + Convert.ToHexString(p.Nome)));
            Assert.Equal(velho.Bosman.Select(p => p.Codigo), novo.Bosman.Select(p => p.Codigo));
            Assert.Equal(velho.PLOP.Select(p => p.Codigo), novo.PLOP.Select(p => p.Codigo));
            Assert.Equal(velho.Arbitro.Select(Convert.ToHexString), novo.Arbitro.Select(Convert.ToHexString));
            Assert.Equal(velho.Equipas.Select(a => a.Nome), novo.Equipas.Select(a => a.Nome));
            for (int i = 0; i < velho.Equipas.Count; i++)
                Assert.True(velho.Equipas[i].Eft.Codificar().AsSpan().SequenceEqual(novo.Equipas[i].Eft.Codificar()), velho.Equipas[i].Nome);
            var (b1, p1) = Dados.ListasDoJogo(Jogo());
            var (b2, p2) = Dados.ListasDoJogo(raiz);
            Assert.Equal(b1, b2);
            Assert.Equal(p1, p2);
            Assert.True(Jogo98Novo(velho).AsSpan().SequenceEqual(Jogo98Novo(novo)), "o jogo novo saiu diferente");
            saida.WriteLine($"{novo.Equipas.Count} equipas, {novo.Paises.Count} países, {novo.Arbitro.Count} árbitros: iguais");

            // e de volta aos .TXE: os mesmos textos
            var volta = Temp();
            PastaDados.ExportarListas(raiz, volta);
            Assert.Equal(TextosDados.LerTxe(File.ReadAllBytes(Path.Combine(Jogo(), "COUNTRY.TXE"))).Select(Convert.ToHexString),
                TextosDados.LerTxe(File.ReadAllBytes(Path.Combine(volta, "COUNTRY.TXE"))).Select(Convert.ToHexString));
            Assert.Equal(TextosDados.LerTxe(File.ReadAllBytes(Path.Combine(Jogo(), "REFEREE.TXE"))).Select(Convert.ToHexString),
                TextosDados.LerTxe(File.ReadAllBytes(Path.Combine(volta, "REFEREE.TXE"))).Select(Convert.ToHexString));
            Assert.Equal(velho.Equipas.Count, PastaDados.ExportarEquipas(raiz, Path.Combine(volta, "EQUIPAS")));
            foreach (var f in Eqs())
                Assert.True(File.ReadAllBytes(f).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(volta, "EQUIPAS", Path.GetFileName(f)))), f);
            Directory.Delete(volta, true);
        }
        finally { Directory.Delete(raiz, true); }
    }

    static byte[] Jogo98Novo(Dados d)
    {
        var sel = d.ContarEquipas();
        sel.Todos();
        sel.Confirmar(new Aleatorio(0x12345678));
        var g = new Aleatorio(0x2A0B1307);
        var e = Criacao.Criar(d, new Opcoes { Moeda = Tabelas.MoedaReais, Ano = 2026, Humanos = new() { "Teste" } }, g);
        e.IniciarTemporada();
        e.PrepararJornada(1, g);
        return e.Save("x").Gravar();
    }

    // os arquivos do jogo classico deixados na pasta sao convertidos; os originais vao para backup/
    [Fact]
    public void ArquivosSoltosConvertidos()
    {
        var raiz = Temp();
        try
        {
            var jogo = Jogo();
            var eqs = Eqs().OrderBy(f => f).ToList();
            // uma pasta EQUIPAS, um .EFT em teams/, COUNTRY.TXE e REFEREE.TXE na raiz, CTRGROUP
            Directory.CreateDirectory(Path.Combine(raiz, "EQUIPAS"));
            foreach (var f in eqs.Take(5)) File.Copy(f, Path.Combine(raiz, "EQUIPAS", Path.GetFileName(f)));
            Directory.CreateDirectory(Path.Combine(raiz, "teams", "XYZ"));
            File.Copy(eqs[10], Path.Combine(raiz, "teams", "XYZ", Path.GetFileName(eqs[10])));
            File.WriteAllText(Path.Combine(raiz, "countries.tsv"), "code\tname\nPOR\tPortugal\n");
            File.Copy(Path.Combine(jogo, "COUNTRY.TXE"), Path.Combine(raiz, "country.txe"));
            File.Copy(Path.Combine(jogo, "REFEREE.TXE"), Path.Combine(raiz, "REFEREE.TXE"));
            Directory.CreateDirectory(Path.Combine(raiz, "CTRGROUP"));
            File.Copy(Path.Combine(jogo, "CTRGROUP", "BOSMAN.TXE"), Path.Combine(raiz, "CTRGROUP", "BOSMAN.TXE"));

            var r = PastaDados.ConverterSoltos(raiz);
            saida.WriteLine(r.ToString());
            Assert.Empty(r.Erros);
            Assert.Equal(6, r.Equipas);
            Assert.False(Directory.Exists(Path.Combine(raiz, "EQUIPAS")));
            Assert.False(File.Exists(Path.Combine(raiz, "country.txe")));
            Assert.Empty(Directory.GetFiles(Path.Combine(raiz, "teams"), "*.EFT", SearchOption.AllDirectories));
            Assert.Equal(6, PastaDados.JsonsDeEquipas(raiz).Count());
            Assert.Equal("code\tname\nPOR\tPortugal\n", File.ReadAllText(Path.Combine(raiz, "countries.tsv.bak")));
            Assert.True(File.Exists(Path.Combine(raiz, "country-groups", "bosman.txt")));
            var bk = Directory.GetDirectories(Path.Combine(raiz, "backup")).Single();
            Assert.True(File.Exists(Path.Combine(bk, "country.txe")));
            Assert.Equal(5, Directory.GetFiles(Path.Combine(bk, "EQUIPAS")).Length);

            // de novo: nada a fazer; o mesmo .EFT outra vez substitui o JSON (nao duplica)
            Assert.False(PastaDados.ConverterSoltos(raiz).Mudou);
            File.Copy(eqs[0], Path.Combine(raiz, "teams", Path.GetFileName(eqs[0])));
            Assert.Equal(1, PastaDados.ConverterSoltos(raiz).Equipas);
            Assert.Equal(6, PastaDados.JsonsDeEquipas(raiz).Count());
            var d = Dados.Ler(raiz);
            Assert.Equal(6, d.Equipas.Count);
        }
        finally { Directory.Delete(raiz, true); }
    }

    static readonly Dictionary<string, string> Corrigidas = new() { ["CRUZEIRO.EFT"] = "Levir Culpi", ["INTER_BR.EFT"] = "Márcio Araújo" };

    // os dados que vao com o jogo (RetroFoot.App/Assets/Dados) sao as equipas originais de 1998
    [Fact]
    public void PacoteSaoAsEquipasOriginais()
    {
        string? pacote = null;
        for (var d = AppContext.BaseDirectory; d != null; d = Path.GetDirectoryName(d))
            if (Directory.Exists(Path.Combine(d, "RetroFoot.App", "Assets", "Dados"))) { pacote = Path.Combine(d, "RetroFoot.App", "Assets", "Dados"); break; }
        Assert.NotNull(pacote);
        var originais = Eqs().ToDictionary(f => Path.GetFileName(f).ToUpperInvariant());
        var jsons = PastaDados.JsonsDeEquipas(pacote!).ToList();
        Assert.Equal(originais.Count, jsons.Count);
        foreach (var j in jsons)
        {
            var l = EquipaJson.LerArquivo(j);
            Assert.True(originais.TryGetValue(l.Arquivo.ToUpperInvariant(), out var f), j);
            // as correcoes da comunidade: o treinador que o arquivo de 1998 trazia em branco (o
            // resto da equipa igual ao original)
            if (Corrigidas.TryGetValue(l.Arquivo.ToUpperInvariant(), out var tecnico))
            {
                var orig = Eft.Decodificar(File.ReadAllBytes(f!));
                var nova = Eft.Decodificar(l.Bytes);
                Assert.Equal(tecnico, System.Text.Encoding.Latin1.GetString(nova.Treinador).Trim());
                nova.Treinador = orig.Treinador;
                Assert.True(orig.Codificar().AsSpan().SequenceEqual(nova.Codificar()), j);
                continue;
            }
            Assert.True(File.ReadAllBytes(f!).AsSpan().SequenceEqual(l.Bytes), j);
        }
        var d0 = Dados.Ler(pacote!);
        Assert.Equal(originais.Count, d0.Equipas.Count);
        Assert.True(d0.Arbitro.Count > 500);
        d0.ContarEquipas();
    }
}
