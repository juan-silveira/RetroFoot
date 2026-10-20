using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using RetroFoot.Motor.Save;
using static RetroFoot.App.ConfigEstilo;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// Os titulos (menu Campeonato > Titulos...): a partir da lista dos ultimos vencedores do
// save (S.Campeoes: ano, campeao da 1a divisao e o treinador, vencedor da Taca e o treinador).
//  - Classico: uma lista no jeito das outras listas do jogo, sem logos: equipa, titulos do
//    campeonato, da Taca e o total, do maior para o menor.
//  - Moderno: tres abas: os ultimos campeoes por ano (Taca e campeonato, com os escudos) e o
//    ranking de cada competicao (escudo, o numero e uma taca por titulo).
static class TelaTitulos
{
    internal sealed record Ano(int Valor, Equipa? Campeao, Equipa? Taca);

    internal static List<Ano> Anos(Sessao s) =>
        s.S.Campeoes.Select(c => new Ano(c.W[0], s.Equipa(c.W[1]), s.Equipa(c.W[3])))
            .OrderByDescending(a => a.Valor).ToList();

    static string Nome(Equipa e) => TelaDinheiro.Texto(e.NomeCurto);

    // (equipa, titulos) do maior para o menor; empate: o titulo mais recente primeiro
    internal static List<(Equipa E, int N)> Ranking(List<Ano> anos, Func<Ano, Equipa?> qual) =>
        anos.Where(a => qual(a) != null).GroupBy(a => qual(a)!.ID)
            .Select(g => (E: qual(g.First())!, N: g.Count(), Ultimo: g.Max(a => a.Valor)))
            .OrderByDescending(x => x.N).ThenByDescending(x => x.Ultimo)
            .Select(x => (x.E, x.N)).ToList();

    public static void Abrir(Sessao s) => Abrir((Dialogos)s, s);

    public static void Abrir(Dialogos d, Sessao s)
    {
        var anos = Anos(s);
        if (Celular.Ativo) { ListasCelular.Titulos(d, anos); return; }
        if (Visual.Moderno) { Moderno(d, anos); return; }
        // classico: as duas contagens numa lista larga
        var ids = anos.SelectMany(a => new[] { a.Campeao, a.Taca }).Where(e => e != null).Select(e => e!).DistinctBy(e => e.ID).ToList();
        var linhas = ids.Select(e => (E: e, C: anos.Count(a => a.Campeao?.ID == e.ID), T: anos.Count(a => a.Taca?.ID == e.ID)))
            .OrderByDescending(x => x.C + x.T).ThenByDescending(x => x.C).ToList();
        TelaListas.Lista(d, T("Títulos: campeonato e copa"), linhas.Count, i =>
        {
            var (e, c, t) = linhas[i];
            var b = new List<byte>();
            Formatado.Col(b, Dlg.Bytes(e.NomeCurto), (byte)'x');
            Formatado.Col(b, T("Camp.") + " " + c, 0x46);
            Formatado.Col(b, T("Copa") + " " + t, 0x46);
            Formatado.Col(b, T("Total") + " " + (c + t), 0x46);
            return b.ToArray();
        }, TelaListas.OpLarga);
    }

    // ------------------------------------------------------------------ Moderno
    static void Moderno(Dialogos d, List<Ano> anos)
    {
        var c = new CaixaModerna(T("Títulos"), 640, rolar: true);
        c.Sub(anos.Count == 0 ? T("Ainda não há campeões: os títulos aparecem no fim da primeira temporada.") : T("Campeões do campeonato e da copa"));
        var abas = new AbasModernas();
        c.Corpo.Children.Add(abas);

        // os ultimos campeoes por ano
        var lista = new StackPanel { Spacing = 4 };
        var cab = Linha(T("Ano"), T("Copa"), T("Campeonato"), true);
        lista.Children.Add(cab);
        foreach (var a in anos) lista.Children.Add(LinhaAno(a));
        var camp = PaginaRanking(Ranking(anos, a => a.Campeao));
        var copa = PaginaRanking(Ranking(anos, a => a.Taca));
        abas.Pagina(T("Últimos campeões"), lista);
        abas.Pagina(T("Campeonato"), camp);
        abas.Pagina(T("Copa"), copa);
        c.Corpo.Children.Add(new Panel { Margin = new Thickness(0, 12, 0, 0), Children = { lista, camp, copa } });

        void Fechar() => d.Fechar(c);
        c.Rodape((T("OK"), true, Fechar));
        c.AoEnter = Fechar;
        c.AoFechar = Fechar;
        d.AbrirControle(c);
    }

