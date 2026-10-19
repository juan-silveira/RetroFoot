using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using System.Collections.Generic;
using System.IO;

namespace RetroFoot.App;

// As pecas das paginas de ferramenta (Scout, Arbitros): cartoes arredondados e a lista com
// zebra, nas cores do visual (Tema: o verde do launcher no Classico, o escuro no Moderno).
sealed class EstiloFerramenta
{
    public readonly IBrush Painel = Tema.FundoCartao, PainelBorda = Tema.Linha, Rotulo = Tema.TextoFraco, Texto = Tema.Texto,
        Fraco = Tema.TextoFraco, TabelaFundo = Tema.Fundo, Zebra = Tema.FundoZebra, TabelaTexto = Tema.Texto, CabFundo = Tema.FundoBarra,
        CabTexto = Tema.Amarelo, Sel = Tema.AzulFundo, SelHover = Tema.FundoHover, Ouro = Tema.Ouro, Bom = Tema.Verde, Mau = Tema.Erro,
        CampoFundo = Tema.Campo, CampoTexto = Tema.CampoTexto;
    public readonly FontFamily Fonte = Tema.Inter;

    // a pagina inteira: o tema escuro do Fluent (combos, caixas, barras)
    public Control Raiz(Control c) => Ui.Pagina(c);

    public Border Cartao(Control c, Thickness? padding = null) =>
        new() { Background = Painel, BorderBrush = PainelBorda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = padding ?? new Thickness(10), Child = c };

    // a moldura da lista: um cartao com borda
    public Border Moldura(Control c) =>
        new() { Background = TabelaFundo, BorderBrush = PainelBorda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = c };

    public TextBlock Txt(string s, double tam = 12.5, IBrush? cor = null, bool negrito = false) => new()
    {
        Text = s, FontFamily = Fonte, FontSize = tam, Foreground = cor ?? Texto, FontWeight = negrito ? FontWeight.Bold : FontWeight.Normal,
        TextWrapping = TextWrapping.Wrap,
    };

    public TextBlock RotuloCampo(string s) => new()
    {
        Text = s, FontFamily = Fonte, FontSize = 12, Foreground = Rotulo,
    };

    public Control Campo(string rotulo, Control editor) => new StackPanel { Spacing = 2, Children = { RotuloCampo(rotulo), editor } };

    public ComboBox Combo()
    {
        var c = Ui.Combo();
        c.HorizontalAlignment = HorizontalAlignment.Stretch;
        c.FontSize = 12.5;
        return c;
    }

    public TextBox Caixa(string? texto = null)
    {
        var t = Ui.CaixaTexto(texto);
        t.FontSize = 12.5;
        return t;
    }

    public CheckBox Marcar(string texto) => new()
    {
        Content = new TextBlock { Text = texto, FontFamily = Fonte, FontSize = 12, Foreground = Texto, TextWrapping = TextWrapping.Wrap },
        MinHeight = 24, Padding = new Thickness(4, 0, 0, 0),
    };

    // a lista: itens sem folga do Fluent, a selecao e o hover nas cores do visual
    public ListBox Lista()
    {
        var l = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontFamily = Fonte, Foreground = TabelaTexto, Padding = new Thickness(0) };
        l.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(0)), new Setter(Layoutable.MinHeightProperty, 0d) },
        });
        // a zebra (linhas pares) antes da selecao e do hover, que ficam por cima
        l.Styles.Add(new Style(x => x.OfType<ListBoxItem>().NthChild(2, 0).Template().OfType<ContentPresenter>())
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Zebra) },
        });
        foreach (var (classe, cor) in new[] { (":pointerover", SelHover), (":selected", Sel), (":selected:pointerover", Sel), (":selected:focus", Sel), (":pressed", Sel) })
        {
            var pcs = classe.Split(':', StringSplitOptions.RemoveEmptyEntries);
            l.Styles.Add(new Style(x =>
            {
                var s0 = x.OfType<ListBoxItem>();
                foreach (var pc in pcs) s0 = s0.Class(":" + pc);
                return s0.Template().OfType<ContentPresenter>();
            })
            {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, cor) },
            });
        }
        return l;
    }

    // as bandeiras: as redondas do app
    readonly Dictionary<string, Bitmap?> _bandeiras = new();

    public Bitmap? Bandeira(string pais)
    {
        if (string.IsNullOrEmpty(pais)) return null;
        if (_bandeiras.TryGetValue(pais, out var b)) return b;
        b = null;
        try
        {
            b = new Bitmap(AssetLoader.Open(new Uri($"avares://RetroFoot.App/Assets/Bandeiras/{pais}.png")));
        }
        catch { b = null; }
        _bandeiras[pais] = b;
        return b;
    }

    public Control ImagemBandeira(string pais, double alto = 13)
    {
        var b = Bandeira(pais);
        if (b == null) return new Border { Width = alto, Height = alto };
        var img = new Image { Source = b, Width = alto, Height = alto, Stretch = Stretch.UniformToFill };
        return new Border { Width = alto, Height = alto, CornerRadius = new CornerRadius(alto / 2), ClipToBounds = true, Child = img };
    }

    // as cores das posicoes (G, D, M, A), como no Scout do launcher
    public static readonly IBrush[] CoresPosicao = { Ui.Hex("#E0B000"), Ui.Hex("#3D7BD9"), Ui.Hex("#2E9E4F"), Ui.Hex("#D9443D") };
}
