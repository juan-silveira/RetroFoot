using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace RetroFoot.App;

// As pecas do hub e das ferramentas. Os dois visuais (Visual.cs) tem o mesmo desenho (o cartao
// grande Jogar e os cartoes do hub, as ferramentas na pagina inteira com a barra de cima e
// Voltar, cartoes arredondados, tabelas com zebra); so o tema muda (Tema, abaixo):
//   Classico: o verde e amarelo da marca do RetroFoot (UiClassico.cs: o fundo verde, os
//             cartoes verde-escuros, titulos amarelos, botoes amarelos e claros, campos brancos);
//   Moderno:  o verde e amarelo do hub (TemaVerde: o quadro verde, cartoes meio transparentes
//             com o fio dourado, botoes em pilula ouro; ConfigEstilo, UiModerno.cs).
// Os icones sao os Lucide (UiModerno.Icones) nos dois.
//   Ui.Janela(titulo, conteudo, fechar, icone)  uma janelinha (dialogo) por cima da pagina
//   Ui.Veu(janela, fechar)                      a janelinha no centro, sobre um veu escuro
//   Ui.Caixa(titulo, texto, ok)                 uma mensagem com OK
//   Ui.Cartao / Afundado / Botao / BotaoIcone / Titulo / Txt / Icone / Lista / CaixaTexto / Campo
//   Ui.CorTexto / CorFraca / CorRotulo / CorDestaque / CorErro / CorBom: o texto na pagina
public static class Ui
{
    static bool M => Visual.Moderno;

    public static IBrush Hex(string s) => new SolidColorBrush(Color.Parse(s));

    // ---------------------------------------------------------------- cores do texto na pagina
    public static IBrush Fundo => Tema.Fundo;
    public static IBrush CorTexto => Tema.Texto;
    public static IBrush CorFraca => Tema.TextoFraco;
    public static IBrush CorRotulo => Tema.TextoFraco;
    public static IBrush CorDestaque => Tema.Amarelo;
    public static IBrush CorErro => Tema.Erro;
    public static IBrush CorBom => Tema.Verde;
    public static IBrush CorAviso => Tema.Amarelo;
    public static FontFamily Fonte => Tema.Inter;

    // ---------------------------------------------------------------- pecas
    public static Control Icone(string nome, double tam, IBrush cor) => UiModerno.Icone(nome, tam, cor);

    public static TextBlock Titulo(string texto, double tamanho = 20) => new()
    {
        Text = texto, FontFamily = Fonte, FontSize = tamanho, FontWeight = M ? FontWeight.SemiBold : FontWeight.Bold, Foreground = Tema.CorTitulo,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    public static TextBlock Txt(string texto, double tamanho = 12.5, IBrush? cor = null, bool negrito = false) => new()
    {
        Text = texto, FontFamily = Fonte, FontSize = tamanho, Foreground = cor ?? CorTexto, TextWrapping = TextWrapping.Wrap,
        FontWeight = negrito ? (M ? FontWeight.SemiBold : FontWeight.Bold) : FontWeight.Normal,
    };

    public static Border Cartao(Control conteudo, Thickness? padding = null) => new()
    {
        Background = Tema.FundoCartao, BorderBrush = Tema.Linha, BorderThickness = new Thickness(M ? 1.5 : 1), CornerRadius = new CornerRadius(12),
        Padding = padding ?? new Thickness(14, 12), Child = conteudo, BoxShadow = M ? BoxShadows.Parse("0 4 12 0 #40000000") : default,
    };

    public static Border Afundado(Control c) => new()
    {
        Background = Tema.Fundo2, BorderBrush = Tema.Linha, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
        Padding = new Thickness(2), Child = c, ClipToBounds = true,
    };

    // o botao do visual: Classico = os amarelos e claros da marca; Moderno = azul e cinzento
    public static Clicavel Botao(string texto, Action clique, bool principal = false, string? icone = null) =>
        M ? UiModerno.Botao(texto, clique, principal, icone) : UiClassico.Botao(texto, clique, principal, icone);

    // um botao redondo so com o icone (fechar, voltar)
    public static Clicavel BotaoIcone(string icone, Action clique, string nome, double tam = 32)
    {
        var b = new Clicavel
        {
            Width = tam, Height = tam, CornerRadius = new CornerRadius(tam / 2), Background = Brushes.Transparent, Hover = Tema.FundoHover,
            Child = Icone(icone, tam * 0.55, Tema.Texto), BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, CorFoco = Tema.Azul,
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.Clique += clique;
        ToolTip.SetTip(b, nome);
        Nome(b, nome);
        return b;
    }

    public static ListBox Lista()
    {
        var l = new ListBox
        {
            Background = Brushes.Transparent, Foreground = Tema.Texto, FontFamily = Fonte, FontSize = 13, BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
        };
        foreach (var (estado, cor) in new[] { (":pointerover", Tema.FundoHover), (":selected", Tema.AzulFundo), (":selected:pointerover", Tema.AzulFundo), (":selected:focus", Tema.AzulFundo) })
        {
            var pcs = estado.Split(':', StringSplitOptions.RemoveEmptyEntries);
            l.Styles.Add(new Style(x =>
            {
                var s0 = x.OfType<ListBoxItem>();
                foreach (var pc in pcs) s0 = s0.Class(":" + pc);
                return s0.Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
            })
            {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, cor), new Setter(ContentPresenter.ForegroundProperty, Tema.Texto) },
            });
        }
        return l;
    }

