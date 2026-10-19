using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Rodada.Campos;
using static RetroFoot.App.Idioma;
using Path = Avalonia.Controls.Shapes.Path;
using TRegras = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.App;

// A Classificacao no visual Moderno (Visual.cs; tema escuro, no estilo das tabelas de
// campeonato do Google), no lugar do TclassificationDlg do original (TelaClassificacaoOriginal):
//  - uma divisao por vez, escolhida nas abas do topo (1a a 4a e o Distrital; num jogo de um
//    pais so, com o logo da liga: Escudos.Liga); abre na
//    divisao com mais treinadores humanos (empate: a de cima);
//  - colunas: posicao, clube (logo da equipa, ou escudo nas cores dela, + nome curto), Pts,
//    PJ, VIT, E, DER, GM, GC, SG e as ultimas 5 partidas do campeonato (do calendario da equipa);
//  - a barra a esquerda marca a zona: campeao (1o da 1a), sobe (os K primeiros da 2a a 4a)
//    e desce (os K ultimos da 1a a 4a; na 4a, para o Distrital), K = Regras.SobemDescem;
//  - "Continuar" (ou Esc / Enter) fecha; depois da jornada fecha sozinha em 10 s (como o
//    relogio do original, seg06:0a50); mudar de divisao recomeca a contagem.
sealed class TelaClassificacaoModerna : UserControl
{
    // cores (as do TemaVerde: a tabela num cartao verde-escuro sobre o quadro)
    static readonly IBrush Fundo = TemaVerde.FundoTela, FundoZebra = TemaVerde.Zebra, FundoHumano = TemaVerde.Humano;
    static readonly IBrush Texto = TemaVerde.Texto, TextoFraco = TemaVerde.TextoFraco, Linha = TemaVerde.Linha;
    static readonly IBrush Azul = TemaVerde.Destaque;
    static readonly IBrush CorCampeao = TemaVerde.Destaque, CorSobe = TemaVerde.Verde, CorDesce = TemaVerde.VermelhoForte;
    static readonly IBrush CorVitoria = TemaVerde.VerdeForte, CorEmpate = TemaVerde.CinzaForte, CorDerrota = TemaVerde.VermelhoForte;
    static readonly FontFamily Inter = new("fonts:Inter#Inter");

    const double AlturaLinha = 26;
    // larguras das colunas: barra, posicao, clube (*), Pts, PJ, VIT, E, DER, GM, GC, SG, ultimas 5
    static readonly string Colunas = "4,34,*,46,40,40,40,40,40,40,46,152";

    public event Action? Fechou;

    readonly Jogo98 _s;
    readonly TRegras _regras;
    readonly Dictionary<ushort, Equipa> _equipas = new();
    readonly Dictionary<ushort, Treinador> _treinadores = new();
    readonly string _titulo;
    readonly int _divisoes;
    int _divisao;
    int _tiques;
    DispatcherTimer? _relogio;
    bool _fechou;

    readonly StackPanel _abas = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    readonly StackPanel _linhas = new();
    readonly TextBlock _textoBotao = new() { FontWeight = FontWeight.SemiBold, FontSize = 14, Foreground = TemaVerde.SobreDestaque };

    public TelaClassificacaoModerna(Jogo98 s, int rodada, bool relogio, TRegras regras)
    {
        _s = s;
        _regras = regras;
        foreach (var e in s.Equipas) _equipas[e.ID] = e;
        foreach (var t in s.Treinadores) _treinadores[t.ID] = t;
        _divisoes = Math.Min(s.Divisoes.Count, 5);
        _titulo = rodada > 0 ? T("Classificação · {0}ª rodada", rodada) : T("Classificação");
        _divisao = DivisaoDosHumanos();

        Focusable = true;
        FontFamily = Inter;
        Background = Fundo;
        Content = Montar();
        MostrarDivisao(_divisao);

        if (relogio)
        {
            _relogio = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _relogio.Tick += (_, _) =>
            {
                if (++_tiques >= 10) Fechar();
                else AtualizarBotao();
            };
            _relogio.Start();
        }
        AtualizarBotao();
        AttachedToVisualTree += (_, _) => Focus();
        DetachedFromVisualTree += (_, _) => _relogio?.Stop();
    }

    // ---------------------------------------------------------------- dados
    bool Humana(Equipa e) => _treinadores.TryGetValue(e.Treinador, out var t) && t.Humano != 0;

