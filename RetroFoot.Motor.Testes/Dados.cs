using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RetroFoot.Motor.Arquivos;

namespace RetroFoot.Motor.Testes;

// Os arquivos do jogo original para os testes: a pasta game do launcher (RETROFOOT_JOGO, ou
// <pasta ao lado deste projeto>/game com o COUNTRY.TXE) e os saves reais (RETROFOOT_SAVES,
// Downloads e as pastas listadas em saves-locais.txt, um caminho por linha, fora do git).
static class Arquivos
{
    public static string? Raiz()
    {
        var p = Environment.GetEnvironmentVariable("RETROFOOT_JOGO");
        if (!string.IsNullOrEmpty(p)) return p;
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++, dir = Path.GetDirectoryName(dir))
            if (PastaClassica.Em(Path.GetDirectoryName(dir)).FirstOrDefault() is string g) return Path.GetFullPath(g);
        return null;
    }

    // saves-locais.txt (na raiz de RetroFoot.Motor.Testes, fora do git): pastas com saves reais
    // desta maquina, um caminho por linha (~ = a pasta do usuario; # comenta)
    public static IEnumerable<string> Locais()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++, dir = Path.GetDirectoryName(dir))
        {
            var f = Path.Combine(dir, "saves-locais.txt");
            if (!File.Exists(f)) continue;
            return File.ReadAllLines(f).Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != '#')
                .Select(l => l.StartsWith('~') ? home + l[1..] : l).ToList();
        }
        return [];
    }

    public static IEnumerable<string> Saves()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var pastas = new[]
        {
            Environment.GetEnvironmentVariable("RETROFOOT_SAVES") ?? "",
            Path.Combine(home, "Downloads"),
        }.Concat(Locais());
        foreach (var p in pastas)
        {
            if (p.Length == 0 || !Directory.Exists(p)) continue;
            foreach (var a in Directory.GetFiles(p, "*.e98"))
                if (new FileInfo(a).Length > 1000) yield return a;
        }
    }
}
