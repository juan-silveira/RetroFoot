using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using RetroFoot.Motor.Arquivos;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// Arbitros no celular: a mesma lista do computador (FerramentaArbitros.cs, referees.tsv da pasta
// de dados) em pe, ao toque. Em cima a procura e os filtros (pais e ativos), no meio a lista que
// rola (a marca liga e desliga; o toque na linha abre a folha do arbitro: pais com a bandeira,
// nome, ativo, remover), embaixo Opcoes (repor a lista do jogo, a pasta) e Novo. Grava a cada
// mudanca (o anterior fica em referees.tsv.bak). Nas duas peles do kit (CelularUi.cs): o
// Moderno verde e amarelo e o Retro das janelas do computador (Windows 95).
static class FerramentaArbitrosCelular
{
    [ModuleInitializer]
    internal static void Registar() =>
        Ferramentas.RegistrarCelular(Ferramentas.Arbitros, ctx => new TelaArbitrosCelular(ctx));
}

sealed class TelaArbitrosCelular : UserControl
{
    sealed class Item
    {
        public string Pais = "", Nome = "", Origem = "";
        public bool Ativo = true;
    }

    static bool R => CUi.Retro;
    // a caixa branca afundada do TEdit / TListBox / TComboBox e o botao solto (como no CR95)
    static readonly BoxShadows Afundada = BoxShadows.Parse("inset 1 1 0 0 #A6A6A6, inset -1 -1 0 0 #FFFFFF, inset 2 2 0 0 #9E9E9E, inset -2 -2 0 0 #9E9E9E");
    static readonly BoxShadows Solto = BoxShadows.Parse("inset 1 1 0 0 #FFFFFF, inset -1 -1 0 0 #000000, inset 2 2 0 0 #DFDFDF, inset -2 -2 0 0 #808080");

    readonly Ferramentas.Contexto ctx;
    readonly string arquivo;
    readonly List<Item> itens = new();
    readonly List<TextosDados.Pais> paises = new();
    readonly Dictionary<string, string> nomePais = new(StringComparer.OrdinalIgnoreCase);
    ObservableCollection<Item> visiveis = new();

    string? filtroPais;     // null = todos
    int mostrar;            // 0 todos, 1 so os ativos, 2 so os desativados
    string estado = "";

    readonly TextBox tBusca;
    readonly TextBlock tPais, tMostrar, conta, vazio;
    readonly ContentControl bandeiraFiltro = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly ItemsControl lista;
    ScrollViewer? rolarLista;
    readonly Panel camada = new();

    static IBrush Texto => R ? W95.Preto : TemaVerde.Texto;
    static IBrush Fraco => R ? new ImmutableSolidColorBrush(Color.Parse("#404040")) : TemaVerde.TextoFraco;
    static FontFamily Fonte => CUi.Fonte;

