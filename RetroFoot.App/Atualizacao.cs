using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RetroFoot.App;

// A atualizacao do RetroFoot (OTA): ao abrir, o hub pergunta ao GitHub pelas releases do
// projeto (as beta tambem); havendo uma versao maior que a deste executavel, com o pacote
// deste sistema (linux-x64.tar.gz ou windows-x64.zip), o hub oferece atualizar. Em dois
// passos, porque o executavel e um arquivo unico que o processo continua a ler enquanto roda
// (trocado por baixo dele, as partes ainda nao carregadas falham):
//  1. o jogo baixa o pacote, tira dele o executavel novo (RetroFoot.new, ao lado), abre-o com
//     --retrofoot-concluir <executavel> <pid> e fecha;
//  2. o .new (sem tela, Concluir) espera o velho fechar, copia-se por cima do executavel,
//     abre-o e fecha. Na abertura seguinte o .new que sobrou e apagado (LimparAntigo).
// A pasta data (saves, equipas, configuracoes) nao e tocada.
// So vale para o executavel publicado (RetroFoot / RetroFoot.exe numa pasta com escrita);
// rodando pelo dotnet (desenvolvimento), nada.
// No Android o pacote e o APK (android.apk): o jogo o baixa para a PastaApk e o entrega ao
// instalador do sistema (InstalarApk, ligado pelo projeto Android), que pede a confirmacao;
// na 1a vez o Android pede tambem a permissao de instalar apps vindos do RetroFoot. O APK novo
// tem a mesma assinatura e substitui este, com os dados (saves, configuracoes) intactos.
// RETROFOOT_OTA_URL: outra lista de releases (o mesmo JSON do GitHub), para testes.
public static class Atualizacao
{
    const string Releases = "https://api.github.com/repos/juan-silveira/RetroFoot/releases?per_page=20";

    public sealed record Nova(Version Versao, string Tag, string Url, string Arquivo, long Tamanho);

    // a versao deste executavel (a do Sobre, sem o "+commit")
    public static Version Atual => Ler(TelaAcerca.Versao) ?? new Version(0, 0, 0);

    // "v0.1.1-beta" -> 0.1.1
    static Version? Ler(string s)
    {
        var m = System.Text.RegularExpressions.Regex.Match(s, @"(\d+)\.(\d+)(?:\.(\d+))?");
        if (!m.Success) return null;
        return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
    }

    // o executavel em uso, se for o publicado (senao null)
    static string? Executavel
    {
        get
        {
            if (OperatingSystem.IsAndroid() || OperatingSystem.IsBrowser()) return null;
            var p = Environment.ProcessPath;
            if (p == null) return null;
            var n = Path.GetFileName(p);
            return n is "RetroFoot" or "RetroFoot.exe" ? p : null;
        }
    }

    static string? Pacote => OperatingSystem.IsAndroid() ? "android.apk"
        : OperatingSystem.IsLinux() ? "linux-x64.tar.gz" : OperatingSystem.IsWindows() ? "windows-x64.zip" : null;

    public static bool Suportada => OperatingSystem.IsAndroid() ? InstalarApk != null && PastaApk != null : Executavel != null && Pacote != null;

    // no Android (MainActivity): a pasta do APK baixado (o cache do app, a do FileProvider) e
    // quem o abre no instalador do sistema; false = falta a permissao de instalar apps (o
    // Android abriu a tela dela: depois de permitir, chamar de novo)
    public static string? PastaApk;
    public static Func<string, bool>? InstalarApk;

    const string ArgConcluir = "--retrofoot-concluir";

    // o .new da atualizacao anterior (no Windows ele so solta depois que o 2o passo fecha)
    public static void LimparAntigo()
    {
        if (Executavel is not { } e) return;
        foreach (var x in new[] { e + ".new", e + ".old" })
            try { if (File.Exists(x)) File.Delete(x); } catch { }
    }

