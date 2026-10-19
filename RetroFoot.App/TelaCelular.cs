using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Platform;
using Avalonia.Threading;
using RetroFoot.Motor.Codec;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// Prova: a tela da equipa no celular, no padrao atual de apps (Material 3), com os dados de
// um save real (SaveCodec do launcher). A cor do time e a cor de destaque.
//  - topo: escudo, time, tecnico, divisao, ano, dinheiro e moral;
//  - elenco em cartoes, agrupado por setor; tocar num jogador abre a gaveta dele;
//  - as abas do time (o painel da direita do original): deitado ficam ao lado do elenco, como
//    no original; em pe viram pilulas que abrem na gaveta;
//  - embaixo, a barra de navegacao com o menu do jogo; cada item abre a gaveta com o submenu.
public sealed class TelaCelular : UserControl
{
    // ---------------------------------------------------------------- paleta (Material 3)
    static readonly IBrush Fundo = Hex(0xF4F5F9), Superficie = Brushes.White, Texto1 = Hex(0x1B1B21),
        Texto2 = Hex(0x5E6170), Linha = Hex(0xE4E5EC), Barra = Hex(0xFFFFFF), Escuro = Hex(0x2B2C33);
    static readonly Dictionary<string, (IBrush Fundo, IBrush Letra, string Nome)> Setores = new()
    {
        ["G"] = (Hex(0xFFE7B3), Hex(0x7A4D00), "Goleiros"),
        ["D"] = (Hex(0xD6E4FF), Hex(0x0B3F94), "Defensores"),
        ["M"] = (Hex(0xD4F3DC), Hex(0x13652F), "Meio-campistas"),
        ["A"] = (Hex(0xFFDAD6), Hex(0x93000A), "Atacantes"),
    };

    // o menu do jogo (TprepareGameDlg.MainMenu1 no jogo classico), com icones Material
    static readonly (string Menu, string Icone, string[] Itens)[] Menu =
    {
        ("RetroFoot", "M3 18h18v-2H3v2zm0-5h18v-2H3v2zm0-7v2h18V6H3z",
            new[] { "Opções...", "Sair sem salvar", "menu: Sair", "Sobre..." }),
        ("Escalar", "M4 11h5V5H4v6zm0 7h5v-6H4v6zm6 0h5v-6h-5v6zm6 0h5v-6h-5v6zm-6-7h5V5h-5v6zm6-6v6h5V5h-5z",
            new[] { "3-3-4", "3-4-3", "4-2-4", "4-3-3", "4-4-2", "4-5-1", "5-2-3", "5-3-2", "5-4-1", "5-5-0", "6-3-1", "6-4-0", "5-0-5", "-", "Automático", "Melhores" }),
        ("Time", "M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4z",
            new[] { "Imprimir", "Pedir / pagar empréstimo...", "Estádio...", "Histórico..." }),
        ("Jogador", "M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z",
            new[] { "Vender", "Jogadores sob observação...", "Procurar...", "-", "Últimas transferências...", "Contratações..." }),
        ("Campeonato", "M19 5h-2V3H7v2H5c-1.1 0-2 .9-2 2v1c0 2.55 1.92 4.63 4.39 4.94.63 1.5 1.98 2.63 3.61 2.96V19H7v2h10v-2h-4v-3.1c1.63-.33 2.98-1.46 3.61-2.96C19.08 12.63 21 10.55 21 8V7c0-1.1-.9-2-2-2zM5 8V7h2v3.82C5.84 10.4 5 9.3 5 8zm14 0c0 1.3-.84 2.4-2 2.82V7h2v1z",
            new[] { "Classificação...", "Artilheiros...", "Calendário...", "-", "Últimos vencedores...", "Maiores artilheiros..." }),
        ("Técnico", "M19 3h-4.18C14.4 1.84 13.3 1 12 1c-1.3 0-2.4.84-2.82 2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-7 0c.55 0 1 .45 1 1s-.45 1-1 1-1-.45-1-1 .45-1 1-1zm0 4c1.66 0 3 1.34 3 3s-1.34 3-3 3-3-1.34-3-3 1.34-3 3-3zm6 12H6v-1.4c0-2 4-3.1 6-3.1s6 1.1 6 3.1V19z",
            new[] { "História...", "Gestão de técnicos...", "Ranking...", "-", "Perfil..." }),
    };
    static readonly string[] Abas = { "Jogo", "Finanças", "Escalação", "Adversário", "Patrocínio" };

    // A moeda do jogo: "R$" (real), "$" (dolar), "€" (euro) ou "£" (libra). Na prova, fixa no
    // real; no jogo vem das configuracoes.
    public static string Moeda = "R$";

    // so na prova: liga o cenario de teste dos indicadores (Android: debug.retrofoot.cenario=1)
    public static bool Cenario;
    static readonly CultureInfo Br = new("pt-BR");

    static readonly Dictionary<string, string> NomeDaPosicao = new()
    { ["G"] = "Goleiro", ["D"] = "Defensor", ["M"] = "Meio-campista", ["A"] = "Atacante" };

    // comportamento (SaveCodec.ComportamentoLabels): cor de fundo e de letra da etiqueta
    static readonly (IBrush Fundo, IBrush Letra)[] CorComportamento =
    {
        (Hex(0xD4F3DC), Hex(0x13652F)),   // Fair Play
        (Hex(0xDDF1FF), Hex(0x0B4F80)),   // Cordeirinho
        (Hex(0xE6E8FF), Hex(0x2E3A9E)),   // Cavalheiro
        (Hex(0xFFF0C2), Hex(0x7A5600)),   // Caneleiro
        (Hex(0xFFE0CC), Hex(0x8A3A00)),   // Caceteiro
        (Hex(0xFFDAD6), Hex(0x93000A)),   // Sarrafeiro
    };

