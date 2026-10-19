using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Platform;

namespace RetroFoot.App;

// O idioma dos textos (Configuracoes.Idioma): pt-PT (padrao, os textos do original), pt-BR,
// en e es. Estilo gettext: a chave e o proprio texto como esta escrito no codigo.
//
// COMO USAR
//   using static RetroFoot.App.Idioma;
//   T("Classificação...")                         o texto traduzido (ou a chave, se faltar)
//   T("{0} equipas por divisão", n)               string.Format da traducao
//   Idioma.Posicao('G')                           a letra da posicao (G/D/M/A do original)
//   No motor (RetroFoot.Motor nao conhece a App): RetroFoot.Motor.Base.Textos.T("...").
//   Traduzir so o que aparece na tela; NUNCA o que e gravado no save (nomes, noticias).
//   A traducao e lida quando o texto e montado: uma tela que guarda textos prontos se refaz
//   quando Idioma.Mudou (ou ao voltar a ela, comparando Idioma.Atual, como a TelaEquipa).
//
// ONDE POR AS TRADUCOES
//   RetroFoot.App/Assets/Idiomas/<idioma>/<grupo>.txt (pt-PT, pt-BR, en, es). O carregador
//   junta TODOS os .txt da pasta: cada grupo de telas tem o seu arquivo (ex. en/equipa.txt),
//   sem conflito. Linhas UTF-8 "chave<TAB>traducao"; '#' no comeco = comentario; um campo
//   entre aspas guarda espacos nas pontas; escapes \t \n \r \\ nos dois campos.
//   A chave e escrita em pt-PT (o original, docs/glossario-idiomas.md). Se ficou em pt-BR no
//   codigo (ex. "Técnico"), o pt-PT/<grupo>.txt a leva de volta ("Técnico<TAB>Treinador").
//   Os termos do futebol em cada idioma: docs/glossario-idiomas.md.
public static class Idioma
{
    public const string PT = "pt-PT", BR = "pt-BR", EN = "en", ES = "es";
    public static readonly string[] Codigos = { PT, BR, EN, ES };
    // o nome de cada idioma no proprio idioma (as escolhas das Configuracoes)
    public static readonly string[] Nomes = { "Português de Portugal", "Português do Brasil", "English", "Español" };

    // RETROFOOT_IDIOMA=en|es|pt-BR|pt-PT passa por cima das Configuracoes (testes, demos)
    public static string Atual { get; private set; } = PT;
    public static event Action? Mudou;

    // lida na 1a traducao (o AssetLoader so existe depois do Avalonia iniciar)
    static Dictionary<string, string>? _tabela;
    static Dictionary<string, string> Tabela => _tabela ??= Carregar(Atual);
    static bool _iniciado;

    static Idioma()
    {
        var env = Environment.GetEnvironmentVariable("RETROFOOT_IDIOMA");
        Aplicar(Normalizar(env) ?? Configuracoes.Ler().Idioma, avisar: false);
    }

    // o codigo valido (pt-pt, PT_BR, EN-us ...) ou null
    public static string? Normalizar(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var x = s.Trim().Replace('_', '-').ToLowerInvariant();
        if (x is "pt-pt" or "pt") return PT;
        if (x == "pt-br") return BR;
        if (x == "en" || x.StartsWith("en-")) return EN;
        if (x == "es" || x.StartsWith("es-")) return ES;
        return null;
    }

    public static void Aplicar(string codigo) => Aplicar(codigo, true);

    static void Aplicar(string codigo, bool avisar)
    {
        codigo = Normalizar(codigo) ?? PT;
        if (_iniciado && codigo == Atual) return;
        _iniciado = true;
        Atual = codigo;
        _tabela = null;
        Motor.Base.Textos.Traduzir = Traduzir;
        if (avisar) Mudou?.Invoke();
    }

    public static string T(string chave) => Traduzir(chave);

    public static string T(string formato, params object?[] args) => string.Format(Traduzir(formato), args);

    // a letra da posicao (0 G, 1 D, 2 M, 3 A; ou a propria letra do original)
    public static string Posicao(int p) => Posicao("GDMA"[p]);
    public static string Posicao(char letra) =>
        Tabela.TryGetValue("posicao:" + letra, out var t) ? t : letra.ToString();

    static string Traduzir(string chave) => chave != null && Tabela.TryGetValue(chave, out var t) ? t : chave!;

    // ---------------------------------------------------------------- arquivos
    static Dictionary<string, string> Carregar(string codigo)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var pasta = new Uri($"avares://RetroFoot.App/Assets/Idiomas/{codigo}/");
            foreach (var uri in AssetLoader.GetAssets(pasta, null)
                         .Where(u => u.AbsolutePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(u => u.AbsolutePath, StringComparer.Ordinal))
            {
                using var f = AssetLoader.Open(uri);
                using var r = new StreamReader(f, Encoding.UTF8);
                Ler(r.ReadToEnd(), d);
            }
        }
        catch (Exception ex) { Console.Error.WriteLine("Idioma " + codigo + ": " + ex.Message); }
        return d;
    }

    // "chave<TAB>traducao" por linha; '#' = comentario; linhas sem TAB sao ignoradas
    internal static void Ler(string texto, Dictionary<string, string> d)
    {
        foreach (var bruta in texto.Split('\n'))
        {
            var l = bruta.TrimEnd('\r');
            if (l.Length == 0 || l[0] == '#') continue;
            int tab = l.IndexOf('\t');
            if (tab <= 0) continue;
            var k = Campo(l[..tab]);
            var v = Campo(l[(tab + 1)..].TrimStart('\t'));
            if (k.Length > 0) d[k] = v;
        }
    }

    static string Campo(string s)
    {
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1];
        if (s.IndexOf('\\') < 0) return s;
        var b = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 == s.Length) { b.Append(s[i]); continue; }
            char c = s[++i];
            b.Append(c switch { 't' => '\t', 'n' => '\n', 'r' => '\r', _ => c });
        }
        return b.ToString();
    }
}
