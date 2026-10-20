using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using RetroFoot.Motor.Arquivos;
using RetroFoot.Motor.Base;
using static RetroFoot.App.Idioma;
using Rgba = RetroFoot.Motor.Arquivos.EscudoProcessador.Rgba;
using TEE = RetroFoot.App.TelaEditorEquipas;

namespace RetroFoot.App;

// O Editor de Equipas no celular (Ferramentas.RegistrarCelular): o mesmo do computador
// (FerramentaEditorEquipas.cs, EditorEquipasEscudo.cs), em pe e ao toque, nas duas peles do kit
// (CelularUi.cs). Duas paginas: a lista das equipas (o pais, a busca, Nova e o menu com os
// patches e os logos das ligas) e a equipa aberta, com as abas Equipa / Jogadores / Escudo e
// Desfazer / Gravar fixos embaixo. O resto (a ficha do jogador, as cores, o pais, o escudo, a
// colecao, os patches) abre em folhas que sobem de baixo. As regras e o modelo sao os do motor
// (EquipaEditavel, RegrasEquipa); o escudo e os logos das ligas gravam pelo codigo do computador
// (TelaEditorEquipas.GravarEscudo, GravarLogoLiga).
//  - Moderno: os cartoes verdes e amarelos do hub, as abas em pilulas;
//  - Retro: a janela do computador: a face cinza, as abas do TTabbedNotebook, as molduras de
//    grupo, as listas brancas afundadas e o nome nas cores da equipa.
static class FerramentaEditorEquipasCelular
{
    [ModuleInitializer]
    internal static void Registar() =>
        Ferramentas.RegistrarCelular(Ferramentas.EditorEquipas, ctx => new EditorEquipasCelular(ctx));
}

sealed class EditorEquipasCelular : UserControl
{
    sealed class ItemEquipa
    {
        public string Caminho = "";
        public EquipaEditavel E = null!;
        public bool Valida;
        public string Pasta => EquipaJson.PastaPais(E.Pais);
    }

    readonly Ferramentas.Contexto ctx;
    readonly string raiz;
    RegrasEquipa regras = null!;
    List<TextosDados.Pais> paises = new();
    Dictionary<string, string> nomePais = new(StringComparer.OrdinalIgnoreCase);
    readonly List<ItemEquipa> itens = new();
    readonly List<string> ilegiveis = new();

    // a lista: o pais escolhido (a pasta; null = todos) e a busca
    string? filtro;
    string busca = "";

    // a equipa aberta: a copia em edicao e a gravada (null = nova), o escudo escolhido por gravar
    ItemEquipa? atual;
    EquipaEditavel? edit;
    TEE.EscolhaEscudo? pendente;
    int aba;

    readonly Grid raizUi = new();
    readonly ContentControl pagina = new();
    readonly Panel camada = new() { IsVisible = false };
    readonly List<CUi.Folha> pilha = new();

    static bool R => CUi.Retro;
    static readonly string[] Setores = { "Goleiros", "Defensores", "Meio-campistas", "Atacantes" };
    static readonly string[] NomesPos = { "Guarda-redes", "Defesa", "Médio", "Avançado" };

