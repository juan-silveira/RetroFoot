using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RetroFoot.App;

// As pecas do Retro do computador (os controles de TelaInicio.cs e Dialogos.cs) para as telas
// do celular do comeco, das Configuracoes e das Opcoes, ao toque: o mesmo desenho do Windows
// 95 (a bolinha e a caixa de marcar brancas e afundadas, a caixa de lista do TComboBox com o
// botao da seta, as abas do TTabbedNotebook, o TBevel, os TBitBtn com o glifo) em tamanho de
// dedo (44 pontos), com o texto em Arial.
static class CR95
{
    static readonly BoxShadows BotaoSolto = BoxShadows.Parse("inset 1 1 0 0 #FFFFFF, inset -1 -1 0 0 #000000, inset 2 2 0 0 #DFDFDF, inset -2 -2 0 0 #808080");
    // a caixa branca do TEdit / TListBox / TComboBox (anel sombra/realce e o fio 158 por dentro)
    static readonly BoxShadows CaixaAfundada = BoxShadows.Parse("inset 1 1 0 0 #A6A6A6, inset -1 -1 0 0 #FFFFFF, inset 2 2 0 0 #9E9E9E, inset -2 -2 0 0 #9E9E9E");

    // ---------------------------------------------------------------- texto
    // o texto no Arial do Retro (sem passar pelo CUi: a cor e a do computador)
    public static TextBlock Txt(string s, double tam, IBrush cor, bool negrito = false, bool quebra = true) => new()
    {
        Text = s, FontFamily = W95.Arial13.Familia, FontSize = tam * Acessibilidade.Texto, Foreground = cor,
        FontWeight = negrito ? FontWeight.Bold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = quebra ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = quebra ? TextTrimming.None : TextTrimming.CharacterEllipsis,
    };