    public TelaArbitrosCelular(Ferramentas.Contexto ctx)
    {
        this.ctx = ctx;
        Focusable = true;
        arquivo = Path.Combine(ctx.RaizDados, PastaDados.Arbitros);
        try { paises.AddRange(TextosDados.LerPaises(Path.Combine(ctx.RaizDados, PastaDados.Paises))); } catch { }
        foreach (var p in paises) nomePais[p.Codigo] = p.Nome;

        tBusca = CUi.Caixa("", 60, T("Procurar nome"));
        tBusca.TextChanged += (_, _) => Filtrar();
        tPais = Txt("", 14.5, Texto, FontWeight.Medium);
        tMostrar = Txt("", 14.5, Texto, FontWeight.Medium);
        conta = Txt("", 13, R ? W95.Preto : TemaVerde.TextoFraco, FontWeight.Normal, true);
        vazio = Txt(T("Nenhum árbitro encontrado."), 15, Fraco, FontWeight.Normal, true);
        vazio.HorizontalAlignment = HorizontalAlignment.Center;
        vazio.VerticalAlignment = VerticalAlignment.Top;
        vazio.Margin = new Thickness(16, 24);
        vazio.TextAlignment = TextAlignment.Center;

        lista = ListaVirtual<Item>(Linha, s => rolarLista = s);

        var raiz = new Grid();
        raiz.Children.Add(MontarCorpo());
        raiz.Children.Add(camada);
        Content = raiz;
        Ler();

        // o Esc da previa (o Voltar do Android vai pela Folha): fecha a folha de cima
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && camada.Children.Count > 0) { Fechar(); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    string NomePais(string cod) => nomePais.TryGetValue(cod, out var n) ? n : TextosMercado.NomePais(cod);

    static TextBlock Txt(string s, double tam, IBrush cor, FontWeight peso = FontWeight.Normal, bool quebra = false) => CUi.Txt(s, tam, cor, peso, quebra);

    // ---------------------------------------------------------------- montagem
    Control MontarCorpo()
    {
        var filtros = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
        // (uma grade e nao uma fila: o nome comprido termina em reticencias)
        var spPais = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        bandeiraFiltro.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(tPais, 1);
        spPais.Children.Add(bandeiraFiltro);
        spPais.Children.Add(tPais);
        var sPais = Seletor(spPais, EscolherFiltroPais);
        filtros.Children.Add(sPais);
        var sMostrar = Seletor(tMostrar, EscolherMostrar);
        Grid.SetColumn(sMostrar, 1);
        filtros.Children.Add(sMostrar);

        var topo = new StackPanel { Spacing = 8, Margin = new Thickness(12, 10, 12, 8), Children = { tBusca, filtros, conta } };

        // a lista: no Moderno, o cartao arredondado; no Retro, a caixa branca afundada (TListBox)
        var miolo = new Panel { Children = { lista, vazio } };
        Control caixa = R
            ? new Border { Background = W95.Branco, Padding = new Thickness(2), ClipToBounds = true, BoxShadow = Afundada, Child = miolo, Margin = new Thickness(12, 0) }
            : new Border
            {
                Background = TemaVerde.CartaoHub, CornerRadius = new CornerRadius(14), BorderBrush = TemaVerde.Borda, BorderThickness = new Thickness(1),
                ClipToBounds = true, Child = miolo, Margin = new Thickness(12, 0),
            };

        var novo = R ? CR95.Botao(T("Novo"), null, true, Novo) : new CUi.Botao(T("Novo árbitro"), true, Novo);
        var opcoes = R ? CR95.Botao(T("Opções"), "Botoes/CONFIGURAR.png", false, AbrirOpcoes, false) : new CUi.Botao(T("Opções"), false, AbrirOpcoes);
        var rod = CUi.Botoes(opcoes, novo);
        var rodape = new Border
        {
            Padding = new Thickness(12, 10, 12, 12), Child = rod, Margin = new Thickness(0, 10, 0, 0),
            Background = R ? W95.Face : TemaVerde.BarraHub, BorderBrush = R ? W95.Sombra : TemaVerde.Borda, BorderThickness = new Thickness(0, 1, 0, 0),
            BoxShadow = R ? BoxShadows.Parse("inset 0 1 0 0 #FFFFFF") : default,
        };

        var d = new DockPanel { Background = R ? W95.Face : Brushes.Transparent };
        DockPanel.SetDock(topo, Dock.Top);
        d.Children.Add(topo);
        DockPanel.SetDock(rodape, Dock.Bottom);
        d.Children.Add(rodape);
        d.Children.Add(caixa);
        return d;
    }

    // a lista que so monta as linhas que aparecem (sao mais de mil arbitros)
    static ItemsControl ListaVirtual<TI>(Func<TI, Control> linha, Action<ScrollViewer> rolar) where TI : class => new()
    {
        Template = new FuncControlTemplate<ItemsControl>((_, ns) =>
        {
            var s = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                // (o painel vai no ItemsPresenter: o ItemsPanel do ItemsControl so chega a ele pelo tema)
                Content = new ItemsPresenter { Name = "PART_ItemsPresenter", ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()) }.RegisterInNameScope(ns),
            };
            rolar(s);
            return s;
        }),
        ItemTemplate = new FuncDataTemplate<TI>((it, _) => it == null ? new Panel() : linha(it), false),
    };

    // a caixa de lista que se toca (TComboBox no Retro; a pilula com a seta no Moderno)
    static Clicavel Seletor(Control conteudo, Action abrir)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        g.Children.Add(conteudo);
        conteudo.VerticalAlignment = VerticalAlignment.Center;
        Clicavel c;
        if (R)
        {
            conteudo.Margin = new Thickness(8, 0, 4, 0);
            var seta = new Border
            {
                Width = 32, Background = new ImmutableSolidColorBrush(Color.Parse("#C0C0C0")), BoxShadow = Solto, Margin = new Thickness(0, 2, 2, 2),
                Child = new Pintor((dc, s) =>
                {
                    double cx = Math.Floor(s.Width / 2), cy = Math.Floor(s.Height / 2);
                    for (int i = 0; i < 6; i++) dc.FillRectangle(W95.Preto, new Rect(cx - 5.5 + i, cy - 3 + i, 11 - 2 * i, 1));
                }),
            };
            Grid.SetColumn(seta, 1);
            g.Children.Add(seta);
            c = new Clicavel { Background = W95.Branco, BoxShadow = Afundada, MinHeight = 44, Padding = new Thickness(2, 0, 0, 0), Child = g, CorFoco = null };
        }
        else
        {
            var seta = Txt("▾", 16, TemaVerde.TextoFraco);
            seta.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(seta, 1);
            g.Children.Add(seta);
            c = new Clicavel
            {
                Background = TemaVerde.BotaoSecundario, Hover = TemaVerde.Hover, BorderBrush = TemaVerde.Borda, BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(12), MinHeight = 44, Padding = new Thickness(12, 4), Child = g, CorFoco = TemaVerde.Destaque,
            };
        }
        c.Clique += abrir;
        return c;
    }

    // a marca de ativo (24 pontos num alvo de 44): o visto amarelo no Moderno, a caixa do
    // TCheckBox no Retro
    Control Marca(bool ativo, Action tocar)
    {
        if (R)
        {
            var m = CR95.Marca("", ativo, _ => tocar());
            m.Width = 40; m.Padding = new Thickness(8, 4, 0, 4);
            m.VerticalAlignment = VerticalAlignment.Center;
            return m;
        }
        var caixa = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Background = ativo ? TemaVerde.Destaque : Brushes.Transparent, BorderBrush = ativo ? TemaVerde.Ouro : TemaVerde.TextoFraco, BorderThickness = new Thickness(2),
            Child = ativo ? Ui.Icone("visto", 16, TemaVerde.SobreDestaque) : null,
        };
        var c = new Clicavel { Width = 44, Height = 44, Background = Brushes.Transparent, CorFoco = null, Child = caixa, VerticalAlignment = VerticalAlignment.Center };
        c.Clique += tocar;
        return c;
    }

    static Control BandeiraOu(string pais, double tam)
    {
        if (CUi.Bandeira(pais, tam) is { } b) return b;
        var t = Txt(pais, 11, R ? W95.Preto : TemaVerde.TextoFraco, FontWeight.Bold);
        t.Width = tam + 8;
        return t;
    }

    Control Linha(Item it)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), MinHeight = 58 };
        g.Children.Add(Marca(it.Ativo, () => { it.Ativo = !it.Ativo; Mudou(it); }));
        var b = BandeiraOu(it.Pais, 22);
        b.Margin = new Thickness(4, 0, 10, 0);
        b.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(b, 1);
        g.Children.Add(b);
        var nome = Txt(it.Nome.Length > 0 ? it.Nome : T("(sem nome)"), 15.5, it.Ativo ? Texto : Fraco, R ? FontWeight.Normal : FontWeight.Medium);
        if (!it.Ativo) nome.FontStyle = FontStyle.Italic;
        var sub = Txt($"{it.Pais} · {NomePais(it.Pais)} · {TelaArbitros.Origem(it.Origem)}", 12.5, Fraco);
        var t = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { nome, sub } };
        Grid.SetColumn(t, 2);
        g.Children.Add(t);
        if (!R)
        {
            var seta = Txt("›", 22, TemaVerde.TextoFraco);
            seta.Margin = new Thickness(8, 0, 4, 0);
            Grid.SetColumn(seta, 3);
            g.Children.Add(seta);
        }
        var c = new Clicavel
        {
            Background = Brushes.Transparent, Hover = R ? null : TemaVerde.Hover, CorFoco = null, Child = g, Padding = new Thickness(2, 0, 8, 0),
            BorderBrush = R ? null : TemaVerde.Linha, BorderThickness = new Thickness(0, 0, 0, R ? 0 : 1),
        };
        c.Clique += () => Editar(it);
        return c;
    }

    // ---------------------------------------------------------------- dados
    void Ler()
    {
        itens.Clear();
        try
        {
            foreach (var a in TextosDados.LerArbitrosEditaveis(arquivo))
                itens.Add(new Item { Pais = a.Pais, Nome = a.Nome, Origem = a.Origem, Ativo = a.Ativo });
        }
        catch (Exception ex) { estado = T("Não consegui ler {0}: {1}", PastaDados.Arbitros, ex.Message); }
        if (estado.Length == 0)
            estado = File.Exists(arquivo) ? T("Arquivo: {0}", arquivo) : T("Ainda sem lista: a do jogo entra ao repor.");
        if (filtroPais != null && !itens.Any(i => i.Pais == filtroPais)) filtroPais = null;
        Filtrar();
    }

    List<Item> Visiveis()
    {
        var termo = TelaArbitros.SemAcento((tBusca.Text ?? "").Trim());
        return itens.Where(Passa(termo)).ToList();
    }

    Func<Item, bool> Passa(string termo) => i =>
        (termo.Length == 0 || TelaArbitros.SemAcento(i.Nome).Contains(termo, StringComparison.OrdinalIgnoreCase)) &&
        (filtroPais == null || i.Pais == filtroPais) &&
        (mostrar != 1 || i.Ativo) && (mostrar != 2 || !i.Ativo);

    // refaz a lista visivel (do alto); manter: o arbitro que deve aparecer (o novo)
    void Filtrar(Item? manter = null)
    {
        var v = Visiveis();
        // (uma colecao nova de uma vez: mil Add seguidos refaziam a lista a cada um)
        visiveis = new ObservableCollection<Item>(v);
        lista.ItemsSource = visiveis;
        if (rolarLista != null) rolarLista.Offset = default;
        if (manter != null && v.IndexOf(manter) is var k and >= 0)
            Dispatcher.UIThread.Post(() => lista.ScrollIntoView(k), DispatcherPriority.Loaded);
        Rotulos();
    }

    // depois de mudar um arbitro: so a linha dele (a lista fica onde estava)
    void Mudou(Item it)
    {
        int k = visiveis.IndexOf(it);
        bool passa = Passa(TelaArbitros.SemAcento((tBusca.Text ?? "").Trim()))(it);
        if (k >= 0)
        {
            visiveis.RemoveAt(k);
            if (passa) visiveis.Insert(k, it);
        }
        Gravar();
        Rotulos();
    }

    void Rotulos()
    {
        if (filtroPais != null && !itens.Any(i => i.Pais == filtroPais)) filtroPais = null;
        tPais.Text = filtroPais == null ? T("Todos os países ({0})", itens.Select(i => i.Pais).Distinct().Count()) : NomePais(filtroPais);
        bandeiraFiltro.Content = filtroPais == null ? null : CUi.Bandeira(filtroPais, 20);
        bandeiraFiltro.IsVisible = bandeiraFiltro.Content != null;
        tMostrar.Text = Mostrar[mostrar];
        int ativos = itens.Count(i => i.Ativo);
        int np = itens.Where(i => i.Ativo).Select(i => i.Pais).Distinct().Count();
        var s = T("{0} árbitros ativos em {1} países · {2} desativados", ativos, np, itens.Count - ativos);
        if (visiveis.Count != itens.Count) s += "\n" + T("Mostrando {0} de {1}", visiveis.Count, itens.Count);
        conta.Text = s;
        vazio.IsVisible = visiveis.Count == 0;
    }

    static string[] Mostrar => new[] { T("Todos"), T("Só os ativos"), T("Só os desativados") };

    void Gravar()
    {
        try
        {
            TextosDados.GravarArbitrosEditaveis(arquivo, itens.Where(i => i.Nome.Trim().Length > 0 && i.Pais.Trim().Length > 0)
                .Select(i => new TextosDados.ArbitroEditavel(i.Pais.Trim().ToUpperInvariant(), i.Nome.Trim(), i.Origem, i.Ativo)));
            estado = T("Gravado em {0} (o anterior fica em .bak).", PastaDados.Arbitros);
        }
        catch (Exception ex)
        {
            estado = T("Não consegui gravar: {0}", ex.Message);
            ctx.Mensagem(estado);
        }
    }

    // ---------------------------------------------------------------- folhas
    void Abrir(CUi.Folha f)
    {
        var veu = new Panel { Background = TemaVerde.Veu, Children = { f } };
        // o toque fora da folha a fecha (como o Voltar)
        if (!f.Inteira)
            veu.PointerPressed += (_, e) => { if (e.Source == veu) { e.Handled = true; f.AoVoltar?.Invoke(); } };
        camada.Children.Add(veu);
    }

    void Fechar()
    {
        if (camada.Children.Count > 0) camada.Children.RemoveAt(camada.Children.Count - 1);
        Focus();
    }

    static Control Rotulado(string rotulo, Control controle)
    {
        if (R) return CR95.Rotulado(rotulo, controle);
        return new StackPanel { Spacing = 6, Children = { Txt(rotulo, 13.5, TemaVerde.TextoFraco, FontWeight.SemiBold), controle } };
    }

    // uma lista para escolher (o pais do filtro, o pais do arbitro): a tela toda, com a procura
    // em cima. opcoes: (pais para a bandeira, texto, texto pequeno embaixo)
    void EscolherDaLista(string titulo, IReadOnlyList<(string? Pais, string Texto, string Sub)> opcoes, int atual, Action<int> escolheu)
    {
        var caixa = CUi.Caixa("", 40, T("Procurar país"));
        var vis = new List<int>();
        ScrollViewer? rolar = null;
        CUi.Folha? folha = null;
        Control LinhaOp(object o)
        {
            int k = (int)o;
            var (pais, texto, sub) = opcoes[k];
            bool sel = k == atual;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), MinHeight = 52 };
            if (pais != null)
            {
                var b = BandeiraOu(pais, 22);
                b.Margin = new Thickness(0, 0, 12, 0);
                b.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(b);
            }
            var cor = R ? (sel ? W95.Branco : W95.Preto) : TemaVerde.Texto;
            var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
            t.Children.Add(Txt(texto, 15.5, cor, sel ? FontWeight.Bold : FontWeight.Normal));
            if (sub.Length > 0) t.Children.Add(Txt(sub, 12.5, R ? (sel ? W95.Branco : Fraco) : TemaVerde.TextoFraco));
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            if (sel && !R)
            {
                var v = Ui.Icone("visto", 18, TemaVerde.Destaque);
                v.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(v, 2);
                g.Children.Add(v);
            }
            var c = new Clicavel
            {
                Background = sel ? (R ? W95.Destaque : TemaVerde.Selecao) : Brushes.Transparent, Hover = R ? null : TemaVerde.Hover, CorFoco = null,
                Child = g, Padding = new Thickness(12, 0), BorderBrush = R ? null : TemaVerde.Linha, BorderThickness = new Thickness(0, 0, 0, R ? 0 : 1),
            };
            c.Clique += () => { Fechar(); escolheu(k); };
            return c;
        }
        var ic = ListaVirtual<object>(LinhaOp, s => rolar = s);
        void Refazer()
        {
            var termo = TelaArbitros.SemAcento((caixa.Text ?? "").Trim());
            vis = Enumerable.Range(0, opcoes.Count).Where(i => termo.Length == 0
                || TelaArbitros.SemAcento(opcoes[i].Texto).Contains(termo, StringComparison.OrdinalIgnoreCase)
                || (opcoes[i].Pais ?? "").Contains(termo, StringComparison.OrdinalIgnoreCase)).ToList();
            ic.ItemsSource = vis;
            if (rolar != null) rolar.Offset = default;
        }
        caixa.TextChanged += (_, _) => Refazer();
        Refazer();
        Control moldura = R
            ? new Border { Background = W95.Branco, Padding = new Thickness(2), ClipToBounds = true, BoxShadow = Afundada, Child = ic }
            : new Border
            {
                Background = TemaVerde.CartaoHub, CornerRadius = new CornerRadius(14), BorderBrush = TemaVerde.Borda, BorderThickness = new Thickness(1),
                ClipToBounds = true, Child = ic,
            };
        var cab = new Border { Padding = new Thickness(0, 0, 0, 10), Child = caixa };
        // (uma folha alta, quase a tela toda: a barra da ferramenta fica a vista em cima)
        var d = new DockPanel { Height = Math.Max(320, Bounds.Height * 0.88 - 70) };
        DockPanel.SetDock(cab, Dock.Top);
        d.Children.Add(cab);
        d.Children.Add(moldura);
        folha = new CUi.Folha(titulo, d, rolar: false, fechar: Fechar);
        Abrir(folha);
        if (atual > 0) Dispatcher.UIThread.Post(() => ic.ScrollIntoView(vis.IndexOf(atual)), DispatcherPriority.Loaded);
    }

    void EscolherFiltroPais()
    {
        var grupos = itens.GroupBy(i => i.Pais).OrderBy(g => NomePais(g.Key), StringComparer.CurrentCultureIgnoreCase).ToList();
        var ops = new List<(string?, string, string)> { (null, T("Todos os países ({0})", grupos.Count), "") };
        ops.AddRange(grupos.Select(g => ((string?)g.Key, NomePais(g.Key), $"{g.Key} · {g.Count(i => i.Ativo)}/{g.Count()}")));
        int atual = filtroPais == null ? 0 : grupos.FindIndex(g => g.Key == filtroPais) + 1;
        EscolherDaLista(T("País"), ops, atual, k =>
        {
            filtroPais = k == 0 ? null : grupos[k - 1].Key;
            Filtrar();
        });
    }

    void EscolherMostrar()
    {
        var nomes = Mostrar;
        Control corpo;
        if (R)
        {
            var (painel, _, _) = CR95.Radios(nomes, mostrar, k => { Fechar(); mostrar = k; Filtrar(); });
            corpo = painel;
        }
        else
        {
            var linhas = new List<Control>();
            for (int i = 0; i < nomes.Length; i++)
            {
                int k = i;
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                g.Children.Add(Txt(nomes[i], 16, TemaVerde.Texto, k == mostrar ? FontWeight.Bold : FontWeight.Normal));
                if (k == mostrar)
                {
                    var v = Ui.Icone("visto", 18, TemaVerde.Destaque);
                    Grid.SetColumn(v, 1);
                    g.Children.Add(v);
                }
                linhas.Add(CUi.Toque(g, () => { Fechar(); mostrar = k; Filtrar(); }, 52));
            }
            corpo = CUi.Grupo(linhas);
        }
        Abrir(new CUi.Folha(T("Mostrar"), corpo, fechar: Fechar));
    }

    // a folha do arbitro (novo == true: ainda nao esta na lista; entra ao gravar)
    void Editar(Item it, bool novo = false)
    {
        string pais = it.Pais, origem = it.Origem;
        bool ativo = it.Ativo;
        var nome = CUi.Caixa(it.Nome, 60);
        var aviso = Txt("", 13.5, R ? W95.Vermelho : TemaVerde.Vermelho, FontWeight.Normal, true);
        var tPaisEd = Txt("", 15.5, Texto, FontWeight.Medium);
        var bPais = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        var cabPais = new ContentControl();
        bPais.Margin = new Thickness(0, 0, 10, 0);
        Grid.SetColumn(tPaisEd, 1);
        var spPais = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { bPais, tPaisEd } };
        CUi.Botao? gravar = null;

        void Atualizar()
        {
            tPaisEd.Text = pais.Length > 0 ? $"{pais} - {NomePais(pais)}" : "";
            bPais.Content = BandeiraOu(pais, 22);
            if (!R) cabPais.Content = BandeiraOu(pais, 28);
            var s = nome.Text ?? "";
            var avisos = new List<string>();
            if (s.Trim().Length == 0) avisos.Add(T("Sem nome: não é gravado."));
            if (s.Any(c => c > 255)) avisos.Add(T("Letras fora do Latin-1 perdem o acento no jogo."));
            if (s.Length > 40) avisos.Add(T("O jogo usa só as primeiras 40 letras."));
            if (pais.Length > 0 && !nomePais.ContainsKey(pais)) avisos.Add(T("O país {0} não está em countries.tsv.", pais));
            aviso.Text = string.Join("\n", avisos);
            aviso.IsVisible = avisos.Count > 0;
            if (gravar != null) gravar.Habilitado = s.Trim().Length > 0 && pais.Length > 0;
        }
        nome.TextChanged += (_, _) => Atualizar();

        var seletor = Seletor(spPais, () =>
        {
            var ops = paises.Select(p => ((string?)p.Codigo, p.Nome, p.Codigo)).ToList();
            EscolherDaLista(T("País"), ops, paises.FindIndex(p => p.Codigo == pais), k => { pais = paises[k].Codigo; Atualizar(); });
        });

        var corpo = new StackPanel { Spacing = R ? 4 : 14 };
        corpo.Children.Add(Rotulado(T("País"), seletor));
        corpo.Children.Add(Rotulado(T("Nome"), nome));
        corpo.Children.Add(aviso);
        var explica = T("Os jogos novos usam os árbitros ativos. Se escolher só um país no jogo novo, ficam só os desse país.");
        if (R)
        {
            corpo.Children.Add(CR95.Marca(T("Ativo (usado nos jogos novos)"), ativo, v => ativo = v));
            corpo.Children.Add(CR95.Txt(explica, 13, W95.Cinzento));
        }
        else
            corpo.Children.Add(CUi.Grupo(new[] { CUi.Interruptor(T("Ativo (usado nos jogos novos)"), explica, ativo, v => ativo = v) }));
        if (!novo)
            corpo.Children.Add(R ? CR95.Txt(T("Origem") + ": " + TelaArbitros.Origem(origem), 14, W95.Preto) : CUi.Linha_(T("Origem"), TelaArbitros.Origem(origem)));

        void Gravar_()
        {
            var s = (nome.Text ?? "").Trim();
            if (s.Length == 0 || pais.Length == 0) return;
            it.Nome = s; it.Pais = pais; it.Ativo = ativo;
            Fechar();
            if (novo)
            {
                // entra depois do ultimo do mesmo pais (a lista do arquivo fica agrupada)
                int k = itens.FindLastIndex(i => i.Pais == pais);
                itens.Insert(k < 0 ? itens.Count : k + 1, it);
                if (mostrar == 2 && it.Ativo) mostrar = 0;
                if (filtroPais != null && filtroPais != pais) filtroPais = null;
                tBusca.Text = "";
                Gravar();
                Filtrar(it);
            }
            else if (visiveis.Contains(it)) Mudou(it);
            else { Gravar(); Filtrar(); }
        }

        Control rodape;
        if (R)
        {
            gravar = CR95.Botao(T("OK"), "Botoes/BBOK.png", true, Gravar_, false);
            var cancelar = CR95.Botao(T("Cancelar"), "Botoes/BBCANCEL.png", false, Fechar, false);
            rodape = novo ? CUi.Botoes(gravar, cancelar)
                : new StackPanel { Spacing = 8, Children = { CUi.Botoes(gravar, cancelar), CR95.Botao(T("Remover"), "Botoes/EXCLUIR.png", false, () => PerguntarRemover(it), false) } };
        }
        else
        {
            gravar = new CUi.Botao(T("Gravar"), true, Gravar_);
            rodape = novo ? CUi.Botoes(new CUi.Botao(T("Cancelar"), false, Fechar), gravar)
                : CUi.Botoes(new CUi.Botao(T("Remover"), false, () => PerguntarRemover(it)), gravar);
        }
        Atualizar();
        var titulo = novo ? T("Novo árbitro") : T("Árbitro");
        Abrir(new CUi.Folha(titulo, corpo, rodape, null, R ? null : cabPais, fechar: Fechar));
        if (novo) Dispatcher.UIThread.Post(() => nome.Focus(), DispatcherPriority.Loaded);
    }

    void PerguntarRemover(Item it)
    {
        void Sim()
        {
            camada.Children.Clear();
            itens.Remove(it);
            visiveis.Remove(it);
            Gravar();
            Rotulos();
            Focus();
        }
        var texto = T("Remover {0} da lista de árbitros?", it.Nome.Length > 0 ? it.Nome : T("(sem nome)"));
        Control rod = R
            ? CUi.Botoes(CR95.Botao(T("Sim"), "Botoes/BBYES.png", true, Sim, false), CR95.Botao(T("Não"), "Botoes/BBNO.png", false, Fechar, false))
            : CUi.Botoes(new CUi.Botao(T("Não"), false, Fechar), new CUi.Botao(T("Remover"), true, Sim));
        Abrir(new CUi.Folha(T("Remover"), Txt(texto, 15.5, Texto, FontWeight.Normal, true), rod, fechar: Fechar));
    }

    void Novo()
    {
        var pais = filtroPais ?? paises.FirstOrDefault()?.Codigo ?? "POR";
        Editar(new Item { Pais = pais, Nome = "", Origem = "user", Ativo = true }, true);
    }

    void AbrirOpcoes()
    {
        var corpo = new StackPanel { Spacing = 12 };
        corpo.Children.Add(Txt(T("Os jogos novos usam os árbitros ativos. Se escolher só um país no jogo novo, ficam só os desse país."), 14.5, Texto, FontWeight.Normal, true));
        corpo.Children.Add(Txt(estado, 12.5, Fraco, FontWeight.Normal, true));
        var repor = R ? CR95.Botao(T("Repor a lista do jogo"), null, false, PerguntarRepor) : new CUi.Botao(T("Repor a lista do jogo"), false, PerguntarRepor);
        repor.HorizontalAlignment = HorizontalAlignment.Stretch;
        corpo.Children.Add(repor);
        if (DadosJogo.PodeAbrirPasta)
        {
            var abrir = R ? CR95.Botao(T("Abrir pasta de dados"), null, false, () => DadosJogo.AbrirPasta(ctx.RaizDados))
                : new CUi.Botao(T("Abrir pasta de dados"), false, () => DadosJogo.AbrirPasta(ctx.RaizDados));
            abrir.HorizontalAlignment = HorizontalAlignment.Stretch;
            corpo.Children.Add(abrir);
        }
        Abrir(new CUi.Folha(T("Opções"), corpo, fechar: Fechar));
    }

    void PerguntarRepor()
    {
        void Sim() { camada.Children.Clear(); Repor(); Focus(); }
        var texto = T("Repor a lista de árbitros que vem com o jogo? A sua lista atual fica guardada em {0}.bak.", PastaDados.Arbitros);
        Control rod = R
            ? CUi.Botoes(CR95.Botao(T("Repor"), "Botoes/BBYES.png", true, Sim, false), CR95.Botao(T("Cancelar"), "Botoes/BBCANCEL.png", false, Fechar, false))
            : CUi.Botoes(new CUi.Botao(T("Cancelar"), false, Fechar), new CUi.Botao(T("Repor"), true, Sim));
        Abrir(new CUi.Folha(T("Repor a lista do jogo"), Txt(texto, 15.5, Texto, FontWeight.Normal, true), rod, fechar: Fechar));
    }

    void Repor()
    {
        try
        {
            using var s = AssetLoader.Open(new Uri(TelaArbitros.Pacote));
            using var m = new MemoryStream();
            s.CopyTo(m);
            if (File.Exists(arquivo)) File.Copy(arquivo, arquivo + ".bak", true);
            File.WriteAllBytes(arquivo, m.ToArray());
        }
        catch (Exception ex) { estado = T("Não consegui repor: {0}", ex.Message); ctx.Mensagem(estado); return; }
        estado = "";
        Ler();
        estado = T("Lista do jogo reposta ({0} árbitros ativos).", itens.Count(i => i.Ativo));
        ctx.Mensagem(estado);
    }
}
