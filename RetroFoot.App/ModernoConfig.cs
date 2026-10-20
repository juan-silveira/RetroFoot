using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// As pecas do visual Moderno das telas de configuracao e de comeco (Configuracoes, Inicio,
// Opcoes, Sobre/Registro/Extras, Treinadores): as cores, as letras e os controles do tema
// verde e amarelo do hub (TemaVerde), para todas parecerem um aplicativo so.
static class ConfigEstilo
{
    // as cores sao as do TemaVerde (o tema do Moderno); Azul e o destaque (amarelo), Linha o fio dourado
    public static readonly IBrush Fundo = TemaVerde.Fundo, FundoCartao = TemaVerde.CartaoHub, FundoAviso = TemaVerde.Alfa(TemaVerde.Cores.Selecao, 0.85);
    public static readonly IBrush FundoBarra = TemaVerde.BarraHub, FundoHover = TemaVerde.Hover, FundoZebra = TemaVerde.Zebra;
    public static readonly IBrush Texto = TemaVerde.Texto, TextoSuave = TemaVerde.TextoSuave, TextoFraco = TemaVerde.TextoFraco, Linha = TemaVerde.Borda;
    public static readonly IBrush Azul = TemaVerde.Destaque, AzulFundo = TemaVerde.Selecao, TextoEscuro = TemaVerde.SobreDestaque;
    public static readonly IBrush Erro = TemaVerde.Vermelho, Verde = TemaVerde.Verde, Amarelo = TemaVerde.Destaque;
    public static readonly FontFamily Inter = new("fonts:Inter#Inter");

    public static IBrush Hex(string s) => new SolidColorBrush(Color.Parse(s));

    // o tema escuro do Fluent (caixas de texto, interruptores, barras de rolagem); no Moderno com
    // as cores do TemaVerde no lugar do azul e do cinzento do Fluent
    public static Control Escuro(Control c)
    {
        var s = new ThemeVariantScope { RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark, Child = c };
        if (Visual.Moderno) CoresFluent(s.Resources);
        return s;
    }