    public static TextBox CaixaTexto(string? texto = null) => new()
    {
        Text = texto, FontFamily = Fonte, FontSize = 13, Background = Tema.Campo, Foreground = Tema.CampoTexto, BorderBrush = Tema.Linha,
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), MinHeight = 30, Padding = new Thickness(9, 4),
        VerticalContentAlignment = VerticalAlignment.Center, CaretBrush = Tema.CampoTexto,
    };

    public static ComboBox Combo() => new()
    {
        FontFamily = Fonte, FontSize = 13, Background = Tema.Campo, Foreground = Tema.CampoTexto, BorderBrush = Tema.Linha,
        CornerRadius = new CornerRadius(8), MinHeight = 30, Padding = new Thickness(9, 3),
    };

    // rotulo a esquerda, o editor a direita
    public static Control Campo(string rotulo, Control editor, double larguraRotulo = 140)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{larguraRotulo},*") };
        var r = Txt(rotulo, 13, CorRotulo);
        r.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(r);
        Grid.SetColumn(editor, 1);
        g.Children.Add(editor);
        return g;
    }

    // a pagina inteira de uma ferramenta: o tema escuro do Fluent, a fonte e os campos (caixas,
    // combos) nas cores do visual
    public static Control Pagina(Control c)
    {
        var s = (ThemeVariantScope)ConfigEstilo.Escuro(c);
        s.SetValue(Avalonia.Controls.Documents.TextElement.FontFamilyProperty, Fonte);
        s.SetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty, Tema.Texto);
        foreach (var t in new[] { typeof(TextBox), typeof(ComboBox), typeof(ComboBoxItem), typeof(CheckBox), typeof(ListBoxItem) })
            s.Styles.Add(new Style(x => x.OfType(t)) { Setters = { new Setter(Avalonia.Controls.Primitives.TemplatedControl.FontFamilyProperty, Fonte) } });
        foreach (var estado in new[] { ":pointerover", ":focus", ":focus-within" })
        {
            s.Styles.Add(new Style(x => x.OfType<TextBox>().Class(estado).Template().OfType<Border>().Name("PART_BorderElement"))
            {
                Setters = { new Setter(Border.BackgroundProperty, Tema.Campo), new Setter(Border.BorderBrushProperty, estado == ":pointerover" ? Tema.HoverForte : Tema.Foco) },
            });
            s.Styles.Add(new Style(x => x.OfType<ComboBox>().Class(estado).Template().OfType<Border>().Name("Background"))
            {
                Setters = { new Setter(Border.BackgroundProperty, estado == ":pointerover" ? Tema.CampoHover : Tema.Campo), new Setter(Border.BorderBrushProperty, estado == ":pointerover" ? Tema.HoverForte : Tema.Foco) },
            });
        }
        if (!M) UiClassico.Campos(s);
        return s;
    }

    // ---------------------------------------------------------------- janelinhas
    // a janelinha: o icone, o titulo e o "x" em cima; o conteudo embaixo
    public static Border Janela(string titulo, Control conteudo, Action? fechar = null, string? icone = null)
    {
        var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10, Margin = new Thickness(16, 12, 10, 0) };
        if (icone != null)
            topo.Children.Add(new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(M ? 15 : 8), Background = M ? TemaVerde.CirculoIcone : Tema.AzulFundo, Child = Icone(icone, 17, Tema.Azul),
            });
        var t = Titulo(titulo, 16);
        t.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(t, 1);
        topo.Children.Add(t);
        if (fechar != null)
        {
            var x = BotaoIcone("fechar", fechar, Idioma.T("Fechar"));
            Grid.SetColumn(x, 2);
            topo.Children.Add(x);
        }
        var d = new DockPanel();
        DockPanel.SetDock(topo, Dock.Top);
        d.Children.Add(topo);
        d.Children.Add(conteudo);
        return new Border
        {
            Background = Tema.FundoDialogo, BorderBrush = M ? TemaVerde.Destaque : Tema.Linha, BorderThickness = new Thickness(M ? 1.5 : 1),
            CornerRadius = new CornerRadius(M ? 16 : 14), BoxShadow = BoxShadows.Parse("0 12 36 0 #A0000000"), Child = d, ClipToBounds = true,
        };
    }

    // a janelinha no centro, sobre o veu; Esc fecha
    public static Panel Veu(Control janela, Action? fechar = null)
    {
        janela.HorizontalAlignment = HorizontalAlignment.Center;
        janela.VerticalAlignment = VerticalAlignment.Center;
        var veu = new Panel { Background = M ? TemaVerde.Veu : Hex("#99000000"), Children = { Pagina(janela) } };
        if (fechar != null) veu.KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; fechar(); } };
        return veu;
    }

    // uma mensagem com OK
    public static Control Caixa(string titulo, string texto, Action ok)
    {
        var corpo = new StackPanel { Spacing = 14, Margin = new Thickness(18, 14), Width = 380 };
        corpo.Children.Add(Txt(texto, 13.5));
        var b = Botao(Idioma.T("OK"), ok, true);
        b.HorizontalAlignment = HorizontalAlignment.Right;
        b.MinWidth = 80;
        corpo.Children.Add(b);
        var veu = Veu(Janela(titulo, corpo, ok, "info"));
        veu.Loaded += (_, _) => b.Focus();
        veu.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Escape) { e.Handled = true; ok(); } };
        return veu;
    }

    // ---------------------------------------------------------------- tabelas
    // o cabecalho de uma tabela com as mesmas colunas das linhas: quando a ScrollViewer das
    // linhas rola, a folga da barra (App.FolgaBarra) tira largura das linhas, entao o
    // cabecalho perde a mesma largura a direita (rolagem: a ScrollViewer ou uma lista)
    public static void AlinharCabecalho(Control cabecalho, Control rolagem)
    {
        var base0 = cabecalho.Margin;
        ScrollViewer? sv = null;
        void Acertar()
        {
            sv ??= rolagem as ScrollViewer ?? rolagem.FindDescendantOfType<ScrollViewer>();
            if (sv == null || (sv.Content as Control ?? sv.Presenter?.Child) is not { } conteudo) return;
            var p = conteudo.TranslatePoint(new Point(conteudo.Bounds.Width, 0), sv);
            if (p == null) return;
            double tira = Math.Max(0, Math.Round(sv.Bounds.Width - p.Value.X));
            var m = new Thickness(base0.Left, base0.Top, base0.Right + tira, base0.Bottom);
            if (cabecalho.Margin != m) cabecalho.Margin = m;
        }
        rolagem.LayoutUpdated += (_, _) => Acertar();
    }

    internal static void Nome(Control c, string nome) => Avalonia.Automation.AutomationProperties.SetName(c, nome);
}

