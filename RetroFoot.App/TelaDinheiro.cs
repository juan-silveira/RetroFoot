using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Save;
using Mem = RetroFoot.Motor.Partida.Memoria;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// O que as janelas do dinheiro (TelaBalanco, TelaEstadio, TelaEmprestimo) dividem: o dinheiro
// por extenso e com os milhares separados, as cores da equipa, as fontes, o botao com margem
// (TBitBtn com Margin) e o quadro do dialogo (Enter e Esc). Tambem o aviso para quem mostra o
// dinheiro (a tela da equipa) se atualizar depois de construir, pedir ou pagar.
public static class TelaDinheiro
{
    // o dinheiro (+0x71), a divida (+0x6D) ou as bancadas da equipa mudaram (na thread da
    // interface, com o motor parado)
    public static event Action<SEquipa>? Mudou;

    internal static void Avisar(SEquipa e) => Mudou?.Invoke(e);

    // ------------------------------------------------------------ fontes
    static readonly FontFamily MsSans = new("avares://RetroFoot.App/Assets/Fontes#MS Sans Serif");
    static readonly FontFamily Arial = new("avares://RetroFoot.App/Assets/Fontes#Liberation Sans");
    internal static readonly Fonte MsSans11b = new(MsSans, 11, true), MsSans16b = new(MsSans, 16, true),
        Arial9 = new(Arial, 9, false), Arial9b = new(Arial, 9, true), Arial11 = new(Arial, 11, false),
        Arial11b = new(Arial, 11, true), Arial13 = new(Arial, 13, false), Arial16b = new(Arial, 16, true);

    // ------------------------------------------------------------ textos
    internal static string Texto(byte[] b) => b.Length == 0 ? "" : Encoding.Latin1.GetString(Cifra.TextoEFT(b).Texto);

    // ------------------------------------------------------------ cores
    // TColor $00BBGGRR; as cores do sistema (bit de cima) nao aparecem nas equipas
    internal static IBrush CorDelphi(byte[] b, IBrush padrao) =>
        b.Length < 4 || (b[3] & 0x80) != 0 ? padrao : new SolidColorBrush(Color.FromRgb(b[0], b[1], b[2]));

    internal static (IBrush Letra, IBrush Fundo) Cores(SEquipa e) =>
        (CorDelphi(e.CorLetra, W95.Preto), CorDelphi(e.CorFundo, W95.Branco));

    // ------------------------------------------------------------ dialogo
    // poe a janela num quadro que pega o teclado (Enter = botao padrao, Esc = fechar) e o abre
    // pela Sessao (ou pelo "abrir" da Demo)
    internal static Dialogo Abrir(Sessao? s, Janela j, Action<Control>? abrir = null, Action<Control>? fechar = null)
    {
        var d = new Dialogo(j, fechar ?? (c => s?.FecharDialogo(c)));
        (abrir ?? (c => s?.AbrirDialogo(c)))(d);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => d.Focus());
        return d;
    }

    internal sealed class Dialogo : UserControl
    {
        readonly Janela j;
        readonly Action<Control> fechar;
        bool fechado;
        public event Action? Fechou;

        public Dialogo(Janela j, Action<Control> fechar)
        {
            this.j = j;
            this.fechar = fechar;
            Content = j;
            Width = j.Width;
            Height = j.Height;
            Focusable = true;
            FocusAdorner = null;
            j.AoFechar ??= Fechar;
        }

        public void Fechar()
        {
            if (fechado) return;
            fechado = true;
            fechar(this);
            Fechou?.Invoke();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled) return;
            if (e.Key == Key.Enter && j.AoEnter != null) { j.AoEnter(); e.Handled = true; }
            else if (e.Key == Key.Escape) { (j.AoFechar ?? Fechar)(); e.Handled = true; }
        }
    }

    // ------------------------------------------------------------ Demo
    // RETROFOOT_TELA=dinheiro (RETROFOOT_DINHEIRO=balanco|estadio|emprestimo, padrao estadio): o
    // save do oraculo j3, a equipa humana, a janela por cima de um fundo verde de 800x600
    // ampliado como os dialogos da Sessao. RETROFOOT_REGISTADO=0 esconde o Emprestimo.
    public static Control Demo()
    {
        var raiz = new Grid { Background = new SolidColorBrush(Color.FromRgb(0, 128, 0)) };
        var arq = AcharSaveDemo();
        if (arq == null) return new TextBlock { Text = "Demo: não achei RetroFoot.Motor.Testes/Oraculo/j3-entrada.e98", Foreground = Brushes.White };
        var jogo = Jogo98.Ler(File.ReadAllBytes(arq));
        var s = new Sessao(jogo, _ => { });
        var e = s.S.Equipas.FirstOrDefault(x => Mem.Humano(s.TreinadorDa(x))) ?? s.S.Equipas[0];
        var dinheiro = Environment.GetEnvironmentVariable("RETROFOOT_DINHEIRO_VALOR");
        if (int.TryParse(dinheiro, out var din)) e.Dinheiro = (uint)din;
        if (int.TryParse(Environment.GetEnvironmentVariable("RETROFOOT_BANCADAS"), out var banc)) e.Estadio = (byte)banc;
        void AbrirC(Control c)
        {
            var quadro = new Grid { Width = 800, Height = 600 };
            c.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            c.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            quadro.Children.Add(c);
            raiz.Children.Add(new Viewbox { Stretch = Stretch.Uniform, Child = quadro, Tag = c });
        }
        void FecharC(Control c)
        {
            var v = raiz.Children.FirstOrDefault(x => x.Tag == c);
            if (v != null) raiz.Children.Remove(v);
        }
        bool registado = Environment.GetEnvironmentVariable("RETROFOOT_REGISTADO") != "0";
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            switch (Environment.GetEnvironmentVariable("RETROFOOT_DINHEIRO"))
            {
                case "balanco": TelaBalanco.Abrir(s, e, null, AbrirC, FecharC); break;
                case "emprestimo": TelaEmprestimo.Abrir(s, e, null, AbrirC, FecharC); break;
                default: TelaEstadio.Abrir(s, e, registado, null, AbrirC, FecharC); break;
            }
        });
        return raiz;
    }

    static string? AcharSaveDemo()
    {
        var env = Environment.GetEnvironmentVariable("RETROFOOT_SAVE");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        foreach (var ini in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var d = new DirectoryInfo(ini); d != null; d = d.Parent)
            {
                var f = Path.Combine(d.FullName, "RetroFoot.Motor.Testes", "Oraculo", "j3-entrada.e98");
                if (File.Exists(f)) return f;
            }
        return null;
    }
}