    // os pinceis do Fluent que os controles do Moderno usam (interruptor, caixa de marcar, radio,
    // caixas de texto, combos e as listas deles, barras de rolagem, dicas, menus)
    static void CoresFluent(IResourceDictionary r)
    {
        IBrush amarelo = TemaVerde.Destaque, ouro = TemaVerde.Ouro, claro = TemaVerde.DestaqueClaro, escuro = TemaVerde.SobreDestaque;
        void P(IBrush b, params string[] chaves) { foreach (var k in chaves) r[k] = b; }
        foreach (var e in new[] { "", "PointerOver", "Pressed" })
        {
            P(e == "PointerOver" ? claro : e == "Pressed" ? ouro : amarelo, "ToggleSwitchFillOn" + e, "ToggleSwitchStrokeOn" + e,
                "CheckBoxCheckBackgroundFillChecked" + e, "CheckBoxCheckBackgroundStrokeChecked" + e,
                "CheckBoxCheckBackgroundFillIndeterminate" + e, "CheckBoxCheckBackgroundStrokeIndeterminate" + e,
                "RadioButtonOuterEllipseCheckedStroke" + e, "RadioButtonCheckGlyphFill" + e, "RadioButtonCheckGlyphStroke" + e,
                "SliderTrackValueFill" + e, "SliderThumbBackground" + e);
            P(escuro, "ToggleSwitchKnobFillOn" + e, "CheckBoxCheckGlyphForegroundChecked" + e, "CheckBoxCheckGlyphForegroundIndeterminate" + e);
            P(Brushes.Transparent, "RadioButtonOuterEllipseCheckedFill" + e);
            P(e == "" ? TemaVerde.BotaoSecundario : TemaVerde.Hover, "ToggleSwitchFillOff" + e, "CheckBoxCheckBackgroundFillUnchecked" + e, "RadioButtonOuterEllipseFill" + e);
            P(e == "" ? TemaVerde.TextoSuave : TemaVerde.Texto, "ToggleSwitchStrokeOff" + e, "ToggleSwitchKnobFillOff" + e,
                "CheckBoxCheckBackgroundStrokeUnchecked" + e, "RadioButtonOuterEllipseStroke" + e);
        }
        // desligados: o verde apagado (nao o amarelo apagado)
        P(TemaVerde.Linha, "ToggleSwitchFillOnDisabled", "CheckBoxCheckBackgroundFillCheckedDisabled", "ToggleSwitchFillOffDisabled",
            "CheckBoxCheckBackgroundFillUncheckedDisabled");
        P(TemaVerde.Apagado, "ToggleSwitchStrokeOnDisabled", "ToggleSwitchStrokeOffDisabled", "ToggleSwitchKnobFillOnDisabled",
            "ToggleSwitchKnobFillOffDisabled", "CheckBoxCheckBackgroundStrokeCheckedDisabled", "CheckBoxCheckBackgroundStrokeUncheckedDisabled",
            "CheckBoxCheckGlyphForegroundCheckedDisabled", "RadioButtonOuterEllipseCheckedStrokeDisabled", "RadioButtonCheckGlyphFillDisabled",
            "SliderTrackValueFillDisabled", "SliderThumbBackgroundDisabled");
        P(TemaVerde.Linha, "SliderTrackFill", "SliderTrackFillPointerOver", "SliderTrackFillPressed");
        // caixas de texto e combos: o campo verde, o fio dourado, o foco amarelo
        P(TemaVerde.CartaoForte, "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
            "ComboBoxBackground", "ComboBoxBackgroundPressed", "ComboBoxBackgroundUnfocused");
        P(TemaVerde.Hover, "ComboBoxBackgroundPointerOver");
        P(TemaVerde.Alfa(TemaVerde.Cores.CartaoForte, 0.5), "TextControlBackgroundDisabled", "ComboBoxBackgroundDisabled");
        P(TemaVerde.Borda, "TextControlBorderBrush", "ComboBoxBorderBrush", "TextControlBorderBrushDisabled", "ComboBoxBorderBrushDisabled");
        P(TemaVerde.TextoSuave, "TextControlBorderBrushPointerOver", "ComboBoxBorderBrushPointerOver");
        P(amarelo, "TextControlBorderBrushFocused", "ComboBoxBorderBrushPressed", "ComboBoxBackgroundBorderBrushFocused");
        P(TemaVerde.Texto, "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused",
            "ComboBoxForeground", "ComboBoxForegroundFocused", "ComboBoxForegroundFocusedPressed");
        P(TemaVerde.Apagado, "TextControlForegroundDisabled", "ComboBoxForegroundDisabled", "TextControlPlaceholderForegroundDisabled");
        P(TemaVerde.TextoFraco, "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused");
        P(TemaVerde.TextoSuave, "ComboBoxDropDownGlyphForeground", "TextControlButtonForeground");
        r["TextControlSelectionHighlightColor"] = TemaVerde.Alfa(TemaVerde.Cores.Destaque, 0.45);
        // a lista aberta do combo, os menus e as dicas: o verde do menu com o fio dourado
        P(TemaVerde.Menu, "ComboBoxDropDownBackground", "MenuFlyoutPresenterBackground", "FlyoutPresenterBackground", "ToolTipBackground");
        P(TemaVerde.Borda, "ComboBoxDropDownBorderBrush", "MenuFlyoutPresenterBorderBrush", "ToolTipBorderBrush");
        P(TemaVerde.Texto, "ComboBoxItemForeground", "ComboBoxItemForegroundPointerOver", "ComboBoxItemForegroundPressed",
            "ComboBoxItemForegroundSelected", "ComboBoxItemForegroundSelectedPointerOver", "ComboBoxItemForegroundSelectedPressed",
            "MenuFlyoutItemForeground", "MenuFlyoutItemForegroundPointerOver", "MenuFlyoutItemForegroundPressed", "ToolTipForeground");
        P(TemaVerde.Hover, "ComboBoxItemBackgroundPointerOver", "ComboBoxItemBackgroundPressed", "MenuFlyoutItemBackgroundPointerOver", "MenuFlyoutItemBackgroundPressed");
        P(TemaVerde.Selecao, "ComboBoxItemBackgroundSelected", "ComboBoxItemBackgroundSelectedPointerOver", "ComboBoxItemBackgroundSelectedPressed");
        P(TemaVerde.Apagado, "ComboBoxItemForegroundDisabled", "MenuFlyoutItemForegroundDisabled");
        // as barras de rolagem: o polegar verde-claro
        P(TemaVerde.Alfa(TemaVerde.Cores.TextoFraco, 0.55), "ScrollBarPanningThumbBackground");
        P(TemaVerde.TextoFraco, "ScrollBarThumbFillPointerOver");
        P(TemaVerde.TextoSuave, "ScrollBarThumbFillPressed");
        P(TemaVerde.FundoEscuro, "ScrollBarTrackFill", "ScrollBarTrackFillPointerOver");
        P(TemaVerde.TextoSuave, "ScrollBarButtonArrowForeground", "ScrollBarButtonArrowForegroundPointerOver");
        r["ScrollBarThumbBackgroundColor"] = TemaVerde.Cores.TextoFraco;
        r["ScrollBarPanningThumbBackgroundColor"] = TemaVerde.Cores.TextoFraco;
        // a barra de progresso e o resto do Fluent que usa o acento
        r["SystemAccentColor"] = TemaVerde.Cores.Destaque;
        r["SystemAccentColorLight1"] = TemaVerde.Cores.DestaqueClaro;
        r["SystemAccentColorDark1"] = TemaVerde.Cores.Ouro;
        P(amarelo, "SystemControlHighlightAccentBrush");
    }

