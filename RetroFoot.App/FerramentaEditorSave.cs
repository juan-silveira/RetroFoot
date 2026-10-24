using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RetroFoot.Motor.Save;
using static RetroFoot.App.Idioma;
using C = RetroFoot.App.Tema;

namespace RetroFoot.App;

// O Editor de Save no hub (docs/ferramentas.md). Porte do Editor de Save do launcher do jogo classico
// (src-cross/SaveEditorWindow.cs, Juan Silveira) sobre o SaveEditavel do motor
// (Motor/Save/EditorSave.cs): esquerda, o jogo (inflacao), os treinadores humanos (troca de
// equipa), o clube (dinheiro, moral, estadio) e as contas de 16 bits do plantel; direita, o
// plantel; um clique (ou Enter) abre a ficha do jogador (forca com o maximo deste plantel,
// salario, nota, lesao, S/A/L, comportamento). Gravar faz o .bak na 1a vez e confere os bytes.
static class FerramentaEditorSave
{
    [ModuleInitializer]
    internal static void Registar() =>
        Ferramentas.Registrar(Ferramentas.EditorSave, "Editor de Save", "Altere dinheiro, jogadores e equipas de um jogo gravado",
            "disquete", ctx => new TelaEditorSave(ctx));
}

public sealed class TelaEditorSave : UserControl
{
    readonly Ferramentas.Contexto ctx;
    readonly string pasta;
    readonly Panel veu = new();
    readonly Grid corpo = new();

    List<string> saves = new();
    SaveEditavel? save;
    string caminho = "";
    EquipaEditavel? equipa;
    List<EquipaEditavel> equipas = new();
    bool alterado, montando;

