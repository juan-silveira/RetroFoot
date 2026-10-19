using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.NovoJogo;

namespace RetroFoot.Motor.Arquivos;

// A pasta de dados do RetroFoot (docs/ferramentas.md): o que no jogo classico ficava ao lado do
// EXE, numa pasta que o jogador abre no gestor de arquivos e onde copia arquivos a granel.
//   teams/<PAIS>/<slug>.json   uma equipa por arquivo (EquipaJson)
//   logos/                     logos de clubes e ligas do jogador (+ logos.tsv, leagues.tsv)
//   saves/                     os jogos (.e98)
//   patches/                   a biblioteca de patches (.zip)
//   country-groups/            bosman.txt e plop.txt
//   flags/                     bandeiras (<CODIGO>.BMP ou .png)
//   sponsors/                  patrocinadores (MARCAS.DAT e os logos)
//   countries.tsv, referees.tsv
//   backup/<data>/             o que foi convertido (os .EFT/.TXE originais)
// Arquivos no formato do jogo classico deixados aqui (COUNTRY.TXE, REFEREE.TXE, CTRGROUP/,
// EQUIPAS/, FLAGS/, SPONSORS/, .EFT em teams/) sao convertidos por ConverterSoltos.
public static class PastaDados
{
    public const string Equipas = "teams", Logos = "logos", Saves = "saves", Patches = "patches",
        Grupos = "country-groups", Bandeiras = "flags", Patrocinadores = "sponsors",
        Paises = "countries.tsv", Arbitros = "referees.tsv", Backup = "backup",
        Bosman = "bosman.txt", Plop = "plop.txt";

    public static readonly string[] Pastas = { Equipas, Logos, Saves, Patches, Grupos, Bandeiras, Patrocinadores };

    static readonly Encoding Latin1 = Encoding.Latin1;

    // a pasta esta no formato novo (e nao a pasta do jogo classico)?
    public static bool EhPastaNova(string pasta) =>
        File.Exists(Path.Combine(pasta, Paises)) || Directory.Exists(Path.Combine(pasta, Equipas));

    // ---------------------------------------------------------------- leitura para o jogo
    // os Dados de um jogo novo, como Dados.Ler leria a pasta do jogo classico com os mesmos
    // arquivos. Equipas com JSON quebrado ficam de fora (o motivo vai em avisos).
    public static Dados Ler(string raiz, List<string>? avisos = null)
    {
        var d = new Dados();
        d.Paises = LerPaises(raiz);
        var (bosman, plop) = Grupos2(raiz);
        d.Bosman = bosman.Select(c => Dados.AcharPais(d.Paises, c)).OfType<Pais>().ToList();
        d.PLOP = plop.Select(c => Dados.AcharPais(d.Paises, c)).OfType<Pais>().ToList();
        d.Arbitro = TextosDados.LerArbitros(Path.Combine(raiz, Arbitros)).Select(TextosDados.TextoArbitro).ToList();
        d.ArbitrosProntos = true;
        // a mesma equipa (o mesmo "file") em dois JSON: vale o mais recente
        var porArquivo = new Dictionary<string, (DateTime Data, Arquivo A)>();
        foreach (var j in JsonsDeEquipas(raiz))
        {
            try
            {
                var l = EquipaJson.LerArquivo(j);
                var a = new Arquivo(l.Arquivo, Eft.Decodificar(l.Bytes));
                var data = File.GetLastWriteTimeUtc(j);
                var k = l.Arquivo.ToUpperInvariant();
                if (!porArquivo.TryGetValue(k, out var x) || x.Data <= data) porArquivo[k] = (data, a);
            }
            catch (Exception e) { avisos?.Add($"{Relativo(raiz, j)}: {e.Message}"); }
        }
        // em ordem alfabetica do nome do .EFT em maiusculas (como Dados.Ler)
        d.Equipas = porArquivo.Values.Select(v => v.A).OrderBy(a => a.Nome.ToUpperInvariant(), StringComparer.Ordinal).ToList();
        return d;
    }