    public static TextBlock Ajuda(string s, double tamanho = 12.5) => new()
    {
        Text = s, FontSize = tamanho, Foreground = TextoSuave, TextWrapping = TextWrapping.Wrap, LineHeight = tamanho * 1.4,
    };

    public static TextBlock Titulo(string s, double tamanho = 16, IBrush? cor = null) => new()
    {
        Text = s, FontSize = tamanho, FontWeight = FontWeight.SemiBold, Foreground = cor ?? Texto, TextWrapping = TextWrapping.Wrap,
    };

    public static Control Separador() => new Border { Height = 1, Background = Linha };

    public static Border Cartao(string? titulo, Control? direita, Control conteudo, Thickness? padding = null)
    {
        var p = new StackPanel();
        if (titulo != null)
        {
            var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
            topo.Children.Add(new TextBlock { Text = titulo, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Texto, VerticalAlignment = VerticalAlignment.Center });
            if (direita != null) { Grid.SetColumn(direita, 1); topo.Children.Add(direita); }
            p.Children.Add(topo);
        }
        p.Children.Add(conteudo);
        return new Border
        {
            Background = FundoCartao, CornerRadius = new CornerRadius(12), Padding = padding ?? new Thickness(18, 16),
            BorderBrush = Linha, BorderThickness = new Thickness(1.5), Child = p, BoxShadow = BoxShadows.Parse("0 4 12 0 #40000000"),
        };
    }

