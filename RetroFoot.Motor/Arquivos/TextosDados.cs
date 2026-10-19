using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.NovoJogo;

namespace RetroFoot.Motor.Arquivos;

// As listas do jogo em texto simples (UTF-8, uma por linha, colunas separadas por TAB), no
// lugar dos .TXE cifrados do jogo classico: countries.tsv (COUNTRY.TXE), referees.tsv
// (REFEREE.TXE) e country-groups/bosman.txt e plop.txt (CTRGROUP/*.TXE). Linhas com '#' sao
// comentarios; a 1a linha sem '#' das .tsv e o cabecalho das colunas.
public static class TextosDados
{
    static readonly Encoding Latin1 = Encoding.Latin1;
    static readonly UTF8Encoding Utf8 = new(false);

    public sealed record Pais(string Codigo, string Nome);
    public sealed record Arbitro(string Pais, string Nome, string Origem);

    // ---------------------------------------------------------------- leitura
    // as linhas de dados de uma .tsv: sem comentarios, sem o cabecalho, colunas por TAB
    static IEnumerable<string[]> Linhas(string arquivo, string cabecalho)
    {
        if (!File.Exists(arquivo)) yield break;
        bool primeira = true;
        foreach (var l0 in File.ReadAllLines(arquivo, Encoding.UTF8))
        {
            var l = l0.TrimEnd('\r');
            if (l.Trim().Length == 0 || l.TrimStart().StartsWith('#')) continue;
            var c = l.Split('\t');
            if (primeira)
            {
                primeira = false;
                if (string.Equals(c[0].Trim(), cabecalho, StringComparison.OrdinalIgnoreCase)) continue;
            }
            yield return c;
        }
    }

    public static List<Pais> LerPaises(string arquivo) =>
        Linhas(arquivo, "code").Where(c => c.Length >= 2 && c[0].Trim().Length > 0)
            .Select(c => new Pais(c[0].Trim().ToUpperInvariant(), c[1])).ToList();

    public static List<Arbitro> LerArbitros(string arquivo) =>
        Linhas(arquivo, "country").Where(c => c.Length >= 2 && c[0].Trim().Length > 0 && c[1].Length > 0)
            .Select(c => new Arbitro(c[0].Trim().ToUpperInvariant(), c[1], c.Length > 2 ? c[2].Trim() : "")).ToList();

    // um codigo por linha (as 3 primeiras letras; o resto da linha e ignorado)
    public static List<string> LerGrupo(string arquivo)
    {
        var r = new List<string>();
        if (!File.Exists(arquivo)) return r;
        foreach (var l0 in File.ReadAllLines(arquivo, Encoding.UTF8))
        {
            var l = l0.Trim();
            if (l.Length == 0 || l.StartsWith('#')) continue;
            var cod = l.Split('\t', ' ')[0].ToUpperInvariant();
            r.Add(cod.Length > 3 ? cod[..3] : cod);
        }
        return r;
    }

    // ---------------------------------------------------------------- arbitros do editor
    // A ferramenta Arbitros do hub: os arbitros ativos e tambem os comentados ("#CCC<TAB>nome",
    // como os do original no fim da lista do pacote), que o jogo novo nao usa.
    public sealed record ArbitroEditavel(string Pais, string Nome, string Origem, bool Ativo);

    public static List<ArbitroEditavel> LerArbitrosEditaveis(string arquivo) =>
        File.Exists(arquivo) ? LerArbitrosEditaveis(File.ReadAllLines(arquivo, Encoding.UTF8)) : new();

    public static List<ArbitroEditavel> LerArbitrosEditaveis(IEnumerable<string> linhas)
    {
        var r = new List<ArbitroEditavel>();
        bool cabecalho = false;
        foreach (var l0 in linhas)
        {
            var l = l0.TrimEnd('\r');
            if (l.Trim().Length == 0) continue;
            bool ativo = !l.TrimStart().StartsWith('#');
            var corpo = ativo ? l : l.TrimStart()[1..];
            var c = corpo.Split('\t');
            if (ativo && !cabecalho)
            {
                cabecalho = true;
                if (string.Equals(c[0].Trim(), "country", StringComparison.OrdinalIgnoreCase)) continue;
            }
            // comentario comum (sem "CCC<TAB>nome"): fica de fora
            if (c.Length < 2 || c[1].Length == 0) continue;
            var cod = c[0].Trim();
            if (cod.Length == 0 || (!ativo && (cod.Length != 3 || !cod.All(char.IsLetter)))) continue;
            r.Add(new ArbitroEditavel(cod.ToUpperInvariant(), c[1], c.Length > 2 ? c[2].Trim() : "", ativo));
        }
        return r;
    }

