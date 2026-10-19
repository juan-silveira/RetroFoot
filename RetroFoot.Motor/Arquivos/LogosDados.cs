using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace RetroFoot.Motor.Arquivos;

// Os indices dos logos do jogador (logos/logos.tsv e logos/leagues.tsv da pasta de dados;
// docs/ferramentas.md, secao 2). UTF-8, colunas separadas por TAB:
//   logos.tsv    country, team (o nome da equipa como no jogo), file ("-" = sem logo)
//   leagues.tsv  country, level (1 a 4, T = taca), file
// O Escudos.cs da App le os mesmos arquivos (a chave da equipa e normalizada do mesmo jeito).
public static class LogosDados
{
    public const string Indice = "logos.tsv", Ligas = "leagues.tsv", SemLogo = "-";

    public sealed record Linha(string Pais, string Chave, string Arquivo);

    // o mesmo de Escudos.Normalizar (App) e casar.norm (python): maiusculas, sem acentos, so
    // A-Z 0-9 e espacos simples
    public static string Normalizar(string s)
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

    static string Pasta(string raiz) => Path.Combine(raiz, PastaDados.Logos);

    // as linhas de um indice (sem comentarios nem cabecalho)
    public static List<Linha> Ler(string raiz, string indice)
    {
        var arq = Path.Combine(Pasta(raiz), indice);
        return File.Exists(arq) ? LerTexto(File.ReadAllText(arq, Encoding.UTF8)) : new List<Linha>();
    }

    public static List<Linha> LerTexto(string texto)
    {
        var r = new List<Linha>();
        foreach (var l0 in texto.Split('\n'))
        {
            var l = l0.TrimEnd('\r');
            if (l.Trim().Length == 0 || l.TrimStart()[0] == '#') continue;
            var c = l.Split('\t');
            if (c.Length < 3 || c[0].Trim().Equals("country", StringComparison.OrdinalIgnoreCase)) continue;
            r.Add(new Linha(c[0].Trim().ToUpperInvariant(), c[1].Trim(), c[2].Trim()));
        }
        return r;
    }

    static string Cabecalho(string indice) => indice == Ligas
        ? "# RetroFoot: league logos. UTF-8, columns separated by TAB. level: 1 to 4 (division), T = cup; file in this folder (- = no logo).\ncountry\tlevel\tfile\n"
        : "# RetroFoot: team logos. UTF-8, columns separated by TAB. team: the team name as in the game; file in this folder (- = no logo).\ncountry\tteam\tfile\n";

    public static void Gravar(string raiz, string indice, IEnumerable<Linha> linhas)
    {
        Directory.CreateDirectory(Pasta(raiz));
        var sb = new StringBuilder(Cabecalho(indice));
        foreach (var l in linhas) sb.Append(l.Pais).Append('\t').Append(l.Chave).Append('\t').Append(l.Arquivo).Append('\n');
        File.WriteAllText(Path.Combine(Pasta(raiz), indice), sb.ToString(), new UTF8Encoding(false));
    }

    static string ChaveDe(string indice, string chave) => indice == Ligas ? chave.Trim().ToUpperInvariant() : Normalizar(chave);

    // a linha de uma equipa (pelo nome completo ou abreviado) ou de uma liga
    public static Linha? Achar(string raiz, string indice, string pais, params string[] chaves)
    {
        pais = pais.Trim().ToUpperInvariant();
        var l = Ler(raiz, indice);
        foreach (var k in chaves.Where(k => k.Trim().Length > 0))
            if (l.FirstOrDefault(x => x.Pais == pais && ChaveDe(indice, x.Chave) == ChaveDe(indice, k)) is { } a) return a;
        return null;
    }

    // poe (ou troca) a linha; remover = as chaves velhas que saem (ex.: o nome antigo)
    public static void Definir(string raiz, string indice, string pais, string chave, string arquivo, params string[] remover)
    {
        pais = pais.Trim().ToUpperInvariant();
        var fora = remover.Append(chave).Where(k => k.Trim().Length > 0).Select(k => ChaveDe(indice, k)).ToHashSet();
        var l = Ler(raiz, indice).Where(x => !(x.Pais == pais && fora.Contains(ChaveDe(indice, x.Chave)))).ToList();
        l.Add(new Linha(pais, chave.Trim(), arquivo));
        Gravar(raiz, indice, l);
    }

    public static void Remover(string raiz, string indice, string pais, params string[] chaves)
    {
        pais = pais.Trim().ToUpperInvariant();
        var fora = chaves.Where(k => k.Trim().Length > 0).Select(k => ChaveDe(indice, k)).ToHashSet();
        var antes = Ler(raiz, indice);
        var l = antes.Where(x => !(x.Pais == pais && fora.Contains(ChaveDe(indice, x.Chave)))).ToList();
        if (l.Count != antes.Count) Gravar(raiz, indice, l);
    }

    // as linhas de um indice de patch entram no do jogador (as do patch substituem as iguais)
    public static int Mesclar(string raiz, string indice, string texto)
    {
        var novas = LerTexto(texto);
        if (novas.Count == 0) return 0;
        var chaves = novas.Select(n => (n.Pais, ChaveDe(indice, n.Chave))).ToHashSet();
        var l = Ler(raiz, indice).Where(x => !chaves.Contains((x.Pais, ChaveDe(indice, x.Chave)))).ToList();
        l.AddRange(novas);
        Gravar(raiz, indice, l);
        return novas.Count;
    }

    // o nome do arquivo de um logo novo: <PAIS>_<slug>.png (a convencao do Escudos.cs)
    public static string ArquivoEquipa(string pais, string nome) => EquipaJson.PastaPais(pais) + "_" + EquipaJson.Slug(nome) + ".png";
    public static string ArquivoLiga(string pais, string nivel) =>
        EquipaJson.PastaPais(pais) + "_" + (nivel.Trim().ToUpperInvariant() == "T" ? "cup" : "league-" + nivel.Trim()) + ".png";
}
