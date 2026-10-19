using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RetroFoot.App;

// O visual Moderno (Visual.cs) das janelas do mercado (leilao, salario, plantel, lista de
// jogadores, observados e as listas dos menus): o tema verde e amarelo (TemaVerde) da
// jornada, da classificacao e dos detalhes (cartoes arredondados, Inter, amarelo de realce). As
// janelas continuam caixas no centro do quadro de 800x600 (4:3 e 16:9), com o mesmo
// tamanho e os mesmos controles do original; so muda o desenho.
static class MM
{
    // as cores sao as do TemaVerde (o tema do Moderno); Azul e o destaque (amarelo)
    public static readonly IBrush Fundo = TemaVerde.FundoTela, Barra = TemaVerde.Barra, Cartao = TemaVerde.Cartao;
    public static readonly IBrush Campo = TemaVerde.CartaoForte, Linha = TemaVerde.Linha, Borda = TemaVerde.Borda;
    public static readonly IBrush Texto = TemaVerde.Texto, Fraco = TemaVerde.TextoFraco, Apagado = TemaVerde.Apagado;
    public static readonly IBrush Azul = TemaVerde.Destaque, SobreAzul = TemaVerde.SobreDestaque, AzulFundo = TemaVerde.Selecao;
    public static readonly IBrush Vivo = TemaVerde.Verde, Vermelho = TemaVerde.Vermelho, Amarelo = TemaVerde.Destaque;
    public static readonly IBrush Botao = TemaVerde.CartaoForte, BotaoSobre = TemaVerde.Hover, BotaoBaixo = TemaVerde.Linha;
    // as listas: o poco escuro (como o fundo dos botoes do hub), as linhas pares e a escolhida
    public static readonly IBrush Poco = TemaVerde.FundoEscuro, Zebra = TemaVerde.Zebra, Selecao = TemaVerde.Selecao;
    // o texto da linha escolhida: o amarelo claro (le melhor que o amarelo sobre a Selecao)
    public static readonly IBrush TextoEscolhido = TemaVerde.DestaqueClaro;
    // o botao padrao (Enter): a pilula ouro do Jogar do hub, com o texto e o icone verde-escuros
    public static readonly IBrush Primario = TemaVerde.BotaoPrimario, PrimarioSobre = TemaVerde.BotaoPrimarioHover;
    public static readonly IBrush PrimarioBaixo = TemaVerde.Ouro, PrimarioBorda = TemaVerde.Ouro;
    public static readonly FontFamily Inter = new("fonts:Inter#Inter");

    // a Inter e mais larga que a Arial: um pouco menor para caber nas mesmas colunas
    public const double Escala = 0.92;

    public static IBrush Hex(string s) => new SolidColorBrush(Color.Parse(s));

    public static Fonte Fonte(Fonte f) => new(Inter, Math.Round(f.Tamanho * Escala, 1), f.Negrito);

