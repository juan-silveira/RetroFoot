using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace RetroFoot.App;

// O visual Moderno do hub e das ferramentas: o que so ele tem (os botoes do hub e as
// cores que o ConfigEstilo nao tem) e os icones de traco Lucide, que os dois visuais usam (como
// os de IconesModernos.cs; licenca ISC, la). As outras pecas sao as de Ui.cs, com as cores do Tema.
public static class UiModerno
{
    // as cores sao as do TemaVerde (o tema do Moderno)
    public static readonly IBrush Campo = TemaVerde.CartaoForte, Fundo2 = TemaVerde.FundoEscuro, AzulHover = TemaVerde.DestaqueClaro, HoverForte = TemaVerde.Selecao;
    static readonly IBrush Azul = ConfigEstilo.Azul, Escuro = ConfigEstilo.TextoEscuro, Texto = ConfigEstilo.Texto,
        Linha = ConfigEstilo.Linha;
    static readonly FontFamily Inter = ConfigEstilo.Inter;

    // ---------------------------------------------------------------- icones (Lucide, 24x24, traco 2)
    static string C(double cx, double cy, double r) =>
        FormattableString.Invariant($"M{cx - r} {cy}a{r} {r} 0 1 0 {2 * r} 0a{r} {r} 0 1 0 {-2 * r} 0");

    public static readonly Dictionary<string, string> Icones = new()
    {
        ["jogar"] = "M6 3l14 9-14 9z",
        ["escudo"] = "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z",
        ["disquete"] = "M15.2 3a2 2 0 0 1 1.4.6l3.8 3.8a2 2 0 0 1 .6 1.4V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zM17 21v-7a1 1 0 0 0-1-1H8a1 1 0 0 0-1 1v7M7 3v4a1 1 0 0 0 1 1h7",
        ["lupa"] = C(11, 11, 8) + "M21 21l-4.3-4.3",
        ["peca"] = "M19.439 7.85c-.049.322.059.648.289.878l1.568 1.568c.47.47.706 1.087.706 1.704s-.235 1.233-.706 1.704l-1.611 1.611a.98.98 0 0 1-.837.276c-.47-.07-.802-.48-.968-.925a2.501 2.501 0 1 0-3.214 3.214c.446.166.855.497.925.968a.979.979 0 0 1-.276.837l-1.61 1.61a2.404 2.404 0 0 1-1.705.707 2.402 2.402 0 0 1-1.704-.706l-1.568-1.568a1.026 1.026 0 0 0-.877-.29c-.493.074-.84.504-1.02.968a2.5 2.5 0 1 1-3.237-3.237c.464-.18.894-.527.967-1.02a1.026 1.026 0 0 0-.289-.877l-1.568-1.568A2.402 2.402 0 0 1 1.998 12c0-.617.236-1.234.706-1.704L4.23 8.77c.24-.24.581-.353.917-.303.515.077.877.528 1.073 1.01a2.5 2.5 0 1 0 3.259-3.259c-.482-.196-.933-.558-1.01-1.073-.05-.336.062-.676.303-.917l1.525-1.525A2.402 2.402 0 0 1 12 1.998c.617 0 1.234.236 1.704.706l1.568 1.568c.23.23.556.338.877.29.493-.074.84-.504 1.02-.968a2.5 2.5 0 1 1 3.237 3.237c-.464.18-.894.527-.967 1.02Z",
        ["apito"] = "M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1zM4 22v-7",
        ["ajustes"] = "M21 4h-7M10 4H3M21 12h-9M8 12H3M21 20h-5M12 20H3M14 2v4M8 10v4M16 18v4",
        ["config"] = "M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z" + C(12, 12, 3),
        ["info"] = C(12, 12, 10) + "M12 16v-4M12 8h.01",
        ["sair"] = "M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9",
        ["pasta"] = "m6 14 1.5-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.54 6a2 2 0 0 1-1.95 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H18a2 2 0 0 1 2 2v2",
        ["relogio"] = "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8M3 3v5h5",
        ["voltar"] = "m12 19-7-7 7-7M19 12H5",
        ["fechar"] = "M18 6 6 18M6 6l12 12",
        ["bola"] = C(12, 12, 10) + "M12 7l4 3-1.5 5h-5L8 10z",
        ["visto"] = "M20 6 9 17l-5-5",
        ["trofeu"] = "M6 9H4.5a2.5 2.5 0 0 1 0-5H6M18 9h1.5a2.5 2.5 0 0 0 0-5H18M4 22h16M10 14.66V17c0 .55-.47.98-.97 1.21C7.85 18.75 7 20.24 7 22M14 14.66V17c0 .55.47.98.97 1.21C16.15 18.75 17 20.24 17 22M18 2H6v7a6 6 0 0 0 12 0V2Z",
        ["aviso"] = "m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3M12 9v4M12 17h.01",
        ["seta"] = "M5 12h14M12 5l7 7-7 7",
    };

    // o icone desenhado com traco (como IconesModernos.Desenhar)
    sealed class IconeTraco(Geometry g, IBrush cor) : Control
    {
        public override void Render(DrawingContext dc)
        {
            double k = Math.Min(Bounds.Width, Bounds.Height) / 24;
            if (k <= 0) return;
            var pen = new Pen(cor, Math.Max(2, 1.6 / k), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            double ox = (Bounds.Width - 24 * k) / 2, oy = (Bounds.Height - 24 * k) / 2;
            using (dc.PushTransform(Matrix.CreateScale(k, k) * Matrix.CreateTranslation(ox, oy)))
                dc.DrawGeometry(null, pen, g);
        }
    }

    static readonly Dictionary<string, Geometry> _geo = new();

    public static Control Icone(string nome, double tam, IBrush cor)
    {
        if (Icones.TryGetValue(nome, out var d))
        {
            if (!_geo.TryGetValue(nome, out var g)) _geo[nome] = g = Geometry.Parse(d);
            return new IconeTraco(g, cor)
            {
                Width = tam, Height = tam, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        }
        // um caminho cheio solto
        try
        {
            return new PathIcon
            {
                Data = Geometry.Parse(nome), Width = tam, Height = tam, Foreground = cor,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
        }
        catch { return new Border { Width = tam, Height = tam }; }
    }

    // ---------------------------------------------------------------- botoes
    // como os do hub: principal = a pilula ouro do Jogar (degrade, letra verde-escura); comum = o
    // verde-escuro meio transparente com o fio dourado, a letra branca e o icone amarelo
    public static Clicavel Botao(string texto, Action clique, bool principal = false, string? icone = null)
    {
        var cor = principal ? Escuro : Texto;
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        if (icone != null) p.Children.Add(Icone(icone, 15, principal ? Escuro : Azul));
        if (texto.Length > 0)
            p.Children.Add(new TextBlock { Text = texto, FontFamily = Inter, FontSize = 13, FontWeight = principal ? FontWeight.Bold : FontWeight.SemiBold, Foreground = cor, VerticalAlignment = VerticalAlignment.Center });
        var b = new Clicavel
        {
            Background = principal ? TemaVerde.BotaoPrimario : TemaVerde.BotaoSecundario, CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 5),
            MinHeight = 30, MinWidth = 40, Child = p, Hover = principal ? TemaVerde.BotaoPrimarioHover : HoverForte,
            BorderBrush = principal ? TemaVerde.DestaqueClaro : Linha, BorderThickness = new Thickness(1), CorFoco = principal ? Texto : Azul,
            BoxShadow = principal ? BoxShadows.Parse("0 2 8 0 #50000000") : default,
        };
        b.Clique += clique;
        Ui.Nome(b, texto);
        return b;
    }
}
