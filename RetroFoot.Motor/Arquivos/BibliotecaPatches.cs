using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RetroFoot.Motor.Arquivos;

// A biblioteca de patches do RetroFoot (a ferramenta Patches do hub; docs/ferramentas.md, secao 5).
// Vem do "Aplicar Patch" do launcher do jogo classico (src/PatchApplier.cs e src/Patches.cs, de Juan
// Silveira), adaptado a pasta de dados: em vez de extrair o zip por cima da pasta do jogo, cada
// arquivo do patch vai para o lugar dele no formato novo (PastaDados) e o que ele substitui fica
// guardado, para o patch poder ser revertido.
//
//   patches/<nome>.zip                      a biblioteca (o jogador tambem pode copiar zips para la)
//   patches/.aplicados/<nome>/registo.json  o que o patch mudou (arquivos, antes/depois)
//   patches/.aplicados/<nome>/antes/<rel>   os arquivos como estavam antes do patch
//
// Aceita os dois formatos:
//   jogo classico (comunidade): EQUIPAS/*.EFT, COUNTRY.TXE, REFEREE.TXE, CTRGROUP/*.TXE, FLAGS/*,
//     SPONSORS/* (em qualquer subpasta do zip) e um leia-me .txt;
//   RetroFoot (o que o Editor de Equipas exporta, PatchEquipas): retrofoot-patch.json (ou
//     patch.json: nome, descricao, autor), teams/<PAIS>/*.json, logos/* (+ logos.tsv e leagues.tsv,
//     juntados ao indice do jogador), countries.tsv, referees.tsv, country-groups/*.txt, flags/*,
//     sponsors/* e, opcional, EQUIPAS/*.EFT com as mesmas equipas (o JSON vale mais).
public static class BibliotecaPatches
{
    public const string Aplicados = ".aplicados", ArquivoManifesto = "retrofoot-patch.json", ManifestoCurto = "patch.json", Registo = "registo.json", PastaAntes = "antes";
    public const string FormatoPatch = "retrofoot-patch";

    static readonly Encoding Latin1 = Encoding.Latin1;
    static readonly UTF8Encoding Utf8 = new(false);

    public static string Pasta(string raiz) => Path.Combine(raiz, PastaDados.Patches);
    static string PastaRegisto(string raiz, string nome) => Path.Combine(Pasta(raiz), Aplicados, nome);

    // ---------------------------------------------------------------- o que cada entrada e
    public enum Tipo { Ignorado, Leiame, Manifesto, Equipa, EquipaJson, PaisesTxe, PaisesTsv, ArbitrosTxe, ArbitrosTsv, GrupoTxe, GrupoTxt, Bandeira, Patrocinio, Logo, IndiceLogos, IndiceLigas }