// As cores do hub e das ferramentas no visual escolhido (os nomes sao os do ConfigEstilo):
// Moderno = o ConfigEstilo e o TemaVerde (o vidro do hub); Classico = o verde e amarelo da marca (UiClassico)
public static class Tema
{
    static bool M => Visual.Moderno;
    static IBrush E(IBrush moderno, IBrush classico) => M ? moderno : classico;

    public static IBrush Fundo => E(ConfigEstilo.Fundo, UiClassico.Verde);
    // o fundo da pagina inteira (a ferramenta): no Moderno o quadro verde do hub (degrade)
    public static IBrush FundoPagina => E(TemaVerde.FundoTela, UiClassico.Verde);
    // o fundo das janelinhas (dialogos): no Moderno o verde quase opaco da caixa do hub
    public static IBrush FundoDialogo => E(TemaVerde.DialogoHub, UiClassico.Cartao);
    public static IBrush Fundo2 => E(UiModerno.Fundo2, UiClassico.VerdeFundo);
    public static IBrush FundoCartao => E(ConfigEstilo.FundoCartao, UiClassico.Cartao);
    public static IBrush FundoHover => E(ConfigEstilo.FundoHover, UiClassico.Hover);
    public static IBrush HoverForte => E(UiModerno.HoverForte, UiClassico.HoverForte);
    public static IBrush FundoBarra => E(ConfigEstilo.FundoBarra, UiClassico.VerdeTopo);
    public static IBrush FundoZebra => E(ConfigEstilo.FundoZebra, UiClassico.LinhaPar);
    public static IBrush Linha => E(ConfigEstilo.Linha, UiClassico.Borda);
    public static IBrush Texto => E(ConfigEstilo.Texto, UiClassico.Branco);
    public static IBrush TextoFraco => E(ConfigEstilo.TextoFraco, UiClassico.Cinza);
    public static IBrush CorTitulo => E(ConfigEstilo.Texto, UiClassico.Amarelo);
    public static IBrush CorCabecalho => E(TemaVerde.Destaque, UiClassico.Amarelo);
    // o destaque (botao principal, foco, aba escolhida, icones) e o fundo da selecao
    public static IBrush Azul => E(ConfigEstilo.Azul, UiClassico.Amarelo);
    public static IBrush AzulHover => E(UiModerno.AzulHover, UiClassico.AmareloHover);
    public static IBrush AzulFundo => E(ConfigEstilo.AzulFundo, UiClassico.Selecao);
    public static IBrush TextoEscuro => E(ConfigEstilo.TextoEscuro, UiClassico.Preto);
    public static IBrush Erro => E(ConfigEstilo.Erro, UiClassico.Alerta);
    public static IBrush Verde => E(ConfigEstilo.Verde, UiClassico.Ok);
    public static IBrush Amarelo => E(ConfigEstilo.Amarelo, UiClassico.Amarelo);
    public static IBrush Ouro => E(ConfigEstilo.Amarelo, UiClassico.Ouro);
    // os campos (caixas de texto, combos): escuros no Moderno, brancos no Classico
    public static IBrush Campo => E(UiModerno.Campo, UiClassico.Branco);
    public static IBrush CampoHover => E(UiModerno.HoverForte, UiClassico.Branco);
    public static IBrush CampoTexto => E(ConfigEstilo.Texto, UiClassico.Preto);
    public static IBrush Foco => E(ConfigEstilo.Azul, UiClassico.Selecao);
    public static FontFamily Inter => ConfigEstilo.Inter;
}