    static List<Pais> LerPaises(string raiz)
    {
        var r = new List<Pais>();
        foreach (var p in TextosDados.LerPaises(Path.Combine(raiz, Paises)))
        {
            var cod = p.Codigo.Length > 3 ? p.Codigo[..3] : p.Codigo;
            var nome = EquipaJson.Bytes(p.Nome);
            r.Add(new Pais { Codigo = cod, Nome = nome.Length > 20 ? nome[..20] : nome });
        }
        // como o original: POR entra se faltar
        if (Dados.AcharPais(r, "POR") == null) r.Add(new Pais { Codigo = "POR", Nome = "PORTUGAL"u8.ToArray() });
        return r;
    }

    static (List<string> Bosman, List<string> Plop) Grupos2(string raiz) =>
        (TextosDados.LerGrupo(Path.Combine(raiz, Grupos, Bosman)), TextosDados.LerGrupo(Path.Combine(raiz, Grupos, Plop)));

    // os codigos da Lei Bosman e do PLOP que existem em countries.tsv
    public static (List<string> Bosman, List<string> Plop) ListasDoJogo(string raiz)
    {
        var paises = LerPaises(raiz);
        var (b, p) = Grupos2(raiz);
        return (b.Where(c => Dados.AcharPais(paises, c) != null).ToList(), p.Where(c => Dados.AcharPais(paises, c) != null).ToList());
    }

    public static List<string> CodigosDosPaises(string raiz) => LerPaises(raiz).Select(p => p.Codigo).ToList();

    // os JSON de teams/ (sem backup/ nem pastas que comecam com '.' ou '_')
    public static IEnumerable<string> JsonsDeEquipas(string raiz)
    {
        var t = Path.Combine(raiz, Equipas);
        if (!Directory.Exists(t)) return Array.Empty<string>();
        return Arquivos(t, "*.json");
    }

    static IEnumerable<string> Arquivos(string pasta, string padrao)
    {
        var r = new List<string>();
        void Ir(string p)
        {
            foreach (var f in Directory.EnumerateFiles(p, padrao)) r.Add(f);
            foreach (var s in Directory.EnumerateDirectories(p))
            {
                var n = Path.GetFileName(s);
                if (n.StartsWith('.') || n.StartsWith('_') || string.Equals(n, Backup, StringComparison.OrdinalIgnoreCase)) continue;
                Ir(s);
            }
        }
        Ir(pasta);
        return r.OrderBy(x => x, StringComparer.Ordinal);
    }

    // ---------------------------------------------------------------- relatorio
    public sealed class Relatorio
    {
        public int Equipas;
        public SortedDictionary<string, int> PorPais = new(StringComparer.Ordinal);
        public List<string> Erros = new();
        public List<string> Outros = new();   // paises, arbitros, grupos
        public SortedDictionary<string, int> Copiados = new(StringComparer.Ordinal);   // flags, sponsors
        public bool Mudou => Equipas > 0 || Outros.Count > 0 || Copiados.Count > 0;
        public override string ToString() =>
            $"{Equipas} equipas ({string.Join(", ", PorPais.Select(p => $"{p.Key} {p.Value}"))})" +
            (Outros.Count > 0 ? "; " + string.Join(", ", Outros) : "") +
            string.Concat(Copiados.Select(c => $"; {c.Key}/ {c.Value}")) + (Erros.Count > 0 ? $"; {Erros.Count} erros" : "");
    }

