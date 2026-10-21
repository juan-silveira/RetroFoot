using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Layout;
using Avalonia.Media;
using RetroFoot.Motor.Save;
using static RetroFoot.App.Idioma;
using E = RetroFoot.App.TelaEditorSave;

namespace RetroFoot.App;

// O Editor de Save no celular (a versao em pe do FerramentaEditorSave.cs, com os mesmos dados e
// as mesmas regras: SaveEditavel e LimitesSave do motor). Uma pagina que rola: o jogo gravado
// (e Recarregar), a epoca (inflacao), os treinadores humanos (Trocar abre a lista das equipas
// das divisoes), o clube (a escolha da equipa com procura, dinheiro, moral e estadio com - e +)
// e as contas de 16 bits do plantel; embaixo o plantel; o toque num jogador abre a ficha numa
// folha de baixo (forca ate ao maximo deste plantel, salario, nota, lesao, S/A/L e
// comportamento). O rodape fixo tem o estado e o Gravar (o .bak na 1a vez, como no computador).
// Duas peles: Moderno (os cartoes verdes do kit) e Retro (a janela cinza do Windows 95 com as
// molduras de grupo, a caixa branca afundada da lista e a equipa nas cores dela).
static class FerramentaEditorSaveCelular
{
    [ModuleInitializer]
    internal static void Registar() => Ferramentas.RegistrarCelular(Ferramentas.EditorSave, ctx => new TelaEditorSaveCelular(ctx));
}

sealed class TelaEditorSaveCelular : UserControl
{
    readonly Ferramentas.Contexto ctx;
    readonly string pasta;
    readonly Grid raiz = new();
    readonly Panel sobre = new() { IsVisible = false };

    List<string> saves = new();
    SaveEditavel? save;
    string caminho = "";
    int saveMostrado = -1;
    EquipaEditavel? equipa;
    List<EquipaEditavel> equipas = new();
    bool alterado, montando;

    // o que muda com o save e o clube
    TextBox inflacao = null!, dinheiro = null!, moral = null!;
    string inflacaoMostrada = "", moralMostrado = "", dinheiroMostrado = "";
    readonly ContentControl escolhaSave = new(), escolhaEquipa = new(), escolhaEquipa2 = new(), previa = new(), treinadores = new(), contas = new(), plantel = new();
    Control cartaoTreinadores = null!, corpoJogo = null!;
    TextBlock epoca = null!, estadio = null!, estado = null!, tituloPlantel = null!;
    CUi.Botao estMenos = null!, estMais = null!, gravar = null!;
    readonly List<Control> controlesDoSave = new();

    static bool R => CUi.Retro;
    // no Retro a pagina e verde (o texto branco, os rotulos e avisos amarelos) e as folhas sao
    // as janelas cinza (o texto preto): folha = true enquanto se monta o corpo de uma folha
    static bool folha;
    static IBrush Aviso => R ? (folha ? W95.Marinho : W95.Amarelo) : TemaVerde.Destaque;
    static IBrush Fraco => R ? (folha ? W95.Cinzento : W95.Branco) : CUi.Fraco;
    static IBrush Texto => R ? (folha ? W95.Preto : W95.Branco) : CUi.Texto;
    static IBrush Rotulo => R ? (folha ? W95.Preto : W95.Amarelo) : CUi.Texto;

