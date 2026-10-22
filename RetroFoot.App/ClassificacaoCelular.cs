using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Rodada.Campos;
using static RetroFoot.App.Idioma;
using TRegras = RetroFoot.Motor.Regras.Regras;
using R = RetroFoot.App.TelaClassificacaoRetro;

namespace RetroFoot.App;

// A Classificacao no celular (Celular.cs), em pe, nas duas peles do kit (CelularUi.cs), no lugar
// do TclassificationDlg e da tabela do Moderno:
//  - a tela toda: a barra com o titulo (e a jornada) e a seta de voltar; as divisoes nas abas
//    (Retro: 1a a 4a, como o classico; Moderno: e o Distrital), abrindo na divisao com mais
//    treinadores humanos (empate: a de cima);
//  - tres vistas das colunas (o celular nao cabe todas de uma vez): Tabela (Pontos, Jogos,
//    Vitorias, Empates, Derrotas e, no Retro, golos marcados:sofridos como o classico; no
//    Moderno, o saldo), Golos (marcados, sofridos, saldo) e as Ultimas 5 partidas;
//  - a equipa de cada linha nas cores dela (Retro) ou com o logo (Moderno); a do humano
//    destacada; a barra a esquerda marca a zona (campeao, sobe, desce), com a legenda embaixo;
//  - tocar numa equipa abre o plantel (com o relogio parado, que continua ao voltar);
//  - "Continuar" (ou o voltar, Enter, Esc) fecha; depois da jornada fecha sozinha em 10 s
//    (seg06:0a50); mudar de divisao recomeca a contagem. As mesmas regras das telas do computador.
sealed class TelaClassificacaoCelular : UserControl
{
    public event Action? Fechou;

    readonly Jogo98 _s;
    readonly TRegras _regras;
    readonly Dictionary<ushort, Equipa> _equipas = new();
    readonly Dictionary<ushort, Treinador> _treinadores = new();
    readonly int _divisoes;
    int _divisao, _vista;
    int _tiques;
    DispatcherTimer? _relogio;
    bool _fechou;
    readonly ContentControl _tabela = new();
    readonly CUi.Botao _continuar;
    Action<int>? _marcarAba;
    Control? _filaAbas;

