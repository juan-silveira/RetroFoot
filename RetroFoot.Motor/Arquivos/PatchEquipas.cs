using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace RetroFoot.Motor.Arquivos;

// Um patch de equipas e logos (.zip) feito pelo Editor de Equipas do RetroFoot, que a
// ferramenta Patches (e PastaDados.Importar) aplica numa pasta de dados:
//   retrofoot-patch.json           o que e (format, version, name, teams, logos, eft)
//   teams/<PAIS>/<slug>.json       as equipas (o mesmo arquivo da pasta de dados)
//   logos/<arquivo>.png            os logos delas e das ligas dos paises delas
//   logos/logos.tsv, leagues.tsv   so as linhas desses logos
//   EQUIPAS/<ARQUIVO>.EFT          (opcional) as mesmas equipas para o jogo classico
// Ao importar, o JSON de uma equipa vale mais que o .EFT dela (PastaDados.Importar).
public static class PatchEquipas
{
    public const string Manifesto = "retrofoot-patch.json", Formato = "retrofoot-patch";

    public sealed record Resumo(int Equipas, int Logos, int Ligas);

    // jsons: os caminhos dos JSON de equipas (da pasta de dados); comEft: tambem os .EFT;
    // ligas: tambem os logos das ligas dos paises das equipas
    public static Resumo Exportar(string raiz, IEnumerable<string> jsons, string zip, string nome, bool comEft, bool ligas = true)
    {
        var equipas = new List<(string Rel, string Json, EquipaEditavel E)>();
        foreach (var j in jsons.Distinct())
        {
            var e = EquipaEditavel.Ler(j);
            var rel = $"{PastaDados.Equipas}/{EquipaJson.PastaPais(e.Pais)}/{Path.GetFileName(j)}";
            equipas.Add((rel, File.ReadAllText(j, Encoding.UTF8), e));
        }
        var pastaLogos = Path.Combine(raiz, PastaDados.Logos);
        var linhas = new List<LogosDados.Linha>();
        var arquivos = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (_, _, e) in equipas)
        {
            var l = LogosDados.Achar(raiz, LogosDados.Indice, e.Pais, e.NomeCompleto, e.NomeAbreviado);
            if (l == null)
            {
                // sem linha no indice: o logo pela convencao <PAIS>_<slug>.png
                foreach (var n in new[] { e.NomeCompleto, e.NomeAbreviado }.Where(n => n.Trim().Length > 0))
                {
                    var conv = LogosDados.ArquivoEquipa(e.Pais, n);
                    if (File.Exists(Path.Combine(pastaLogos, conv))) { l = new LogosDados.Linha(EquipaJson.PastaPais(e.Pais), e.NomeCompleto, conv); break; }
                }
            }
            if (l == null) continue;
            linhas.Add(l);
            if (l.Arquivo != LogosDados.SemLogo && File.Exists(Path.Combine(pastaLogos, l.Arquivo))) arquivos.Add(l.Arquivo);
        }
        var linhasLigas = new List<LogosDados.Linha>();
        if (ligas)
        {
            var paises = equipas.Select(x => EquipaJson.PastaPais(x.E.Pais)).ToHashSet();
            foreach (var l in LogosDados.Ler(raiz, LogosDados.Ligas).Where(l => paises.Contains(l.Pais)))
            {
                linhasLigas.Add(l);
                if (l.Arquivo != LogosDados.SemLogo && File.Exists(Path.Combine(pastaLogos, l.Arquivo))) arquivos.Add(l.Arquivo);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zip))!);
        var tmp = zip + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        using (var z = ZipFile.Open(tmp, ZipArchiveMode.Create, Encoding.UTF8))
        {
            void Texto(string rel, string t)
            {
                using var s = z.CreateEntry(rel, CompressionLevel.Optimal).Open();
                var b = new UTF8Encoding(false).GetBytes(t);
                s.Write(b);
            }
            void Bytes(string rel, byte[] b)
            {
                using var s = z.CreateEntry(rel, CompressionLevel.Optimal).Open();
                s.Write(b);
            }
            var man = new Dictionary<string, object>
            {
                ["format"] = Formato, ["version"] = 1, ["name"] = nome, ["kind"] = "teams",
                ["created"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["teams"] = equipas.Count, ["logos"] = arquivos.Count, ["eft"] = comEft,
            };
            Texto(Manifesto, JsonSerializer.Serialize(man, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
            foreach (var (rel, json, _) in equipas) Texto(rel, json);
            foreach (var a in arquivos) Bytes($"{PastaDados.Logos}/{a}", File.ReadAllBytes(Path.Combine(pastaLogos, a)));
            if (linhas.Count > 0) Texto($"{PastaDados.Logos}/{LogosDados.Indice}", Tsv(LogosDados.Indice, linhas));
            if (linhasLigas.Count > 0) Texto($"{PastaDados.Logos}/{LogosDados.Ligas}", Tsv(LogosDados.Ligas, linhasLigas));
            if (comEft)
                foreach (var (_, _, e) in equipas) Bytes($"EQUIPAS/{e.Arquivo}", e.ParaEft());
        }
        File.Move(tmp, zip, true);
        return new Resumo(equipas.Count, arquivos.Count, linhasLigas.Count);
    }

    static string Tsv(string indice, IEnumerable<LogosDados.Linha> linhas)
    {
        var sb = new StringBuilder(indice == LogosDados.Ligas ? "country\tlevel\tfile\n" : "country\tteam\tfile\n");
        foreach (var l in linhas) sb.Append(l.Pais).Append('\t').Append(l.Chave).Append('\t').Append(l.Arquivo).Append('\n');
        return sb.ToString();
    }

    // o manifesto de um zip (null se nao e um patch do RetroFoot)
    public static JsonDocument? LerManifesto(string zip)
    {
        try
        {
            using var z = ZipFile.OpenRead(zip);
            // (versoes anteriores do jogo usavam outro prefixo: vale qualquer *-patch.json / *-patch)
            var e = z.GetEntry(Manifesto) ?? z.Entries.FirstOrDefault(x => !x.FullName.Contains('/') && x.Name.EndsWith("-patch.json", StringComparison.OrdinalIgnoreCase));
            if (e == null) return null;
            using var s = e.Open();
            var d = JsonDocument.Parse(s);
            return d.RootElement.TryGetProperty("format", out var f) && f.GetString() is string fm && (fm == Formato || fm.EndsWith("-patch")) ? d : null;
        }
        catch { return null; }
    }
}