    // os ativos (o que LerArbitros ve) e depois os desativados, comentados
    public static void GravarArbitrosEditaveis(string arquivo, IEnumerable<ArbitroEditavel> arbitros)
    {
        var lista = arbitros.ToList();
        var sb = new StringBuilder();
        sb.Append("# RetroFoot: referees. UTF-8, one per line, columns separated by TAB.\n");
        sb.Append("# country = 3-letter code (countries.tsv); name = up to 40 letters are used\n");
        sb.Append("# source = where the name came from (1998 = original game, fifa, liga, txe = converted patch, user); optional\n");
        sb.Append("country\tname\tsource\n");
        foreach (var a in lista.Where(a => a.Ativo)) sb.Append(a.Pais).Append('\t').Append(a.Nome).Append('\t').Append(a.Origem).Append('\n');
        var fora = lista.Where(a => !a.Ativo).ToList();
        if (fora.Count > 0)
        {
            sb.Append("#\n# Referees switched off (not used in new games).\n");
            sb.Append("# Remove the '#' at the start of a line to use that referee too.\n");
            foreach (var a in fora) sb.Append('#').Append(a.Pais).Append('\t').Append(a.Nome).Append('\t').Append(a.Origem).Append('\n');
        }
        Gravar(arquivo, sb.ToString());
    }

    // uma lista nova do pacote numa pasta de dados que ja tem referees.tsv: entram no fim os
    // arbitros ativos do pacote que o jogador ainda nao tem (nem ativos nem desativados); os
    // dele ficam como estao. Devolve quantos entraram.
    // A mesma pessoa com o nome mais curto ou mais longo ("Wilton Sampaio" e "Wilton Pereira
    // Sampaio") conta como ja tida.
    public static int AcrescentarArbitros(string arquivo, IEnumerable<ArbitroEditavel> pacote)
    {
        var dele = LerArbitrosEditaveis(arquivo);
        var tem = dele.Select(a => (a.Pais, Palavras(a.Nome))).ToList();
        var novos = new List<ArbitroEditavel>();
        foreach (var a in pacote.Where(a => a.Ativo))
        {
            var p = Palavras(a.Nome);
            if (p.Length == 0 || tem.Any(t => t.Pais == a.Pais && MesmaPessoa(t.Item2, p))) continue;
            tem.Add((a.Pais, p));
            novos.Add(a);
        }
        if (novos.Count > 0) GravarArbitrosEditaveis(arquivo, dele.Concat(novos));
        return novos.Count;
    }

    static readonly HashSet<string> Particulas = new() { "de", "da", "do", "dos", "das", "e", "del", "la", "van", "der", "von" };

