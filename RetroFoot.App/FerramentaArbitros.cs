using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using RetroFoot.Motor.Arquivos;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// Arbitros: a lista de arbitros dos jogos novos (referees.tsv da pasta de dados; o pacote do
// jogo traz a de Assets/Dados, que junta a lista FIFA de Assets/arbitros.tsv com os do original
// dos paises que ela nao tem). Acrescentar, mudar nome e pais, desativar (fica comentado no
// arquivo, como os do original no fim da lista do pacote), remover e repor a lista do jogo.
// Grava sozinho (o anterior fica em referees.tsv.bak). Veio do editor de arbitros do launcher
// (launcher/src-cross/RefereeEditorWindow.cs), que mexia no REFEREE.TXE.
static class FerramentaArbitros
{
    [ModuleInitializer]
    internal static void Registar() =>
        Ferramentas.Registrar(Ferramentas.Arbitros, "Árbitros", "Edite a lista de árbitros", "apito", ctx => new TelaArbitros(ctx));
}

sealed class TelaArbitros : UserControl
{
    sealed class Item
    {
        public string Pais = "", Nome = "", Origem = "";
        public bool Ativo = true;
    }

    const string Pacote = "avares://RetroFoot.App/Assets/Dados/" + PastaDados.Arbitros;

    readonly Ferramentas.Contexto ctx;
    readonly EstiloFerramenta est = new();
    readonly bool largo;
    readonly string arquivo;
    readonly List<Item> itens = new();
    readonly List<TextosDados.Pais> paises = new();
    readonly Dictionary<string, string> nomePais = new(StringComparer.OrdinalIgnoreCase);
    List<string?> filtroPaises = new() { null };
    bool montando, sujo;
    Item? atual;
    readonly DispatcherTimer gravarDepois;

    readonly TextBox tBusca, tNome;
    readonly ComboBox cFiltroPais, cMostrar, cPais;
    readonly CheckBox xAtivo;
    readonly ListBox lista;
    readonly TextBlock conta, aviso, estado;
    readonly Control editor;
    readonly Panel camada = new();