    public TelaClassificacaoCelular(Jogo98 s, int rodada, bool relogio, TRegras regras)
    {
        _s = s;
        _regras = regras;
        foreach (var e in s.Equipas) _equipas[e.ID] = e;
        foreach (var t in s.Treinadores) _treinadores[t.ID] = t;
        _divisoes = Math.Min(s.Divisoes.Count, CUi.Retro ? 4 : 5);
        _divisao = DivisaoDosHumanos();
        Focusable = true;
        FontFamily = CUi.Fonte;

        _continuar = new CUi.Botao(T("Continuar"), true, Fechar) { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 48 };
        var nomes = Enumerable.Range(0, _divisoes).Select(NomeDivisao).ToList();
        var (fila, marcar) = CUi.Abas(nomes, d => { MostrarDivisao(d); _tiques = 0; AtualizarBotao(); }, _divisao);
        _filaAbas = fila;
        _marcarAba = marcar;
        var (vistas, _) = CUi.Escolha(new[] { T("Tabela"), T("Gols"), T("Últimas 5") }, 0, v => { _vista = v; Refazer(); });
        foreach (var c in ((Panel)vistas).Children) { c.MinHeight = 34; if (c is Border b) b.Padding = new Thickness(12, 4); }
        vistas.Margin = new Thickness(12, 0, 12, 8);
        var topo = new StackPanel { Children = { fila, vistas } };
        var corpo = new DockPanel();
        // as abas das divisoes e as vistas nas duas peles (no Retro, os botoes do Windows 95)
        DockPanel.SetDock(topo, Dock.Top);
        corpo.Children.Add(topo);
        corpo.Children.Add(new ScrollViewer { Content = _tabela, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        string titulo = rodada > 0 ? T("Classificação · {0}ª rodada", rodada) : T("Classificação");
        Content = new CUi.Folha(titulo, corpo, _continuar, inteira: true, rolar: false, fechar: Fechar);
        Refazer();

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
        AttachedToVisualTree += (_, _) =>
        {
            Focus();
            // a aba da divisao escolhida a vista (a fila rola de lado)
            Dispatcher.UIThread.Post(() =>
            {
                if (_filaAbas is ScrollViewer { Content: Panel p } && _divisao < p.Children.Count) p.Children[_divisao].BringIntoView();
            }, DispatcherPriority.Loaded);
        };
        DetachedFromVisualTree += (_, _) => _relogio?.Stop();
    }

    // ---------------------------------------------------------------- dados
    bool Humana(Equipa e) => _treinadores.TryGetValue(e.Treinador, out var t) && t.Humano != 0;

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

    // as cores das zonas (as do kit: o destaque, o bom e o erro de cada pele)
    // (no Retro, as da classificacao do computador: TelaClassificacaoRetro)
    static IBrush CorCampeao => CUi.Retro ? R.Campeao : TemaVerde.Destaque;
    static IBrush CorSobe => CUi.Retro ? R.Sobe : TemaVerde.Verde;
    static IBrush CorDesce => CUi.Retro ? R.Desce : TemaVerde.VermelhoForte;

    // a zona da posicao i (0 = 1o) numa divisao de n equipas (como a do Moderno)
    IBrush? Zona(int d, int i, int n)
    {
        int k = _regras.SobemDescem;
        if (d >= 4) return null;
        if (d == 0 && i == 0) return CorCampeao;
        if (d > 0 && i < k) return CorSobe;
        if (i >= n - k) return CorDesce;
        return null;
    }

    // as ultimas 5 do campeonato (B07 = 0, ja jogadas), da mais antiga a mais recente
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

    // ---------------------------------------------------------------- a tabela
    // as colunas de cada vista: a barra da zona, a posicao, a equipa (*) e os numeros
    string Colunas => _vista switch
    {
        0 => CUi.Retro ? "4,26,*,30,24,24,24,24,52" : "4,26,*,30,24,24,24,24,36",
        1 => "4,26,*,30,24,36,36,38",
        _ => "4,26,*,30,124",
    };

    static string Pts => CUi.Retro ? T("pontos:P") : T("Pts");

    string[] Titulos => _vista switch
    {
        0 => CUi.Retro
            ? new[] { T("pontos:P"), T("J"), T("V"), T("E"), T("D"), T("GM") + ":" + T("GS") }
            : new[] { Pts, T("J"), T("V"), T("E"), T("D"), T("SG") },
        1 => new[] { Pts, T("J"), T("GM"), CUi.Retro ? T("GS") : T("GC"), T("SG") },
        _ => new[] { Pts, T("Últimas 5") },
    };

    void MostrarDivisao(int d)
    {
        _divisao = Math.Clamp(d, 0, Math.Max(_divisoes - 1, 0));
        Refazer();
    }

    void Refazer()
    {
        if (CUi.Retro) { _tabela.Content = Retro(); return; }
        var p = new StackPanel { Spacing = 6, Margin = new Thickness(10, 0, 10, 14) };
        // o cabecalho das colunas, sobre o fundo
        var cab = new Grid { ColumnDefinitions = new ColumnDefinitions(Colunas), Margin = new Thickness(8, 0, 8, 0) };
        var t0 = CUi.Txt(T("Clube"), 11.5, CUi.TituloSecao, FontWeight.Bold);
        t0.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(t0, 2);
        cab.Children.Add(t0);
        var tit = Titulos;
        for (int i = 0; i < tit.Length; i++)
        {
            var t = CUi.Txt(tit[i], 11.5, CUi.TituloSecao, FontWeight.Bold);
            t.HorizontalAlignment = _vista == 2 && i == 1 ? HorizontalAlignment.Center : HorizontalAlignment.Right;
            Grid.SetColumn(t, 3 + i);
            cab.Children.Add(t);
        }
        p.Children.Add(cab);
        var eqs = Equipas(_divisao);
        var linhas = new List<Control>();
        for (int i = 0; i < eqs.Count; i++) linhas.Add(LinhaEquipa(eqs[i], i, eqs.Count));
        if (linhas.Count > 0) p.Children.Add(CUi.Grupo(linhas));
        if (_divisao < 4) p.Children.Add(Legenda());
        _tabela.Content = p;
    }

    Control LinhaEquipa(Equipa e, int i, int n)
    {
        bool humana = Humana(e);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(Colunas), MinHeight = 38 };
        if (Zona(_divisao, i, n) is { } zona)
            g.Children.Add(new Border { Background = zona, Margin = new Thickness(0, 5), CornerRadius = new CornerRadius(CUi.Retro ? 0 : 2) });
        var pos = ListasCelular.Num((i + 1).ToString(), 13, CUi.Fraco, true);
        Grid.SetColumn(pos, 1);
        g.Children.Add(pos);
        var eq = ListasCelular.Eq(e, 14, humana);
        eq.Margin = new Thickness(8, 0, 4, 0);
        eq.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(eq, 2);
        g.Children.Add(eq);
        void Cel(int c, string s, bool forte = false)
        {
            var t = ListasCelular.Num(s, forte ? 15 : 14, forte ? ListasCelular.Forte : null, forte);
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, c);
            g.Children.Add(t);
        }
        // o Distrital nao joga: os numeros dessas equipas sao restos (como no Moderno: tracos)
        bool distrital = _divisao >= 4;
        string N(int v) => distrital ? "–" : v.ToString();
        int pts = EqW(e, EqPontos), v = EqW(e, EqVitorias), em = EqW(e, EqEmpates), de = EqW(e, EqDerrotas);
        int gm = EqW(e, EqGolosPro), gc = EqW(e, EqGolosContra), sg = gm - gc;
        string Sg() => distrital ? "–" : sg > 0 ? "+" + sg : sg.ToString();
        Cel(3, N(pts), true);
        switch (_vista)
        {
            case 0:
                Cel(4, N(v + em + de)); Cel(5, N(v)); Cel(6, N(em)); Cel(7, N(de));
                Cel(8, CUi.Retro ? (distrital ? "–" : gm + ":" + gc) : Sg());
                break;
            case 1:
                Cel(4, N(v + em + de)); Cel(5, N(gm)); Cel(6, N(gc)); Cel(7, Sg());
                break;
            default:
            {
                var ult = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var r = Ultimas(e);
                for (int k = 0; k < 5 - r.Count; k++) ult.Children.Add(Resultado(null));
                foreach (var x in r) ult.Children.Add(Resultado(x));
                Grid.SetColumn(ult, 4);
                g.Children.Add(ult);
                break;
            }
        }
        var linha = CUi.Toque(g, AbrirEquipa == null ? null : () => Abrir(e), 40);
        linha.Padding = new Thickness(8, 0);
        if (humana) linha.Background = CUi.Retro ? new SolidColorBrush(Color.FromRgb(255, 255, 176)) : TemaVerde.Humano;
        return linha;
    }

