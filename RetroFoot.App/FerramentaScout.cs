using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RetroFoot.Motor.Arquivos;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Save;
using static RetroFoot.App.Idioma;
using Mem = RetroFoot.Motor.Partida.Memoria;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;

namespace RetroFoot.App;

// Scout: todos os jogadores de um jogo gravado, com filtros e ordenacao por varias colunas,
// para achar os melhores. So leitura. Veio do Scout do launcher do jogo classico
// (launcher/src-cross/ScoutWindow.cs, src/Score.cs): as mesmas colunas, Score e
// Impacto (ScoutScore.cs), mais o Valor do jogador (MercadoJogo.ValorDoJogador). O "?" explica
// cada coluna; duplo clique (ou Enter) abre o jogador e a equipa dele.
static class FerramentaScout
{
    [ModuleInitializer]
    internal static void Registar() =>
        Ferramentas.Registrar(Ferramentas.Scout, "Scout", "Procure os melhores jogadores", "lupa", ctx => new TelaScout(ctx));
}

// um jogador do save, com o que o Scout mostra
sealed class LinhaScout
{
    public SJogador J = null!;
    public SEquipa E = null!;
    public string Nome = "", Pais = "", Equipa = "", Situacao = "";
    public int Pos, Score, Div, Jogos, Golos, Lesoes, Amarelos, Expulsoes, Acumulados;
    public double Impacto;
    public long Valor;
    public bool Humana, Estrangeiro;

    public string SitCurta
    {
        get
        {
            var s = ((J.Suspensao > 0 ? "S" + J.Suspensao : Acumulados > 0 ? "A" + Acumulados : "") + (J.JogosLesao > 0 ? " L" + J.JogosLesao : "")).Trim();
            return s.Length > 0 ? s : Situacao;
        }
    }

    public bool Indisponivel => (short)J.Suspensao > 0 || (short)J.JogosLesao > 0;
}

// o save lido para o Scout
sealed class SaveScout
{
    public Jogo98 S = null!;
    public Moeda Moeda = null!;
    public List<LinhaScout> Linhas = new();
    public List<SEquipa> Equipas = new();
    public Dictionary<ushort, int> DivisaoDe = new();
    public HashSet<ushort> Humanas = new();
    public int NDivisoes;

    public static string Texto(byte[] b) => b.Length == 0 ? "" : Encoding.Latin1.GetString(Cifra.TextoEFT(b).Texto);

    public static SaveScout Ler(string arquivo, ICollection<string> bosman, ICollection<string> plop)
    {
        var s = Jogo98.Ler(File.ReadAllBytes(arquivo));
        var r = new SaveScout { S = s, Moeda = Moeda.DoSave(s), NDivisoes = s.Divisoes.Count };
        for (int d = 0; d < s.Divisoes.Count; d++)
            foreach (var id in s.Divisoes[d].Equipas) r.DivisaoDe.TryAdd(id, d);
        foreach (var t in s.Treinadores.Where(t => t.Humano != 0))
            foreach (var e in s.Equipas.Where(e => e.Treinador == t.ID)) r.Humanas.Add(e.ID);
        MercadoJogo? m = null;
        try { m = new MercadoJogo(s, new Aleatorio(0)); } catch { }
        r.Equipas = s.Equipas.OrderBy(e => Texto(e.NomeCurto), StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var e in s.Equipas)
        {
            var pe = Texto(e.Pais).ToUpperInvariant();
            var ne = Texto(e.NomeCurto);
            int div = r.DivisaoDe.TryGetValue(e.ID, out var dv) ? dv : 9;
            foreach (var j in e.Jogadores)
            {
                var pj = Texto(j.Pais).ToUpperInvariant();
                int pos = j.Posicao <= 3 ? j.Posicao : -1;
                var l = new LinhaScout
                {
                    J = j, E = e, Nome = Texto(j.NomeCifrado).Trim(), Pais = pj, Equipa = ne, Pos = pos, Div = div,
                    Humana = r.Humanas.Contains(e.ID),
                    Score = ScoutScore.Rendimento(pos, (short)j.Nota, (short)j.Lesao, (short)j.Comportamento),
                    Impacto = ScoutScore.Impacto(pos, (short)j.Nota, (short)j.Lesao, (short)j.Comportamento),
                    Jogos = TextosMercado.Hist(j, Mem.HistJogos), Golos = TextosMercado.Hist(j, Mem.HistGolos),
                    Lesoes = TextosMercado.Hist(j, Mem.HistLesoes), Expulsoes = TextosMercado.Hist(j, Mem.HistVermelhos),
                    Amarelos = Cartoes.AmarelosCarreira(j), Acumulados = Cartoes.AmarelosAcumulados(j),
                };
                try { l.Valor = m?.ValorDoJogador(j) ?? 0; } catch { l.Valor = 0; }
                // seg12:0a2f: os dois paises no Bosman ou no PLOP nao contam como estrangeiro
                if (pj != pe)
                {
                    if (bosman.Contains(pj) && bosman.Contains(pe)) l.Situacao = "Bosman";
                    else if (plop.Contains(pj) && plop.Contains(pe)) l.Situacao = "PLOP";
                    else { l.Situacao = T("Estrang."); l.Estrangeiro = true; }
                }
                r.Linhas.Add(l);
            }
        }
        return r;
    }
}

sealed class TelaScout : UserControl
{
    readonly Ferramentas.Contexto ctx;
    readonly EstiloFerramenta est = new();
    readonly bool largo;
    static string? ultimoSave;   // o save escolhido da ultima vez (nesta execucao)

