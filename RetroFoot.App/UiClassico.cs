using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace RetroFoot.App;

// O hub e as ferramentas no visual Classico: o verde e amarelo da marca do RetroFoot
// (Assets/Marca: o verde-escuro da bola #0D5A2E, o verde do TURBO #168C40, o amarelo da bola
// #F8D734, o ouro do FOOT #DDA917): o fundo verde, a barra de cima mais escura com o titulo
// amarelo, os cartoes verde-escuros arredondados, a zebra das tabelas, o botao amarelo (o
// principal) e o claro, os campos brancos com a letra preta, a selecao no verde do TURBO. O
// desenho e o mesmo do Moderno (Ui.cs); aqui so as cores (Tema) e o que muda no jeito.
// (as telas do jogo no Classico continuam com as cores do jogo classico)
public static class UiClassico
{
    static IBrush C(int rgb) => new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    // a paleta da marca: Fundo (o hub), Verde (as ferramentas), VerdeTopo (a barra), Cartao,
    // LinhaPar (a zebra), Amarelo, Cinza (rotulos), Alerta, Ouro, Ok
    public static readonly IBrush Fundo = C(0x0D5A2E), Verde = C(0x0A3F21), VerdeTopo = C(0x062A15), Cartao = C(0x0F4D29),
        LinhaPar = C(0x135A31), Amarelo = C(0xF8D734), Cinza = C(0xBCD5C4), CinzaFaixa = C(0x8FB39C), Branco = Brushes.White,
        Preto = Brushes.Black, Alerta = C(0xFF8A80), Ouro = C(0xDDA917), Ok = C(0x8EE6A8),
        // a selecao e o hover das listas, a borda dos cartoes, o fundo das molduras, os botoes
        Selecao = C(0x168C40), Hover = C(0x176E40), HoverForte = C(0x1A7A45), Borda = C(0x1F7044), VerdeFundo = C(0x07321A),
        AmareloHover = C(0xFAE163), AmareloPremido = C(0xDDA917), Claro = C(0xE8EDE9), ClaroHover = C(0xD2DBD5), ClaroPremido = C(0xB5C2B9);

    // ---------------------------------------------------------------- botoes
    // o botao do launcher: principal = amarelo com a letra preta em negrito (BotaoAmarelo);
    // comum = o claro (BotaoClaro); cantos de 6, sem relevo
    public static Clicavel Botao(string texto, Action clique, bool principal = false, string? icone = null)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        if (icone != null) p.Children.Add(Ui.Icone(icone, 15, Preto));
        if (texto.Length > 0)
            p.Children.Add(new TextBlock
            {
                Text = texto, FontFamily = Tema.Inter, FontSize = 13, FontWeight = principal ? FontWeight.Bold : FontWeight.Normal,
                Foreground = Preto, VerticalAlignment = VerticalAlignment.Center,
            });
        var b = new Clicavel
        {
            Background = principal ? Amarelo : Claro, Hover = principal ? AmareloHover : ClaroHover, CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 5), MinHeight = 30, MinWidth = 40, Child = p,
            BorderBrush = principal ? AmareloPremido : ClaroPremido, BorderThickness = new Thickness(1), CorFoco = principal ? Branco : Amarelo,
        };
        b.Clique += clique;
        Ui.Nome(b, texto);
        return b;
    }

    // ---------------------------------------------------------------- campos
    // os campos brancos com a letra preta sobre o verde (o tema escuro do Fluent os deixaria
    // escuros e com a letra branca): caixas de texto e combos em todos os estados
    public static void Campos(ThemeVariantScope s)
    {
        foreach (var estado in new[] { "", ":pointerover", ":focus", ":focus-within", ":disabled" })
        {
            s.Styles.Add(new Style(x => estado.Length == 0 ? x.OfType<TextBox>() : x.OfType<TextBox>().Class(estado))
            {
                Setters =
                {
                    new Setter(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, Preto),
                    new Setter(TextBox.CaretBrushProperty, Preto),
                    new Setter(TextBox.SelectionBrushProperty, Selecao),
                    new Setter(TextBox.SelectionForegroundBrushProperty, Branco),
                },
            });
            s.Styles.Add(new Style(x => estado.Length == 0 ? x.OfType<ComboBox>() : x.OfType<ComboBox>().Class(estado))
            {
                Setters = { new Setter(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, Preto) },
            });
            s.Styles.Add(new Style(x => (estado.Length == 0 ? x.OfType<ComboBox>() : x.OfType<ComboBox>().Class(estado)).Template().OfType<ContentControl>().Name("ContentPresenter"))
            {
                Setters = { new Setter(ContentControl.ForegroundProperty, Preto) },
            });
            s.Styles.Add(new Style(x => (estado.Length == 0 ? x.OfType<ComboBox>() : x.OfType<ComboBox>().Class(estado)).Template().OfType<PathIcon>().Name("DropDownGlyph"))
            {
                Setters = { new Setter(PathIcon.ForegroundProperty, Preto) },
            });
            if (estado == ":disabled")
                s.Styles.Add(new Style(x => x.OfType<TextBox>().Class(estado).Template().OfType<Border>().Name("PART_BorderElement"))
                {
                    Setters = { new Setter(Border.BackgroundProperty, C(0xD5E2D9)) },
                });
        }
        s.Styles.Add(new Style(x => x.OfType<TextBox>().Template().OfType<TextBlock>().Name("PART_Watermark"))
        {
            Setters = { new Setter(TextBlock.ForegroundProperty, C(0x707070)) },
        });
        // a lista que se abre do combo: verde-escura, a selecao verde
        s.Styles.Add(new Style(x => x.OfType<ComboBox>().Template().OfType<Border>().Name("PopupBorder"))
        {
            Setters = { new Setter(Border.BackgroundProperty, Cartao), new Setter(Border.BorderBrushProperty, Borda) },
        });
        foreach (var (estado, cor) in new[] { (":pointerover", Hover), (":selected", Selecao), (":selected:pointerover", Selecao), (":selected:focus", Selecao) })
        {
            var pcs = estado.Split(':', StringSplitOptions.RemoveEmptyEntries);
            s.Styles.Add(new Style(x =>
            {
                var s0 = x.OfType<ComboBoxItem>();
                foreach (var pc in pcs) s0 = s0.Class(":" + pc);
                return s0.Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
            })
            {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, cor), new Setter(ContentPresenter.ForegroundProperty, Branco) },
            });
        }
        // as caixas de marcar: a marca amarela com o visto preto
        foreach (var estado in new[] { ":checked", ":checked:pointerover", ":checked:pressed" })
        {
            var pcs = estado.Split(':', StringSplitOptions.RemoveEmptyEntries);
            s.Styles.Add(new Style(x =>
            {
                var s0 = x.OfType<CheckBox>();
                foreach (var pc in pcs) s0 = s0.Class(":" + pc);
                return s0.Template().OfType<Border>().Name("NormalRectangle");
            })
            {
                Setters = { new Setter(Border.BackgroundProperty, Amarelo), new Setter(Border.BorderBrushProperty, Amarelo) },
            });
            s.Styles.Add(new Style(x =>
            {
                var s0 = x.OfType<CheckBox>();
                foreach (var pc in pcs) s0 = s0.Class(":" + pc);
                return s0.Template().OfType<Avalonia.Controls.Shapes.Path>().Name("CheckGlyph");
            })
            {
                Setters = { new Setter(Avalonia.Controls.Shapes.Shape.FillProperty, Preto) },
            });
        }
    }
}