    // ---------------------------------------------------------------- o Retro
    // a tabela da classificacao do computador (TelaClassificacaoRetro) em pe: nos verdes do
    // jogo, com a zebra; o nome da divisao, os titulos das colunas e as posicoes em amarelo; a
    // bandeira nas cores da equipa e o nome em maiusculas, em branco; a equipa do humano em
    // verde-escuro, em negrito; a barra a esquerda marca a zona; as ultimas 5 em bolinhas. As
    // linhas encolhem para caber a divisao toda (20 times) sem rolar. Tocar abre o plantel
    Control Retro()
    {
        var eqs = Equipas(_divisao);
        bool apertado = eqs.Count > 12;
        string cols = _vista switch
        {
            0 => "5,24,22,*,30,26,26,24,26,34",
            1 => "5,24,22,*,30,26,30,30,34",
            _ => "5,24,22,*,30,100",
        };
        string[] tit = _vista switch
        {
            0 => new[] { T("Pts"), T("PJ"), T("VIT"), T("E"), T("DER"), T("SG") },
            1 => new[] { T("Pts"), T("PJ"), T("GM"), T("GC"), T("SG") },
            _ => new[] { T("Pts"), T("Últimas 5") },
        };
        var p = new StackPanel { Margin = new Thickness(6, 2, 8, 10) };
        p.Children.Add(new TextBlock { Text = NomeDivisao(_divisao), FontSize = 17, FontWeight = FontWeight.Bold, Foreground = R.Fraco, Margin = new Thickness(4, 0, 0, 0) });
        var cab = new Grid { ColumnDefinitions = new ColumnDefinitions(cols), Margin = new Thickness(0, 0, 0, 2) };
        var clube = CUi.Txt(T("Clube"), 11.5, R.Fraco);
        Grid.SetColumn(clube, 3);
        cab.Children.Add(clube);
        for (int i = 0; i < tit.Length; i++)
        {
            var t = CUi.Txt(tit[i], 11.5, R.Fraco);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(t, 4 + i);
            cab.Children.Add(t);
        }
        p.Children.Add(cab);
        for (int i = 0; i < eqs.Count; i++) p.Children.Add(LinhaRetro(eqs[i], i, eqs.Count, cols, apertado));
        if (_divisao < 4) p.Children.Add(LegendaRetro());
        return p;
    }

