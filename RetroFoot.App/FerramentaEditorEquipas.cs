using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using RetroFoot.Motor.Arquivos;
using RetroFoot.Motor.Base;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// O Editor de Equipas no hub do RetroFoot (docs/ferramentas.md): as equipas da pasta de dados
// (teams/<PAIS>/<slug>.json) por pais; criar, editar e apagar equipas e jogadores com as regras
// do jogo classico (14 a 20 jogadores, guarda-redes, 10 de campo, ate 5 estrangeiros pela Lei
// Bosman e PLOP, nomes, nivel 1 a 20, cores); Exportar .EFT para o jogo classico; o escudo de cada
// equipa e os logos das ligas (EditorEquipasEscudo.cs); patches de equipas e logos (.zip).
// Portado do Editor de Equipes do launcher do jogo classico (src-cross/TeamEditorWindow.cs,
// TeamEditorEscudo.cs; regras de src/TeamCodec.cs). O modelo e as regras ficam no motor
// (Motor/Arquivos/EquipaEditavel.cs), testados em EditorEquipasTestes.
static class FerramentaEditorEquipas
{
    [ModuleInitializer]
    internal static void Registar() =>
        Ferramentas.Registrar(Ferramentas.EditorEquipas, "Editor de Equipas", "Crie e edite equipas, jogadores, cores e escudos", "escudo",
            ctx => new TelaEditorEquipas(ctx));
}

public sealed partial class TelaEditorEquipas : UserControl
{
    // uma equipa da pasta de dados (a versao gravada)
    sealed class ItemEquipa
    {
        public string Caminho = "";
        public EquipaEditavel E = null!;
        public bool Valida;
        public string Pasta => EquipaJson.PastaPais(E.Pais);
    }

    sealed record ItemPais(string? Codigo, string Nome, int N)
    {
        public override string ToString() => Nome;
    }

    readonly Ferramentas.Contexto ctx;
    readonly string raiz;
    readonly TemaEditor tema;
    readonly bool largo;
    RegrasEquipa regras = null!;
    List<TextosDados.Pais> paises = new();
    Dictionary<string, string> nomePais = new(StringComparer.OrdinalIgnoreCase);
    readonly List<ItemEquipa> itens = new();
    readonly List<string> ilegiveis = new();

    // a equipa aberta: a copia em edicao e a gravada (null = nova)
    ItemEquipa? atual;
    EquipaEditavel? edit;
    int aba;

    readonly Grid raizUi = new();
    readonly Panel sobre = new();
    readonly ComboBox filtro;
    readonly TextBox busca;
    readonly ListBox listaEquipas;
    readonly TextBlock contaEquipas;
    readonly ContentControl direita = new();
    bool carregando;