    public static FormattedText Txt(string s, double tam, IBrush cor, bool negrito = false, FontWeight? peso = null) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(Inter, FontStyle.Normal, peso ?? (negrito ? FontWeight.Bold : FontWeight.Normal)), tam, cor);

    public static void Cartao_(DrawingContext dc, Rect r, IBrush? fundo, double raio, IBrush? borda = null, double espessura = 1)
    {
        var rr = borda != null ? r.Deflate(espessura / 2) : r;
        dc.DrawRectangle(fundo, borda != null ? new Pen(borda, espessura) : null, new RoundedRect(rr, raio));
    }

    // a cor do original no tema escuro: o vermelho e o azul dos avisos ficam, mais claros;
    // o resto (a letra da equipa, o preto) vira o texto
    public static IBrush Cor(IBrush c)
    {
        if (c is ISolidColorBrush s)
        {
            var k = s.Color;
            if (k.R > 200 && k.G < 80 && k.B < 80) return Vermelho;
            if (k.B > 200 && k.R < 80 && k.G < 80) return Azul;
        }
        return Texto;
    }

    public static void Glifo(DrawingContext dc, Bitmap img, Rect origem, Rect destino, bool ligado)
    {
        using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
        using (dc.PushOpacity(ligado ? 1 : 0.3))
            dc.DrawImage(img, origem, destino);
    }

    // a linha escolhida das listas: a Selecao arredondada (o texto vai em TextoEscolhido; o
    // traco a esquerda nao cabe: as colunas do original comecam encostadas)
    public static void Escolhida(DrawingContext dc, Rect r) => Cartao_(dc, r, Selecao, 4);

    // a barra de rolagem fina (o trilho e o cursor arredondados)
    public static void BarraRolagem(DrawingContext dc, Rect barra, Rect cursor, bool rola)
    {
        if (!rola) return;
        var trilho = new Rect(barra.X + barra.Width / 2 - 3, barra.Y + 2, 6, barra.Height - 4);
        Cartao_(dc, trilho, Linha, 3);
        var c = new Rect(trilho.X, Math.Max(trilho.Y, cursor.Y), 6, Math.Min(cursor.Height, trilho.Bottom - Math.Max(trilho.Y, cursor.Y)));
        Cartao_(dc, c, Apagado, 3);
    }

    // ------------------------------------------------------------ a Janela do W95 no Moderno
    // Esconde a moldura do Wine e poe por baixo da area cliente o cartao arredondado com a
    // barra do titulo (e o logo, se houver); o "x" fica onde o da Janela fica (o clique
    // continua com ela).
    public static void Vestir(Janela j, string titulo, bool fechar, Bitmap? logo = null)
    {
        if (j.Children.Count > 0) j.Children[0].IsVisible = false;
        j.Cliente.Background = Brushes.Transparent;
        j.Children.Insert(0, new Moldura(titulo, fechar, logo) { Width = j.Width, Height = j.Height });
    }

    public sealed class Moldura(string titulo, bool fechar, Bitmap? logo) : Control
    {
        public string Titulo = titulo;
        public Bitmap? Logo = logo;
        const double T = 29;

        public override void Render(DrawingContext dc) => DesenharMoldura(dc, Bounds.Size, Titulo, Logo, fechar);
    }

    public static void DesenharMoldura(DrawingContext dc, Size tam, string titulo, Bitmap? logo, bool fechar)
    {
        {
            const double T = 29;
            var r = new Rect(tam);
            Cartao_(dc, r, Cartao, 10, Borda);
            // a barra do titulo: cantos de cima arredondados
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                double w = r.Width, raio = 10;
                c.BeginFigure(new Point(0.5, T), true);
                c.LineTo(new Point(0.5, raio));
                c.ArcTo(new Point(raio, 0.5), new Size(raio - 0.5, raio - 0.5), 0, false, SweepDirection.Clockwise);
                c.LineTo(new Point(w - raio, 0.5));
                c.ArcTo(new Point(w - 0.5, raio), new Size(raio - 0.5, raio - 0.5), 0, false, SweepDirection.Clockwise);
                c.LineTo(new Point(w - 0.5, T));
                c.EndFigure(true);
            }
            dc.DrawGeometry(Barra, null, g);
            dc.FillRectangle(Linha, new Rect(1, T, r.Width - 2, 1));
            double x = 12;
            if (logo != null)
            {
                Glifo(dc, logo, new Rect(logo.Size), new Rect(10, 4, 21, 21), true);
                x = 38;
            }
            var t = Txt(titulo, 13, Texto, peso: FontWeight.SemiBold);
            t.MaxTextWidth = Math.Max(1, r.Width - x - 34);
            t.MaxLineCount = 1;
            t.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(t, new Point(x, Math.Floor((T - t.Height) / 2)));
            if (!fechar) return;
            // o "x" no lugar do botao fechar da Janela (Width - 3 - 2 - 16, 5, 16, 21)
            double cx = r.Width - 13, cy = 15;
            var p = new Pen(Fraco, 1.6, lineCap: PenLineCap.Round);
            dc.DrawLine(p, new Point(cx - 4.5, cy - 4.5), new Point(cx + 4.5, cy + 4.5));
            dc.DrawLine(p, new Point(cx + 4.5, cy - 4.5), new Point(cx - 4.5, cy + 4.5));
        }
    }

    // ------------------------------------------------------------ o Botao do W95 no Moderno
    // O Botao (TelaInicio) continua a receber os cliques, transparente; por baixo, este
    // desenha o botao moderno com o texto, o glifo e o estado dele.
    public static void Cobrir(Botao b, Panel pai, bool padrao = false)
    {
        var v = new BotaoVisto(b, padrao) { IsHitTestVisible = false };
        b.Opacity = 0;
        int i = pai.Children.IndexOf(b);
        if (i < 0) pai.Children.Add(v); else pai.Children.Insert(i, v);
        void Pos()
        {
            Canvas.SetLeft(v, Canvas.GetLeft(b)); Canvas.SetTop(v, Canvas.GetTop(b));
            v.Width = b.Width; v.Height = b.Height;
            v.IsVisible = b.IsVisible;
            v.InvalidateVisual();
        }
        Pos();
        b.PropertyChanged += (_, e) =>
        {
            if (e.Property == Canvas.LeftProperty || e.Property == Canvas.TopProperty || e.Property == Avalonia.Layout.Layoutable.WidthProperty
                || e.Property == Avalonia.Layout.Layoutable.HeightProperty || e.Property == Avalonia.Visual.IsVisibleProperty) Pos();
            else if (e.Property == InputElement.IsPointerOverProperty) v.InvalidateVisual();
        };
        b.AddHandler(InputElement.PointerPressedEvent, (_, _) => { v.Baixo = true; v.InvalidateVisual(); }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        b.AddHandler(InputElement.PointerReleasedEvent, (_, _) => { v.Baixo = false; v.InvalidateVisual(); }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        b.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => { v.Baixo = false; v.InvalidateVisual(); }, RoutingStrategies.Bubble, true);
    }

    sealed class BotaoVisto(Botao b, bool padrao) : Control
    {
        public bool Baixo;

        public override void Render(DrawingContext dc)
        {
            var r = new Rect(Bounds.Size);
            bool hab = b.Habilitado;
            IBrush fundo = !hab ? Barra : Baixo && b.IsPointerOver ? BotaoBaixo : b.IsPointerOver ? BotaoSobre : Botao;
            if (padrao && hab)
            {
                fundo = Baixo && b.IsPointerOver ? PrimarioBaixo : b.IsPointerOver ? PrimarioSobre : Primario;
                Desenhar(dc, r, fundo, PrimarioBorda, b.Texto, b.Glifo, b.NumGlifos, hab, 11, true, cor: SobreAzul);
                return;
            }
            Desenhar(dc, r, fundo, !hab ? Linha : Borda, b.Texto, b.Glifo, b.NumGlifos, hab, 11);
        }
    }

    // um botao moderno: o cartao, o icone (em cima, se houver texto) e o texto. O glifo de
    // bitmap do original vira o icone vetorial dele (IconesModernos); so o que nao tem icone
    // continua com o bitmap
    public static void Desenhar(DrawingContext dc, Rect r, IBrush fundo, IBrush borda, string texto, Bitmap? glifo, int glifos,
        bool hab, double tam, bool negrito = false, IconesModernos.Icone? icone = null, IBrush? cor = null)
    {
        // cor: o texto e o icone (o verde-escuro sobre a pilula ouro); senao os de sempre
        var letra = !hab ? Apagado : cor ?? Texto;
        Cartao_(dc, r, fundo, 8, borda);
        icone ??= IconesModernos.DoBitmap(glifo);
        if (icone != null) glifo = null;
        double gw = 0, gh = 0;
        if (glifo != null) { gw = glifo.Size.Width / Math.Max(1, glifos); gh = glifo.Size.Height; }
        bool tem = glifo != null || icone != null;
        void Por(double x, double y, double l)
        {
            if (icone != null) IconesModernos.Desenhar(dc, icone, new Rect(x, y, l, l), hab, cor);
            else Glifo(dc, glifo!, new Rect(0, 0, gw, gh), new Rect(x, y, l, l), hab);
        }
        var t = Txt(texto, tam, letra, peso: negrito ? FontWeight.Bold : FontWeight.Medium);
        t.MaxTextWidth = Math.Max(1, r.Width - 4);
        t.MaxLineCount = 1;
        t.Trimming = TextTrimming.CharacterEllipsis;
        t.TextAlignment = TextAlignment.Center;
        if (texto.Length == 0)
        {
            if (tem)
            {
                double l = icone != null ? Math.Min(18, Math.Min(r.Width, r.Height) - 8) : Math.Min(Math.Min(gw, gh), Math.Min(r.Width, r.Height) - 8);
                Por(r.X + Math.Floor((r.Width - l) / 2), r.Y + Math.Floor((r.Height - l) / 2), l);
            }
            return;
        }
        if (tem && r.Height >= 48)
        {
            double l = icone != null ? Math.Min(22, r.Height - t.Height - 18) : Math.Min(28, Math.Min(gw, gh));
            double total = l + 5 + t.Height;
            double y = r.Y + Math.Floor((r.Height - total) / 2);
            Por(r.X + Math.Floor((r.Width - l) / 2), y, l);
            dc.DrawText(t, new Point(r.X + 2, y + l + 5));
            return;
        }
        if (tem)
        {
            // icone a esquerda do texto (os botoes baixos)
            double l = icone != null ? 16 : Math.Min(16, Math.Min(gw, gh));
            var t2 = Txt(texto, tam, letra, peso: negrito ? FontWeight.Bold : FontWeight.Medium);
            double total = l + 6 + t2.WidthIncludingTrailingWhitespace;
            double x = r.X + Math.Floor((r.Width - total) / 2);
            Por(x, r.Y + Math.Floor((r.Height - l) / 2), l);
            dc.DrawText(t2, new Point(x + l + 6, r.Y + Math.Floor((r.Height - t2.Height) / 2)));
            return;
        }
        dc.DrawText(t, new Point(r.X + 2, r.Y + Math.Floor((r.Height - t.Height) / 2)));
    }
}