    // ---------------------------------------------------------------- equipas
    // o indice "file" (em maiusculas) -> JSON das equipas que ja estao na pasta
    public static Dictionary<string, string> IndiceEquipas(string raiz)
    {
        var r = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var j in JsonsDeEquipas(raiz))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(j), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                if (doc.RootElement.TryGetProperty("file", out var f) && f.GetString() is { Length: > 0 } s)
                    r[s.Trim().ToUpperInvariant()] = j;
            }
            catch { }
        }
        return r;
    }

    // grava a equipa de um .EFT na pasta: a mesma equipa (o mesmo nome de arquivo .EFT) e
    // substituida, mesmo que mude de pais; uma nova ganha teams/<PAIS>/<slug>.json
    public static string GravarEquipa(string raiz, string nomeEft, byte[] eft, Dictionary<string, string> indice)
    {
        var json = EquipaJson.DeEft(eft, nomeEft);
        var (pais, nome) = EquipaJson.Identidade(eft);
        var pastaPais = Path.Combine(raiz, Equipas, EquipaJson.PastaPais(pais));
        var k = nomeEft.ToUpperInvariant();
        string? destino = null;
        if (indice.TryGetValue(k, out var velho))
        {
            if (string.Equals(Path.GetDirectoryName(velho), pastaPais, StringComparison.Ordinal)) destino = velho;
            else try { File.Delete(velho); } catch { }
        }
        if (destino == null)
        {
            var slug = EquipaJson.Slug(nome.Trim().Length > 0 ? nome : Path.GetFileNameWithoutExtension(nomeEft));
            var usados = indice.Values.ToHashSet(StringComparer.Ordinal);
            destino = Path.Combine(pastaPais, slug + ".json");
            for (int i = 2; File.Exists(destino) || usados.Contains(destino); i++) destino = Path.Combine(pastaPais, $"{slug}-{i}.json");
        }
        Directory.CreateDirectory(pastaPais);
        File.WriteAllText(destino, json, new UTF8Encoding(false));
        indice[k] = destino;
        return destino;
    }

    // ---------------------------------------------------------------- importar (patch)
    // Um patch do jogo classico (zip, pasta ou um arquivo so: .EFT, COUNTRY.TXE...) na pasta de
    // dados: .EFT -> teams/*.json, COUNTRY.TXE -> countries.tsv, REFEREE.TXE -> referees.tsv,
    // CTRGROUP/*.TXE -> country-groups/*.txt, FLAGS/* -> flags/, SPONSORS/* -> sponsors/ e
    // os .json de equipas do RetroFoot. O resto (EXE, leia-me...) e ignorado.
    public static Relatorio Importar(string origem, string raiz)
    {
        var r = new Relatorio();
        var indice = IndiceEquipas(raiz);
        // um patch com a mesma equipa em JSON e em .EFT (PatchEquipas, o JSON vem antes): vale o JSON
        var feitas = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (rel, ler) in Entradas(origem))
            ImportarEntrada(raiz, rel, ler, indice, r, feitas);
        return r;
    }

    // as entradas (caminho relativo com '/', leitura) de um zip, pasta ou arquivo
    static IEnumerable<(string Rel, Func<byte[]> Ler)> Entradas(string origem)
    {
        if (Directory.Exists(origem))
        {
            foreach (var e in EntradasPasta(origem, "")) yield return e;
            yield break;
        }
        if (string.Equals(Path.GetExtension(origem), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            // nomes sem a marca UTF-8 do zip: Latin-1 (os patches antigos sao do Windows)
            using var z = ZipFile.Open(origem, ZipArchiveMode.Read, Latin1);
            foreach (var e in z.Entries)
            {
                if (e.FullName.EndsWith('/')) continue;
                var ent = e;
                yield return (e.FullName.Replace('\\', '/'), () =>
                {
                    using var s = ent.Open();
                    using var m = new MemoryStream();
                    s.CopyTo(m);
                    return m.ToArray();
                });
            }
            yield break;
        }
        yield return (Path.GetFileName(origem), () => File.ReadAllBytes(origem));
    }

    static IEnumerable<(string, Func<byte[]>)> EntradasPasta(string pasta, string prefixo)
    {
        foreach (var e in ArquivosBrutos.Listar(pasta))
        {
            // nome que nao e UTF-8 (Latin-1 do Windows, so no Linux/macOS): lido pelos bytes
            bool bruto = e.Bruto != null && e.Nome.Contains('\uFFFD');
            var cam = Path.Combine(pasta, e.Nome);
            if (!bruto && Directory.Exists(cam))
            {
                foreach (var x in EntradasPasta(cam, prefixo + e.Nome + "/")) yield return x;
                continue;
            }
            var nome = bruto ? Latin1.GetString(e.Bruto!) : e.Nome;
            var ent = e;
            yield return (prefixo + nome, () => ArquivosBrutos.Ler(pasta, ent));
        }
    }

    static void ImportarEntrada(string raiz, string rel, Func<byte[]> ler, Dictionary<string, string> indice, Relatorio r, HashSet<string>? feitas = null)
    {
        var partes = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length == 0) return;
        var nome = partes[^1];
        var pai = partes.Length > 1 ? partes[^2] : "";
        var ext = Path.GetExtension(nome).ToUpperInvariant();
        bool Pai(params string[] n) => n.Any(x => string.Equals(pai, x, StringComparison.OrdinalIgnoreCase));
        try
        {
            if (ext == ".EFT")
            {
                if (feitas != null && feitas.Contains(nome.ToUpperInvariant())) return;
                var b = ler();
                var destino = GravarEquipa(raiz, nome, b, indice);
                r.Equipas++;
                var p = Path.GetFileName(Path.GetDirectoryName(destino))!;
                r.PorPais[p] = r.PorPais.GetValueOrDefault(p) + 1;
            }
            else if (ext == ".JSON" && partes.Any(x => string.Equals(x, Equipas, StringComparison.OrdinalIgnoreCase)))
            {
                var b = ler();
                var l = EquipaJson.ParaEft(Encoding.UTF8.GetString(b), nome);
                feitas?.Add(l.Arquivo.ToUpperInvariant());
                var destino = GravarEquipa(raiz, l.Arquivo, l.Bytes, indice);
                r.Equipas++;
                var p = Path.GetFileName(Path.GetDirectoryName(destino))!;
                r.PorPais[p] = r.PorPais.GetValueOrDefault(p) + 1;
            }
            else if (nome.Equals("COUNTRY.TXE", StringComparison.OrdinalIgnoreCase))
            {
                var l = TextosDados.PaisesDoTxe(ler());
                TextosDados.GravarPaises(Path.Combine(raiz, Paises), l);
                r.Outros.Add($"{Paises} ({l.Count})");
            }
            else if (nome.Equals("REFEREE.TXE", StringComparison.OrdinalIgnoreCase))
            {
                var l = TextosDados.ArbitrosDoTxe(ler());
                TextosDados.GravarArbitros(Path.Combine(raiz, Arbitros), l);
                r.Outros.Add($"{Arbitros} ({l.Count})");
            }
            else if (ext == ".TXE" && Pai("CTRGROUP", Grupos))
            {
                var l = TextosDados.GrupoDoTxe(ler());
                var txt = Path.GetFileNameWithoutExtension(nome).ToLowerInvariant() + ".txt";
                TextosDados.GravarGrupo(Path.Combine(raiz, Grupos, txt), DescricaoGrupo(txt), l);
                r.Outros.Add($"{Grupos}/{txt} ({l.Count})");
            }
            else if (Pai("FLAGS", Bandeiras))
            {
                CopiarComBackup(raiz, Path.Combine(raiz, Bandeiras, nome), ler());
                r.Copiados[Bandeiras] = r.Copiados.GetValueOrDefault(Bandeiras) + 1;
            }
            // logos de um patch do Editor de Equipas (PatchEquipas): as imagens e os indices
            else if (Pai(Logos) && (nome.Equals(LogosDados.Indice, StringComparison.OrdinalIgnoreCase) || nome.Equals(LogosDados.Ligas, StringComparison.OrdinalIgnoreCase)))
            {
                var indiceLogos = nome.Equals(LogosDados.Ligas, StringComparison.OrdinalIgnoreCase) ? LogosDados.Ligas : LogosDados.Indice;
                int n = LogosDados.Mesclar(raiz, indiceLogos, Encoding.UTF8.GetString(ler()));
                r.Outros.Add($"{Logos}/{indiceLogos} ({n})");
            }
            else if (Pai(Logos) && ext is ".PNG" or ".JPG" or ".JPEG" or ".BMP" or ".WEBP")
            {
                CopiarComBackup(raiz, Path.Combine(raiz, Logos, nome), ler());
                r.Copiados[Logos] = r.Copiados.GetValueOrDefault(Logos) + 1;
            }
            else if (Pai("SPONSORS", Patrocinadores))
            {
                CopiarComBackup(raiz, Path.Combine(raiz, Patrocinadores, nome), ler());
                r.Copiados[Patrocinadores] = r.Copiados.GetValueOrDefault(Patrocinadores) + 1;
            }
        }
        catch (Exception e) { r.Erros.Add($"{rel}: {e.Message}"); }
    }

    public static string DescricaoGrupo(string txt) => txt switch
    {
        Bosman => "Bosman law countries (players from these countries are not foreigners in each other's teams)",
        Plop => "Portuguese-speaking countries (PLOP; same rule as the Bosman list)",
        _ => "country group",
    };

    // um arquivo que ja existe e diferente vai antes para backup/<data>/
    static void CopiarComBackup(string raiz, string destino, byte[] b)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        if (File.Exists(destino))
        {
            if (File.ReadAllBytes(destino).AsSpan().SequenceEqual(b)) return;
            var bk = Path.Combine(PastaBackup(raiz), Relativo(raiz, destino));
            Directory.CreateDirectory(Path.GetDirectoryName(bk)!);
            File.Copy(destino, bk, true);
        }
        File.WriteAllBytes(destino, b);
    }

    static string? _carimbo;
    static string PastaBackup(string raiz) => Path.Combine(raiz, Backup, _carimbo ??= DateTime.Now.ToString("yyyyMMdd-HHmmss"));

    static string Relativo(string raiz, string cam) => Path.GetRelativePath(raiz, cam).Replace('\\', '/');

    // ---------------------------------------------------------------- arquivos soltos
    // Converte o que o jogador deixou na pasta no formato do jogo classico (chamado ao abrir o
    // jogo e antes de cada jogo novo). Os originais vao para backup/<data>/ (nada e apagado).
    public static Relatorio ConverterSoltos(string raiz)
    {
        _carimbo = null;
        var r = new Relatorio();
        if (!Directory.Exists(raiz)) return r;
        Dictionary<string, string>? indice = null;
        Dictionary<string, string> Indice() => indice ??= IndiceEquipas(raiz);

        void Guardar(string cam)
        {
            var bk = Path.Combine(PastaBackup(raiz), Relativo(raiz, cam));
            Directory.CreateDirectory(Path.GetDirectoryName(bk)!);
            if (Directory.Exists(cam)) Directory.Move(cam, bk);
            else File.Move(cam, bk, true);
        }

        foreach (var e in Directory.EnumerateFileSystemEntries(raiz).ToList())
        {
            var n = Path.GetFileName(e);
            bool dir = Directory.Exists(e);
            if (!dir && (n.Equals("COUNTRY.TXE", StringComparison.OrdinalIgnoreCase) || n.Equals("REFEREE.TXE", StringComparison.OrdinalIgnoreCase)))
            {
                int erros = r.Erros.Count;
                ImportarEntrada(raiz, n, () => File.ReadAllBytes(e), Indice(), r);
                if (r.Erros.Count == erros) Guardar(e);
            }
            else if (dir && n.Equals("CTRGROUP", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var (rel, ler) in EntradasPasta(e, n + "/")) ImportarEntrada(raiz, rel, ler, Indice(), r);
                Guardar(e);
            }
            // FLAGS/SPONSORS: so com o nome antigo (num sistema sem maiusculas, flags e FLAGS
            // sao a mesma pasta e nao ha o que fazer)
            else if (dir && (n.Equals("FLAGS", StringComparison.OrdinalIgnoreCase) && n != Bandeiras
                          || n.Equals("SPONSORS", StringComparison.OrdinalIgnoreCase) && n != Patrocinadores))
            {
                foreach (var (rel, ler) in EntradasPasta(e, n + "/")) ImportarEntrada(raiz, rel, ler, Indice(), r);
                Guardar(e);
            }
            else if (dir && n.Equals("EQUIPAS", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var (rel, ler) in EntradasPasta(e, n + "/")) ImportarEntrada(raiz, rel, ler, Indice(), r);
                Guardar(e);
            }
        }
        // .TXE deixados em country-groups/
        var g = Path.Combine(raiz, Grupos);
        if (Directory.Exists(g))
            foreach (var f in Directory.EnumerateFiles(g).Where(f => Path.GetExtension(f).Equals(".TXE", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                int erros = r.Erros.Count;
                ImportarEntrada(raiz, Grupos + "/" + Path.GetFileName(f), () => File.ReadAllBytes(f), Indice(), r);
                if (r.Erros.Count == erros) Guardar(f);
            }
        // .EFT deixados em teams/ (em qualquer subpasta, menos backup)
        var t = Path.Combine(raiz, Equipas);
        if (Directory.Exists(t))
            foreach (var f in Arquivos(t, "*").Where(f => Path.GetExtension(f).Equals(".EFT", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                int erros = r.Erros.Count;
                ImportarEntrada(raiz, Path.GetFileName(f), () => File.ReadAllBytes(f), Indice(), r);
                if (r.Erros.Count == erros) try { Guardar(f); } catch { }
            }
        return r;
    }

    // ---------------------------------------------------------------- exportar
    // teams/*.json -> pasta EQUIPAS com os .EFT (para o jogo classico); devolve quantos
    public static int ExportarEquipas(string raiz, string destino, List<string>? erros = null)
    {
        Directory.CreateDirectory(destino);
        int n = 0;
        foreach (var j in JsonsDeEquipas(raiz))
        {
            try
            {
                var l = EquipaJson.LerArquivo(j);
                File.WriteAllBytes(Path.Combine(destino, Path.GetFileName(l.Arquivo)), l.Bytes);
                n++;
            }
            catch (Exception e) { erros?.Add($"{Relativo(raiz, j)}: {e.Message}"); }
        }
        return n;
    }

    // countries.tsv, referees.tsv e os grupos de volta aos .TXE (COUNTRY.TXE, REFEREE.TXE,
    // CTRGROUP/BOSMAN.TXE e PLOP.TXE) numa pasta do jogo classico
    public static void ExportarListas(string raiz, string destino)
    {
        Directory.CreateDirectory(Path.Combine(destino, "CTRGROUP"));
        File.WriteAllBytes(Path.Combine(destino, "COUNTRY.TXE"), TextosDados.Txe("Country",
            TextosDados.LerPaises(Path.Combine(raiz, Paises)).Select(p => p.Codigo + " " + p.Nome)));
        File.WriteAllBytes(Path.Combine(destino, "REFEREE.TXE"), TextosDados.Txe("Referee",
            TextosDados.LerArbitros(Path.Combine(raiz, Arbitros)).Select(a => a.Pais + " " + a.Nome)));
        File.WriteAllBytes(Path.Combine(destino, "CTRGROUP", "BOSMAN.TXE"), TextosDados.Txe("Bosman",
            TextosDados.LerGrupo(Path.Combine(raiz, Grupos, Bosman))));
        File.WriteAllBytes(Path.Combine(destino, "CTRGROUP", "PLOP.TXE"), TextosDados.Txe("PLOP",
            TextosDados.LerGrupo(Path.Combine(raiz, Grupos, Plop))));
    }
}
