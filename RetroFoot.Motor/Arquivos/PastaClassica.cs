using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RetroFoot.Motor.Arquivos;

// As pastas do jogo classico instaladas por um launcher: <base>/<qualquer>/game com o
// COUNTRY.TXE dentro (sem depender do nome da pasta do launcher). Erros de leitura (sem
// permissao, pasta sumiu) so pulam a pasta.
public static class PastaClassica
{
    public static IEnumerable<string> Em(string? baseDir)
    {
        if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir)) yield break;
        string[] subs;
        try { subs = Directory.GetDirectories(baseDir); }
        catch { yield break; }
        Array.Sort(subs, StringComparer.Ordinal);
        foreach (var s in subs)
        {
            var g = Path.Combine(s, "game");
            if (TemJogo(g)) yield return g;
        }
    }

    // a do launcher instalado para o usuario (LocalApplicationData)
    public static string? Instalada() =>
        Em(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)).FirstOrDefault();

    public static bool TemJogo(string pasta)
    {
        try
        {
            return Directory.Exists(pasta) && Directory.EnumerateFiles(pasta, "COUNTRY.TXE",
                new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).Any();
        }
        catch { return false; }
    }
}
