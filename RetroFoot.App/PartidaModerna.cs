using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;

namespace RetroFoot.App;

// O que os dialogos da partida no visual Moderno (Visual.cs) tem em comum: a paleta e a letra
// da jornada e dos detalhes modernos (cartao escuro arredondado, Inter), os logos, os botoes
// em pilula e a lista de jogadores desenhada (setas, Home/End, roda, clique e duplo clique).
// Os dialogos ficam no quadro de 800x600 da Sessao, entao cabem do mesmo jeito em 4:3 e 16:9.
static class PM
{
    // as cores sao as do TemaVerde (o tema do Moderno); Azul e o destaque (amarelo)
    public static readonly IBrush Fundo = TemaVerde.Fundo, Barra = TemaVerde.Barra, Linha = TemaVerde.Linha, Zebra = TemaVerde.Zebra;
    public static readonly IBrush Texto = TemaVerde.Texto, Fraco = TemaVerde.TextoFraco, Vivo = TemaVerde.Verde, Azul = TemaVerde.Destaque;
    public static readonly IBrush Escolhido = TemaVerde.Selecao, Vermelho = TemaVerde.Vermelho, Amarelo = TemaVerde.Destaque, Escuro = TemaVerde.SobreDestaque;
    public static readonly FontFamily Inter = new("fonts:Inter#Inter");

    public static IBrush Hex(string s) => new ImmutableSolidColorBrush(Color.Parse(s));

    public static FormattedText T(string s, double tam, IBrush cor, bool negrito = false) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Inter, FontStyle.Normal, negrito ? FontWeight.SemiBold : FontWeight.Normal), tam * Acessibilidade.Texto, cor);

    public static TextBlock Rotulo(string s, double tam, IBrush cor, bool negrito = false) => new()
    {
        Text = s, FontSize = tam, Foreground = cor, FontWeight = negrito ? FontWeight.SemiBold : FontWeight.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    // o cartao do dialogo, com o tema escuro (as barras de rolagem, a selecao)
    public static Border Cartao(Control corpo) => new()
    {
        Background = Fundo, CornerRadius = new CornerRadius(14), ClipToBounds = true, BorderBrush = TemaVerde.Borda, BorderThickness = new Thickness(1),
        BoxShadow = BoxShadows.Parse("0 8 28 0 #80000000"),
        Child = new ThemeVariantScope { RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark, Child = corpo },
    };

    // o rodape com os botoes a direita (e um texto opcional a esquerda)
    public static Border Rodape(Control? esquerda, params Control[] botoes)
    {
        var g = new DockPanel { Margin = new Thickness(16, 10) };
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var b in botoes) p.Children.Add(b);
        DockPanel.SetDock(p, Dock.Right);
        g.Children.Add(p);
        if (esquerda != null) { esquerda.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(esquerda); }
        return new Border { BorderBrush = Linha, BorderThickness = new Thickness(0, 1, 0, 0), Child = g };
    }

    // a equipa: o logo (ou um circulo nas cores dela) e o nome curto embaixo
    public static Control Equipa(SEquipa? e, double logo = 52, double letra = 15)
    {
        var p = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        p.Children.Add(new EscudoM(e) { Width = logo, Height = logo, HorizontalAlignment = HorizontalAlignment.Center });
        var t = Rotulo(P98.NomeEquipa(e), letra, Texto, true);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        p.Children.Add(t);
        return p;
    }

    // a cor da posicao: G, D, M, A
    public static IBrush CorPosicao(int pos) => pos switch
    {
        0 => TemaVerde.Destaque, 1 => TemaVerde.Azul, 2 => TemaVerde.Verde, _ => TemaVerde.Vermelho,
    };

    // uma linha de jogador: a posicao num circulo, o nome (com a estrela) e a forca a direita
    public static void LinhaJogador(DrawingContext dc, SJogador p, Rect r, bool escolhido, bool apagado)
    {
        if (escolhido) Escolha(dc, r.Deflate(new Thickness(4, 1)));
        double op = apagado ? 0.4 : 1;
        using (dc.PushOpacity(op))
        {
            int pos = Math.Min((int)p.Posicao, 3);
            double raio = 10, cy = r.Y + r.Height / 2;
            var cp = new Point(r.X + 14 + raio, cy);
            dc.DrawEllipse(CorPosicao(pos), null, cp, raio, raio);
            var tp = T(Idioma.Posicao(pos), 11, Escuro, true);
            dc.DrawText(tp, new Point(cp.X - tp.Width / 2, cy - tp.Height / 2));
            var tf = T(p.Forca.ToString(), 13, escolhido ? Azul : Fraco, true);
            double xf = r.Right - 14 - tf.Width;
            dc.DrawText(tf, new Point(xf, cy - tf.Height / 2));
            var tn = T(P98.NomeJogador(p), 13.5, Texto, true);
            double xn = cp.X + raio + 10;
            using (dc.PushClip(new Rect(xn, r.Y, Math.Max(0, xf - 8 - xn), r.Height)))
                dc.DrawText(tn, new Point(xn, cy - tn.Height / 2));
        }
    }

    // a linha escolhida: o verde da selecao com o traco amarelo a esquerda
    public static void Escolha(DrawingContext dc, Rect r)
    {
        dc.DrawRectangle(Escolhido, null, r, 8, 8);
        dc.DrawRectangle(Azul, null, new Rect(r.X + 1, r.Y + 6, 3, Math.Max(0, r.Height - 12)), 1.5, 1.5);
    }

    // a barra de rolagem fina das listas desenhadas
    public static readonly IBrush Rolagem = TemaVerde.Borda;

    public static Control Titulo(string s, double tam = 13) =>
        new TextBlock { Text = s.ToUpperInvariant(), FontSize = tam, FontWeight = FontWeight.SemiBold, Foreground = Fraco, LetterSpacing = 0.6 };
}