    // controles que mudam com o save/clube escolhido
    readonly ComboBox selSave = new() { MinWidth = 180, MaxDropDownHeight = 360 };
    readonly ComboBox selEquipa = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 360 };
    TextBox inflacao = null!, dinheiro = null!, moral = null!;
    string inflacaoMostrada = "", moralMostrado = "";
    readonly TextBlock epoca = new(), estadio = new(), estado = new(), previaNome = new(), previaDiv = new();
    readonly Border previa = new() { CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 4) };
    readonly StackPanel treinadores = new() { Spacing = 6 }, contas = new() { Spacing = 4 }, linhas = new();
    Control cartaoTreinadores = null!;
    Control estMenos = null!, estMais = null!;
    readonly ScrollViewer rolagem = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

    public TelaEditorSave(Ferramentas.Contexto ctx)
    {
        this.ctx = ctx;
        pasta = Environment.GetEnvironmentVariable("RETROFOOT_JOGOS") is { Length: > 0 } j ? j : Path.Combine(ctx.RaizDados, Motor.Arquivos.PastaDados.Saves);
        Focusable = true;
        var raiz = new Grid();
        raiz.Children.Add(corpo);
        raiz.Children.Add(veu);
        Content = raiz;
        Montar();
        ListarSaves(null);
    }

    // Esc com alteracoes por gravar pergunta antes de voltar ao hub
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape || veu.Children.Count > 0) return;
        if (Aplicar() is null && !alterado) return;   // o hub volta
        e.Handled = true;
        Confirmar(ctx.Voltar);
    }

    // ---------------------------------------------------------------- pecas dos dois visuais
    static IBrush CorTexto => Ui.CorTexto;
    static IBrush CorFraca => Ui.CorFraca;
    static IBrush CorRotulo => Ui.CorRotulo;
    static IBrush CorErro => Ui.CorErro;
    static IBrush CorAviso => Ui.CorAviso;

    static TextBlock Txt(string s, double tam = 12.5, IBrush? cor = null, bool negrito = false) => Ui.Txt(s, tam, cor ?? CorTexto, negrito);

    TextBlock Rotulo(string s) => Txt(s, 11.5, CorRotulo, true);

    Border Cartao(string titulo, Control conteudo)
    {
        var p = new StackPanel { Spacing = 6 };
        var t = Txt(titulo, 13.5, C.CorTitulo, true);
        p.Children.Add(t);
        p.Children.Add(conteudo);
        return new Border { Background = C.FundoCartao, BorderBrush = C.Linha, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8), Child = p };
    }

    TextBox Caixa(string texto = "")
    {
        return new TextBox
        {
            Text = texto, FontFamily = C.Inter, FontSize = 13, Background = C.Campo, Foreground = C.CampoTexto, BorderBrush = C.Linha, CaretBrush = C.CampoTexto,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), MinHeight = 26, Padding = new Thickness(6, 3),
        };
    }

    Control Botao(string texto, Action acao, bool principal = false, string? icone = null)
    {
        var b = Ui.Botao(texto, acao, principal, icone);
        b.MinWidth = 0;
        b.MinHeight = 28;
        b.Padding = new Thickness(12, 3);
        return b;
    }

    void Combo(ComboBox c)
    {
        c.FontSize = 13;
        c.FontFamily = C.Inter; c.Background = C.Campo; c.Foreground = C.CampoTexto; c.BorderBrush = C.Linha;
        c.MinHeight = 28;
        c.Padding = new Thickness(6, 2);
    }

    static Control LinhaCampo(Control rotulo, Control editor, double largura = 76)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{largura},*"), ColumnSpacing = 6 };
        rotulo.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(rotulo);
        Grid.SetColumn(editor, 1);
        g.Children.Add(editor);
        return g;
    }

    internal static readonly string[] CoresPosicao = { "#D99A00", "#1E6FD0", "#2E9E44", "#D23A2F" };

    static Border Selo(int posicao, double largura = 22) => new()
    {
        Background = Ui.Hex(posicao is >= 0 and < 4 ? CoresPosicao[posicao] : "#808080"), CornerRadius = new CornerRadius(4),
        Width = largura, Padding = new Thickness(0, 1), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = posicao is >= 0 and < 4 ? Posicao(posicao) : "?", Foreground = Brushes.White, FontWeight = FontWeight.Bold,
            FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center,
        },
    };

    internal static string Num(long v) => FormatoDinheiro.Numero(v);
    internal static string Dec(double v) => v.ToString("0.#", Atual == EN ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo("pt-PT"));

    // ---------------------------------------------------------------- a pagina
    void Montar()
    {
        bool largo = ctx.Largo;
        corpo.RowDefinitions = new RowDefinitions("Auto,*,Auto");
        corpo.ColumnDefinitions = new ColumnDefinitions(largo ? "290,*" : "236,*");
        corpo.ColumnSpacing = 10;
        corpo.RowSpacing = 8;

        // topo: o jogo gravado, Recarregar e Gravar
        Combo(selSave);
        selSave.SelectionChanged += (_, _) => { if (!montando) TrocouSave(); };
        var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"), ColumnSpacing = 8 };
        var rj = Rotulo(T("Jogo gravado"));
        rj.VerticalAlignment = VerticalAlignment.Center;
        topo.Children.Add(rj);
        Grid.SetColumn(selSave, 1);
        selSave.HorizontalAlignment = HorizontalAlignment.Left;
        selSave.MinWidth = largo ? 300 : 200;
        topo.Children.Add(selSave);
        var abrir = Botao(T("Pasta"), () => { if (!DadosJogo.AbrirPasta(pasta)) ctx.Mensagem(pasta); }, false, "pasta");
        ToolTip.SetTip(abrir, pasta);
        var recarregar = Botao(T("Recarregar"), () => Confirmar(() => ListarSaves(caminho)), false, "relogio");
        var gravar = Botao(T("Gravar"), Gravar, true, "disquete");
        Grid.SetColumn(abrir, 2); Grid.SetColumn(recarregar, 3); Grid.SetColumn(gravar, 4);
        topo.Children.Add(abrir); topo.Children.Add(recarregar); topo.Children.Add(gravar);
        Grid.SetColumnSpan(topo, 2);
        corpo.Children.Add(topo);

        // esquerda: jogo, treinadores, clube, contas
        inflacao = Caixa();
        epoca.FontSize = 10.5; epoca.Foreground = CorFraca; epoca.TextWrapping = TextWrapping.Wrap;
        var jogo = new StackPanel { Spacing = 3 };
        jogo.Children.Add(LinhaCampo(Rotulo(T("Inflação")), inflacao));
        jogo.Children.Add(epoca);
        cartaoTreinadores = Cartao(T("Treinadores"), treinadores);

        Combo(selEquipa);
        selEquipa.SelectionChanged += (_, _) => { if (!montando) TrocouEquipa(); };
        previaNome.FontWeight = FontWeight.Bold; previaNome.FontSize = 13.5; previaNome.TextTrimming = TextTrimming.CharacterEllipsis;
        previaDiv.FontSize = 10.5;
        previa.Child = new StackPanel { Children = { previaNome, previaDiv } };
        dinheiro = Caixa();
        moral = Caixa();
        estadio.FontWeight = FontWeight.Bold; estadio.FontSize = 13.5; estadio.Foreground = CorTexto;
        estadio.HorizontalAlignment = HorizontalAlignment.Center; estadio.VerticalAlignment = VerticalAlignment.Center;
        estMenos = Botao("−", () => PassoEstadio(-1));
        estMais = Botao("+", () => PassoEstadio(1));
        foreach (var b in new[] { estMenos, estMais }) { b.MinWidth = 30; if (b is Border bb) bb.Padding = new Thickness(6, 2); }
        var est = new DockPanel();
        DockPanel.SetDock(estMenos, Dock.Left); DockPanel.SetDock(estMais, Dock.Right);
        est.Children.Add(estMenos); est.Children.Add(estMais); est.Children.Add(estadio);
        var clube = new StackPanel { Spacing = 5 };
        clube.Children.Add(selEquipa);
        clube.Children.Add(previa);
        clube.Children.Add(LinhaCampo(Rotulo(T("Dinheiro")), dinheiro));
        clube.Children.Add(LinhaCampo(Rotulo(T("Moral")), moral));
        clube.Children.Add(Txt(T("Moral: 0 a 20 · Estádio: 5.000 a 120.000 lugares"), 10.5, CorFraca));
        clube.Children.Add(LinhaCampo(Rotulo(T("Estádio")), est));

        var esquerda = new StackPanel { Spacing = 8 };
        esquerda.Children.Add(Cartao(T("Época"), jogo));
        esquerda.Children.Add(cartaoTreinadores);
        esquerda.Children.Add(Cartao(T("Clube"), clube));
        esquerda.Children.Add(Cartao(T("Limites de 16 bits"), contas));
        var se = new ScrollViewer { Content = esquerda, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(se, 1);
        corpo.Children.Add(se);

        // direita: o plantel
        var cab = Linha(Colunas().Select(c => c.Titulo).ToArray(), true);
        var tabela = new DockPanel();
        DockPanel.SetDock(cab, Dock.Top);
        tabela.Children.Add(cab);
        rolagem.Content = linhas;
        tabela.Children.Add(rolagem);
        Ui.AlinharCabecalho(cab, rolagem);
        Control moldura = new Border { Background = C.FundoCartao, BorderBrush = C.Linha, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = tabela };
        Grid.SetRow(moldura, 1); Grid.SetColumn(moldura, 1);
        corpo.Children.Add(moldura);

        // embaixo: a dica e o estado (gravado, por gravar, erro)
        var baixo = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        baixo.Children.Add(Txt(T("Clique (ou Enter) num jogador para editar. S = suspenso, A = amarelos, L = lesionado (jogos)."), 11, CorFraca));
        estado.FontSize = 11.5; estado.FontWeight = FontWeight.Bold; estado.TextWrapping = TextWrapping.NoWrap;
        estado.TextTrimming = TextTrimming.CharacterEllipsis; estado.MaxWidth = largo ? 460 : 300; estado.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(estado, 1);
        baixo.Children.Add(estado);
        Grid.SetRow(baixo, 2); Grid.SetColumnSpan(baixo, 2);
        corpo.Children.Add(baixo);
        Habilitar(false);
    }

    // as colunas do plantel (16:9 tem mais)
    sealed record Coluna(string Titulo, string Largura, HorizontalAlignment Alinhar, Func<JogadorEditavel, string> Valor);

    List<Coluna> Colunas()
    {
        var l = new List<Coluna>
        {
            new(T("Pos"), "32", HorizontalAlignment.Center, _ => ""),
            new(T("Nome"), "*", HorizontalAlignment.Left, j => j.Nome),
            new("✱", "16", HorizontalAlignment.Center, j => j.Estrela ? "✱" : ""),
            new(T("Força"), "58", HorizontalAlignment.Center, j => j.Forca.ToString()),
        };
        if (ctx.Largo) l.Add(new(T("Máx."), "50", HorizontalAlignment.Center, j => equipa == null ? "" : Num(LimitesSave.ForcaMaximaNoElenco(equipa.Jogadores, j, j.Nota, j.Estrela))));
        l.Add(new(T("Salário"), "72", HorizontalAlignment.Center, j => Num(j.Salario)));
        l.Add(new(T("Nota"), "36", HorizontalAlignment.Center, j => j.Nota.ToString()));
        if (ctx.Largo) l.Add(new(T("Lesão"), "42", HorizontalAlignment.Center, j => j.Lesao.ToString()));
        l.Add(new(T("Comport."), ctx.Largo ? "96" : "80", HorizontalAlignment.Center, j => Comportamento(j.Comportamento)));
        if (ctx.Largo) l.Add(new(T("Jogos"), "40", HorizontalAlignment.Center, j => j.Jogos.ToString()));
        l.Add(new(T("Sit."), "46", HorizontalAlignment.Center, Situacao));
        return l;
    }

    internal static string Comportamento(int c) => c is >= 0 and < 6 ? T(LimitesSave.Comportamentos[c]) : "?";

    internal static string Situacao(JogadorEditavel j) =>
        ((j.Suspensao > 0 ? "S" + j.Suspensao : j.AmarelosAcumulados > 0 ? "A" + j.AmarelosAcumulados : "") + (j.JogosLesionado > 0 ? " L" + j.JogosLesionado : "")).Trim();

    Grid Linha(string[] textos, bool cabecalho, IBrush? cor = null)
    {
        var cols = Colunas();
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", cols.Select(c => c.Largura))), ColumnSpacing = 5 };
        for (int i = 0; i < textos.Length; i++)
        {
            var t = new TextBlock
            {
                Text = textos[i], FontSize = cabecalho ? 11 : 12.5, FontWeight = cabecalho ? FontWeight.Bold : FontWeight.Normal,
                FontFamily = C.Inter, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = cols[i].Alinhar,
                Foreground = cabecalho ? C.CorCabecalho : cor ?? C.Texto,
            };
            Grid.SetColumn(t, i);
            g.Children.Add(t);
        }
        if (cabecalho)
        {
            g.Margin = new Thickness(0);
            var b = new Border
            {
                Background = C.FundoBarra, Padding = new Thickness(8, 4), Child = g,
            };
            var w = new Grid();
            w.Children.Add(b);
            return w;
        }
        return g;
    }

    // ---------------------------------------------------------------- saves
    void ListarSaves(string? escolher)
    {
        saves = Directory.Exists(pasta)
            ? Directory.EnumerateFiles(pasta).Where(p => string.Equals(Path.GetExtension(p), ".e98", StringComparison.OrdinalIgnoreCase)
                && new FileInfo(p).Length > 0).OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();
        montando = true;
        selSave.ItemsSource = saves.Select(p => Path.GetFileNameWithoutExtension(p).ToUpperInvariant()).ToList();
        selSave.SelectedIndex = -1;
        montando = false;
        if (saves.Count == 0)
        {
            save = null; equipa = null; alterado = false;
            MostrarTudo();
            Estado(T("Nenhum jogo gravado em {0}", pasta), CorAviso);
            return;
        }
        int i = escolher != null ? saves.IndexOf(escolher) : -1;
        selSave.SelectedIndex = i >= 0 ? i : 0;   // TrocouSave abre
    }

    int saveMostrado = -1;

    void TrocouSave()
    {
        int i = selSave.SelectedIndex;
        if (i < 0 || i >= saves.Count || i == saveMostrado && saves[i] == caminho && !alterado && save != null) return;
        if (alterado && saveMostrado >= 0 && i != saveMostrado)
        {
            int volta = saveMostrado;
            montando = true; selSave.SelectedIndex = volta; montando = false;
            Confirmar(() => { alterado = false; montando = true; selSave.SelectedIndex = i; montando = false; Abrir(i); });
            return;
        }
        Abrir(i);
    }

    void Abrir(int i)
    {
        saveMostrado = i;
        caminho = saves[i];
        try
        {
            save = SaveEditavel.Abrir(caminho);
            equipas = save.Equipas.Where(e => e.Jogadores.Count > 0).OrderBy(e => LimitesSave.SemAcento(e.NomeExibido), StringComparer.Ordinal).ToList();
            // a equipa do humano primeiro escolhida
            equipa = save.Treinadores.Where(t => t.Humano).Select(save.EquipaDoTreinador).FirstOrDefault(e => e != null) ?? equipas.FirstOrDefault();
            alterado = false;
            MostrarTudo();
            Estado(T("{0}: {1} equipas", Path.GetFileName(caminho), equipas.Count), CorFraca);
        }
        catch (Exception ex)
        {
            save = null; equipa = null;
            MostrarTudo();
            Estado(T("Não consegui ler {0}: {1}", Path.GetFileName(caminho), ex.Message), CorErro);
        }
    }

    void Habilitar(bool sim)
    {
        foreach (var c in new Control[] { inflacao, dinheiro, moral, selEquipa, estMenos, estMais }) c.IsEnabled = sim;
    }

    void MostrarTudo()
    {
        Habilitar(save != null);
        inflacaoMostrada = save != null ? Dec(save.Inflacao * 10) : "";
        inflacao.Text = inflacaoMostrada;
        epoca.Text = (save != null ? T("Época {0}", save.Ano) + " · " : "") + T("inflação de 5 a 100 (o jogo mostra x 10)");
        montando = true;
        selEquipa.ItemsSource = equipas.Select(e => e.NomeExibido).ToList();
        selEquipa.SelectedIndex = equipa != null ? equipas.IndexOf(equipa) : -1;
        montando = false;
        MostrarTreinadores();
        MostrarClube();
    }

    void Estado(string texto, IBrush cor)
    {
        estado.Text = texto;
        estado.Foreground = cor;
        ToolTip.SetTip(estado, texto);
    }

    void Mudou()
    {
        alterado = true;
        Estado(T("Alterações por gravar"), CorAviso);
    }

    // ---------------------------------------------------------------- clube
    void TrocouEquipa()
    {
        int i = selEquipa.SelectedIndex;
        if (i < 0 || i >= equipas.Count || equipas[i] == equipa) return;
        if (Aplicar() is { } erro)
        {
            montando = true; selEquipa.SelectedIndex = equipa != null ? equipas.IndexOf(equipa) : -1; montando = false;
            Mensagem(T("Valor fora do limite"), erro);
            return;
        }
        equipa = equipas[i];
        MostrarClube();
    }

    void MostrarClube()
    {
        var e = equipa;
        dinheiro.Text = e != null ? Num(e.Dinheiro) : "";
        moralMostrado = e != null ? Dec(e.Moral * 10) : "";
        moral.Text = moralMostrado;
        previaNome.Text = e?.Nome ?? "";
        previaDiv.Text = e == null ? "" : string.Join(" · ", new[] { e.Pais, e.Divisao, e.Treinador?.Nome ?? "" }.Where(s => s.Length > 0));
        if (e != null)
        {
            previa.Background = new ImmutableSolidColorBrush(Color.FromRgb((byte)(e.CorFundo >> 16), (byte)(e.CorFundo >> 8), (byte)e.CorFundo));
            previaNome.Foreground = previaDiv.Foreground = new ImmutableSolidColorBrush(Color.FromRgb((byte)(e.CorLetra >> 16), (byte)(e.CorLetra >> 8), (byte)e.CorLetra));
        }
        else previa.Background = Brushes.Transparent;
        MostrarEstadio();
        MostrarPlantel();
    }

    void MostrarEstadio()
    {
        var e = equipa;
        estadio.Text = e != null ? Num(e.Capacidade) : "—";
        estMenos.IsEnabled = e != null && e.Estadio > 1;
        estMais.IsEnabled = e != null && e.Estadio < LimitesSave.EstadioMax;
    }

    void PassoEstadio(int passo)
    {
        if (equipa == null) return;
        int n = Math.Clamp(equipa.Estadio + passo, 1, LimitesSave.EstadioMax);
        if (n == equipa.Estadio) return;
        equipa.Estadio = n;
        MostrarEstadio();
        Mudou();
    }

    // as contas de 16 bits do plantel (docs internos do launcher, "Limites de 16 bits")
    void MostrarContas()
    {
        contas.Children.Clear();
        if (equipa == null) return;
        var (pesos, forca, escal) = LimitesSave.Contas(equipa.Jogadores);
        // o nome curto numa linha (sem quebrar) e a conta inteira na dica
        void Uma(string nome, string conta, long v, long max)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            var tn = Txt(nome, 11);
            tn.TextWrapping = TextWrapping.NoWrap; tn.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTip.SetTip(g, nome + (conta != "" ? " — " + conta : ""));
            g.Children.Add(tn);
            var t = Txt($"{Num(v)} / {Num(max)}", 11, v > max ? CorErro : CorTexto, true);
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            contas.Children.Add(g);
            // a barra: fina e arredondada
            double f = Math.Clamp(v / (double)max, 0, 1);
            var cheio = new Border
            {
                Background = v > max ? CorErro : f > 0.8 ? CorAviso : C.Verde, CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var dentro = new Grid();
            dentro.Children.Add(cheio);
            dentro.SizeChanged += (_, a) => cheio.Width = a.NewSize.Width * f;
            var barra = new Border { Height = 6, Background = C.Linha, CornerRadius = new CornerRadius(3), Child = dentro, Margin = new Thickness(0, 1, 0, 5) };
            contas.Children.Add(barra);
        }
        Uma(T("Soma da força"), "", forca, LimitesSave.SomaForcaMax);
        Uma(T("Pesos de golo"), T("pos² × força × nota"), pesos, LimitesSave.SomaPesosMax);
        Uma(T("Melhor escalação"), "3G + D + M + A", escal, LimitesSave.EscalacaoMax);
    }

    // ---------------------------------------------------------------- plantel
    void MostrarPlantel(JogadorEditavel? focar = null)
    {
        linhas.Children.Clear();
        MostrarContas();
        if (equipa == null) return;
        var cols = Colunas();
        int i = 0;
        Control? foco = null;
        foreach (var j in equipa.OrdemDoJogo())
        {
            var sit = Situacao(j);
            var g = Linha(cols.Select(c => c.Valor(j)).ToArray(), false);
            g.Children.Insert(0, Selo(j.Posicao));
            if (j.Estrela) ((TextBlock)g.Children[3]).Foreground = C.Ouro;
            if (j.Forca > LimitesSave.ForcaNormal) { var f = (TextBlock)g.Children[4]; f.Foreground = C.Amarelo; f.FontWeight = FontWeight.Bold; }
            if (sit.Length > 0) { var s = (TextBlock)g.Children[^1]; s.Foreground = C.Erro; s.FontWeight = FontWeight.Bold; }
            IBrush fundo = i % 2 == 0 ? C.FundoCartao : C.FundoZebra;
            // (a borda do foco ocupa 1 ponto do Padding: as colunas ficam onde as do cabecalho)
            var linha = new Clicavel
            {
                Background = fundo, Padding = new Thickness(7, 3), Child = g, Hover = C.FundoHover,
                BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, CorFoco = C.Azul,
            };
            var jj = j;
            linha.Clique += () => Ficha(jj);
            linha.KeyDown += (_, e) => MoverFoco(linha, e);
            Avalonia.Automation.AutomationProperties.SetName(linha, j.Nome);
            linhas.Children.Add(linha);
            if (j == focar) foco = linha;
            i++;
        }
        if (foco != null) Dispatcher.UIThread.Post(() => { foco.Focus(); foco.BringIntoView(); }, DispatcherPriority.Loaded);
    }

    void MoverFoco(Control linha, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down)) return;
        int i = linhas.Children.IndexOf(linha) + (e.Key == Key.Down ? 1 : -1);
        if (i >= 0 && i < linhas.Children.Count) { linhas.Children[i].Focus(NavigationMethod.Directional); linhas.Children[i].BringIntoView(); }
        e.Handled = true;
    }

    // ---------------------------------------------------------------- aplicar e gravar
    internal static bool LerInteiro(string? s, long min, long max, out long v)
    {
        var t = new string((s ?? "").Where(c => !char.IsWhiteSpace(c) && c is not ('.' or ',')).ToArray());
        return long.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v) && v >= min && v <= max;
    }

    internal static bool LerReal(string? s, double min, double max, out double v) =>
        double.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= min && v <= max;

    internal static string Faixa(string nome, string min, string max) => T("{0}: use um valor de {1} a {2}.", nome, min, max);

    // passa os campos do jogo e do clube para o save em memoria; o erro, ou null
    string? Aplicar()
    {
        if (save == null) return null;
        if ((inflacao.Text ?? "").Trim() != inflacaoMostrada)
        {
            if (!LerReal(inflacao.Text, LimitesSave.InflacaoMin * 10, LimitesSave.InflacaoMax * 10, out var inf))
                return Faixa(T("Inflação"), "5", "100");
            save.Inflacao = inf / 10;
            inflacaoMostrada = (inflacao.Text ?? "").Trim();
            alterado = true;
        }
        if (equipa == null) return null;
        if (!LerInteiro(dinheiro.Text, LimitesSave.DinheiroMin, LimitesSave.DinheiroMax, out var din))
            return Faixa(T("Dinheiro"), Num(LimitesSave.DinheiroMin), Num(LimitesSave.DinheiroMax));
        if ((moral.Text ?? "").Trim() != moralMostrado)
        {
            if (!LerReal(moral.Text, 0, LimitesSave.MoralMax * 10, out var m)) return Faixa(T("Moral"), "0", "20");
            equipa.Moral = m / 10;
            moralMostrado = (moral.Text ?? "").Trim();
            alterado = true;
        }
        if (din != equipa.Dinheiro) { equipa.Dinheiro = din; alterado = true; }
        return null;
    }

    void Gravar()
    {
        if (save == null) return;
        if (Aplicar() is { } erro) { Mensagem(T("Valor fora do limite"), erro); return; }
        try
        {
            var bak = save.GravarEm(caminho);
            alterado = false;
            Estado(T("Gravado. O original fica em {0}", Path.GetFileName(bak)), Ui.CorBom);
        }
        catch (Exception ex) { Mensagem(T("Erro"), T("Não consegui gravar:\n{0}", ex.Message)); }
    }

    // ---------------------------------------------------------------- treinadores
    void MostrarTreinadores()
    {
        treinadores.Children.Clear();
        if (save != null)
            foreach (var tec in save.Treinadores.Where(t => t.Humano))
            {
                var eq = save.EquipaDoTreinador(tec);
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
                var p = new StackPanel();
                p.Children.Add(Txt(tec.Nome, 12.5, CorTexto, true));
                p.Children.Add(Txt(eq?.NomeExibido ?? T("sem equipa"), 11, CorFraca));
                g.Children.Add(p);
                if (eq != null)
                {
                    var b = Botao(T("Trocar"), () => TrocarEquipa(tec));
                    b.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(b, 1);
                    g.Children.Add(b);
                }
                treinadores.Children.Add(g);
            }
        cartaoTreinadores.IsVisible = treinadores.Children.Count > 0;
    }

    void TrocarEquipa(TreinadorEditavel tec)
    {
        if (save == null) return;
        if (Aplicar() is { } erro) { Mensagem(T("Valor fora do limite"), erro); return; }
        var origem = save.EquipaDoTreinador(tec);
        if (origem == null) return;
        var destinos = equipas.Where(e => e != origem && e.PodeTerHumano).ToList();
        if (destinos.Count == 0) { Mensagem(T("Trocar de equipa"), T("Este jogo não tem divisões: não dá para saber que equipas podem ter um treinador humano.")); return; }
        var lista = new ListBox
        {
            ItemsSource = destinos.Select(e => $"{e.NomeExibido}  ({e.Divisao})").ToList(), SelectedIndex = 0, Height = 220,
            FontFamily = C.Inter, FontSize = 12.5, Background = C.Fundo, Foreground = C.Texto,
        };
        var efeito = Txt("", 11.5, CorFraca);
        void Atualiza()
        {
            var d = destinos[Math.Max(0, lista.SelectedIndex)];
            efeito.Text = (d.Treinador is { } o ? T("{0} vai para {1}.", o.Nome, origem.NomeExibido) + " " : "")
                + T("Na história do treinador fica \"Promovido para o {0}\", como num convite aceito no jogo.", d.NomeExibido) + " "
                + T("As duas equipas ficam com moral 10, como numa chicotada psicológica. Só aparecem equipas das divisões (no Distrital o jogo quebra).");
        }
        lista.SelectionChanged += (_, _) => Atualiza();
        Atualiza();
        var p = new StackPanel { Spacing = 8 };
        p.Children.Add(Txt(T("Equipa atual: {0}", origem.NomeExibido), 12.5, CorTexto, true));
        p.Children.Add(Ui.Afundado(lista));
        p.Children.Add(efeito);
        Janelinha(T("Trocar de equipa") + " - " + tec.Nome, p, T("Trocar"), () =>
        {
            var d = destinos[Math.Max(0, lista.SelectedIndex)];
            save.TrocarEquipa(tec, d);
            equipa = d;
            MostrarTudo();
            Mudou();
            return true;
        }, 440);
    }

    // ---------------------------------------------------------------- ficha do jogador
    void Ficha(JogadorEditavel j)
    {
        if (equipa == null) return;
        var eq = equipa;
        var grade = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 12, RowSpacing = 8 };
        int celula = 0;
        TextBlock Celula(string titulo, Control campo, int colunas = 1)
        {
            var c = new StackPanel { Spacing = 2 };
            c.Children.Add(Rotulo(titulo));
            var faixa = Txt("", 10.5, CorFraca);
            c.Children.Add(faixa);
            c.Children.Add(campo);
            int l = celula / 3;
            while (grade.RowDefinitions.Count <= l) grade.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(c, l); Grid.SetColumn(c, celula % 3); Grid.SetColumnSpan(c, colunas);
            grade.Children.Add(c);
            celula += colunas;
            return faixa;
        }
        TextBox Campo(string titulo, string faixa, long valor, out TextBlock tf)
        {
            var tb = Caixa(valor.ToString());
            tf = Celula(titulo, tb);
            tf.Text = faixa;
            return tb;
        }
        var forca = Campo(T("Força"), "", j.Forca, out var faixaForca);
        var salario = Campo(T("Salário"), T("{0} a {1}", Num(LimitesSave.SalarioMin), Num(LimitesSave.SalarioMax)), j.Salario, out _);
        var nota = Campo(T("Nota"), T("1 a 10"), j.Nota, out _);
        var lesao = Campo(T("Lesão"), T("0 = nunca, 10 = muito"), j.Lesao, out _);
        var susp = Campo(T("Suspenso (S)"), T("jogos, 0 a {0}", LimitesSave.SuspensaoMax), j.Suspensao, out _);
        var amar = Campo(T("Amarelos (A)"), T("acumulados, 0 a {0}", LimitesSave.AmarelosAcumuladosMax), j.AmarelosAcumulados, out _);
        var lesionado = Campo(T("Lesionado (L)"), T("jogos, 0 a {0}", LimitesSave.JogosLesionadoMax), j.JogosLesionado, out _);
        var comp = new ComboBox { ItemsSource = LimitesSave.Comportamentos.Select(x => T(x)).ToList(), SelectedIndex = Math.Clamp(j.Comportamento, 0, 5), HorizontalAlignment = HorizontalAlignment.Stretch };
        Combo(comp);
        Celula(T("Comportamento"), comp, 2).Text = "Fair Play … " + T("Sarrafeiro");

        var estrela = Txt("", 12.5, CorTexto, true);
        var maximo = Txt("", 12.5, CorAviso, true);
        var aviso = Txt("", 11.5, CorAviso);
        var erroTxt = Txt("", 12, CorErro, true);

        int NotaAgora() => int.TryParse((nota.Text ?? "").Trim(), out var n) ? Math.Clamp(n, 1, 10) : j.Nota;
        bool EstrelaAgora() => NotaAgora() != j.Nota ? LimitesSave.TemEstrela(j.Posicao, NotaAgora()) : j.Estrela;
        void Atualiza()
        {
            bool est = EstrelaAgora();
            estrela.Text = est ? "✱ " + T("Estrela") : T("Sem estrela");
            estrela.Foreground = est ? C.Ouro : CorFraca;
            int max = LimitesSave.ForcaMaximaNoElenco(eq.Jogadores, j, NotaAgora(), est);
            faixaForca.Text = max >= 1 ? T("1 a {0} (normal até {1})", Num(max), LimitesSave.ForcaNormal) : T("o plantel já passa do limite");
            maximo.Text = max >= 1 ? T("Força máxima neste plantel: {0}", Num(max)) : T("O resto do plantel já passa de um limite: diminua a força de outros jogadores.");
            aviso.Text = LerInteiro(forca.Text, 1, int.MaxValue, out var f) && f > LimitesSave.ForcaNormal
                ? T("Força {0} é bem acima do normal (1 a {1}).", f, LimitesSave.ForcaNormal) : "";
        }
        nota.TextChanged += (_, _) => Atualiza();
        forca.TextChanged += (_, _) => Atualiza();
        Atualiza();

        var topo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        topo.Children.Add(Selo(j.Posicao, 28));
        topo.Children.Add(Txt(j.Pais, 12, CorFraca, true));
        estrela.VerticalAlignment = VerticalAlignment.Center;
        topo.Children.Add(estrela);
        topo.Children.Add(Txt(T("Jogos {0} · Golos {1}", j.Jogos, j.Golos), 12, CorFraca));
        foreach (var c in topo.Children) c.VerticalAlignment = VerticalAlignment.Center;

        var p = new StackPanel { Spacing = 8 };
        p.Children.Add(topo);
        p.Children.Add(Txt(T("Estrela (✱): médio ou avançado com nota 8 ou mais. Muda sozinha com a nota."), 11, CorFraca));
        p.Children.Add(maximo);
        p.Children.Add(grade);
        p.Children.Add(aviso);
        p.Children.Add(erroTxt);

        Janelinha(j.Nome, p, T("OK"), () =>
        {
            var erro = ValidarFicha(eq, j, forca.Text, salario.Text, nota.Text, lesao.Text, susp.Text, amar.Text, lesionado.Text, out var v);
            long f = v.Forca, sal = v.Salario, n = v.Nota, l = v.Lesao, su = v.Suspensao, am = v.Amarelos, le = v.Lesionado;
            bool est = v.Estrela;
            if (erro != null) { erroTxt.Text = erro; return false; }
            bool mudou = f != j.Forca || sal != j.Salario || n != j.Nota || l != j.Lesao || su != j.Suspensao || am != j.AmarelosAcumulados
                || le != j.JogosLesionado || comp.SelectedIndex != Math.Clamp(j.Comportamento, 0, 5);
            if (!mudou) return true;
            if (n != j.Nota) j.Estrela = est;
            j.Forca = (int)f; j.Salario = sal; j.Nota = (int)n; j.Lesao = (int)l; j.Suspensao = (int)su;
            j.AmarelosAcumulados = (int)am; j.JogosLesionado = (int)le;
            if (comp.SelectedIndex != Math.Clamp(j.Comportamento, 0, 5)) j.Comportamento = comp.SelectedIndex;
            Mudou();
            MostrarPlantel(j);
            return true;
        }, ctx.Largo ? 560 : 520, forca);
    }

    // os valores da ficha (o texto das caixas) conferidos com os limites do jogo e do plantel
    // (LimitesSave): o erro, ou null. Serve a ficha do computador e a do celular
    internal readonly record struct ValoresFicha(int Forca, long Salario, int Nota, int Lesao, int Suspensao, int Amarelos, int Lesionado, bool Estrela);

    internal static string? ValidarFicha(EquipaEditavel eq, JogadorEditavel j, string? forca, string? salario, string? nota, string? lesao,
        string? susp, string? amar, string? lesionado, out ValoresFicha valores)
    {
        string? erro = null;
        long f = 0, sal = 0, n = 0, l = 0, su = 0, am = 0, le = 0;
        if (!LerInteiro(forca, LimitesSave.ForcaMin, LimitesSave.ForcaMax, out f)) erro = Faixa(T("Força"), "1", Num(LimitesSave.ForcaMax));
        else if (!LerInteiro(salario, LimitesSave.SalarioMin, LimitesSave.SalarioMax, out sal)) erro = Faixa(T("Salário"), Num(LimitesSave.SalarioMin), Num(LimitesSave.SalarioMax));
        else if (!LerInteiro(nota, 1, 10, out n)) erro = Faixa(T("Nota"), "1", "10");
        else if (!LerInteiro(lesao, 0, 10, out l)) erro = Faixa(T("Lesão"), "0", "10");
        else if (!LerInteiro(susp, 0, LimitesSave.SuspensaoMax, out su)) erro = Faixa(T("Suspenso (S)"), "0", LimitesSave.SuspensaoMax.ToString());
        else if (!LerInteiro(amar, 0, LimitesSave.AmarelosAcumuladosMax, out am)) erro = Faixa(T("Amarelos (A)"), "0", LimitesSave.AmarelosAcumuladosMax.ToString());
        else if (!LerInteiro(lesionado, 0, LimitesSave.JogosLesionadoMax, out le)) erro = Faixa(T("Lesionado (L)"), "0", LimitesSave.JogosLesionadoMax.ToString());
        bool est = n != j.Nota ? LimitesSave.TemEstrela(j.Posicao, (int)n) : j.Estrela;
        if (erro == null && (f != j.Forca || n != j.Nota))
        {
            var (qual, valor, max) = LimitesSave.Estouro(eq.Jogadores, j, (int)f, (int)n, est);
            if (qual != LimitesSave.Limite.Nenhum)
            {
                int fm = LimitesSave.ForcaMaximaNoElenco(eq.Jogadores, j, (int)n, est);
                erro = qual switch
                {
                    LimitesSave.Limite.ForcaJogador => T("Com nota {0}, acima de força {1} o jogo calcula errado e ele nunca marca golo.", n, Num(max)),
                    LimitesSave.Limite.SomaPesos => T("Os pesos de golo do plantel somariam {0} (máximo {1}): os golos sairiam sempre do mesmo jogador, ou a equipa deixaria de marcar.", Num(valor), Num(max)),
                    LimitesSave.Limite.SomaForca => T("A força do plantel somaria {0} (máximo {1}): o público dos jogos sairia errado.", Num(valor), Num(max)),
                    _ => T("A melhor escalação somaria {0} (máximo {1}): a força das equipas na partida sairia errada.", Num(valor), Num(max)),
                } + " " + (fm >= 1 ? T("Força máxima neste plantel: {0}", Num(fm)) : "");
            }
        }
        valores = new ValoresFicha((int)f, sal, (int)n, (int)l, (int)su, (int)am, (int)le, est);
        return erro;
    }

    // ---------------------------------------------------------------- janelinhas por cima
    // a janelinha (Ui.Janela) no centro, com OK e Cancelar;
    // ok devolve false para ficar aberta (erro)
    void Janelinha(string titulo, Control conteudo, string textoOk, Func<bool> ok, double largura, Control? focar = null)
    {
        Panel? fundo = null;
        // fecha esta (se ok abriu outra por cima, essa fica)
        void Fechar() { if (fundo != null) veu.Children.Remove(fundo); }
        void Ok() { if (ok()) Fechar(); }
        var okb = Botao(textoOk, Ok, true);
        var cancelar = Botao(T("Cancelar"), Fechar);
        var botoes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        botoes.Children.Add(okb); botoes.Children.Add(cancelar);
        var p = new StackPanel { Spacing = 12, Width = largura, Margin = new Thickness(16, 12) };
        p.Children.Add(conteudo);
        p.Children.Add(botoes);
        if (okb is Clicavel ob) ob.MinWidth = cancelar.MinWidth = 84;
        fundo = Ui.Veu(Ui.Janela(titulo, p, Fechar, "disquete"));
        fundo.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Fechar(); }
            else if (e.Key == Key.Enter && e.Source is not ComboBox) { e.Handled = true; Ok(); }
        };
        veu.Children.Clear();
        veu.Children.Add(fundo);
        Dispatcher.UIThread.Post(() =>
        {
            if (focar != null) { focar.Focus(); if (focar is TextBox tb) tb.SelectAll(); }
            else okb.Focus();
        }, DispatcherPriority.Loaded);
    }

    void Mensagem(string titulo, string texto) => Janelinha(titulo, Txt(texto, 13, CorTexto), T("OK"), () => true, 400);

    // com alteracoes por gravar: pergunta antes de as perder
    void Confirmar(Action seguir)
    {
        if (!alterado) { seguir(); return; }
        Janelinha(T("Alterações por gravar"), Txt(T("As alterações a este jogo ainda não foram gravadas. Descartar?"), 13, CorTexto),
            T("Descartar"), () => { alterado = false; seguir(); return true; }, 400);
    }
}
