using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using RetroFoot.Motor.Arquivos;
using RetroFoot.Motor.NovoJogo;

// O conversor da pasta de dados do RetroFoot (o mesmo codigo que o jogo usa, Motor/Arquivos):
//   importar <patch.zip|pasta|arquivo> <raiz>   um patch do jogo classico para a pasta de dados
//   exportar <raiz> <pasta>                     teams/*.json -> EQUIPAS/*.EFT e as listas .TXE
//   validar  <patch.zip|pasta> <raiz temporaria> importa e confere a volta .EFT -> JSON -> .EFT
//   pacote   <pasta do jogo 98> <arbitros.tsv> <destino>  os dados que vao com o jogo
static class Programa
{
    static int Main(string[] a)
    {
        if (a.Length < 3) { Console.Error.WriteLine("uso: importar|exportar|validar|pacote ..."); return 2; }
        switch (a[0])
        {
            case "importar":
            {
                var r = PastaDados.Importar(a[1], a[2]);
                Console.WriteLine(r);
                foreach (var e in r.Erros) Console.WriteLine("  ERRO " + e);
                return r.Erros.Count > 0 ? 1 : 0;
            }
            case "exportar":
            {
                var erros = new List<string>();
                int n = PastaDados.ExportarEquipas(a[1], Path.Combine(a[2], "EQUIPAS"), erros);
                PastaDados.ExportarListas(a[1], a[2]);
                Console.WriteLine($"{n} equipas exportadas");
                foreach (var e in erros) Console.WriteLine("  ERRO " + e);
                return erros.Count > 0 ? 1 : 0;
            }
            case "validar": return Validar(a[1], a[2]);
            case "pacote": return Pacote(a[1], a[2], a[3]);
        }
        return 2;
    }

    // cada .EFT da origem: importado, e o JSON dele de volta tem de dar os mesmos bytes
    static int Validar(string origem, string raiz)
    {
        if (Directory.Exists(raiz) && Directory.EnumerateFileSystemEntries(raiz).Any()) { Console.Error.WriteLine("a raiz temporaria tem de estar vazia"); return 2; }
        Directory.CreateDirectory(raiz);
        var r = PastaDados.Importar(origem, raiz);
        Console.WriteLine(r);
        foreach (var e in r.Erros) Console.WriteLine("  ERRO " + e);
        // os .EFT da origem, pelo nome em maiusculas (o ultimo vale, como na importacao)
        var originais = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (origem.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var z = ZipFile.Open(origem, ZipArchiveMode.Read, Encoding.Latin1);
            foreach (var e in z.Entries.Where(e => e.Name.EndsWith(".EFT", StringComparison.OrdinalIgnoreCase)))
            {
                using var s = e.Open(); using var m = new MemoryStream(); s.CopyTo(m);
                originais[e.Name.ToUpperInvariant()] = m.ToArray();
            }
        }
        else
            foreach (var f in Directory.EnumerateFiles(origem, "*.EFT", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, RecurseSubdirectories = true }))
                originais[Path.GetFileName(f).ToUpperInvariant()] = File.ReadAllBytes(f);
        int iguais = 0, diferentes = 0, comExtra = 0;
        var vistos = new HashSet<string>();
        foreach (var j in PastaDados.JsonsDeEquipas(raiz))
        {
            var l = EquipaJson.LerArquivo(j);
            var k = l.Arquivo.ToUpperInvariant();
            vistos.Add(k);
            if (File.ReadAllText(j).Contains("\"eft\"")) comExtra++;
            if (originais.TryGetValue(k, out var b) && b.AsSpan().SequenceEqual(l.Bytes)) iguais++;
            else { diferentes++; Console.WriteLine("  DIFERENTE " + j); }
        }
        var faltam = originais.Keys.Where(k => !vistos.Contains(k)).ToList();
        foreach (var k in faltam) Console.WriteLine("  FALTA " + k);
        // os Dados do jogo novo lidos da pasta convertida
        var avisos = new List<string>();
        var d = PastaDados.Ler(raiz, avisos);
        SelecaoPaises? sel = null;
        try { sel = d.ContarEquipas(); }
        catch (Exception e) { avisos.Add("paises: " + e.Message); }
        Console.WriteLine($"origem {originais.Count} .EFT; JSON {vistos.Count}; ida e volta iguais {iguais}, diferentes {diferentes}, com bloco eft {comExtra}, faltam {faltam.Count}");
        Console.WriteLine($"jogo novo: {d.Equipas.Count} equipas, {d.Paises.Count} paises, {sel?.Lista.Count} paises com equipas, {d.Arbitro.Count} arbitros, bosman {d.Bosman.Count}, plop {d.PLOP.Count}, avisos {avisos.Count}");
        return diferentes == 0 && faltam.Count == 0 && r.Erros.Count == 0 && avisos.Count == 0 ? 0 : 1;
    }

    // os dados do jogo original de 1998 no formato novo: equipas, paises, grupos, bandeiras,
    // patrocinadores e os arbitros (os da FIFA do arbitros.tsv + os do original dos paises
    // que a FIFA nao tem, como ListaArbitros.Juntar)
    static int Pacote(string jogo, string arbitrosTsv, string destino)
    {
        Directory.CreateDirectory(destino);
        var r = PastaDados.Importar(jogo, destino);
        Console.WriteLine(r);
        foreach (var e in r.Erros) Console.WriteLine("  ERRO " + e);
        var fifa = File.ReadAllLines(arbitrosTsv, Encoding.UTF8)
            .Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t'))
            .Where(c => c.Length >= 2 && c[0].Trim().Length == 3 && c[1].Trim().Length > 0)
            .Select(c => new TextosDados.Arbitro(c[0].Trim().ToUpperInvariant(), c[1].Trim(), c.Length > 2 ? c[2].Trim() : "fifa")).ToList();
        var paisesFifa = fifa.Select(x => x.Pais).ToHashSet();
        var ref98 = Directory.EnumerateFiles(jogo).First(f => Path.GetFileName(f).Equals("REFEREE.TXE", StringComparison.OrdinalIgnoreCase));
        var original = TextosDados.ArbitrosDoTxe(File.ReadAllBytes(ref98), "1998");
        var todos = fifa.Concat(original.Where(x => !paisesFifa.Contains(x.Pais))).ToList();
        var arq = Path.Combine(destino, PastaDados.Arbitros);
        TextosDados.GravarArbitros(arq, todos);
        // os do original que a FIFA substituiu ficam no fim, comentados (tirar o '#' para usar)
        var fora = original.Where(x => paisesFifa.Contains(x.Pais)).ToList();
        if (fora.Count > 0)
            File.AppendAllText(arq, "#\n# Referees of the classic game (REFEREE.TXE) of countries the list above replaces.\n" +
                "# Remove the '#' at the start of a line to use that referee too.\n" +
                string.Concat(fora.Select(x => $"#{x.Pais}\t{x.Nome}\t{x.Origem}\n")), new UTF8Encoding(false));
        foreach (var bak in Directory.EnumerateFiles(destino, "*.bak", SearchOption.AllDirectories).ToList()) File.Delete(bak);
        Console.WriteLine($"referees.tsv: {fifa.Count} da FIFA/liga + {todos.Count - fifa.Count} do original ({fora.Count} comentados)");
        return r.Erros.Count > 0 ? 1 : 0;
    }
}
