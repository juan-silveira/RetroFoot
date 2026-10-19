using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SEquipa = RetroFoot.Motor.Save.Equipa;

namespace RetroFoot.App;

// O que as janelas do clube no visual Moderno (Visual.cs) dividem: balanco, estadio,
// emprestimo, patrocinio, fim de temporada e as perguntas (Dlg.Perguntar / PedirTexto). As
// cores sao as do TemaVerde e a fonte a da TelaDetalhesModerna e da Classificacao moderna (
// cartoes arredondados, Inter): uma janela com o cabecalho (logo, titulo, subtitulo), o corpo
// e o rodape com os botoes em pilula. Continuam centradas no quadro de 800x600 dos dialogos;
// no 16:9 algumas usam um arranjo mais largo (cada tela decide por Formato.Largo).
static class ClubeModerno
{
    // as cores sao as do TemaVerde (o tema do Moderno); Azul e o destaque (amarelo)
    public static readonly IBrush Fundo = TemaVerde.Fundo, Barra = TemaVerde.Barra, Cartao = TemaVerde.Cartao, Linha = TemaVerde.Linha;
    public static readonly IBrush Texto = TemaVerde.Texto, Fraco = TemaVerde.TextoFraco, Azul = TemaVerde.Destaque, Verde = TemaVerde.Verde;
    public static readonly IBrush Vermelho = TemaVerde.Vermelho, Ouro = TemaVerde.Destaque, Prata = TemaVerde.Prata, Escuro = TemaVerde.SobreDestaque;
    public static readonly FontFamily Inter = new("fonts:Inter#Inter");

    public static IBrush Hex(string s) => new SolidColorBrush(Color.Parse(s));

    public static TextBlock T(string texto, double tam, IBrush cor, FontWeight? peso = null, TextAlignment alin = TextAlignment.Left) => new()
    {
        Text = texto, FontSize = tam, Foreground = cor, FontWeight = peso ?? FontWeight.Normal, TextAlignment = alin,
        TextWrapping = TextWrapping.Wrap,
    };

    // um cartao (o verde mais fundo do hub, com o fio dourado, cantos de 10)
    public static Border Secao(Control filho, double pad = 14) => new()
    {
        Background = Cartao, CornerRadius = new CornerRadius(10), Padding = new Thickness(pad), Child = filho,
        BorderBrush = TemaVerde.Borda, BorderThickness = new Thickness(1),
    };

    // a pintura dos botoes em pilula do Moderno (BotaoModerno, BotaoM, os dos detalhes):
    // principal = a pilula ouro do Jogar do hub; comum = contorno dourado com o texto amarelo;
    // desligado = verde apagado (nunca o amarelo desbotado); rato = em cima
    public static void Pilula(Border b, TextBlock t, bool principal, bool habilitado, bool rato)
    {
        if (!habilitado)
        {
            b.Background = principal ? TemaVerde.CartaoForte : Brushes.Transparent;
            b.BorderBrush = TemaVerde.Linha;
            t.Foreground = TemaVerde.Apagado;
        }
        else if (principal)
        {
            b.Background = rato ? TemaVerde.BotaoPrimarioHover : TemaVerde.BotaoPrimario;
            b.BorderBrush = TemaVerde.Ouro;
            t.Foreground = TemaVerde.SobreDestaque;
        }
        else
        {
            b.Background = rato ? TemaVerde.Hover : Brushes.Transparent;
            b.BorderBrush = rato ? TemaVerde.Ouro : TemaVerde.Borda;
            t.Foreground = rato ? TemaVerde.DestaqueClaro : TemaVerde.Destaque;
        }
        b.Opacity = 1;
    }

    // o rotulo pequeno em cima de um valor ("CAPITAL EM DIVIDA")
    public static TextBlock Rotulo(string texto) => new()
    {
        Text = texto.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Fraco, LetterSpacing = 0.6,
    };

    // uma barra de progresso fina (0 a 1)
    public static Control Progresso(double fracao, IBrush cor) => new Pintor((dc, s) =>
    {
        var r = new Rect(s);
        dc.DrawRectangle(Linha, null, r, s.Height / 2, s.Height / 2);
        double w = Math.Clamp(fracao, 0, 1) * s.Width;
        if (w > 0) dc.DrawRectangle(cor, null, new Rect(0, 0, Math.Max(w, s.Height), s.Height), s.Height / 2, s.Height / 2);
    }) { Height = 6 };
}

// o logo da equipa (Escudos: so no Moderno), ou um circulo nas cores dela
sealed class EscudoClube : Control
{
    readonly Bitmap? _b;
    readonly Color _fundo, _letra;

    public EscudoClube(SEquipa? e, double tam)
    {
        Width = Height = tam;
        _b = e == null ? null : Escudos.Escudo(e);
        _fundo = e != null && e.CorFundo.Length >= 3 ? Color.FromRgb(e.CorFundo[0], e.CorFundo[1], e.CorFundo[2]) : Colors.White;
        _letra = e != null && e.CorLetra.Length >= 3 ? Color.FromRgb(e.CorLetra[0], e.CorLetra[1], e.CorLetra[2]) : Colors.Black;
    }