    Control LinhaRetro(Equipa e, int i, int n, string cols, bool apertado)
    {
        bool humana = Humana(e);
        double tam = apertado ? 12 : 14, h = apertado ? 22 : 32;
        var g = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(cols), Height = h,
            Background = humana ? R.Humano : i % 2 == 1 ? R.Zebra : R.Fundo,
        };
        if (Zona(_divisao, i, n) is { } zona) g.Children.Add(new Border { Background = zona, Margin = new Thickness(0, 2, 1, 2) });
        void Cel(int c, string t, IBrush cor, bool forte = false, HorizontalAlignment al = HorizontalAlignment.Center)
        {
            var x = CUi.Txt(t, tam, cor, forte ? FontWeight.Bold : FontWeight.Normal);
            x.HorizontalAlignment = al;
            x.VerticalAlignment = VerticalAlignment.Center;
            if (c == 1) x.Margin = new Thickness(0, 0, 4, 0);
            Grid.SetColumn(x, c);
            g.Children.Add(x);
        }
        Cel(1, (i + 1).ToString(), R.Fraco, false, HorizontalAlignment.Right);
        var band = new Pintor((dc, t) => TelaClassificacaoRetro.Bandeira(dc, e, new Rect(t))) { Width = 14, Height = Math.Round(h * 0.6), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        Grid.SetColumn(band, 2);
        g.Children.Add(band);
        var nome = CUi.Txt(Dlg.Texto(e.NomeCurto).ToUpperInvariant(), tam, R.Texto, humana ? FontWeight.Bold : FontWeight.Normal);
        nome.VerticalAlignment = VerticalAlignment.Center;
        nome.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(nome, 3);
        g.Children.Add(nome);
        int pts = EqW(e, EqPontos), v = EqW(e, EqVitorias), em = EqW(e, EqEmpates), de = EqW(e, EqDerrotas);
        int gm = EqW(e, EqGolosPro), gc = EqW(e, EqGolosContra), sg = gm - gc;
        string Sg = sg > 0 ? "+" + sg : sg.ToString();
        Cel(4, pts.ToString(), R.Texto, true);
        switch (_vista)
        {
            case 0:
                Cel(5, (v + em + de).ToString(), R.Texto); Cel(6, v.ToString(), R.Texto); Cel(7, em.ToString(), R.Texto);
                Cel(8, de.ToString(), R.Texto); Cel(9, Sg, R.Texto);
                break;
            case 1:
                Cel(5, (v + em + de).ToString(), R.Texto); Cel(6, gm.ToString(), R.Texto); Cel(7, gc.ToString(), R.Texto); Cel(8, Sg, R.Texto);
                break;
            default:
            {
                var ult = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var rs = Ultimas(e);
                for (int k = 0; k < 5; k++)
                {
                    int j = k - (5 - rs.Count);
                    ult.Children.Add(BolaRetro(j < 0 ? null : rs[j], 13));
                }
                Grid.SetColumn(ult, 5);
                g.Children.Add(ult);
                break;
            }
        }
        var cl = new Clicavel { Child = new Border { Child = g, BorderBrush = R.Fio, BorderThickness = new Thickness(0, 0, 0, 1) }, Background = Brushes.Transparent, CorFoco = null };
        if (AbrirEquipa != null) cl.Clique += () => Abrir(e);
        return cl;
    }

    // uma bolinha das ultimas 5 (com o contorno escuro, para aparecer no verde)
    static Control BolaRetro(int? r, double t) => new Ellipse
    {
        Width = t, Height = t, StrokeThickness = 1, Stroke = R.Contorno.Brush, VerticalAlignment = VerticalAlignment.Center,
        Fill = r switch { > 0 => R.Vitoria, 0 => R.Empate, < 0 => R.Derrota, _ => R.NaoJogou },
    };