    public EditorEquipasCelular(Ferramentas.Contexto ctx)
    {
        this.ctx = ctx;
        raiz = ctx.RaizDados;
        FontFamily = CUi.Fonte;
        Focusable = true;
        raizUi.Children.Add(pagina);
        raizUi.Children.Add(camada);
        Content = raizUi;
        Carregar();
        var porPais = itens.GroupBy(i => i.Pasta).ToDictionary(x => x.Key, x => x.Count());
        filtro = porPais.Count == 0 ? null : porPais.ContainsKey("POR") ? "POR" : porPais.MaxBy(p => p.Value).Key;
        MostrarLista();
        AttachedToVisualTree += (_, _) =>
        {
            // a barra do celular (o titulo e o voltar) fica por conta desta tela: o titulo muda
            // com a equipa aberta e o voltar pergunta antes de perder o que nao foi gravado
            if (Parent is Panel d) foreach (var c in d.Children) if (c != this) c.IsVisible = false;
            CUi.Folha.VoltarPagina = () => { Voltar(); return true; };
            Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Loaded);
        };
    }

    // Esc (no computador, a previa): como o Voltar do Android
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape) return;
        e.Handled = true;
        if (!CUi.Folha.Voltar()) Voltar();
    }

    void Voltar()
    {
        if (pilha.Count > 0) { pilha[^1].AoVoltar?.Invoke(); return; }
        if (edit != null) _ = FecharEquipa();
        else ctx.Voltar();
    }

    // ---------------------------------------------------------------- dados
    void Carregar()
    {
        paises = TextosDados.LerPaises(Path.Combine(raiz, PastaDados.Paises));
        nomePais = new(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paises) nomePais[p.Codigo] = p.Nome;
        regras = RegrasEquipa.DaPasta(raiz);
        itens.Clear();
        ilegiveis.Clear();
        foreach (var j in PastaDados.JsonsDeEquipas(raiz))
        {
            try
            {
                var e = EquipaEditavel.Ler(j);
                itens.Add(new ItemEquipa { Caminho = j, E = e, Valida = regras.Validar(e).Count == 0 });
            }
            catch (Exception x) { ilegiveis.Add($"{Path.GetRelativePath(raiz, j)}: {x.Message}"); }
        }
        Ordenar();
    }

    void Ordenar() => itens.Sort((a, b) =>
    {
        int c = string.Compare(a.Pasta, b.Pasta, StringComparison.Ordinal);
        if (c != 0) return c;
        c = b.E.Nivel.CompareTo(a.E.Nivel);
        return c != 0 ? c : string.Compare(a.E.NomeAbreviado, b.E.NomeAbreviado, StringComparison.CurrentCultureIgnoreCase);
    });

    string NomePais(string cod) => nomePais.TryGetValue(cod.Trim(), out var n) ? n : cod;

    bool Alterada => edit != null && (atual == null || !edit.MesmoConteudo(atual.E) || pendente != null);

    IStorageProvider? Arquivos => TopLevel.GetTopLevel(this)?.StorageProvider;

    // ---------------------------------------------------------------- pecas
    static TextBlock Tx(string s, double tam = 15, IBrush? cor = null, bool negrito = false, bool quebra = true) =>
        CUi.Txt(s, tam, cor, negrito ? FontWeight.Bold : FontWeight.Normal, quebra);

    static IBrush Fraco => R ? W95.Cinzento : CUi.Fraco;
    static IBrush Ouro => R ? new SolidColorBrush(Color.Parse("#806000")) : new SolidColorBrush(Color.Parse("#E8B400"));

    // o botao (no Retro, com o glifo do computador quando ha)
    static CUi.Botao Bt(string t, bool principal, Action a, string? glifo = null)
    {
        var b = R && glifo != null ? CR95.Botao(t, glifo, principal, a, false) : new CUi.Botao(t, principal, a);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        return b;
    }

    // botoes um embaixo do outro (os textos compridos)
    static Control Pilha(params Control[] bs)
    {
        var s = new StackPanel { Spacing = 10 };
        foreach (var b in bs) { b.HorizontalAlignment = HorizontalAlignment.Stretch; s.Children.Add(b); }
        return s;
    }

    // um grupo de campos (Moderno: o cartao; Retro: a moldura de grupo do computador)
    static Control Secao(string? titulo, Control c) => R ? CUi.GrupoRetro(titulo, c) : CUi.Cartao_(titulo, c);

    // o rotulo em cima e o controle embaixo (e a ajuda cinza)
    static Control Campo(string rotulo, Control c, string? ajuda = null)
    {
        var p = new StackPanel { Spacing = 6 };
        p.Children.Add(Tx(rotulo, 14, R ? W95.Preto : CUi.Fraco, !R));
        p.Children.Add(c);
        if (ajuda != null) p.Children.Add(Tx(ajuda, 13, Fraco));
        return p;
    }

    // a caixa que abre uma escolha (o pais): no Retro a TComboBox branca com a seta
    static Clicavel Seletor(Control conteudo, Action toque)
    {
        var seta = R
            ? (Control)new Border
            {
                Width = 32, Background = new SolidColorBrush(Color.Parse("#C0C0C0")), Margin = new Thickness(0, 2, 2, 2),
                BoxShadow = BoxShadows.Parse("inset 1 1 0 0 #FFFFFF, inset -1 -1 0 0 #000000, inset 2 2 0 0 #DFDFDF, inset -2 -2 0 0 #808080"),
                Child = Tx("▼", 11, W95.Preto).Ajustar(t => t.HorizontalAlignment = HorizontalAlignment.Center),
            }
            : Tx("▾", 20, CUi.Fraco).Ajustar(t => t.Margin = new Thickness(0, 0, 12, 0));
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        conteudo.Margin = new Thickness(R ? 8 : 12, 0, 6, 0);
        conteudo.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(conteudo);
        Grid.SetColumn(seta, 1);
        g.Children.Add(seta);
        var c = new Clicavel { MinHeight = 46, Child = g, CorFoco = null };
        if (R)
        {
            c.Background = W95.Branco;
            c.BoxShadow = BoxShadows.Parse("inset 1 1 0 0 #808080, inset 2 2 0 0 #000000, inset -1 -1 0 0 #FFFFFF, inset -2 -2 0 0 #DFDFDF");
        }
        else
        {
            c.Background = TemaVerde.BotaoSecundario; c.Hover = TemaVerde.Hover;
            c.BorderBrush = TemaVerde.Borda; c.BorderThickness = new Thickness(1.5); c.CornerRadius = new CornerRadius(12);
        }
        c.Clique += toque;
        return c;
    }

    // a bandeira (ou o espaco dela)
    static Control Bandeira(string? cod, double tam = 22) =>
        (cod != null ? CUi.Bandeira(cod.Trim().ToUpperInvariant(), tam) : null) ?? new Border { Width = tam, Height = tam };

    static Control LinhaPais(string? cod, string texto, double tam = 15)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        s.Children.Add(Bandeira(cod));
        s.Children.Add(Tx(texto, tam, null, false, false));
        return s;
    }

    // uma marca liga/desliga (Moderno: o interruptor; Retro: a caixa de marcar do computador)
    static Control Marca(string texto, bool valor, Action<bool> mudou, bool habilitado = true) =>
        R ? CR95.Marca(texto, valor, mudou, habilitado) : CUi.Interruptor(texto, null, valor, mudou, habilitado);

    // a escolha de uma entre varias linhas (Retro: as bolinhas; Moderno: a lista marcada)
    static Control Opcoes(IReadOnlyList<string> nomes, int atual, Action<int> escolher)
    {
        if (R) return CR95.Radios(nomes, atual, escolher).Painel;
        var (lista, marcar) = CUi.ListaEscolha(nomes.Select(n => (Control)Tx(n, 15)).ToList(), escolher);
        marcar(atual);
        return lista;
    }

    // a pagina: a barra, o que fica fixo em cima, o corpo (que rola, ou nao) e o rodape
    Control Pagina(Control barra, Control? topo, Control corpo, Control? rodape, out ScrollViewer? rolar, bool rola = true)
    {
        var d = new DockPanel { Background = R ? W95.Face : CUi.Fundo };
        DockPanel.SetDock(barra, Dock.Top);
        d.Children.Add(barra);
        if (topo != null) { DockPanel.SetDock(topo, Dock.Top); d.Children.Add(topo); }
        if (rodape != null)
        {
            var r = new Border
            {
                Background = R ? W95.Face : TemaVerde.BarraHub, Padding = new Thickness(12, 10, 12, 12), Child = rodape,
                BorderBrush = R ? W95.Sombra : CUi.Linha, BorderThickness = new Thickness(0, 1, 0, 0),
                BoxShadow = R ? BoxShadows.Parse("inset 0 1 0 0 #FFFFFF") : default,
            };
            DockPanel.SetDock(r, Dock.Bottom);
            d.Children.Add(r);
        }
        rolar = null;
        if (rola)
        {
            rolar = new ScrollViewer
            {
                Content = new Border { Padding = new Thickness(12, 10, 12, 16), Child = corpo, VerticalAlignment = VerticalAlignment.Top },
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            d.Children.Add(rolar);
        }
        else d.Children.Add(corpo);
        return d;
    }

    // o botao de icone da barra de cima (o menu ⋮)
    static Clicavel BotaoBarra(string texto, Action a)
    {
        var c = new Clicavel
        {
            Width = 44, Height = 44, Background = Brushes.Transparent, CorFoco = null, Margin = new Thickness(0, 0, -8, 0),
            Child = new TextBlock { Text = texto, FontSize = 24, FontWeight = FontWeight.Bold, Foreground = CUi.TextoBarra, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        c.Clique += a;
        return c;
    }

    // o escudo de uma equipa (o logo, ou o escudo desenhado nas cores)
    static Control EscudoDe(EquipaEditavel e, double tam)
    {
        var b = Escudos.Escudo(e.Pais, e.NomeCompleto, e.NomeAbreviado);
        return Escudos.Controle(b, tam) ?? TemaEditor.EscudoCores(e.CorLetra, e.CorFundo, tam);
    }

    // o nome na caixa das cores da equipa (como nas listas do jogo classico)
    static Border NomeNasCores(EquipaEditavel e, string nome, double tam = 15) => new()
    {
        Background = TemaEditor.Pincel(e.CorFundo), Padding = new Thickness(6, 2), HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = nome, FontFamily = CUi.Fonte, FontSize = tam * Acessibilidade.Texto, FontWeight = FontWeight.Bold,
            Foreground = TemaEditor.Pincel(e.CorLetra), Tag = "fixo", TextTrimming = TextTrimming.CharacterEllipsis,
        },
    };

    // ---------------------------------------------------------------- folhas
    // as janelas sobem de baixo, uma por cima da outra; o Voltar (e o toque no veu) fecha a de
    // cima
    // (alta: a altura toda, para nao pular enquanto a busca filtra a lista)
    // (rolar: false quando o corpo rola por si, com a busca fixa em cima)
    CUi.Folha Abrir(string titulo, Control corpo, Control? rodape = null, string? sub = null, Action? aoFechar = null, bool alta = false, bool rolar = true)
    {
        CUi.Folha f = null!;
        f = new CUi.Folha(titulo, corpo, rodape, sub, rolar: rolar, fechar: aoFechar ?? (() => Fechar(f)));
        f.MaxHeight = Math.Max(380, (Bounds.Height > 0 ? Bounds.Height : 700) * 0.92);
        if (alta) f.Height = f.MaxHeight;
        // (o foco na folha: o Esc do teclado chega a ela)
        f.Focusable = true;
        pilha.Add(f);
        MostrarFolhas();
        return f;
    }

    void Fechar(CUi.Folha f)
    {
        pilha.Remove(f);
        MostrarFolhas();
    }

    void FecharTodas()
    {
        pilha.Clear();
        MostrarFolhas();
    }

    void MostrarFolhas()
    {
        camada.Children.Clear();
        if (pilha.Count == 0) { camada.IsVisible = false; Focus(); return; }
        var veu = new Border { Background = TemaVerde.Veu };
        var cima = pilha[^1];
        veu.PointerPressed += (_, e) => { e.Handled = true; cima.AoVoltar?.Invoke(); };
        camada.Children.Add(veu);
        camada.Children.Add(cima);
        camada.IsVisible = true;
        Dispatcher.UIThread.Post(() => { if (pilha.Count > 0 && pilha[^1] == cima && !cima.IsKeyboardFocusWithin) cima.Focus(); }, DispatcherPriority.Loaded);
    }

    void Mensagem(string texto)
    {
        CUi.Folha f = null!;
        f = Abrir(T("Editor de Equipas"), Tx(texto, 15), CUi.Botoes(Bt(T("OK"), true, () => Fechar(f), "Botoes/BBOK.png")));
    }

    Task<bool> Confirmar(string texto)
    {
        var tcs = new TaskCompletionSource<bool>();
        CUi.Folha f = null!;
        void Fim(bool r) { Fechar(f); tcs.TrySetResult(r); }
        f = Abrir(T("Editor de Equipas"), Tx(texto, 15.5),
            CUi.Botoes(Bt(T("Não"), false, () => Fim(false), "Botoes/BBNO.png"), Bt(T("Sim"), true, () => Fim(true), "Botoes/BBYES.png")),
            aoFechar: () => Fim(false));
        return tcs.Task;
    }

    // ---------------------------------------------------------------- a lista
    ContentControl? listaHost;

    void MostrarLista()
    {
        edit = null;
        pendente = null;
        var barra = CUi.BarraTopo(T("Editor de Equipas"), null, ctx.Voltar, BotaoBarra("⋮", MenuMais));
        var pais = Seletor(LinhaPais(filtro, filtro == null ? T("Todos os países") : $"{NomePais(filtro)} ({itens.Count(i => i.Pasta == filtro)})"),
            () => EscolherPais(T("País"), filtro, true, c => { filtro = c; MostrarLista(); }));
        var caixa = CUi.Caixa(busca, 0, T("Procurar equipa…"));
        caixa.TextChanged += (_, _) => { busca = caixa.Text ?? ""; RefazerLista(); };
        var topo = new StackPanel { Spacing = 8, Margin = new Thickness(12, 10, 12, 2), Children = { pais, caixa } };
        listaHost = new ContentControl();
        RefazerLista();
        var rodape = CUi.Botoes(Bt("+  " + T("Nova"), true, () => _ = NovaEquipa()), Bt(T("Logos das ligas…"), false, () => AbrirLigas()));
        pagina.Content = Pagina(barra, topo, listaHost, rodape, out _);
    }

    List<ItemEquipa> Filtradas()
    {
        var q = LogosDados.Normalizar(busca);
        return itens.Where(i => (filtro == null || i.Pasta == filtro) &&
            (q.Length == 0 || LogosDados.Normalizar(i.E.NomeCompleto + " " + i.E.NomeAbreviado + " " + i.E.Arquivo).Contains(q))).ToList();
    }

    void RefazerLista()
    {
        if (listaHost == null) return;
        var l = Filtradas();
        var p = new StackPanel { Spacing = 8 };
        var cab = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(4, 4, 4, 0) };
        cab.Children.Add(R ? CUi.SecaoFolha(T("{0} equipas", l.Count)) : CUi.TituloSecao_(T("{0} equipas", l.Count)));
        if (ilegiveis.Count > 0)
        {
            var err = new Clicavel { Background = Brushes.Transparent, CorFoco = null, MinHeight = 32, Child = Tx("⚠ " + T("{0} arquivos com erro", ilegiveis.Count), 13, CUi.Erro, true, false) };
            err.Clique += () => Mensagem(string.Join("\n\n", ilegiveis.Take(20)));
            Grid.SetColumn(err, 1);
            cab.Children.Add(err);
        }
        p.Children.Add(cab);
        if (l.Count == 0) p.Children.Add(Tx(T("(vazia)"), 15, Fraco).Ajustar(t => t.Margin = new Thickness(4, 8)));
        else p.Children.Add(CUi.Grupo(l.Select(LinhaEquipa)));
        listaHost.Content = p;
    }

    Control LinhaEquipa(ItemEquipa t)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*,Auto,Auto") };
        var nome = t.E.NomeAbreviado.Length > 0 ? t.E.NomeAbreviado : T("(sem nome)");
        var sub = T("nível {0} · {1} jog.", t.E.Nivel, t.E.Jogadores.Count) + (filtro == null ? " · " + t.Pasta : "");
        var textos = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        if (R)
        {
            // no Retro, como no jogo: sem logo, o nome nas cores da equipa
            g.ColumnDefinitions = new ColumnDefinitions("0,0,*,Auto,Auto");
            textos.Children.Add(NomeNasCores(t.E, nome));
        }
        else
        {
            var esc = EscudoDe(t.E, 34);
            esc.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(esc);
            textos.Children.Add(Tx(nome, 15.5, null, true, false));
        }
        textos.Children.Add(Tx(sub, 13, Fraco, false, false));
        Grid.SetColumn(textos, 2);
        g.Children.Add(textos);
        if (!t.Valida)
        {
            var av = Ui.Icone("aviso", 20, CUi.Erro);
            av.VerticalAlignment = VerticalAlignment.Center;
            av.Margin = new Thickness(6, 0);
            Grid.SetColumn(av, 3);
            g.Children.Add(av);
        }
        var seta = Tx("›", 22, Fraco);
        seta.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(seta, 4);
        g.Children.Add(seta);
        return CUi.Toque(g, () => AbrirEquipa(t), 58);
    }

    // o menu da lista: os patches de equipas
    void MenuMais()
    {
        var linhas = new List<Control>();
        void Linha(string icone, string texto, Action a)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*") };
            var ic = Ui.Icone(icone, 24, R ? W95.Marinho : TemaVerde.Destaque);
            ic.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(ic);
            var t = Tx(texto, 15.5, null, true, false);
            Grid.SetColumn(t, 2);
            g.Children.Add(t);
            linhas.Add(CUi.Toque(g, () => { FecharTodas(); a(); }, 54));
        }
        Linha("peca", T("Importar patch de equipas"), ImportarPatch);
        Linha("disquete", T("Exportar patch de equipas"), ExportarPatch);
        Linha("bola", T("Logos das ligas"), () => AbrirLigas());
        Abrir(T("Editor de Equipas"), CUi.Grupo(linhas));
    }

    // ---------------------------------------------------------------- o pais
    // a lista dos paises (com a busca); comTodos: a do filtro (com "Todos os paises" e o numero
    // de equipas de cada um, os que tem equipas primeiro)
    void EscolherPais(string titulo, string? atualCod, bool comTodos, Action<string?> escolher)
    {
        var l = new List<(string? Cod, string Nome, string Lado)>();
        if (comTodos)
        {
            var porPais = itens.GroupBy(i => i.Pasta).ToDictionary(x => x.Key, x => x.Count());
            l.Add((null, T("Todos os países"), itens.Count.ToString()));
            l.AddRange(porPais.Keys.Select(c => ((string?)c, NomePais(c), porPais[c].ToString())).OrderBy(p => p.Item2, StringComparer.CurrentCultureIgnoreCase));
            l.AddRange(paises.Where(p => !porPais.ContainsKey(EquipaJson.PastaPais(p.Codigo)))
                .Select(p => ((string?)EquipaJson.PastaPais(p.Codigo), p.Nome, "0")).OrderBy(p => p.Item2, StringComparer.CurrentCultureIgnoreCase));
        }
        else
        {
            l.AddRange(paises.Select(p => ((string?)p.Codigo, p.Nome, p.Codigo)).OrderBy(p => p.Item2, StringComparer.CurrentCultureIgnoreCase));
            if (atualCod != null && atualCod.Trim().Length > 0 && !l.Any(p => string.Equals(p.Cod, atualCod.Trim(), StringComparison.OrdinalIgnoreCase)))
                l.Insert(0, (atualCod, atualCod + " " + T("(não existe em countries.tsv)"), atualCod));
        }
        var caixa = CUi.Caixa("", 0, T("Procurar país…"));
        var host = new ContentControl { VerticalAlignment = VerticalAlignment.Top };
        CUi.Folha f = null!;
        Control? escolhido = null;
        void Mostrar()
        {
            var q = LogosDados.Normalizar(caixa.Text ?? "");
            var linhas = new List<Control>();
            escolhido = null;
            foreach (var (cod, nome, lado) in l)
            {
                if (q.Length > 0 && !LogosDados.Normalizar(nome + " " + cod).Contains(q)) continue;
                bool sel = string.Equals(cod, atualCod?.Trim(), StringComparison.OrdinalIgnoreCase) || (cod == null && atualCod == null && comTodos);
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                var nt = LinhaPais(cod, nome, 15.5);
                if (sel && nt is StackPanel sp && sp.Children[1] is TextBlock tb) { tb.FontWeight = FontWeight.Bold; if (!R) tb.Foreground = TemaVerde.Destaque; }
                g.Children.Add(nt);
                var d = Tx((sel ? "✓  " : "") + lado, 13.5, sel && !R ? TemaVerde.Destaque : Fraco, sel, false);
                Grid.SetColumn(d, 1);
                g.Children.Add(d);
                var cc = cod;
                var linha = CUi.Toque(g, () => { Fechar(f); escolher(cc); }, 50);
                if (sel) escolhido = linha;
                if (sel && R)
                {
                    linha.Background = W95.Marinho;
                    d.Foreground = W95.Branco;
                    if (nt is StackPanel sp2 && sp2.Children[1] is TextBlock tb2) tb2.Foreground = W95.Branco;
                }
                linhas.Add(linha);
            }
            host.Content = linhas.Count > 0 ? CUi.Grupo(linhas) : Tx(T("(vazia)"), 15, Fraco);
        }
        caixa.TextChanged += (_, _) => Mostrar();
        Mostrar();
        var sv = new ScrollViewer { Content = host, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        f = Abrir(titulo, ComBusca(caixa, sv), alta: true, rolar: false);
        // o pais de agora a vista, no meio da lista
        Dispatcher.UIThread.Post(() =>
        {
            if (escolhido?.TranslatePoint(default, host) is { } pt)
                sv.Offset = new Vector(0, Math.Max(0, pt.Y - sv.Viewport.Height / 2 + escolhido.Bounds.Height / 2));
        }, DispatcherPriority.Loaded);
    }

    // a busca fixa em cima e a lista que rola embaixo (o corpo de uma folha sem rolar)
    static Control ComBusca(Control topo, Control lista)
    {
        var d = new DockPanel();
        topo.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(topo, Dock.Top);
        d.Children.Add(topo);
        d.Children.Add(lista);
        return d;
    }

    // ---------------------------------------------------------------- a equipa aberta
    void AbrirEquipa(ItemEquipa t)
    {
        atual = t;
        edit = t.E.Copia();
        pendente = null;
        aba = 0;
        MostrarEquipa();
    }

    async Task FecharEquipa()
    {
        if (Alterada && !await Confirmar(T("Há alterações não gravadas nesta equipa. Descartá-las?"))) return;
        MostrarLista();
    }

    TextBlock? tituloBarra, subBarra;
    ContentControl? cabHost, corpoHost;
    ScrollViewer? rolarEquipa;
    CR95.Caderno? caderno;
    StackPanel? situacao;

    // a pagina da equipa (manter: fica na mesma altura da rolagem)
    void MostrarEquipa(bool manter = false)
    {
        var e = edit!;
        var antes = manter ? rolarEquipa?.Offset : null;
        var barra = CUi.BarraTopo(TituloEquipa(), " ", () => _ = FecharEquipa());
        // os textos da barra (o titulo e o estado mudam ao escrever)
        if (barra is Border { Child: Grid gb } && gb.Children.OfType<StackPanel>().FirstOrDefault() is { } st)
        {
            var ts = st.Children.OfType<TextBlock>().ToList();
            tituloBarra = ts.ElementAtOrDefault(0);
            subBarra = ts.ElementAtOrDefault(1);
            if (subBarra != null) subBarra.FontWeight = FontWeight.Bold;
        }
        var rodape = CUi.Botoes(Bt(T("Desfazer"), false, Desfazer), Bt(T("Gravar"), true, () => _ = Gravar()));
        var nomes = new[] { T("Equipa"), T("Jogadores ({0})", e.Jogadores.Count), T("Escudo") };
        cabHost = new ContentControl();
        if (R)
        {
            caderno = new CR95.Caderno(nomes, k => { aba = k; return CorpoAba(k); }, aba);
            pagina.Content = Pagina(barra, null, new Border { Background = W95.Face, Child = caderno }, rodape, out rolarEquipa, false);
        }
        else
        {
            caderno = null;
            corpoHost = new ContentControl();
            var (fila, _) = CUi.Abas(nomes, k =>
            {
                aba = k;
                corpoHost.Content = CorpoAba(k);
                if (rolarEquipa != null) rolarEquipa.Offset = default;
            }, aba);
            corpoHost.Content = CorpoAba(aba);
            pagina.Content = Pagina(barra, fila, corpoHost, rodape, out rolarEquipa);
            if (antes is { } o) Dispatcher.UIThread.Post(() => { if (rolarEquipa != null) rolarEquipa.Offset = o; }, DispatcherPriority.Loaded);
        }
        AtualizarEstado();
    }

    string TituloEquipa() => edit == null ? "" : edit.NomeAbreviado.Length > 0 ? edit.NomeAbreviado : T("(sem nome)");

    // a aba de agora feita de novo (as cores, o escudo, o pais mudaram)
    void RefazerAba()
    {
        if (caderno != null) caderno.Refazer();
        else if (corpoHost != null) corpoHost.Content = CorpoAba(aba);
        AtualizarEstado();
    }

    Control CorpoAba(int k)
    {
        situacao = null;
        var p = new StackPanel { Spacing = 12 };
        cabHost ??= new ContentControl();
        (cabHost.Parent as Panel)?.Children.Remove(cabHost);
        cabHost.Content = Cabecalho();
        p.Children.Add(cabHost);
        p.Children.Add(k switch { 1 => AbaJogadores(), 2 => AbaEscudo(), _ => AbaEquipa() });
        return p;
    }

    // o topo da equipa: o escudo, o nome completo, o pais e o arquivo (no Retro, nas cores dela)
    Control Cabecalho()
    {
        var e = edit!;
        var sub = $"{NomePais(e.Pais)} · {(e.Arquivo.Length > 0 ? e.Arquivo : T("nova"))}";
        var nome = e.NomeCompleto.Length > 0 ? e.NomeCompleto : T("(sem nome)");
        if (R)
        {
            var letra = TemaEditor.Pincel(e.CorLetra);
            var s = new StackPanel { Spacing = 4 };
            s.Children.Add(new TextBlock { Text = nome, FontFamily = TEE.Arial, FontSize = 18 * Acessibilidade.Texto, FontWeight = FontWeight.Bold, Foreground = letra, TextWrapping = TextWrapping.Wrap });
            var l2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            l2.Children.Add(Bandeira(e.Pais, 20));
            l2.Children.Add(new TextBlock { Text = sub, FontFamily = TEE.Arial, FontSize = 13.5 * Acessibilidade.Texto, Foreground = letra, VerticalAlignment = VerticalAlignment.Center });
            s.Children.Add(l2);
            return new Border { Background = TemaEditor.Pincel(e.CorFundo), Padding = new Thickness(10, 8), BoxShadow = TEE.Relevo, Child = s };
        }
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*") };
        var esc = pendente?.Bitmap is { } bp ? Escudos.Controle(bp, 52)! : pendente?.Tipo == TEE.TipoEscudo.Nenhum ? TemaEditor.EscudoCores(e.CorLetra, e.CorFundo, 52) : EscudoDe(e, 52);
        esc.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(esc);
        var t = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(Tx(nome, 17, null, true));
        var l = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        l.Children.Add(Bandeira(e.Pais, 18));
        l.Children.Add(Tx(sub, 13, CUi.Fraco, false, false));
        t.Children.Add(l);
        Grid.SetColumn(t, 2);
        g.Children.Add(t);
        return CUi.Cartao_(null, g);
    }

    void AtualizarEstado()
    {
        if (edit == null) return;
        var erros = regras.Validar(edit);
        if (tituloBarra != null) tituloBarra.Text = TituloEquipa();
        if (subBarra != null)
        {
            subBarra.Text = (erros.Count == 0 ? "✓ " + T("Pronta para o jogo") : "✗ " + T("{0} problemas", erros.Count)) + (Alterada ? " · " + T("por gravar") : "");
            subBarra.Foreground = erros.Count == 0 ? CUi.Bom : CUi.Erro;
        }
    }

    // depois de cada mudanca: o estado, a situacao e o topo
    void Mudou()
    {
        AtualizarEstado();
        AtualizarSituacao();
        if (cabHost != null && edit != null) cabHost.Content = Cabecalho();
    }

    // ---------------------------------------------------------------- aba Equipa
    Control AbaEquipa()
    {
        var e = edit!;
        var p = new StackPanel { Spacing = 12 };
        var completo = CUi.Caixa(e.NomeCompleto, RegrasEquipa.MaxNomeCompleto);
        completo.TextChanged += (_, _) => { if (e.NomeCompleto == (completo.Text ?? "")) return; e.NomeCompleto = completo.Text ?? ""; Mudou(); };
        var curto = CUi.Caixa(e.NomeAbreviado, RegrasEquipa.MaxNome);
        var treinador = CUi.Caixa(e.Treinador, RegrasEquipa.MaxNome);
        treinador.TextChanged += (_, _) => { if (e.Treinador == (treinador.Text ?? "")) return; e.Treinador = treinador.Text ?? ""; Mudou(); };
        var pais = Seletor(LinhaPais(e.Pais, $"{NomePais(e.Pais)} ({e.Pais.Trim().ToUpperInvariant()})"),
            () => EscolherPais(T("País"), e.Pais, false, c => { if (c == null || c == e.Pais) return; e.Pais = c; Mudou(); RefazerAba(); }));
        var nivel = Numero(e.Nivel, RegrasEquipa.NivelMin, RegrasEquipa.NivelMax, v => { if (v == e.Nivel) return; e.Nivel = v; Mudou(); });

        var dados = new StackPanel { Spacing = 12 };
        dados.Children.Add(Campo(T("Nome completo"), completo));
        dados.Children.Add(Campo(T("Nome abreviado"), curto));
        dados.Children.Add(Campo(T("País"), pais));
        dados.Children.Add(Campo(T("Nível"), nivel, T("1 = mais fraca, 20 = mais forte; as maiores começam nas divisões de cima")));
        dados.Children.Add(Campo(T("Treinador"), treinador));
        p.Children.Add(Secao(null, dados));

        // as cores: a amostra do nome e as duas cores (o toque abre a escolha)
        var amostraT = new TextBlock { FontFamily = R ? TEE.Arial : CUi.Fonte, FontSize = 18 * Acessibilidade.Texto, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center };
        var amostra = new Border
        {
            Padding = new Thickness(12, 10), Child = amostraT, CornerRadius = new CornerRadius(R ? 0 : 10),
            BoxShadow = R ? TEE.Relevo : default, BorderBrush = R ? null : CUi.Linha, BorderThickness = new Thickness(R ? 0 : 1),
        };
        void Pintar()
        {
            amostra.Background = TemaEditor.Pincel(e.CorFundo);
            amostraT.Foreground = TemaEditor.Pincel(e.CorLetra);
            amostraT.Text = e.NomeAbreviado.Length > 0 ? e.NomeAbreviado : "ABC";
        }
        Pintar();
        curto.TextChanged += (_, _) => { if (e.NomeAbreviado == (curto.Text ?? "")) return; e.NomeAbreviado = curto.Text ?? ""; Pintar(); Mudou(); };
        var cores = new StackPanel { Spacing = 10 };
        cores.Children.Add(amostra);
        cores.Children.Add(CUi.Botoes(
            BotaoCor(T("Letra"), e.CorLetra, () => EscolherCor(T("Letra"), e.CorLetra, c => { e.CorLetra = c; Mudou(); RefazerAba(); })),
            BotaoCor(T("Fundo"), e.CorFundo, () => EscolherCor(T("Fundo"), e.CorFundo, c => { e.CorFundo = c; Mudou(); RefazerAba(); }))));
        p.Children.Add(Secao(T("Cores"), cores));

        var arq = new StackPanel { Spacing = 10 };
        arq.Children.Add(Tx(e.Arquivo.Length > 0 ? e.Arquivo : T("(escolhido ao gravar)"), 15, e.Arquivo.Length > 0 ? null : Fraco, e.Arquivo.Length > 0));
        arq.Children.Add(Bt(T("Exportar .EFT…"), false, () => _ = ExportarEft()));
        p.Children.Add(Secao(T("Arquivo"), arq));

        situacao = new StackPanel { Spacing = 6 };
        AtualizarSituacao();
        p.Children.Add(Secao(T("Situação para o jogo"), situacao));

        if (atual != null) p.Children.Add(Bt(T("Apagar"), false, () => _ = Apagar(), "Botoes/EXCLUIR.png"));
        return p;
    }

    // o numero na caixa (o teclado de numeros) com o − e o + dos lados, nos limites
    static Control Numero(int valor, int min, int max, Action<int> mudou)
    {
        int v = Math.Clamp(valor, min, max);
        var caixa = CUi.Caixa(v.ToString(), max.ToString().Length);
        caixa.Width = 84;
        caixa.TextAlignment = TextAlignment.Center;
        caixa.HorizontalContentAlignment = HorizontalAlignment.Center;
        caixa.FontWeight = FontWeight.Bold;
        TextInputOptions.SetContentType(caixa, TextInputContentType.Number);
        CUi.Botao menos = null!, mais = null!;
        void Por(int n, bool texto)
        {
            v = Math.Clamp(n, min, max);
            if (texto && caixa.Text != v.ToString()) caixa.Text = v.ToString();
            menos.Habilitado = v > min;
            mais.Habilitado = v < max;
            mudou(v);
        }
        menos = new CUi.Botao("−", false, () => Por(v - 1, true)) { Width = 60, Padding = new Thickness(0, 6) };
        mais = new CUi.Botao("+", false, () => Por(v + 1, true)) { Width = 60, Padding = new Thickness(0, 6) };
        caixa.TextChanged += (_, _) =>
        {
            var s = new string((caixa.Text ?? "").Where(char.IsDigit).ToArray());
            if (s != (caixa.Text ?? "")) { caixa.Text = s; return; }
            if (int.TryParse(s, out var n))
            {
                // acima do maximo: fica o maximo (e a caixa mostra-o)
                if (n > max) Dispatcher.UIThread.Post(() => Por(max, true));
                else if (n >= min) Por(n, false);
            }
        };
        caixa.LostFocus += (_, _) => { if (caixa.Text != v.ToString()) caixa.Text = v.ToString(); };
        Por(v, false);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { menos, caixa, mais } };
    }

    static CUi.Botao BotaoCor(string nome, uint cor, Action abrir)
    {
        var b = new CUi.Botao(nome, false, abrir) { HorizontalAlignment = HorizontalAlignment.Stretch };
        var quad = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(R ? 0 : 5), Background = TemaEditor.Pincel(cor),
            BorderBrush = Ui.Hex("#404040"), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center,
        };
        if (b.Child is TextBlock t)
        {
            b.Child = null;
            b.Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, Children = { quad, t } };
        }
        return b;
    }

    void AtualizarSituacao()
    {
        if (situacao == null || edit == null) return;
        var t = edit;
        situacao.Children.Clear();
        int gr = t.Jogadores.Count(j => j.Posicao == 0), campo = t.Jogadores.Count - gr, est = regras.Estrangeiros(t);
        situacao.Children.Add(Tx(T("Jogadores: {0} (de {1} a {2})", t.Jogadores.Count, RegrasEquipa.MinJogadores, RegrasEquipa.MaxJogadores), 14));
        situacao.Children.Add(Tx(T("Guarda-redes: {0} (mín. 1) · Campo: {1} (mín. {2})", gr, campo, RegrasEquipa.MinCampo), 14));
        situacao.Children.Add(Tx(regras.Liberado ? T("Estrangeiros: sem limite (todos na Lei Bosman)")
            : T("Estrangeiros: {0} de {1} (Lei Bosman e PLOP não contam)", est, RegrasEquipa.MaxEstrangeiros), 14));
        situacao.Children.Add(new Border { Height = 1, Background = CUi.Linha, Margin = new Thickness(0, 4) });
        var erros = regras.Validar(t);
        if (erros.Count == 0) situacao.Children.Add(Tx("✓ " + T("Pronta para o jogo"), 15, CUi.Bom, true));
        else foreach (var x in erros) situacao.Children.Add(Tx("✗ " + x, 14, CUi.Erro));
    }

    // as 16 cores do original e o codigo #RRGGBB
    void EscolherCor(string titulo, uint atualCor, Action<uint> ok)
    {
        uint escolhida = atualCor;
        var hex = CUi.Caixa("#" + atualCor.ToString("X6"), 7);
        hex.Width = 140;
        var previa = new Border
        {
            Width = 80, Height = 44, CornerRadius = new CornerRadius(R ? 0 : 8), BorderBrush = Ui.Hex("#404040"), BorderThickness = new Thickness(1),
            Background = TemaEditor.Pincel(atualCor), VerticalAlignment = VerticalAlignment.Center,
        };
        var quads = new List<(uint Cor, Clicavel Q)>();
        void Marcar()
        {
            foreach (var (c, q) in quads)
            {
                bool s = c == escolhida;
                q.BorderBrush = s ? (R ? W95.Preto : TemaVerde.Destaque) : Ui.Hex("#404040");
                q.BorderThickness = new Thickness(s ? 4 : 1);
                if (q.Child is TextBlock t) t.IsVisible = s;
            }
        }
        void Por(uint c)
        {
            escolhida = c;
            previa.Background = TemaEditor.Pincel(c);
            var s = "#" + c.ToString("X6");
            if (!string.Equals(hex.Text, s, StringComparison.OrdinalIgnoreCase)) hex.Text = s;
            Marcar();
        }
        hex.TextChanged += (_, _) =>
        {
            var s = (hex.Text ?? "").Trim().TrimStart('#');
            if (s.Length == 6 && uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var c))
            {
                escolhida = c;
                previa.Background = TemaEditor.Pincel(c);
                Marcar();
            }
        };
        var grade = new UniformGrid { Columns = 4, ColumnSpacing = 8, RowSpacing = 8 };
        foreach (var c in TemaEditor.Paleta)
        {
            var cc = c;
            // o visto em cima da cor escolhida, na cor que se le sobre ela
            var claro = ((c >> 16) & 0xFF) * 0.3 + ((c >> 8) & 0xFF) * 0.59 + (c & 0xFF) * 0.11 > 140;
            var q = new Clicavel
            {
                Height = 52, CornerRadius = new CornerRadius(R ? 0 : 10), Background = TemaEditor.Pincel(c), CorFoco = null,
                Child = new TextBlock { Text = "✓", FontSize = 22, FontWeight = FontWeight.Bold, Foreground = claro ? Brushes.Black : Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            q.Clique += () => Por(cc);
            quads.Add((c, q));
            grade.Children.Add(q);
        }
        Marcar();
        var p = new StackPanel { Spacing = 12 };
        p.Children.Add(Tx(T("As 16 cores Retrô:"), 14, Fraco));
        p.Children.Add(grade);
        var lh = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        lh.Children.Add(Tx(T("Código:"), 15, null, true));
        lh.Children.Add(hex);
        lh.Children.Add(previa);
        p.Children.Add(lh);
        CUi.Folha f = null!;
        f = Abrir(titulo, p, CUi.Botoes(Bt(T("Cancelar"), false, () => Fechar(f), "Botoes/BBCANCEL.png"),
            Bt(T("OK"), true, () => { Fechar(f); ok(escolhida); }, "Botoes/BBOK.png")));
    }

    // ---------------------------------------------------------------- aba Jogadores
    Control AbaJogadores()
    {
        var e = edit!;
        var p = new StackPanel { Spacing = 8 };
        int gr = e.Jogadores.Count(j => j.Posicao == 0);
        p.Children.Add(Tx(T("{0} jogadores · {1} GR · {2} estrangeiros", e.Jogadores.Count, gr, regras.Estrangeiros(e)), 14, Fraco));
        var add = Bt("+  " + T("Adicionar"), false, () => EditarJogador(null));
        add.Habilitado = e.Jogadores.Count < RegrasEquipa.MaxJogadores;
        p.Children.Add(add);
        for (int k = 0; k < 4; k++)
        {
            var grupo = e.Jogadores.Select((j, i) => (j, i)).Where(x => Math.Clamp(x.j.Posicao, 0, 3) == k).ToList();
            if (grupo.Count == 0) continue;
            var cab = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,52"), Margin = new Thickness(4, 8, 12, 0) };
            cab.Children.Add(R ? CUi.SecaoFolha(T(Setores[k]) + " · " + grupo.Count) : CUi.TituloSecao_(T(Setores[k]) + " · " + grupo.Count));
            var nt = R ? CUi.SecaoFolha(T("Nota")) : CUi.TituloSecao_(T("Nota"));
            nt.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(nt, 2);
            cab.Children.Add(nt);
            p.Children.Add(cab);
            p.Children.Add(CUi.Grupo(grupo.Select(x => LinhaJog(x.i, x.j))));
        }
        return p;
    }

    string? TextoEstrangeiro(RegrasEquipa.Situacao s) => s switch
    {
        RegrasEquipa.Situacao.Estrangeiro => T("Estrangeiro"),
        RegrasEquipa.Situacao.Bosman => "Bosman",
        RegrasEquipa.Situacao.Plop => "PLOP",
        _ => null,
    };

    Control LinhaJog(int indice, JogadorEditavel j)
    {
        var a = j.Atributos;
        var sit = regras.Classificar(j.Pais, edit!.Pais);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,10,*,Auto,52") };
        var letra = Idioma.Posicao("GDMA"[Math.Clamp(j.Posicao, 0, 3)]);
        Control pos = R
            ? Tx(letra, 15, W95.Marinho, true, false).Ajustar(t => { t.Width = 22; t.TextAlignment = TextAlignment.Center; })
            : new Border
            {
                Background = TEE.CorPosicao(j.Posicao), CornerRadius = new CornerRadius(6), Width = 30, Height = 26, VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = letra, Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = 13.5, FontFamily = CUi.Fonte, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Tag = "fixo" },
            };
        g.Children.Add(pos);
        var meio = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var l1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        l1.Children.Add(Bandeira(j.Pais, 17));
        l1.Children.Add(Tx(j.Nome.Length > 0 ? j.Nome : T("(sem nome)"), 15.5, RegrasEquipa.ErroNome(j.Nome) != null ? CUi.Erro : null, R, false));
        meio.Children.Add(l1);
        var l2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        l2.Children.Add(Tx(j.Pais.Trim().ToUpperInvariant(), 12.5, regras.PaisExiste(j.Pais) ? Fraco : CUi.Erro, false, false));
        if (TextoEstrangeiro(sit) is { } te)
            l2.Children.Add(Tx("· " + te, 12.5, sit == RegrasEquipa.Situacao.Estrangeiro ? CUi.Erro : CUi.Bom, true, false));
        l2.Children.Add(Tx("· " + T(Atributos.Comportamentos[Math.Clamp(a.Comportamento, 0, 5)]), 12.5, Fraco, false, false));
        meio.Children.Add(l2);
        Grid.SetColumn(meio, 2);
        g.Children.Add(meio);
        if (a.Estrela)
        {
            var s = Tx("★", 17, Ouro, true, false);
            Grid.SetColumn(s, 3);
            g.Children.Add(s);
        }
        var nota = Tx(a.Nota.ToString(), 18, R ? W95.Marinho : TemaVerde.Destaque, true, false);
        nota.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(nota, 4);
        g.Children.Add(nota);
        return CUi.Toque(g, () => EditarJogador(indice), 56);
    }

    // a ficha do jogador: o nome, a posicao, o pais e o que o jogo tira do nome, ao vivo
    void EditarJogador(int? indice)
    {
        var e = edit!;
        var j = indice is int k0 ? e.Jogadores[k0].Copia() : new JogadorEditavel { Pais = e.Pais.Trim().ToUpperInvariant(), Posicao = 1 };
        var p = new StackPanel { Spacing = 12 };
        var nome = CUi.Caixa(j.Nome, RegrasEquipa.MaxNome);
        var nota = Tx("", 22, null, true, false);
        var lesao = Tx("", 22, null, true, false);
        var comp = Tx("", 15, null, true, false);
        var estrela = Tx("", 15, null, true, false);
        var sit = Tx("", 14);
        var erro = Tx("", 14, CUi.Erro, true);
        var paisHost = new ContentControl();
        void PorPais() => paisHost.Content = LinhaPais(j.Pais, $"{NomePais(j.Pais)} ({j.Pais.Trim().ToUpperInvariant()})");
        PorPais();
        void Atualizar()
        {
            bool vazio = j.Nome.Trim().Length == 0;
            var a = j.Atributos;
            nota.Text = vazio ? "—" : a.Nota.ToString();
            lesao.Text = vazio ? "—" : a.Lesao.ToString();
            comp.Text = vazio ? "—" : T(Atributos.Comportamentos[Math.Clamp(a.Comportamento, 0, 5)]);
            estrela.Text = vazio ? "—" : a.Estrela ? "★ " + T("Estrela") : T("Sem estrela");
            estrela.Foreground = a.Estrela && !vazio ? Ouro : Fraco;
            var s0 = regras.Classificar(j.Pais, e.Pais);
            sit.Text = s0 switch
            {
                RegrasEquipa.Situacao.Nacional => T("Nacional."),
                RegrasEquipa.Situacao.Bosman => T("Não conta como estrangeiro: os dois países estão na Lei Bosman."),
                RegrasEquipa.Situacao.Plop => T("Não conta como estrangeiro: os dois países são de língua portuguesa (PLOP)."),
                _ => T("Estrangeiro: conta no limite de {0} por equipa.", RegrasEquipa.MaxEstrangeiros),
            };
            sit.Foreground = s0 == RegrasEquipa.Situacao.Estrangeiro ? CUi.Erro : CUi.Bom;
            erro.Text = vazio ? "" : RegrasEquipa.ErroNome(j.Nome) ?? "";
            erro.IsVisible = erro.Text.Length > 0;
        }
        nome.TextChanged += (_, _) => { j.Nome = nome.Text ?? ""; Atualizar(); };
        var (posicoes, _) = CUi.Escolha(NomesPos.Select(n => T(n)).ToList(), Math.Clamp(j.Posicao, 0, 3), q => { j.Posicao = q; Atualizar(); });
        var pais = Seletor(paisHost, () => EscolherPais(T("País"), j.Pais, false, c => { if (c == null) return; j.Pais = c; PorPais(); Atualizar(); }));
        p.Children.Add(Campo(T("Nome"), nome));
        p.Children.Add(erro);
        p.Children.Add(Campo(T("Posição"), posicoes));
        p.Children.Add(Campo(T("País"), pais));
        p.Children.Add(sit);
        p.Children.Add(Tx(T("O jogo tira estes valores do nome (e da posição):"), 13.5, Fraco));
        Control Quadro(string r, TextBlock v)
        {
            var s = new StackPanel { Spacing = 2, Children = { Tx(r, 12.5, Fraco, false, false), v } };
            return R
                ? CR95.CaixaLista(new Border { Padding = new Thickness(8, 6), Child = s })
                : new Border { Background = TemaVerde.BotaoSecundario, CornerRadius = new CornerRadius(12), BorderBrush = CUi.Linha, BorderThickness = new Thickness(1), Padding = new Thickness(12, 8), Child = s };
        }
        var at = new UniformGrid { Columns = 2, ColumnSpacing = 10, RowSpacing = 10 };
        at.Children.Add(Quadro(T("Nota"), nota));
        at.Children.Add(Quadro(T("Lesão"), lesao));
        at.Children.Add(Quadro(T("Comportamento"), comp));
        at.Children.Add(Quadro(T("Estrela"), estrela));
        p.Children.Add(at);
        Atualizar();
        CUi.Folha f = null!;
        void Ok()
        {
            var n = j.Nome.Trim();
            if (RegrasEquipa.ErroNome(n) is { } x) { erro.Text = x; erro.IsVisible = true; nome.Focus(); return; }
            if (!regras.PaisExiste(j.Pais)) { erro.Text = T("Escolha o país do jogador."); erro.IsVisible = true; return; }
            j.Nome = n;
            if (indice is int k2)
            {
                bool mudouPos = e.Jogadores[k2].Posicao != j.Posicao;
                e.Jogadores[k2] = j;
                // mudou de posicao: vai para o grupo da posicao nova (G, D, M, A)
                if (mudouPos) { e.Jogadores.RemoveAt(k2); e.InserirNaPosicao(j); }
            }
            else e.InserirNaPosicao(j);
            Fechar(f);
            MostrarEquipa(true);
        }
        nome.KeyDown += (_, a) => { if (a.Key == Key.Enter) { a.Handled = true; Ok(); } };
        var rodape = indice is int k1
            ? CUi.Botoes(Bt(T("Remover"), false, () => { e.Jogadores.RemoveAt(k1); Fechar(f); MostrarEquipa(true); }, "Botoes/EXCLUIR.png"),
                Bt(T("OK"), true, Ok, "Botoes/BBOK.png"))
            : CUi.Botoes(Bt(T("Cancelar"), false, () => Fechar(f), "Botoes/BBCANCEL.png"), Bt(T("OK"), true, Ok, "Botoes/BBOK.png"));
        f = Abrir(indice == null ? T("Novo jogador") : T("Editar jogador"), p, rodape);
        if (indice == null) Dispatcher.UIThread.Post(() => nome.Focus(), DispatcherPriority.Loaded);
    }

    // ---------------------------------------------------------------- aba Escudo
    Control AbaEscudo()
    {
        var e = edit!;
        var (bmp, origem) = TEE.EscudoAtual(raiz, pendente, e);
        var p = new StackPanel { Spacing = 12 };
        var quadro = new Border
        {
            Width = 168, Height = 168, CornerRadius = new CornerRadius(R ? 0 : 14), Background = TemaEditor.Xadrez, Padding = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Center, BoxShadow = R ? TEE.RelevoAfundado : default,
            Child = bmp != null ? Escudos.Controle(bmp, 150) : TemaEditor.EscudoCores(e.CorLetra, e.CorFundo, 110),
        };
        var topo = new StackPanel { Spacing = 8 };
        topo.Children.Add(quadro);
        topo.Children.Add(Tx(origem, 15, null, true).Ajustar(t => t.TextAlignment = TextAlignment.Center));
        if (pendente != null) topo.Children.Add(Tx(T("(vai para a pasta logos/ ao Gravar)"), 13, Fraco).Ajustar(t => t.TextAlignment = TextAlignment.Center));
        p.Children.Add(Secao(T("Escudo"), topo));

        string titulo = T("Escudo de {0}", e.NomeAbreviado);
        void Usar(Rgba r) => Escolher(new TEE.EscolhaEscudo { Tipo = TEE.TipoEscudo.Imagem, Imagem = r, Bitmap = EscudoArquivo.Bitmap(r) });
        var bts = new StackPanel { Spacing = 10 };
        bts.Children.Add(Bt(T("Escolher imagem…"), true, () => _ = EscolherImagem(titulo, Usar)));
        bts.Children.Add(Bt(T("Da coleção…"), false, () => EscolherDaColecao(e.Pais, LogosDados.Normalizar(e.NomeAbreviado),
            a => Escolher(new TEE.EscolhaEscudo { Tipo = TEE.TipoEscudo.Colecao, Colecao = a, Bitmap = Escudos.DoPacote(a) }))));
        bts.Children.Add(Bt(T("Da pasta de dados…"), false, () => EscolherDaPasta(titulo, Usar)));
        bts.Children.Add(CUi.Botoes(
            Bt(T("Automático"), false, () => Escolher(new TEE.EscolhaEscudo { Tipo = TEE.TipoEscudo.Automatico, Bitmap = TEE.PacoteDe(e) })),
            Bt(T("Sem escudo"), false, () => Escolher(new TEE.EscolhaEscudo { Tipo = TEE.TipoEscudo.Nenhum }))));
        bts.Children.Add(Tx(T("O logo que o jogo acha sozinho pelo país e nome (ou o escudo nas cores)"), 13, Fraco));
        if (bmp != null) bts.Children.Add(Bt(T("Cores do escudo"), false, () => CoresDoEscudo(bmp)));
        bts.Children.Add(Tx(TEE.Requisitos, 13, Fraco));
        p.Children.Add(Secao(null, bts));
        p.Children.Add(Previas(pendente?.Tipo == TEE.TipoEscudo.Nenhum ? null : bmp, e));
        return p;
    }

    void Escolher(TEE.EscolhaEscudo es)
    {
        FecharTodas();
        pendente = es;
        aba = 2;
        MostrarEquipa(true);
    }

    // como o escudo fica nas telas do jogo, nos dois visuais (um embaixo do outro)
    Control Previas(Bitmap? logo, EquipaEditavel e)
    {
        var curto = e.NomeAbreviado.Length > 0 ? e.NomeAbreviado : "ABC";
        Control Logo(double tam)
        {
            if (logo == null) return TemaEditor.EscudoCores(e.CorLetra, e.CorFundo, tam);
            var im = Escudos.Controle(logo, tam)!;
            return Escudos.Escuro(logo)
                ? new Border { Width = tam + 6, Height = tam + 6, CornerRadius = new CornerRadius((tam + 6) / 2), Background = Ui.Hex("#E8EAED"), Child = im }
                : im;
        }
        var m = new StackPanel { Spacing = 8 };
        var cab = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        cab.Children.Add(Logo(40));
        var nm = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nm.Children.Add(new TextBlock { Text = e.NomeCompleto, FontFamily = ConfigEstilo.Inter, FontSize = 15, FontWeight = FontWeight.Bold, Foreground = ConfigEstilo.Texto, TextTrimming = TextTrimming.CharacterEllipsis });
        nm.Children.Add(new TextBlock { Text = NomePais(e.Pais) + "  ·  " + T("1ª Divisão"), FontFamily = ConfigEstilo.Inter, FontSize = 12, Foreground = ConfigEstilo.TextoFraco });
        Grid.SetColumn(nm, 1);
        cab.Children.Add(nm);
        m.Children.Add(new Border { Background = Ui.Hex("#24282e"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8), Child = cab });
        var linhas = new StackPanel { Background = Ui.Hex("#202124") };
        linhas.Children.Add(TEE.LinhaModerna("1", Logo(18), curto, "31", false));
        linhas.Children.Add(TEE.LinhaModerna("2", TemaEditor.EscudoCores(0xFFFFFF, 0xC00000, 18), T("Outra equipa"), "28", true));
        m.Children.Add(new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = linhas, BorderBrush = Ui.Hex("#3c4043"), BorderThickness = new Thickness(1) });

        var c = new StackPanel { Spacing = 8 };
        c.Children.Add(new Border
        {
            Background = TemaEditor.Pincel(e.CorFundo), Padding = new Thickness(8, 5), BoxShadow = TEE.Relevo,
            Child = new TextBlock { Text = e.NomeCompleto, FontFamily = TEE.Arial, FontSize = 16, FontWeight = FontWeight.Bold, Foreground = TemaEditor.Pincel(e.CorLetra), TextTrimming = TextTrimming.CharacterEllipsis },
        });
        var tab = new StackPanel { Background = Brushes.White };
        tab.Children.Add(TEE.LinhaClassica("1", curto, "31", TemaEditor.Pincel(e.CorFundo), TemaEditor.Pincel(e.CorLetra)));
        tab.Children.Add(TEE.LinhaClassica("2", T("Outra equipa"), "28", Brushes.White, Brushes.Black));
        c.Children.Add(new Border { BoxShadow = TEE.RelevoAfundado, Padding = new Thickness(2), Background = Brushes.White, Child = tab });
        c.Children.Add(Tx(T("O visual Retrô não mostra logos: valem as cores da equipa."), 13, Fraco));
        return new StackPanel { Spacing = 12, Children = { Secao(T("Visual Moderno"), m), Secao(T("Visual Retrô"), c) } };
    }

    // uma copia local do arquivo escolhido (no Android o seletor da um content://, sem caminho)
    static async Task<string?> Local(IStorageFile f)
    {
        try
        {
            if (f.TryGetLocalPath() is { } c && File.Exists(c)) return c;
            var pasta = Path.Combine(Path.GetTempPath(), "retrofoot-importar", Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(pasta);
            var destino = Path.Combine(pasta, Path.GetFileName(f.Name));
            await using var s = await f.OpenReadAsync();
            await using var o = File.Create(destino);
            await s.CopyToAsync(o);
            return destino;
        }
        catch { return null; }
    }

    async Task EscolherImagem(string titulo, Action<Rgba> usar, bool liga = false)
    {
        var sp = Arquivos;
        if (sp is not { CanOpen: true })
        {
            // sem o seletor do sistema: as imagens que o jogador copiou para logos/
            EscolherDaPasta(titulo, usar, liga);
            return;
        }
        var fs = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = titulo, AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(T("Imagens"))
                {
                    Patterns = EscudoArquivo.Extensoes.SelectMany(x => new[] { "*." + x, "*." + x.ToUpperInvariant() }).ToArray(),
                    MimeTypes = new[] { "image/*" },
                },
            },
        });
        if (fs.Count == 0) return;
        if (await Local(fs[0]) is { } c) Adaptar(c, titulo, usar, liga);
        else Mensagem(T("Não consegui abrir a imagem: {0}", fs[0].Name));
    }

    // as imagens da pasta logos/ da pasta de dados
    void EscolherDaPasta(string titulo, Action<Rgba> usar, bool liga = false)
    {
        var pasta = Path.Combine(raiz, PastaDados.Logos);
        Directory.CreateDirectory(pasta);
        var arqs = Directory.GetFiles(pasta)
            .Where(a => EscudoArquivo.Extensoes.Contains(Path.GetExtension(a).TrimStart('.').ToLowerInvariant()))
            .OrderBy(a => Path.GetFileName(a), StringComparer.CurrentCultureIgnoreCase).ToList();
        var p = new StackPanel { Spacing = 10 };
        p.Children.Add(Tx(T("Copie a imagem para a pasta logos/ da pasta de dados e escolha-a aqui:"), 14));
        p.Children.Add(Tx(pasta, 12.5, Fraco));
        CUi.Folha f = null!;
        if (arqs.Count == 0) p.Children.Add(Tx(T("A pasta logos/ não tem imagens (PNG, JPG, WEBP, SVG ou BMP)."), 14, Fraco).Ajustar(t => t.Margin = new Thickness(0, 12)));
        else
        {
            var grade = new UniformGrid { Columns = 3, ColumnSpacing = 8, RowSpacing = 8 };
            foreach (var a in arqs.Take(200))
            {
                var arq = a;
                Control mini;
                try
                {
                    if (Path.GetExtension(a).Equals(".svg", StringComparison.OrdinalIgnoreCase)) mini = Ui.Icone("escudo", 52, Fraco);
                    else
                    {
                        using var fs = File.OpenRead(a);
                        mini = new Image { Source = Bitmap.DecodeToWidth(fs, 128), Width = 64, Height = 64, Stretch = Stretch.Uniform };
                    }
                }
                catch { mini = Ui.Icone("aviso", 34, CUi.Erro); }
                grade.Children.Add(Ficha(mini, Path.GetFileName(a), () => { Fechar(f); Adaptar(arq, titulo, usar, liga); }));
            }
            p.Children.Add(grade);
        }
        var bts = new List<Control> { Bt(T("Atualizar"), false, () => { Fechar(f); EscolherDaPasta(titulo, usar, liga); }) };
        if (DadosJogo.PodeAbrirPasta) bts.Insert(0, Bt(T("Abrir pasta de dados"), false, () => DadosJogo.AbrirPasta(pasta)));
        f = Abrir(T("Imagens da pasta de dados"), p, Pilha(bts.ToArray()), alta: arqs.Count > 6);
    }

    // um quadrinho da grade (a imagem no xadrez e o nome embaixo) que se toca
    static Clicavel Ficha(Control imagem, string nome, Action toque)
    {
        imagem.HorizontalAlignment = HorizontalAlignment.Center;
        var s = new StackPanel { Spacing = 4 };
        s.Children.Add(new Border { Background = TemaEditor.Xadrez, Width = 72, Height = 72, Padding = new Thickness(4), Child = imagem, HorizontalAlignment = HorizontalAlignment.Center, CornerRadius = new CornerRadius(R ? 0 : 8) });
        s.Children.Add(new TextBlock
        {
            Text = nome, FontFamily = CUi.Fonte, FontSize = 12 * Acessibilidade.Texto, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            MaxHeight = 32, Foreground = CUi.Texto, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var c = new Clicavel { Child = s, Padding = new Thickness(4, 6), CorFoco = null, MinHeight = 44 };
        if (R) { c.Background = W95.Branco; c.BoxShadow = BoxShadows.Parse("inset 1 1 0 0 #808080, inset -1 -1 0 0 #FFFFFF"); }
        else { c.Background = TemaVerde.BotaoSecundario; c.Hover = TemaVerde.Hover; c.CornerRadius = new CornerRadius(12); c.BorderBrush = CUi.Linha; c.BorderThickness = new Thickness(1); }
        c.Clique += toque;
        return c;
    }

    // a imagem original e o resultado no padrao do jogo, a opcao de tirar o fundo, os avisos e
    // as previas nas telas
    void Adaptar(string arquivo, string titulo, Action<Rgba> usar, bool liga = false)
    {
        var (img, erro) = EscudoArquivo.Abrir(arquivo);
        bool tirar = true;
        var p = new StackPanel { Spacing = 12 };
        Bitmap? original = null;
        if (img != null) original = Math.Max(img.Largura, img.Altura) > 512 ? EscudoArquivo.Bitmap(TEE.Reduzida(img, 512)) : EscudoArquivo.Bitmap(img);
        EscudoProcessador.Resultado? r = null;
        CUi.Folha f = null!;
        var usarB = Bt(T("Usar este escudo"), true, () => { if (r?.Imagem != null) { Fechar(f); usar(r.Imagem); } });
        void Montar()
        {
            p.Children.Clear();
            r = img != null ? EscudoProcessador.Processar(img, tirar) : null;
            var err = erro ?? r?.Erro;
            Bitmap? res = r?.Imagem != null ? EscudoArquivo.Bitmap(r.Imagem) : null;
            Control Quadro(string rotulo, Bitmap? b, string legenda)
            {
                var s = new StackPanel { Spacing = 4 };
                s.Children.Add(Tx(rotulo, 13, null, true, false));
                s.Children.Add(new Border
                {
                    Height = 150, Background = TemaEditor.Xadrez, CornerRadius = new CornerRadius(R ? 0 : 10), Padding = new Thickness(6),
                    BoxShadow = R ? TEE.RelevoAfundado : default,
                    Child = b != null ? new Image { Source = b, Stretch = Stretch.Uniform } : Tx("—", 30, Fraco).Ajustar(t => t.HorizontalAlignment = HorizontalAlignment.Center),
                });
                s.Children.Add(Tx(legenda, 12, Fraco));
                return s;
            }
            var lado = new UniformGrid { Columns = 2, ColumnSpacing = 10 };
            lado.Children.Add(Quadro(T("Original"), original, img != null ? $"{img.Largura}x{img.Altura} px · {Path.GetExtension(arquivo).TrimStart('.').ToUpperInvariant()}" : Path.GetFileName(arquivo)));
            lado.Children.Add(Quadro(T("No padrão do jogo"), res, res != null ? T("{0}x{0} px, PNG transparente", EscudoProcessador.Tamanho) : ""));
            p.Children.Add(lado);
            if (err != null)
            {
                p.Children.Add(Tx("✗ " + T("Esta imagem não serve:"), 15, CUi.Erro, true));
                p.Children.Add(Tx(err, 14, CUi.Erro));
            }
            else if (r != null) p.Children.Add(Tx("✓ " + T("Imagem adaptada"), 15, CUi.Bom, true));
            if (r != null)
            {
                // a opcao aparece tambem quando tirar o fundo nao deixou nada (para desligar)
                if (r.FundoUniforme)
                    p.Children.Add(Marca(T("Tirar o fundo de cor única (#{0})", r.CorDoFundo.ToString("X6")), tirar, v => { tirar = v; Dispatcher.UIThread.Post(Montar); }));
                else if (r.TemTransparencia && err == null) p.Children.Add(Tx(T("O fundo já é transparente."), 14, Fraco));
                foreach (var a in r.Avisos) p.Children.Add(Tx("! " + a, 14, R ? W95.Marinho : TemaVerde.Destaque));
            }
            p.Children.Add(Tx(TEE.Requisitos, 13, Fraco));
            if (res != null && !liga && edit != null) p.Children.Add(Previas(res, edit));
            else if (res != null && liga) p.Children.Add(PreviasLiga(res, titulo));
            usarB.Habilitado = err == null && r?.Imagem != null;
        }
        Montar();
        var outra = Bt(T("Outra imagem…"), false, () => { Fechar(f); _ = EscolherImagem(titulo, usar, liga); });
        f = Abrir(titulo, p, CUi.Botoes(outra, usarB));
    }

    // o logo de uma liga: o separador da divisao na Classificacao moderna
    Control PreviasLiga(Bitmap logo, string nome)
    {
        var s = new StackPanel { Spacing = 8 };
        var abas = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Background = Ui.Hex("#202124") };
        var a1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(12, 8) };
        a1.Children.Add(Escudos.Controle(logo, 20)!);
        a1.Children.Add(new TextBlock { Text = nome, FontFamily = ConfigEstilo.Inter, FontSize = 14, Foreground = ConfigEstilo.Azul, VerticalAlignment = VerticalAlignment.Center });
        abas.Children.Add(new Border { BorderBrush = ConfigEstilo.Azul, BorderThickness = new Thickness(0, 0, 0, 3), Child = a1 });
        s.Children.Add(new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = abas });
        s.Children.Add(Tx(T("O visual Retrô não mostra logos. O logo da liga aparece quando todas as equipas do jogo são do mesmo país."), 13, Fraco));
        return Secao(T("Visual Moderno (a Classificação e a tela da equipa)"), s);
    }

    // os escudos da colecao do jogo (a busca e "so deste pais")
    void EscolherDaColecao(string? pais, string procurar, Action<string> usar)
    {
        var col = Escudos.Colecao();
        bool soPais = pais != null && col.Any(i => i.Pais == pais);
        var busca0 = CUi.Caixa(soPais ? "" : procurar, 0, T("Procurar na coleção…"));
        var conta = Tx("", 13, Fraco, false, false);
        var host = new ContentControl { VerticalAlignment = VerticalAlignment.Top };
        CUi.Folha f = null!;
        const int Max = 120;
        void Mostrar()
        {
            var q = LogosDados.Normalizar(busca0.Text ?? "");
            var l = col.Where(i => (!soPais || i.Pais == pais) && (q.Length == 0 || LogosDados.Normalizar(i.Nome).Contains(q))).ToList();
            var grade = new UniformGrid { Columns = 3, ColumnSpacing = 8, RowSpacing = 8 };
            foreach (var i in l.Take(Max))
            {
                var it = i;
                var b = Escudos.DoPacote(i.Arquivo);
                Control im = b != null ? Escudos.Controle(b, 60)! : new Border { Height = 60 };
                grade.Children.Add(Ficha(im, i.Nome, () => { Fechar(f); usar(it.Arquivo); }));
            }
            host.Content = l.Count > 0 ? grade : Tx(T("(vazia)"), 15, Fraco);
            conta.Text = l.Count > Max ? T("{0} logos (mostrando {1}; procure pelo nome)", l.Count, Max) : T("{0} logos", l.Count);
        }
        busca0.TextChanged += (_, _) => Mostrar();
        var topo = new StackPanel { Spacing = 8 };
        topo.Children.Add(busca0);
        topo.Children.Add(Marca(T("Só de {0}", pais != null ? NomePais(pais) : "—"), soPais, v => { soPais = v; Mostrar(); }, pais != null));
        topo.Children.Add(conta);
        Mostrar();
        var sv = new ScrollViewer { Content = new Border { Child = host, Padding = new Thickness(0, 0, 0, 14) }, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        f = Abrir(T("Coleção de escudos do jogo"), ComBusca(topo, sv), alta: true, rolar: false);
    }

    // as cores do jogo sugeridas pelo escudo (letra, fundo)
    void CoresDoEscudo(Bitmap b)
    {
        var px = EscudoArquivo.DeBitmap(b);
        if (px == null || edit == null) return;
        var sug = EscudoProcessador.Sugestoes(px);
        var p = new StackPanel { Spacing = 12 };
        p.Children.Add(Tx(T("Escolha as cores da equipa (letra sobre fundo), tiradas das 16 cores do jogo que mais aparecem no escudo:"), 14));
        var g = new UniformGrid { Columns = 2, ColumnSpacing = 10, RowSpacing = 10 };
        CUi.Folha f = null!;
        foreach (var (letra, fundo) in sug)
        {
            var (l, fu) = (letra, fundo);
            var c = new Clicavel
            {
                Background = TemaEditor.Pincel(fu), MinHeight = 52, Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(R ? 0 : 10),
                BorderBrush = Ui.Hex("#808080"), BorderThickness = new Thickness(1), CorFoco = null, BoxShadow = R ? TEE.Relevo : default,
                Child = new TextBlock { Text = edit.NomeAbreviado, FontFamily = TEE.Arial, FontSize = 16, FontWeight = FontWeight.Bold, Foreground = TemaEditor.Pincel(l), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            c.Clique += () => { edit.CorLetra = l; edit.CorFundo = fu; Fechar(f); Mudou(); RefazerAba(); };
            g.Children.Add(c);
        }
        p.Children.Add(g);
        f = Abrir(T("Cores do escudo"), p);
    }

    // ---------------------------------------------------------------- acoes
    async Task NovaEquipa()
    {
        var pais = filtro ?? atual?.Pasta ?? "POR";
        atual = null;
        edit = new EquipaEditavel { Pais = pais, NomeCompleto = T("NOVA EQUIPA"), NomeAbreviado = T("NOVA"), Nivel = 5, CorLetra = 0xFFFFFF, CorFundo = 0x000080 };
        pendente = null;
        aba = 0;
        MostrarEquipa();
        await Task.CompletedTask;
    }

    void Desfazer()
    {
        if (atual == null) { MostrarLista(); return; }
        edit = atual.E.Copia();
        pendente = null;
        MostrarEquipa(true);
    }

    async Task<bool> Gravar()
    {
        if (edit == null) return false;
        var erros = regras.Validar(edit);
        if (erros.Count > 0)
        {
            Mensagem(T("O jogo não aceita a equipa assim:") + "\n\n• " + string.Join("\n• ", erros));
            return false;
        }
        // outra equipa do mesmo pais com o mesmo nome: avisa (o jogo aceitaria, mas confunde)
        if (itens.Any(i => i != atual && i.Pasta == EquipaJson.PastaPais(edit.Pais) && LogosDados.Normalizar(i.E.NomeCompleto) == LogosDados.Normalizar(edit.NomeCompleto))
            && !await Confirmar(T("Já há outra equipa com este nome neste país. Gravar mesmo assim?"))) return false;
        var antes = atual?.E;
        try
        {
            edit.Gravar(raiz);
            TEE.GravarEscudo(raiz, pendente, antes, edit);
        }
        catch (Exception x)
        {
            Mensagem(T("Erro ao gravar:") + "\n" + x.Message);
            return false;
        }
        Escudos.Recarregar();
        var item = atual ?? new ItemEquipa();
        if (atual == null) itens.Add(item);
        item.Caminho = edit.Caminho!;
        item.E = edit.Copia();
        item.Valida = true;
        atual = item;
        pendente = null;
        Ordenar();
        if (filtro != null) filtro = item.Pasta;
        edit = item.E.Copia();
        MostrarEquipa(true);
        return true;
    }

    async Task Apagar()
    {
        if (atual == null) { MostrarLista(); return; }
        var t = atual;
        if (!await Confirmar(T("Apagar a equipa {0}? O arquivo vai para a pasta backup.", t.E.NomeCompleto))) return;
        try
        {
            var bk = Path.Combine(raiz, PastaDados.Backup, DateTime.Now.ToString("yyyyMMdd-HHmmss"), Path.GetRelativePath(raiz, t.Caminho));
            Directory.CreateDirectory(Path.GetDirectoryName(bk)!);
            File.Move(t.Caminho, bk, true);
        }
        catch (Exception x) { Mensagem(T("Erro ao apagar:") + "\n" + x.Message); return; }
        itens.Remove(t);
        atual = null;
        MostrarLista();
    }

    async Task ExportarEft()
    {
        if (edit == null) return;
        var erros = regras.Validar(edit);
        if (erros.Count > 0) { Mensagem(T("O formato .EFT não aceita a equipa assim:") + "\n\n• " + string.Join("\n• ", erros)); return; }
        var nome = edit.Arquivo.Length > 0 ? edit.Arquivo : EquipaEditavel.SugerirArquivo(edit.NomeAbreviado, itens.Select(i => i.E.Arquivo));
        var bytes = edit.ParaEft();
        try
        {
            if (Arquivos is { CanSave: true } sp)
            {
                var f = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = T("Exportar .EFT"), SuggestedFileName = nome, DefaultExtension = "EFT",
                    FileTypeChoices = new[] { new FilePickerFileType(T("Equipa no formato .EFT")) { Patterns = new[] { "*.EFT", "*.eft" }, MimeTypes = new[] { "application/octet-stream" } } },
                });
                if (f == null) return;
                await using (var s = await f.OpenWriteAsync()) await s.WriteAsync(bytes);
                Mensagem(T("Equipa exportada no formato .EFT:") + "\n" + (f.TryGetLocalPath() ?? f.Name) + "\n\n" + T("Para a usar no jogo Retrô, copie o arquivo para a pasta EQUIPAS dele."));
                return;
            }
            // sem seletor: na pasta de dados, em export/EQUIPAS
            var destino = Path.Combine(raiz, "export", "EQUIPAS", nome);
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            File.WriteAllBytes(destino, bytes);
            Mensagem(T("Equipa exportada no formato .EFT:") + "\n" + destino + "\n\n" + T("Para a usar no jogo Retrô, copie o arquivo para a pasta EQUIPAS dele."));
        }
        catch (Exception x) { Mensagem(T("Erro ao gravar:") + "\n" + x.Message); }
    }

    // ---------------------------------------------------------------- patches
    void ExportarPatch()
    {
        var opcoes = new List<(string Texto, string Nome, Func<List<string>> Jsons)>();
        var sel = atual;
        if (sel != null) opcoes.Add((T("Só esta equipa: {0}", sel.E.NomeCompleto), sel.E.NomeCompleto, () => new List<string> { sel.Caminho }));
        var paisF = filtro;
        if (paisF != null)
        {
            int n = itens.Count(i => i.Pasta == paisF);
            opcoes.Add((T("As equipas de {0} ({1})", NomePais(paisF), n), T("Equipas de {0}", NomePais(paisF)), () => itens.Where(i => i.Pasta == paisF).Select(i => i.Caminho).ToList()));
        }
        opcoes.Add((T("Todas as equipas ({0})", itens.Count), T("Minhas equipas"), () => itens.Select(i => i.Caminho).ToList()));
        int escolha = 0;
        var nomeP = CUi.Caixa(opcoes[0].Nome, 60);
        bool comEft = true, ligas = true;
        var p = new StackPanel { Spacing = 12 };
        p.Children.Add(Tx(T("Um patch (.zip) com as equipas em JSON e os logos delas, para aplicar noutro RetroFoot (ferramenta Patches ou Importar)."), 14, Fraco));
        p.Children.Add(Opcoes(opcoes.Select(o => o.Texto).ToList(), 0, k =>
        {
            // o nome sugerido segue a escolha (se o jogador nao escreveu outro)
            if ((nomeP.Text ?? "") == opcoes[escolha].Nome) nomeP.Text = opcoes[k].Nome;
            escolha = k;
        }));
        p.Children.Add(Campo(T("Nome do patch"), nomeP));
        p.Children.Add(Marca(T("Incluir também os .EFT (formato Retrô)"), true, v => comEft = v));
        p.Children.Add(Marca(T("Incluir os logos das ligas desses países"), true, v => ligas = v));
        CUi.Folha f = null!;
        async Task Fazer(bool escolherLugar)
        {
            var jsons = opcoes[escolha].Jsons();
            if (jsons.Count == 0) return;
            var nome = (nomeP.Text ?? "").Trim();
            if (nome.Length == 0) nome = T("Minhas equipas");
            var arq = EquipaJson.Slug(nome) + ".zip";
            var destino = Path.Combine(raiz, PastaDados.Patches, arq);
            try
            {
                if (escolherLugar && Arquivos is { CanSave: true } sp)
                {
                    var fa = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
                    {
                        Title = T("Exportar patch"), SuggestedFileName = arq, DefaultExtension = "zip",
                        FileTypeChoices = new[] { new FilePickerFileType(T("Patch do RetroFoot")) { Patterns = new[] { "*.zip" }, MimeTypes = new[] { "application/zip" } } },
                    });
                    if (fa == null) return;
                    if (fa.TryGetLocalPath() is { } local) destino = local;
                    else
                    {
                        // (no Android: grava numa copia e passa para o arquivo escolhido)
                        var tmp = Path.Combine(Path.GetTempPath(), "retrofoot-" + arq);
                        var r0 = PatchEquipas.Exportar(raiz, jsons, tmp, nome, comEft, ligas);
                        await using (var o = await fa.OpenWriteAsync())
                        await using (var i = File.OpenRead(tmp))
                            await i.CopyToAsync(o);
                        File.Delete(tmp);
                        Fechar(f);
                        Mensagem(T("Patch gravado: {0} equipas, {1} logos.", r0.Equipas, r0.Logos) + "\n" + fa.Name);
                        return;
                    }
                }
                var r = PatchEquipas.Exportar(raiz, jsons, destino, nome, comEft, ligas);
                Fechar(f);
                Mensagem(T("Patch gravado: {0} equipas, {1} logos.", r.Equipas, r.Logos) + "\n" + destino);
            }
            catch (Exception x) { Mensagem(T("Erro ao gravar:") + "\n" + x.Message); }
        }
        var bts = new List<Control>();
        if (Arquivos?.CanSave == true) bts.Add(Bt(T("Gravar como…"), false, () => _ = Fazer(true)));
        bts.Add(Bt(T("Gravar em patches/"), true, () => _ = Fazer(false)));
        f = Abrir(T("Exportar patch de equipas"), p, Pilha(bts.ToArray()));
    }

    void ImportarPatch()
    {
        var p = new StackPanel { Spacing = 12 };
        p.Children.Add(Tx(T("Aplica um patch na pasta de dados: as equipas (JSON ou .EFT) e os logos dele. Uma equipa com o mesmo arquivo .EFT é substituída."), 14, Fraco));
        var pasta = Path.Combine(raiz, PastaDados.Patches);
        var zips = Directory.Exists(pasta) ? Directory.GetFiles(pasta, "*.zip").OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList() : new List<string>();
        p.Children.Add(R ? CUi.SecaoFolha(T("Biblioteca (pasta patches/):")) : CUi.TituloSecao_(T("Biblioteca (pasta patches/):")));
        string? escolhido = null;
        CUi.Folha f = null!;
        var aplicar = Bt(T("Aplicar"), true, () => { if (escolhido != null) Aplicar(escolhido); }, "Botoes/BBOK.png");
        aplicar.Habilitado = false;
        if (zips.Count == 0) p.Children.Add(Tx(T("(vazia)"), 15, Fraco));
        else
        {
            var linhas = zips.Select(z =>
            {
                var s = new StackPanel { Spacing = 2 };
                string? nome = null;
                int? n = null;
                try
                {
                    using var man = PatchEquipas.LerManifesto(z);
                    nome = man?.RootElement.TryGetProperty("name", out var nn) == true ? nn.GetString() : null;
                    if (man?.RootElement.TryGetProperty("teams", out var tt) == true) n = tt.GetInt32();
                }
                catch { }
                s.Children.Add(Tx(nome ?? Path.GetFileNameWithoutExtension(z), 15.5, null, true, false));
                s.Children.Add(Tx(Path.GetFileName(z) + (n is int k ? " · " + T("{0} equipas", k) : ""), 13, Fraco, false, false));
                return (Control)s;
            }).ToList();
            var (lista, _) = CUi.ListaEscolha(linhas, k => { escolhido = zips[k]; aplicar.Habilitado = true; });
            p.Children.Add(lista);
        }
        void Aplicar(string zip)
        {
            try
            {
                var r = PastaDados.Importar(zip, raiz);
                FecharTodas();
                Escudos.Recarregar();
                atual = null;
                Carregar();
                if (r.PorPais.Count == 1) filtro = r.PorPais.Keys.First();
                MostrarLista();
                var txt = T("{0} equipas importadas", r.Equipas) + (r.PorPais.Count > 0 ? " (" + string.Join(", ", r.PorPais.Select(x => $"{x.Key} {x.Value}")) + ")" : "")
                    + (r.Copiados.TryGetValue(PastaDados.Logos, out var nl) ? "\n" + T("{0} logos", nl) : "")
                    + (r.Outros.Count > 0 ? "\n" + string.Join("\n", r.Outros) : "")
                    + (r.Erros.Count > 0 ? "\n\n" + T("Erros:") + "\n" + string.Join("\n", r.Erros.Take(8)) : "");
                Mensagem(txt);
            }
            catch (Exception x) { Mensagem(T("Erro ao importar:") + "\n" + x.Message); }
        }
        var bts = new List<Control>();
        if (Arquivos?.CanOpen == true)
            bts.Add(Bt(T("Escolher arquivo…"), false, async () =>
            {
                var fs = await Arquivos!.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = T("Importar patch"), AllowMultiple = false,
                    FileTypeFilter = new[] { new FilePickerFileType(T("Patch (.zip) ou equipa (.EFT, .json)")) { Patterns = new[] { "*.zip", "*.EFT", "*.eft", "*.json" }, MimeTypes = new[] { "application/zip", "application/json", "application/octet-stream" } } },
                });
                if (fs.Count > 0 && await Local(fs[0]) is { } c) Aplicar(c);
            }));
        bts.Add(aplicar);
        f = Abrir(T("Importar patch de equipas"), p, CUi.Botoes(bts.ToArray()));
    }

    // ---------------------------------------------------------------- ligas
    // os logos das divisoes e da taca de um pais (gravados na hora)
    void AbrirLigas(string? pais = null)
    {
        pais ??= filtro ?? (edit != null ? EquipaJson.PastaPais(edit.Pais) : null) ?? "POR";
        var ps = pais;
        FecharTodas();
        var p = new StackPanel { Spacing = 12 };
        p.Children.Add(Campo(T("País"), Seletor(LinhaPais(ps, NomePais(ps)), () => EscolherPais(T("País"), ps, false, c => { if (c != null) AbrirLigas(EquipaJson.PastaPais(c)); }))));
        p.Children.Add(Tx(T("Os logos das divisões e da taça aparecem no visual Moderno quando todas as equipas do jogo são deste país. Mudanças gravadas na hora."), 13.5, Fraco));
        var linhas = new List<Control>();
        foreach (var (nivel, nome0) in TEE.NiveisLiga)
        {
            var nv = nivel;
            var nome = T(nome0);
            var l = LogosDados.Ler(raiz, LogosDados.Ligas).FirstOrDefault(x => x.Pais == ps && x.Chave.Trim().ToUpperInvariant() == nv);
            Bitmap? b;
            string origem;
            if (l != null)
            {
                b = l.Arquivo == LogosDados.SemLogo ? null : Escudos.DoJogadorAgora(l.Arquivo);
                origem = l.Arquivo == LogosDados.SemLogo ? T("Sem logo (escolhido)") : T("Escolhido: logos/{0}", l.Arquivo);
            }
            else
            {
                var a = Escudos.LigaDoPacote(ps, nv);
                b = a != null ? Escudos.DoPacote(a) : null;
                origem = a != null ? T("Automático: da coleção do jogo") : T("Automático: sem logo");
            }
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*,Auto") };
            g.Children.Add(new Border { Width = 48, Height = 48, Background = TemaEditor.Xadrez, CornerRadius = new CornerRadius(R ? 0 : 8), Padding = new Thickness(3), Child = b != null ? Escudos.Controle(b, 42) : null, VerticalAlignment = VerticalAlignment.Center });
            var tx = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            tx.Children.Add(Tx(nome, 15.5, null, true, false));
            tx.Children.Add(Tx(origem, 12.5, Fraco));
            Grid.SetColumn(tx, 2);
            g.Children.Add(tx);
            var seta = Tx("›", 22, Fraco);
            Grid.SetColumn(seta, 3);
            g.Children.Add(seta);
            linhas.Add(CUi.Toque(g, () => AcoesLiga(ps, nv, nome), 64));
        }
        p.Children.Add(CUi.Grupo(linhas));
        Abrir(T("Logos das ligas"), p);
    }

    // o que fazer com o logo de uma divisao
    void AcoesLiga(string pais, string nivel, string nome)
    {
        var p = new StackPanel { Spacing = 10 };
        void Gravar(Rgba? img, string? colecao)
        {
            try { TEE.GravarLogoLiga(raiz, pais, nivel, img, colecao); AbrirLigas(pais); }
            catch (Exception x) { Mensagem(T("Erro ao gravar:") + "\n" + x.Message); }
        }
        p.Children.Add(Bt(T("Imagem…"), true, () => _ = EscolherImagem(nome, r => Gravar(r, null), true)));
        p.Children.Add(Bt(T("Coleção…"), false, () => EscolherDaColecao(pais, "", a => Gravar(null, a))));
        p.Children.Add(CUi.Botoes(
            Bt(T("Automático"), false, () => { LogosDados.Remover(raiz, LogosDados.Ligas, pais, nivel); Escudos.Recarregar(); AbrirLigas(pais); }),
            Bt(T("Sem logo"), false, () => { LogosDados.Definir(raiz, LogosDados.Ligas, pais, nivel, LogosDados.SemLogo); Escudos.Recarregar(); AbrirLigas(pais); })));
        Abrir(nome, p, sub: NomePais(pais));
    }
}