    // a linha de um interruptor: titulo e ajuda a esquerda, o interruptor a direita
    public static Control LinhaInterruptor(string titulo, string ajuda, ToggleSwitch t)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,Auto") };
        var txt = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        txt.Children.Add(new TextBlock { Text = titulo, FontSize = 14.5, FontWeight = FontWeight.Medium, Foreground = Texto, TextWrapping = TextWrapping.Wrap });
        if (ajuda.Length > 0) txt.Children.Add(Ajuda(ajuda));
        g.Children.Add(txt);
        Grid.SetColumn(t, 2);
        g.Children.Add(t);
        return g;
    }

    public static ToggleSwitch Interruptor(bool ligado, string? on = null, string? off = null) => new()
    {
        IsChecked = ligado, OnContent = on ?? T("Ligado"), OffContent = off ?? T("Desligado"), VerticalAlignment = VerticalAlignment.Center,
        Foreground = Texto, FontSize = 13, MinWidth = 0,
    };

    public static TextBox CaixaTexto(string texto, int maximo, string? dica = null) => new()
    {
        Text = texto, MaxLength = maximo, FontSize = 15, PlaceholderText = dica, Foreground = Texto,
        Background = TemaVerde.CartaoForte, BorderBrush = Linha, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7),
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    public static BotaoConfig Botao(string texto, bool principal, Action clique) => new(texto, principal, clique);

    // uma etiqueta pequena (estado, contagem)
    public static Border Etiqueta(string texto, IBrush cor, IBrush fundo) => new()
    {
        Background = fundo, CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = texto, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = cor },
    };

    // o logo de uma equipa (ou o circulo nas cores dela, como na jornada)
    public sealed class Escudo : Control
    {
        readonly Avalonia.Media.Imaging.Bitmap? _b;
        readonly Color _fundo, _letra;
        public Escudo(RetroFoot.Motor.Save.Equipa? e)
        {
            _b = e == null ? null : Escudos.Escudo(e);
            _fundo = e != null && e.CorFundo.Length >= 3 ? Color.FromRgb(e.CorFundo[0], e.CorFundo[1], e.CorFundo[2]) : Colors.White;
            _letra = e != null && e.CorLetra.Length >= 3 ? Color.FromRgb(e.CorLetra[0], e.CorLetra[1], e.CorLetra[2]) : Colors.Black;
        }
        // o logo que some no verde ganha o circulo claro por tras (Escudos.PrecisaDisco)
        public override void Render(DrawingContext dc)
        {
            var r = new Rect(Bounds.Size).Deflate(2);
            if (_b != null && Escudos.PrecisaDisco(_b))
            {
                double raio = Math.Min(Bounds.Width, Bounds.Height) / 2;
                dc.DrawEllipse(TemaVerde.DiscoLogo, null, new Rect(Bounds.Size).Center, raio, raio);
                r = r.Deflate(raio * 0.16);
            }
            TelaJornada.Logo(dc, _b, _fundo, _letra, r);
        }
    }

    // ---------------------------------------------------------------- dialogos
    // pergunta Sim / Nao (Dlg.Perguntar no Moderno)
    public static void Perguntar(Dialogos d, string titulo, string texto, Action sim, Action? nao = null)
    {
        if (Celular.Ativo) { ClubeCelular.Perguntar(d, titulo, texto, sim, nao); return; }
        CaixaModerna? c = null;
        c = new CaixaModerna(titulo, 420);
        c.Corpo.Children.Add(new TextBlock { Text = texto, FontSize = 15, Foreground = Texto, TextWrapping = TextWrapping.Wrap });
        c.Rodape((T("Não"), false, () => { d.Fechar(c!); nao?.Invoke(); }), (T("Sim"), true, () => { d.Fechar(c!); sim(); }));
        c.AoFechar = () => { d.Fechar(c!); nao?.Invoke(); };
        c.AoEnter = () => { d.Fechar(c!); sim(); };
        d.AbrirControle(c);
    }

    // um texto (Dlg.PedirTexto no Moderno)
    public static void PedirTexto(Dialogos d, string titulo, string rotulo, string inicial, int maximo, Action<string> ok)
    {
        if (Celular.Ativo) { ClubeCelular.PedirTexto(d, titulo, rotulo, inicial, maximo, ok, apara: true); return; }
        CaixaModerna? c = null;
        c = new CaixaModerna(titulo, 420);
        var caixa = CaixaTexto(inicial, maximo);
        c.Corpo.Children.Add(new TextBlock { Text = rotulo, FontSize = 14, Foreground = Texto });
        c.Corpo.Children.Add(caixa);
        void Ok()
        {
            var t = (caixa.Text ?? "").Trim();
            if (t.Length == 0) return;
            d.Fechar(c!);
            ok(t);
        }
        c.Rodape((T("Cancelar"), false, () => d.Fechar(c!)), (T("OK"), true, Ok));
        c.AoFechar = () => d.Fechar(c!);
        c.AoEnter = Ok;
        c.Foco = caixa;
        d.AbrirControle(c);
    }
}

