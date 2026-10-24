using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Platform;
using Avalonia.Threading;

// SOAK_VIDEO=arquivo.mp4: o video do teste, o host e o convidado lado a lado (as janelas headless
// capturadas 8 vezes por segundo e mandadas cruas ao ffmpeg), com a legenda embaixo do que esta
// acontecendo (tirada das telas e janelas abertas) e o titulo de cada lado em cima.
sealed class Video
{
    const int Fps = 8;
    readonly string _saida, _cru, _legendas;
    readonly string _modo;
    readonly Window _esq, _dir;
    readonly Func<(string Host, string Cliente)> _estados;
    readonly Process _ffmpeg;
    readonly Stream _entrada;
    readonly DispatcherTimer _timer;
    int _w, _h, _quadros;
    byte[]? _buf;
    readonly List<(int De, int Ate, string Texto)> _cues = new();
    string _legenda = "";
    int _legendaDe;

    public Video(string saida, string modo, Window esq, Window dir, Func<(string, string)> estados)
    {
        _saida = saida; _modo = modo; _esq = esq; _dir = dir; _estados = estados;
        _cru = Path.ChangeExtension(saida, ".cru.mp4");
        _legendas = Path.ChangeExtension(saida, ".srt");
        _w = (int)esq.Width; _h = (int)esq.Height;
        _ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-y -hide_banner -loglevel error -f rawvideo -pix_fmt bgra -s {_w * 2}x{_h} -r {Fps} -i - -c:v libx264 -preset veryfast -crf 23 -pix_fmt yuv420p \"{_cru}\"")
        { RedirectStandardInput = true, UseShellExecute = false })!;
        _entrada = _ffmpeg.StandardInput.BaseStream;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000 / Fps) };
        _timer.Tick += (_, _) => Quadro();
        _timer.Start();
    }

    void Quadro()
    {
        var a = _esq.CaptureRenderedFrame();
        var b = _dir.CaptureRenderedFrame();
        if (a == null || b == null) return;
        _buf ??= new byte[_w * 2 * _h * 4];
        Copiar(a, 0);
        Copiar(b, _w);
        _entrada.Write(_buf);
        // a legenda do momento
        var (h, c) = _estados();
        var txt = Legenda(h, c);
        if (txt != _legenda)
        {
            if (_legenda.Length > 0) _cues.Add((_legendaDe, _quadros, _legenda));
            _legenda = txt; _legendaDe = _quadros;
        }
        _quadros++;
    }

    void Copiar(Avalonia.Media.Imaging.WriteableBitmap bmp, int x0)
    {
        using var fb = bmp.Lock();
        int w = Math.Min(_w, fb.Size.Width), h = Math.Min(_h, fb.Size.Height);
        bool rgba = fb.Format == PixelFormat.Rgba8888;
        var linha = new byte[w * 4];
        for (int y = 0; y < h; y++)
        {
            Marshal.Copy(fb.Address + y * fb.RowBytes, linha, 0, w * 4);
            if (rgba) for (int i = 0; i < linha.Length; i += 4) (linha[i], linha[i + 2]) = (linha[i + 2], linha[i]);
            Buffer.BlockCopy(linha, 0, _buf!, (y * _w * 2 + x0) * 4, w * 4);
        }
    }

    // o que esta acontecendo, pelas telas e janelas dos dois lados (as janelas primeiro)
    static string Legenda(string h, string c)
    {
        string t = h + " " + c;
        if (t.Contains("TelaPenaltis")) return "Disputa de pênaltis da Copa: aparece ao mesmo tempo nos dois aparelhos";
        if (t.Contains("TelaPenalti")) return "Pênalti: quem bate escolhe o batedor; o resultado aparece para os dois";
        if (t.Contains("TelaLeilao")) return "Leilão da CPU: abre para todos os técnicos ao mesmo tempo e espera todos darem o lance (30 s)";
        if (t.Contains("TelaPedidoSalario") || t.Contains("TelaNovoSalario")) return "Pedido de aumento: o dono do time responde; os outros veem o aviso";
        if (t.Contains("TelaPatrocinio")) return "Proposta de patrocínio: cada técnico responde no seu aparelho";
        if (t.Contains("TelaSubstituicaoForcada")) return "Lesão: o dono do time escolhe quem entra (30 s); o jogo para para todos";
        if (t.Contains("TelaSubstituicao")) return "Intervalo: os técnicos fazem as substituições ao mesmo tempo (30 s)";
        if (t.Contains("TelaDetalhes")) return "Paralisação: o técnico mexe no time e o jogo para para todos, com o aviso nos outros aparelhos (30 s)";
        if (t.Contains("TelaSorteioTimes")) return "Sorteio dos times: cada técnico recebe o seu, nos dois aparelhos";
        if (t.Contains("TelaClassificacao")) return "Fim da rodada: a classificação aparece ao mesmo tempo nos dois";
        if (t.Contains("TelaJornada")) return "Rodada ao vivo: as partidas na mesma velocidade nos dois aparelhos";
        bool hEquipa = h.StartsWith("tela=TelaEquipa"), cEquipa = c.StartsWith("tela=TelaEquipa");
        bool hPronto = h.Contains("TelaAguardandoOnline"), cPronto = c.Contains("TelaEsperaRemota");
        if (hPronto && cEquipa) return "O host deu PRONTO; o convidado ainda prepara o time (o relógio marca o prazo)";
        if (cPronto && hEquipa) return "O convidado deu PRONTO; o host ainda prepara o time (o relógio marca o prazo)";
        if (hPronto && cPronto) return "Todos PRONTOS: a rodada começa";
        if (hEquipa || cEquipa) return "Preparação: cada técnico escala o time no seu aparelho e aperta Jogar (PRONTO)";
        if (t.Contains("sem-sessao")) return "O host abre o jogo; o convidado espera na sala";
        return "Aguardando os outros técnicos…";
    }

    public void Fechar()
    {
        _timer.Stop();
        if (_legenda.Length > 0) _cues.Add((_legendaDe, _quadros, _legenda));
        _entrada.Close();
        _ffmpeg.WaitForExit();
        // a legenda (srt) e o titulo de cada lado, gravados no video
        var sb = new StringBuilder();
        int n = 1;
        foreach (var (de, ate, txt) in _cues)
        {
            if (ate - de < 2) continue;   // (o que passou num instante nao vira legenda)
            sb.Append(n++).Append('\n').Append(Tempo(de)).Append(" --> ").Append(Tempo(ate)).Append('\n').Append(txt).Append("\n\n");
        }
        File.WriteAllText(_legendas, sb.ToString());
        // uma faixa em cima (o titulo de cada lado) e uma embaixo (a legenda): nada cobre as telas
        string fonte = "Inter";
        const int topo = 64, baixo = 96;
        string rotulo(string texto, string x) =>
            $"drawtext=font='{fonte}\\:style=Bold':text='{texto}':fontsize=30:fontcolor=0xF5C518:x={x}:y=17";
        string filtro = string.Join(",",
            $"pad=iw:ih+{topo + baixo}:0:{topo}:color=0x0B2A16",
            rotulo($"HOST", "24"), rotulo("CONVIDADO", $"{_w}+24"),
            $"drawtext=font='{fonte}':text='{_modo}':fontsize=26:fontcolor=white:x=w-tw-24:y=20",
            $"subtitles='{_legendas.Replace("'", "\\'")}':force_style='FontName={fonte},FontSize=11,PrimaryColour=&H00FFFFFF,BorderStyle=1,Outline=0,Shadow=0,MarginV=10,Alignment=2'");
        var p = Process.Start(new ProcessStartInfo("ffmpeg",
            $"-y -hide_banner -loglevel error -i \"{_cru}\" -vf \"{filtro}\" -c:v libx264 -preset medium -crf 21 -pix_fmt yuv420p -movflags +faststart \"{_saida}\"")
        { UseShellExecute = false })!;
        p.WaitForExit();
        Console.WriteLine(p.ExitCode == 0 ? $"VIDEO: {_saida} ({_quadros / Fps} s)" : $"VIDEO: o ffmpeg falhou ({p.ExitCode}); o cru ficou em {_cru}");
        if (p.ExitCode == 0) try { File.Delete(_cru); } catch { }
    }

    static string Tempo(int quadro)
    {
        var t = TimeSpan.FromSeconds(quadro / (double)Fps);
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}";
    }
}