    static Grid Linha(string a, string b, string c, bool titulo)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("70,*,*"), Margin = new Thickness(10, 0) };
        for (int i = 0; i < 3; i++)
        {
            var t = new TextBlock { Text = new[] { a, b, c }[i], FontSize = 12, Foreground = TemaVerde.TextoFraco, FontWeight = FontWeight.SemiBold };
            Grid.SetColumn(t, i);
            g.Children.Add(t);
        }
        return g;
    }

    static Control LinhaAno(Ano a)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("70,*,*"), Margin = new Thickness(4, 2), MinHeight = 34 };
        g.Children.Add(new TextBlock { Text = a.Valor.ToString(), FontSize = 16, FontWeight = FontWeight.Bold, Foreground = TemaVerde.Destaque, VerticalAlignment = VerticalAlignment.Center });
        var c1 = EquipaCompacta(a.Taca); Grid.SetColumn(c1, 1); g.Children.Add(c1);
        var c2 = EquipaCompacta(a.Campeao); Grid.SetColumn(c2, 2); g.Children.Add(c2);
        return new Border { Background = TemaVerde.Cartao, CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 2), Child = g };
    }

    static Control EquipaCompacta(Equipa? e)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (e == null) { p.Children.Add(new TextBlock { Text = "—", Foreground = TemaVerde.TextoFraco }); return p; }
        if (Escudos.Controle(e, 26) is { } logo) p.Children.Add(logo);
        p.Children.Add(new TextBlock { Text = Nome(e), FontSize = 13.5, Foreground = TemaVerde.Texto, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        return p;
    }

    // o ranking: as equipas com o mesmo numero de titulos ficam na mesma linha (como no
    // quadro de campeoes), o numero numa caixa e uma taca por titulo
    static Control PaginaRanking(List<(Equipa E, int N)> r)
    {
        var p = new StackPanel { Spacing = 0 };
        if (r.Count == 0) { p.Children.Add(new TextBlock { Text = T("Ainda não há campeões."), Foreground = TemaVerde.TextoFraco, Margin = new Thickness(4, 10) }); return p; }
        foreach (var grupo in r.GroupBy(x => x.N))
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("220,52,*"), Margin = new Thickness(4, 8) };
            var equipas = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var (e, _) in grupo)
            {
                var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 2, 12, 2) };
                if (Escudos.Controle(e, 28) is { } logo) item.Children.Add(logo);
                item.Children.Add(new TextBlock { Text = Nome(e), FontSize = 13, Foreground = TemaVerde.Texto, VerticalAlignment = VerticalAlignment.Center });
                equipas.Children.Add(item);
            }
            g.Children.Add(equipas);
            var num = new Border
            {
                Width = 40, Height = 40, CornerRadius = new CornerRadius(6), Background = TemaVerde.FundoEscuro,
                BorderBrush = TemaVerde.Borda, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = grupo.Key.ToString(), FontSize = 20, FontWeight = FontWeight.Bold, Foreground = TemaVerde.Texto, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            Grid.SetColumn(num, 1);
            g.Children.Add(num);
            var tacas = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            for (int i = 0; i < grupo.Key; i++) tacas.Children.Add(Taca());
            Grid.SetColumn(tacas, 2);
            g.Children.Add(tacas);
            p.Children.Add(g);
            p.Children.Add(new Border { Height = 1, Background = TemaVerde.Linha });
        }
        return p;
    }

    // a taca (o "trophy" do Lucide, ISC)
    static Control Taca() => new Path
    {
        Data = Geometry.Parse("M6 9H4.5a2.5 2.5 0 0 1 0-5H6M18 9h1.5a2.5 2.5 0 0 0 0-5H18M4 22h16M10 14.66V17c0 .55-.47.98-.97 1.21C7.85 18.75 7 20.24 7 22M14 14.66V17c0 .55.47.98.97 1.21C16.15 18.75 17 20.24 17 22M18 2H6v7a6 6 0 0 0 12 0V2Z"),
        Stroke = TemaVerde.Destaque, StrokeThickness = 1.8, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
        Width = 26, Height = 26, Stretch = Stretch.Uniform, Margin = new Thickness(2),
    };
}
