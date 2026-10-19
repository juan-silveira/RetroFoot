using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RetroFoot.Motor.NovoJogo;

// Acrescimo do RetroFoot (nao existe no original): a lista de arbitros de um jogo novo.
// Os arbitros da FIFA (Assets/arbitros.tsv) substituem os do REFEREE.TXE nos paises que
// a lista tem; os outros paises ficam com os do original. Se o jogador escolher um so
// pais, ficam so os arbitros desse pais. Tudo acontece antes de Criacao.Criar, nos
// textos "CCC Nome" de Dados.Arbitro; o motor continua igual ao original.
public static class ListaArbitros
{
    public const int MaximoNome = 40;   // TReferee: o nome e string[40] (seg03:c6ca)

    // as linhas "pais<TAB>nome" do arbitros.tsv (UTF-8); '#' comeca um comentario
    public static List<(string Pais, string Nome)> LerTsv(IEnumerable<string> linhas)
    {
        var r = new List<(string, string)>();
        foreach (var l in linhas)
        {
            if (l.Length == 0 || l[0] == '#') continue;
            var c = l.Split('\t');
            if (c.Length < 2 || c[0].Trim().Length != 3 || c[1].Trim().Length == 0) continue;
            r.Add((c[0].Trim().ToUpperInvariant(), c[1].Trim()));
        }
        return r;
    }

    // "CCC Nome" em Latin-1, o nome com no maximo 40 letras
    public static byte[] Texto(string pais, string nome)
    {
        var n = ParaLatin1(nome);
        if (n.Length > MaximoNome)
        {
            n = n[..MaximoNome];
            int k = n.LastIndexOf(' ');
            if (k > 0) n = n[..k];
        }
        return Encoding.Latin1.GetBytes(pais + " " + n);
    }

    // letras fora do Latin-1 (ł, ş, č, ő...) perdem o acento
    internal static string ParaLatin1(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if (ch < 256) { sb.Append(ch); continue; }
            var t = ch switch { 'ł' => "l", 'Ł' => "L", 'đ' => "d", 'Đ' => "D", 'ı' => "i", _ => null };
            if (t == null)
                t = new string(ch.ToString().Normalize(NormalizationForm.FormD)
                    .Where(c => c < 256 && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
            sb.Append(t);
        }
        return sb.ToString();
    }

    // a lista para um jogo novo: os da FIFA, na ordem do arquivo, e depois os do original
    // dos paises que a lista da FIFA nao tem
    public static List<byte[]> Juntar(List<byte[]> original, IEnumerable<(string Pais, string Nome)> fifa)
    {
        var r = fifa.Select(a => Texto(a.Pais, a.Nome)).ToList();
        var paises = fifa.Select(a => a.Pais).ToHashSet();
        foreach (var t in original)
        {
            var (cod, _) = Dados.PartirTxe(t, MaximoNome);
            if (!paises.Contains(cod)) r.Add(t);
        }
        return r;
    }

    // com um so pais escolhido, so os arbitros dele, nunca estrangeiros. O jogo pede
    // equipas / 2 + 1 arbitros (seg05:3e83); se o pais tiver menos, os dele repetem-se ate la
    // (cada copia vira outro arbitro, com outro ID: o motor so olha o ID e o pais). Um pais
    // sem nenhum arbitro fica com a lista toda (nao ha nomes dele para usar).
    public static List<byte[]> SoDoPaisEscolhido(List<byte[]> lista, IEnumerable<Pais> paises)
    {
        var escolhidos = paises.Where(p => p.Selecionado && p.Vagas > 0).ToList();
        if (escolhidos.Count != 1) return lista;
        var pais = escolhidos[0];
        var doPais = lista.Where(t => Dados.PartirTxe(t, MaximoNome).Codigo == pais.Codigo).ToList();
        if (doPais.Count == 0) return lista;
        int precisa = pais.Vagas / 2 + 1;
        var r = new List<byte[]>(doPais);
        for (int i = 0; r.Count < precisa; i++) r.Add(doPais[i % doPais.Count]);
        return r;
    }

    public static List<byte[]> ParaJogoNovo(List<byte[]> original, IEnumerable<(string Pais, string Nome)> fifa, IEnumerable<Pais> paises) =>
        SoDoPaisEscolhido(Juntar(original, fifa), paises);
}