    // o 2o passo (chamado no comeco do Main, antes da tela): true se era ele
    public static bool Concluir(string[] args)
    {
        if (args.Length < 3 || args[0] != ArgConcluir) return false;
        string destino = args[1];
        try
        {
            if (int.TryParse(args[2], out int pid))
                try { using var p = Process.GetProcessById(pid); p.WaitForExit(20000); } catch { }
            var eu = Environment.ProcessPath!;
            // o Windows pode demorar a soltar o arquivo do processo que fechou
            for (int i = 0; ; i++)
            {
                try { File.Copy(eu, destino, true); break; }
                catch (IOException) when (i < 40) { Thread.Sleep(250); }
                catch (UnauthorizedAccessException) when (i < 40) { Thread.Sleep(250); }
            }
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(destino, File.GetUnixFileMode(eu));
            Process.Start(new ProcessStartInfo(destino) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(destino)! });
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(destino) ?? ".", "atualizacao-erro.txt"), ex.ToString()); } catch { }
        }
        return true;
    }

    static HttpClient Cliente()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("RetroFoot/" + Atual);
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    // a release mais nova que esta versao, com o pacote deste sistema; null sem nada novo
    // (ou sem rede: a procura nunca atrapalha o jogo)
    public static async Task<Nova?> Procurar()
    {
        if (!Suportada) return null;
        try
        {
            using var c = Cliente();
            var url = Environment.GetEnvironmentVariable("RETROFOOT_OTA_URL") is { Length: > 0 } u ? u : Releases;
            var json = await c.GetStringAsync(url);
            if (JsonNode.Parse(json) is not JsonArray lista) return null;
            Nova? melhor = null;
            foreach (var r in lista.OfType<JsonObject>())
            {
                if (r["draft"]?.GetValue<bool>() == true) continue;
                var tag = r["tag_name"]?.GetValue<string>() ?? "";
                if (Ler(tag) is not { } v || v <= Atual || (melhor != null && v <= melhor.Versao)) continue;
                var a = (r["assets"] as JsonArray)?.OfType<JsonObject>()
                    .FirstOrDefault(x => (x["name"]?.GetValue<string>() ?? "").EndsWith(Pacote!, StringComparison.OrdinalIgnoreCase));
                if (a == null) continue;
                melhor = new Nova(v, tag, a["browser_download_url"]!.GetValue<string>(), a["name"]!.GetValue<string>(), a["size"]?.GetValue<long>() ?? 0);
            }
            return melhor;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("atualizacao: " + ex.Message);
            return null;
        }
    }

    // o 1o passo: baixa e tira o executavel novo (RetroFoot.new); devolve o caminho dele, para
    // Reabrir. O executavel em uso nao e tocado. Progresso de 0 a 1
    public static async Task<string> Instalar(Nova n, IProgress<double>? progresso, CancellationToken ct = default)
    {
        if (OperatingSystem.IsAndroid()) return await BaixarApk(n, progresso, ct);
        var exe = Executavel ?? throw new InvalidOperationException("sem executável publicado");
        var pasta = Path.GetDirectoryName(exe)!;
        var baixado = Path.Combine(pasta, ".retrofoot-atualizacao" + (n.Arquivo.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ".zip" : ".tar.gz"));
        var novo = exe + ".new";
        try
        {
            using (var c = Cliente())
            using (var resp = await c.GetAsync(n.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? n.Tamanho;
                await using var origem = await resp.Content.ReadAsStreamAsync(ct);
                await using var destino = File.Create(baixado);
                var buf = new byte[81920];
                long lido = 0;
                for (int k; (k = await origem.ReadAsync(buf, ct)) > 0;)
                {
                    await destino.WriteAsync(buf.AsMemory(0, k), ct);
                    lido += k;
                    if (total > 0) progresso?.Report(Math.Min(1, (double)lido / total));
                }
            }
            // o executavel de dentro do pacote
            string nomeExe = Path.GetFileName(exe);
            bool achou = false;
            if (baixado.EndsWith(".zip"))
            {
                using var z = ZipFile.OpenRead(baixado);
                var e = z.Entries.FirstOrDefault(x => string.Equals(x.Name, nomeExe, StringComparison.OrdinalIgnoreCase));
                if (e != null) { e.ExtractToFile(novo, true); achou = true; }
            }
            else
            {
                await using var f = File.OpenRead(baixado);
                await using var gz = new GZipStream(f, CompressionMode.Decompress);
                using var tar = new TarReader(gz);
                for (TarEntry? e; (e = tar.GetNextEntry()) != null;)
                    if (e.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile && Path.GetFileName(e.Name) == nomeExe)
                    {
                        e.ExtractToFile(novo, true);
                        achou = true;
                        break;
                    }
            }
            if (!achou || new FileInfo(novo).Length < 1_000_000) throw new InvalidDataException("o pacote não tem o executável " + nomeExe);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(novo, File.GetUnixFileMode(exe) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            return novo;
        }
        catch
        {
            try { if (File.Exists(novo)) File.Delete(novo); } catch { }
            throw;
        }
        finally
        {
            try { File.Delete(baixado); } catch { }
        }
    }

    // o Android: o APK baixado na PastaApk (o caminho dele, para o InstalarApk)
    static async Task<string> BaixarApk(Nova n, IProgress<double>? progresso, CancellationToken ct)
    {
        var pasta = PastaApk ?? throw new InvalidOperationException("sem pasta para o APK");
        Directory.CreateDirectory(pasta);
        var apk = Path.Combine(pasta, "retrofoot-atualizacao.apk");
        try
        {
            using var c = Cliente();
            using var resp = await c.GetAsync(n.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? n.Tamanho;
            await using (var origem = await resp.Content.ReadAsStreamAsync(ct))
            await using (var destino = File.Create(apk))
            {
                var buf = new byte[81920];
                long lido = 0;
                for (int k; (k = await origem.ReadAsync(buf, ct)) > 0;)
                {
                    await destino.WriteAsync(buf.AsMemory(0, k), ct);
                    lido += k;
                    if (total > 0) progresso?.Report(Math.Min(1, (double)lido / total));
                }
            }
            if (new FileInfo(apk).Length < 1_000_000) throw new InvalidDataException("o APK baixado está incompleto");
            return apk;
        }
        catch
        {
            try { if (File.Exists(apk)) File.Delete(apk); } catch { }
            throw;
        }
    }

    // abre o .new no 2o passo (ele espera este processo fechar) e fecha este
    public static void Reabrir(string novo, Action sair)
    {
        var psi = new ProcessStartInfo(novo) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(novo)! };
        psi.ArgumentList.Add(ArgConcluir);
        psi.ArgumentList.Add(Executavel!);
        psi.ArgumentList.Add(Environment.ProcessId.ToString());
        Process.Start(psi);
        sair();
    }
}