    // siglas do comportamento, como na tela de Procura do jogo
    static readonly string[] SiglaComportamento = { "FP", "COR", "CAV", "CAN", "CAC", "SAR" };

    // t14 do jogador no save (2 bytes depois da forca + 10): 1 = salario ja revisto nesta
    // temporada (o "+" da lista do jogo; o jogador nao pode ser vendido)
    bool SalarioAcertado(SavePlayer p) =>
        _save != null && p.ForcaOffsetInFile > 0 && p.ForcaOffsetInFile + 12 < _save.RawBytes.Length && _save.RawBytes[p.ForcaOffsetInFile + 12] != 0;

    static readonly Dictionary<string, string> Paises = LerPaises();

    static Dictionary<string, string> LerPaises()
    {
        var d = new Dictionary<string, string>();
        try
        {
            using var r = new StreamReader(AssetLoader.Open(new Uri("avares://RetroFoot.App/Assets/paises.tsv")));
            for (string? l; (l = r.ReadLine()) != null;)
            {
                var c = l.Split('\t');
                if (c.Length == 2) d[c[0]] = c[1];
            }
        }
        catch { }
        return d;
    }

    readonly Grid _corpo = new();
    readonly Grid _gaveta = new() { IsVisible = false };
    readonly Border _folha = new();
    readonly Border _aviso = new();
    readonly TextBlock _avisoTexto = new();
    readonly List<Border> _linhas = new();
    readonly List<Border> _botoesAba = new();
    readonly ContentControl _conteudoAba = new();
    Control? _elenco, _painel, _pilulas;
    SaveTeam? _time;
    SaveFile? _save;
    Control? _linhaAberta;      // a linha arrastada para a esquerda (mostra o salario)
    SavePlayer? _jogador;
    IBrush _cor = Hex(0x1A56DB), _sobreCor = Brushes.White;
    bool? _deitado;
    double _arrasteY = double.NaN;
    DispatcherTimer? _timerAviso;