    public TelaEditorEquipas(Ferramentas.Contexto ctx)
    {
        this.ctx = ctx;
        raiz = ctx.RaizDados;
        largo = ctx.Largo;
        tema = new TemaEditor();
        Focusable = true;

        filtro = tema.Escolha();
        filtro.ItemTemplate = new FuncDataTemplate<ItemPais>((p, _) => p == null ? new Panel() : LinhaPais(p.Codigo, p.Nome + (p.N > 0 ? $" ({p.N})" : "")));
        filtro.SelectionChanged += (_, _) => { if (!carregando) Filtrar(); };
        busca = tema.Entrada();
        busca.Watermark = T("Procurar equipa…");
        busca.TextChanged += (_, _) => Filtrar();
        listaEquipas = tema.Lista0();
        Selecao(listaEquipas);
        listaEquipas.ItemTemplate = new FuncDataTemplate<ItemEquipa>((t, _) => t == null ? new Panel() : LinhaEquipa(t));
        listaEquipas.SelectionChanged += async (_, _) => { if (!carregando) await TrocarEquipa(listaEquipas.SelectedItem as ItemEquipa); };
        contaEquipas = tema.Linha1("", 11, tema.Fraco);

        Carregar();
        Montar();
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => listaEquipas.Focus(), DispatcherPriority.Loaded);
    }

    // Esc: fecha a janelinha aberta; com alteracoes por gravar, pergunta antes de voltar ao hub
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape) return;
        if (sobre.Children.Count > 0)
        {
            e.Handled = true;
            sobre.Children.Clear();
            pergunta?.TrySetResult(false);
            return;
        }
        if (!Alterada) return;   // o hub volta
        e.Handled = true;
        _ = SairPerguntando();
    }

    async Task SairPerguntando()
    {
        if (await Confirmar(T("Há alterações não gravadas nesta equipa. Descartá-las?"))) ctx.Voltar();
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

    // ---------------------------------------------------------------- montagem
    void Montar()
    {
        var esquerda = MontarEsquerda();
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(largo ? "270,*" : "222,*"), ColumnSpacing = 8 };
        g.Children.Add(esquerda);
        Grid.SetColumn(direita, 1);
        g.Children.Add(direita);
        raizUi.Children.Add(tema.Escopo(g));
        raizUi.Children.Add(sobre);
        Content = raizUi;
        MontarFiltro(null);
        MostrarDireita();
    }

    Control MontarEsquerda()
    {
        var d = new DockPanel();
        var topo = new StackPanel { Spacing = 5 };
        topo.Children.Add(filtro);
        topo.Children.Add(busca);
        DockPanel.SetDock(topo, Dock.Top);
        d.Children.Add(topo);

        var baixo = new StackPanel { Spacing = 5, Margin = new Thickness(0, 5, 0, 0) };
        baixo.Children.Add(contaEquipas);
        var g1 = new UniformGrid { Columns = 2, ColumnSpacing = 5 };
        g1.Children.Add(tema.Botao(T("Nova"), () => _ = NovaEquipa(), false, "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z"));
        g1.Children.Add(tema.Botao(T("Apagar"), () => _ = Apagar(), false, "M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z"));
        baixo.Children.Add(g1);
        var g2 = new UniformGrid { Columns = 2, ColumnSpacing = 5 };
        g2.Children.Add(tema.Botao(T("Importar…"), ImportarPatch, false, "peca"));
        g2.Children.Add(tema.Botao(T("Exportar…"), ExportarPatch, false, "disquete"));
        baixo.Children.Add(g2);
        baixo.Children.Add(tema.Botao(T("Logos das ligas…"), () => AbrirLigas(), false, "bola"));
        DockPanel.SetDock(baixo, Dock.Bottom);
        d.Children.Add(baixo);
        listaEquipas.Margin = new Thickness(0);
        var lista = tema.Funda(listaEquipas);
        lista.Margin = new Thickness(0, 5, 0, 0);
        d.Children.Add(lista);
        return tema.Caixa(d, null, new Thickness(7));
    }

    // a selecao e o hover das listas, nas cores do visual
    void Selecao(ListBox l)
    {
        foreach (var (classe, fundo, letra) in new[]
        {
            (":selected", tema.Selecao, tema.SelecaoTexto), (":selected:pointerover", tema.Selecao, tema.SelecaoTexto),
            (":selected:focus", tema.Selecao, tema.SelecaoTexto), (":pointerover", tema.Hover, tema.ListaTexto),
        })
        {
            var cl = classe.Split(':', StringSplitOptions.RemoveEmptyEntries);
            l.Styles.Add(new Style(x =>
            {
                var s = x.OfType<ListBoxItem>();
                foreach (var c in cl) s = s.Class(":" + c);
                return s.Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
            })
            {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, fundo), new Setter(ContentPresenter.ForegroundProperty, letra) },
            });
        }
        l.Styles.Add(new Style(x => x.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(5, 3)), new Setter(ListBoxItem.MinHeightProperty, 0.0) },
        });
    }

    Control LinhaPais(string? cod, string texto)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        sp.Children.Add(Bandeira(cod, 20, 13));
        sp.Children.Add(new TextBlock { Text = texto, FontFamily = tema.Fonte, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }

    static Control Bandeira(string? cod, double w, double h)
    {
        var b = cod == null ? null : W95.Imagem($"Bandeiras/{cod.Trim().ToUpperInvariant()}.png");
        return b != null
            // as bandeiras sao redondas (128x128): quadradas, para nao achatar
            ? new Image { Source = b, Width = Math.Max(w, h) * 0.8, Height = Math.Max(w, h) * 0.8, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center }
            : new Border { Width = w, Height = h };
    }

    // o logo (do jogador ou do jogo) ou o escudo nas cores
    Control EscudoDe(EquipaEditavel e, double tam)
    {
        var b = Escudos.Escudo(e.Pais, e.NomeCompleto, e.NomeAbreviado);
        return Escudos.Controle(b, tam) ?? TemaEditor.EscudoCores(e.CorLetra, e.CorFundo, tam);
    }

    Control LinhaEquipa(ItemEquipa t)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6 };
        var esc = EscudoDe(t.E, 22);
        g.Children.Add(esc);
        var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textos.Children.Add(new TextBlock
        {
            Text = t.E.NomeAbreviado.Length > 0 ? t.E.NomeAbreviado : T("(sem nome)"), FontFamily = tema.Fonte, FontSize = 12.5, FontWeight = FontWeight.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        textos.Children.Add(new TextBlock
        {
            Text = T("nível {0} · {1} jog.", t.E.Nivel, t.E.Jogadores.Count) + (filtro.SelectedItem is ItemPais { Codigo: null } ? " · " + t.Pasta : ""),
            FontFamily = tema.Fonte, FontSize = 10.5, Opacity = 0.72, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(textos, 1);
        g.Children.Add(textos);
        if (!t.Valida)
        {
            var av = Ui.Icone("aviso", 15, tema.Erro);
            ToolTip.SetTip(av, T("O jogo não aceita esta equipa assim (abra para ver porquê)"));
            Grid.SetColumn(av, 2);
            g.Children.Add(av);
        }
        return g;
    }

    void MontarFiltro(string? manter)
    {
        carregando = true;
        var porPais = itens.GroupBy(i => i.Pasta).ToDictionary(x => x.Key, x => x.Count());
        var l = new List<ItemPais> { new(null, T("Todos os países"), itens.Count) };
        // os paises com equipas primeiro; depois os outros (para criar a 1a equipa de um pais)
        l.AddRange(porPais.Keys.Select(c => new ItemPais(c, NomePais(c), porPais[c])).OrderBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase));
        var anterior = manter ?? (filtro.SelectedItem as ItemPais)?.Codigo ?? (porPais.Count > 0 ? (porPais.ContainsKey("POR") ? "POR" : porPais.MaxBy(p => p.Value).Key) : null);
        l.AddRange(paises.Where(p => !porPais.ContainsKey(EquipaJson.PastaPais(p.Codigo))).Select(p => new ItemPais(EquipaJson.PastaPais(p.Codigo), p.Nome, 0))
            .OrderBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase));
        filtro.ItemsSource = l;
        filtro.SelectedItem = l.FirstOrDefault(p => p.Codigo == anterior) ?? l[0];
        carregando = false;
        Filtrar();
    }

    string? PaisDoFiltro => (filtro.SelectedItem as ItemPais)?.Codigo;

    void Filtrar()
    {
        var pais = PaisDoFiltro;
        var q = LogosDados.Normalizar(busca.Text ?? "");
        var l = itens.Where(i => (pais == null || i.Pasta == pais) &&
            (q.Length == 0 || LogosDados.Normalizar(i.E.NomeCompleto + " " + i.E.NomeAbreviado + " " + i.E.Arquivo).Contains(q))).ToList();
        carregando = true;
        listaEquipas.ItemsSource = l;
        listaEquipas.SelectedItem = atual != null && l.Contains(atual) ? atual : null;
        carregando = false;
        contaEquipas.Text = T("{0} equipas", l.Count) + (ilegiveis.Count > 0 ? " · " + T("{0} arquivos com erro", ilegiveis.Count) : "");
        ToolTip.SetTip(contaEquipas, ilegiveis.Count > 0 ? string.Join("\n", ilegiveis.Take(20)) : null);
    }

    // ---------------------------------------------------------------- equipa aberta
    bool Alterada => edit != null && (atual == null || !edit.MesmoConteudo(atual.E) || escudoPendente != null);

    async Task TrocarEquipa(ItemEquipa? t)
    {
        if (t == atual && t != null) return;
        if (Alterada && !await Confirmar(T("Há alterações não gravadas nesta equipa. Descartá-las?")))
        {
            carregando = true;
            listaEquipas.SelectedItem = atual;
            carregando = false;
            return;
        }
        Abrir(t);
    }

    void Abrir(ItemEquipa? t)
    {
        atual = t;
        edit = t?.E.Copia();
        escudoPendente = null;
        MostrarDireita();
    }

    void MostrarDireita()
    {
        if (edit == null)
        {
            var p = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 380 };
            var ic = Ui.Icone("escudo", 54, tema.Fraco);
            p.Children.Add(ic);
            var t1 = tema.T(T("Escolha uma equipa na lista ou crie uma nova."), 14, null, true);
            t1.TextAlignment = TextAlignment.Center;
            p.Children.Add(t1);
            var t2 = tema.T(T("As equipas ficam na pasta de dados (teams/<país>/<equipa>.json). O jogo usa-as como estão; \"Exportar .EFT\" grava uma equipa no formato Retrô .EFT."), 12, tema.Fraco);
            t2.TextAlignment = TextAlignment.Center;
            p.Children.Add(t2);
            direita.Content = tema.Caixa(p);
            return;
        }
        var d = new DockPanel();
        var cab = Cabecalho();
        DockPanel.SetDock(cab, Dock.Top);
        d.Children.Add(cab);
        var textos = new List<TextBlock>();
        var abas = tema.Abas(new[] { T("Equipa"), T("Jogadores ({0})", edit.Jogadores.Count), T("Escudo") }, aba, i => { aba = i; MostrarDireita(); }, textos);
        abas.Margin = new Thickness(0, 6, 0, 0);
        DockPanel.SetDock(abas, Dock.Top);
        d.Children.Add(abas);
        Control corpo = aba switch { 1 => AbaJogadores(), 2 => AbaEscudo(), _ => AbaEquipa() };
        var caixa = tema.Caixa(corpo, null, new Thickness(9, 8));
        d.Children.Add(caixa);
        direita.Content = d;
    }

    readonly TextBlock estado = new() { FontSize = 12, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center };

    Control Cabecalho()
    {
        var e = edit!;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
        var esc = escudoPendente?.Bitmap is { } bp ? Escudos.Controle(bp, 40)! : escudoPendente?.Tipo == TipoEscudo.Nenhum ? TemaEditor.EscudoCores(e.CorLetra, e.CorFundo, 40) : EscudoDe(e, 40);
        g.Children.Add(esc);
        var nomes = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        nomes.Children.Add(tema.Linha1(e.NomeCompleto.Length > 0 ? e.NomeCompleto : T("(sem nome)"), 15, tema.Texto, true));
        var sub = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        sub.Children.Add(Bandeira(e.Pais, 18, 12));
        sub.Children.Add(tema.Linha1($"{NomePais(e.Pais)} · {(e.Arquivo.Length > 0 ? e.Arquivo : T("nova"))}", 11.5, tema.Fraco));
        nomes.Children.Add(sub);
        // o estado numa linha propria (em 4:3 nao cabe ao lado do pais)
        estado.TextTrimming = TextTrimming.CharacterEllipsis;
        // o mesmo TextBlock a cada cabecalho refeito: sai do anterior antes
        (estado.Parent as Panel)?.Children.Remove(estado);
        nomes.Children.Add(estado);
        Grid.SetColumn(nomes, 1);
        g.Children.Add(nomes);
        var bts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        bts.Children.Add(tema.Botao(T("Gravar"), () => _ = Gravar(), true, "disquete"));
        bts.Children.Add(tema.Botao(T("Desfazer"), Desfazer, false, "M12.5,8C9.85,8 7.45,9 5.6,10.6L2,7V16H11L7.38,12.38C8.77,11.22 10.54,10.5 12.5,10.5C16.04,10.5 19.05,12.81 20.1,16L22.47,15.22C21.08,11.03 17.15,8 12.5,8Z"));
        Grid.SetColumn(bts, 2);
        g.Children.Add(bts);
        AtualizarEstado();
        var c = tema.Caixa(g, null, new Thickness(9, 6));
        return c;
    }

    void AtualizarEstado()
    {
        if (edit == null) return;
        var erros = regras.Validar(edit);
        estado.FontFamily = tema.Fonte;
        estado.Text = (erros.Count == 0 ? "✓ " + T("Pronta para o jogo") : "✗ " + T("{0} problemas", erros.Count)) + (Alterada ? " · " + T("por gravar") : "");
        estado.Foreground = erros.Count == 0 ? tema.Ok : tema.Erro;
    }

    // ---------------------------------------------------------------- aba Equipa
    StackPanel? situacao;

    Control AbaEquipa()
    {
        var e = edit!;
        var campos = new StackPanel { Spacing = 6 };
        double rot = largo ? 118 : 96;
        Control Campo(string r, Control c)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{rot},*") };
            var t = tema.Linha1(r, 12, tema.Rotulo, true);
            g.Children.Add(t);
            Grid.SetColumn(c, 1);
            g.Children.Add(c);
            return g;
        }
        var completo = tema.Entrada(e.NomeCompleto, RegrasEquipa.MaxNomeCompleto);
        completo.TextChanged += (_, _) => { e.NomeCompleto = completo.Text ?? ""; Mudou(); };
        var curto = tema.Entrada(e.NomeAbreviado, RegrasEquipa.MaxNome);
        curto.TextChanged += (_, _) => { e.NomeAbreviado = curto.Text ?? ""; Mudou(); };
        var treinador = tema.Entrada(e.Treinador, RegrasEquipa.MaxNome);
        treinador.TextChanged += (_, _) => { e.Treinador = treinador.Text ?? ""; Mudou(); };
        var pais = tema.Escolha();
        var lp = paises.Select(p => new ItemPais(p.Codigo, p.Nome, 0)).OrderBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (!lp.Any(p => string.Equals(p.Codigo, e.Pais.Trim(), StringComparison.OrdinalIgnoreCase)) && e.Pais.Trim().Length > 0)
            lp.Insert(0, new ItemPais(e.Pais, e.Pais + " " + T("(não existe em countries.tsv)"), 0));
        pais.ItemsSource = lp;
        pais.ItemTemplate = new FuncDataTemplate<ItemPais>((p, _) => p == null ? new Panel() : LinhaPais(p.Codigo, $"{p.Nome} ({p.Codigo})"));
        pais.SelectedItem = lp.FirstOrDefault(p => string.Equals(p.Codigo, e.Pais.Trim(), StringComparison.OrdinalIgnoreCase));
        pais.SelectionChanged += (_, _) => { if (pais.SelectedItem is ItemPais p && p.Codigo != null && p.Codigo != e.Pais) { e.Pais = p.Codigo; Mudou(); } };

        var nivel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var nv = tema.Linha1(e.Nivel.ToString(), 15, null, true);
        nv.Width = 28;
        nv.TextAlignment = TextAlignment.Center;
        var ajuda = tema.T("", 11, tema.Fraco);
        void Nivel(int passo)
        {
            e.Nivel = Math.Clamp(e.Nivel + passo, RegrasEquipa.NivelMin, RegrasEquipa.NivelMax);
            nv.Text = e.Nivel.ToString();
            ajuda.Text = T("1 = mais fraca, 20 = mais forte; as maiores começam nas divisões de cima");
            Mudou();
        }
        nivel.Children.Add(tema.Botao("−", () => Nivel(-1)));
        nivel.Children.Add(nv);
        nivel.Children.Add(tema.Botao("+", () => Nivel(+1)));
        Nivel(0);
        var nivelG = new StackPanel { Spacing = 2, Children = { nivel, ajuda } };

        var cores = new WrapPanel { ItemSpacing = 6, LineSpacing = 4 };
        var amostra = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(10, 3), VerticalAlignment = VerticalAlignment.Center, BorderBrush = Ui.Hex("#808080"), BorderThickness = new Thickness(1) };
        var amostraT = new TextBlock { FontFamily = tema.Fonte, FontSize = 13, FontWeight = FontWeight.Bold };
        amostra.Child = amostraT;
        void Pintar()
        {
            amostra.Background = TemaEditor.Pincel(e.CorFundo);
            amostraT.Foreground = TemaEditor.Pincel(e.CorLetra);
            amostraT.Text = e.NomeAbreviado.Length > 0 ? e.NomeAbreviado : "ABC";
        }
        Pintar();
        curto.TextChanged += (_, _) => Pintar();
        cores.Children.Add(BotaoCor(T("Letra"), () => e.CorLetra, c => { e.CorLetra = c; Pintar(); Mudou(); }));
        cores.Children.Add(BotaoCor(T("Fundo"), () => e.CorFundo, c => { e.CorFundo = c; Pintar(); Mudou(); }));
        cores.Children.Add(amostra);

        campos.Children.Add(Campo(T("Nome completo"), completo));
        campos.Children.Add(Campo(T("Nome abreviado"), curto));
        campos.Children.Add(Campo(T("País"), pais));
        campos.Children.Add(Campo(T("Nível"), nivelG));
        campos.Children.Add(Campo(T("Treinador"), treinador));
        campos.Children.Add(Campo(T("Cores"), cores));
        var eft = new WrapPanel { ItemSpacing = 8, LineSpacing = 4 };
        eft.Children.Add(tema.Linha1(e.Arquivo.Length > 0 ? e.Arquivo : T("(escolhido ao gravar)"), 12, tema.Fraco));
        eft.Children.Add(tema.Botao(T("Exportar .EFT…"), () => _ = ExportarEft(), false, "disquete"));
        campos.Children.Add(Campo(T("Arquivo"), eft));

        situacao = new StackPanel { Spacing = 3 };
        AtualizarSituacao();
        var sit = tema.Caixa(new ScrollViewer { Content = situacao }, T("Situação para o jogo"));
        sit.Background = tema.Lista;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(largo ? "*,300" : "*,172"), ColumnSpacing = 10 };
        g.Children.Add(new ScrollViewer { Content = campos });
        Grid.SetColumn(sit, 1);
        g.Children.Add(sit);
        return g;
    }

    void Mudou()
    {
        AtualizarEstado();
        AtualizarSituacao();
    }

    void AtualizarSituacao()
    {
        if (situacao == null || edit == null) return;
        var t = edit;
        situacao.Children.Clear();
        int gr = t.Jogadores.Count(j => j.Posicao == 0), campo = t.Jogadores.Count - gr, est = regras.Estrangeiros(t);
        situacao.Children.Add(tema.T(T("Jogadores: {0} (de {1} a {2})", t.Jogadores.Count, RegrasEquipa.MinJogadores, RegrasEquipa.MaxJogadores), 12));
        situacao.Children.Add(tema.T(T("Guarda-redes: {0} (mín. 1) · Campo: {1} (mín. {2})", gr, campo, RegrasEquipa.MinCampo), 12));
        situacao.Children.Add(tema.T(regras.Liberado ? T("Estrangeiros: sem limite (todos na Lei Bosman)")
            : T("Estrangeiros: {0} de {1} (Lei Bosman e PLOP não contam)", est, RegrasEquipa.MaxEstrangeiros), 12));
        var erros = regras.Validar(t);
        var sep = new Border { Height = 1, Background = tema.Borda, Margin = new Thickness(0, 4) };
        situacao.Children.Add(sep);
        if (erros.Count == 0) situacao.Children.Add(tema.T("✓ " + T("Pronta para o jogo"), 12.5, tema.Ok, true));
        else foreach (var x in erros) situacao.Children.Add(tema.T("✗ " + x, 12, tema.Erro));
    }

    Control BotaoCor(string nome, Func<uint> ler, Action<uint> escrever)
    {
        var quad = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(2), BorderBrush = Ui.Hex("#404040"), BorderThickness = new Thickness(1), Background = TemaEditor.Pincel(ler()) };
        var b = tema.Botao(nome, () => { }, false);
        if (b.Child is StackPanel sp) sp.Children.Insert(0, quad);
        b.Clique += () => EscolherCor(nome, ler(), c => { escrever(c); quad.Background = TemaEditor.Pincel(c); });
        return b;
    }

    // as 16 cores do original, as sugeridas pelo escudo e um codigo #RRGGBB
    void EscolherCor(string titulo, uint atualCor, Action<uint> ok)
    {
        var p = new StackPanel { Spacing = 10 };
        uint escolhida = atualCor;
        var hex = tema.Entrada("#" + atualCor.ToString("X6"), 7);
        var previa = new Border { Width = 60, Height = 26, CornerRadius = new CornerRadius(3), BorderBrush = Ui.Hex("#404040"), BorderThickness = new Thickness(1), Background = TemaEditor.Pincel(atualCor) };
        void Por(uint c)
        {
            escolhida = c;
            previa.Background = TemaEditor.Pincel(c);
            var s = "#" + c.ToString("X6");
            if (!string.Equals(hex.Text, s, StringComparison.OrdinalIgnoreCase)) hex.Text = s;
        }
        hex.TextChanged += (_, _) =>
        {
            var s = (hex.Text ?? "").Trim().TrimStart('#');
            if (s.Length == 6 && uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var c)) { escolhida = c; previa.Background = TemaEditor.Pincel(c); }
        };
        var grade = new UniformGrid { Columns = 8, ColumnSpacing = 4, RowSpacing = 4 };
        foreach (var c in TemaEditor.Paleta)
        {
            var cc = c;
            var q = new Clicavel { Height = 26, CornerRadius = new CornerRadius(3), Background = TemaEditor.Pincel(c), BorderBrush = Ui.Hex("#404040"), BorderThickness = new Thickness(1) };
            q.Clique += () => Por(cc);
            ToolTip.SetTip(q, "#" + c.ToString("X6"));
            grade.Children.Add(q);
        }
        p.Children.Add(tema.T(T("As 16 cores Retrô:"), 12, tema.Fraco));
        p.Children.Add(grade);
        var lh = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        lh.Children.Add(tema.T(T("Código:"), 12, tema.Rotulo, true));
        hex.Width = 100;
        lh.Children.Add(hex);
        lh.Children.Add(previa);
        p.Children.Add(lh);
        Action fechar = () => sobre.Children.Clear();
        p.Children.Add(tema.Botoes(tema.Botao(T("OK"), () => { fechar(); ok(escolhida); }, true), tema.Botao(T("Cancelar"), fechar)));
        AbrirModal(titulo, p, 400);
    }

    // ---------------------------------------------------------------- aba Jogadores
    sealed record LinhaJogador(int Indice, JogadorEditavel J);
    ListBox? listaJog;

    string Colunas => largo ? "34,*,92,40,44,98,26,104" : "30,*,72,36,22,92";

    Control AbaJogadores()
    {
        var e = edit!;
        var d = new DockPanel();
        var cab = Grade(largo
            ? new[] { T("Pos."), T("Nome"), T("País"), T("Nota"), T("Lesão"), T("Comport."), "★", T("Estrangeiro") }
            : new[] { T("Pos."), T("Nome"), T("País"), T("Nota"), "★", T("Estrangeiro") }, true);
        // (a moldura funda tem 2 de folga e cada linha 5: as colunas batem; a barra de rolagem
        // e descontada por Ui.AlinharCabecalho)
        cab.Margin = new Thickness(7, 0, 7, 3);
        DockPanel.SetDock(cab, Dock.Top);
        d.Children.Add(cab);

        var baixo = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
        int gr = e.Jogadores.Count(j => j.Posicao == 0);
        var conta = tema.Linha1(T("{0} jogadores · {1} GR · {2} estrangeiros", e.Jogadores.Count, gr, regras.Estrangeiros(e)), 11.5, tema.Fraco);
        baixo.Children.Add(conta);
        var bts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        var add = tema.Botao(T("Adicionar"), () => EditarJogador(null), false, "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z");
        TemaEditor.Ativo(add, e.Jogadores.Count < RegrasEquipa.MaxJogadores);
        var ed = tema.Botao(T("Editar"), () => { if (listaJog?.SelectedItem is LinhaJogador l) EditarJogador(l.Indice); });
        var rm = tema.Botao(T("Remover"), () => { if (listaJog?.SelectedItem is LinhaJogador l) { e.Jogadores.RemoveAt(l.Indice); MostrarDireita(); } });
        bts.Children.Add(add);
        bts.Children.Add(ed);
        bts.Children.Add(rm);
        Grid.SetColumn(bts, 1);
        baixo.Children.Add(bts);
        DockPanel.SetDock(baixo, Dock.Bottom);
        d.Children.Add(baixo);

        listaJog = tema.Lista0();
        Selecao(listaJog);
        listaJog.ItemTemplate = new FuncDataTemplate<LinhaJogador>((l, _) => l == null ? new Panel() : LinhaTabela(l));
        listaJog.ItemsSource = e.Jogadores.Select((j, i) => new LinhaJogador(i, j)).ToList();
        listaJog.DoubleTapped += (_, _) => { if (listaJog.SelectedItem is LinhaJogador l) EditarJogador(l.Indice); };
        listaJog.KeyDown += (_, a) =>
        {
            if (listaJog.SelectedItem is not LinhaJogador l) return;
            if (a.Key == Key.Enter) { a.Handled = true; EditarJogador(l.Indice); }
            else if (a.Key == Key.Delete) { a.Handled = true; e.Jogadores.RemoveAt(l.Indice); MostrarDireita(); }
        };
        void Sel() { bool s = listaJog.SelectedItem != null; TemaEditor.Ativo(ed, s); TemaEditor.Ativo(rm, s); }
        listaJog.SelectionChanged += (_, _) => Sel();
        Sel();
        d.Children.Add(tema.Funda(listaJog));
        Ui.AlinharCabecalho(cab, listaJog);
        return d;
    }

    Grid Grade(string[] textos, bool cabecalho)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(Colunas) };
        for (int i = 0; i < textos.Length; i++)
        {
            var tb = new TextBlock
            {
                Text = textos[i], FontFamily = tema.Fonte, FontSize = cabecalho ? 11 : 12.5, FontWeight = cabecalho ? FontWeight.Bold : FontWeight.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(i == 0 ? 0 : 5, 0, 0, 0),
            };
            if (cabecalho) tb.Foreground = tema.Rotulo;
            bool esq = i is 1 or 2 || i == textos.Length - 1;
            tb.HorizontalAlignment = esq ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            Grid.SetColumn(tb, i);
            g.Children.Add(tb);
        }
        return g;
    }

    static IBrush CorPosicao(int p) => p switch
    {
        0 => Ui.Hex("#E0A000"), 1 => Ui.Hex("#2E7D32"), 2 => Ui.Hex("#1565C0"), _ => Ui.Hex("#C62828"),
    };

    string TextoEstrangeiro(RegrasEquipa.Situacao s) => s switch
    {
        RegrasEquipa.Situacao.Estrangeiro => T("Sim"),
        RegrasEquipa.Situacao.Bosman => T("Não (Bosman)"),
        RegrasEquipa.Situacao.Plop => T("Não (PLOP)"),
        _ => "",
    };

    Control LinhaTabela(LinhaJogador l)
    {
        var j = l.J;
        var a = j.Atributos;
        var sit = regras.Classificar(j.Pais, edit!.Pais);
        var cols = largo
            ? new[] { "", j.Nome, "", a.Nota.ToString(), a.Lesao.ToString(), T(Atributos.Comportamentos[Math.Clamp(a.Comportamento, 0, 5)]), a.Estrela ? "★" : "", TextoEstrangeiro(sit) }
            : new[] { "", j.Nome, "", a.Nota.ToString(), a.Estrela ? "★" : "", TextoEstrangeiro(sit) };
        var g = Grade(cols, false);
        var pos = new Border
        {
            Background = CorPosicao(j.Posicao), CornerRadius = new CornerRadius(3), Width = 24, Padding = new Thickness(0, 1), HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock { Text = Idioma.Posicao("GDMA"[Math.Clamp(j.Posicao, 0, 3)]), Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = tema.Fonte },
        };
        g.Children.Add(pos);
        var pais = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        pais.Children.Add(Bandeira(j.Pais, 18, 12));
        var paisT = new TextBlock { Text = j.Pais.ToUpperInvariant(), FontFamily = tema.Fonte, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
        if (!regras.PaisExiste(j.Pais)) paisT.Foreground = tema.Erro;
        pais.Children.Add(paisT);
        Grid.SetColumn(pais, 2);
        g.Children.Add(pais);
        var est = g.Children.OfType<TextBlock>().Last();
        est.Foreground = sit == RegrasEquipa.Situacao.Estrangeiro ? tema.Erro : tema.Ok;
        var estrela = g.Children.OfType<TextBlock>().ElementAt(largo ? 6 : 4);
        estrela.Foreground = Ui.Hex("#C79A00");
        if (RegrasEquipa.ErroNome(j.Nome) != null) g.Children.OfType<TextBlock>().ElementAt(1).Foreground = tema.Erro;
        return g;
    }

    // a ficha do jogador: nome, posicao, pais e o que o jogo tira do nome, ao vivo
    void EditarJogador(int? indice)
    {
        var e = edit!;
        var j = indice is int k ? e.Jogadores[k].Copia() : new JogadorEditavel { Pais = e.Pais.Trim().ToUpperInvariant(), Posicao = 1 };
        var p = new StackPanel { Spacing = 8 };
        var nome = tema.Entrada(j.Nome, RegrasEquipa.MaxNome);
        var posicoes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var bpos = new List<Clicavel>();
        string[] nomesPos = { "Guarda-redes", "Defesa", "Médio", "Avançado" };
        var pais = tema.Escolha();
        var lp = paises.Select(x => new ItemPais(x.Codigo, x.Nome, 0)).OrderBy(x => x.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
        pais.ItemsSource = lp;
        pais.ItemTemplate = new FuncDataTemplate<ItemPais>((x, _) => x == null ? new Panel() : LinhaPais(x.Codigo, $"{x.Nome} ({x.Codigo})"));
        pais.SelectedItem = lp.FirstOrDefault(x => string.Equals(x.Codigo, j.Pais.Trim(), StringComparison.OrdinalIgnoreCase));
        var nota = tema.T("", 20, null, true);
        var lesao = tema.T("", 20, null, true);
        var comp = tema.T("", 13, null, true);
        var estrela = tema.T("", 13, null, true);
        var sit = tema.T("", 12);
        var erro = tema.T("", 12, tema.Erro);
        Control Quadro(string r, TextBlock v) => tema.Caixa(new StackPanel { Children = { tema.Linha1(r, 10.5, tema.Fraco), v } }, null, new Thickness(8, 4));
        void Atualizar()
        {
            for (int i = 0; i < 4; i++)
            {
                bool s = i == j.Posicao;
                bpos[i].Background = s ? CorPosicao(i) : tema.Hover;
                if (bpos[i].Child is StackPanel sp) foreach (var t in sp.Children.OfType<TextBlock>()) t.Foreground = s ? Brushes.White : tema.Texto;
            }
            bool vazio = j.Nome.Trim().Length == 0;
            var a = j.Atributos;
            nota.Text = vazio ? "—" : a.Nota.ToString();
            lesao.Text = vazio ? "—" : a.Lesao.ToString();
            comp.Text = vazio ? "—" : T(Atributos.Comportamentos[Math.Clamp(a.Comportamento, 0, 5)]);
            estrela.Text = vazio ? "—" : a.Estrela ? "★ " + T("Estrela") : T("Sem estrela");
            estrela.Foreground = a.Estrela && !vazio ? Ui.Hex("#C79A00") : tema.Fraco;
            var s0 = regras.Classificar(j.Pais, e.Pais);
            sit.Text = s0 switch
            {
                RegrasEquipa.Situacao.Nacional => T("Nacional."),
                RegrasEquipa.Situacao.Bosman => T("Não conta como estrangeiro: os dois países estão na Lei Bosman."),
                RegrasEquipa.Situacao.Plop => T("Não conta como estrangeiro: os dois países são de língua portuguesa (PLOP)."),
                _ => T("Estrangeiro: conta no limite de {0} por equipa.", RegrasEquipa.MaxEstrangeiros),
            };
            sit.Foreground = s0 == RegrasEquipa.Situacao.Estrangeiro ? tema.Erro : tema.Ok;
            erro.Text = vazio ? "" : RegrasEquipa.ErroNome(j.Nome) ?? "";
        }
        for (int i = 0; i < 4; i++)
        {
            int q = i;
            var b = tema.Botao(T(nomesPos[i]), () => { j.Posicao = q; Atualizar(); });
            bpos.Add(b);
            posicoes.Children.Add(b);
        }
        nome.TextChanged += (_, _) => { j.Nome = nome.Text ?? ""; Atualizar(); };
        pais.SelectionChanged += (_, _) => { if (pais.SelectedItem is ItemPais x && x.Codigo != null) { j.Pais = x.Codigo; Atualizar(); } };
        Control Linha(string r, Control c)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*") };
            g.Children.Add(tema.Linha1(r, 12, tema.Rotulo, true));
            Grid.SetColumn(c, 1);
            g.Children.Add(c);
            return g;
        }
        p.Children.Add(Linha(T("Nome"), nome));
        p.Children.Add(Linha(T("Posição"), posicoes));
        p.Children.Add(Linha(T("País"), pais));
        p.Children.Add(erro);
        p.Children.Add(tema.T(T("O jogo tira estes valores do nome (e da posição):"), 11.5, tema.Fraco));
        var at = new UniformGrid { Columns = 4, ColumnSpacing = 6 };
        at.Children.Add(Quadro(T("Nota"), nota));
        at.Children.Add(Quadro(T("Lesão"), lesao));
        at.Children.Add(Quadro(T("Comportamento"), comp));
        at.Children.Add(Quadro(T("Estrela"), estrela));
        p.Children.Add(at);
        p.Children.Add(sit);
        Action fechar = () => sobre.Children.Clear();
        Action ok = () =>
        {
            var n = j.Nome.Trim();
            if (RegrasEquipa.ErroNome(n) is { } x) { erro.Text = x; nome.Focus(); return; }
            if (!regras.PaisExiste(j.Pais)) { erro.Text = T("Escolha o país do jogador."); return; }
            j.Nome = n;
            if (indice is int k2)
            {
                bool mudouPos = e.Jogadores[k2].Posicao != j.Posicao;
                e.Jogadores[k2] = j;
                // mudou de posicao: vai para o grupo da posicao nova (G, D, M, A)
                if (mudouPos) { e.Jogadores.RemoveAt(k2); e.InserirNaPosicao(j); }
            }
            else e.InserirNaPosicao(j);
            fechar();
            MostrarDireita();
            int novo = e.Jogadores.IndexOf(j);
            if (listaJog != null && novo >= 0) Dispatcher.UIThread.Post(() => { listaJog.SelectedIndex = novo; listaJog.ScrollIntoView(novo); });
        };
        nome.KeyDown += (_, a) => { if (a.Key == Key.Enter) { a.Handled = true; ok(); } };
        p.Children.Add(tema.Botoes(tema.Botao(T("OK"), ok, true), tema.Botao(T("Cancelar"), fechar)));
        Atualizar();
        AbrirModal(indice == null ? T("Novo jogador") : T("Editar jogador"), p, 520);
        Dispatcher.UIThread.Post(() => { nome.Focus(); nome.SelectAll(); }, DispatcherPriority.Loaded);
    }

    // ---------------------------------------------------------------- acoes
    async Task NovaEquipa()
    {
        if (Alterada && !await Confirmar(T("Há alterações não gravadas nesta equipa. Descartá-las?"))) return;
        var pais = PaisDoFiltro ?? atual?.Pasta ?? "POR";
        atual = null;
        edit = new EquipaEditavel { Pais = pais, NomeCompleto = T("NOVA EQUIPA"), NomeAbreviado = T("NOVA"), Nivel = 5, CorLetra = 0xFFFFFF, CorFundo = 0x000080 };
        escudoPendente = null;
        aba = 0;
        carregando = true;
        listaEquipas.SelectedItem = null;
        carregando = false;
        MostrarDireita();
    }

    void Desfazer()
    {
        if (atual == null) { edit = null; escudoPendente = null; MostrarDireita(); return; }
        Abrir(atual);
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
            AplicarEscudo(antes, edit);
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
        escudoPendente = null;
        Ordenar();
        MontarFiltro(PaisDoFiltro == null ? null : item.Pasta);
        carregando = true;
        listaEquipas.SelectedItem = item;
        listaEquipas.ScrollIntoView(item);
        carregando = false;
        Abrir(item);
        return true;
    }

    async Task Apagar()
    {
        if (atual == null) { if (edit != null) Desfazer(); return; }
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
        edit = null;
        escudoPendente = null;
        MontarFiltro(null);
        MostrarDireita();
    }

    IStorageProvider? Arquivos => TopLevel.GetTopLevel(this)?.StorageProvider;

    async Task ExportarEft()
    {
        if (edit == null) return;
        var erros = regras.Validar(edit);
        if (erros.Count > 0) { Mensagem(T("O formato .EFT não aceita a equipa assim:") + "\n\n• " + string.Join("\n• ", erros)); return; }
        var nome = edit.Arquivo.Length > 0 ? edit.Arquivo : EquipaEditavel.SugerirArquivo(edit.NomeAbreviado, itens.Select(i => i.E.Arquivo));
        var sp = Arquivos;
        string? destino = null;
        if (sp is { CanSave: true })
        {
            var f = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = T("Exportar .EFT"), SuggestedFileName = nome, DefaultExtension = "EFT",
                FileTypeChoices = new[] { new FilePickerFileType(T("Equipa no formato .EFT")) { Patterns = new[] { "*.EFT", "*.eft" } } },
            });
            if (f == null) return;
            destino = f.TryGetLocalPath();
        }
        // sem seletor (celular): na pasta de dados, em export/EQUIPAS
        destino ??= Path.Combine(raiz, "export", "EQUIPAS", nome);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            File.WriteAllBytes(destino, edit.ParaEft());
            Mensagem(T("Equipa exportada no formato .EFT:") + "\n" + destino + "\n\n" + T("Para a usar no jogo Retrô, copie o arquivo para a pasta EQUIPAS dele."));
        }
        catch (Exception x) { Mensagem(T("Erro ao gravar:") + "\n" + x.Message); }
    }

    // ---------------------------------------------------------------- patches
    void ExportarPatch()
    {
        var p = new StackPanel { Spacing = 8 };
        var paisF = PaisDoFiltro;
        var opcoes = new List<(string Texto, string Nome, Func<List<string>> Jsons)>();
        var sel = atual;
        if (sel != null) opcoes.Add((T("Só esta equipa: {0}", sel.E.NomeCompleto), sel.E.NomeCompleto, () => new List<string> { sel.Caminho }));
        if (paisF != null)
        {
            int n = itens.Count(i => i.Pasta == paisF);
            opcoes.Add((T("As equipas de {0} ({1})", NomePais(paisF), n), T("Equipas de {0}", NomePais(paisF)), () => itens.Where(i => i.Pasta == paisF).Select(i => i.Caminho).ToList()));
        }
        opcoes.Add((T("Todas as equipas ({0})", itens.Count), T("Minhas equipas"), () => itens.Select(i => i.Caminho).ToList()));
        int escolha = 0;
        var nomeP = tema.Entrada(opcoes[0].Nome, 60);
        var radios = new StackPanel { Spacing = 4 };
        for (int i = 0; i < opcoes.Count; i++)
        {
            int k = i;
            var r = new RadioButton { Content = opcoes[i].Texto, IsChecked = i == 0, GroupName = "ambito", FontFamily = tema.Fonte, FontSize = 12.5, Foreground = tema.Texto };
            r.IsCheckedChanged += (_, _) =>
            {
                if (r.IsChecked != true) return;
                // o nome sugerido segue a escolha (se o jogador nao escreveu outro)
                if ((nomeP.Text ?? "") == opcoes[escolha].Nome) nomeP.Text = opcoes[k].Nome;
                escolha = k;
            };
            radios.Children.Add(r);
        }

        var comEft = new CheckBox { Content = T("Incluir também os .EFT (formato Retrô)"), IsChecked = true, FontFamily = tema.Fonte, FontSize = 12.5, Foreground = tema.Texto };
        var ligas = new CheckBox { Content = T("Incluir os logos das ligas desses países"), IsChecked = true, FontFamily = tema.Fonte, FontSize = 12.5, Foreground = tema.Texto };
        p.Children.Add(tema.T(T("Um patch (.zip) com as equipas em JSON e os logos delas, para aplicar noutro RetroFoot (ferramenta Patches ou Importar)."), 12, tema.Fraco));
        p.Children.Add(radios);
        var ln = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*") };
        ln.Children.Add(tema.Linha1(T("Nome do patch"), 12, tema.Rotulo, true));
        Grid.SetColumn(nomeP, 1);
        ln.Children.Add(nomeP);
        p.Children.Add(ln);
        p.Children.Add(comEft);
        p.Children.Add(ligas);
        Action fechar = () => sobre.Children.Clear();
        async Task Fazer(bool escolherLugar)
        {
            var jsons = opcoes[escolha].Jsons();
            if (jsons.Count == 0) return;
            var nome = (nomeP.Text ?? "").Trim();
            if (nome.Length == 0) nome = T("Minhas equipas");
            var arq = EquipaJson.Slug(nome) + ".zip";
            string? destino = Path.Combine(raiz, PastaDados.Patches, arq);
            if (escolherLugar && Arquivos is { CanSave: true } sp)
            {
                var f = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = T("Exportar patch"), SuggestedFileName = arq, DefaultExtension = "zip",
                    FileTypeChoices = new[] { new FilePickerFileType(T("Patch do RetroFoot")) { Patterns = new[] { "*.zip" } } },
                });
                if (f == null) return;
                destino = f.TryGetLocalPath() ?? destino;
            }
            try
            {
                var r = PatchEquipas.Exportar(raiz, jsons, destino, nome, comEft.IsChecked == true, ligas.IsChecked == true);
                fechar();
                Mensagem(T("Patch gravado: {0} equipas, {1} logos.", r.Equipas, r.Logos) + "\n" + destino);
            }
            catch (Exception x) { Mensagem(T("Erro ao gravar:") + "\n" + x.Message); }
        }
        var bts = new List<Control> { tema.Botao(T("Gravar em patches/"), () => _ = Fazer(false), true, "disquete") };
        if (Arquivos?.CanSave == true) bts.Add(tema.Botao(T("Gravar como…"), () => _ = Fazer(true)));
        bts.Add(tema.Botao(T("Cancelar"), fechar));
        p.Children.Add(tema.Botoes(bts.ToArray()));
        AbrirModal(T("Exportar patch de equipas"), p, 500);
    }

    void ImportarPatch()
    {
        var p = new StackPanel { Spacing = 8 };
        p.Children.Add(tema.T(T("Aplica um patch na pasta de dados: as equipas (JSON ou .EFT) e os logos dele. Uma equipa com o mesmo arquivo .EFT é substituída."), 12, tema.Fraco));
        var lista = tema.Lista0();
        Selecao(lista);
        var pasta = Path.Combine(raiz, PastaDados.Patches);
        var zips = Directory.Exists(pasta) ? Directory.GetFiles(pasta, "*.zip").OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList() : new List<string>();
        lista.ItemsSource = zips;
        lista.ItemTemplate = new FuncDataTemplate<string>((z, _) =>
        {
            if (z == null) return new Panel();
            var s = new StackPanel();
            using var man = PatchEquipas.LerManifesto(z);
            var nome = man?.RootElement.TryGetProperty("name", out var n) == true ? n.GetString() : null;
            s.Children.Add(new TextBlock { Text = nome ?? Path.GetFileNameWithoutExtension(z), FontFamily = tema.Fonte, FontSize = 12.5, FontWeight = FontWeight.Bold });
            s.Children.Add(new TextBlock { Text = Path.GetFileName(z) + (man == null ? "" : " · " + T("{0} equipas", man.RootElement.GetProperty("teams").GetInt32())), FontFamily = tema.Fonte, FontSize = 10.5, Opacity = 0.72 });
            return s;
        });
        p.Children.Add(tema.T(T("Biblioteca (pasta patches/):"), 12, tema.Rotulo, true));
        p.Children.Add(new Border { Height = 150, Child = tema.Funda(zips.Count > 0 ? lista : tema.T("  " + T("(vazia)"), 12, tema.Fraco)) });
        Action fechar = () => sobre.Children.Clear();
        void Aplicar(string zip)
        {
            try
            {
                var r = PastaDados.Importar(zip, raiz);
                fechar();
                Escudos.Recarregar();
                atual = null; edit = null; escudoPendente = null;
                Carregar();
                MontarFiltro(r.PorPais.Count == 1 ? r.PorPais.Keys.First() : null);
                MostrarDireita();
                var txt = T("{0} equipas importadas", r.Equipas) + (r.PorPais.Count > 0 ? " (" + string.Join(", ", r.PorPais.Select(x => $"{x.Key} {x.Value}")) + ")" : "")
                    + (r.Copiados.TryGetValue(PastaDados.Logos, out var nl) ? "\n" + T("{0} logos", nl) : "")
                    + (r.Outros.Count > 0 ? "\n" + string.Join("\n", r.Outros) : "")
                    + (r.Erros.Count > 0 ? "\n\n" + T("Erros:") + "\n" + string.Join("\n", r.Erros.Take(8)) : "");
                Mensagem(txt);
            }
            catch (Exception x) { Mensagem(T("Erro ao importar:") + "\n" + x.Message); }
        }
        lista.DoubleTapped += (_, _) => { if (lista.SelectedItem is string z) Aplicar(z); };
        var bts = new List<Control> { tema.Botao(T("Aplicar"), () => { if (lista.SelectedItem is string z) Aplicar(z); }, true) };
        if (Arquivos?.CanOpen == true)
            bts.Add(tema.Botao(T("Escolher arquivo…"), async () =>
            {
                var fs = await Arquivos!.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = T("Importar patch"), AllowMultiple = false,
                    FileTypeFilter = new[] { new FilePickerFileType(T("Patch (.zip) ou equipa (.EFT, .json)")) { Patterns = new[] { "*.zip", "*.EFT", "*.eft", "*.json" } } },
                });
                if (fs.Count > 0 && fs[0].TryGetLocalPath() is { } c) Aplicar(c);
            }));
        bts.Add(tema.Botao(T("Cancelar"), fechar));
        p.Children.Add(tema.Botoes(bts.ToArray()));
        AbrirModal(T("Importar patch de equipas"), p, 480);
    }

    // ---------------------------------------------------------------- janelinhas
    void AbrirModal(string titulo, Control corpo, double largura)
    {
        sobre.Children.Clear();
        sobre.Children.Add(TemaEditor.Modal(titulo, tema.Corpo(corpo), () => sobre.Children.Clear(), "escudo", largura));
    }

    void Mensagem(string texto)
    {
        sobre.Children.Clear();
        sobre.Children.Add(Ui.Caixa(T("Editor de Equipas"), texto, () => sobre.Children.Clear()));
    }

    TaskCompletionSource<bool>? pergunta;

    Task<bool> Confirmar(string texto)
    {
        var tcs = pergunta = new TaskCompletionSource<bool>();
        var p = new StackPanel { Spacing = 6 };
        p.Children.Add(tema.T(texto, 13));
        void Fim(bool r) { sobre.Children.Clear(); tcs.TrySetResult(r); }
        var sim = tema.Botao(T("Sim"), () => Fim(true), true);
        p.Children.Add(tema.Botoes(sim, tema.Botao(T("Não"), () => Fim(false))));
        sobre.Children.Clear();
        sobre.Children.Add(TemaEditor.Modal(T("Editor de Equipas"), tema.Corpo(p), () => Fim(false), "info", 400));
        Dispatcher.UIThread.Post(() => sim.Focus(), DispatcherPriority.Loaded);
        return tcs.Task;
    }
}