    // a legenda: as zonas e, nas ultimas 5, os resultados
    Control LegendaRetro()
    {
        var w = new WrapPanel { ItemSpacing = 14, LineSpacing = 4, Margin = new Thickness(4, 8, 4, 0) };
        Control Item(Control c, string s)
        {
            c.VerticalAlignment = VerticalAlignment.Center;
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { c, CUi.Txt(s, 12, R.Legenda_) } };
        }
        Control Barra(IBrush c) => new Border { Width = 5, Height = 14, Background = c };
        if (_divisao == 0) w.Children.Add(Item(Barra(CorCampeao), T("Campeão")));
        else w.Children.Add(Item(Barra(CorSobe), T("Sobe de divisão")));
        w.Children.Add(Item(Barra(CorDesce), T("Desce de divisão")));
        if (_vista == 2)
        {
            w.Children.Add(Item(BolaRetro(1, 12), T("Vitória")));
            w.Children.Add(Item(BolaRetro(0, 12), T("Empate")));
            w.Children.Add(Item(BolaRetro(-1, 12), T("Derrota")));
        }
        return w;
    }

    static Action<Equipa, bool, Action>? AbrirEquipa => TelaClassificacao.AbrirEquipa;

    // o plantel da equipa tocada (seg06:07f4), com o relogio parado; ao fechar ele continua
    void Abrir(Equipa e)
    {
        if (_fechou || AbrirEquipa is not { } abrir) return;
        _relogio?.Stop();
        abrir(e, _relogio != null, () => { if (!_fechou) { _relogio?.Start(); Focus(); } });
    }

    // um resultado: verde (vitoria), cinza (empate), vermelho (derrota); null = ainda nao jogou
    static Control Resultado(int? r)
    {
        const double t = 20;
        IBrush cor = r switch { > 0 => CUi.Retro ? CUi.Bom : TemaVerde.VerdeForte, 0 => CUi.Retro ? W95.Cinzento : TemaVerde.CinzaForte, < 0 => CUi.Retro ? W95.Vermelho : TemaVerde.VermelhoForte, _ => CUi.Fraco };
        var g = new Grid { Width = t, Height = t, VerticalAlignment = VerticalAlignment.Center };
        if (r is not int v)
        {
            g.Children.Add(new Ellipse { Width = t - 6, Height = t - 6, Stroke = cor, StrokeThickness = 1.5 });
            return g;
        }
        g.Children.Add(CUi.Retro ? new Rectangle { Width = t - 2, Height = t - 2, Fill = cor } : new Ellipse { Width = t, Height = t, Fill = cor });
        string d = v > 0 ? "M5.5,10.5 L8.8,13.6 L14.5,7" : v == 0 ? "M6,10 L14,10" : "M6.5,6.5 L13.5,13.5 M13.5,6.5 L6.5,13.5";
        g.Children.Add(new Path { Data = Geometry.Parse(d), Stroke = Brushes.White, StrokeThickness = 2, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Width = t, Height = t });
        return g;
    }

    Control Legenda()
    {
        var w = new WrapPanel { ItemSpacing = 16, LineSpacing = 6, Margin = new Thickness(6, 4, 6, 0) };
        IBrush texto = CUi.Retro ? W95.Branco : CUi.Fraco;
        Control Item(Control ic, string s)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            ic.VerticalAlignment = VerticalAlignment.Center;
            p.Children.Add(ic);
            p.Children.Add(CUi.Txt(s, 12.5, texto));
            return p;
        }
        Control Barra(IBrush c) => new Border { Width = 4, Height = 16, Background = c, CornerRadius = new CornerRadius(CUi.Retro ? 0 : 2) };
        if (_divisao == 0) w.Children.Add(Item(Barra(CorCampeao), T("Campeão")));
        else w.Children.Add(Item(Barra(CorSobe), T("Sobe de divisão")));
        w.Children.Add(Item(Barra(CorDesce), T("Desce de divisão")));
        if (_vista == 2)
        {
            w.Children.Add(Item(Resultado(1), T("Vitória")));
            w.Children.Add(Item(Resultado(0), T("Empate")));
            w.Children.Add(Item(Resultado(-1), T("Derrota")));
        }
        return w;
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
            case Key.Left when _divisao > 0:
            case Key.Right when _divisao < _divisoes - 1:
                MostrarDivisao(_divisao + (e.Key == Key.Left ? -1 : 1));
                _marcarAba?.Invoke(_divisao);
                _tiques = 0;
                AtualizarBotao();
                break;
            default:
                base.OnKeyDown(e);
                return;
        }
        e.Handled = true;
    }

    void AtualizarBotao() => _continuar.Texto = _relogio != null ? T("Continuar ({0})", 10 - _tiques) : T("Continuar");

    void Fechar()
    {
        if (_fechou) return;
        _fechou = true;
        _relogio?.Stop();
        Fechou?.Invoke();
    }
}
