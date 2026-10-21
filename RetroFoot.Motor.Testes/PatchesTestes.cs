using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RetroFoot.Motor.Arquivos;
using RetroFoot.Motor.Base;
using Xunit;
using Xunit.Abstractions;
using Dados = RetroFoot.Motor.NovoJogo.Dados;

namespace RetroFoot.Motor.Testes;

// A biblioteca de patches (Motor/Arquivos/BibliotecaPatches.cs): aplicar um patch do jogo classico
// ou do RetroFoot na pasta de dados e reverter deixa a pasta exatamente como estava; conflitos
// entre patches; mudancas feitas depois do patch ficam guardadas ao reverter.
public class PatchesTestes(ITestOutputHelper saida)
{
    static string Jogo() => Arquivos.Raiz() ?? throw new InvalidOperationException("jogo original não encontrado");

    static List<string> Eqs() => Directory.GetFiles(Path.Combine(Jogo(), "EQUIPAS"), "*.EFT", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive })
        .OrderBy(f => f, StringComparer.Ordinal).ToList();

    static string Temp()
    {
        var p = Path.Combine(Path.GetTempPath(), "retrofoot-patch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    // uma pasta de dados com o jogo original
    static string Pasta()
    {
        var raiz = Temp();
        Assert.Empty(PastaDados.Importar(Jogo(), raiz).Erros);
        return raiz;
    }

    // o estado da pasta (sem patches/ e backup/): caminho -> hash
    static SortedDictionary<string, string> Foto(string raiz)
    {
        var r = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in Directory.EnumerateFiles(raiz, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(raiz, f).Replace('\\', '/');
            if (rel.StartsWith("patches/") || rel.StartsWith("backup/")) continue;
            r[rel] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)));
        }
        return r;
    }

    static void Igual(SortedDictionary<string, string> a, SortedDictionary<string, string> b)
    {
        Assert.Equal(a.Keys, b.Keys);
        foreach (var k in a.Keys) Assert.True(a[k] == b[k], k);
    }

    static string Zip(string pasta, string nome, params (string Rel, byte[] B)[] arqs)
    {
        var z = Path.Combine(pasta, nome + ".zip");
        using var a = ZipFile.Open(z, ZipArchiveMode.Create);
        foreach (var (rel, b) in arqs)
        {
            using var s = a.CreateEntry(rel).Open();
            s.Write(b);
        }
        return z;
    }

    [Fact]
    public void PatchDoJogoClassicoAplicaEReverte()
    {
        var raiz = Pasta();
        var fora = Temp();
        try
        {
            var eqs = Eqs();
            var antes = Foto(raiz);
            var flag = Directory.GetFiles(Path.Combine(raiz, "flags")).OrderBy(f => f).First();
            // eqs[0] passa a ter o conteudo de eqs[100] (pode mudar de pais); eqs[1] igual; NOVA.EFT nova
            var zip = Zip(fora, "Patch 98",
                ("Patch 98/EQUIPAS/" + Path.GetFileName(eqs[0]), File.ReadAllBytes(eqs[100])),
                ("Patch 98/EQUIPAS/" + Path.GetFileName(eqs[1]), File.ReadAllBytes(eqs[1])),
                ("Patch 98/EQUIPAS/NOVA.EFT", File.ReadAllBytes(eqs[50])),
                ("Patch 98/COUNTRY.TXE", File.ReadAllBytes(Path.Combine(Jogo(), "COUNTRY.TXE"))),
                ("Patch 98/FLAGS/ZZZ.BMP", new byte[] { 1, 2, 3 }),
                ("Patch 98/FLAGS/" + Path.GetFileName(flag), new byte[] { 4, 5, 6 }),
                ("Patch 98/leia-me.txt", Encoding.Latin1.GetBytes("--------\r\nPatch de teste\r\nSubstitua a pasta EQUIPAS.\r\n")),
                ("Patch 98/JOGO.EXE", new byte[] { 0x4D, 0x5A }));
            var nome = BibliotecaPatches.Adicionar(raiz, zip);
            Assert.Equal("Patch 98", nome);
            Assert.Equal(nome, BibliotecaPatches.Adicionar(raiz, zip));   // o mesmo zip: nao duplica
            var info = BibliotecaPatches.Listar(raiz).Single();
            Assert.Equal(3, info.Equipas);
            Assert.True(info.Paises);
            Assert.True(info.PastaEquipas);
            Assert.Equal(2, info.Bandeiras);
            Assert.Equal(1, info.Ignorados);
            Assert.StartsWith("Patch de teste", info.Descricao);
            Assert.False(info.Aplicado);

            var an = BibliotecaPatches.Analisar(raiz, nome, false);
            Assert.Equal(2, an.EquipasSubstituidas);
            Assert.Equal(1, an.EquipasNovas);
            Assert.Empty(an.Conflitos);
            Assert.Equal(eqs.Count - 2, BibliotecaPatches.Analisar(raiz, nome, true).EquipasRetiradas);

            // juntar as equipas
            var r = BibliotecaPatches.Aplicar(raiz, nome, false);
            Assert.True(r.Ok, r.Erro);
            Assert.Empty(r.Avisos);
            Assert.Equal(3, r.Equipas);
            Assert.True(BibliotecaPatches.Listar(raiz).Single().Aplicado);
            Assert.Equal("aplicado", BibliotecaPatches.Aplicar(raiz, nome, false).Erro);
            var d = Dados.Ler(raiz);
            // NOVA.EFT e o mesmo clube de eqs[50] (o mesmo pais e nome curto): substitui-o, nao
            // fica duplicado (volta ao reverter)
            Assert.Equal(1, r.Substituidas);
            Assert.Equal(eqs.Count, d.Equipas.Count);
            Assert.DoesNotContain(d.Equipas, a => a.Nome.Equals(Path.GetFileName(eqs[50]), StringComparison.OrdinalIgnoreCase));
            var e0 = d.Equipas.Single(a => a.Nome.Equals(Path.GetFileName(eqs[0]), StringComparison.OrdinalIgnoreCase));
            Assert.True(e0.Eft.Codificar().AsSpan().SequenceEqual(File.ReadAllBytes(eqs[100])));
            Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(flag));
            var rv = BibliotecaPatches.Reverter(raiz, nome);
            Assert.True(rv.Ok, rv.Erro);
            Assert.Null(rv.CopiaDasMudancas);
            Igual(antes, Foto(raiz));
            Assert.False(BibliotecaPatches.Listar(raiz).Single().Aplicado);

            // como no jogo classico: a pasta EQUIPAS inteira trocada
            r = BibliotecaPatches.Aplicar(raiz, nome, true);
            Assert.True(r.Ok, r.Erro);
            Assert.Equal(eqs.Count - 2, r.Retirados);
            Assert.Equal(3, Dados.Ler(raiz).Equipas.Count);
            Assert.True(BibliotecaPatches.Reverter(raiz, nome).Ok);
            Igual(antes, Foto(raiz));
            Assert.Equal(eqs.Count, Dados.Ler(raiz).Equipas.Count);
        }
        finally { Directory.Delete(raiz, true); Directory.Delete(fora, true); }
    }

    [Fact]
    public void ConflitosEOrdemDeReverter()
    {
        var raiz = Pasta();
        var fora = Temp();
        try
        {
            var eqs = Eqs();
            var antes = Foto(raiz);
            var a = BibliotecaPatches.Adicionar(raiz, Zip(fora, "A", ("EQUIPAS/" + Path.GetFileName(eqs[3]), File.ReadAllBytes(eqs[7])),
                ("EQUIPAS/" + Path.GetFileName(eqs[4]), File.ReadAllBytes(eqs[8]))));
            var b = BibliotecaPatches.Adicionar(raiz, Zip(fora, "B", ("EQUIPAS/" + Path.GetFileName(eqs[3]), File.ReadAllBytes(eqs[9]))));
            Assert.True(BibliotecaPatches.Aplicar(raiz, a, false).Ok);
            var an = BibliotecaPatches.Analisar(raiz, b, false);
            Assert.Equal(("A", 1), (an.Conflitos.Single().Patch, an.Conflitos.Single().Comuns));
            var depoisA = Foto(raiz);
            Assert.True(BibliotecaPatches.Aplicar(raiz, b, false).Ok);
            // A nao pode voltar antes de B (B mexeu na mesma equipa)
            var rv = BibliotecaPatches.Reverter(raiz, a);
            Assert.False(rv.Ok);
            Assert.Equal("B", rv.Bloqueadores.Single());
            Assert.True(BibliotecaPatches.Reverter(raiz, b).Ok);
            Igual(depoisA, Foto(raiz));
            Assert.True(BibliotecaPatches.Reverter(raiz, a).Ok);
            Igual(antes, Foto(raiz));

            // uma mudanca feita depois do patch: guardada em backup/ ao reverter
            Assert.True(BibliotecaPatches.Aplicar(raiz, a, false).Ok);
            var j = PastaDados.IndiceEquipas(raiz)[Path.GetFileName(eqs[3]).ToUpperInvariant()];
            File.AppendAllText(j, "\n");
            rv = BibliotecaPatches.Reverter(raiz, a);
            Assert.True(rv.Ok);
            Assert.NotNull(rv.CopiaDasMudancas);
            Assert.Single(Directory.GetFiles(rv.CopiaDasMudancas!, "*.json", SearchOption.AllDirectories));
            Igual(antes, Foto(raiz));

            // um patch que trocou todas as equipas conflita com qualquer equipa de outro
            var c = BibliotecaPatches.Adicionar(raiz, Zip(fora, "C", ("EQUIPAS/" + Path.GetFileName(eqs[30]), File.ReadAllBytes(eqs[30]))));
            Assert.True(BibliotecaPatches.Aplicar(raiz, c, true).Ok);
            Assert.Equal("C", BibliotecaPatches.Analisar(raiz, b, false).Conflitos.Single().Patch);
            Assert.True(BibliotecaPatches.Aplicar(raiz, b, false).Ok);
            Assert.Equal("B", BibliotecaPatches.Bloqueadores(raiz, c).Single().Nome);
            Assert.True(BibliotecaPatches.Reverter(raiz, b).Ok);
            Assert.True(BibliotecaPatches.Reverter(raiz, c).Ok);
            Igual(antes, Foto(raiz));
        }
        finally { Directory.Delete(raiz, true); Directory.Delete(fora, true); }
    }

    // o patch do RetroFoot (Editor de Equipas): equipas em JSON e logos com o indice
    [Fact]
    public void PatchDoRetroFootComLogos()
    {
        var origem = Pasta();
        var raiz = Pasta();
        var fora = Temp();
        try
        {
            var antes = Foto(raiz);
            var indice = PastaDados.IndiceEquipas(origem);
            var json = indice[Path.GetFileName(Eqs()[20]).ToUpperInvariant()];
            var l = EquipaJson.LerArquivo(json);
            var (pais, nomeEq) = EquipaJson.Identidade(l.Bytes);
            // a equipa editada (outro treinador) e um logo novo
            var txt = File.ReadAllText(json).Replace("\"coach\": \"", "\"coach\": \"Novo ");
            File.WriteAllText(json, txt);
            Directory.CreateDirectory(Path.Combine(origem, "logos"));
            File.WriteAllBytes(Path.Combine(origem, "logos", "meu.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            File.WriteAllText(Path.Combine(origem, "logos", "logos.tsv"), $"country\tteam\tfile\n{pais}\t{nomeEq}\tmeu.png\nBRA\tOutra\toutra.png\n");
            // o jogador ja tinha um indice: a linha da equipa troca, as outras ficam
            Directory.CreateDirectory(Path.Combine(raiz, "logos"));
            File.WriteAllText(Path.Combine(raiz, "logos", "logos.tsv"), $"# meu\ncountry\tteam\tfile\n{pais}\t{nomeEq.ToLowerInvariant()}\tvelho.png\nARG\tX\tx.png\n");
            antes = Foto(raiz);

            var rel = Path.GetRelativePath(origem, json).Replace('\\', '/');
            var zip = Path.Combine(fora, "Minha equipa.zip");
            BibliotecaPatches.Criar(origem, zip, new("Minha equipa", "Treinador novo e logo", "Teste", "1.2"), new[] { rel, "logos/meu.png" });
            var nome = BibliotecaPatches.Adicionar(raiz, zip);
            var info = BibliotecaPatches.Listar(raiz).Single();
            Assert.True(info.RetroFoot);
            Assert.Equal("Minha equipa", info.Titulo);
            Assert.Equal("Treinador novo e logo", info.Descricao);
            Assert.Equal("1.2", info.Versao);
            Assert.Equal(1, info.Equipas);
            Assert.Equal(1, info.Logos);
            Assert.True(BibliotecaPatches.Aplicar(raiz, nome, false).Ok);
            var novo = File.ReadAllText(PastaDados.IndiceEquipas(raiz)[Path.GetFileName(Eqs()[20]).ToUpperInvariant()]);
            Assert.Contains("\"coach\": \"Novo ", novo);
            Assert.True(File.Exists(Path.Combine(raiz, "logos", "meu.png")));
            var linhas = File.ReadAllLines(Path.Combine(raiz, "logos", "logos.tsv"));
            Assert.Equal(new[] { "# meu", "country\tteam\tfile", $"{pais}\t{nomeEq}\tmeu.png", "ARG\tX\tx.png" }, linhas);
            Assert.True(BibliotecaPatches.Reverter(raiz, nome).Ok);
            Igual(antes, Foto(raiz));
        }
        finally { Directory.Delete(origem, true); Directory.Delete(raiz, true); Directory.Delete(fora, true); }
    }

    // o zip do Editor de Equipas (PatchEquipas): retrofoot-patch.json, a equipa em JSON e tambem em
    // EQUIPAS/*.EFT (vale o JSON, conta uma vez, nao sugere trocar todas); entradas com ../ ficam de fora
    [Fact]
    public void PatchDoEditorDeEquipasEInseguro()
    {
        var raiz = Pasta();
        var fora = Temp();
        try
        {
            var antes = Foto(raiz);
            var eft = Eqs()[5];
            var arq = Path.GetFileName(eft).ToUpperInvariant();
            var json = PastaDados.IndiceEquipas(raiz)[arq];
            var rel = Path.GetRelativePath(raiz, json).Replace('\\', '/');
            var txt = File.ReadAllText(json).Replace("\"coach\": \"", "\"coach\": \"Editado ");
            var man = "{ \"format\": \"retrofoot-patch\", \"version\": 1, \"name\": \"Do editor\", \"kind\": \"teams\", \"teams\": 1, \"eft\": true }";
            var zip = Zip(fora, "editor",
                ("retrofoot-patch.json", Encoding.UTF8.GetBytes(man)),
                (rel, Encoding.UTF8.GetBytes(txt)),
                ("EQUIPAS/" + Path.GetFileName(eft), File.ReadAllBytes(eft)),
                ("../fora.png", new byte[] { 1 }),
                ("logos/../../fora2.png", new byte[] { 2 }));
            var nome = BibliotecaPatches.Adicionar(raiz, zip);
            var info = BibliotecaPatches.Listar(raiz).Single();
            Assert.True(info.RetroFoot);
            Assert.Equal("Do editor", info.Titulo);
            Assert.Equal(1, info.Equipas);
            Assert.False(info.PastaEquipas);
            Assert.Equal(0, info.Logos);
            var an = BibliotecaPatches.Analisar(raiz, nome, false);
            Assert.Equal(1, an.EquipasSubstituidas);
            Assert.Equal(0, an.EquipasNovas);
            var r = BibliotecaPatches.Aplicar(raiz, nome, false);
            Assert.True(r.Ok, r.Erro);
            Assert.Equal(1, r.Equipas);
            Assert.Contains("\"coach\": \"Editado ", File.ReadAllText(json));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(raiz)!, "fora.png")));
            Assert.False(File.Exists(Path.Combine(raiz, "fora2.png")));
            Assert.True(BibliotecaPatches.Reverter(raiz, nome).Ok);
            Igual(antes, Foto(raiz));
        }
        finally { Directory.Delete(raiz, true); Directory.Delete(fora, true); }
    }

    // RETROFOOT_PATCH_TESTE=<patch.zip grande>: aplica e reverte (nada e copiado para o repositorio)
    [Fact]
    public void PatchGrandeDeFora()
    {
        var zip = Environment.GetEnvironmentVariable("RETROFOOT_PATCH_TESTE");
        if (string.IsNullOrEmpty(zip) || !File.Exists(zip)) return;
        var raiz = Pasta();
        try
        {
            var antes = Foto(raiz);
            var nome = BibliotecaPatches.Adicionar(raiz, zip);
            var info = BibliotecaPatches.Ler(BibliotecaPatches.CaminhoZip(raiz, nome));
            var an = BibliotecaPatches.Analisar(raiz, nome, true);
            saida.WriteLine($"{info.Equipas} equipas ({info.EquipasPorPais.Count} países), {info.Bandeiras} bandeiras; substitui {an.EquipasSubstituidas}, novas {an.EquipasNovas}, retira {an.EquipasRetiradas}; países fora da lista: {string.Join(",", an.PaisesDesconhecidos)}");
            var t = System.Diagnostics.Stopwatch.StartNew();
            var r = BibliotecaPatches.Aplicar(raiz, nome, true);
            saida.WriteLine($"aplicar: {t.ElapsedMilliseconds} ms, {r.Equipas} equipas, {r.Arquivos} arquivos, {r.Retirados} retirados, {r.Avisos.Count} avisos");
            Assert.True(r.Ok, r.Erro);
            Assert.Equal(info.Equipas, Dados.Ler(raiz).Equipas.Count);
            t.Restart();
            var rv = BibliotecaPatches.Reverter(raiz, nome);
            saida.WriteLine($"reverter: {t.ElapsedMilliseconds} ms, {rv.Restaurados} restaurados, {rv.Apagados} apagados");
            Assert.True(rv.Ok, rv.Erro);
            Igual(antes, Foto(raiz));
        }
        finally { Directory.Delete(raiz, true); }
    }
}
