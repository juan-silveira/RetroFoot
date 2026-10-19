using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Platform;
using RetroFoot.Motor.Arquivos;

namespace RetroFoot.App;

// A pasta de dados do RetroFoot (Motor/Arquivos/PastaDados.cs; docs/ferramentas.md): equipas,
// logos, saves, patches, paises, arbitros, bandeiras e patrocinadores, numa pasta que o
// jogador abre no gestor de arquivos. Onde fica:
//   RETROFOOT_DADOS            a pasta da variavel (testes, instalacoes especiais);
//   modo portatil              uma pasta "RetroFoot" ao lado do executavel (a config vai junto);
//   normal                     $XDG_DATA_HOME/retrofoot (~/.local/share no Linux; a pasta de
//                              dados do aplicativo nos outros sistemas), a mesma dos JOGOS.
// Na 1a vez (e quando o pacote do jogo muda de versao) os arquivos que vem com o jogo
// (Assets/Dados: as equipas originais de 1998) sao copiados para la sem nunca substituir um
// arquivo do jogador. Os .e98 de JOGOS sao copiados para saves (os de JOGOS ficam).
public static class DadosJogo
{
    // sobe quando Assets/Dados muda (os arquivos novos do pacote entram na pasta do jogador)
    // 2: mais arbitros do Brasil (referees.tsv do jogador recebe os que faltam)
    public const int VersaoPacote = 2;
    const string Marca = ".retrofoot-pacote";
    const string Pacote = "avares://RetroFoot.App/Assets/Dados/";
    static readonly object _trava = new();
    static bool _preparada;

    // Sempre portatil: os dados ficam na pasta data ao lado do executavel (como o
    // jogo classico com as suas pastas). Na 1a vez, o que havia na pasta antiga do sistema
    // ($XDG_DATA_HOME/retrofoot, %LOCALAPPDATA%\RetroFoot...) e copiado para ela. So se a pasta do
    // executavel nao aceitar escrita (ex.: Arquivos de Programas) os dados ficam na do sistema.
    public static string PastaPortatil => Path.Combine(AppContext.BaseDirectory, "data");   // "RetroFoot" batia com o executavel do Linux
    static string PastaSistema => Path.Combine(Configuracoes.RaizDados, Configuracoes.Pasta);
    static string? _raiz;

    public static bool Portatil => Environment.GetEnvironmentVariable("RETROFOOT_DADOS") is not { Length: > 0 } && Raiz == PastaPortatil;

    public static string Raiz
    {
        get
        {
            if (Environment.GetEnvironmentVariable("RETROFOOT_DADOS") is { Length: > 0 } v) return Path.GetFullPath(v);
            if (_raiz != null) return _raiz;
            lock (_trava)
            {
                if (_raiz != null) return _raiz;
                var port = PastaPortatil;
                try
                {
                    if (!Directory.Exists(port))
                    {
                        Directory.CreateDirectory(port);
                        // a pasta antiga do sistema (dados e config.json) vem junto, uma vez
                        if (Directory.Exists(PastaSistema)) CopiarPasta(PastaSistema, port);
                        var cfgAntiga = Path.Combine(Configuracoes.RaizConfig, Configuracoes.Pasta, "config.json");
                        if (File.Exists(cfgAntiga) && !File.Exists(Path.Combine(port, "config.json")))
                            File.Copy(cfgAntiga, Path.Combine(port, "config.json"));
                    }
                    var teste = Path.Combine(port, ".escrita");
                    File.WriteAllText(teste, "");
                    File.Delete(teste);
                    _raiz = port;
                }
                catch { _raiz = PastaSistema; }
                return _raiz;
            }
        }
    }