// TBitBtn com Layout = blGlyphTop e Margin (CalcButtonLayout do Delphi: o glifo a Margin do
// topo e, com Spacing = -1, o texto no meio do que sobra); Margem = -1 e o Botao comum
// (margem e espaco iguais, um terco do que sobra; Spacing 4 no DFM sem Spacing)
sealed class BotaoMargem : Control
{
    public string Texto = "";
    public Fonte Fonte = TelaDinheiro.Arial9;
    public Bitmap? Glifo;
    public int NumGlifos = 1;
    public double Margem = -1;
    bool habilitado = true;
    bool apertado, preso;
    public event Action? Clique;

    public bool Habilitado { get => habilitado; set { habilitado = value; InvalidateVisual(); } }

    public static BotaoMargem Ok(string texto, Fonte f, double margem) =>
        new() { Texto = texto, Fonte = f, Glifo = W95.Imagem("Botoes/BBOK.png"), NumGlifos = 2, Margem = margem };

    public override void Render(DrawingContext dc)
    {
        var tudo = new Rect(Bounds.Size);
        dc.FillRectangle(W95.Face, tudo);
        bool baixo = apertado && habilitado;
        var r = W95.Relevo(dc, tudo, baixo);
        var t = W95.Txt(Texto, Fonte, W95.Preto);
        double gw = 0, gh = 0;
        if (Glifo != null) { gw = Glifo.PixelSize.Width / (double)NumGlifos; gh = Glifo.PixelSize.Height; }
        double th = Texto.Length > 0 ? t.Height : 0;
        double gy, ty;
        if (Margem >= 0)
        {
            gy = Margem;
            double esp = Math.Floor((r.Height - Margem - gh - th) / 2);
            ty = gy + gh + esp;
        }
        else
        {
            double esp = Glifo != null && Texto.Length > 0 ? 4 : 0;
            gy = Math.Floor((r.Height - (gh + esp + th) + 1) / 2);
            ty = gy + gh + esp;
        }
        double gx = Math.Floor((r.Width - gw + 1) / 2), tx = Math.Floor((r.Width - t.Width + 1) / 2);
        double d = baixo ? 1 : 0;
        if (Glifo != null)
        {
            int q = !habilitado && NumGlifos > 1 ? 1 : 0;
            dc.DrawImage(Glifo, new Rect(q * gw, 0, gw, gh), new Rect(r.X + gx + d, r.Y + gy + d, gw, gh));
        }
        if (Texto.Length == 0) return;
        if (habilitado) dc.DrawText(t, new Point(r.X + tx + d, r.Y + ty + d));
        else W95.TextoCinza(dc, Texto, Fonte, r.X + tx, r.Y + ty);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        preso = apertado = true;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!preso) return;
        bool dentro = new Rect(Bounds.Size).Contains(e.GetPosition(this));
        if (dentro != apertado) { apertado = dentro; InvalidateVisual(); }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!preso) return;
        bool clicou = apertado && habilitado;
        preso = apertado = false;
        e.Pointer.Capture(null);
        InvalidateVisual();
        if (clicou) Clique?.Invoke();
    }
}
