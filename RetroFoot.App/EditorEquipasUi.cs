using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// As pecas do Editor de Equipas: cartoes arredondados, listas com borda, a selecao e as abas
// sublinhadas, nas cores do visual (Tema: o verde do launcher no Classico, o escuro no Moderno).
sealed class TemaEditor
{
    public readonly IBrush Painel = Tema.FundoCartao, Borda = Tema.Linha, Texto = Tema.Texto, Fraco = Tema.TextoFraco, Lista = Tema.Fundo,
        ListaTexto = Tema.Texto, Selecao = Tema.AzulFundo, SelecaoTexto = Tema.Texto, Zebra = Tema.FundoZebra, Erro = Tema.Erro, Ok = Tema.Verde,
        Destaque = Tema.Amarelo, Rotulo = Tema.TextoFraco, Hover = Tema.FundoHover;
    public readonly FontFamily Fonte = Tema.Inter;

    // o tema do Fluent (caixas de texto, listas, barras de rolagem) e os campos do visual
    public Control Escopo(Control c) => Ui.Pagina(c);

    public TextBlock T(string s, double tam = 12.5, IBrush? cor = null, bool negrito = false) => new()
    {
        Text = s, FontFamily = Fonte, FontSize = tam, Foreground = cor ?? Texto, TextWrapping = TextWrapping.Wrap,
        FontWeight = negrito ? FontWeight.Bold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center,
    };

    public TextBlock Linha1(string s, double tam = 12.5, IBrush? cor = null, bool negrito = false)
    {
        var t = T(s, tam, cor, negrito);
        t.TextWrapping = TextWrapping.NoWrap;
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        return t;
    }

    // um painel com titulo (um cartao)
    public Border Caixa(Control conteudo, string? titulo = null, Thickness? padding = null)
    {
        Control c = conteudo;
        if (titulo != null)
        {
            var d = new DockPanel();
            var t = Linha1(titulo, 13, Tema.CorTitulo, true);
            t.Margin = new Thickness(0, 0, 0, 6);
            DockPanel.SetDock(t, Dock.Top);
            d.Children.Add(t);
            d.Children.Add(conteudo);
            c = d;
        }
        return new Border { Background = Painel, BorderBrush = Borda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = padding ?? new Thickness(10, 8), Child = c };
    }

    // a moldura das listas: o fundo da pagina com borda
    public Border Funda(Control c) =>
        new() { Background = Lista, BorderBrush = Borda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(2), Child = c, ClipToBounds = true };

    public ListBox Lista0() => new()
    {
        Background = Brushes.Transparent, Foreground = ListaTexto, FontFamily = Fonte, FontSize = 12.5, BorderThickness = new Thickness(0),
        Padding = new Thickness(0),
    };

    public TextBox Entrada(string? texto = null, int max = 0)
    {
        var t = new TextBox
        {
            Text = texto, FontFamily = Fonte, FontSize = 12.5, MinHeight = 26, Padding = new Thickness(6, 3),
            CornerRadius = new CornerRadius(8), VerticalContentAlignment = VerticalAlignment.Center,
            Background = Tema.Campo, Foreground = Tema.CampoTexto, BorderBrush = Tema.Linha, CaretBrush = Tema.CampoTexto,
        };
        if (max > 0) t.MaxLength = max;
        return t;
    }

    public ComboBox Escolha()
    {
        var c = Ui.Combo();
        c.FontSize = 12.5; c.MinHeight = 26; c.HorizontalAlignment = HorizontalAlignment.Stretch; c.Padding = new Thickness(6, 2);
        return c;
    }

    // o botao do visual (Ui.Botao), mais baixo
    public Clicavel Botao(string texto, Action clique, bool principal = false, string? icone = null)
    {
        var b = Ui.Botao(texto, clique, principal, icone);
        b.MinWidth = 0;
        b.MinHeight = 26;
        b.Padding = new Thickness(11, 3);
        return b;
    }

    public static void Ativo(Control c, bool ativo)
    {
        c.IsEnabled = ativo;
        c.Opacity = ativo ? 1 : 0.45;
    }

    // abas (Equipa, Jogadores, Escudo): a escolhida sublinhada na cor do destaque
    public Control Abas(IReadOnlyList<string> nomes, int sel, Action<int> escolher, List<TextBlock> textos)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        textos.Clear();
        for (int i = 0; i < nomes.Count; i++)
        {
            int k = i;
            bool s = i == sel;
            var t = Linha1(nomes[i], 12.5, s ? Tema.Azul : Tema.TextoFraco, s);
            textos.Add(t);
            var b = new Clicavel
            {
                Child = t, Padding = new Thickness(12, 4), Focusable = true,
                Background = Brushes.Transparent, BorderBrush = s ? Tema.Azul : Brushes.Transparent, BorderThickness = new Thickness(0, 0, 0, 2),
            };
            b.Clique += () => escolher(k);
            p.Children.Add(b);
        }
        return p;
    }

    // ---------------------------------------------------------------- janelinhas
    // uma janelinha por cima da pagina (Ui.Janela sobre o veu)
    public static Control Modal(string titulo, Control corpo, Action fechar, string icone = "escudo", double largura = 420)
    {
        var j = Ui.Janela(titulo, corpo, fechar, icone);
        j.Width = largura;
        return Ui.Veu(j, fechar);
    }

    // o corpo das janelinhas
    public Control Corpo(Control c) => Escopo(new Border { Padding = new Thickness(14, 10, 14, 12), Child = c });

    public Control Botoes(params Control[] bs)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var b in bs) p.Children.Add(b);
        return p;
    }

    // ---------------------------------------------------------------- cores
    public static Color Cor(uint c) => Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c);
    public static IBrush Pincel(uint c) => new ImmutableSolidColorBrush(Cor(c));

    // as 16 cores do Windows (as do Editor de Equipas do original)
    public static readonly uint[] Paleta =
    {
        0x000000, 0x800000, 0x008000, 0x808000, 0x000080, 0x800080, 0x008080, 0x808080,
        0xC0C0C0, 0xFF0000, 0x00FF00, 0xFFFF00, 0x0000FF, 0xFF00FF, 0x00FFFF, 0xFFFFFF,
    };

    // o escudo desenhado nas cores da equipa (quando nao ha logo), como o das telas modernas
    public static Control EscudoCores(uint letra, uint fundo, double tam)
    {
        var c = new Canvas { Width = 18, Height = 20 };
        c.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M0.2,0.2 L17.8,0.2 L17.8,9 C17.8,14.5 13.5,18.3 9,19.9 C4.5,18.3 0.2,14.5 0.2,9 Z"),
            Fill = Pincel(fundo), Stroke = Ui.Hex("#80868b"), StrokeThickness = 0.6,
        });
        c.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M0.6,6 L17.4,6 L17.4,9 L0.6,9 Z"), Fill = Pincel(letra) });
        return new Viewbox { Width = tam, Height = tam, Child = c, Stretch = Stretch.Uniform };
    }

    // o fundo xadrez de quem mostra transparencia
    public static IBrush Xadrez { get; } = new VisualBrush
    {
        TileMode = TileMode.Tile, SourceRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute),
        DestinationRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute),
        Visual = new Canvas
        {
            Width = 16, Height = 16, Background = Ui.Hex("#FFFFFF"),
            Children =
            {
                new Border { Width = 8, Height = 8, Background = Ui.Hex("#D8D8D8") },
                new Border { Width = 8, Height = 8, Background = Ui.Hex("#D8D8D8"), [Canvas.LeftProperty] = 8.0, [Canvas.TopProperty] = 8.0 },
            },
        },
    };
}