    // o tipo e o destino (relativo a pasta de dados, com '/') de uma entrada do zip
    public static (Tipo Tipo, string Destino) Classificar(string rel)
    {
        var partes = rel.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length == 0) return (Tipo.Ignorado, "");
        var nome = partes[^1];
        var ext = Path.GetExtension(nome).ToUpperInvariant();
        bool Eh(string a, params string[] n) => n.Any(x => string.Equals(a, x, StringComparison.OrdinalIgnoreCase));
        // a pasta conhecida mais proxima do arquivo (os zips as vezes tem uma pasta por fora)
        int Pasta(params string[] n)
        {
            for (int i = partes.Length - 2; i >= 0; i--) if (Eh(partes[i], n)) return i;
            return -1;
        }
        if (nome.StartsWith('.') || partes.Any(p => p.StartsWith("__MACOSX", StringComparison.Ordinal))) return (Tipo.Ignorado, "");
        // nada de sair da pasta de dados (../, C:, caminhos absolutos)
        if (partes.Any(p => p is "." or ".." || p.Contains(':')) || rel.StartsWith('/') || rel.StartsWith('\\')) return (Tipo.Ignorado, "");
        int lg = Pasta(PastaDados.Logos);
        if (lg >= 0)
        {
            var dentro = string.Join('/', partes.Skip(lg + 1));
            if (Eh(dentro, "logos.tsv")) return (Tipo.IndiceLogos, PastaDados.Logos + "/logos.tsv");
            if (Eh(dentro, "leagues.tsv")) return (Tipo.IndiceLigas, PastaDados.Logos + "/leagues.tsv");
            if (ext is ".PNG" or ".JPG" or ".JPEG" or ".BMP" or ".GIF" or ".WEBP" or ".SVG") return (Tipo.Logo, PastaDados.Logos + "/" + dentro);
            return (Tipo.Ignorado, "");
        }
        if (ext == ".EFT") return (Tipo.Equipa, "");
        if (ext == ".JSON" && Pasta(PastaDados.Equipas, "EQUIPAS") >= 0) return (Tipo.EquipaJson, "");
        if (Pasta("FLAGS") >= 0) return (Tipo.Bandeira, PastaDados.Bandeiras + "/" + nome);
        if (Pasta("SPONSORS") >= 0) return (Tipo.Patrocinio, PastaDados.Patrocinadores + "/" + nome);
        if (Pasta("CTRGROUP", PastaDados.Grupos) >= 0)
        {
            var txt = Path.GetFileNameWithoutExtension(nome).ToLowerInvariant() + ".txt";
            if (ext == ".TXE") return (Tipo.GrupoTxe, PastaDados.Grupos + "/" + txt);
            if (ext == ".TXT") return (Tipo.GrupoTxt, PastaDados.Grupos + "/" + txt);
        }
        if (Eh(nome, "COUNTRY.TXE")) return (Tipo.PaisesTxe, PastaDados.Paises);
        if (Eh(nome, "REFEREE.TXE")) return (Tipo.ArbitrosTxe, PastaDados.Arbitros);
        if (Eh(nome, PastaDados.Paises)) return (Tipo.PaisesTsv, PastaDados.Paises);
        if (Eh(nome, PastaDados.Arbitros)) return (Tipo.ArbitrosTsv, PastaDados.Arbitros);
        // (o manifesto de versoes anteriores do jogo tinha outro prefixo: qualquer *-patch.json)
        if (Eh(nome, ArquivoManifesto, ManifestoCurto) || nome.EndsWith("-patch.json", StringComparison.OrdinalIgnoreCase)) return (Tipo.Manifesto, "");
        if (ext is ".TXT" or ".NFO" or ".MD" || Eh(Path.GetFileNameWithoutExtension(nome), "LEIAME", "LEIA-ME", "README", "LEEME"))
            return (Tipo.Leiame, "");
        return (Tipo.Ignorado, "");
    }

    sealed record Entrada(string Rel, Tipo Tipo, string Destino, Func<byte[]> Ler);

    // as entradas de um zip (nomes sem a marca UTF-8: Latin-1, como os patches antigos do Windows)
    static List<Entrada> Entradas(ZipArchive z) => z.Entries
        .Where(e => !e.FullName.EndsWith('/') && !e.FullName.EndsWith('\\'))
        .Select(e =>
        {
            var rel = e.FullName.Replace('\\', '/');
            var (t, d) = Classificar(rel);
            return new Entrada(rel, t, d, () =>
            {
                using var s = e.Open();
                using var m = new MemoryStream();
                s.CopyTo(m);
                return m.ToArray();
            });
        }).ToList();

    static ZipArchive Abrir(string zip) => ZipFile.Open(zip, ZipArchiveMode.Read, Latin1);

    // ---------------------------------------------------------------- informacao
    public sealed class Info
    {
        public string Nome = "";             // o nome na biblioteca (o .zip sem extensao)
        public string Titulo = "";           // do patch.json, ou o nome
        public string Descricao = "";        // do patch.json, ou o comeco do leia-me
        public string Autor = "", Versao = "";
        public bool RetroFoot;               // tem patch.json (feito pelo RetroFoot)
        public long Tamanho;
        public DateTime Data;
        public int Equipas, Bandeiras, Patrocinios, Logos, Grupos, Ignorados;
        public bool Paises, Arbitros, PastaEquipas;   // PastaEquipas: traz EQUIPAS/ (o jogo classico a trocava inteira)
        public SortedDictionary<string, int> EquipasPorPais = new(StringComparer.Ordinal);
        public List<string> Chaves = new();  // "team:<FILE.EFT>" e os destinos dos outros arquivos
        public bool Aplicado;
        public DateTime? AplicadoEm;
        public string? Erro;
        public bool Vazio => Equipas + Bandeiras + Patrocinios + Logos + Grupos == 0 && !Paises && !Arbitros;
    }

    // le o zip (sem aplicar): o que ele traz, a descricao e as chaves (para os conflitos)
    public static Info Ler(string zip, bool equipasPorPais = true)
    {
        var i = new Info { Nome = Path.GetFileNameWithoutExtension(zip) };
        i.Titulo = i.Nome;
        try
        {
            var fi = new FileInfo(zip);
            i.Tamanho = fi.Length;
            i.Data = fi.LastWriteTime;
            using var z = Abrir(zip);
            string? leiame = null;
            var chaves = new SortedSet<string>(StringComparer.Ordinal);
            bool eftEmEquipas = false;
            // os JSON antes dos .EFT: a mesma equipa nos dois conta uma vez (vale o JSON)
            foreach (var e in Entradas(z).OrderBy(e => e.Tipo == Tipo.Equipa ? 1 : 0))
            {
                switch (e.Tipo)
                {
                    case Tipo.Equipa:
                    case Tipo.EquipaJson:
                        string arquivo = Path.GetFileName(e.Rel);
                        if (e.Tipo == Tipo.Equipa && chaves.Contains(ChaveEquipa(arquivo))) continue;
                        string pais = "";
                        if (equipasPorPais || e.Tipo == Tipo.EquipaJson)
                            try
                            {
                                var b = e.Ler();
                                if (e.Tipo == Tipo.EquipaJson) { var l = EquipaJson.ParaEft(Utf8.GetString(b), e.Rel); arquivo = l.Arquivo; b = l.Bytes; }
                                pais = EquipaJson.PastaPais(EquipaJson.Identidade(b).Pais);
                            }
                            catch { pais = "?"; }
                        i.Equipas++;
                        if (pais.Length > 0) i.EquipasPorPais[pais] = i.EquipasPorPais.GetValueOrDefault(pais) + 1;
                        if (e.Tipo == Tipo.Equipa && e.Rel.Split('/').Any(p => p.Equals("EQUIPAS", StringComparison.OrdinalIgnoreCase))) eftEmEquipas = true;
                        chaves.Add(ChaveEquipa(arquivo));
                        continue;
                    case Tipo.PaisesTxe: case Tipo.PaisesTsv: i.Paises = true; break;
                    case Tipo.ArbitrosTxe: case Tipo.ArbitrosTsv: i.Arbitros = true; break;
                    case Tipo.GrupoTxe: case Tipo.GrupoTxt: i.Grupos++; break;
                    case Tipo.Bandeira: i.Bandeiras++; break;
                    case Tipo.Patrocinio: i.Patrocinios++; break;
                    case Tipo.Logo: i.Logos++; break;
                    case Tipo.IndiceLogos: case Tipo.IndiceLigas: break;
                    case Tipo.Manifesto:
                        try { LerManifesto(e.Ler(), i); } catch { }
                        continue;
                    case Tipo.Leiame:
                        if (leiame == null) try { leiame = Texto(e.Ler()); } catch { }
                        continue;
                    default: i.Ignorados++; continue;
                }
                chaves.Add(e.Destino.ToLowerInvariant());
            }
            i.Chaves = chaves.ToList();
            // so um patch classico (sem manifesto) com a pasta EQUIPAS sugere trocar todas
            i.PastaEquipas = eftEmEquipas && !i.RetroFoot;
            if (i.Descricao.Length == 0 && leiame != null) i.Descricao = Resumo(leiame);
        }
        catch (Exception x) { i.Erro = x.Message; }
        return i;
    }

    public static string ChaveEquipa(string arquivoEft) => "team:" + Path.GetFileName(arquivoEft).Trim().ToUpperInvariant();

    static void LerManifesto(byte[] b, Info i)
    {
        using var d = JsonDocument.Parse(b, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var r = d.RootElement;
        string S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
        i.RetroFoot = true;
        if (S("name") is { Length: > 0 } n) i.Titulo = n;
        i.Descricao = S("description");
        i.Autor = S("author");
        i.Versao = S("patchVersion");
    }

    // texto de um leia-me: UTF-8 se for valido, senao Latin-1 (Windows antigo)
    static string Texto(byte[] b)
    {
        try { return new UTF8Encoding(false, true).GetString(b).TrimStart('﻿'); }
        catch { return Latin1.GetString(b); }
    }

    // as primeiras linhas com texto (sem os tracos de separacao), ate uns 400 caracteres
    static string Resumo(string t)
    {
        var sb = new StringBuilder();
        foreach (var l0 in t.Replace("\r", "").Split('\n'))
        {
            var l = l0.Trim();
            if (l.Length == 0 || l.All(c => c is '-' or '=' or '*' or '_' or '#')) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(l);
            if (sb.Length > 400) break;
        }
        var s = sb.ToString();
        return s.Length > 420 ? s[..417] + "..." : s;
    }

    // ---------------------------------------------------------------- a biblioteca
    // os patches da biblioteca (os .zip de patches/), em ordem alfabetica, com o estado
    public static List<Info> Listar(string raiz)
    {
        var p = Pasta(raiz);
        if (!Directory.Exists(p)) return new();
        var aplicados = RegistosAplicados(raiz).ToDictionary(r => r.Nome, StringComparer.Ordinal);
        var r = new List<Info>();
        foreach (var z in Directory.GetFiles(p, "*", SearchOption.TopDirectoryOnly)
                     .Where(f => Path.GetExtension(f).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
        {
            var i = Ler(z, equipasPorPais: false);
            if (aplicados.TryGetValue(i.Nome, out var a)) { i.Aplicado = true; i.AplicadoEm = a.Data; }
            r.Add(i);
        }
        return r;
    }

    public static string CaminhoZip(string raiz, string nome)
    {
        var p = Pasta(raiz);
        if (Directory.Exists(p))
            foreach (var f in Directory.GetFiles(p))
                if (Path.GetFileNameWithoutExtension(f) == nome && Path.GetExtension(f).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    return f;
        return Path.Combine(p, nome + ".zip");
    }

    // guarda uma copia do zip na biblioteca e devolve o nome dele la (o mesmo zip ja guardado:
    // o mesmo nome; outro zip com o mesmo nome: nome-2, nome-3...)
    public static string Adicionar(string raiz, string zip)
    {
        var info = Ler(zip, equipasPorPais: false);
        if (info.Erro != null) throw new InvalidDataException(info.Erro);
        if (info.Vazio) throw new InvalidDataException("vazio");
        var p = Pasta(raiz);
        Directory.CreateDirectory(p);
        var nome = Path.GetFileNameWithoutExtension(zip);
        foreach (var c in Path.GetInvalidFileNameChars().Append('#')) nome = nome.Replace(c, '_');
        nome = nome.Trim().TrimStart('.');
        if (nome.Length == 0) nome = "patch";
        var novo = File.ReadAllBytes(zip);
        var final = nome;
        for (int n = 2; ; n++)
        {
            var dest = Path.Combine(p, final + ".zip");
            if (!File.Exists(dest)) { File.WriteAllBytes(dest, novo); return final; }
            if (File.ReadAllBytes(dest).AsSpan().SequenceEqual(novo)) return final;
            final = $"{nome}-{n}";
        }
    }

    // tira o zip da biblioteca (um patch aplicado continua aplicado e pode ser revertido)
    public static void Remover(string raiz, string nome)
    {
        var z = CaminhoZip(raiz, nome);
        if (File.Exists(z)) File.Delete(z);
    }

    // ---------------------------------------------------------------- registo (o que foi aplicado)
    public sealed record ArquivoMudado(string Caminho, bool Existia, string? Hash);
    public sealed record RegistoPatch(string Nome, string Titulo, DateTime Data, int Ordem, bool SubstituiuEquipas,
        List<string> Chaves, List<ArquivoMudado> Arquivos);

    public static List<RegistoPatch> RegistosAplicados(string raiz)
    {
        var p = Path.Combine(Pasta(raiz), Aplicados);
        var r = new List<RegistoPatch>();
        if (!Directory.Exists(p)) return r;
        foreach (var d in Directory.GetDirectories(p))
            if (LerRegisto(d) is { } x) r.Add(x);
        return r.OrderBy(x => x.Ordem).ToList();
    }

    static RegistoPatch? LerRegisto(string pasta)
    {
        try
        {
            var o = JsonNode.Parse(File.ReadAllText(Path.Combine(pasta, Registo), Encoding.UTF8))!.AsObject();
            var arqs = o["files"]!.AsArray().Select(f => new ArquivoMudado(
                (string)f!["path"]!, (bool)f["before"]!, (string?)f["hash"])).ToList();
            var chaves = o["keys"]?.AsArray().Select(k => (string)k!).ToList() ?? new();
            return new RegistoPatch(Path.GetFileName(pasta), (string?)o["title"] ?? Path.GetFileName(pasta),
                DateTime.Parse((string)o["appliedAt"]!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                (int?)o["order"] ?? 0, (bool?)o["replaceAllTeams"] ?? false, chaves, arqs);
        }
        catch { return null; }
    }

    static void GravarRegisto(string pasta, RegistoPatch r)
    {
        var o = new JsonObject
        {
            ["format"] = "retrofoot-patch-applied",
            ["version"] = 1,
            ["patch"] = r.Nome,
            ["title"] = r.Titulo,
            ["appliedAt"] = r.Data.ToString("o", CultureInfo.InvariantCulture),
            ["order"] = r.Ordem,
            ["replaceAllTeams"] = r.SubstituiuEquipas,
            ["keys"] = new JsonArray(r.Chaves.Select(k => (JsonNode)JsonValue.Create(k)!).ToArray()),
            ["files"] = new JsonArray(r.Arquivos.Select(a => (JsonNode)new JsonObject
            {
                ["path"] = a.Caminho, ["before"] = a.Existia, ["hash"] = a.Hash,
            }).ToArray()),
        };
        File.WriteAllText(Path.Combine(pasta, Registo), o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Utf8);
    }

    // FNV-1a de 64 bits e o tamanho: so para saber se o arquivo mudou desde que o patch o pos
    // (sem System.Security.Cryptography, que o corte do publish tirava do executavel)
    static string Hash(string arq)
    {
        var b = File.ReadAllBytes(arq);
        ulong h = 14695981039346656037UL;
        foreach (var x in b) { h ^= x; h *= 1099511628211UL; }
        return h.ToString("x16") + "-" + b.Length.ToString("x");
    }

    // ---------------------------------------------------------------- analise (antes de aplicar)
    public sealed class Analise
    {
        public Info Info = new();
        public int EquipasNovas, EquipasSubstituidas, EquipasRetiradas;   // retiradas: com "substituir todas"
        public List<string> PaisesDesconhecidos = new();                  // paises das equipas fora de countries.tsv
        public List<(string Patch, string Titulo, int Comuns)> Conflitos = new();   // patches aplicados que mexem no mesmo
    }

    public static Analise Analisar(string raiz, string nome, bool substituirEquipas)
    {
        var a = new Analise { Info = Ler(CaminhoZip(raiz, nome)) };
        if (a.Info.Erro != null) return a;
        var indice = PastaDados.IndiceEquipas(raiz);
        var doPatch = new HashSet<string>(a.Info.Chaves.Where(k => k.StartsWith("team:")).Select(k => k[5..]), StringComparer.Ordinal);
        a.EquipasSubstituidas = doPatch.Count(indice.ContainsKey);
        a.EquipasNovas = doPatch.Count - a.EquipasSubstituidas;
        if (substituirEquipas && doPatch.Count > 0) a.EquipasRetiradas = indice.Keys.Count(k => !doPatch.Contains(k));
        // paises: os de countries.tsv (ou os do patch, se ele traz a lista)
        var paises = a.Info.Paises ? PaisesDoPatch(CaminhoZip(raiz, nome)) : PastaDados.CodigosDosPaises(raiz).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (paises.Count > 0)
            a.PaisesDesconhecidos = a.Info.EquipasPorPais.Keys.Where(p => p != "?" && !paises.Contains(p)).ToList();
        var minhas = new HashSet<string>(a.Info.Chaves, StringComparer.Ordinal);
        foreach (var r in RegistosAplicados(raiz))
        {
            if (r.Nome == nome) continue;
            int comuns = r.Chaves.Count(minhas.Contains);
            // "substituir todas": mexe em todas as equipas dos outros patches
            if (substituirEquipas && doPatch.Count > 0) comuns = Math.Max(comuns, r.Chaves.Count(k => k.StartsWith("team:")));
            if (comuns > 0) a.Conflitos.Add((r.Nome, r.Titulo, comuns));
        }
        return a;
    }

    static HashSet<string> PaisesDoPatch(string zip)
    {
        var r = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var z = Abrir(zip);
            foreach (var e in Entradas(z))
            {
                if (e.Tipo == Tipo.PaisesTxe) foreach (var p in TextosDados.PaisesDoTxe(e.Ler())) r.Add(Cod(p.Codigo));
                else if (e.Tipo == Tipo.PaisesTsv)
                {
                    var tmp = Path.GetTempFileName();
                    try { File.WriteAllBytes(tmp, e.Ler()); foreach (var p in TextosDados.LerPaises(tmp)) r.Add(Cod(p.Codigo)); }
                    finally { File.Delete(tmp); }
                }
            }
        }
        catch { }
        if (r.Count > 0) r.Add("POR");
        return r;
        static string Cod(string c) => c.Length > 3 ? c[..3] : c;
    }

    // ---------------------------------------------------------------- aplicar
    public sealed class Resultado
    {
        public bool Ok;
        public string Erro = "";
        public int Equipas, Arquivos, Retirados, Restaurados, Apagados;
        public int Substituidas;                    // equipas da pasta trocadas pelo mesmo clube do patch
        public string? CopiaDasMudancas;           // reverter: onde ficaram os arquivos mudados depois do patch
        public List<string> Avisos = new();
        public List<string> Bloqueadores = new();  // reverter: patches aplicados depois que mexem no mesmo
    }

    // o diario do que um patch muda: cada arquivo tocado guarda antes a copia de como estava
    sealed class Diario(string raiz, string pasta)
    {
        public readonly Dictionary<string, bool> Existia = new(StringComparer.Ordinal);
        public readonly List<string> Ordem = new();

        public string Cheio(string rel) => Path.Combine(raiz, rel.Replace('/', Path.DirectorySeparatorChar));

        // chamado ANTES de mudar ou apagar o arquivo
        public void Antes(string rel)
        {
            rel = rel.Replace('\\', '/');
            if (Existia.ContainsKey(rel)) return;
            var cam = Cheio(rel);
            bool existe = File.Exists(cam);
            if (existe)
            {
                var bk = Path.Combine(pasta, PastaAntes, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(bk)!);
                File.Copy(cam, bk, true);
            }
            Existia[rel] = existe;
            Ordem.Add(rel);
        }

        public void Escrever(string rel, byte[] b)
        {
            Antes(rel);
            var cam = Cheio(rel);
            Directory.CreateDirectory(Path.GetDirectoryName(cam)!);
            File.WriteAllBytes(cam, b);
        }

        public void Apagar(string rel)
        {
            Antes(rel);
            var cam = Cheio(rel);
            if (File.Exists(cam)) File.Delete(cam);
        }

        public List<ArquivoMudado> Fechar() => Ordem.Select(r =>
        {
            var c = Cheio(r);
            return new ArquivoMudado(r, Existia[r], File.Exists(c) ? Hash(c) : null);
        }).Where(a => a.Existia || a.Hash != null).ToList();
    }

    static string Rel(string raiz, string cam) => Path.GetRelativePath(raiz, cam).Replace('\\', '/');

    // o clube de uma equipa: "PAIS\tNOME CURTO" normalizado (null sem nome curto)
    static string? Clube(string pais, string curto)
    {
        var n = NormalizarNome(curto);
        return n.Length == 0 ? null : pais.Trim().ToUpperInvariant() + "\t" + n;
    }

    public static string? ClubeDoEft(byte[] eft)
    {
        try { var e = RetroFoot.Motor.Base.Eft.Decodificar(eft); return Clube(e.Pais, Encoding.Latin1.GetString(e.NomeAbreviado)); }
        catch { return null; }
    }

    // os clubes das equipas da pasta (o pais e o nome curto normalizados)
    public static HashSet<string> ClubesDaPasta(Dictionary<string, string> indice) =>
        ClubesExistentes(indice, new HashSet<string>(), repetidosTambem: true).Keys.ToHashSet(StringComparer.Ordinal);

    // as equipas da pasta pelo clube (as que o patch traz com o mesmo arquivo ficam de fora:
    // essas ja sao trocadas pelo arquivo); um clube repetido na pasta nao entra (ambiguo)
    static Dictionary<string, (string Chave, string Json)> ClubesExistentes(Dictionary<string, string> indice, HashSet<string> doPatch, bool repetidosTambem = false)
    {
        var r = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        var repetidos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (k, j) in indice)
        {
            if (doPatch.Contains(k)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(j), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                var o = doc.RootElement;
                string S(string n) => o.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
                if (Clube(S("country"), S("shortName")) is not { } c) continue;
                if (!r.TryAdd(c, (k, j))) repetidos.Add(c);
            }
            catch { }
        }
        if (!repetidosTambem) foreach (var c in repetidos) r.Remove(c);
        return r;
    }

    // aplica o patch da biblioteca na pasta de dados. substituirEquipas: as equipas que o patch
    // nao traz saem (como trocar a pasta EQUIPAS inteira no jogo classico); voltam ao reverter.
    public static Resultado Aplicar(string raiz, string nome, bool substituirEquipas)
    {
        var res = new Resultado();
        var zip = CaminhoZip(raiz, nome);
        if (!File.Exists(zip)) { res.Erro = "zip"; return res; }
        var pasta = PastaRegisto(raiz, nome);
        if (File.Exists(Path.Combine(pasta, Registo))) { res.Erro = "aplicado"; return res; }
        if (Directory.Exists(pasta)) Directory.Delete(pasta, true);
        Directory.CreateDirectory(pasta);
        var info = Ler(zip, equipasPorPais: false);
        var d = new Diario(raiz, pasta);
        try
        {
            using var z = Abrir(zip);
            var entradas = Entradas(z);
            // as equipas do patch, ja como .EFT (nome do arquivo, bytes)
            var equipas = new List<(string Arquivo, byte[] Eft)>();
            var vistas = new HashSet<string>(StringComparer.Ordinal);
            // os JSON antes: a mesma equipa tambem em .EFT (PatchEquipas com EQUIPAS/) fica com o JSON
            foreach (var e in entradas.Where(e => e.Tipo is Tipo.Equipa or Tipo.EquipaJson).OrderBy(e => e.Tipo == Tipo.Equipa ? 1 : 0))
            {
                try
                {
                    if (e.Tipo == Tipo.Equipa && vistas.Contains(Path.GetFileName(e.Rel).Trim().ToUpperInvariant())) continue;
                    var b = e.Ler();
                    if (e.Tipo == Tipo.EquipaJson) { var l = EquipaJson.ParaEft(Utf8.GetString(b), e.Rel); equipas.Add((l.Arquivo, l.Bytes)); }
                    else { EquipaJson.Identidade(b); equipas.Add((Path.GetFileName(e.Rel), b)); }
                    vistas.Add(equipas[^1].Arquivo.Trim().ToUpperInvariant());
                }
                catch (Exception x) { res.Avisos.Add($"{e.Rel}: {x.Message}"); }
            }
            var indice = PastaDados.IndiceEquipas(raiz);
            var retiradas = new List<string>();
            if (substituirEquipas && equipas.Count > 0)
            {
                var doPatch = equipas.Select(q => q.Arquivo.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
                foreach (var (k, j) in indice.ToList())
                {
                    if (doPatch.Contains(k)) continue;
                    d.Apagar(Rel(raiz, j));
                    indice.Remove(k);
                    retiradas.Add(ChaveEquipa(k));
                    res.Retirados++;
                }
            }
            // o mesmo clube com outro arquivo (o patch novo de um time do jogo, ex. 0776ip0h.eft e
            // CRUZEIRO.EFT): o do patch substitui o que ja existe (o mesmo pais e o mesmo nome curto);
            // antes os dois ficavam e o jogo tinha o time duplicado. O antigo volta ao reverter.
            if (!(substituirEquipas && equipas.Count > 0))
            {
                var doPatch = equipas.Select(q => q.Arquivo.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
                var porClube = ClubesExistentes(indice, doPatch);
                foreach (var (arq, eft) in equipas)
                {
                    if (indice.ContainsKey(arq.ToUpperInvariant())) continue;
                    if (ClubeDoEft(eft) is not { } c || !porClube.TryGetValue(c, out var velho)) continue;
                    d.Apagar(Rel(raiz, velho.Json));
                    indice.Remove(velho.Chave);
                    porClube.Remove(c);
                    retiradas.Add(ChaveEquipa(velho.Chave));
                    res.Substituidas++;
                }
            }
            foreach (var (arq, eft) in equipas)
            {
                var k = arq.ToUpperInvariant();
                string? velho = indice.TryGetValue(k, out var v) ? v : null;
                if (velho != null) d.Antes(Rel(raiz, velho));
                // o destino de uma equipa nova e um arquivo que ainda nao existe (GravarEquipa)
                var destino = PastaDados.GravarEquipa(raiz, arq, eft, indice);
                if (velho == null || !string.Equals(velho, destino, StringComparison.Ordinal))
                    if (!d.Existia.ContainsKey(Rel(raiz, destino))) { d.Existia[Rel(raiz, destino)] = false; d.Ordem.Add(Rel(raiz, destino)); }
                res.Equipas++;
            }
            foreach (var e in entradas)
            {
                try
                {
                    switch (e.Tipo)
                    {
                        case Tipo.PaisesTxe:
                            d.Escrever(e.Destino, Gerar(pasta, a => TextosDados.GravarPaises(a, TextosDados.PaisesDoTxe(e.Ler()))));
                            break;
                        case Tipo.ArbitrosTxe:
                            d.Escrever(e.Destino, JuntarArbitros(pasta, d.Cheio(e.Destino), TextosDados.ArbitrosDoTxe(e.Ler())));
                            break;
                        case Tipo.ArbitrosTsv:
                            d.Escrever(e.Destino, JuntarArbitros(pasta, d.Cheio(e.Destino), LerTemp(pasta, e.Ler(), TextosDados.LerArbitros)));
                            break;
                        case Tipo.GrupoTxe:
                            d.Escrever(e.Destino, Gerar(pasta, a => TextosDados.GravarGrupo(a, PastaDados.DescricaoGrupo(Path.GetFileName(e.Destino)), TextosDados.GrupoDoTxe(e.Ler()))));
                            break;
                        case Tipo.PaisesTsv: case Tipo.GrupoTxt:
                        case Tipo.Bandeira: case Tipo.Patrocinio: case Tipo.Logo:
                            d.Escrever(e.Destino, e.Ler());
                            break;
                        case Tipo.IndiceLogos: case Tipo.IndiceLigas:
                            d.Escrever(e.Destino, JuntarIndice(d.Cheio(e.Destino), e.Ler(), e.Tipo == Tipo.IndiceLogos));
                            break;
                        default: continue;
                    }
                    res.Arquivos++;
                }
                catch (Exception x) { res.Avisos.Add($"{e.Rel}: {x.Message}"); }
            }
            var ordem = RegistosAplicados(raiz).Select(r => r.Ordem).DefaultIfEmpty(0).Max() + 1;
            var chaves = new SortedSet<string>(info.Chaves, StringComparer.Ordinal);
            // as equipas que sairam com "substituir todas" tambem contam para os conflitos
            chaves.UnionWith(retiradas);
            GravarRegisto(pasta, new RegistoPatch(nome, info.Titulo, DateTime.Now, ordem, substituirEquipas && equipas.Count > 0,
                chaves.ToList(), d.Fechar()));
            res.Ok = true;
        }
        catch (Exception x)
        {
            // desfaz o que ja foi feito
            res.Erro = x.Message;
            try { Desfazer(raiz, pasta, d.Ordem.Select(r => new ArquivoMudado(r, d.Existia[r], null)).ToList()); } catch { }
            try { Directory.Delete(pasta, true); } catch { }
        }
        return res;
    }

    // o texto que TextosDados grava, gerado num temporario (sem o .bak dele: o diario guarda o antigo)
    // os arbitros de um patch entram JUNTO dos que ja estao (FIFA, CBF, os do jogador): so os
    // nomes novos daquele pais; trocar a lista inteira perdia os quadros das ligas
    static byte[] JuntarArbitros(string pasta, string atual, List<TextosDados.Arbitro> doPatch)
    {
        var lista = File.Exists(atual) ? TextosDados.LerArbitrosEditaveis(atual) : new List<TextosDados.ArbitroEditavel>();
        static string Chave(string p, string n) => p.Trim().ToUpperInvariant() + "|" + string.Concat(n.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => char.IsLetter(c))).ToUpperInvariant();
        var tem = new HashSet<string>(lista.Select(a => Chave(a.Pais, a.Nome)));
        foreach (var a in doPatch)
            if (tem.Add(Chave(a.Pais, a.Nome))) lista.Add(new TextosDados.ArbitroEditavel(a.Pais, a.Nome, a.Origem, true));
        return Gerar(pasta, arq => TextosDados.GravarArbitrosEditaveis(arq, lista));
    }

    // le o conteudo por um arquivo temporario (os leitores pedem um caminho)
    static T LerTemp<T>(string pasta, byte[] b, Func<string, T> ler)
    {
        var tmp = Path.Combine(pasta, "lido.tmp");
        try { File.WriteAllBytes(tmp, b); return ler(tmp); }
        finally { try { File.Delete(tmp); } catch { } }
    }

    static byte[] Gerar(string pasta, Action<string> gravar)
    {
        var tmp = Path.Combine(pasta, "gerado.tmp");
        try { gravar(tmp); return File.ReadAllBytes(tmp); }
        finally { try { File.Delete(tmp); File.Delete(tmp + ".bak"); } catch { } }
    }

    // logos.tsv / leagues.tsv: as linhas do patch entram no indice do jogador (a mesma equipa ou
    // liga do patch substitui a linha dela; as outras ficam)
    static byte[] JuntarIndice(string meu, byte[] doPatch, bool logos)
    {
        string Chave(string[] c) => c[0].Trim().ToUpperInvariant() + "\t" + (logos ? NormalizarNome(c[1]) : c[1].Trim().ToUpperInvariant());
        bool Dado(string l, out string[] c)
        {
            c = l.Split('\t');
            return l.Trim().Length > 0 && !l.TrimStart().StartsWith('#') && c.Length >= 3 && !c[0].Trim().Equals("country", StringComparison.OrdinalIgnoreCase);
        }
        var novas = new List<string>();
        var porChave = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var l in Texto(doPatch).Replace("\r", "").Split('\n'))
            if (Dado(l, out var c))
            {
                if (!porChave.ContainsKey(Chave(c))) novas.Add(Chave(c));
                porChave[Chave(c)] = l;
            }
        var saida = new List<string>();
        if (File.Exists(meu))
            foreach (var l in File.ReadAllText(meu, Encoding.UTF8).Replace("\r", "").Split('\n'))
            {
                if (Dado(l, out var c) && porChave.TryGetValue(Chave(c), out var nova))
                {
                    saida.Add(nova);
                    novas.Remove(Chave(c));
                    porChave.Remove(Chave(c));
                }
                else saida.Add(l);
            }
        else
            saida.Add(logos ? "# RetroFoot: club logos. UTF-8, country<TAB>team<TAB>file ('-' = no logo).\ncountry\tteam\tfile"
                            : "# RetroFoot: league logos. UTF-8, country<TAB>level<TAB>file (level 1-4, T = cup).\ncountry\tlevel\tfile");
        while (saida.Count > 0 && saida[^1].Length == 0) saida.RemoveAt(saida.Count - 1);
        saida.AddRange(novas.Select(k => porChave[k]));
        return Utf8.GetBytes(string.Join('\n', saida) + "\n");
    }

    // o mesmo de Escudos.Normalizar (App): maiusculas, sem acentos, so A-Z 0-9 e espacos simples
    public static string NormalizarNome(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            char u = char.ToUpperInvariant(c);
            sb.Append(u is >= 'A' and <= 'Z' or >= '0' and <= '9' ? u : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // ---------------------------------------------------------------- reverter
    // os patches aplicados DEPOIS deste que mexem nos mesmos arquivos (reverter esses antes)
    public static List<RegistoPatch> Bloqueadores(string raiz, string nome)
    {
        var todos = RegistosAplicados(raiz);
        var este = todos.FirstOrDefault(r => r.Nome == nome);
        if (este == null) return new();
        var meus = new HashSet<string>(este.Arquivos.Select(a => a.Caminho), StringComparer.Ordinal);
        return todos.Where(r => r.Ordem > este.Ordem && r.Arquivos.Any(a => meus.Contains(a.Caminho))).ToList();
    }

    // volta a pasta de dados a como estava antes do patch. Um arquivo mudado depois do patch
    // (pelo editor ou a mao) tem a versao atual guardada em backup/<data>/ antes de voltar.
    public static Resultado Reverter(string raiz, string nome)
    {
        var res = new Resultado();
        var pasta = PastaRegisto(raiz, nome);
        var reg = LerRegisto(pasta);
        if (reg == null) { res.Erro = "nao-aplicado"; return res; }
        var bloq = Bloqueadores(raiz, nome);
        if (bloq.Count > 0) { res.Bloqueadores = bloq.Select(b => b.Titulo).ToList(); res.Erro = "bloqueado"; return res; }
        string? copia = null;
        foreach (var a in reg.Arquivos)
        {
            var cam = Path.Combine(raiz, a.Caminho.Replace('/', Path.DirectorySeparatorChar));
            var hoje = File.Exists(cam) ? Hash(cam) : null;
            if (hoje == a.Hash || hoje == null) continue;
            copia ??= Path.Combine(raiz, PastaDados.Backup, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + nome);
            var bk = Path.Combine(copia, a.Caminho.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(bk)!);
            File.Copy(cam, bk, true);
        }
        res.CopiaDasMudancas = copia;
        (res.Restaurados, res.Apagados) = Desfazer(raiz, pasta, reg.Arquivos);
        Directory.Delete(pasta, true);
        res.Ok = true;
        return res;
    }

    static (int Restaurados, int Apagados) Desfazer(string raiz, string pasta, List<ArquivoMudado> arqs)
    {
        int rest = 0, apag = 0;
        foreach (var a in Enumerable.Reverse(arqs))
        {
            var cam = Path.Combine(raiz, a.Caminho.Replace('/', Path.DirectorySeparatorChar));
            if (a.Existia)
            {
                var bk = Path.Combine(pasta, PastaAntes, a.Caminho.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(bk)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(cam)!);
                File.Copy(bk, cam, true);
                rest++;
            }
            else if (File.Exists(cam))
            {
                File.Delete(cam);
                apag++;
                // a pasta do pais que o patch criou e ficou vazia
                var dir = Path.GetDirectoryName(cam)!;
                try { if (dir.StartsWith(Path.Combine(raiz, PastaDados.Equipas)) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
            }
        }
        return (rest, apag);
    }

    // ---------------------------------------------------------------- criar (Editor de Equipas)
    public sealed record Manifesto(string Nome, string Descricao = "", string Autor = "", string Versao = "");

    // um patch do RetroFoot com arquivos da pasta de dados (caminhos relativos a ela, com '/'):
    // teams/<PAIS>/*.json, logos/<arquivo> (as linhas de logos.tsv e leagues.tsv que apontam para
    // os logos levados vao junto), countries.tsv, referees.tsv, country-groups/*, flags/*, sponsors/*.
    // Equipas sem logo levado: as linhas de logos.tsv delas tambem vao (o logo pode ser do jogo).
    public static void Criar(string raiz, string zipDestino, Manifesto m, IEnumerable<string> arquivos)
    {
        var lista = arquivos.Select(a => a.Replace('\\', '/').TrimStart('/')).Distinct(StringComparer.Ordinal).ToList();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipDestino))!);
        if (File.Exists(zipDestino)) File.Delete(zipDestino);
        using var z = ZipFile.Open(zipDestino, ZipArchiveMode.Create, Utf8);
        var man = new JsonObject
        {
            ["format"] = FormatoPatch, ["version"] = 1, ["name"] = m.Nome, ["description"] = m.Descricao,
            ["author"] = m.Autor, ["created"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
        };
        if (m.Versao.Length > 0) man["patchVersion"] = m.Versao;
        Escrever(z, ArquivoManifesto, Utf8.GetBytes(man.ToJsonString(new JsonSerializerOptions { WriteIndented = true })));
        var logos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var equipas = new HashSet<string>(StringComparer.Ordinal);   // "PAIS\tNOME" normalizado
        foreach (var rel in lista)
        {
            var cam = Path.Combine(raiz, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(cam)) continue;
            var (t, _) = Classificar(rel);
            if (t is Tipo.IndiceLogos or Tipo.IndiceLigas) continue;
            Escrever(z, rel, File.ReadAllBytes(cam));
            if (t == Tipo.Logo) logos.Add(rel[(PastaDados.Logos.Length + 1)..]);
            if (t == Tipo.EquipaJson)
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllBytes(cam), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                    var r = doc.RootElement;
                    string S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
                    var pais = S("country").Trim().ToUpperInvariant();
                    equipas.Add(pais + "\t" + NormalizarNome(S("name")));
                    equipas.Add(pais + "\t" + NormalizarNome(S("shortName")));
                }
                catch { }
        }
        foreach (var (ind, porNome) in new[] { ("logos.tsv", true), ("leagues.tsv", false) })
        {
            var arq = Path.Combine(raiz, PastaDados.Logos, ind);
            if (!File.Exists(arq)) continue;
            var linhas = File.ReadAllLines(arq, Encoding.UTF8).Where(l =>
            {
                var c = l.Split('\t');
                if (l.TrimStart().StartsWith('#') || c.Length < 3 || c[0].Trim().Equals("country", StringComparison.OrdinalIgnoreCase)) return false;
                if (logos.Contains(c[2].Trim())) return true;
                return porNome && equipas.Contains(c[0].Trim().ToUpperInvariant() + "\t" + NormalizarNome(c[1]));
            }).ToList();
            if (linhas.Count == 0) continue;
            Escrever(z, PastaDados.Logos + "/" + ind, Utf8.GetBytes((porNome ? "country\tteam\tfile\n" : "country\tlevel\tfile\n") + string.Join('\n', linhas) + "\n"));
        }
    }

    static void Escrever(ZipArchive z, string rel, byte[] b)
    {
        var e = z.CreateEntry(rel, CompressionLevel.Optimal);
        using var s = e.Open();
        s.Write(b);
    }
}
