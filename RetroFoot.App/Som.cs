using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace RetroFoot.App;

// Os efeitos sonoros (Opcoes > Acessibilidade: "Efeitos sonoros" e o volume). O jogo classico
// so tocava o aviso do Windows nos lances dos times humanos (a opcao "Habilitar som",
// DS:2C6C); aqui os sons sao gerados no proprio jogo (nenhum arquivo de fora): o apito do
// comeco e do intervalo, o apito final, o golo, o aviso (expulsao ou lesao num time humano),
// a compra (um time humano leva o jogador no leilao) e o sorteio (um time humano sai no sorteio
// da Taca).
// Cada som vira um .wav na pasta temporaria (um por volume) e toca com o que o sistema tem:
// PlaySound (Windows), afplay (macOS), paplay / pw-play / aplay (Linux); no Android, o
// TocarAndroid (o MediaPlayer, ligado pela MainActivity). Sem player, nada.
public static class Som
{
    public enum Efeito { Apito, ApitoFinal, Golo, Aviso, Compra, Sorteio }

    // no Android (MainActivity): toca o .wav
    public static Action<string>? TocarAndroid;

    public static bool Ligado { get; set; } = OpcoesDoJogo.Ler()?.Som ?? true;
    // 0 a 100
    public static int Volume { get; set; } = Math.Clamp(OpcoesDoJogo.Ler()?.Volume ?? 50, 0, 100);

    // RETROFOOT_SEM_SOM=1: nada toca (testes, soak e demos na tela virtual)
    static readonly bool Mudo = Environment.GetEnvironmentVariable("RETROFOOT_SEM_SOM") == "1";

    public static void Tocar(Efeito e)
    {
        if (!Ligado || Volume <= 0 || Mudo || (OperatingSystem.IsAndroid() && TocarAndroid == null)) return;
        int vol = Volume;
        Task.Run(() =>
        {
            try { TocarArquivo(Arquivo(e, vol)); }
            catch (Exception ex) { Console.Error.WriteLine("som: " + ex.Message); }
        });
    }

    // ---------------------------------------------------------------- o arquivo
    static readonly object _trava = new();

    static string Arquivo(Efeito e, int vol)
    {
        var pasta = Path.Combine(Path.GetTempPath(), "retrofoot-sons");
        var arq = Path.Combine(pasta, $"{e}-{vol}.wav".ToLowerInvariant());
        lock (_trava)
        {
            if (File.Exists(arq)) return arq;
            Directory.CreateDirectory(pasta);
            var tmp = arq + ".tmp";
            File.WriteAllBytes(tmp, Wav(Amostras(e), vol / 100.0));
            File.Move(tmp, arq, true);
        }
        return arq;
    }

    const int Taxa = 22050;

    // as amostras de -1 a 1
    static List<double> Amostras(Efeito e)
    {
        var s = new List<double>();
        switch (e)
        {
            case Efeito.Apito: Apito(s, 0.45); break;
            case Efeito.ApitoFinal:
                Apito(s, 0.22); Silencio(s, 0.1);
                Apito(s, 0.22); Silencio(s, 0.1);
                Apito(s, 0.7);
                break;
            case Efeito.Golo:
                // um arpejo subindo (do, mi, sol, do), o ultimo mais longo
                foreach (var (f, d) in new[] { (523.25, 0.11), (659.25, 0.11), (783.99, 0.11), (1046.5, 0.45) })
                    Nota(s, f, d);
                break;
            case Efeito.Compra:
                // a caixa registradora: o "ka" seco e o "ching" agudo que soa
                Nota(s, 1318.5, 0.06, decaimento: 30);
                Nota(s, 2637, 0.5, decaimento: 6);
                break;
            case Efeito.Sorteio:
                // a fanfarra curta (sol, do, mi, sol e o do agudo longo)
                foreach (var (f, d) in new[] { (392.0, 0.09), (523.25, 0.09), (659.25, 0.09), (783.99, 0.16), (1046.5, 0.5) })
                    Nota(s, f, d, decaimento: 2.5);
                break;
            case Efeito.Aviso:
                // o "ding" curto, como o aviso do sistema
                Nota(s, 880, 0.12, decaimento: 18);
                Nota(s, 660, 0.25, decaimento: 10);
                break;
        }
        return s;
    }

    // o apito do arbitro: ~2,9 kHz com o trinado da bolinha (modulacao rapida)
    static void Apito(List<double> s, double dur)
    {
        int n = (int)(dur * Taxa);
        double fase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Taxa;
            double f = 2900 + 120 * Math.Sin(2 * Math.PI * 32 * t);
            fase += 2 * Math.PI * f / Taxa;
            double trinado = 0.75 + 0.25 * Math.Sin(2 * Math.PI * 32 * t);
            s.Add(Envelope(i, n, 0.01, 0.04) * trinado * (0.8 * Math.Sin(fase) + 0.2 * Math.Sin(2 * fase)));
        }
    }

    static void Nota(List<double> s, double f, double dur, double decaimento = 4)
    {
        int n = (int)(dur * Taxa);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Taxa;
            double v = Math.Sin(2 * Math.PI * f * t) + 0.3 * Math.Sin(4 * Math.PI * f * t) + 0.1 * Math.Sin(6 * Math.PI * f * t);
            s.Add(Envelope(i, n, 0.005, 0.03) * Math.Exp(-decaimento * t) * v / 1.4);
        }
    }

    static void Silencio(List<double> s, double dur)
    {
        for (int i = 0, n = (int)(dur * Taxa); i < n; i++) s.Add(0);
    }

    // sobe em a segundos e desce nos ultimos r segundos (sem estalos)
    static double Envelope(int i, int n, double a, double r)
    {
        double t = (double)i / Taxa, fim = (double)(n - i) / Taxa;
        return Math.Min(1, Math.Min(t / a, fim / r));
    }

    static byte[] Wav(List<double> s, double volume)
    {
        int dados = s.Count * 2;
        using var m = new MemoryStream();
        using var w = new BinaryWriter(m);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dados); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Taxa); w.Write(Taxa * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dados);
        foreach (var v in s) w.Write((short)Math.Clamp(v * volume * 0.8 * short.MaxValue, short.MinValue, short.MaxValue));
        w.Flush();
        return m.ToArray();
    }

    // ---------------------------------------------------------------- tocar
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    static extern bool PlaySound(string arquivo, IntPtr modulo, uint flags);

    const uint SndAsync = 0x1, SndNoDefault = 0x2, SndFilename = 0x20000;

    static string? _player;
    static bool _procurou;

    static void TocarArquivo(string arq)
    {
        if (OperatingSystem.IsAndroid()) { TocarAndroid?.Invoke(arq); return; }
        if (OperatingSystem.IsWindows()) { PlaySound(arq, IntPtr.Zero, SndAsync | SndNoDefault | SndFilename); return; }
        string? p;
        lock (_trava)
        {
            if (!_procurou)
            {
                _procurou = true;
                _player = OperatingSystem.IsMacOS() ? "afplay" : Achar("paplay") ?? Achar("pw-play") ?? Achar("aplay");
            }
            p = _player;
        }
        if (p == null) return;
        var psi = new ProcessStartInfo(p) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        if (Path.GetFileName(p) == "aplay") psi.ArgumentList.Add("-q");
        psi.ArgumentList.Add(arq);
        using var proc = Process.Start(psi);
        proc?.WaitForExit(5000);
    }

    static string? Achar(string nome)
    {
        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (d.Length == 0) continue;
            var c = Path.Combine(d, nome);
            if (File.Exists(c)) return c;
        }
        return null;
    }
}