    // a divisao com mais equipas de treinadores humanos (empate: a de cima; nenhum: a 1a)
    int DivisaoDosHumanos()
    {
        int melhor = 0, max = 0;
        for (int d = 0; d < _divisoes; d++)
        {
            int n = Equipas(d).Count(Humana);
            if (n > max) { max = n; melhor = d; }
        }
        return melhor;
    }

    List<Equipa> Equipas(int d)
    {
        var l = new List<Equipa>();
        if (d < _s.Divisoes.Count)
            foreach (var id in _s.Divisoes[d].Equipas)
                if (_equipas.TryGetValue(id, out var e)) l.Add(e);
        return l;
    }

    static string NomeDivisao(int d) => d < 4 ? T("{0}ª Divisão", d + 1) : T("Distrital");

    // a zona da posicao i (0 = 1o) numa divisao de n equipas
    IBrush? Zona(int d, int i, int n)
    {
        int k = _regras.SobemDescem;
        if (d >= 4) return null;
        if (d == 0 && i == 0) return CorCampeao;
        if (d > 0 && i < k) return CorSobe;
        if (i >= n - k) return CorDesce;
        return null;
    }

    // as ultimas 5 do campeonato (B07 = 0, ja jogadas: W0A >= 0), da mais antiga a mais recente;
    // 1 vitoria, 0 empate, -1 derrota
    static List<int> Ultimas(Equipa e)
    {
        var r = new List<int>();
        foreach (var c in e.Calendario)
        {
            if (c.B07 != 0 || (short)c.W0A < 0 || (short)c.W0C < 0) continue;
            short gp = (short)c.W0A, gc = (short)c.W0C;
            r.Add(gp > gc ? 1 : gp == gc ? 0 : -1);
        }
        return r.Count > 5 ? r.GetRange(r.Count - 5, 5) : r;
    }

    static string Nome(byte[] cifrado) =>
        cifrado.Length == 0 ? "" : System.Text.Encoding.Latin1.GetString(Cifra.TextoEFT(cifrado).Texto);

    // TColor (00BBGGRR); cores do sistema (0x80......) ficam no padrao
    static Color Cor(byte[] c, Color padrao) => c.Length < 4 || c[3] != 0 ? padrao : Color.FromRgb(c[0], c[1], c[2]);