    public TelaCelular()
    {
        FontFamily = new FontFamily("fonts:Inter#Inter");
        Background = Fundo;
        var raiz = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        try
        {
            var tmp = Path.Combine(Path.GetTempPath(), "prova-20.e98");
            using (var r = AssetLoader.Open(new Uri("avares://RetroFoot.App/Assets/Saves/20.e98")))
            using (var f = File.Create(tmp)) r.CopyTo(f);
            var save = SaveCodec.Read(tmp);
            _save = save;
            _time = save.Teams.FirstOrDefault(t => t.Id == save.HumanoDaVez) ?? throw new InvalidDataException("sem time humano");
            // a cor de destaque e a mais forte das duas cores do time; a outra fica por cima dela
            (int forte, int fraca) = Saturacao(_time.CorLetra) >= Saturacao(_time.CorFundo)
                ? (_time.CorLetra, _time.CorFundo) : (_time.CorFundo, _time.CorLetra);
            _cor = Cor(forte);
            _sobreCor = Cor(fraca);

            raiz.Children.Add(Topo(save.Tecnico(_time.TecnicoId)?.Nome ?? "", save.Ano));
            _pilulas = Pilulas();
            Grid.SetRow(_pilulas, 1);
            raiz.Children.Add(_pilulas);
            if (Cenario || Environment.GetEnvironmentVariable("RETROFOOT_INDICADORES") == "1")
            {
                // so na prova, para ver como cabe na linha: estrela + lesionado + 2 amarelos
                foreach (var p in _time.Players.Where(p => p.Nome is "Andrew Impey" or "Juergen Sommer"))
                {
                    p.Estrela = true;
                    p.JogosLesionado = 3;
                    p.AmarelosAcumulados = 2;
                }
                var ps = SaveCodec.OrdemDoJogo(_time.Players);
                ps[2].Suspensao = 2;
            }
            _elenco = Elenco(SaveCodec.OrdemDoJogo(_time.Players));
            _painel = Painel();
            Grid.SetRow(_corpo, 2);
            raiz.Children.Add(_corpo);
            var nav = Navegacao();
            Grid.SetRow(nav, 3);
            raiz.Children.Add(nav);
            raiz.Children.Add(Aviso());
            MontarGaveta();
            Grid.SetRowSpan(_gaveta, 4);
            raiz.Children.Add(_gaveta);
            _corpo.SizeChanged += (_, e) => Arrumar(e.NewSize.Width > e.NewSize.Height * 1.05);
            MostrarAba("Jogo");
        }
        catch (Exception e)
        {
            raiz.Children.Add(new TextBlock { Text = "Não consegui ler o save: " + e.Message, Margin = new Thickness(16) });
        }
        Content = raiz;
        // o Voltar do Android fecha a gaveta antes de sair do app
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is TopLevel tl)
            {
                tl.BackRequested += (_, e) => { if (_gaveta.IsVisible) { FecharGaveta(); e.Handled = true; } };
            }
        };
    }

    // ---------------------------------------------------------------- topo
    Control Topo(string tecnico, int ano)
    {
        // o logo do time, se houver; senao a bandeira do pais, como antes
        var escudo = Escudos.Controle(Escudos.Escudo(_time!.Pais, _time.Nome, _time.NomeCurto), 56) ?? Bandeira(_time.Pais, 56, true);
        var nomes = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
        nomes.Children.Add(new TextBlock { Text = Titulo(_time.Nome), FontSize = 21, FontWeight = FontWeight.SemiBold, Foreground = _sobreCor, TextTrimming = TextTrimming.CharacterEllipsis });
        nomes.Children.Add(new TextBlock { Text = $"{tecnico} · {T(_time.Divisao)}", FontSize = 14, Foreground = _sobreCor, Opacity = 0.85 });
        var linha1 = new DockPanel();
        linha1.Children.Add(escudo);
        linha1.Children.Add(nomes);

        var stats = new UniformGrid { Rows = 1, Margin = new Thickness(0, 16, 0, 0) };
        stats.Children.Add(Estatistica(T("Temporada"), ano.ToString()));
        stats.Children.Add(Estatistica(T("Em caixa"), Dinheiro(_time.Verba, curto: true)));
        var moral = new StackPanel { Spacing = 6 };
        moral.Children.Add(new TextBlock { Text = T("Moral"), FontSize = 12, Foreground = _sobreCor, Opacity = 0.8 });
        moral.Children.Add(new ProgressBar { Minimum = 0, Maximum = 1, Value = Math.Clamp(_time.Moral / 2, 0, 1), Height = 8, MinHeight = 8, MinWidth = 0, CornerRadius = new CornerRadius(4), Foreground = Hex(0xFFD54F), Background = Hex(0xFFFFFF, 0x40), Margin = new Thickness(0, 4, 0, 0) });
        stats.Children.Add(moral);

        var s = new StackPanel { Margin = new Thickness(20, 18, 20, 18) };
        s.Children.Add(linha1);
        s.Children.Add(stats);
        return new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(((ISolidColorBrush)_cor).Color, 0), new GradientStop(Escurecer(((ISolidColorBrush)_cor).Color, 0.55), 1) },
            },
            CornerRadius = new CornerRadius(0, 0, 24, 24),
            Child = s,
        };
    }

    Control Estatistica(string rotulo, string valor)
    {
        var s = new StackPanel { Spacing = 2 };
        s.Children.Add(new TextBlock { Text = rotulo, FontSize = 12, Foreground = _sobreCor, Opacity = 0.8 });
        s.Children.Add(new TextBlock { Text = valor, FontSize = 17, FontWeight = FontWeight.SemiBold, Foreground = _sobreCor });
        return s;
    }

    // as abas do time em pe: pilulas que abrem a gaveta
    Control Pilulas()
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 12, 16, 4) };
        foreach (var aba in Abas)
        {
            var b = new Border
            {
                Padding = new Thickness(16, 9), CornerRadius = new CornerRadius(18),
                Background = Superficie, BorderBrush = Linha, BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = T(aba), FontSize = 14, FontWeight = FontWeight.Medium, Foreground = Texto1 },
            };
            b.Tapped += (_, _) => AbrirGaveta(T(aba), ConteudoAba(aba));
            s.Children.Add(b);
        }
        return new ScrollViewer { Content = s, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    // deitado: elenco | painel do time (como o original); em pe: so o elenco (abas nas pilulas)
    void Arrumar(bool deitado)
    {
        if (_deitado == deitado || _elenco == null || _painel == null) return;
        _deitado = deitado;
        _corpo.Children.Clear();
        _corpo.ColumnDefinitions = deitado ? new ColumnDefinitions("11*,9*") : new ColumnDefinitions("*");
        _corpo.Children.Add(_elenco);
        if (deitado)
        {
            Grid.SetColumn(_painel, 1);
            _corpo.Children.Add(_painel);
        }
        if (_pilulas != null) _pilulas.IsVisible = !deitado;
    }

    // ---------------------------------------------------------------- elenco
    Control Elenco(List<SavePlayer> jogadores)
    {
        var lista = new StackPanel { Margin = new Thickness(16, 8, 16, 16), Spacing = 6 };
        foreach (var grupo in jogadores.GroupBy(p => p.Posicao))
        {
            var (_, _, nome) = Setores.GetValueOrDefault(grupo.Key, (Linha, Texto2, grupo.Key));
            var titulo = new DockPanel { Margin = new Thickness(4, 10, 10, 2) };
            var siglas = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (sigla, larg) in new[] { ("Comp", 40.0), ("Força", 44.0) })
                siglas.Children.Add(new TextBlock { Text = T(sigla), Width = larg, TextAlignment = TextAlignment.Center, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Texto2 });
            DockPanel.SetDock(siglas, Dock.Right);
            titulo.Children.Add(siglas);
            titulo.Children.Add(new TextBlock { Text = $"{T(nome).ToUpperInvariant()}  ·  {grupo.Count()}", FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Texto2, LetterSpacing = 0.6 });
            lista.Children.Add(titulo);
            var cartao = new StackPanel();
            int i = 0;
            foreach (var p in grupo)
            {
                if (i++ > 0) cartao.Children.Add(new Border { Height = 1, Background = Linha, Margin = new Thickness(44, 0, 0, 0) });
                cartao.Children.Add(LinhaJogador(p));
            }
            lista.Children.Add(new Border { Background = Superficie, CornerRadius = new CornerRadius(16), ClipToBounds = true, Child = cartao });
        }
        return new ScrollViewer { Content = lista, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, IsScrollInertiaEnabled = true };
    }

    // uma linha por jogador (o dobro cabe na tela): posicao, bandeira, nome e indicadores,
    // e Nota, RL e Forca so com os numeros (as siglas ficam no titulo do setor)
    Border LinhaJogador(SavePlayer p)
    {
        var (sf, sl, _) = Setores.GetValueOrDefault(p.Posicao, (Linha, Texto2, ""));
        var badge = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = sf, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = p.Posicao.Length == 1 ? Posicao(p.Posicao[0]) : p.Posicao, FontSize = 12, FontWeight = FontWeight.Bold, Foreground = sl, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        // bandeira | nome (encolhe com "...") | estrela e indicadores (sempre inteiros,
        // encostados nas colunas): os icones nunca passam por cima dos numeros
        var meio = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        if (p.Pais.Length > 0) meio.Children.Add(Bandeira(p.Pais, 16, false));
        var tn = new TextBlock { Text = p.Nome, FontSize = 15, FontWeight = FontWeight.Medium, Foreground = Texto1, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 4, 0) };
        Grid.SetColumn(tn, 1);
        meio.Children.Add(tn);
        var marcas = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        if (p.Estrela) marcas.Children.Add(Icone("M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z", 13, Hex(0xF5B400)));
        foreach (var ind in Indicadores(p)) marcas.Children.Add(ind);
        Grid.SetColumn(marcas, 2);
        meio.Children.Add(marcas);
        var numeros = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        numeros.Children.Add(SiglaComp(p, 40));
        numeros.Children.Add(new TextBlock { Text = p.Forca.ToString(Br), Width = 44, TextAlignment = TextAlignment.Center, FontSize = 16, FontWeight = FontWeight.Bold, Foreground = _cor, VerticalAlignment = VerticalAlignment.Center });
        var g = new DockPanel { Margin = new Thickness(10, 0, 8, 0), Height = 36 };
        DockPanel.SetDock(badge, Dock.Left);
        DockPanel.SetDock(numeros, Dock.Right);
        g.Children.Add(badge);
        g.Children.Add(numeros);
        g.Children.Add(meio);

        // a frente (a linha) desliza para a esquerda e mostra o salario por tras
        var frente = new Border { Background = Superficie, Child = g };
        var tras = Salario(p);
        var linha = new Grid { ClipToBounds = true };
        linha.Children.Add(tras);
        linha.Children.Add(frente);
        double ini = double.NaN, iniY = 0, desloc = 0;
        bool arrastando = false, arrastou = false;
        void Mover(double x, bool animar)
        {
            frente.Transitions = animar ? Deslizar() : null;
            frente.RenderTransform = TransformOperations.Parse($"translateX({x:0}px)");
            desloc = x;
        }
        linha.PointerPressed += (_, e) => { ini = e.GetPosition(linha).X - desloc; iniY = e.GetPosition(linha).Y; arrastando = false; arrastou = false; };
        linha.PointerMoved += (_, e) =>
        {
            if (double.IsNaN(ini)) return;
            var pt = e.GetPosition(linha);
            double dx = pt.X - ini;
            if (!arrastando)
            {
                if (Math.Abs(dx - desloc) < 10 || Math.Abs(dx - desloc) < Math.Abs(pt.Y - iniY)) return;
                arrastando = arrastou = true;
                e.Pointer.Capture(linha);
                if (_linhaAberta != null && _linhaAberta != linha) FecharLinha(_linhaAberta);
            }
            Mover(Math.Clamp(dx, -LarguraSalario, 0), false);
        };
        linha.PointerReleased += (_, e) =>
        {
            if (arrastando)
            {
                e.Pointer.Capture(null);
                bool abre = desloc < -LarguraSalario / 2;
                Mover(abre ? -LarguraSalario : 0, true);
                _linhaAberta = abre ? linha : null;
            }
            ini = double.NaN;
            arrastando = false;
        };
        linha.Tag = (Action)(() => Mover(0, true));
        var b = new Border { Background = Brushes.Transparent, Child = linha };
        b.Tapped += (_, _) =>
        {
            if (arrastou) { arrastou = false; return; }
            if (_linhaAberta != null) { FecharLinha(_linhaAberta); _linhaAberta = null; return; }
            _jogador = p;
            foreach (var l in _linhas) l.Background = Brushes.Transparent;
            AbrirGaveta(null, Jogador(p), (T("Vender"), true), (T("Observar"), false));
        };
        _linhas.Add(b);
        return b;
    }

    const double LarguraSalario = 250;

    static void FecharLinha(Control linha)
    {
        if (linha.Tag is Action fechar) fechar();
    }

    // o que aparece por tras da linha arrastada: Nota, RL (risco de lesao), o salario e se ele
    // ja foi acertado na temporada
    Control Salario(SavePlayer p)
    {
        bool acertado = SalarioAcertado(p);
        var g = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var (v, r) in new[] { (p.Nota.ToString(), "Nota"), (p.Lesao.ToString(), "RL") })
        {
            var c = new StackPanel { Width = 34, VerticalAlignment = VerticalAlignment.Center };
            c.Children.Add(new TextBlock { Text = v, FontSize = 15, FontWeight = FontWeight.Bold, Foreground = _sobreCor, HorizontalAlignment = HorizontalAlignment.Center });
            c.Children.Add(new TextBlock { Text = T(r), FontSize = 10, Foreground = _sobreCor, Opacity = 0.8, HorizontalAlignment = HorizontalAlignment.Center });
            DockPanel.SetDock(c, Dock.Left);
            g.Children.Add(c);
        }
        var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1, Margin = new Thickness(10, 0, 0, 0) };
        s.Children.Add(new TextBlock { Text = Valor(p.Salario), FontSize = 15, FontWeight = FontWeight.Bold, Foreground = _sobreCor });
        var estado = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        estado.Children.Add(Icone(acertado
            ? "M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z"
            : "M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zM12 20c-4.42 0-8-3.58-8-8s3.58-8 8-8 8 3.58 8 8-3.58 8-8 8zm.5-13H11v6l5.25 3.15.75-1.23-4.5-2.67z",
            12, _sobreCor));
        estado.Children.Add(new TextBlock { Text = T(acertado ? "Salário acertado" : "Não acertado"), FontSize = 11.5, Foreground = _sobreCor, Opacity = 0.9 });
        s.Children.Add(estado);
        g.Children.Add(s);
        return new Border
        {
            Width = LarguraSalario, HorizontalAlignment = HorizontalAlignment.Right,
            Background = acertado ? _cor : Hex(0x5E6170), Padding = new Thickness(8, 0, 12, 0), Child = g,
        };
    }

    // a sigla do comportamento, na cor dele
    static Control SiglaComp(SavePlayer p, double largura)
    {
        if (p.Comportamento < 0 || p.Comportamento >= SiglaComportamento.Length) return new Border { Width = largura };
        var (cf, cl) = CorComportamento[p.Comportamento];
        return new Border
        {
            Width = largura, VerticalAlignment = VerticalAlignment.Center,
            Child = new Border
            {
                Background = cf, CornerRadius = new CornerRadius(6), Padding = new Thickness(4, 1), HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock { Text = T(SiglaComportamento[p.Comportamento]), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = cl },
            },
        };
    }

    Control Numero(string valor, string rotulo, bool destaque)
    {
        var s = new StackPanel { Width = destaque ? 48 : 36 };
        s.Children.Add(new TextBlock { Text = valor, FontSize = destaque ? 20 : 17, FontWeight = destaque ? FontWeight.Bold : FontWeight.SemiBold, Foreground = destaque ? _cor : Texto1, HorizontalAlignment = HorizontalAlignment.Center });
        s.Children.Add(new TextBlock { Text = rotulo, FontSize = 11, Foreground = Texto2, HorizontalAlignment = HorizontalAlignment.Center });
        return s;
    }

    // ao lado do nome: amarelos acumulados (1 cartao ou 2 sobrepostos), suspensao e lesao
    static IEnumerable<Control> Indicadores(SavePlayer p)
    {
        if (p.Suspensao > 0)
            yield return Selo("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zM4 12c0-4.42 3.58-8 8-8 1.85 0 3.55.63 4.9 1.69L5.69 16.9C4.63 15.55 4 13.85 4 12zm8 8c-1.85 0-3.55-.63-4.9-1.69L18.31 7.1C19.37 8.45 20 10.15 20 12c0 4.42-3.58 8-8 8z",
                p.Suspensao, Hex(0xFFE4E1), Hex(0xC62828));
        else if (p.AmarelosAcumulados == 1)
            yield return Cartao(Hex(0xFFC107));
        else if (p.AmarelosAcumulados >= 2)
        {
            var dois = new Canvas { Width = 15, Height = 17, VerticalAlignment = VerticalAlignment.Center };
            var c1 = Cartao(Hex(0xE0A800));
            var c2 = Cartao(Hex(0xFFC107));
            Canvas.SetLeft(c1, 0); Canvas.SetTop(c1, 3);
            Canvas.SetLeft(c2, 5); Canvas.SetTop(c2, 0);
            dois.Children.Add(c1);
            dois.Children.Add(c2);
            yield return dois;
        }
        if (p.JogosLesionado > 0)
            yield return Selo("M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-1 11h-4v4h-4v-4H6v-4h4V6h4v4h4v4z",
                p.JogosLesionado, Hex(0xFFEBEE), Hex(0xD32F2F));
    }

    // icone + numero de jogos (suspensao, lesao)
    static Border Selo(string icone, int jogos, IBrush fundo, IBrush cor)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        s.Children.Add(Icone(icone, 13, cor));
        s.Children.Add(new TextBlock { Text = jogos.ToString(), FontSize = 12, FontWeight = FontWeight.Bold, Foreground = cor, VerticalAlignment = VerticalAlignment.Center });
        return new Border { Background = fundo, CornerRadius = new CornerRadius(6), Padding = new Thickness(4, 1), VerticalAlignment = VerticalAlignment.Center, Child = s };
    }

    static Border Cartao(IBrush cor) => new()
    {
        Width = 10, Height = 14, CornerRadius = new CornerRadius(2), Background = cor,
        BorderBrush = Hex(0x000000, 0x30), BorderThickness = new Thickness(0.6), VerticalAlignment = VerticalAlignment.Center,
    };

    Control Jogador(SavePlayer p)
    {
        var (sf, sl, _) = Setores.GetValueOrDefault(p.Posicao, (Linha, Texto2, p.Posicao));
        var s = new StackPanel { Spacing = 16 };

        // cabecalho: a posicao num circulo com a bandeira do pais por cima, e o nome
        var avatar = new Grid { Width = 68, Height = 68 };
        avatar.Children.Add(new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(32), Background = sf, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = p.Posicao, FontSize = 26, FontWeight = FontWeight.Bold, Foreground = sl, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        if (p.Pais.Length > 0)
        {
            var b = Bandeira(p.Pais, 30, true);
            b.HorizontalAlignment = HorizontalAlignment.Right;
            b.VerticalAlignment = VerticalAlignment.Bottom;
            avatar.Children.Add(b);
        }
        var nome = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        nome.Children.Add(new TextBlock { Text = p.Nome, FontSize = 24, FontWeight = FontWeight.Bold, Foreground = Texto1, TextWrapping = TextWrapping.Wrap });
        if (p.Estrela) nome.Children.Add(Icone("M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z", 20, Hex(0xF5B400)));
        var cab = new DockPanel();
        cab.Children.Add(avatar);
        cab.Children.Add(nome);
        s.Children.Add(cab);

        // pais, posicao e comportamento em destaque
        var chips = new WrapPanel { Orientation = Orientation.Horizontal };
        if (p.Pais.Length > 0)
            chips.Children.Add(Chip(Bandeira(p.Pais, 20, false), Paises.GetValueOrDefault(p.Pais, p.Pais), Fundo, Texto1));
        chips.Children.Add(Chip(null, T(NomeDaPosicao.GetValueOrDefault(p.Posicao, p.Posicao)), sf, sl));
        if (p.Comportamento >= 0 && p.Comportamento < SaveCodec.ComportamentoLabels.Length)
        {
            var (cf, cl) = CorComportamento[p.Comportamento];
            chips.Children.Add(Chip(null, T(SaveCodec.ComportamentoLabels[p.Comportamento]), cf, cl));
        }
        if (p.JogosLesionado > 0) chips.Children.Add(Chip(Icone("M19 3H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-1 11h-4v4h-4v-4H6v-4h4V6h4v4h4v4z", 16, Hex(0xD32F2F)),
            p.JogosLesionado == 1 ? T("Lesionado por 1 jogo") : T("Lesionado por {0} jogos", p.JogosLesionado), Hex(0xFFEBEE), Hex(0xB71C1C)));
        if (p.Suspensao > 0) chips.Children.Add(Chip(null, p.Suspensao == 1 ? T("Suspenso por 1 jogo") : T("Suspenso por {0} jogos", p.Suspensao), Hex(0xFFDAD6), Hex(0x93000A)));
        else if (p.AmarelosAcumulados > 0) chips.Children.Add(Chip(new Border { Width = 10, Height = 14, CornerRadius = new CornerRadius(2), Background = Hex(0xFFC107) },
            p.AmarelosAcumulados == 1 ? T("1 amarelo") : T("{0} amarelos", p.AmarelosAcumulados), Hex(0xFFF4CC), Hex(0x6B4E00)));
        s.Children.Add(chips);

        // numeros principais, com os rotulos inteiros
        var tiles = new UniformGrid { Columns = 3 };
        tiles.Children.Add(Bloco(T("Força"), p.Forca.ToString(Br), true));
        tiles.Children.Add(Bloco(T("Nota"), $"{p.Nota}", false));
        tiles.Children.Add(Bloco(T("Risco de lesão"), $"{p.Lesao}", false));
        s.Children.Add(tiles);
        // o salario numa linha so: rotulo, valor e se ja foi acertado
        var sal = new DockPanel { LastChildFill = false };
        var rot = new TextBlock { Text = T("Salário"), FontSize = 13, Foreground = Texto2, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(rot, Dock.Left);
        sal.Children.Add(rot);
        var est = EstadoSalario(SalarioAcertado(p));
        est.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(est, Dock.Right);
        sal.Children.Add(est);
        var valSal = new TextBlock { Text = Valor(p.Salario), FontSize = 17, FontWeight = FontWeight.Bold, Foreground = Texto1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(valSal, Dock.Left);
        sal.Children.Add(valSal);
        s.Children.Add(new Border { Background = Fundo, CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 10), Margin = new Thickness(4), Child = sal });

        s.Children.Add(new TextBlock { Text = T("Histórico"), FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Texto1 });
        var hist = new StackPanel();
        (string, int)[] linhas = { ("Jogos", p.Jogos), ("Gols", p.Gols), ("Cartões amarelos", p.AmarelosAcumulados), ("Cartões vermelhos", p.Expulsoes), ("Lesões", p.Lesoes) };
        foreach (var (r, v) in linhas)
        {
            var d = new DockPanel { Margin = new Thickness(0, 7) };
            var val = new TextBlock { Text = v.ToString(Br), FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Texto1 };
            DockPanel.SetDock(val, Dock.Right);
            d.Children.Add(val);
            d.Children.Add(new TextBlock { Text = T(r), FontSize = 15, Foreground = Texto2 });
            hist.Children.Add(d);
        }
        s.Children.Add(new Border { Background = Fundo, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 6), Child = hist });
        return s;
    }

    static Border Chip(Control? icone, string texto, IBrush fundo, IBrush letra)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        if (icone != null) { icone.VerticalAlignment = VerticalAlignment.Center; s.Children.Add(icone); }
        s.Children.Add(new TextBlock { Text = texto, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = letra, VerticalAlignment = VerticalAlignment.Center });
        return new Border { Background = fundo, CornerRadius = new CornerRadius(16), Padding = new Thickness(12, 7), Margin = new Thickness(0, 0, 8, 8), Child = s };
    }

    Control Bloco(string rotulo, string valor, bool destaque, Control? rodape = null)
    {
        var s = new StackPanel { Spacing = 3 };
        s.Children.Add(new TextBlock { Text = rotulo, FontSize = 12, Foreground = destaque ? _sobreCor : Texto2, Opacity = destaque ? 0.9 : 1, TextWrapping = TextWrapping.Wrap });
        s.Children.Add(new TextBlock { Text = valor, FontSize = 24, FontWeight = FontWeight.Bold, Foreground = destaque ? _sobreCor : Texto1 });
        if (rodape != null) s.Children.Add(rodape);
        return new Border { Background = destaque ? _cor : Fundo, CornerRadius = new CornerRadius(16), Padding = new Thickness(12, 10), Margin = new Thickness(4), Child = s };
    }

    // o "+" do original: o salario ja foi acertado nesta temporada (verde) ou nao (cinza)
    Control EstadoSalario(bool acertado)
    {
        var cor = acertado ? Hex(0x2E7D32) : Texto2;
        var l = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        l.Children.Add(Icone(acertado
            ? "M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z"
            : "M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zM12 20c-4.42 0-8-3.58-8-8s3.58-8 8-8 8 3.58 8 8-3.58 8-8 8zm.5-13H11v6l5.25 3.15.75-1.23-4.5-2.67z",
            13, cor));
        l.Children.Add(new TextBlock { Text = T(acertado ? "Acertado" : "Não acertado"), FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = cor });
        return l;
    }

    // bandeira redonda (circle-flags) com brilho; a do jogo (FLAGS) se nao houver a redonda
    static readonly Dictionary<string, Bitmap?> _bandeiras = new();

    static Control Bandeira(string pais, double tam, bool destaque)
    {
        if (!_bandeiras.TryGetValue(pais, out var bmp))
        {
            try { bmp = new Bitmap(AssetLoader.Open(new Uri($"avares://RetroFoot.App/Assets/Bandeiras/{pais}.png"))); }
            catch { bmp = null; }
            _bandeiras[pais] = bmp;
        }
        var g = new Grid { Width = tam, Height = tam };
        var circulo = new Border { CornerRadius = new CornerRadius(tam / 2), ClipToBounds = true, Background = Linha };
        if (bmp != null) circulo.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
        g.Children.Add(circulo);
        // o brilho: luz em cima a esquerda, sombra embaixo
        g.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(tam / 2),
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0.32, 0.22, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.32, 0.22, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(150, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(25, 255, 255, 255), 0.45),
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.7),
                    new GradientStop(Color.FromArgb(70, 0, 0, 0), 1),
                },
            },
            BorderBrush = destaque ? Hex(0xFFFFFF, 0xCC) : Hex(0x000000, 0x22),
            BorderThickness = new Thickness(destaque ? Math.Max(2, tam / 22) : 0.6),
        });
        return destaque
            ? new Border { CornerRadius = new CornerRadius(tam / 2), BoxShadow = BoxShadows.Parse("0 3 10 0 #40000000"), Child = g }
            : g;
    }

    // ---------------------------------------------------------------- painel do time (deitado)
    Control Painel()
    {
        var abas = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var nome in Abas)
        {
            var t = new TextBlock { Text = T(nome), FontSize = 14, FontWeight = FontWeight.Medium, Foreground = Texto2 };
            var b = new Border { Child = t, Padding = new Thickness(14, 14, 14, 11), BorderThickness = new Thickness(0, 0, 0, 3), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, Tag = nome };
            b.Tapped += (_, _) => MostrarAba(nome);
            _botoesAba.Add(b);
            abas.Children.Add(b);
        }
        var g = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        g.Children.Add(new ScrollViewer { Content = abas, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var sep = new Border { Height = 1, Background = Linha };
        Grid.SetRow(sep, 1);
        g.Children.Add(sep);
        var conteudo = new ScrollViewer { Content = _conteudoAba, Padding = new Thickness(18, 14), IsScrollInertiaEnabled = true };
        Grid.SetRow(conteudo, 2);
        g.Children.Add(conteudo);
        return new Border { Background = Superficie, CornerRadius = new CornerRadius(20), Margin = new Thickness(0, 14, 16, 16), ClipToBounds = true, Child = g };
    }

    void MostrarAba(string nome)
    {
        foreach (var b in _botoesAba)
        {
            bool sel = (string?)b.Tag == nome;
            b.BorderBrush = sel ? _cor : Brushes.Transparent;
            ((TextBlock)b.Child!).Foreground = sel ? _cor : Texto2;
        }
        _conteudoAba.Content = ConteudoAba(nome);
    }

    Control ConteudoAba(string nome)
    {
        var s = new StackPanel { Spacing = 10 };
        switch (nome)
        {
            case "Jogo":
                s.Children.Add(Cartao(T("Próximo jogo"), T("O adversário vem do motor do jogo (ainda não feito nesta prova).")));
                s.Children.Add(Cartao(T("Árbitro"), T("Vem do motor do jogo.")));
                s.Children.Add(Cartao(T("Dinheiro em caixa"), Dinheiro(_time!.Verba)));
                break;
            case "Finanças":
                s.Children.Add(Cartao(T("Dinheiro em caixa"), Dinheiro(_time!.Verba)));
                s.Children.Add(Cartao(T("Folha de salários"), T("{0} por jogo", Valor(_time.Players.Sum(p => (long)p.Salario)))));
                s.Children.Add(Cartao(T("Estádio"), T("{0} lugares", FormatoDinheiro.Numero(5000 * _time.Estadio))));
                break;
            default:
                s.Children.Add(Cartao(T(nome), T("Vem do motor do jogo (ainda não feito nesta prova).")));
                break;
        }
        return s;
    }

    static Control Cartao(string titulo, string texto)
    {
        var s = new StackPanel { Spacing = 4 };
        s.Children.Add(new TextBlock { Text = titulo, FontSize = 13, Foreground = Texto2 });
        s.Children.Add(new TextBlock { Text = texto, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Texto1, TextWrapping = TextWrapping.Wrap });
        return new Border { Background = Fundo, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 12), Child = s };
    }

    // ---------------------------------------------------------------- navegacao (menu do jogo)
    Control Navegacao()
    {
        var g = new UniformGrid { Rows = 1 };
        foreach (var (nome, icone, itens) in Menu)
        {
            var s = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
            var pilula = new Border { Width = 56, Height = 30, CornerRadius = new CornerRadius(15), Background = Brushes.Transparent, Child = Icone(icone, 22, Texto2) };
            s.Children.Add(pilula);
            s.Children.Add(new TextBlock { Text = T(nome), FontSize = 11.5, FontWeight = FontWeight.Medium, Foreground = Texto1, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var b = new Border { Background = Brushes.Transparent, Padding = new Thickness(0, 10, 0, 12), Child = s };
            b.Tapped += (_, _) => AbrirGaveta(T(nome), Itens(T(nome), itens));
            g.Children.Add(b);
        }
        return new Border { Background = Barra, BorderBrush = Linha, BorderThickness = new Thickness(0, 1, 0, 0), Child = g };
    }

    Control Itens(string nome, string[] itens)
    {
        var s = new StackPanel();
        foreach (var item in itens)
        {
            if (item == "-") { s.Children.Add(new Border { Height = 1, Background = Linha, Margin = new Thickness(0, 6) }); continue; }
            var linha = new Border
            {
                Padding = new Thickness(8, 15), CornerRadius = new CornerRadius(12), Background = Brushes.Transparent,
                Child = new TextBlock { Text = T(item).TrimEnd('.'), FontSize = 17, Foreground = Texto1 },
            };
            linha.Tapped += (_, _) => { FecharGaveta(); Avisar(T("{0} › {1}: ainda não feito nesta prova", nome, T(item).TrimEnd('.'))); };
            s.Children.Add(linha);
        }
        return s;
    }

    // ---------------------------------------------------------------- gaveta (bottom sheet)
    void MontarGaveta()
    {
        var fundo = new Border { Background = Hex(0x000000, 0x66) };
        fundo.Tapped += (_, _) => FecharGaveta();
        _folha.Background = Superficie;
        _folha.CornerRadius = new CornerRadius(28, 28, 0, 0);
        _folha.VerticalAlignment = VerticalAlignment.Bottom;
        _folha.MaxWidth = 640;
        _gaveta.Children.Add(fundo);
        _gaveta.Children.Add(_folha);
    }

    static Transitions Deslizar() => new() { new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(220), Easing = new Avalonia.Animation.Easings.CubicEaseOut() } };

    void AbrirGaveta(string? titulo, Control conteudo, params (string Texto, bool Principal)[] acoes)
    {
        var alca = new Border { Width = 36, Height = 4, CornerRadius = new CornerRadius(2), Background = Hex(0xC7C8D0), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 10) };
        var topo = new StackPanel { Background = Brushes.Transparent };
        topo.Children.Add(alca);
        if (titulo != null)
            topo.Children.Add(new TextBlock { Text = titulo, FontSize = 22, FontWeight = FontWeight.SemiBold, Foreground = Texto1, Margin = new Thickness(24, 4, 24, 10) });
        topo.PointerPressed += (_, e) => { _arrasteY = e.GetPosition(this).Y; e.Pointer.Capture(topo); };
        topo.PointerMoved += (_, e) =>
        {
            if (double.IsNaN(_arrasteY)) return;
            _folha.Transitions = null;
            _folha.RenderTransform = TransformOperations.Parse($"translateY({Math.Max(0, e.GetPosition(this).Y - _arrasteY):0}px)");
        };
        topo.PointerReleased += (_, e) =>
        {
            double d = double.IsNaN(_arrasteY) ? 0 : e.GetPosition(this).Y - _arrasteY;
            _arrasteY = double.NaN;
            e.Pointer.Capture(null);
            _folha.Transitions = Deslizar();
            if (d > 80) FecharGaveta(); else _folha.RenderTransform = TransformOperations.Parse("translateY(0px)");
        };
        var corpo = new StackPanel { Margin = new Thickness(24, 0, 24, 24), Spacing = 8 };
        corpo.Children.Add(conteudo);
        if (acoes.Length > 0)
        {
            var g = new UniformGrid { Rows = 1, Margin = new Thickness(-4, 12, -4, 0) };
            foreach (var (texto, principal) in acoes)
            {
                var b = new Border
                {
                    Height = 48, CornerRadius = new CornerRadius(24), Margin = new Thickness(4, 0),
                    Background = principal ? _cor : Hex(0xE8EAF2),
                    Child = new TextBlock { Text = texto, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = principal ? _sobreCor : Texto1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                };
                b.Tapped += (_, _) => { FecharGaveta(); Avisar(T("{0} {1}: ainda não feito nesta prova", texto, _jogador?.Nome)); };

                g.Children.Add(b);
            }
            corpo.Children.Add(g);
        }
        var tudo = new DockPanel();
        DockPanel.SetDock(topo, Dock.Top);
        tudo.Children.Add(topo);
        tudo.Children.Add(new ScrollViewer { Content = corpo, IsScrollInertiaEnabled = true });
        _folha.Child = tudo;
        _folha.MaxHeight = Math.Max(320, Bounds.Height * 0.88);
        _folha.Transitions = null;
        _folha.RenderTransform = TransformOperations.Parse("translateY(1000px)");
        _gaveta.IsVisible = true;
        _folha.Transitions = Deslizar();
        Dispatcher.UIThread.Post(() => _folha.RenderTransform = TransformOperations.Parse("translateY(0px)"), DispatcherPriority.Background);
    }

    void FecharGaveta()
    {
        _folha.Transitions = Deslizar();
        _folha.RenderTransform = TransformOperations.Parse("translateY(1000px)");
        DispatcherTimer.RunOnce(() => _gaveta.IsVisible = false, TimeSpan.FromMilliseconds(230));
    }

    // ---------------------------------------------------------------- aviso (snackbar)
    Control Aviso()
    {
        _avisoTexto.Foreground = Brushes.White;
        _avisoTexto.FontSize = 14;
        _avisoTexto.TextWrapping = TextWrapping.Wrap;
        _aviso.Background = Escuro;
        _aviso.CornerRadius = new CornerRadius(10);
        _aviso.Padding = new Thickness(16, 14);
        _aviso.Margin = new Thickness(16, 0, 16, 12);
        _aviso.VerticalAlignment = VerticalAlignment.Bottom;
        _aviso.Child = _avisoTexto;
        _aviso.IsVisible = false;
        Grid.SetRow(_aviso, 2);
        return _aviso;
    }

    void Avisar(string texto)
    {
        _avisoTexto.Text = texto;
        _aviso.IsVisible = true;
        _timerAviso?.Stop();
        _timerAviso = DispatcherTimer.RunOnce(() => _aviso.IsVisible = false, TimeSpan.FromSeconds(2.5)) as DispatcherTimer;
    }

    // ---------------------------------------------------------------- ajudantes
    static PathIcon Icone(string dados, double tam, IBrush cor) =>
        new() { Data = Geometry.Parse(dados), Width = tam, Height = tam, Foreground = cor, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    static Border Etiqueta(string texto, IBrush fundo, IBrush letra) => new()
    {
        Background = fundo, CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = texto, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = letra },
    };

    static string Titulo(string s) =>
        string.Join(" ", s.ToLowerInvariant().Split(' ').Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..] : p));

    // dinheiro sempre com a mascara da moeda, sem centavos (o jogo nao tem): "R$ 95.000"
    static string Dinheiro(long v, bool curto = false) => Valor(v);

    static string Valor(long v) => (v < 0 ? "-" : "") + Moeda + " " + FormatoDinheiro.Numero(Math.Abs(v));

    static double Saturacao(int rgb)
    {
        int r = rgb >> 16 & 255, g = rgb >> 8 & 255, b = rgb & 255;
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        return max == 0 ? 0 : (max - min) / (double)max + (255 - max) / 1000.0;
    }

    static Color Escurecer(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
    static IBrush Cor(int rgb) => new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    static IBrush Hex(int rgb, byte alfa = 255) => new SolidColorBrush(Color.FromArgb(alfa, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
}