    // as colunas: titulo, largura, alinhamento, chave de ordenacao, maior primeiro?
    sealed record Coluna(string Id, string Titulo, double Largura, HorizontalAlignment Alinha, Func<LinhaScout, IComparable> Chave, bool Desc, string? Dica = null);
    readonly List<Coluna> colunas;
    sealed class Criterio { public Coluna C = null!; public bool Desc; }
    readonly List<Criterio> criterios = new();

    SaveScout? dados;
    List<(string Caminho, string Rotulo)> saves = new();
    readonly Dictionary<string, string> nomesPaises = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> bosman = new(), plop = new();
    bool montando;

    // controles
    readonly ComboBox cSave, cPais, cDivisao, cEquipa, cNota, cLesao, cComp;
    readonly TextBox tNome, tForcaMin, tForcaMax, tValorMax;
    readonly CheckBox xEstrela, xEstrangeiros, xDisponiveis, xHumanas;
    readonly bool[] posicoes = { true, true, true, true };
    readonly Clicavel[] bPos = new Clicavel[4];
    readonly ListBox lista;
    readonly Grid cabecalho = new();
    readonly TextBlock conta;
    readonly Control painelFiltros;
    readonly Panel camada = new();   // o jogador/equipa e a ajuda, por cima
    Clicavel bFiltros = null!;