// um botao arredondado (principal: a pilula ouro do Jogar do hub; senao o verde-escuro meio
// transparente com o fio dourado; desligado fica verde apagado, nao amarelo apagado)
sealed class BotaoConfig : Border
{
    readonly bool _principal;
    readonly TextBlock _t;
    bool _hab = true, _sobre;
    public Action Clique;

    public BotaoConfig(string texto, bool principal, Action clique)
    {
        _principal = principal;
        Clique = clique;
        _t = new TextBlock { Text = texto, FontSize = 14, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        Child = _t;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(18);
        Padding = new Thickness(20, 8);
        Cursor = new Cursor(StandardCursorType.Hand);
        PointerEntered += (_, _) => { _sobre = true; Pintar(); };
        PointerExited += (_, _) => { _sobre = false; Pintar(); };
        PointerPressed += (_, a) => { a.Handled = true; if (_hab) Clique(); };
        Pintar();
    }

    public string Texto { get => _t.Text ?? ""; set => _t.Text = value; }
    public bool Habilitado { get => _hab; set { _hab = value; Pintar(); } }

    void Pintar()
    {
        bool s = _sobre && _hab;
        Opacity = _hab || _principal ? 1 : 0.5;
        if (_principal && !_hab)
        {
            Background = TemaVerde.BotaoDesligado;
            BorderBrush = TemaVerde.Linha;
            _t.Foreground = TemaVerde.Apagado;
            BoxShadow = default;
            return;
        }
        Background = _principal ? (s ? TemaVerde.BotaoPrimarioHover : TemaVerde.BotaoPrimario) : (s ? TemaVerde.Hover : TemaVerde.BotaoSecundario);
        BorderBrush = _principal ? TemaVerde.DestaqueClaro : s ? TemaVerde.Ouro : ConfigEstilo.Linha;
        _t.Foreground = _principal ? ConfigEstilo.TextoEscuro : ConfigEstilo.Texto;
        BoxShadow = _principal ? BoxShadows.Parse("0 2 8 0 #50000000") : default;
    }
}

// uma escolha entre opcoes: em lista (titulo + descricao, com o circulo do radio) ou em
// fichas lado a lado (chips: so o titulo)
sealed class EscolhaModerna : Panel
{
    readonly List<Border> _itens = new();
    readonly List<Ellipse?> _marcas = new();
    readonly List<TextBlock> _titulos = new();
    readonly bool _chips;
    int _sel = -1;
    public event Action<int>? Mudou;

    public EscolhaModerna(IReadOnlyList<(string Titulo, string? Descricao)> opcoes, int sel, bool chips = false, bool lado = false, Func<int, Control>? figura = null)
    {
        _chips = chips;
        Panel painel = chips ? new WrapPanel { ItemSpacing = 8, LineSpacing = 8 }
            : lado ? new UniformGrid { Rows = 1, ColumnSpacing = 10 } : new StackPanel { Spacing = 8 };
        for (int i = 0; i < opcoes.Count; i++)
        {
            int k = i;
            var (titulo, desc) = opcoes[i];
            var t = new TextBlock { Text = titulo, FontSize = chips ? 14 : 14.5, FontWeight = FontWeight.Medium, Foreground = ConfigEstilo.Texto };
            Control filho;
            Ellipse? marca = null;
            if (chips) filho = t;
            else
            {
                marca = new Ellipse { Width = 10, Height = 10, Fill = ConfigEstilo.Azul, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var anel = new Border
                {
                    Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(2),
                    BorderBrush = ConfigEstilo.TextoFraco, Child = marca, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0),
                };
                var txt = new StackPanel { Spacing = 2 };
                txt.Children.Add(t);
                if (desc != null) txt.Children.Add(ConfigEstilo.Ajuda(desc));
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*") };
                g.Children.Add(anel);
                Grid.SetColumn(txt, 2);
                g.Children.Add(txt);
                filho = figura == null ? g : new StackPanel { Children = { figura(i), g } };
            }
            var b = new Border
            {
                Child = filho, CornerRadius = new CornerRadius(chips ? 16 : 10), BorderThickness = new Thickness(1),
                Padding = chips ? new Thickness(16, 6) : new Thickness(14, 10), Cursor = new Cursor(StandardCursorType.Hand),
                MinWidth = chips ? 52 : 0,
            };
            if (chips) t.HorizontalAlignment = HorizontalAlignment.Center;
            if (chips && desc != null) ToolTip.SetTip(b, desc);
            b.PointerPressed += (_, a) =>
            {
                a.Handled = true;
                if (!IsEffectivelyEnabled || k == _sel) return;
                Selecionado = k;
                Mudou?.Invoke(k);
            };
            _itens.Add(b); _marcas.Add(marca); _titulos.Add(t);
            painel.Children.Add(b);
        }
        Children.Add(painel);
        Selecionado = sel;
    }