// um Border que se clica (rato, toque, Enter/Espaco com o foco): cartoes, botoes e linhas.
// Hover = o fundo com o rato em cima; Solto/Premido = a sombra solta e premida (se houver);
// CorFoco = a borda com o foco do teclado
public class Clicavel : Border
{
    public event Action? Clique;
    public IBrush? Hover { get; set; }
    public BoxShadows? Solto { get; set; }
    public BoxShadows? Premido { get; set; }
    public IBrush? CorFoco { get; set; }
    public double EspessuraFoco { get; set; } = 1;
    IBrush? _fundo;
    bool _premido;

    public Clicavel()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        FocusAdorner = null;
        CorFoco = Tema.Azul;
    }

    public void Clicar() => Clique?.Invoke();

    // desligado fica apagado (e nao responde: IsEnabled)
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEffectivelyEnabledProperty) Opacity = IsEffectivelyEnabled ? 1 : 0.45;
    }

    // o fundo normal (quando muda por fora com o rato em cima, o hover volta para este)
    public void TrocarFundo(IBrush? fundo)
    {
        if (_fundo != null) _fundo = fundo;
        Background = IsPointerOver && Hover != null ? Hover : fundo;
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (Hover != null && IsEffectivelyEnabled) { _fundo ??= Background; Background = Hover; }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_fundo != null) { Background = _fundo; _fundo = null; }
        Soltar();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _premido = true;
        if (Premido is { } p) BoxShadow = p;
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        bool foi = _premido && new Rect(Bounds.Size).Contains(e.GetPosition(this));
        Soltar();
        if (foi) { e.Handled = true; Clique?.Invoke(); }
    }

    void Soltar()
    {
        _premido = false;
        if (Solto is { } s) BoxShadow = s;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key is not (Key.Enter or Key.Space)) return;
        e.Handled = true;
        Clique?.Invoke();
    }

    // o foco do teclado: a borda na cor do foco (sem mudar o tamanho: a espessura fica)
    IBrush? _borda;
    bool _comFoco;

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (CorFoco == null || _comFoco) return;
        _comFoco = true;
        _borda = BorderBrush;
        if (BorderThickness == default) BorderThickness = new Thickness(EspessuraFoco);
        BorderBrush = CorFoco;
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        if (!_comFoco) return;
        _comFoco = false;
        BorderBrush = _borda ?? Brushes.Transparent;
    }
}