// o logo de uma equipa (Escudos; sem logo, o circulo nas cores dela)
sealed class EscudoM : Control
{
    readonly Bitmap? _b;
    readonly Color _fundo, _letra;
    public EscudoM(SEquipa? e)
    {
        _b = e == null ? null : Escudos.Escudo(e);
        _fundo = e != null && e.CorFundo.Length >= 3 ? Color.FromRgb(e.CorFundo[0], e.CorFundo[1], e.CorFundo[2]) : Colors.White;
        _letra = e != null && e.CorLetra.Length >= 3 ? Color.FromRgb(e.CorLetra[0], e.CorLetra[1], e.CorLetra[2]) : Colors.Black;
        IsHitTestVisible = false;
    }
    public override void Render(DrawingContext dc) => TelaJornada.Logo(dc, _b, _fundo, _letra, new Rect(Bounds.Size).Deflate(3));
}

// o botao em pilula (principal: a pilula ouro; ClubeModerno.Pilula); desabilitado fica apagado e nao reage
sealed class BotaoM : Border
{
    readonly bool _principal;
    readonly TextBlock _t;
    bool _hab = true, _rato;
    public event Action? Clique;

    public BotaoM(string texto, bool principal)
    {
        _principal = principal;
        _t = new TextBlock { Text = texto, FontSize = 14, FontWeight = FontWeight.SemiBold, FontFamily = PM.Inter };
        Child = _t;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(18);
        Padding = new Thickness(20, 7);
        Cursor = new Cursor(StandardCursorType.Hand);
        PointerEntered += (_, _) => { _rato = true; Pintar(); };
        PointerExited += (_, _) => { _rato = false; Pintar(); };
        Pintar();
    }

    public string Texto { get => _t.Text ?? ""; set => _t.Text = value; }
    public bool Habilitado { get => _hab; set { _hab = value; Pintar(); } }

    void Pintar()
    {
        ClubeModerno.Pilula(this, _t, _principal, _hab, _rato);
        Cursor = _hab ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
    }