    public TelaScout(Ferramentas.Contexto ctx)
    {
        this.ctx = ctx;
        largo = ctx.Largo;
        Focusable = true;
        CarregarListas();

        colunas = new()
        {
            new("pos", T("Pos"), 32, HorizontalAlignment.Center, l => l.Pos, false, T("Posição")),
            new("nome", T("Nome"), 150, HorizontalAlignment.Left, l => l.Nome, false),
            new("estrela", "✱", 18, HorizontalAlignment.Center, l => l.J.Estrela != 0 ? 1 : 0, true, T("Estrela")),
            new("score", "Score", 46, HorizontalAlignment.Right, l => l.Score, true),
            new("impacto", T("Impacto"), 56, HorizontalAlignment.Right, l => l.Impacto, true),
            new("forca", T("Força"), 62, HorizontalAlignment.Right, l => (short)l.J.Forca, true),
            new("nota", T("Nota"), 38, HorizontalAlignment.Right, l => (short)l.J.Nota, true),
            new("lesao", T("Lesão"), 42, HorizontalAlignment.Right, l => (short)l.J.Lesao, false),
            new("comp", T("Comport."), 84, HorizontalAlignment.Left, l => (short)l.J.Comportamento, false, T("Comportamento")),
            new("equipa", T("Equipa"), 118, HorizontalAlignment.Left, l => l.Equipa, false),
            new("div", T("Div."), 40, HorizontalAlignment.Center, l => l.Div, false, T("Divisão")),
            new("pais", T("País"), 54, HorizontalAlignment.Left, l => NomePais(l.Pais), false),
            new("valor", T("Valor"), 100, HorizontalAlignment.Right, l => l.Valor, true),
            new("salario", T("Salário"), 88, HorizontalAlignment.Right, l => (int)l.J.Salario, true),
            new("jogos", T("Jogos"), 42, HorizontalAlignment.Right, l => l.Jogos, true),
            new("golos", T("Golos"), 42, HorizontalAlignment.Right, l => l.Golos, true),
            new("lesoes", T("Lesões"), 46, HorizontalAlignment.Right, l => l.Lesoes, true),
            new("amar", T("Amar."), 42, HorizontalAlignment.Right, l => l.Amarelos, true, T("Amarelos na carreira")),
            new("expuls", T("Expuls."), 48, HorizontalAlignment.Right, l => l.Expulsoes, true, T("Expulsões")),
            new("sit", T("Sit."), 64, HorizontalAlignment.Left, l => l.SitCurta, false, T("Situação")),
        };
        criterios.Add(new Criterio { C = colunas.First(c => c.Id == "forca"), Desc = true });

        cSave = est.Combo(); cPais = est.Combo(); cDivisao = est.Combo(); cEquipa = est.Combo();
        cNota = est.Combo(); cLesao = est.Combo(); cComp = est.Combo();
        tNome = est.Caixa(); tForcaMin = est.Caixa(); tForcaMax = est.Caixa(); tValorMax = est.Caixa();
        tNome.PlaceholderText = T("Parte do nome");
        tValorMax.PlaceholderText = T("Qualquer");
        xEstrela = est.Marcar(T("Só com estrela (✱)"));
        xEstrangeiros = est.Marcar(T("Só estrangeiros (contam no limite)"));
        xDisponiveis = est.Marcar(T("Esconder suspensos e lesionados"));
        xHumanas = est.Marcar(T("Esconder as equipas humanas"));
        conta = est.Txt("", 11.5, est.Fraco);
        conta.TextWrapping = TextWrapping.NoWrap;
        conta.TextTrimming = TextTrimming.CharacterEllipsis;
        conta.VerticalAlignment = VerticalAlignment.Center;

        cNota.ItemsSource = new[] { T("Qualquer") }.Concat(Enumerable.Range(2, 9).Select(n => T("{0} ou mais", n))).ToList();
        cLesao.ItemsSource = new[] { T("Qualquer") }.Concat(Enumerable.Range(0, 10).Select(n => T("até {0}", n))).ToList();
        cComp.ItemsSource = new[] { T("Qualquer"), T("Só Fair Play") }
            .Concat(TextosMercado.Comportamentos.Skip(1).Select(c => T("Até {0}", T(c)))).ToList();

        foreach (var c in new[] { cPais, cDivisao, cEquipa, cNota, cLesao, cComp }) c.SelectionChanged += (_, _) => Filtrar();
        foreach (var t in new[] { tNome, tForcaMin, tForcaMax, tValorMax }) t.TextChanged += (_, _) => Filtrar();
        foreach (var x in new[] { xEstrela, xEstrangeiros, xDisponiveis, xHumanas }) x.IsCheckedChanged += (_, _) => Filtrar();
        cSave.SelectionChanged += (_, _) => CarregarSave();

        lista = est.Lista();
        lista.ItemTemplate = new FuncDataTemplate<LinhaScout>((l, _) => l == null ? new Panel() : Linha(l));
        lista.DoubleTapped += (_, _) => AbrirSelecionado();
        lista.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; AbrirSelecionado(); } };

        painelFiltros = MontarFiltros();
        var raiz = new Grid();
        raiz.Children.Add(MontarCorpo());
        raiz.Children.Add(camada);
        Content = est.Raiz(raiz);

        LimparFiltros();
        ListarSaves();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && camada.Children.Count > 0) { camada.Children.Clear(); lista.Focus(); e.Handled = true; }
        };
    }

    // ---------------------------------------------------------------- listas da pasta de dados
    void CarregarListas()
    {
        try
        {
            foreach (var p in TextosDados.LerPaises(Path.Combine(ctx.RaizDados, PastaDados.Paises))) nomesPaises[p.Codigo] = p.Nome;
            var g = Path.Combine(ctx.RaizDados, PastaDados.Grupos);
            foreach (var c in TextosDados.LerGrupo(Path.Combine(g, PastaDados.Bosman))) bosman.Add(c);
            foreach (var c in TextosDados.LerGrupo(Path.Combine(g, PastaDados.Plop))) plop.Add(c);
        }
        catch { }
    }

    string NomePais(string cod) => nomesPaises.TryGetValue(cod, out var n) ? n : TextosMercado.NomePais(cod);

    string NomeDivisao(int d) => d < 4 ? T("{0}ª Divisão", d + 1) : d == 4 ? T("Distrital") : "—";
    string DivCurta(int d) => d < 4 ? T("{0}ª", d + 1) : d == 4 ? T("Dist.") : "—";

    // ---------------------------------------------------------------- montagem
    Control MontarCorpo()
    {
        // barra de cima: o save, recarregar, filtros, ajuda
        var barra = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        var rSave = est.Txt(T("Jogo gravado:"), 12.5, est.Rotulo, true);
        rSave.VerticalAlignment = VerticalAlignment.Center;
        barra.Children.Add(rSave);
        cSave.MaxWidth = largo ? 460 : 300;
        cSave.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(cSave, 1);
        barra.Children.Add(cSave);
        var recarregar = Ui.Botao(T("Recarregar"), () => { ListarSaves(); }, false, "relogio");
        Grid.SetColumn(recarregar, 2);
        barra.Children.Add(recarregar);
        bFiltros = Ui.Botao(T("Filtros"), AlternarFiltros, false, "ajustes");
        Grid.SetColumn(bFiltros, 3);
        barra.Children.Add(bFiltros);
        var ajuda = Ui.Botao("?", MostrarAjuda, true);
        ajuda.MinWidth = 36;
        ToolTip.SetTip(ajuda, T("O que significa cada coluna"));
        Grid.SetColumn(ajuda, 4);
        barra.Children.Add(ajuda);

        // a tabela: o cabecalho e a lista rolam juntos para o lado
        double total = colunas.Sum(c => c.Largura + 6) + 12;
        cabecalho.ColumnDefinitions = new ColumnDefinitions(string.Join(",", colunas.Select(c => (c.Largura + 6).ToString(System.Globalization.CultureInfo.InvariantCulture))));
        for (int i = 0; i < colunas.Count; i++)
        {
            var c = colunas[i];
            var t = new TextBlock
            {
                Text = c.Titulo, FontFamily = est.Fonte, FontSize = 11.5, FontWeight = FontWeight.Bold, Foreground = est.CabTexto,
                HorizontalAlignment = c.Alinha, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.None,
            };
            var cel = new Border
            {
                Background = est.CabFundo, Padding = new Thickness(3, 4), Child = t, Cursor = new Cursor(StandardCursorType.Hand),
            };
            if (c.Dica != null) ToolTip.SetTip(cel, c.Dica);
            cel.PointerPressed += (_, e) => { if (e.GetCurrentPoint(cel).Properties.IsLeftButtonPressed) { Ordenar(c, e.KeyModifiers.HasFlag(KeyModifiers.Shift)); e.Handled = true; } };
            Grid.SetColumn(cel, i);
            cabecalho.Children.Add(cel);
        }
        var grade = new DockPanel { Width = total };
        DockPanel.SetDock(cabecalho, Dock.Top);
        grade.Children.Add(new Border { Background = est.CabFundo, Child = cabecalho, [DockPanel.DockProperty] = Dock.Top });
        grade.Children.Add(lista);
        Ui.AlinharCabecalho(cabecalho, lista);
        var rolagem = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = grade,
        };
        var tabela = est.Moldura(rolagem);

        var corpo = new Grid { ColumnDefinitions = new ColumnDefinitions(largo ? "236,*" : "206,*"), ColumnSpacing = 8 };
        corpo.Children.Add(painelFiltros);
        Grid.SetColumn(tabela, 1);
        corpo.Children.Add(tabela);

        var d = new DockPanel();
        DockPanel.SetDock(barra, Dock.Top);
        d.Children.Add(barra);
        var rodape = new Border { Margin = new Thickness(2, 6, 0, 0), Child = conta };
        DockPanel.SetDock(rodape, Dock.Bottom);
        d.Children.Add(rodape);
        d.Children.Add(corpo);
        filtrosVisiveis = largo;
        corpoGrade = corpo;
        tabelaCorpo = tabela;
        AplicarFiltrosVisiveis();
        return d;
    }

    bool filtrosVisiveis;
    Grid corpoGrade = null!;
    Control tabelaCorpo = null!;

    void AlternarFiltros()
    {
        filtrosVisiveis = !filtrosVisiveis;
        AplicarFiltrosVisiveis();
    }

    void AplicarFiltrosVisiveis()
    {
        painelFiltros.IsVisible = filtrosVisiveis;
        Grid.SetColumn(tabelaCorpo, filtrosVisiveis ? 1 : 0);
        Grid.SetColumnSpan(tabelaCorpo, filtrosVisiveis ? 1 : 2);
    }

    Control MontarFiltros()
    {
        var p = new StackPanel { Spacing = 7 };
        p.Children.Add(est.Campo(T("Nome"), tNome));
        var posP = new UniformGrid { Columns = 4, Height = 26 };
        for (int i = 0; i < 4; i++)
        {
            int k = i;
            var b = new Clicavel
            {
                CornerRadius = new CornerRadius(6), Margin = new Thickness(i == 0 ? 0 : 2, 0, i == 3 ? 0 : 2, 0),
                BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, CorFoco = Tema.Azul,
                Child = new TextBlock { Text = Posicao(i), FontFamily = est.Fonte, FontWeight = FontWeight.Bold, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            ToolTip.SetTip(b, T(TextosMercado.Posicoes[i]));
            b.Clique += () => { posicoes[k] = !posicoes[k]; PintarPosicoes(); Filtrar(); };
            bPos[i] = b;
            posP.Children.Add(b);
        }
        p.Children.Add(est.Campo(T("Posição"), posP));
        p.Children.Add(est.Campo(T("País do jogador"), cPais));
        p.Children.Add(est.Campo(T("Divisão"), cDivisao));
        p.Children.Add(est.Campo(T("Equipa"), cEquipa));
        var forca = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), ColumnSpacing = 5 };
        forca.Children.Add(tForcaMin);
        var ate = est.RotuloCampo("–");
        ate.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(ate, 1);
        forca.Children.Add(ate);
        Grid.SetColumn(tForcaMax, 2);
        forca.Children.Add(tForcaMax);
        p.Children.Add(est.Campo(T("Força"), forca));
        p.Children.Add(est.Campo(T("Nota"), cNota));
        p.Children.Add(est.Campo(T("Lesão"), cLesao));
        p.Children.Add(est.Campo(T("Comportamento"), cComp));
        p.Children.Add(est.Campo(T("Valor até"), tValorMax));
        p.Children.Add(xEstrela);
        p.Children.Add(xEstrangeiros);
        p.Children.Add(xDisponiveis);
        p.Children.Add(xHumanas);
        var limpar = Ui.Botao(T("Limpar filtros"), LimparFiltros);
        limpar.HorizontalAlignment = HorizontalAlignment.Stretch;
        p.Children.Add(limpar);
        var sv = new ScrollViewer { Content = p, Padding = new Thickness(0, 0, 4, 0) };
        return est.Cartao(sv, new Thickness(8));
    }

    void PintarPosicoes()
    {
        for (int i = 0; i < 4; i++)
        {
            var b = bPos[i];
            b.TrocarFundo(posicoes[i] ? EstiloFerramenta.CoresPosicao[i] : Tema.FundoHover);
            if (b.Child is TextBlock t) t.Foreground = posicoes[i] ? Brushes.White : est.Fraco;
        }
    }

    // ---------------------------------------------------------------- linha da tabela
    Control Linha(LinhaScout l)
    {
        var g = new Grid { ColumnDefinitions = cabecalho.ColumnDefinitions.Count > 0 ? new ColumnDefinitions(string.Join(",", colunas.Select(c => (c.Largura + 6).ToString(System.Globalization.CultureInfo.InvariantCulture)))) : new ColumnDefinitions() };
        var j = l.J;
        for (int i = 0; i < colunas.Count; i++)
        {
            var c = colunas[i];
            Control cel;
            switch (c.Id)
            {
                case "pos":
                    cel = new Border
                    {
                        Background = l.Pos >= 0 ? EstiloFerramenta.CoresPosicao[l.Pos] : Brushes.Gray, CornerRadius = new CornerRadius(3), Width = 24,
                        Child = new TextBlock { Text = l.Pos >= 0 ? Posicao(l.Pos) : "?", Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = 11.5, FontFamily = est.Fonte, HorizontalAlignment = HorizontalAlignment.Center },
                    };
                    break;
                case "pais":
                    var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                    sp.Children.Add(est.ImagemBandeira(l.Pais, 12));
                    sp.Children.Add(Cel(l.Pais, est.TabelaTexto));
                    ToolTip.SetTip(sp, NomePais(l.Pais));
                    cel = sp;
                    break;
                default:
                    var (texto, cor, negrito) = Valor(c.Id, l);
                    var tb = Cel(texto, cor ?? est.TabelaTexto, negrito);
                    if (c.Id == "equipa" && l.Humana) { tb.FontWeight = FontWeight.Bold; ToolTip.SetTip(tb, T("Equipa humana")); }
                    cel = tb;
                    break;
            }
            cel.HorizontalAlignment = c.Alinha;
            cel.VerticalAlignment = VerticalAlignment.Center;
            cel.Margin = new Thickness(3, 0);
            Grid.SetColumn(cel, i);
            g.Children.Add(cel);
        }
        return new Border { Padding = new Thickness(0, 3), Child = g };
    }

    TextBlock Cel(string s, IBrush cor, bool negrito = false) => new()
    {
        Text = s, FontFamily = est.Fonte, FontSize = 12, Foreground = cor, FontWeight = negrito ? FontWeight.Bold : FontWeight.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap,
    };

    (string, IBrush?, bool) Valor(string id, LinhaScout l)
    {
        var j = l.J;
        string Din(long v) => dados == null ? v.ToString() : FormatoDinheiro.Valor(dados.Moeda, v);
        return id switch
        {
            "nome" => (l.Nome, null, true),
            "estrela" => (j.Estrela != 0 ? "✱" : "", est.Ouro, true),
            "score" => (l.Score.ToString(), est.Ouro, true),
            "impacto" => (ScoutScore.TextoImpacto(l.Impacto), l.Impacto > 0.004 ? est.Bom : l.Impacto < -0.004 ? est.Mau : null, false),
            "forca" => (((short)j.Forca).ToString(), null, true),
            "nota" => (((short)j.Nota).ToString(), null, false),
            "lesao" => (((short)j.Lesao).ToString(), null, false),
            "comp" => (TextosMercado.Comportamento(j), null, false),
            "equipa" => (l.Equipa, null, false),
            "div" => (DivCurta(l.Div), null, false),
            "valor" => (Din(l.Valor), null, false),
            "salario" => (Din((int)j.Salario), null, false),
            "jogos" => (l.Jogos.ToString(), null, false),
            "golos" => (l.Golos.ToString(), null, false),
            "lesoes" => (l.Lesoes.ToString(), null, false),
            "amar" => (l.Amarelos.ToString(), null, false),
            "expuls" => (l.Expulsoes.ToString(), null, false),
            "sit" => (l.SitCurta, l.Indisponivel || l.Estrangeiro ? est.Mau : est.Bom, false),
            _ => ("", null, false),
        };
    }

    // ---------------------------------------------------------------- saves
    void ListarSaves()
    {
        var pasta = Environment.GetEnvironmentVariable("RETROFOOT_JOGOS") is { Length: > 0 } j ? j : Path.Combine(ctx.RaizDados, PastaDados.Saves);
        var antes = cSave.SelectedIndex >= 0 && cSave.SelectedIndex < saves.Count ? saves[cSave.SelectedIndex].Caminho : ultimoSave;
        saves = new();
        try
        {
            if (Directory.Exists(pasta))
                foreach (var f in Directory.GetFiles(pasta, "*.e98", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive })
                             .Select(f => new FileInfo(f)).Where(f => f.Length > 0).OrderByDescending(f => f.LastWriteTimeUtc))
                    saves.Add((f.FullName, Path.GetFileNameWithoutExtension(f.Name).ToUpperInvariant() + "  ·  " + f.LastWriteTime.ToString("dd/MM/yyyy HH:mm")));
        }
        catch { }
        montando = true;
        cSave.ItemsSource = saves.Select(s => s.Rotulo).ToList();
        montando = false;
        if (saves.Count == 0)
        {
            dados = null;
            lista.ItemsSource = null;
            conta.Text = T("Nenhum jogo gravado em {0}.", pasta);
            return;
        }
        int i = antes != null ? saves.FindIndex(s => s.Caminho == antes) : -1;
        cSave.SelectedIndex = -1;
        cSave.SelectedIndex = i >= 0 ? i : 0;
    }

    void CarregarSave()
    {
        if (montando || cSave.SelectedIndex < 0 || cSave.SelectedIndex >= saves.Count) return;
        var arq = saves[cSave.SelectedIndex].Caminho;
        ultimoSave = arq;
        try { dados = SaveScout.Ler(arq, bosman, plop); }
        catch (Exception ex)
        {
            dados = null;
            lista.ItemsSource = null;
            conta.Text = T("Não consegui ler o jogo gravado: {0}", ex.Message);
            return;
        }
        montando = true;
        var porPais = dados.Linhas.GroupBy(l => l.Pais).OrderBy(g => NomePais(g.Key), StringComparer.CurrentCultureIgnoreCase).ToList();
        paisesCombo = new List<string?> { null };
        paisesCombo.AddRange(porPais.Select(g => (string?)g.Key));
        cPais.ItemsSource = new[] { T("Todos ({0})", dados.Linhas.Count) }.Concat(porPais.Select(g => $"{NomePais(g.Key)} ({g.Count()})")).ToList();
        divisoesCombo = new List<int?> { null };
        divisoesCombo.AddRange(dados.Linhas.Select(l => l.Div).Distinct().OrderBy(d => d).Select(d => (int?)d));
        cDivisao.ItemsSource = divisoesCombo.Select(d => d == null ? T("Todas") : NomeDivisao(d.Value)).ToList();
        equipasCombo = new List<SEquipa?> { null };
        equipasCombo.AddRange(dados.Equipas);
        cEquipa.ItemsSource = equipasCombo.Select(e => e == null ? T("Todas") : (dados.Humanas.Contains(e.ID) ? "★ " : "") + SaveScout.Texto(e.NomeCurto)).ToList();
        cPais.SelectedIndex = cDivisao.SelectedIndex = cEquipa.SelectedIndex = 0;
        montando = false;
        Filtrar();
    }

    List<string?> paisesCombo = new() { null };
    List<int?> divisoesCombo = new() { null };
    List<SEquipa?> equipasCombo = new() { null };

    // ---------------------------------------------------------------- filtros e ordem
    void LimparFiltros()
    {
        montando = true;
        tNome.Text = tForcaMin.Text = tForcaMax.Text = tValorMax.Text = "";
        for (int i = 0; i < 4; i++) posicoes[i] = true;
        if (cPais.ItemCount > 0) cPais.SelectedIndex = 0;
        if (cDivisao.ItemCount > 0) cDivisao.SelectedIndex = 0;
        if (cEquipa.ItemCount > 0) cEquipa.SelectedIndex = 0;
        cNota.SelectedIndex = cLesao.SelectedIndex = cComp.SelectedIndex = 0;
        foreach (var x in new[] { xEstrela, xEstrangeiros, xDisponiveis, xHumanas }) x.IsChecked = false;
        criterios.Clear();
        criterios.Add(new Criterio { C = colunas.First(c => c.Id == "forca"), Desc = true });
        montando = false;
        PintarPosicoes();
        Filtrar();
    }

    static int? Numero(TextBox t)
    {
        var s = new string((t.Text ?? "").Where(char.IsDigit).ToArray());
        return s.Length > 0 && long.TryParse(s, out var v) ? (int)Math.Min(v, int.MaxValue) : null;
    }

    void Filtrar()
    {
        if (montando) return;
        if (dados == null) { AtualizarCabecalho(); return; }
        var termo = SemAcento((tNome.Text ?? "").Trim());
        var pais = cPais.SelectedIndex > 0 && cPais.SelectedIndex < paisesCombo.Count ? paisesCombo[cPais.SelectedIndex] : null;
        var div = cDivisao.SelectedIndex > 0 && cDivisao.SelectedIndex < divisoesCombo.Count ? divisoesCombo[cDivisao.SelectedIndex] : null;
        var eq = cEquipa.SelectedIndex > 0 && cEquipa.SelectedIndex < equipasCombo.Count ? equipasCombo[cEquipa.SelectedIndex] : null;
        int? fMin = Numero(tForcaMin), fMax = Numero(tForcaMax), vMax = Numero(tValorMax);
        int notaMin = cNota.SelectedIndex > 0 ? cNota.SelectedIndex + 1 : 0;
        int lesaoMax = cLesao.SelectedIndex > 0 ? cLesao.SelectedIndex - 1 : 99;
        int compMax = cComp.SelectedIndex > 0 ? cComp.SelectedIndex - 1 : 99;
        var r = dados.Linhas.Where(l =>
            (termo.Length == 0 || SemAcento(l.Nome).Contains(termo, StringComparison.OrdinalIgnoreCase)) &&
            (l.Pos < 0 || posicoes[l.Pos]) &&
            (pais == null || l.Pais == pais) && (div == null || l.Div == div) && (eq == null || l.E == eq) &&
            (fMin == null || (short)l.J.Forca >= fMin) && (fMax == null || (short)l.J.Forca <= fMax) &&
            (vMax == null || l.Valor <= vMax) &&
            (short)l.J.Nota >= notaMin && (short)l.J.Lesao <= lesaoMax && (short)l.J.Comportamento <= compMax &&
            (xEstrela.IsChecked != true || l.J.Estrela != 0) &&
            (xEstrangeiros.IsChecked != true || l.Estrangeiro) &&
            (xDisponiveis.IsChecked != true || !l.Indisponivel) &&
            (xHumanas.IsChecked != true || !l.Humana)).ToList();
        IOrderedEnumerable<LinhaScout>? o = null;
        foreach (var c in criterios)
        {
            var k = c.C.Chave;
            o = o == null ? (c.Desc ? r.OrderByDescending(k) : r.OrderBy(k)) : (c.Desc ? o.ThenByDescending(k) : o.ThenBy(k));
        }
        r = (o ?? r.OrderBy(x => 0)).ThenByDescending(x => (short)x.J.Forca).ThenByDescending(x => (short)x.J.Nota).ToList();
        lista.ItemsSource = r;
        conta.Text = T("{0} de {1} jogadores · clique nos títulos para ordenar (Shift+clique junta colunas) · duplo clique abre o jogador", r.Count, dados.Linhas.Count);
        AtualizarCabecalho();
    }

    static string SemAcento(string s) => new string(s.Normalize(NormalizationForm.FormD)
        .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());

    // clique: so esta coluna (outro clique inverte); Shift+clique: a coluna entra como proximo
    // criterio (outro Shift+clique inverte, o 3o tira), como juntar colunas no Scout do launcher
    void Ordenar(Coluna c, bool juntar)
    {
        var achado = criterios.FirstOrDefault(x => x.C == c);
        if (!juntar)
        {
            bool desc = achado != null && criterios.Count == 1 ? !achado.Desc : c.Desc;
            criterios.Clear();
            criterios.Add(new Criterio { C = c, Desc = desc });
        }
        else if (achado == null) criterios.Add(new Criterio { C = c, Desc = c.Desc });
        else if (achado.Desc == c.Desc) achado.Desc = !achado.Desc;
        else criterios.Remove(achado);
        if (criterios.Count == 0) criterios.Add(new Criterio { C = colunas.First(x => x.Id == "forca"), Desc = true });
        Filtrar();
    }

    void AtualizarCabecalho()
    {
        for (int i = 0; i < colunas.Count; i++)
        {
            if (cabecalho.Children[i] is not Border { Child: TextBlock t }) continue;
            int n = criterios.FindIndex(x => x.C == colunas[i]);
            // a seta (e o numero do criterio) antes do titulo: depois dele cortava nas colunas estreitas
            t.Text = n < 0 ? colunas[i].Titulo
                : (criterios[n].Desc ? "▼" : "▲") + (criterios.Count > 1 ? "¹²³⁴⁵⁶⁷⁸⁹"[Math.Min(n, 8)].ToString() : "") + " " + colunas[i].Titulo;
            t.TextTrimming = TextTrimming.None;
        }
    }

    // ---------------------------------------------------------------- jogador e equipa
    void AbrirSelecionado()
    {
        if (lista.SelectedItem is LinhaScout l) MostrarJogador(l);
    }

    void Sobrepor(string titulo, string icone, Control conteudo, double largura)
    {
        camada.Children.Clear();
        var fechar = Ui.Botao(T("Fechar"), Fechar, true);
        fechar.HorizontalAlignment = HorizontalAlignment.Right;
        var d = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(fechar, Dock.Bottom);
        fechar.Margin = new Thickness(0, 8, 0, 0);
        d.Children.Add(fechar);
        d.Children.Add(conteudo);
        var j = Ui.Janela(titulo, d, Fechar, icone);
        j.Width = largura;
        j.MaxHeight = 470;
        camada.Children.Add(Ui.Veu(j));
        Dispatcher.UIThread.Post(() => fechar.Focus());

        void Fechar()
        {
            camada.Children.Clear();
            lista.Focus();
        }
    }

    void MostrarAjuda()
    {
        var p = new StackPanel { Spacing = 8 };
        foreach (var (t, x) in ScoutScore.Ajuda())
        {
            var b = new StackPanel { Spacing = 1 };
            b.Children.Add(est.Txt(t, 13, Tema.CorTitulo, true));
            b.Children.Add(est.Txt(x, 12));
            p.Children.Add(b);
        }
        Sobrepor(T("O que significa cada coluna"), "info", new ScrollViewer { Content = p, Padding = new Thickness(0, 0, 8, 0) }, largo ? 760 : 640);
    }

    void MostrarJogador(LinhaScout l)
    {
        if (dados == null) return;
        var j = l.J;
        string Din(long v) => FormatoDinheiro.Valor(dados.Moeda, v);

        // o jogador
        var esq = new StackPanel { Spacing = 6 };
        var topo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        topo.Children.Add(new Border
        {
            Background = l.Pos >= 0 ? EstiloFerramenta.CoresPosicao[l.Pos] : Brushes.Gray, CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = l.Pos >= 0 ? T(TextosMercado.Posicoes[l.Pos]) : "?", Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontFamily = est.Fonte, FontSize = 12 },
        });
        var nome = Ui.Titulo(l.Nome + (j.Estrela != 0 ? " ✱" : ""), 19);
        nome.TextWrapping = TextWrapping.Wrap;
        nome.MaxWidth = largo ? 300 : 230;
        topo.Children.Add(nome);
        esq.Children.Add(topo);
        var pais = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        pais.Children.Add(est.ImagemBandeira(l.Pais, 14));
        pais.Children.Add(est.Txt(NomePais(l.Pais) + " (" + l.Pais + ")", 12.5));
        esq.Children.Add(pais);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, RowSpacing = 2 };
        void Par(string r, string v, IBrush? cor = null)
        {
            int row = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var a = est.Txt(r, 12, est.Fraco);
            Grid.SetRow(a, row);
            g.Children.Add(a);
            var b = est.Txt(v, 12, cor ?? est.Texto, true);
            Grid.SetRow(b, row); Grid.SetColumn(b, 1);
            g.Children.Add(b);
        }
        Par("Score", l.Score.ToString(), est.Ouro);
        Par(T("Impacto"), ScoutScore.TextoImpacto(l.Impacto) + " " + T("pontos por época"), l.Impacto > 0.004 ? est.Bom : l.Impacto < -0.004 ? est.Mau : null);
        Par(T("Força"), ((short)j.Forca).ToString());
        Par(T("Nota"), ((short)j.Nota).ToString());
        Par(T("Lesão"), ((short)j.Lesao).ToString());
        Par(T("Comportamento"), TextosMercado.Comportamento(j));
        Par(T("Valor"), Din(l.Valor));
        Par(T("Salário"), Din((int)j.Salario));
        Par(T("Jogos"), l.Jogos.ToString());
        Par(T("Golos"), l.Golos.ToString());
        Par(T("Lesões"), l.Lesoes.ToString());
        Par(T("Amarelos na carreira"), l.Amarelos.ToString());
        Par(T("Expulsões"), l.Expulsoes.ToString());
        var sit = new List<string>();
        if ((short)j.Suspensao > 0) sit.Add(T("suspenso por {0} jogos", (short)j.Suspensao));
        if ((short)j.JogosLesao > 0) sit.Add(T("lesionado por {0} jogos", (short)j.JogosLesao));
        if (l.Acumulados == 1) sit.Add(T("1 amarelo acumulado"));
        else if (l.Acumulados > 1) sit.Add(T("{0} amarelos acumulados", l.Acumulados));
        if (l.Estrangeiro) sit.Add(T("conta como estrangeiro"));
        else if (l.Situacao.Length > 0) sit.Add(l.Situacao);
        Par(T("Situação"), sit.Count == 0 ? T("disponível") : string.Join(", ", sit), sit.Count == 0 ? est.Bom : l.Indisponivel || l.Estrangeiro ? est.Mau : est.Texto);
        esq.Children.Add(g);
        var cartaoJ = est.Cartao(new ScrollViewer { Content = esq }, new Thickness(12, 10));

        // a equipa: as cores dela, a divisao, o treinador, o dinheiro e o plantel
        var e = l.E;
        var (letra, fundo) = TextosMercado.Cores(e);
        var dir = new DockPanel();
        var cab = new StackPanel { Spacing = 3 };
        var faixa = new Border
        {
            Background = fundo, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 5), BorderBrush = Tema.Linha, BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = SaveScout.Texto(e.NomeCompleto), Foreground = letra, FontWeight = FontWeight.Bold, FontSize = 15, FontFamily = est.Fonte, TextTrimming = TextTrimming.CharacterEllipsis },
        };
        cab.Children.Add(faixa);
        var treinador = dados.S.Treinadores.FirstOrDefault(t => t.ID == e.Treinador);
        var pe = SaveScout.Texto(e.Pais).ToUpperInvariant();
        cab.Children.Add(est.Txt(NomePais(pe) + " · " + NomeDivisao(l.Div)
            + (treinador != null ? " · " + T("Treinador") + ": " + SaveScout.Texto(treinador.Nome) + (dados.Humanas.Contains(e.ID) ? " (" + T("humano") + ")" : "") : ""), 11.5, est.Fraco));
        cab.Children.Add(est.Txt(T("Dinheiro") + ": " + Din((int)e.Dinheiro) + " · " + T("{0} jogadores", e.Jogadores.Count), 11.5, est.Fraco));
        cab.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(cab, Dock.Top);
        dir.Children.Add(cab);
        var plantel = dados.Linhas.Where(x => x.E == e).OrderBy(x => x.Pos).ThenByDescending(x => (short)x.J.Forca).ToList();
        var lp = est.Lista();
        lp.ItemsSource = plantel;
        lp.ItemTemplate = new FuncDataTemplate<LinhaScout>((x, _) =>
        {
            if (x == null) return new Panel();
            var gg = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,56,44,20"), Margin = new Thickness(4, 2) };
            gg.Children.Add(new Border
            {
                Background = x.Pos >= 0 ? EstiloFerramenta.CoresPosicao[x.Pos] : Brushes.Gray, CornerRadius = new CornerRadius(3), Width = 22, HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = x.Pos >= 0 ? Posicao(x.Pos) : "?", Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = 11, FontFamily = est.Fonte, HorizontalAlignment = HorizontalAlignment.Center },
            });
            var n = Cel(x.Nome, est.TabelaTexto, x == l);
            Grid.SetColumn(n, 1); gg.Children.Add(n);
            var f = Cel(((short)x.J.Forca).ToString(), est.TabelaTexto, true);
            f.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(f, 2); gg.Children.Add(f);
            var sc = Cel(x.Score.ToString(), est.Ouro);
            sc.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(sc, 3); gg.Children.Add(sc);
            var st = Cel(x.J.Estrela != 0 ? "✱" : "", est.Ouro, true);
            st.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(st, 4); gg.Children.Add(st);
            return new Border { Background = x == l ? est.Sel : Brushes.Transparent, Child = gg };
        });
        lp.DoubleTapped += (_, _) => { if (lp.SelectedItem is LinhaScout x && x != l) MostrarJogador(x); };
        lp.KeyDown += (_, ev) => { if (ev.Key == Key.Enter && lp.SelectedItem is LinhaScout x && x != l) { ev.Handled = true; MostrarJogador(x); } };
        var cabP = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,56,44,20"), Margin = new Thickness(4, 3, 4, 3) };
        foreach (var (t, c, a) in new[] { (T("Pos"), 0, HorizontalAlignment.Left), (T("Plantel") + " · " + T("duplo clique abre"), 1, HorizontalAlignment.Left), (T("Força"), 2, HorizontalAlignment.Right), ("Score", 3, HorizontalAlignment.Right) })
        {
            var tb = new TextBlock { Text = t, FontFamily = est.Fonte, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = est.CabTexto, HorizontalAlignment = a, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(tb, c);
            cabP.Children.Add(tb);
        }
        var dp = new DockPanel();
        var cabB = new Border { Background = est.CabFundo, Child = cabP };
        DockPanel.SetDock(cabB, Dock.Top);
        dp.Children.Add(cabB);
        dp.Children.Add(lp);
        Ui.AlinharCabecalho(cabP, lp);
        dir.Children.Add(est.Moldura(dp));

        var corpo = new Grid { ColumnDefinitions = new ColumnDefinitions(largo ? "380,*" : "300,*"), ColumnSpacing = 10, Height = 360 };
        corpo.Children.Add(cartaoJ);
        Grid.SetColumn(dir, 1);
        corpo.Children.Add(dir);
        Sobrepor(l.Nome + " - " + SaveScout.Texto(e.NomeCurto), "lupa", corpo, largo ? 900 : 740);
        Dispatcher.UIThread.Post(() => lp.ScrollIntoView(l));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => { if (saves.Count == 0) cSave.Focus(); else lista.Focus(); }, DispatcherPriority.Loaded);
    }
}