    // ---------------------------------------------------------------- molduras
    // o TBevel (bsFrame): o fio cinza com o branco um ponto para dentro e para fora
    public static Border Bevel(Control c, Thickness? padding = null) => new()
    {
        BorderBrush = W95.Sombra, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 1, 1),
        BoxShadow = BoxShadows.Parse("inset 1 1 0 0 #FFFFFF, 1 1 0 0 #FFFFFF"), Padding = padding ?? new Thickness(12, 10), Child = c,
    };

    // a caixa branca afundada de uma lista (o TListBox)
    public static Border CaixaLista(Control c) => new()
    {
        Background = W95.Branco, Padding = new Thickness(2), ClipToBounds = true, BoxShadow = CaixaAfundada, Child = c,
    };

    // a pagina inteira no celular: a barra de titulo da janela em cima (azul-claro, texto preto),
    // a area cliente na cor da janela (rola) e o rodape fixo na mesma cor, com o fio do TPanel
    public static Control Pagina(Control barra, Control corpo, Control? rodape, IBrush fundo, Control? topo = null)
    {
        var d = new DockPanel { Background = fundo };
        DockPanel.SetDock(barra, Dock.Top);
        d.Children.Add(barra);
        if (topo != null) { DockPanel.SetDock(topo, Dock.Top); d.Children.Add(topo); }
        if (rodape != null)
        {
            var r = new Border
            {
                Background = fundo, Padding = new Thickness(12, 10, 12, 12), Child = rodape,
                BorderBrush = W95.Sombra, BorderThickness = new Thickness(0, 1, 0, 0), BoxShadow = BoxShadows.Parse("inset 0 1 0 0 #FFFFFF"),
            };
            DockPanel.SetDock(r, Dock.Bottom);
            d.Children.Add(r);
        }
        d.Children.Add(new ScrollViewer
        {
            Content = new Border { Padding = new Thickness(12, 12, 12, 16), Child = corpo, VerticalAlignment = VerticalAlignment.Top },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        return d;
    }

    // ---------------------------------------------------------------- botoes
    // o TBitBtn: o botao do kit com o glifo do computador (Assets/<caminho>, n glifos lado a
    // lado: o 2o e o desligado), em cima do texto (GlifoEmCima) ou a esquerda
    public static CUi.Botao Botao(string texto, string? glifo, bool principal, Action clique, bool emCima = true, int n = 2, double escala = 1.5)
    {
        var b = new CUi.Botao(texto, principal, clique) { HorizontalAlignment = HorizontalAlignment.Stretch };
        if (glifo == null || W95.Imagem(glifo) is not { } bmp || b.Child is not TextBlock t) return b;
        int gw = bmp.PixelSize.Width / Math.Max(1, n), gh = bmp.PixelSize.Height;
        IImage ligado = new CroppedBitmap(bmp, new PixelRect(0, 0, gw, gh));
        IImage desligado = n > 1 ? new CroppedBitmap(bmp, new PixelRect(gw, 0, gw, gh)) : ligado;
        var img = new Image { Source = ligado, Width = gw * escala, Height = gh * escala, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.HighQuality);
        b.Child = null;
        b.Child = new StackPanel
        {
            Orientation = emCima ? Orientation.Vertical : Orientation.Horizontal, Spacing = emCima ? 2 : 8,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { img, t },
        };
        if (emCima) b.Padding = new Thickness(6, 6);
        b.PropertyChanged += (_, e) => { if (e.Property == InputElement.IsEffectivelyEnabledProperty) img.Source = b.IsEffectivelyEnabled ? ligado : desligado; };
        return b;
    }

    // ---------------------------------------------------------------- marcar
    // a bolinha do TRadioButton e a caixa do TCheckBox (CB_Paint do Wine), uma vez e meia
    sealed class Desenho(bool quadrado) : Control
    {
        public bool Marcado, Habilitado = true;

        public override void Render(DrawingContext dc)
        {
            double s = 20, y0 = Math.Floor((Bounds.Height - s) / 2);
            if (quadrado)
            {
                var r = new Rect(0, y0, s, s);
                dc.FillRectangle(Habilitado ? W95.Branco : W95.Face, r);
                W95.Linhas(dc, r, W95.Sombra, W95.Luz);
                W95.Linhas(dc, r.Deflate(1), W95.Escura, W95.Luz3D);
                if (Marcado)
                {
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(5, y0 + 10), false);
                        c.LineTo(new Point(8.5, y0 + 13.5));
                        c.LineTo(new Point(15, y0 + 6.5));
                        c.EndFigure(false);
                    }
                    dc.DrawGeometry(null, new Pen(Habilitado ? W95.Preto : W95.Cinzento, 3), g);
                }
                return;
            }
            var p = new Point(s / 2, y0 + s / 2);
            dc.DrawEllipse(Habilitado ? W95.Branco : W95.Face, new Pen(W95.Sombra, 1.5), p, 9.25, 9.25);
            dc.DrawEllipse(null, new Pen(W95.Escura, 1.5), p, 7.75, 7.75);
            if (Marcado) dc.DrawEllipse(Habilitado ? W95.Preto : W95.Cinzento, null, p, 4, 4);
        }
    }

    // uma linha com a bolinha (quadrado = false) ou a caixa de marcar e o texto; o toque na
    // linha toda. Desligada: o texto cinza do Windows 95 (sem apagar a linha)
    public sealed class Marcador : Clicavel
    {
        readonly Desenho _d;
        public readonly TextBlock Texto;
        IBrush _cor;

        public Marcador(string texto, bool quadrado, double tam = 15.5, bool negrito = false, IBrush? cor = null)
        {
            _cor = cor ?? W95.Preto;
            _d = new Desenho(quadrado) { Width = 21, MinHeight = 22, VerticalAlignment = VerticalAlignment.Stretch };
            Texto = Txt(texto, tam, _cor, negrito);
            Texto.Margin = new Thickness(10, 0, 0, 0);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            g.Children.Add(_d);
            Grid.SetColumn(Texto, 1);
            g.Children.Add(Texto);
            Child = g;
            Background = Brushes.Transparent;
            MinHeight = 44;
            Padding = new Thickness(2, 4);
            CorFoco = null;
        }

        public bool Marcado { get => _d.Marcado; set { _d.Marcado = value; _d.InvalidateVisual(); } }
        public IBrush Cor { get => _cor; set { _cor = value; if (IsEffectivelyEnabled) Texto.Foreground = value; } }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != IsEffectivelyEnabledProperty) return;
            Opacity = 1;
            _d.Habilitado = IsEffectivelyEnabled;
            _d.InvalidateVisual();
            Texto.Foreground = IsEffectivelyEnabled ? _cor : W95.Cinzento;
        }
    }

    // a caixa de marcar: mudou(v) a cada toque
    public static Marcador Marca(string texto, bool marcado, Action<bool> mudou, bool habilitado = true)
    {
        var m = new Marcador(texto, true) { Marcado = marcado, IsEnabled = habilitado };
        m.Clique += () => { m.Marcado = !m.Marcado; mudou(m.Marcado); };
        return m;
    }

    // um grupo de bolinhas (escolha unica): escolher(i) no toque; devolve o painel e o que marca
    public static (StackPanel Painel, Action<int> Marcar, List<Marcador> Itens) Radios(IReadOnlyList<string> nomes, int atual, Action<int> escolher, double tam = 15.5)
    {
        var p = new StackPanel();
        var ms = new List<Marcador>();
        void Marcar(int k) { for (int i = 0; i < ms.Count; i++) ms[i].Marcado = i == k; }
        for (int i = 0; i < nomes.Count; i++)
        {
            int k = i;
            var m = new Marcador(nomes[i], false, tam);
            m.Clique += () => { Marcar(k); escolher(k); };
            ms.Add(m);
            p.Children.Add(m);
        }
        Marcar(atual);
        return (p, Marcar, ms);
    }

    // ---------------------------------------------------------------- TComboBox
    // a caixa branca com o botao da seta; o toque abre a lista por baixo (o escolhido no realce
    // azul do computador); escolher(i) quando muda
    public sealed class Combo : Panel
    {
        readonly IReadOnlyList<string> _itens;
        readonly TextBlock _t;
        readonly Popup _pop = new() { Placement = PlacementMode.Bottom, IsLightDismissEnabled = true };
        readonly Clicavel _caixa;
        int _indice;
        public event Action<int>? Mudou;

        public Combo(IReadOnlyList<string> itens, int indice, double tam = 15.5)
        {
            _itens = itens;
            _indice = indice;
            _t = Txt(Texto(indice), tam, W95.Preto, false, false);
            _t.Margin = new Thickness(8, 0, 4, 0);
            var seta = new Border
            {
                Width = 32, Background = H("#C0C0C0"), BoxShadow = BotaoSolto, Margin = new Thickness(0, 2, 2, 2),
                Child = new Pintor((dc, s) =>
                {
                    // a seta do computador, maior (o triangulo preto para baixo)
                    double cx = Math.Floor(s.Width / 2), cy = Math.Floor(s.Height / 2);
                    for (int i = 0; i < 6; i++) dc.FillRectangle(W95.Preto, new Rect(cx - 5.5 + i, cy - 3 + i, 11 - 2 * i, 1));
                }),
            };
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            g.Children.Add(_t);
            Grid.SetColumn(seta, 1);
            g.Children.Add(seta);
            _caixa = new Clicavel { Background = W95.Branco, BoxShadow = CaixaAfundada, MinHeight = 44, Padding = new Thickness(2, 0, 0, 0), Child = g, CorFoco = null };
            _caixa.Clique += Abrir;
            _pop.PlacementTarget = _caixa;
            Children.Add(_caixa);
            Children.Add(_pop);
        }

        static IBrush H(string s) => new SolidColorBrush(Color.Parse(s));
        string Texto(int i) => i >= 0 && i < _itens.Count ? _itens[i] : "";

        public int Indice
        {
            get => _indice;
            set { _indice = value; _t.Text = Texto(value); }
        }

        void Abrir()
        {
            var p = new StackPanel();
            for (int i = 0; i < _itens.Count; i++)
            {
                int k = i;
                bool sel = i == _indice;
                var t = Txt(_itens[i], _t.FontSize / Acessibilidade.Texto, sel ? W95.Branco : W95.Preto, false, false);
                var l = new Clicavel { Background = sel ? W95.Destaque : W95.Branco, MinHeight = 44, Padding = new Thickness(10, 0), Child = t, CorFoco = null };
                l.Clique += () =>
                {
                    _pop.IsOpen = false;
                    if (k == _indice) return;
                    Indice = k;
                    Mudou?.Invoke(k);
                };
                p.Children.Add(l);
            }
            _pop.Child = new Border
            {
                Width = _caixa.Bounds.Width, Background = W95.Branco, BorderBrush = W95.Preto, BorderThickness = new Thickness(1),
                Child = new ScrollViewer { Content = p, MaxHeight = 320, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            };
            _pop.IsOpen = true;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            // desligada como no computador: a caixa meio apagada
            if (change.Property == IsEffectivelyEnabledProperty) Opacity = IsEffectivelyEnabled ? 1 : 0.5;
        }
    }

    // ---------------------------------------------------------------- TTabbedNotebook
    // as abas do computador (o texto azul, a escolhida mais alta e colada a pagina) e a pagina
    // cinza com o relevo; trocar(i) na escolha. As abas dividem a largura pelo tamanho do nome
    public sealed class Caderno : Grid
    {
        readonly List<(Clicavel Aba, TextBlock Nome)> _abas = new();
        readonly ContentControl _pagina = new();
        readonly ScrollViewer _rolar;
        readonly Func<int, Control> _montar;
        public int Atual { get; private set; } = -1;
        public event Action<int>? Trocou;

        public Caderno(IReadOnlyList<string> nomes, Func<int, Control> montar, int inicial = 0)
        {
            _montar = montar;
            RowDefinitions = new RowDefinitions("Auto,*");
            var fila = new Grid { Margin = new Thickness(4, 6, 4, -2), ZIndex = 1 };
            for (int i = 0; i < nomes.Count; i++)
            {
                int k = i;
                fila.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(nomes[i].Length + 4, GridUnitType.Star)));
                var t = Txt(nomes[i], 14.5, W95.Azul, false, false);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                var a = new Clicavel { Background = W95.Face, Child = t, CorFoco = null, CornerRadius = new CornerRadius(3, 3, 0, 0), Padding = new Thickness(2, 0) };
                a.Clique += () => Mostrar(k);
                Grid.SetColumn(a, i);
                fila.Children.Add(a);
                _abas.Add((a, t));
            }
            _rolar = new ScrollViewer
            {
                Content = new Border { Padding = new Thickness(12, 14, 12, 16), Child = _pagina, VerticalAlignment = VerticalAlignment.Top },
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            var pagina = new Border
            {
                Background = W95.Face, Margin = new Thickness(4, 0, 4, 4), Child = _rolar,
                BoxShadow = BoxShadows.Parse("inset 1 1 0 0 #FFFFFF, inset -1 -1 0 0 #6A6A6A, inset 2 2 0 0 #E3E3E3, inset -2 -2 0 0 #A6A6A6"),
            };
            Grid.SetRow(pagina, 1);
            Children.Add(pagina);
            Children.Add(fila);
            Mostrar(inicial);
        }

        public void Mostrar(int k)
        {
            if (k == Atual) return;
            Atual = k;
            for (int i = 0; i < _abas.Count; i++)
            {
                bool s = i == k;
                var a = _abas[i].Aba;
                a.Height = s ? 46 : 40;
                a.Margin = s ? new Thickness(-2, 0, -2, 0) : new Thickness(0, 4, 0, 2);
                a.ZIndex = s ? 2 : 0;
                a.VerticalAlignment = VerticalAlignment.Top;
                a.BoxShadow = BoxShadows.Parse(s
                    ? "inset 1 1 0 0 #FFFFFF, inset -1 0 0 0 #6A6A6A, inset -2 0 0 0 #A6A6A6"
                    : "inset 1 1 0 0 #FFFFFF, inset -1 0 0 0 #6A6A6A, inset -2 0 0 0 #A6A6A6, inset 0 -1 0 0 #FFFFFF");
            }
            _pagina.Content = _montar(k);
            _rolar.Offset = default;
            Trocou?.Invoke(k);
        }

        // a pagina de agora feita de novo (o que depende das escolhas mudou)
        public void Refazer() { _pagina.Content = _montar(Atual); }
    }

    // ---------------------------------------------------------------- linhas
    // o rotulo em cima e o controle embaixo, na largura toda (as Opcoes: "Salvar o jogo" e a
    // caixa da lista), com a explicacao cinza do computador
    public static Control Rotulado(string rotulo, Control controle, string? ajuda = null)
    {
        var p = new StackPanel { Spacing = 6, Margin = new Thickness(0, 4, 0, 8) };
        p.Children.Add(Txt(rotulo, 15, W95.Preto));
        p.Children.Add(controle);
        if (ajuda != null) p.Children.Add(Txt(ajuda, 13, W95.Cinzento));
        return p;
    }
}