    public void Clicar() { if (_hab && IsVisible) Clique?.Invoke(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true;
        Clicar();
    }
}

// a lista desenhada do visual Moderno: linhas sem borda, a escolhida realcada, a barra de
// rolagem fina; setas, PageUp/PageDown, Home/End, roda, clique e duplo clique
sealed class ListaM : Control
{
    public Func<int> Quantos = () => 0;
    public Action<DrawingContext, int, Rect, bool>? Desenhar;
    public double AlturaItem = 34;
    public bool SemSelecao;
    public string Vazia = "";
    int _sel = -1;
    double _topo;
    bool _hab = true;
    public event Action<int>? Clique, Duplo;

    public ListaM() { Focusable = true; FocusAdorner = null; ClipToBounds = true; }

    public int Sel { get => _sel; set { _sel = value; Mostrar(value); InvalidateVisual(); } }
    public bool Habilitado { get => _hab; set { _hab = value; InvalidateVisual(); } }

    double Total => Quantos() * AlturaItem;
    double MaxTopo => Math.Max(0, Total - Bounds.Height);

    public void Mostrar(int i)
    {
        if (i < 0) return;
        double y = i * AlturaItem;
        if (y < _topo) _topo = y;
        else if (y + AlturaItem > _topo + Bounds.Height) _topo = y + AlturaItem - Bounds.Height;
        _topo = Math.Clamp(_topo, 0, MaxTopo);
        InvalidateVisual();
    }

    public void AoFim() { _topo = MaxTopo; InvalidateVisual(); }

    public override void Render(DrawingContext dc)
    {
        var r = new Rect(Bounds.Size);
        dc.FillRectangle(Brushes.Transparent, r);
        _topo = Math.Clamp(_topo, 0, MaxTopo);
        int n = Quantos();
        if (n == 0 && Vazia.Length > 0)
        {
            var v = PM.T(Vazia, 13, PM.Fraco);
            dc.DrawText(v, new Point((r.Width - v.Width) / 2, 24));
        }
        int i0 = (int)Math.Floor(_topo / AlturaItem);
        for (int i = Math.Max(0, i0); i < n; i++)
        {
            double y = i * AlturaItem - _topo;
            if (y >= r.Height) break;
            Desenhar?.Invoke(dc, i, new Rect(0, y, r.Width - (Total > r.Height ? 6 : 0), AlturaItem), !SemSelecao && i == _sel);
        }
        if (Total > r.Height)
        {
            double h = Math.Max(24, r.Height * r.Height / Total);
            double y = (r.Height - h) * (_topo / Math.Max(1, MaxTopo));
            dc.DrawRectangle(PM.Rolagem, null, new Rect(r.Width - 5, y, 4, h), 2, 2);
        }
        if (IsFocused && _hab && !SemSelecao)
            dc.DrawRectangle(null, new Pen(TemaVerde.Ouro, 1), r.Deflate(0.5), 8, 8);
    }

    void Escolher(int i)
    {
        int n = Quantos();
        if (n == 0) return;
        i = Math.Clamp(i, 0, n - 1);
        Sel = i;
        Clique?.Invoke(i);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!_hab || SemSelecao || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        int i = (int)Math.Floor((e.GetPosition(this).Y + _topo) / AlturaItem);
        if (i < 0 || i >= Quantos()) return;
        e.Handled = true;
        Escolher(i);
        if (e.ClickCount == 2) Duplo?.Invoke(i);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _topo = Math.Clamp(_topo - e.Delta.Y * AlturaItem * 2, 0, MaxTopo);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!_hab || SemSelecao || e.Handled) return;
        int pagina = Math.Max(1, (int)(Bounds.Height / AlturaItem) - 1);
        int? novo = e.Key switch
        {
            Key.Down => _sel < 0 ? 0 : _sel + 1,
            Key.Up => _sel < 0 ? 0 : _sel - 1,
            Key.PageDown => Math.Max(0, _sel) + pagina,
            Key.PageUp => Math.Max(0, _sel) - pagina,
            Key.Home => 0,
            Key.End => Quantos() - 1,
            _ => null,
        };
        if (novo is int i) { Escolher(i); e.Handled = true; }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsFocusedProperty) InvalidateVisual();
    }
}