// a lista das TPrintListDlg (TelaListas) no Moderno: o poco escuro, linhas alternadas, o
// escolhido na Selecao e a barra de rolagem fina. Mesmo comportamento da
// Lista do W95: clique, duplo clique, roda, arrastar a barra, setas, Home e End.
sealed class ListaModerna : Control
{
    public Func<int> Quantos = () => 0;
    public Action<DrawingContext, int, Rect, bool>? Desenhar;
    public double AlturaItem = 20;
    int sel = -1;
    double topo;
    double? arrastoY;
    double arrastoTopo;
    public event Action<int>? Clique, Duplo;
    const double Barra = 12;

    public ListaModerna() { Focusable = true; FocusAdorner = null; ClipToBounds = true; }

    public int Sel { get => sel; set { sel = value; Mostrar(value); InvalidateVisual(); } }

    Rect Dentro => new Rect(Bounds.Size).Deflate(new Thickness(1, 4, 1, 4));
    double Total => Quantos() * AlturaItem;
    double MaxTopo => Math.Max(0, Total - Dentro.Height);
    bool ComBarra => MaxTopo > 0;

    void Rolar(double t) { topo = Math.Clamp(t, 0, MaxTopo); InvalidateVisual(); }

    void Mostrar(int i)
    {
        if (i < 0) return;
        double y = i * AlturaItem;
        if (y < topo) topo = y;
        else if (y + AlturaItem > topo + Dentro.Height) topo = y + AlturaItem - Dentro.Height;
        topo = Math.Clamp(topo, 0, MaxTopo);
    }