    static void CopiarPasta(string de, string para)
    {
        foreach (var d in Directory.GetDirectories(de, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(para, Path.GetRelativePath(de, d)));
        foreach (var f in Directory.GetFiles(de, "*", SearchOption.AllDirectories))
        {
            var alvo = Path.Combine(para, Path.GetRelativePath(de, f));
            if (!File.Exists(alvo)) File.Copy(f, alvo);
        }
    }

    public static string Pasta(string sub) => Path.Combine(Raiz, sub);
    public static string Saves => Pasta(PastaDados.Saves);
    public static string Logos => Pasta(PastaDados.Logos);

    // o ultimo relatorio da conversao dos arquivos soltos (para a tela mostrar)
    public static PastaDados.Relatorio? UltimaConversao { get; private set; }

    // cria a pasta, copia o pacote e os saves (uma vez por execucao) e converte os arquivos do
    // jogo classico deixados nela (sempre). Erros nao param o jogo.
    public static string Preparar()
    {
        var raiz = Raiz;
        lock (_trava)
        {
            try
            {
                if (!_preparada)
                {
                    _preparada = true;
                    Directory.CreateDirectory(raiz);
                    foreach (var p in PastaDados.Pastas) Directory.CreateDirectory(Path.Combine(raiz, p));
                    CopiarPacote(raiz);
                    CopiarSaves(raiz);
                }
            }
            catch (Exception e) { Console.Error.WriteLine("dados: " + e.Message); }
            try
            {
                var r = PastaDados.ConverterSoltos(raiz);
                if (r.Mudou || r.Erros.Count > 0)
                {
                    UltimaConversao = r;
                    Console.Error.WriteLine("dados convertidos: " + r);
                    foreach (var e in r.Erros) Console.Error.WriteLine("  " + e);
                }
            }
            catch (Exception e) { Console.Error.WriteLine("dados (conversao): " + e.Message); }
            // os paises do catalogo do jogo que faltam em countries.tsv (um patch antigo que
            // trocou a lista, por exemplo) entram de volta; os do jogador ficam como estao
            try
            {
                int n = PastaDados.CompletarPaises(raiz);
                if (n > 0) Console.Error.WriteLine($"dados: {n} países do catálogo acrescentados a {PastaDados.Paises}");
            }
            catch (Exception e) { Console.Error.WriteLine("dados (países): " + e.Message); }
        }
        return raiz;
    }

    static void CopiarPacote(string raiz)
    {
        var marca = Path.Combine(raiz, Marca);
        try { if (File.Exists(marca) && File.ReadAllText(marca).Trim() == VersaoPacote.ToString()) return; }
        catch { }
        var base0 = new Uri(Pacote);
        var lista = AssetLoader.GetAssets(base0, null).ToList();
        if (lista.Count == 0) return;
        // a mesma equipa (o mesmo .EFT) ja na pasta do jogador, com outro nome de JSON: fica a dele
        var equipas = new HashSet<string>(PastaDados.IndiceEquipas(raiz).Keys, StringComparer.Ordinal);
        int n = 0;
        foreach (var u in lista)
        {
            var rel = Uri.UnescapeDataString(u.AbsolutePath)["/Assets/Dados/".Length..];
            var destino = Path.Combine(raiz, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(destino) && rel != PastaDados.Arbitros) continue;
            using var s = AssetLoader.Open(u);
            using var m = new MemoryStream();
            s.CopyTo(m);
            var b = m.ToArray();
            if (File.Exists(destino))
            {
                // a lista de arbitros do jogador: so acrescenta os novos do pacote
                try
                {
                    var linhas = System.Text.Encoding.UTF8.GetString(b).Split('\n');
                    n += TextosDados.AcrescentarArbitros(destino, TextosDados.LerArbitrosEditaveis(linhas)) > 0 ? 1 : 0;
                }
                catch { }
                continue;
            }
            if (rel.StartsWith(PastaDados.Equipas + "/", StringComparison.Ordinal) && rel.EndsWith(".json", StringComparison.Ordinal))
            {
                try
                {
                    var l = EquipaJson.ParaEft(System.Text.Encoding.UTF8.GetString(b), rel);
                    if (!equipas.Add(l.Arquivo.ToUpperInvariant())) continue;
                }
                catch { continue; }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            File.WriteAllBytes(destino, b);
            n++;
        }
        File.WriteAllText(marca, VersaoPacote.ToString());
        Console.Error.WriteLine($"dados: {n} arquivos do pacote copiados para {raiz}");
    }

    // saves/ nova: os .e98 de JOGOS (a pasta antiga do RetroFoot) ou, sem ela, os do launcher
    static void CopiarSaves(string raiz)
    {
        var saves = Path.Combine(raiz, PastaDados.Saves);
        if (Directory.EnumerateFileSystemEntries(saves).Any()) return;
        var marca = Path.Combine(saves, ".copiados");
        if (File.Exists(marca)) return;
        var antigas = new List<string>
        {
            Path.Combine(Configuracoes.RaizDados, Configuracoes.Pasta, "JOGOS"),
            Path.Combine(raiz, "JOGOS"),
        };
        if (Motor.Arquivos.PastaClassica.Instalada() is string inst) antigas.Add(Path.Combine(inst, "JOGOS"));
        foreach (var a in antigas.Distinct())
        {
            if (!Directory.Exists(a)) continue;
            var arqs = Directory.GetFiles(a, "*.e98", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive });
            if (arqs.Length == 0) continue;
            foreach (var f in arqs) File.Copy(f, Path.Combine(saves, Path.GetFileName(f)), false);
            break;
        }
        File.WriteAllText(marca, "");
    }

    // abre a pasta no gestor de arquivos do sistema (false se nao der: Android, sem xdg-open)
    public static bool AbrirPasta(string? pasta = null)
    {
        pasta ??= Preparar();
        try
        {
            Directory.CreateDirectory(pasta);
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + pasta + "\"") { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS()) Process.Start("open", new[] { pasta });
            else if (OperatingSystem.IsLinux()) Process.Start("xdg-open", new[] { pasta });
            else return false;
            return true;
        }
        catch { return false; }
    }

    public static bool PodeAbrirPasta => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();
}