    public TelaEditorSaveCelular(Ferramentas.Contexto ctx)
    {
        this.ctx = ctx;
        pasta = Environment.GetEnvironmentVariable("RETROFOOT_JOGOS") is { Length: > 0 } j ? j : Path.Combine(ctx.RaizDados, Motor.Arquivos.PastaDados.Saves);
        FontFamily = CUi.Fonte;
        raiz.Children.Add(Montar());
        raiz.Children.Add(sobre);
        Content = raiz;
        ListarSaves(null);
        // a barra do titulo e a desta pagina (com o voltar que pergunta antes de perder as
        // alteracoes): a do celular, posta por cima, fica escondida; o Voltar do Android tambem
        // pergunta
        AttachedToVisualTree += (_, _) =>
        {
            if (Parent is Panel p) foreach (var c in p.Children) if (c != this) c.IsVisible = false;
            CUi.Folha.VoltarPagina = () => { Sair(); return true; };
        };
        // o Esc (no computador, a previa) faz o mesmo que o voltar
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || e.Handled) return;
            e.Handled = true;
            Sair();
        }, Avalonia.Interactivity.RoutingStrategies.Bubble);
    }

    // ---------------------------------------------------------------- pecas
    static TextBlock Txt(string s, double tam = 15, IBrush? cor = null, FontWeight peso = FontWeight.Normal, bool quebra = false) =>
        R ? CR95.Txt(s, tam, cor ?? Texto, peso >= FontWeight.SemiBold, quebra) : CUi.Txt(s, tam, cor, peso, quebra);

    // um cartao do kit (Moderno) ou a moldura de grupo (TGroupBox) na janela cinza (Retro)
    static Control Secao(string titulo, Control conteudo) => R
        ? CR95.Bevel(new StackPanel { Spacing = 8, Children = { CR95.Txt(titulo, 16, W95.Amarelo, true, false), conteudo } })
        : CUi.Cartao_(titulo, conteudo);

    // o selo da posicao (as cores do computador: G amarelo, D azul, M verde, A vermelho)
    static Control Selo(int posicao, double tam = 26)
    {
        var t = new TextBlock
        {
            Text = posicao is >= 0 and < 4 ? Posicao(posicao) : "?", Foreground = Brushes.White, FontWeight = FontWeight.Bold,
            FontFamily = CUi.Fonte, FontSize = 13 * Acessibilidade.Texto, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Tag = "fixo",
        };
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse(posicao is >= 0 and < 4 ? E.CoresPosicao[posicao] : "#808080")),
            CornerRadius = new CornerRadius(R ? 0 : 6), Width = tam, Height = tam, Child = t, VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = R ? W95.Preto : null, BorderThickness = new Thickness(R ? 1 : 0),
        };
    }

    // o campo de numero: o menos, a caixa (o teclado numerico) e o mais; passo(+-1) ao toque
    static (Control Campo, TextBox Caixa) Numero(string texto, Action<TextBox, int> passo, bool decimais = false)
    {
        var caixa = CUi.Caixa(texto);
        caixa.FontWeight = FontWeight.Bold;
        caixa.HorizontalContentAlignment = HorizontalAlignment.Center;
        caixa.MinHeight = 46;
        caixa.Padding = new Thickness(4, 0);
        TextInputOptions.SetContentType(caixa, TextInputContentType.Number);
        CUi.Botao B(string s, int d)
        {
            var b = new CUi.Botao(s, false, () => passo(caixa, d)) { Padding = new Thickness(0), MinHeight = 46, MinWidth = 46, HorizontalAlignment = HorizontalAlignment.Stretch };
            if (b.Child is TextBlock tb) tb.FontSize = 22 * Acessibilidade.Texto;
            return b;
        }
        var menos = B("−", -1);
        var mais = B("+", 1);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("46,6,*,6,46") };
        g.Children.Add(menos);
        Grid.SetColumn(caixa, 2); g.Children.Add(caixa);
        Grid.SetColumn(mais, 4); g.Children.Add(mais);
        return (g, caixa);
    }

    // o rotulo (e a faixa embaixo) a esquerda e o campo a direita
    static Control Campo(string rotulo, Control campo, TextBlock? faixa = null, double largura = 196)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"*,{largura}"), ColumnSpacing = 10, MinHeight = 48 };
        var t = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(Txt(rotulo, 15, Rotulo, FontWeight.SemiBold));
        if (faixa != null) t.Children.Add(faixa);
        g.Children.Add(t);
        Grid.SetColumn(campo, 1);
        g.Children.Add(campo);
        return g;
    }

    static TextBlock Faixa(string s = "") => Txt(s, 12.5, Fraco, FontWeight.Normal, true);

    // o campo que abre uma escolha (no Retro, a caixa do TComboBox com a seta; no Moderno, a
    // pilula com o chevron)
    static Control CampoEscolha(Control conteudo, Action toque)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        g.Children.Add(conteudo);
        Control seta = R
            ? new Border
            {
                Width = 32, Background = new SolidColorBrush(Color.Parse("#C0C0C0")), Margin = new Thickness(0, 2, 2, 2),
                BoxShadow = BoxShadows.Parse("inset 1 1 0 0 #FFFFFF, inset -1 -1 0 0 #000000, inset 2 2 0 0 #DFDFDF, inset -2 -2 0 0 #808080"),
                // a seta do TComboBox (a mesma do CR95.Combo)
                Child = new Pintor((dc, s) =>
                {
                    double cx = Math.Floor(s.Width / 2), cy = Math.Floor(s.Height / 2);
                    for (int i = 0; i < 6; i++) dc.FillRectangle(W95.Preto, new Rect(cx - 5.5 + i, cy - 3 + i, 11 - 2 * i, 1));
                }),
            }
            : CUi.Txt("›", 24, CUi.Fraco);
        Grid.SetColumn(seta, 1);
        g.Children.Add(seta);
        conteudo.Margin = new Thickness(R ? 6 : 0, 0, 6, 0);
        var c = new Clicavel
        {
            Child = g, MinHeight = 46, CorFoco = null,
            Padding = R ? new Thickness(2, 0, 0, 0) : new Thickness(12, 4, 10, 4),
            Background = R ? W95.Branco : TemaVerde.BotaoSecundario,
            CornerRadius = new CornerRadius(R ? 0 : 12),
            BorderBrush = R ? null : TemaVerde.Borda, BorderThickness = new Thickness(R ? 0 : 1.5),
            BoxShadow = R ? BoxShadows.Parse("inset 1 1 0 0 #A6A6A6, inset -1 -1 0 0 #FFFFFF, inset 2 2 0 0 #9E9E9E, inset -2 -2 0 0 #9E9E9E") : default,
            Hover = R ? null : TemaVerde.Hover,
        };
        c.Clique += toque;
        return c;
    }

    static IBrush Cor(int rgb) => new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    // o nome da equipa na caixa nas cores dela (como a previa do computador)
    static Control CaixaEquipa(EquipaEditavel e, string? sub, double tam = 15)
    {
        var p = new StackPanel { Spacing = 1 };
        var n = new TextBlock { Text = e.NomeExibido, FontFamily = CUi.Fonte, FontSize = tam * Acessibilidade.Texto, FontWeight = FontWeight.Bold, Foreground = Cor(e.CorLetra), TextTrimming = TextTrimming.CharacterEllipsis, Tag = "fixo" };
        p.Children.Add(n);
        // (sub: uma linha por \n)
        foreach (var l in (sub ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            p.Children.Add(new TextBlock { Text = l, FontFamily = CUi.Fonte, FontSize = 12.5 * Acessibilidade.Texto, Foreground = Cor(e.CorLetra), TextTrimming = TextTrimming.CharacterEllipsis, Tag = "fixo" });
        return new Border
        {
            Background = Cor(e.CorFundo), Padding = new Thickness(10, 6), Child = p, CornerRadius = new CornerRadius(R ? 0 : 8),
            BorderBrush = R ? W95.Preto : null, BorderThickness = new Thickness(R ? 1 : 0),
        };
    }

    // ---------------------------------------------------------------- a pagina
    // duas abas: Clube (o jogo gravado, a epoca, os treinadores e o clube) e Plantel (a equipa,
    // as contas de 16 bits e os jogadores); o rodape com o estado e o Gravar fica nas duas
    Control Montar()
    {
        var pClube = new StackPanel { Spacing = R ? 6 : 12 };

        // o jogo gravado: a escolha e o Recarregar lado a lado; a pasta embaixo
        var recarregar = new CUi.Botao(T("Recarregar"), false, () => Confirmar(() => ListarSaves(caminho), true)) { Padding = new Thickness(14, 6) };
        var linhaSave = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto") };
        linhaSave.Children.Add(escolhaSave);
        Grid.SetColumn(recarregar, 2);
        linhaSave.Children.Add(recarregar);
        var js = new StackPanel { Spacing = 6 };
        js.Children.Add(linhaSave);
        var tp = Txt(pasta, 11.5, Fraco, FontWeight.Normal, true);
        js.Children.Add(tp);
        pClube.Children.Add(Secao(T("Jogo gravado"), js));

        // a epoca: a inflacao (x 10, como o jogo mostra)
        var (ci, inf) = Numero("", (c, d) => PassoReal(c, d, LimitesSave.InflacaoMin * 10, LimitesSave.InflacaoMax * 10), true);
        inflacao = inf;
        epoca = Faixa();
        var jogo = new StackPanel { Spacing = 4 };
        jogo.Children.Add(Campo(T("Inflação"), ci, null, 180));
        jogo.Children.Add(epoca);
        corpoJogo = Secao(T("Época"), jogo);
        pClube.Children.Add(corpoJogo);

        // os treinadores humanos
        cartaoTreinadores = Secao(T("Treinadores"), treinadores);
        pClube.Children.Add(cartaoTreinadores);

        // o clube
        var (cd, din) = Numero("", (c, d) => PassoInteiro(c, d * 100_000, LimitesSave.DinheiroMin, LimitesSave.DinheiroMax));
        dinheiro = din;
        var (cm, mor) = Numero("", (c, d) => PassoReal(c, d, 0, LimitesSave.MoralMax * 10), true);
        moral = mor;
        foreach (var t in new[] { inflacao, dinheiro, moral }) t.TextChanged += (_, _) => { if (!montando && CamposMudaram()) Estado(T("Alterações por gravar"), Aviso); };
        estadio = Txt("—", 17, null, FontWeight.Bold);
        estadio.HorizontalAlignment = HorizontalAlignment.Center;
        estMenos = new CUi.Botao("−", false, () => PassoEstadio(-1)) { Padding = new Thickness(0), MinHeight = 46, MinWidth = 46, HorizontalAlignment = HorizontalAlignment.Stretch };
        estMais = new CUi.Botao("+", false, () => PassoEstadio(1)) { Padding = new Thickness(0), MinHeight = 46, MinWidth = 46, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var b in new[] { estMenos, estMais }) if (b.Child is TextBlock tb) tb.FontSize = 22 * Acessibilidade.Texto;
        var est = new Grid { ColumnDefinitions = new ColumnDefinitions("46,6,*,6,46") };
        est.Children.Add(estMenos);
        Grid.SetColumn(estadio, 2); est.Children.Add(estadio);
        Grid.SetColumn(estMais, 4); est.Children.Add(estMais);
        var clube = new StackPanel { Spacing = 8 };
        clube.Children.Add(escolhaEquipa);
        clube.Children.Add(previa);
        // (o dinheiro tem ate 11 algarismos: a linha inteira, com o rotulo em cima)
        clube.Children.Add(Txt(T("Dinheiro"), 15, Rotulo, FontWeight.SemiBold));
        clube.Children.Add(cd);
        clube.Children.Add(Campo(T("Moral"), cm, null, 180));
        clube.Children.Add(Campo(T("Estádio"), est, null, 180));
        clube.Children.Add(Faixa(T("Moral: 0 a 20 · Estádio: 5.000 a 120.000 lugares")));
        pClube.Children.Add(Secao(T("Clube"), clube));

        // o plantel: a equipa (a mesma escolha), as contas e a lista
        var pPlantel = new StackPanel { Spacing = R ? 6 : 10 };
        pPlantel.Children.Add(escolhaEquipa2);
        pPlantel.Children.Add(Secao(T("Limites de 16 bits"), contas));
        tituloPlantel = R ? CR95.Txt("", 16, W95.Amarelo, true, false) : CUi.TituloSecao_("");
        tituloPlantel.Margin = new Thickness(R ? 2 : 4, 6, 0, 0);
        pPlantel.Children.Add(tituloPlantel);
        pPlantel.Children.Add(Txt(T("Toque num jogador para editar. S = suspenso, A = amarelos, L = lesionado (jogos)."), 12.5, R ? W95.Branco : CUi.TituloSecao, FontWeight.Normal, true));
        pPlantel.Children.Add(plantel);

        controlesDoSave.AddRange(new Control[] { inflacao, dinheiro, moral, escolhaEquipa, escolhaEquipa2, ci, cd, cm });

        // o rodape: o estado e o Gravar
        estado = Txt("", 13, Fraco, FontWeight.SemiBold, true);
        estado.MaxLines = 2;
        gravar = new CUi.Botao(T("Gravar"), true, Gravar) { MinWidth = 120 };
        var rod = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,Auto") };
        rod.Children.Add(estado);
        Grid.SetColumn(gravar, 2);
        rod.Children.Add(gravar);

        var barra = CUi.BarraTopo(T("Editor de Save"), null, Sair);
        var nomes = new[] { T("Clube"), T("Plantel") };
        Control[] paginas = { pClube, pPlantel };
        var corpo = new ContentControl { Content = pClube };
        Control pagina = null!;
        var (fila, _) = CUi.Abas(nomes, k =>
        {
            corpo.Content = paginas[k];
            if (pagina is DockPanel dp && dp.Children.OfType<ScrollViewer>().LastOrDefault() is { } sv) sv.Offset = default;
        });
        // o Retro: o verde da escolha do jogo (as molduras, os rotulos amarelos, as caixas
        // brancas), como a pagina do Editor de Save do computador no visual classico
        if (R) pagina = CR95.Pagina(barra, corpo, rod, W95.Verde, fila);
        else pagina = CUi.Pagina(new StackPanel { Children = { barra, fila } }, corpo, rod);
        return pagina;
    }

    void Habilitar(bool sim)
    {
        foreach (var c in controlesDoSave) c.IsEnabled = sim;
        gravar.Habilitado = sim;
    }

    void Estado(string texto, IBrush cor)
    {
        estado.Text = texto;
        estado.Foreground = cor;
    }

    void Mudou()
    {
        alterado = true;
        Estado(T("Alterações por gravar"), Aviso);
    }

    // os campos do jogo e do clube mudaram (ainda por aplicar)
    bool CamposMudaram() => save != null && ((inflacao.Text ?? "").Trim() != inflacaoMostrada
        || equipa != null && ((moral.Text ?? "").Trim() != moralMostrado || (dinheiro.Text ?? "").Trim() != dinheiroMostrado));

    // o - e o + dos campos: o valor escrito (ou o do save) mais o passo, dentro da faixa
    static void PassoReal(TextBox c, int d, double min, double max)
    {
        if (!E.LerReal(c.Text, double.MinValue, double.MaxValue, out var v)) return;
        c.Text = E.Dec(Math.Clamp(Math.Round(v + d, 1), min, max));
    }

    static void PassoInteiro(TextBox c, long d, long min, long max)
    {
        if (!E.LerInteiro(c.Text, long.MinValue / 2, long.MaxValue / 2, out var v)) return;
        c.Text = E.Num(Math.Clamp(v + d, min, max));
    }

    // ---------------------------------------------------------------- saves
    void ListarSaves(string? escolher)
    {
        saves = Directory.Exists(pasta)
            ? Directory.EnumerateFiles(pasta).Where(p => string.Equals(Path.GetExtension(p), ".e98", StringComparison.OrdinalIgnoreCase)
                && new FileInfo(p).Length > 0).OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();
        saveMostrado = -1;
        if (saves.Count == 0)
        {
            save = null; equipa = null; equipas = new(); alterado = false;
            MostrarTudo();
            Estado(T("Nenhum jogo gravado em {0}", pasta), Aviso);
            return;
        }
        int i = escolher != null ? saves.IndexOf(escolher) : -1;
        Abrir(i >= 0 ? i : 0);
    }

    static string NomeSave(string p) => Path.GetFileNameWithoutExtension(p).ToUpperInvariant();

    // a escolha do jogo gravado: no Retro o TComboBox do computador; no Moderno, uma folha
    void MostrarEscolhaSave()
    {
        var nomes = saves.Select(NomeSave).ToList();
        if (R)
        {
            var combo = new CR95.Combo(nomes, saveMostrado);
            combo.Mudou += i => TrocouSave(i, () => combo.Indice = saveMostrado);
            combo.IsEnabled = saves.Count > 0;
            escolhaSave.Content = combo;
            return;
        }
        var t = CUi.Txt(saveMostrado >= 0 ? nomes[saveMostrado] : "—", 16, null, FontWeight.Bold);
        var c = CampoEscolha(t, () =>
        {
            if (saves.Count == 0) return;
            CUi.Folha? f = null;
            var linhas = nomes.Select(n => (Control)CUi.Txt(n, 16, null, FontWeight.SemiBold)).ToList();
            var (lista, marcar) = CUi.ListaEscolha(linhas, i => { Fechar(f!); TrocouSave(i, null); });
            marcar(saveMostrado);
            f = new CUi.Folha(T("Jogo gravado"), lista, fechar: () => Fechar(f!));
            f.AoVoltar = () => Fechar(f);
            Abrir(f);
        });
        c.IsEnabled = saves.Count > 0;
        escolhaSave.Content = c;
    }

    void TrocouSave(int i, Action? voltar)
    {
        if (i < 0 || i >= saves.Count || i == saveMostrado && save != null) return;
        if ((alterado || CamposMudaram()) && saveMostrado >= 0)
        {
            voltar?.Invoke();
            Confirmar(() => { alterado = false; Abrir(i); }, true);
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
            Estado(T("{0}: {1} equipas", Path.GetFileName(caminho), equipas.Count), Fraco);
        }
        catch (Exception ex)
        {
            save = null; equipa = null; equipas = new();
            MostrarTudo();
            Estado(T("Não consegui ler {0}: {1}", Path.GetFileName(caminho), ex.Message), CUi.Erro);
        }
    }

    void MostrarTudo()
    {
        Habilitar(save != null);
        MostrarEscolhaSave();
        montando = true;
        inflacaoMostrada = save != null ? E.Dec(save.Inflacao * 10) : "";
        inflacao.Text = inflacaoMostrada;
        montando = false;
        epoca.Text = (save != null ? T("Época {0}", save.Ano) + " · " : "") + T("inflação de 5 a 100 (o jogo mostra x 10)");
        MostrarTreinadores();
        MostrarClube();
    }

    // ---------------------------------------------------------------- clube
    void MostrarClube()
    {
        var e = equipa;
        montando = true;
        dinheiroMostrado = e != null ? E.Num(e.Dinheiro) : "";
        dinheiro.Text = dinheiroMostrado;
        moralMostrado = e != null ? E.Dec(e.Moral * 10) : "";
        moral.Text = moralMostrado;
        montando = false;
        escolhaEquipa.Content = CampoEscolha(Txt(e?.NomeExibido ?? "—", 16, R ? W95.Preto : null, FontWeight.Bold), EscolherEquipa);
        // no Plantel, a equipa na caixa nas cores dela (o toque tambem troca)
        escolhaEquipa2.Content = e == null ? CampoEscolha(Txt("—", 16, R ? W95.Preto : null, FontWeight.Bold), EscolherEquipa)
            : CampoEscolha(CaixaEquipa(e, string.Join(" · ", new[] { e.Pais, e.Divisao }.Where(x => x.Length > 0)), 16), EscolherEquipa);
        escolhaEquipa.IsEnabled = escolhaEquipa2.IsEnabled = save != null && equipas.Count > 0;
        previa.Content = e == null ? null
            : CaixaEquipa(e, (e.Nome != e.NomeExibido ? e.Nome + "\n" : "") + string.Join(" · ", new[] { e.Pais, e.Divisao, e.Treinador?.Nome ?? "" }.Where(s => s.Length > 0)), 16);
        MostrarEstadio();
        MostrarPlantel();
    }

    void MostrarEstadio()
    {
        var e = equipa;
        estadio.Text = e != null ? E.Num(e.Capacidade) : "—";
        estMenos.Habilitado = e != null && e.Estadio > 1;
        estMais.Habilitado = e != null && e.Estadio < LimitesSave.EstadioMax;
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

    // a escolha da equipa: a folha com a procura e a lista (o nome nas cores dela e a divisao)
    void EscolherEquipa()
    {
        folha = true;
        try
        {
            if (save == null || equipas.Count == 0) return;
            if (Aplicar() is { } erro) { Mensagem(T("Valor fora do limite"), erro); return; }
            CUi.Folha? f = null;
            var procura = CUi.Caixa("", 30, T("Procurar"));
            var lista = new ContentControl { VerticalAlignment = VerticalAlignment.Top };
            void Filtrar()
            {
                bool antes = folha;
                folha = true;
                try { FiltrarJa(); } finally { folha = antes; }
            }
            void FiltrarJa()
            {
                var q = LimitesSave.SemAcento((procura.Text ?? "").Trim()).ToUpperInvariant();
                var lst = equipas.Where(e => q.Length == 0 || LimitesSave.SemAcento(e.NomeExibido + " " + e.Nome).ToUpperInvariant().Contains(q)).Take(200).ToList();
                var linhas = lst.Select(e =>
                {
                    var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
                    var cx = CaixaEquipa(e, null, 15);
                    cx.HorizontalAlignment = HorizontalAlignment.Left;
                    g.Children.Add(cx);
                    var dv = Txt(e.Divisao, 12.5, Fraco);
                    Grid.SetColumn(dv, 1);
                    g.Children.Add(dv);
                    return (Control)g;
                }).ToList();
                var (l, marcar) = CUi.ListaEscolha(linhas, i => { Fechar(f!); TrocarClube(lst[i]); });
                marcar(equipa != null ? lst.IndexOf(equipa) : -1);
                lista.Content = linhas.Count > 0 ? l : Txt(T("Nenhuma equipa"), 14, Fraco);
            }
            procura.TextChanged += (_, _) => Filtrar();
            Filtrar();
            var rolar = new ScrollViewer { Content = lista, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Height = Math.Max(240, Bounds.Height * 0.62) };
            var p = new StackPanel { Spacing = 10, Children = { procura, rolar } };
            f = new CUi.Folha(T("Clube"), new Border { Padding = new Thickness(14, 6, 14, 14), Child = p }, sub: T("{0}: {1} equipas", Path.GetFileName(caminho), equipas.Count), rolar: false, fechar: () => Fechar(f!));
            f.AoVoltar = () => Fechar(f);
            Abrir(f);
        }
        finally { folha = false; }
    }

    void TrocarClube(EquipaEditavel e)
    {
        if (e == equipa) return;
        equipa = e;
        MostrarClube();
    }

    // as contas de 16 bits do plantel (o medidor de cada uma)
    void MostrarContas()
    {
        if (equipa == null) { contas.Content = Txt("—", 14, Fraco); return; }
        var p = new StackPanel { Spacing = 4 };
        var (pesos, forca, escal) = LimitesSave.Contas(equipa.Jogadores);
        void Uma(string nome, string conta, long v, long max)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            var tn = new StackPanel { Spacing = 0 };
            tn.Children.Add(Txt(nome, 14, null, FontWeight.SemiBold));
            if (conta != "") tn.Children.Add(Txt(conta, 12, Fraco));
            g.Children.Add(tn);
            var t = Txt($"{E.Num(v)} / {E.Num(max)}", 14, v > max ? CUi.Erro : null, FontWeight.Bold);
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            p.Children.Add(g);
            double f = v / (double)max;
            IBrush cor = v > max ? CUi.Erro : f > 0.8 ? (R ? W95.Marinho : TemaVerde.Ouro) : (R ? W95.Marinho : TemaVerde.Verde);
            var m = CUi.Medidor(v, max, cor, R ? 16 : 8);
            m.Margin = new Thickness(0, 2, 0, 8);
            p.Children.Add(m);
        }
        Uma(T("Soma da força"), "", forca, LimitesSave.SomaForcaMax);
        Uma(T("Pesos de golo"), T("pos² × força × nota"), pesos, LimitesSave.SomaPesosMax);
        Uma(T("Melhor escalação"), "3G + D + M + A", escal, LimitesSave.EscalacaoMax);
        contas.Content = p;
    }

    // ---------------------------------------------------------------- plantel
    static readonly string[] Setores = { "Goleiros", "Defensores", "Meio-campistas", "Atacantes" };
    static readonly string[] NomePosicao = { "Goleiro", "Defensor", "Meio-campista", "Atacante" };

    void MostrarPlantel()
    {
        MostrarContas();
        if (equipa == null) { plantel.Content = null; tituloPlantel.Text = R ? T("Plantel") : T("Plantel").ToUpperInvariant(); return; }
        var o = equipa.OrdemDoJogo();
        var tp = T("Plantel") + "  ·  " + o.Count;
        tituloPlantel.Text = R ? tp : tp.ToUpperInvariant();
        plantel.Content = R ? PlantelRetro(o) : PlantelModerno(o);
    }

    // o Moderno: um cartao por setor; cada linha com o selo, o nome (e a estrela), os dados
    // embaixo, a situacao e a forca a direita
    Control PlantelModerno(List<JogadorEditavel> o)
    {
        var p = new StackPanel { Spacing = 6 };
        foreach (var grupo in o.GroupBy(j => Math.Clamp(j.Posicao, 0, 3)))
        {
            var cab = CUi.TituloSecao_(T(Setores[grupo.Key]) + "  ·  " + grupo.Count());
            cab.Margin = new Thickness(4, 8, 0, 2);
            p.Children.Add(cab);
            p.Children.Add(CUi.Grupo(grupo.Select(j => (Control)CUi.Toque(LinhaModerna(j), () => Ficha(j), 60))));
        }
        return p;
    }

    Control LinhaModerna(JogadorEditavel j)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,10,*,Auto,Auto"), MinHeight = 46 };
        g.Children.Add(Selo(j.Posicao));
        var nome = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        nome.Children.Add(CUi.Txt(j.Nome, 15.5, null, FontWeight.SemiBold));
        if (j.Estrela) nome.Children.Add(CUi.Txt("✱", 15, TemaVerde.Ouro, FontWeight.Bold));
        var dados = CUi.Txt(T("Salário") + " " + E.Num(j.Salario) + " · " + T("Nota") + " " + j.Nota + " · " + E.Comportamento(j.Comportamento), 12.5, CUi.Fraco);
        var meio = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { nome, dados } };
        Grid.SetColumn(meio, 2);
        g.Children.Add(meio);
        var sit = E.Situacao(j);
        if (sit.Length > 0)
        {
            var s = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#FFE4E1")), CornerRadius = new CornerRadius(5), Padding = new Thickness(5, 1),
                Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = CUi.Txt(sit, 12, new SolidColorBrush(Color.Parse("#C62828")), FontWeight.Bold),
            };
            Grid.SetColumn(s, 3);
            g.Children.Add(s);
        }
        bool acima = j.Forca > LimitesSave.ForcaNormal;
        var f = CUi.Txt(j.Forca.ToString(), 17, acima ? TemaVerde.Laranja : TemaVerde.Destaque, FontWeight.Bold);
        f.MinWidth = 44; f.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(f, 4);
        g.Children.Add(f);
        return g;
    }

    // o Retro: a lista do computador na caixa branca afundada, com os titulos das colunas em
    // cima, o texto preto em Arial e o traco no fim de cada posicao; o toque realca (o azul do
    // Windows 95) e abre a ficha
    static readonly string ColunasRetro = "26,*,48,74,28,44";

    Grid LinhaRetroGrade(string[] t, bool titulos, JogadorEditavel? j = null)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(ColunasRetro), ColumnSpacing = 4, MinHeight = titulos ? 26 : 40 };
        var alinhar = new[] { HorizontalAlignment.Center, HorizontalAlignment.Left, HorizontalAlignment.Right, HorizontalAlignment.Right, HorizontalAlignment.Center, HorizontalAlignment.Center };
        for (int i = 0; i < t.Length; i++)
        {
            IBrush cor = W95.Preto;
            if (j != null && i == 2 && j.Forca > LimitesSave.ForcaNormal) cor = W95.Vermelho;
            if (j != null && i == 5) cor = W95.Vermelho;
            var x = CR95.Txt(t[i], titulos ? 12.5 : 14.5, cor, titulos, false);
            x.HorizontalAlignment = alinhar[i];
            Grid.SetColumn(x, i);
            g.Children.Add(x);
        }
        return g;
    }

    Control PlantelRetro(List<JogadorEditavel> o)
    {
        var lista = new StackPanel();
        for (int i = 0; i < o.Count; i++)
        {
            var j = o[i];
            bool fim = i < o.Count - 1 && Math.Clamp(o[i + 1].Posicao, 0, 3) != Math.Clamp(j.Posicao, 0, 3);
            var g = LinhaRetroGrade(new[]
            {
                j.Posicao is >= 0 and < 4 ? Posicao(j.Posicao) : "?", j.Nome + (j.Estrela ? " ✱" : ""), j.Forca.ToString(),
                E.Num(j.Salario), j.Nota.ToString(), E.Situacao(j),
            }, false, j);
            var c = new Clicavel { Background = Brushes.Transparent, Child = g, Padding = new Thickness(4, 0), CorFoco = null };
            void Pinta(bool sel)
            {
                c.Background = sel ? W95.Marinho : Brushes.Transparent;
                foreach (var x in g.Children.OfType<TextBlock>())
                    x.Foreground = sel ? W95.Branco : Grid.GetColumn(x) == 5 || Grid.GetColumn(x) == 2 && j.Forca > LimitesSave.ForcaNormal ? W95.Vermelho : W95.Preto;
            }
            c.Clique += () =>
            {
                Pinta(true);
                Ficha(j, () => Pinta(false));
            };
            lista.Children.Add(new Border
            {
                Child = c, BorderBrush = W95.Cinzento, BorderThickness = new Thickness(0, 0, 0, fim ? 1 : 0), Margin = new Thickness(0, 0, 0, fim ? 3 : 0),
            });
        }
        var cab = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#C0C0C0")), Padding = new Thickness(4, 0),
            BoxShadow = BoxShadows.Parse("inset 1 1 0 0 #FFFFFF, inset -1 -1 0 0 #808080"),
            Child = LinhaRetroGrade(new[] { "P", T("Nome"), T("Força"), T("Salário"), "N", T("Sit.") }, true),
        };
        return CR95.CaixaLista(new StackPanel { Children = { cab, lista } });
    }

    // ---------------------------------------------------------------- aplicar e gravar
    // passa os campos do jogo e do clube para o save em memoria; o erro, ou null (o mesmo do
    // computador)
    string? Aplicar()
    {
        if (save == null) return null;
        if ((inflacao.Text ?? "").Trim() != inflacaoMostrada)
        {
            if (!E.LerReal(inflacao.Text, LimitesSave.InflacaoMin * 10, LimitesSave.InflacaoMax * 10, out var inf))
                return E.Faixa(T("Inflação"), "5", "100");
            save.Inflacao = inf / 10;
            inflacaoMostrada = (inflacao.Text ?? "").Trim();
            alterado = true;
        }
        if (equipa == null) return null;
        if (!E.LerInteiro(dinheiro.Text, LimitesSave.DinheiroMin, LimitesSave.DinheiroMax, out var din))
            return E.Faixa(T("Dinheiro"), E.Num(LimitesSave.DinheiroMin), E.Num(LimitesSave.DinheiroMax));
        if ((moral.Text ?? "").Trim() != moralMostrado)
        {
            if (!E.LerReal(moral.Text, 0, LimitesSave.MoralMax * 10, out var m)) return E.Faixa(T("Moral"), "0", "20");
            equipa.Moral = m / 10;
            moralMostrado = (moral.Text ?? "").Trim();
            alterado = true;
        }
        if (din != equipa.Dinheiro) { equipa.Dinheiro = din; alterado = true; }
        dinheiroMostrado = (dinheiro.Text ?? "").Trim();
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
            Estado(T("Gravado. O original fica em {0}", Path.GetFileName(bak)), CUi.Bom);
        }
        catch (Exception ex) { Mensagem(T("Erro"), T("Não consegui gravar:\n{0}", ex.Message)); }
    }

    // o voltar (da barra e do Android): com alteracoes por gravar, pergunta
    void Sair()
    {
        if (sobre.Children.OfType<CUi.Folha>().LastOrDefault() is { } f) { f.AoVoltar?.Invoke(); return; }
        Confirmar(ctx.Voltar, true);
    }

    // ---------------------------------------------------------------- treinadores
    void MostrarTreinadores()
    {
        var p = new StackPanel { Spacing = 8 };
        if (save != null)
            foreach (var tec in save.Treinadores.Where(t => t.Humano))
            {
                var eq = save.EquipaDoTreinador(tec);
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
                var s = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                s.Children.Add(Txt(tec.Nome, 15.5, null, FontWeight.Bold));
                if (eq != null)
                {
                    var cx = CaixaEquipa(eq, null, 13.5);
                    cx.HorizontalAlignment = HorizontalAlignment.Left;
                    s.Children.Add(cx);
                }
                else s.Children.Add(Txt(T("sem equipa"), 13, Fraco));
                g.Children.Add(s);
                if (eq != null)
                {
                    var t = tec;
                    var b = new CUi.Botao(T("Trocar"), false, () => TrocarEquipa(t)) { VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(b, 1);
                    g.Children.Add(b);
                }
                p.Children.Add(g);
            }
        treinadores.Content = p;
        cartaoTreinadores.IsVisible = p.Children.Count > 0;
    }

    void TrocarEquipa(TreinadorEditavel tec)
    {
        folha = true;
        try
        {
            if (save == null) return;
            if (Aplicar() is { } erro) { Mensagem(T("Valor fora do limite"), erro); return; }
            var origem = save.EquipaDoTreinador(tec);
            if (origem == null) return;
            var destinos = equipas.Where(e => e != origem && e.PodeTerHumano).ToList();
            if (destinos.Count == 0) { Mensagem(T("Trocar de equipa"), T("Este jogo não tem divisões: não dá para saber que equipas podem ter um treinador humano.")); return; }
            int escolhido = 0;
            var efeito = Txt("", 13, Fraco, FontWeight.Normal, true);
            void Atualiza()
            {
                var d = destinos[escolhido];
                efeito.Text = (d.Treinador is { } o ? T("{0} vai para {1}.", o.Nome, origem.NomeExibido) + " " : "")
                    + T("Na história do treinador fica \"Promovido para o {0}\", como num convite aceito no jogo.", d.NomeExibido) + " "
                + T("As duas equipas ficam com moral 10, como numa chicotada psicológica. Só aparecem equipas das divisões (no Distrital o jogo quebra).");
            }
            var linhas = destinos.Select(e =>
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
                var cx = CaixaEquipa(e, null, 14.5);
                cx.HorizontalAlignment = HorizontalAlignment.Left;
                g.Children.Add(cx);
                var dv = Txt(e.Divisao, 12.5, Fraco);
                Grid.SetColumn(dv, 1);
                g.Children.Add(dv);
                return (Control)g;
            }).ToList();
            var (lista, marcar) = CUi.ListaEscolha(linhas, i => { escolhido = i; Atualiza(); });
            marcar(0);
            Atualiza();
            var rolar = new ScrollViewer { Content = lista, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, MaxHeight = Math.Max(200, Bounds.Height * 0.42) };
            var p = new StackPanel { Spacing = 10 };
            p.Children.Add(Txt(T("Equipa atual: {0}", origem.NomeExibido), 15, null, FontWeight.Bold, true));
            p.Children.Add(rolar);
            p.Children.Add(efeito);
            CUi.Folha? f = null;
            void Trocar()
            {
                var d = destinos[escolhido];
                save.TrocarEquipa(tec, d);
                equipa = d;
                Fechar(f!);
                MostrarTudo();
                Mudou();
            }
            f = new CUi.Folha(T("Trocar de equipa"), new Border { Padding = new Thickness(14, 6, 14, 10), Child = p },
                Botoes((T("Cancelar"), false, () => Fechar(f!)), (T("Trocar"), true, Trocar)), sub: tec.Nome, rolar: false, fechar: () => Fechar(f!));
            f.AoVoltar = () => Fechar(f);
            Abrir(f);
        }
        finally { folha = false; }
    }

    // os botoes de baixo das folhas (no Retro, os TBitBtn com o glifo do computador)
    static Control Botoes(params (string Texto, bool Principal, Action Acao)[] bs)
    {
        var l = new List<Control>();
        foreach (var (t, pr, a) in bs)
        {
            string? glifo = !R ? null : t == T("OK") ? "Botoes/BBOK.png" : t == T("Cancelar") ? "Botoes/BBCANCEL.png" : null;
            l.Add(R ? CR95.Botao(t, glifo, pr, a, false) : new CUi.Botao(t, pr, a));
        }
        return CUi.Botoes(l.ToArray());
    }

    // ---------------------------------------------------------------- ficha do jogador
    void Ficha(JogadorEditavel j, Action? fechou = null)
    {
        folha = true;
        try
        {
            if (equipa == null) return;
            var eq = equipa;
            int pos = Math.Clamp(j.Posicao, 0, 3);

            TextBlock faixaForca = Faixa();
            TextBox nota = null!;
            var (cf, forca) = Numero(j.Forca.ToString(), (c, d) =>
            {
                int max = LimitesSave.ForcaMaximaNoElenco(eq.Jogadores, j, NotaAgora(), EstrelaAgora());
                if (E.LerInteiro(c.Text, long.MinValue / 2, long.MaxValue / 2, out var v)) c.Text = Math.Clamp(v + d, LimitesSave.ForcaMin, max >= 1 ? max : LimitesSave.ForcaMax).ToString();
            });
            TextBox Num(int valor, long min, long max, int passo, out Control campo)
            {
                var (c, t) = Numero(valor.ToString(), (cx, d) => { if (E.LerInteiro(cx.Text, long.MinValue / 2, long.MaxValue / 2, out var v)) cx.Text = Math.Clamp(v + d * passo, min, max).ToString(); });
                campo = c;
                return t;
            }
            nota = Num(j.Nota, 1, 10, 1, out var cn);
            var lesao = Num(j.Lesao, 0, 10, 1, out var cl);
            var susp = Num(j.Suspensao, 0, LimitesSave.SuspensaoMax, 1, out var cs);
            var amar = Num(j.AmarelosAcumulados, 0, LimitesSave.AmarelosAcumuladosMax, 1, out var ca);
            var lesionado = Num(j.JogosLesionado, 0, LimitesSave.JogosLesionadoMax, 1, out var cle);
            var (csal, salario) = Numero(E.Num(j.Salario), (c, d) => PassoInteiro(c, d * 500, LimitesSave.SalarioMin, LimitesSave.SalarioMax));
            int comp = Math.Clamp(j.Comportamento, 0, 5);
            var (escComp, _) = CUi.Escolha(LimitesSave.Comportamentos.Select(x => T(x)).ToList(), comp, i => comp = i, 100);

            var estrela = Txt("", 15, null, FontWeight.Bold);
            var maximo = Txt("", 14, Aviso, FontWeight.Bold, true);
            var aviso = Txt("", 13, Aviso, FontWeight.Normal, true);
            var erroTxt = Txt("", 14, CUi.Erro, FontWeight.Bold, true);
            erroTxt.IsVisible = false;

            var fraco = Fraco;
            int NotaAgora() => int.TryParse((nota.Text ?? "").Trim(), out var n) ? Math.Clamp(n, 1, 10) : j.Nota;
            bool EstrelaAgora() => NotaAgora() != j.Nota ? LimitesSave.TemEstrela(j.Posicao, NotaAgora()) : j.Estrela;
            void Atualiza()
            {
                bool est = EstrelaAgora();
                estrela.Text = est ? "✱ " + T("Estrela") : T("Sem estrela");
                estrela.Foreground = est ? (R ? W95.Marinho : TemaVerde.Ouro) : fraco;
                int max = LimitesSave.ForcaMaximaNoElenco(eq.Jogadores, j, NotaAgora(), est);
                faixaForca.Text = max >= 1 ? T("1 a {0} (normal até {1})", E.Num(max), LimitesSave.ForcaNormal) : T("o plantel já passa do limite");
                maximo.Text = max >= 1 ? T("Força máxima neste plantel: {0}", E.Num(max)) : T("O resto do plantel já passa de um limite: diminua a força de outros jogadores.");
                aviso.Text = E.LerInteiro(forca.Text, 1, int.MaxValue, out var f) && f > LimitesSave.ForcaNormal
                    ? T("Força {0} é bem acima do normal (1 a {1}).", f, LimitesSave.ForcaNormal) : "";
                aviso.IsVisible = aviso.Text.Length > 0;
            }
            nota.TextChanged += (_, _) => Atualiza();
            forca.TextChanged += (_, _) => Atualiza();
            // (o erro do OK sai quando um valor muda)
            foreach (var t in new[] { forca, salario, nota, lesao, susp, amar, lesionado }) t.TextChanged += (_, _) => erroTxt.IsVisible = false;
            Atualiza();

            var p = new StackPanel { Spacing = 8 };
            var topo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            if (CUi.Bandeira(j.Pais, 22) is { } bb) topo.Children.Add(bb);
            topo.Children.Add(estrela);
            topo.Children.Add(Txt(T("Jogos {0} · Golos {1}", j.Jogos, j.Golos), 14, Fraco));
            p.Children.Add(topo);
            p.Children.Add(Txt(T("Estrela (✱): médio ou avançado com nota 8 ou mais. Muda sozinha com a nota."), 12.5, Fraco, FontWeight.Normal, true));
            p.Children.Add(maximo);
            p.Children.Add(Campo(T("Força"), cf, faixaForca));
            p.Children.Add(aviso);
            p.Children.Add(Campo(T("Salário"), csal, Faixa(T("{0} a {1}", E.Num(LimitesSave.SalarioMin), E.Num(LimitesSave.SalarioMax)))));
            p.Children.Add(Campo(T("Nota"), cn, Faixa(T("1 a 10"))));
            p.Children.Add(Campo(T("Lesão"), cl, Faixa(T("0 = nunca, 10 = muito"))));
            p.Children.Add(Campo(T("Suspenso (S)"), cs, Faixa(T("jogos, 0 a {0}", LimitesSave.SuspensaoMax))));
            p.Children.Add(Campo(T("Amarelos (A)"), ca, Faixa(T("acumulados, 0 a {0}", LimitesSave.AmarelosAcumuladosMax))));
            p.Children.Add(Campo(T("Lesionado (L)"), cle, Faixa(T("jogos, 0 a {0}", LimitesSave.JogosLesionadoMax))));
            p.Children.Add(Txt(T("Comportamento"), 15, null, FontWeight.SemiBold));
            p.Children.Add(escComp);

            CUi.Folha? f = null;
            void Fecha() { Fechar(f!); fechou?.Invoke(); }
            void Ok()
            {
                var erro = E.ValidarFicha(eq, j, forca.Text, salario.Text, nota.Text, lesao.Text, susp.Text, amar.Text, lesionado.Text, out var v);
                if (erro != null) { erroTxt.Text = erro; erroTxt.IsVisible = true; return; }
                bool mudou = v.Forca != j.Forca || v.Salario != j.Salario || v.Nota != j.Nota || v.Lesao != j.Lesao || v.Suspensao != j.Suspensao
                    || v.Amarelos != j.AmarelosAcumulados || v.Lesionado != j.JogosLesionado || comp != Math.Clamp(j.Comportamento, 0, 5);
                Fecha();
                if (!mudou) return;
                if (v.Nota != j.Nota) j.Estrela = v.Estrela;
                j.Forca = v.Forca; j.Salario = v.Salario; j.Nota = v.Nota; j.Lesao = v.Lesao; j.Suspensao = v.Suspensao;
                j.AmarelosAcumulados = v.Amarelos; j.JogosLesionado = v.Lesionado;
                if (comp != Math.Clamp(j.Comportamento, 0, 5)) j.Comportamento = comp;
                Mudou();
                MostrarPlantel();
            }
            var rodape = new StackPanel { Spacing = 8, Children = { erroTxt, Botoes((T("Cancelar"), false, Fecha), (T("OK"), true, Ok)) } };
            f = new CUi.Folha(j.Nome, p, rodape, sub: T(NomePosicao[pos]) + " · " + j.Pais, icone: Selo(j.Posicao, 32), fechar: Fecha);
            Abrir(f);
        }
        finally { folha = false; }
    }

    // ---------------------------------------------------------------- folhas por cima
    // a folha sobe de baixo sobre o veu; o toque no veu (ou o Voltar) faz o que ela faz ao voltar
    void Abrir(CUi.Folha f)
    {
        var veu = new Border { Background = TemaVerde.Veu };
        veu.PointerPressed += (_, e) => { e.Handled = true; f.AoVoltar?.Invoke(); };
        f.MaxHeight = Math.Max(360, Bounds.Height * 0.92);
        f.Tag = veu;
        sobre.Children.Add(veu);
        sobre.Children.Add(f);
        sobre.IsVisible = true;
    }

    void Fechar(CUi.Folha f)
    {
        if (f.Tag is Control veu) sobre.Children.Remove(veu);
        sobre.Children.Remove(f);
        sobre.IsVisible = sobre.Children.Count > 0;
    }

    void Mensagem(string titulo, string texto)
    {
        CUi.Folha? f = null;
        f = CUi.Pergunta(titulo, texto, new (string, bool, Action)[] { (T("OK"), true, () => Fechar(f!)) }, () => Fechar(f!));
        Abrir(f);
    }

    // com alteracoes por gravar: pergunta antes de as perder (sempre = mesmo sem o alterado: os
    // campos mudados e ainda nao aplicados)
    void Confirmar(Action seguir, bool sempre = false)
    {
        if (!alterado && !(sempre && CamposMudaram())) { seguir(); return; }
        CUi.Folha? f = null;
        f = CUi.Pergunta(T("Alterações por gravar"), T("As alterações a este jogo ainda não foram gravadas. Descartar?"),
            new (string, bool, Action)[] { (T("Cancelar"), false, () => Fechar(f!)), (T("Descartar"), true, () => { Fechar(f!); alterado = false; seguir(); }) },
            () => Fechar(f!));
        Abrir(f);
    }
}