    Rect Trilho => new(Dentro.Right - Barra, Dentro.Y, Barra, Dentro.Height);

    Rect Cursor_()
    {
        var tr = Trilho;
        double h = Math.Max(24, tr.Height * Dentro.Height / Math.Max(1, Total));
        double y = tr.Y + (MaxTopo == 0 ? 0 : (tr.Height - h) * topo / MaxTopo);
        return new Rect(tr.X, y, Barra, h);
    }

    public override void Render(DrawingContext dc)
    {
        var b = new Rect(Bounds.Size);
        MM.Cartao_(dc, b, MM.Poco, 8, MM.Linha);
        topo = Math.Clamp(topo, 0, MaxTopo);
        var d = Dentro;
        var area = ComBarra ? new Rect(d.X, d.Y, d.Width - Barra, d.Height) : d;
        using (dc.PushClip(area))
        {
            int n = Quantos();
            for (int i = (int)(topo / AlturaItem); i < n; i++)
            {
                double y = area.Y + i * AlturaItem - topo;
                if (y >= area.Bottom) break;
                var r = new Rect(area.X + 4, y, area.Width - 8, AlturaItem);
                if (i == sel) MM.Escolhida(dc, r);
                else if (i % 2 == 1) MM.Cartao_(dc, r, MM.Zebra, 4);
                Desenhar?.Invoke(dc, i, new Rect(r.X + 4, r.Y + Math.Floor((AlturaItem - 15) / 2), r.Width - 8, 16), i == sel);
            }
        }
        if (ComBarra) MM.BarraRolagem(dc, Trilho, Cursor_(), true);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        Focus();
        if (ComBarra && p.X >= Trilho.X)
        {
            var c = Cursor_();
            if (c.Contains(p)) { arrastoY = p.Y; arrastoTopo = topo; e.Pointer.Capture(this); }
            else Rolar(topo + (p.Y < c.Y ? -1 : 1) * Dentro.Height);
            e.Handled = true;
            return;
        }
        int i = (int)Math.Floor((p.Y - Dentro.Y + topo) / AlturaItem);
        if (i < 0 || i >= Quantos()) return;
        sel = i;
        InvalidateVisual();
        e.Handled = true;
        Clique?.Invoke(i);
        if (e.ClickCount == 2) Duplo?.Invoke(i);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (arrastoY is not double y0) return;
        double livre = Trilho.Height - Cursor_().Height;
        if (livre <= 0) return;
        Rolar(arrastoTopo + (e.GetPosition(this).Y - y0) * MaxTopo / livre);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (arrastoY == null) return;
        arrastoY = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Rolar(topo - e.Delta.Y * AlturaItem * 3);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int n = Quantos();
        int vis = Math.Max(1, (int)(Dentro.Height / AlturaItem));
        int novo = e.Key switch
        {
            Key.Up => Math.Max(0, sel - 1),
            Key.Down => Math.Min(n - 1, sel + 1),
            Key.PageUp => Math.Max(0, sel - vis),
            Key.PageDown => Math.Min(n - 1, sel + vis),
            Key.Home => 0,
            Key.End => n - 1,
            _ => -2,
        };
        if (novo == -2 || n == 0) return;
        Sel = novo;
        e.Handled = true;
        Clique?.Invoke(novo);
    }
}