    public int Quantas => _itens.Count;

    public int Selecionado
    {
        get => _sel;
        set
        {
            _sel = value;
            for (int i = 0; i < _itens.Count; i++)
            {
                bool s = i == _sel;
                // a ficha escolhida e a pilula amarela (como as abas); o cartao escolhido, o verde com o fio amarelo
                _itens[i].Background = _chips ? (s ? ConfigEstilo.Azul : TemaVerde.BotaoSecundario) : s ? ConfigEstilo.AzulFundo : TemaVerde.BotaoSecundario;
                _itens[i].BorderBrush = s ? ConfigEstilo.Azul : ConfigEstilo.Linha;
                if (_marcas[i] is { } m)
                {
                    m.IsVisible = s;
                    ((Border)m.Parent!).BorderBrush = s ? ConfigEstilo.Azul : ConfigEstilo.TextoFraco;
                }
                if (_chips) _titulos[i].Foreground = s ? ConfigEstilo.TextoEscuro : ConfigEstilo.Texto;
                if (_chips) _titulos[i].FontWeight = s ? FontWeight.Bold : FontWeight.Medium;
            }
        }
    }

    // setas: a anterior / a seguinte (quem tem o foco chama)
    public void Mover(int d)
    {
        int n = Math.Clamp(_sel + d, 0, _itens.Count - 1);
        if (n == _sel || !IsEffectivelyEnabled) return;
        Selecionado = n;
        Mudou?.Invoke(n);
    }
}

// a caixa de um dialogo Moderno (no quadro de 800x600 da Sessao): titulo, corpo e os botoes
// no rodape; Enter = AoEnter, Esc = AoFechar
sealed class CaixaModerna : UserControl
{
    public readonly StackPanel Corpo = new() { Spacing = 12 };
    readonly StackPanel _botoes = new() { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
    readonly Grid _rodape;
    public readonly TextBlock Subtitulo;
    public Action? AoEnter, AoFechar;
    public Control? Foco;

    public CaixaModerna(string titulo, double largura, Control? icone = null, bool rolar = false)
    {
        Width = largura;
        MaxHeight = 580;
        Focusable = true;
        FocusAdorner = null;
        FontFamily = ConfigEstilo.Inter;
        var cab = new StackPanel { Spacing = 2 };
        cab.Children.Add(ConfigEstilo.Titulo(titulo, 19));
        Subtitulo = ConfigEstilo.Ajuda("", 13);
        Subtitulo.IsVisible = false;
        cab.Children.Add(Subtitulo);
        var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(22, 18, 22, 6) };
        if (icone != null) { icone.Margin = new Thickness(0, 0, 14, 0); topo.Children.Add(icone); }
        Grid.SetColumn(cab, 1);
        topo.Children.Add(cab);
        _rodape = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(22, 12, 22, 16) };
        Grid.SetColumn(_botoes, 1);
        _rodape.Children.Add(_botoes);
        Control meio = new Border { Padding = new Thickness(22, 8, 22, 4), Child = Corpo };
        if (rolar) meio = new ScrollViewer { Content = meio, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var corpo = new DockPanel();
        DockPanel.SetDock(topo, Dock.Top);
        corpo.Children.Add(topo);
        DockPanel.SetDock(_rodape, Dock.Bottom);
        corpo.Children.Add(_rodape);
        corpo.Children.Add(meio);
        Content = new Border
        {
            // como a caixa "Sair do RetroFoot?" do hub: o verde quase opaco com o fio amarelo
            Background = TemaVerde.DialogoHub, CornerRadius = new CornerRadius(16), ClipToBounds = true,
            BorderBrush = TemaVerde.Destaque, BorderThickness = new Thickness(1.5),
            BoxShadow = BoxShadows.Parse("0 12 36 0 #A0000000"),
            Child = ConfigEstilo.Escuro(corpo),
        };
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(Focar, DispatcherPriority.Loaded);
    }