    // ---------------------------------------------------------------- montagem
    Control Montar()
    {
        // topo: titulo, abas das divisoes e o Continuar
        var titulo = new TextBlock
        {
            Text = _titulo, FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Texto,
            VerticalAlignment = VerticalAlignment.Center,
        };
        for (int d = 0; d < _divisoes; d++) _abas.Children.Add(Aba(d));
        var botao = new Border
        {
            Background = TemaVerde.BotaoPrimario, CornerRadius = new CornerRadius(18), Padding = new Thickness(20, 8),
            BorderBrush = TemaVerde.Alfa(Colors.White, 0.55), BorderThickness = new Thickness(1.5),
            Cursor = new Cursor(StandardCursorType.Hand), Child = _textoBotao, VerticalAlignment = VerticalAlignment.Center,
        };
        botao.PointerPressed += (_, a) => { a.Handled = true; Fechar(); };
        // 16:9: titulo, abas e Continuar numa linha; 4:3: as abas descem para a linha de baixo
        // (numa linha so, com 5 divisoes, elas passavam por baixo do Continuar)
        bool largo = Formato.Largo;
        var topo = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), RowDefinitions = new RowDefinitions(largo ? "Auto" : "Auto,Auto"),
            Margin = new Thickness(0, 0, 0, 10),
        };
        topo.Children.Add(titulo);
        _abas.VerticalAlignment = VerticalAlignment.Center;
        if (largo) { Grid.SetColumn(_abas, 1); _abas.HorizontalAlignment = HorizontalAlignment.Center; }
        else { Grid.SetRow(_abas, 1); Grid.SetColumnSpan(_abas, 3); _abas.HorizontalAlignment = HorizontalAlignment.Left; _abas.Margin = new Thickness(0, 6, 0, 0); }
        topo.Children.Add(_abas);
        Grid.SetColumn(botao, 2);
        topo.Children.Add(botao);

        var cabecalho = LinhaGrade();
        cabecalho.Height = 30;
        string[] nomes = { "", "", "Clube", "Pts", "PJ", "VIT", "E", "DER", "GM", "GC", "SG", "Últimas 5" };
        for (int c = 2; c < nomes.Length; c++)
        {
            var t = new TextBlock
            {
                Text = T(nomes[c]), FontSize = 12, Foreground = TextoFraco, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = c == 2 ? HorizontalAlignment.Left : HorizontalAlignment.Center,
                Margin = c == 2 ? new Thickness(8, 0, 0, 0) : default,
            };
            Grid.SetColumn(t, c);
            cabecalho.Children.Add(t);
        }
        var cab = new Border { BorderBrush = Linha, BorderThickness = new Thickness(0, 0, 0, 1), Child = cabecalho };

        // as linhas rolam so alem de 20 equipas (o Distrital, com dezenas): ate ai a tela inteira
        // cabe sem rolagem (o Viewbox do fim encolhe o que for preciso) e o Distrital nao fica
        // minusculo. A legenda fica fixa embaixo.
        var lista = new ScrollViewer
        {
            Content = _linhas, MaxHeight = 20 * (AlturaLinha + 1) + 2,   // + a linha de baixo de cada uma
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        // o cabecalho acompanha a folga da barra de rolagem (App.FolgaBarra): as colunas batem
        lista.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.PaddingProperty) cab.Padding = new Thickness(0, 0, lista.Padding.Right, 0);
        };
        var rolagem = new DockPanel();
        var legenda = Legenda();
        DockPanel.SetDock(legenda, Dock.Bottom);
        rolagem.Children.Add(legenda);
        rolagem.Children.Add(lista);

        // a tabela (cabecalho e linhas) num cartao, como os botoes do hub
        var tabela = new DockPanel();
        DockPanel.SetDock(cab, Dock.Top);
        tabela.Children.Add(cab);
        tabela.Children.Add(rolagem);
        var cartao = new Border
        {
            Background = TemaVerde.Cartao, BorderBrush = TemaVerde.Borda, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 4, 10, 10), Child = tabela, VerticalAlignment = VerticalAlignment.Top,
        };
        var corpo = new DockPanel { Width = largo ? 1000 : 900, Margin = new Thickness(20, 16, 20, 12) };
        DockPanel.SetDock(topo, Dock.Top);
        corpo.Children.Add(topo);
        corpo.Children.Add(cartao);
        // a tabela inteira (20 equipas e a legenda) sem rolagem: encolhe o que for preciso para
        // caber na tela (no 4:3 com 20 equipas ficava cortada embaixo, com barra de rolagem)
        return new Avalonia.Controls.ThemeVariantScope
        {
            RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark,
            Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, VerticalAlignment = VerticalAlignment.Top, Child = corpo },
        };
    }

    static Grid LinhaGrade() => new() { ColumnDefinitions = new ColumnDefinitions(Colunas) };

    Control Aba(int d)
    {
        var t = new TextBlock { Text = NomeDivisao(d), FontSize = 14, FontWeight = FontWeight.Medium, HorizontalAlignment = HorizontalAlignment.Center };
        // jogo de um pais so: o logo da liga daquele nivel ao lado do nome
        Control conteudo = t;
        if (Escudos.ControleComDisco(Escudos.Liga(_s, d), 18) is { } logo)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            p.Children.Add(logo);
            p.Children.Add(t);
            conteudo = p;
        }
        var b = new Border
        {
            Padding = new Thickness(12, 8, 12, 6), Background = Brushes.Transparent, BorderThickness = new Thickness(0, 0, 0, 3),
            Cursor = new Cursor(StandardCursorType.Hand), Child = conteudo, Tag = d,
        };
        b.PointerPressed += (_, a) => { a.Handled = true; MostrarDivisao(d); _tiques = 0; AtualizarBotao(); };
        return b;
    }

    void MostrarDivisao(int d)
    {
        _divisao = Math.Clamp(d, 0, Math.Max(_divisoes - 1, 0));
        foreach (var c in _abas.Children.OfType<Border>())
        {
            bool sel = (int)c.Tag! == _divisao;
            c.BorderBrush = sel ? Azul : Brushes.Transparent;
            var t = c.Child as TextBlock ?? ((Panel)c.Child!).Children.OfType<TextBlock>().First();
            t.Foreground = sel ? Azul : TextoFraco;
        }
        _linhas.Children.Clear();
        var eqs = Equipas(_divisao);
        for (int i = 0; i < eqs.Count; i++) _linhas.Children.Add(LinhaEquipa(eqs[i], i, eqs.Count));
    }

    Control LinhaEquipa(Equipa e, int i, int n)
    {
        bool humana = Humana(e);
        var g = LinhaGrade();
        g.Height = AlturaLinha;
        var zona = Zona(_divisao, i, n);
        if (zona != null)
        {
            var barra = new Border { Background = zona, Margin = new Thickness(0, 2), CornerRadius = new CornerRadius(2) };
            g.Children.Add(barra);
        }
        Celula(g, 1, (i + 1).ToString(), TextoFraco);

        // clube: escudo + nome curto
        var clube = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        clube.Children.Add(Escudos.ControleComDisco(Escudos.Escudo(e), 20) ?? Escudo(e));
        clube.Children.Add(new TextBlock
        {
            Text = Nome(e.NomeCurto), FontSize = 14, Foreground = Texto, VerticalAlignment = VerticalAlignment.Center,
            FontWeight = humana ? FontWeight.Bold : FontWeight.Normal, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(clube, 2);
        g.Children.Add(clube);

        if (_divisao == 4)
        {
            // o Distrital nao joga: o jogo nao zera +0x16..+0x21 dessas equipas (restos de
            // outras temporadas e dos patches), entao nao ha numeros a mostrar
            for (int c = 3; c <= 10; c++) Celula(g, c, "–", TextoFraco);
        }
        else
        {
            int v = EqW(e, EqVitorias), em = EqW(e, EqEmpates), de = EqW(e, EqDerrotas);
            int gm = EqW(e, EqGolosPro), gc = EqW(e, EqGolosContra), sg = gm - gc;
            Celula(g, 3, EqW(e, EqPontos).ToString(), Texto, true);
            Celula(g, 4, (v + em + de).ToString(), Texto);
            Celula(g, 5, v.ToString(), Texto);
            Celula(g, 6, em.ToString(), Texto);
            Celula(g, 7, de.ToString(), Texto);
            Celula(g, 8, gm.ToString(), Texto);
            Celula(g, 9, gc.ToString(), Texto);
            Celula(g, 10, sg > 0 ? "+" + sg : sg.ToString(), Texto);
        }

        var ult = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var r = Ultimas(e);
        for (int k = 0; k < 5 - r.Count; k++) ult.Children.Add(Resultado(null, false));
        for (int k = 0; k < r.Count; k++) ult.Children.Add(Resultado(r[k], k == r.Count - 1));
        Grid.SetColumn(ult, 11);
        g.Children.Add(ult);

        var linha = new Border
        {
            Background = humana ? FundoHumano : i % 2 == 1 ? FundoZebra : Brushes.Transparent,
            BorderBrush = Linha, BorderThickness = new Thickness(0, 0, 0, 1), Child = g,
        };
        // o clique numa equipa abre o plantel dela (seg06:07f4, TPlantelDlg) com o relogio
        // parado; ao fechar, o relogio continua de onde estava (17:2671)
        if (AbrirEquipa != null)
        {
            linha.Cursor = new Cursor(StandardCursorType.Hand);
            linha.PointerReleased += (_, a) =>
            {
                if (_fechou || a.InitialPressMouseButton != MouseButton.Left) return;
                a.Handled = true;
                _relogio?.Stop();
                AbrirEquipa(e, _relogio != null, () => { if (!_fechou) { _relogio?.Start(); Focus(); } });
            };
        }
        return linha;
    }

    static Action<Equipa, bool, Action>? AbrirEquipa => TelaClassificacao.AbrirEquipa;

    static void Celula(Grid g, int c, string s, IBrush cor, bool negrito = false)
    {
        var t = new TextBlock
        {
            Text = s, FontSize = 14, Foreground = cor, FontWeight = negrito ? FontWeight.Bold : FontWeight.Normal,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(t, c);
        g.Children.Add(t);
    }

    // escudo em duas cores: o corpo na cor do fundo e a faixa e o contorno na cor da letra
    static Control Escudo(Equipa e)
    {
        var fundo = Cor(e.CorFundo, Colors.White);
        var letra = Cor(e.CorLetra, Colors.Black);
        var forma = Geometry.Parse("M1,1 L17,1 L17,9 C17,14 13,17.5 9,19 C5,17.5 1,14 1,9 Z");
        var faixa = Geometry.Parse("M1,7 L17,7 L17,11 L1,11 Z");
        var c = new Canvas { Width = 18, Height = 20, VerticalAlignment = VerticalAlignment.Center };
        c.Children.Add(new Path { Data = forma, Fill = new SolidColorBrush(fundo) });
        c.Children.Add(new Path { Data = faixa, Fill = new SolidColorBrush(letra), Clip = forma });
        // contorno: na cor da letra, e um aro claro por fora para nao sumir no fundo escuro
        c.Children.Add(new Path { Data = forma, Stroke = new SolidColorBrush(letra), StrokeThickness = 1.2 });
        c.Children.Add(new Path { Data = Geometry.Parse("M0.2,0.2 L17.8,0.2 L17.8,9 C17.8,14.5 13.5,18.3 9,19.9 C4.5,18.3 0.2,14.5 0.2,9 Z"), Stroke = TemaVerde.TextoFraco, StrokeThickness = 0.6 });
        return c;
    }

    // um resultado: verde com check, cinza com traco, vermelho com x; null = ainda nao jogou
    static Control Resultado(int? r, bool recente)
    {
        const double t = 20;
        var g = new Grid { Width = t + 4, Height = t + 4 };
        if (recente)
            g.Children.Add(new Ellipse { Width = t + 4, Height = t + 4, Stroke = Texto, StrokeThickness = 1.5 });
        if (r is not int v)
        {
            g.Children.Add(new Ellipse { Width = t - 4, Height = t - 4, Stroke = CorEmpate, StrokeThickness = 1.5 });
            return g;
        }
        g.Children.Add(new Ellipse { Width = t, Height = t, Fill = v > 0 ? CorVitoria : v == 0 ? CorEmpate : CorDerrota });
        g.Children.Add(Simbolo(v));
        return g;
    }

    // os simbolos desenhados (a fonte pode nao ter os glifos)
    static Control Simbolo(int v)
    {
        string d = v > 0 ? "M5.5,10.5 L8.8,13.6 L14.5,7" : v == 0 ? "M6,10 L14,10" : "M6.5,6.5 L13.5,13.5 M13.5,6.5 L6.5,13.5";
        return new Path
        {
            Data = Geometry.Parse(d), Stroke = Brushes.White, StrokeThickness = 2, StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round, Width = 20, Height = 20,
        };
    }

    Control Legenda()
    {
        // duas colunas; em cada uma, o titulo e os itens numa linha (para caber 20 equipas)
        static StackPanel Coluna(string titulo, params Control[] itens)
        {
            var linha = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var i in itens) { i.Margin = new Thickness(0, 0, 20, 0); linha.Children.Add(i); }
            var p = new StackPanel { Spacing = 6 };
            p.Children.Add(TituloLegenda(titulo));
            p.Children.Add(linha);
            return p;
        }
        var zonas = Coluna(T("Zonas"),
            ItemLegenda(BarraLegenda(CorCampeao), T("Campeão")),
            ItemLegenda(BarraLegenda(CorSobe), T("Sobe de divisão")),
            ItemLegenda(BarraLegenda(CorDesce), T("Desce de divisão")));
        var ult = Coluna(T("Últimas 5 partidas"),
            ItemLegenda(Resultado(1, false), T("Vitória")),
            ItemLegenda(Resultado(0, false), T("Empate")),
            ItemLegenda(Resultado(-1, false), T("Derrota")));
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, 12, 0, 0) };
        g.Children.Add(zonas);
        Grid.SetColumn(ult, 1);
        g.Children.Add(ult);
        return g;
    }

    static TextBlock TituloLegenda(string s) => new() { Text = s, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Texto };

    static Control BarraLegenda(IBrush cor) =>
        new Border { Width = 4, Height = 18, Background = cor, CornerRadius = new CornerRadius(2), Margin = new Thickness(4, 0, 4, 0) };

    static Control ItemLegenda(Control icone, string s)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        icone.VerticalAlignment = VerticalAlignment.Center;
        p.Children.Add(icone);
        p.Children.Add(new TextBlock { Text = s, FontSize = 13, Foreground = TextoFraco, VerticalAlignment = VerticalAlignment.Center });
        return p;
    }

    // ---------------------------------------------------------------- comandos
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
            case Key.Enter:
                Fechar();
                break;
            case Key.Left:
                if (_divisao > 0) { MostrarDivisao(_divisao - 1); _tiques = 0; AtualizarBotao(); }
                break;
            case Key.Right:
                if (_divisao < _divisoes - 1) { MostrarDivisao(_divisao + 1); _tiques = 0; AtualizarBotao(); }
                break;
            default:
                base.OnKeyDown(e);
                return;
        }
        e.Handled = true;
    }

    void AtualizarBotao() => _textoBotao.Text = _relogio != null ? T("Continuar ({0})", 10 - _tiques) : T("Continuar");


    void Fechar()
    {
        if (_fechou) return;
        _fechou = true;
        _relogio?.Stop();
        Fechou?.Invoke();
    }

    static IBrush Hex(string s) => new SolidColorBrush(Color.Parse(s));
}