    // as palavras do nome, sem acentos, em minusculas e sem particulas
    static string[] Palavras(string nome)
    {
        var sem = new string(nome.Normalize(NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        return sem.ToLowerInvariant().Split(new[] { ' ', '-', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !Particulas.Contains(w)).ToArray();
    }

    // o mesmo primeiro nome e todas as palavras do nome mais curto no mais longo
    static bool MesmaPessoa(string[] a, string[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a[0] != b[0]) return false;
        var (curto, longo) = a.Length <= b.Length ? (a, b) : (b, a);
        return curto.All(longo.Contains);
    }

    // ---------------------------------------------------------------- escrita
    public static void GravarPaises(string arquivo, IEnumerable<Pais> paises)
    {
        var sb = new StringBuilder();
        sb.Append("# RetroFoot: countries. UTF-8, one per line, columns separated by TAB.\n");
        sb.Append("# code = 3-letter code used by teams and players (\"country\" in teams/*.json and referees.tsv)\n");
        sb.Append("# name = the name shown in the game (up to 20 letters are used)\n");
        sb.Append("code\tname\n");
        foreach (var p in paises) sb.Append(p.Codigo).Append('\t').Append(p.Nome).Append('\n');
        Gravar(arquivo, sb.ToString());
    }

    public static void GravarArbitros(string arquivo, IEnumerable<Arbitro> arbitros)
    {
        var sb = new StringBuilder();
        sb.Append("# RetroFoot: referees. UTF-8, one per line, columns separated by TAB.\n");
        sb.Append("# country = 3-letter code (countries.tsv); name = up to 40 letters are used\n");
        sb.Append("# source = where the name came from (1998 = original game, fifa, liga, txe = converted patch); optional\n");
        sb.Append("country\tname\tsource\n");
        foreach (var a in arbitros) sb.Append(a.Pais).Append('\t').Append(a.Nome).Append('\t').Append(a.Origem).Append('\n');
        Gravar(arquivo, sb.ToString());
    }

    public static void GravarGrupo(string arquivo, string descricao, IEnumerable<string> codigos)
    {
        var sb = new StringBuilder();
        sb.Append("# RetroFoot: ").Append(descricao).Append('\n');
        sb.Append("# one 3-letter country code per line (countries.tsv)\n");
        foreach (var c in codigos) sb.Append(c).Append('\n');
        Gravar(arquivo, sb.ToString());
    }

    // grava por um temporario; o arquivo de texto anterior fica em <arquivo>.bak
    static void Gravar(string arquivo, string texto)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(arquivo))!);
        var tmp = arquivo + ".tmp";
        File.WriteAllText(tmp, texto, Utf8);
        if (File.Exists(arquivo)) File.Copy(arquivo, arquivo + ".bak", true);
        File.Move(tmp, arquivo, true);
    }

    // ---------------------------------------------------------------- .TXE do jogo classico
    // COUNTRY.TXE: o 1o texto e "Country"; cada outro e "CCC Nome" (codigo 1-3, nome do 5o)
    public static List<Pais> PaisesDoTxe(byte[] txe)
    {
        var l = LerTxe(txe);
        if (l.Count == 0 || Latin1.GetString(l[0]) != "Country") throw new InvalidDataException("COUNTRY.TXE sem o cabeçalho \"Country\"");
        var r = new List<Pais>();
        foreach (var t in l.Skip(1))
        {
            var (cod, nome) = Dados.PartirTxe(t, 255);
            if (cod.Length > 0) r.Add(new Pais(cod, Latin1.GetString(nome)));
        }
        return r;
    }

    // REFEREE.TXE: o 1o texto e "Referee"; cada outro e "CCC Nome"
    public static List<Arbitro> ArbitrosDoTxe(byte[] txe, string origem = "txe")
    {
        var l = LerTxe(txe);
        if (l.Count == 0 || Latin1.GetString(l[0]) != "Referee") throw new InvalidDataException("REFEREE.TXE sem o cabeçalho \"Referee\"");
        var r = new List<Arbitro>();
        foreach (var t in l.Skip(1))
        {
            var (cod, nome) = Dados.PartirTxe(t, 255);
            if (cod.Length > 0) r.Add(new Arbitro(cod, Latin1.GetString(nome), origem));
        }
        return r;
    }

    // CTRGROUP/*.TXE: o 1o texto e o titulo; de cada outro valem as 3 primeiras letras
    public static List<string> GrupoDoTxe(byte[] txe) =>
        LerTxe(txe).Skip(1).Select(t => Latin1.GetString(t, 0, Math.Min(3, t.Length))).ToList();

    public static List<byte[]> LerTxe(byte[] b)
    {
        var lista = new List<byte[]>();
        for (int o = 0; o < b.Length;)
        {
            if (o + 1 + b[o] > b.Length) break;
            var (p, n) = Cifra.TextoTXE(b.AsSpan(o));
            lista.Add(p);
            o += n;
        }
        return lista;
    }

    // de volta ao .TXE (para levar os dados ao jogo classico)
    public static byte[] Txe(string cabecalho, IEnumerable<string> textos)
    {
        var s = new MemoryStream();
        foreach (var t in textos.Prepend(cabecalho))
        {
            var b = EquipaJson.Bytes(t);
            s.Write(Cifra.PorTextoTXE(b));
        }
        return s.ToArray();
    }

    // o texto "CCC Nome" de um arbitro, como o jogo le do REFEREE.TXE (nome ate 40 letras)
    public static byte[] TextoArbitro(Arbitro a) => ListaArbitros.Texto(a.Pais, a.Nome);
}