    public override void Render(DrawingContext dc) => TelaJornada.Logo(dc, _b, _fundo, _letra, new Rect(Bounds.Size).Deflate(2));
}

// o botao em pilula (o "Continuar" da TelaDetalhesModerna): principal (a pilula ouro) ou
// comum (contorno dourado); desligado fica apagado e nao responde
sealed class BotaoModerno : Border
{
    readonly TextBlock _t;
    readonly bool _principal;
    bool _habilitado = true, _rato;
    public event Action? Clique;

    public BotaoModerno(string texto, bool principal = false)
    {
        _principal = principal;
        _t = new TextBlock { Text = texto, FontSize = 14, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Child = _t;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(18);
        Padding = new Thickness(20, 7);
        MinWidth = 96;
        _t.HorizontalAlignment = HorizontalAlignment.Center;
        Cursor = new Cursor(StandardCursorType.Hand);
        Pintar();
        PointerPressed += (_, a) =>
        {
            if (!a.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            a.Handled = true;
            if (_habilitado) Clique?.Invoke();
        };
        PointerEntered += (_, _) => { _rato = true; Pintar(); };
        PointerExited += (_, _) => { _rato = false; Pintar(); };
    }

    public string Texto { get => _t.Text ?? ""; set => _t.Text = value; }
    public bool Habilitado { get => _habilitado; set { _habilitado = value; Pintar(); } }

    void Pintar()
    {
        ClubeModerno.Pilula(this, _t, _principal, _habilitado, _rato);
        Cursor = _habilitado ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
    }
}

// a janela moderna: cabecalho (logo, titulo, subtitulo), o corpo e o rodape com os botoes a
// direita. Enter = AoEnter, Esc = AoFechar (como a Janela do original). Abrir/Fechar pelo
// "abrir/fechar" de quem chama (a Sessao, os Dialogos, as Demos).
sealed class JanelaModerna : UserControl
{
    public Action? AoEnter, AoFechar;
    public Control? Foco;
    public event Action? Fechou;
    Action<Control>? _fechar;
    bool _fechado;
    readonly StackPanel _botoes = new() { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
    readonly DockPanel _rodape = new() { Margin = new Thickness(18, 12) };

    public JanelaModerna(string titulo, string? subtitulo, Control? icone, Control corpo, double largura, double altura)
    {
        Width = largura;
        Height = altura;
        Focusable = true;
        FocusAdorner = null;
        FontFamily = ClubeModerno.Inter;

        var cab = new DockPanel { Margin = new Thickness(20, 14, 20, 14) };
        if (icone != null)
        {
            icone.Margin = new Thickness(0, 0, 14, 0);
            icone.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(icone, Dock.Left);
            cab.Children.Add(icone);
        }
        var tit = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        tit.Children.Add(new TextBlock { Text = titulo, FontSize = 19, FontWeight = FontWeight.Bold, Foreground = ClubeModerno.Texto, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrEmpty(subtitulo))
            tit.Children.Add(new TextBlock { Text = subtitulo, FontSize = 13, Foreground = ClubeModerno.Fraco, TextTrimming = TextTrimming.CharacterEllipsis });
        cab.Children.Add(tit);
        var cabeca = new Border { Background = ClubeModerno.Barra, Child = cab };

        DockPanel.SetDock(_botoes, Dock.Right);
        _rodape.Children.Add(_botoes);
        var rodape = new Border { BorderBrush = ClubeModerno.Linha, BorderThickness = new Thickness(0, 1, 0, 0), Child = _rodape };

        corpo.Margin = new Thickness(20, 16, 20, 4);
        var tudo = new DockPanel();
        DockPanel.SetDock(cabeca, Dock.Top);
        tudo.Children.Add(cabeca);
        DockPanel.SetDock(rodape, Dock.Bottom);
        tudo.Children.Add(rodape);
        tudo.Children.Add(corpo);
        Content = new Border
        {
            Background = ClubeModerno.Fundo, CornerRadius = new CornerRadius(14), ClipToBounds = true,
            BorderBrush = TemaVerde.Borda, BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 8 28 0 #80000000"),
            Child = new ThemeVariantScope { RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark, Child = tudo },
        };
    }

    public BotaoModerno Botao(string texto, bool principal, Action clique)
    {
        var b = new BotaoModerno(texto, principal);
        b.Clique += clique;
        _botoes.Children.Add(b);
        return b;
    }

    // um texto no rodape, a esquerda (uma nota, a contagem)
    public void NotaRodape(Control c)
    {
        c.VerticalAlignment = VerticalAlignment.Center;
        _rodape.Children.Add(c);
    }

    public JanelaModerna Abrir(Action<Control> abrir, Action<Control> fechar)
    {
        _fechar = fechar;
        AoFechar ??= Fechar;
        abrir(this);
        return this;
    }

    public void Fechar()
    {
        if (_fechado) return;
        _fechado = true;
        _fechar?.Invoke(this);
        Fechou?.Invoke();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => { if (Foco is { IsEffectivelyVisible: true } f) f.Focus(); else Focus(); }, DispatcherPriority.Loaded);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (e.Key == Key.Enter && AoEnter != null) { AoEnter(); e.Handled = true; }
        else if (e.Key == Key.Escape && AoFechar != null) { AoFechar(); e.Handled = true; }
    }
}