    public TelaArbitros(Ferramentas.Contexto ctx)
    {
        this.ctx = ctx;
        largo = ctx.Largo;
        Focusable = true;
        arquivo = Path.Combine(ctx.RaizDados, PastaDados.Arbitros);
        gravarDepois = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        gravarDepois.Tick += (_, _) => { gravarDepois.Stop(); Gravar(); };

        try { paises.AddRange(TextosDados.LerPaises(Path.Combine(ctx.RaizDados, PastaDados.Paises))); } catch { }
        foreach (var p in paises) nomePais[p.Codigo] = p.Nome;

        tBusca = est.Caixa();
        tBusca.PlaceholderText = T("Procurar nome");
        cFiltroPais = est.Combo();
        cMostrar = est.Combo();
        cMostrar.ItemsSource = new[] { T("Todos"), T("Só os ativos"), T("Só os desativados") };
        cMostrar.SelectedIndex = 0;
        cPais = est.Combo();
        cPais.ItemsSource = paises.Select(p => p.Codigo + " - " + p.Nome).ToList();
        tNome = est.Caixa();
        tNome.MaxLength = 60;
        xAtivo = est.Marcar(T("Ativo (usado nos jogos novos)"));
        conta = est.Txt("", 11.5, est.Fraco);
        aviso = est.Txt("", 11.5, est.Mau);
        estado = est.Txt("", 11, est.Fraco);

        lista = est.Lista();
        lista.ItemTemplate = new FuncDataTemplate<Item>((it, _) => it == null ? new Panel() : Linha(it));
        lista.SelectionChanged += (_, _) => { if (!montando) Selecionar(lista.SelectedItem as Item); };
        lista.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete) { e.Handled = true; Remover(); }
            else if (e.Key == Key.Enter) { e.Handled = true; tNome.Focus(); }
        };

        tBusca.TextChanged += (_, _) => Filtrar();
        cFiltroPais.SelectionChanged += (_, _) => Filtrar();
        cMostrar.SelectionChanged += (_, _) => Filtrar();
        tNome.TextChanged += (_, _) => { if (!montando && atual != null && atual.Nome != (tNome.Text ?? "")) { atual.Nome = tNome.Text ?? ""; Mudou(); } };
        cPais.SelectionChanged += (_, _) =>
        {
            if (montando || atual == null || cPais.SelectedIndex < 0) return;
            if (atual.Pais == paises[cPais.SelectedIndex].Codigo) return;
            atual.Pais = paises[cPais.SelectedIndex].Codigo;
            Mudou(true);
        };
        xAtivo.IsCheckedChanged += (_, _) => { if (!montando && atual != null && atual.Ativo != (xAtivo.IsChecked == true)) { atual.Ativo = xAtivo.IsChecked == true; Mudou(true); } };

        editor = MontarEditor();
        var raiz = new Grid();
        raiz.Children.Add(MontarCorpo());
        raiz.Children.Add(camada);
        Content = est.Raiz(raiz);

        Ler();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && camada.Children.Count > 0) { camada.Children.Clear(); lista.Focus(); e.Handled = true; }
        };
        // ao sair (Voltar) o que faltava gravar vai para o arquivo
        DetachedFromVisualTree += (_, _) => { if (sujo) { gravarDepois.Stop(); Gravar(); } };
    }

    string NomePais(string cod) => nomePais.TryGetValue(cod, out var n) ? n : TextosMercado.NomePais(cod);

    static string Origem(string o) => o.ToLowerInvariant() switch
    {
        "fifa" => "FIFA",
        "liga" => T("Liga"),
        "1998" => T("Retrô (1998)"),
        "txe" => T("Patch"),
        "user" or "" => T("Seu"),
        _ => o,
    };

    // ---------------------------------------------------------------- montagem
    Control MontarCorpo()
    {
        var barra = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto"), ColumnSpacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        barra.Children.Add(tBusca);
        Grid.SetColumn(cFiltroPais, 1);
        barra.Children.Add(cFiltroPais);
        cMostrar.Width = largo ? 170 : 150;
        Grid.SetColumn(cMostrar, 2);
        barra.Children.Add(cMostrar);

        var cab = new Grid { ColumnDefinitions = new ColumnDefinitions("46,64,*,96") };
        foreach (var (t, c) in new[] { (T("Ativo"), 0), (T("País"), 1), (T("Nome"), 2), (T("Origem"), 3) })
        {
            var tb = new TextBlock
            {
                Text = t, FontFamily = est.Fonte, FontSize = 11.5, FontWeight = FontWeight.Bold, Foreground = est.CabTexto, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = c == 0 ? HorizontalAlignment.Center : HorizontalAlignment.Left, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var b = new Border { Background = est.CabFundo, Padding = new Thickness(4, 4), Child = tb };
            Grid.SetColumn(b, c);
            cab.Children.Add(b);
        }
        var dp = new DockPanel();
        var cabB = new Border { Background = est.CabFundo, Child = cab };
        DockPanel.SetDock(cabB, Dock.Top);
        dp.Children.Add(cabB);
        dp.Children.Add(lista);
        Ui.AlinharCabecalho(cab, lista);
        var tabela = est.Moldura(dp);

        var corpo = new Grid { ColumnDefinitions = new ColumnDefinitions(largo ? "*,320" : "*,258"), ColumnSpacing = 8 };
        corpo.Children.Add(tabela);
        Grid.SetColumn(editor, 1);
        corpo.Children.Add(editor);

        var d = new DockPanel();
        DockPanel.SetDock(barra, Dock.Top);
        d.Children.Add(barra);
        conta.TextWrapping = TextWrapping.NoWrap;
        conta.TextTrimming = TextTrimming.CharacterEllipsis;
        var rod = new Border { Margin = new Thickness(2, 6, 0, 0), Child = conta };
        DockPanel.SetDock(rod, Dock.Bottom);
        d.Children.Add(rod);
        d.Children.Add(corpo);
        return d;
    }

    Control MontarEditor()
    {
        var p = new StackPanel { Spacing = 8 };
        p.Children.Add(est.Txt(T("Árbitro"), 14, Tema.CorTitulo, true));
        p.Children.Add(est.Campo(T("País"), cPais));
        p.Children.Add(est.Campo(T("Nome"), tNome));
        p.Children.Add(aviso);
        p.Children.Add(xAtivo);
        var botoes = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
        var novo = Ui.Botao(T("Novo"), Novo, false);
        novo.HorizontalAlignment = HorizontalAlignment.Stretch;
        botoes.Children.Add(novo);
        var rem = Ui.Botao(T("Remover"), Remover, false);
        rem.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetColumn(rem, 1);
        botoes.Children.Add(rem);
        p.Children.Add(botoes);
        p.Children.Add(new Border { Height = 1, Background = est.PainelBorda, Margin = new Thickness(0, 2) });
        p.Children.Add(est.Txt(T("Os jogos novos usam os árbitros ativos. Se escolher só um país no jogo novo, ficam só os desse país."), 11.5, est.Fraco));
        var repor = Ui.Botao(T("Repor a lista do jogo"), PerguntarRepor, false, "relogio");
        repor.HorizontalAlignment = HorizontalAlignment.Stretch;
        p.Children.Add(repor);
        if (DadosJogo.PodeAbrirPasta)
        {
            var abrir = Ui.Botao(T("Abrir pasta de dados"), () => DadosJogo.AbrirPasta(ctx.RaizDados), false, "pasta");
            abrir.HorizontalAlignment = HorizontalAlignment.Stretch;
            p.Children.Add(abrir);
        }
        p.Children.Add(estado);
        return est.Cartao(new ScrollViewer { Content = p, Padding = new Thickness(0, 0, 4, 0) }, new Thickness(10));
    }

    Control Linha(Item it)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("46,64,*,96") };
        var marca = new Clicavel
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Background = it.Ativo ? Tema.Azul : Tema.FundoHover,
            BorderBrush = Tema.Linha, BorderThickness = new Thickness(1),
            Child = it.Ativo ? Ui.Icone("visto", 12, Tema.TextoEscuro) : null,
        };
        ToolTip.SetTip(marca, it.Ativo ? T("Ativo: clique para desativar") : T("Desativado: clique para ativar"));
        marca.Clique += () => { it.Ativo = !it.Ativo; if (it == atual) { montando = true; xAtivo.IsChecked = it.Ativo; montando = false; } Mudou(true); };
        g.Children.Add(marca);
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) };
        sp.Children.Add(est.ImagemBandeira(it.Pais, 12));
        sp.Children.Add(new TextBlock { Text = it.Pais, FontFamily = est.Fonte, FontSize = 12, Foreground = est.TabelaTexto });
        ToolTip.SetTip(sp, NomePais(it.Pais));
        Grid.SetColumn(sp, 1);
        g.Children.Add(sp);
        var n = new TextBlock
        {
            Text = it.Nome.Length > 0 ? it.Nome : T("(sem nome)"), FontFamily = est.Fonte, FontSize = 12.5, Foreground = it.Ativo ? est.TabelaTexto : est.Fraco,
            FontStyle = it.Ativo ? FontStyle.Normal : FontStyle.Italic, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0),
        };
        Grid.SetColumn(n, 2);
        g.Children.Add(n);
        var o = new TextBlock { Text = Origem(it.Origem), FontFamily = est.Fonte, FontSize = 11.5, Foreground = est.Fraco, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(o, 3);
        g.Children.Add(o);
        return new Border { Padding = new Thickness(0, 3), Child = g };
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
        catch (Exception ex) { estado.Text = T("Não consegui ler {0}: {1}", PastaDados.Arbitros, ex.Message); }
        sujo = false;
        MontarFiltroPaises();
        // o 1o ja escolhido: o editor ao lado nao fica vazio
        Filtrar(itens.FirstOrDefault());
        estado.Text = File.Exists(arquivo) ? T("Arquivo: {0}", arquivo) : T("Ainda sem lista: a do jogo entra ao repor.");
    }

    void MontarFiltroPaises()
    {
        var antes = cFiltroPais.SelectedIndex > 0 && cFiltroPais.SelectedIndex < filtroPaises.Count ? filtroPaises[cFiltroPais.SelectedIndex] : null;
        var grupos = itens.GroupBy(i => i.Pais).OrderBy(g => NomePais(g.Key), StringComparer.CurrentCultureIgnoreCase).ToList();
        filtroPaises = new List<string?> { null };
        filtroPaises.AddRange(grupos.Select(g => (string?)g.Key));
        montando = true;
        cFiltroPais.ItemsSource = new[] { T("Todos os países ({0})", grupos.Count) }
            .Concat(grupos.Select(g => $"{NomePais(g.Key)} ({g.Count(i => i.Ativo)}/{g.Count()})")).ToList();
        int k = antes != null ? filtroPaises.IndexOf(antes) : 0;
        cFiltroPais.SelectedIndex = k < 0 ? 0 : k;
        montando = false;
    }

    static string SemAcento(string s) => new string(s.Normalize(NormalizationForm.FormD)
        .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());

    List<Item> Visiveis()
    {
        var termo = SemAcento((tBusca.Text ?? "").Trim());
        var pais = cFiltroPais.SelectedIndex > 0 && cFiltroPais.SelectedIndex < filtroPaises.Count ? filtroPaises[cFiltroPais.SelectedIndex] : null;
        int mostrar = cMostrar.SelectedIndex;
        return itens.Where(i =>
            (termo.Length == 0 || SemAcento(i.Nome).Contains(termo, StringComparison.OrdinalIgnoreCase)) &&
            (pais == null || i.Pais == pais) &&
            (mostrar != 1 || i.Ativo) && (mostrar != 2 || !i.Ativo)).ToList();
    }

    void Filtrar(Item? manter = null)
    {
        if (montando) return;
        manter ??= atual;
        var v = Visiveis();
        montando = true;
        lista.ItemsSource = v;
        if (manter != null && v.Contains(manter)) lista.SelectedItem = manter;
        montando = false;
        Selecionar(manter != null && v.Contains(manter) ? manter : null);
        if (atual != null) lista.ScrollIntoView(atual);
        Contar();
    }

    void Contar()
    {
        int ativos = itens.Count(i => i.Ativo);
        int np = itens.Where(i => i.Ativo).Select(i => i.Pais).Distinct().Count();
        conta.Text = T("{0} árbitros ativos em {1} países · {2} desativados · Delete remove · clique na marca para ativar ou desativar", ativos, np, itens.Count - ativos);
    }

    void Selecionar(Item? it)
    {
        atual = it;
        montando = true;
        editor.IsEnabled = true;
        tNome.Text = it?.Nome ?? "";
        tNome.IsEnabled = cPais.IsEnabled = xAtivo.IsEnabled = it != null;
        int k = it == null ? -1 : paises.FindIndex(p => p.Codigo == it.Pais);
        cPais.SelectedIndex = k;
        xAtivo.IsChecked = it?.Ativo ?? false;
        montando = false;
        Avisar();
    }

    void Avisar()
    {
        var s = atual?.Nome ?? "";
        var avisos = new List<string>();
        if (atual != null && s.Trim().Length == 0) avisos.Add(T("Sem nome: não é gravado."));
        if (s.Any(c => c > 255)) avisos.Add(T("Letras fora do Latin-1 perdem o acento no jogo."));
        if (s.Length > 40) avisos.Add(T("O jogo usa só as primeiras 40 letras."));
        if (atual != null && atual.Pais.Length > 0 && !nomePais.ContainsKey(atual.Pais)) avisos.Add(T("O país {0} não está em countries.tsv.", atual.Pais));
        aviso.Text = string.Join("\n", avisos);
        aviso.IsVisible = avisos.Count > 0;
    }

    // uma mudanca no arbitro escolhido: refaz a linha e grava daqui a pouco
    void Mudou(bool refazer = false)
    {
        sujo = true;
        Avisar();
        if (refazer) { MontarFiltroPaises(); Filtrar(atual); }
        else if (atual != null)
        {
            // so o nome mudou: a lista se redesenha sem perder o foco da caixa
            if (lista.ContainerFromItem(atual) is ListBoxItem c) { c.Content = null; c.Content = atual; }
        }
        Contar();
        estado.Text = T("Por gravar...");
        gravarDepois.Stop();
        gravarDepois.Start();
    }

    void Gravar()
    {
        try
        {
            TextosDados.GravarArbitrosEditaveis(arquivo, itens.Where(i => i.Nome.Trim().Length > 0 && i.Pais.Trim().Length > 0)
                .Select(i => new TextosDados.ArbitroEditavel(i.Pais.Trim().ToUpperInvariant(), i.Nome.Trim(), i.Origem, i.Ativo)));
            sujo = false;
            estado.Text = T("Gravado em {0} (o anterior fica em .bak).", PastaDados.Arbitros);
        }
        catch (Exception ex) { estado.Text = T("Não consegui gravar: {0}", ex.Message); }
    }

    // ---------------------------------------------------------------- acoes
    void Novo()
    {
        var pais = cFiltroPais.SelectedIndex > 0 && cFiltroPais.SelectedIndex < filtroPaises.Count ? filtroPaises[cFiltroPais.SelectedIndex]
            : atual?.Pais ?? paises.FirstOrDefault()?.Codigo ?? "POR";
        var it = new Item { Pais = pais!, Nome = "", Origem = "user", Ativo = true };
        int k = atual != null ? itens.IndexOf(atual) + 1 : itens.Count;
        itens.Insert(Math.Clamp(k, 0, itens.Count), it);
        montando = true;
        tBusca.Text = "";
        if (cMostrar.SelectedIndex == 2) cMostrar.SelectedIndex = 0;
        montando = false;
        MontarFiltroPaises();
        Filtrar(it);
        Contar();
        Dispatcher.UIThread.Post(() => tNome.Focus(), DispatcherPriority.Loaded);
    }

    void Remover()
    {
        if (atual == null) return;
        var v = lista.ItemsSource as List<Item>;
        int k = v?.IndexOf(atual) ?? -1;
        itens.Remove(atual);
        Item? prox = v != null && k >= 0 && k + 1 < v.Count ? v[k + 1] : v != null && k > 0 ? v[k - 1] : null;
        atual = null;
        MontarFiltroPaises();
        Filtrar(prox);
        sujo = true;
        estado.Text = T("Por gravar...");
        gravarDepois.Stop();
        gravarDepois.Start();
        lista.Focus();
    }

    void PerguntarRepor()
    {
        camada.Children.Clear();
        var corpo = new StackPanel { Spacing = 14, Margin = new Thickness(18, 14), Width = 400 };
        corpo.Children.Add(Ui.Txt(T("Repor a lista de árbitros que vem com o jogo? A sua lista atual fica guardada em {0}.bak.", PastaDados.Arbitros), 13.5));
        var bs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var sim = Ui.Botao(T("Repor"), () => { camada.Children.Clear(); Repor(); }, true);
        var nao = Ui.Botao(T("Cancelar"), () => { camada.Children.Clear(); lista.Focus(); });
        bs.Children.Add(sim);
        bs.Children.Add(nao);
        corpo.Children.Add(bs);
        camada.Children.Add(Ui.Veu(Ui.Janela(T("Repor a lista do jogo"), corpo, () => camada.Children.Clear(), "apito")));
        Dispatcher.UIThread.Post(() => nao.Focus());
    }

    void Repor()
    {
        gravarDepois.Stop();
        try
        {
            using var s = AssetLoader.Open(new Uri(Pacote));
            using var m = new MemoryStream();
            s.CopyTo(m);
            if (File.Exists(arquivo)) File.Copy(arquivo, arquivo + ".bak", true);
            File.WriteAllBytes(arquivo, m.ToArray());
        }
        catch (Exception ex) { estado.Text = T("Não consegui repor: {0}", ex.Message); return; }
        atual = null;
        Ler();
        estado.Text = T("Lista do jogo reposta ({0} árbitros ativos).", itens.Count(i => i.Ativo));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => lista.Focus(), DispatcherPriority.Loaded);
    }
}