    public void Focar()
    {
        if (Foco is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } f && f.Focus()) return;
        Focus();
    }

    public void Sub(string texto) { Subtitulo.Text = texto; Subtitulo.IsVisible = texto.Length > 0; }

    // os botoes do rodape, da esquerda para a direita (o ultimo e o principal)
    public BotaoConfig[] Rodape(params (string Texto, bool Principal, Action Clique)[] botoes)
    {
        var r = new BotaoConfig[botoes.Length];
        for (int i = 0; i < botoes.Length; i++)
            _botoes.Children.Add(r[i] = ConfigEstilo.Botao(botoes[i].Texto, botoes[i].Principal, botoes[i].Clique));
        return r;
    }

    // um controle a esquerda no rodape (um botao a mais, um aviso)
    public void RodapeEsquerda(Control c)
    {
        c.VerticalAlignment = VerticalAlignment.Center;
        c.HorizontalAlignment = HorizontalAlignment.Left;
        _rodape.Children.Add(c);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (e.Key == Key.Enter && AoEnter != null) { AoEnter(); e.Handled = true; }
        else if (e.Key == Key.Escape && AoFechar != null) { AoFechar(); e.Handled = true; }
    }
}

// uma linha de abas (segmentos) para as paginas de um dialogo Moderno
sealed class AbasModernas : Border
{
    readonly StackPanel _p = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    readonly List<(Border Aba, TextBlock T, Control Pagina)> _abas = new();
    int _atual;
    public event Action<int>? Mudou;

    public AbasModernas()
    {
        Child = _p;
        Background = ConfigEstilo.FundoBarra;
        BorderBrush = ConfigEstilo.Linha;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(20);
        Padding = new Thickness(4);
        HorizontalAlignment = HorizontalAlignment.Left;
    }

    public void Pagina(string titulo, Control pagina)
    {
        int k = _abas.Count;
        var t = new TextBlock { Text = titulo, FontSize = 13.5, FontWeight = FontWeight.SemiBold };
        var b = new Border { Child = t, CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 6), Cursor = new Cursor(StandardCursorType.Hand) };
        b.PointerPressed += (_, a) => { a.Handled = true; Atual = k; };
        _abas.Add((b, t, pagina));
        _p.Children.Add(b);
        Atual = _atual;
    }

    public int Quantas => _abas.Count;

    public int Atual
    {
        get => _atual;
        set
        {
            _atual = Math.Clamp(value, 0, Math.Max(0, _abas.Count - 1));
            for (int i = 0; i < _abas.Count; i++)
            {
                bool s = i == _atual;
                // a escolhida: a pilula amarela (como as abas da equipa)
                _abas[i].Aba.Background = s ? ConfigEstilo.Azul : Brushes.Transparent;
                _abas[i].T.Foreground = s ? ConfigEstilo.TextoEscuro : ConfigEstilo.TextoSuave;
                _abas[i].Pagina.IsVisible = s;
            }
            Mudou?.Invoke(_atual);
        }
    }
}
