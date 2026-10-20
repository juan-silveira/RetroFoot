using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using RetroFoot.Motor.Arquivos;
using static RetroFoot.App.Idioma;
using SEquipa = RetroFoot.Motor.Save.Equipa;

namespace RetroFoot.App;

// O Scout no celular (a versao em pe da FerramentaScout.cs): o jogo gravado, o nome a procurar,
// as quatro posicoes e os botoes Filtros, Ordenar e "?" em cima; embaixo os jogadores achados
// (de 50 em 50, "Mostrar mais"), cada um em duas linhas (a posicao, o nome, a equipa, a divisao,
// a situacao, a coluna da ordem e a forca). O toque abre a ficha numa folha: tudo o que a tabela
// do computador mostra, a equipa dele e o plantel (o toque num colega abre a ficha dele). Os
// filtros sao os mesmos do computador (pais, divisao, equipa, forca, nota, lesao,
// comportamento, valor e as quatro caixas); a ordem tambem, por qualquer coluna, e "Juntar
// colunas" faz o Shift+clique (mais um criterio; outro toque inverte, o terceiro tira). Le os
// dados com o SaveScout e o ScoutScore do computador. Alem dos saves da pasta de dados, abre
// um .e98 de fora pelo seletor de arquivos do sistema (o do Android tambem).
// Duas peles (CelularUi.cs): Moderno, os cartoes verdes e amarelos; Retro, o Windows 95 do
// celular (a caixa de lista branca afundada, o TComboBox, as caixas de marcar, as equipas nas
// cores delas).
static class FerramentaScoutCelular
{
    [ModuleInitializer]
    internal static void Registar() => Ferramentas.RegistrarCelular(Ferramentas.Scout, ctx => new ScoutCelular(ctx));
}

sealed class ScoutCelular : UserControl
{
    const int Pagina = 50;
    readonly Ferramentas.Contexto ctx;
    static string? ultimoSave;           // o save escolhido da ultima vez (nesta execucao)
    static (string Caminho, string Rotulo)? externo;   // o arquivo aberto de fora

    // as colunas da tabela do computador: titulo curto, nome inteiro, chave, maior primeiro?
    sealed record Coluna(string Id, string Titulo, string Nome, Func<LinhaScout, IComparable> Chave, bool Desc);
    readonly List<Coluna> colunas;
    sealed class Criterio { public Coluna C = null!; public bool Desc; }
    readonly List<Criterio> criterios = new();
    bool juntar;

    SaveScout? dados;
    string? arquivo;
    List<(string Caminho, string Rotulo)> saves = new();
    readonly Dictionary<string, string> nomesPaises = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> bosman = new(), plop = new();

    // os filtros (nota, lesao e comp: o indice da caixa do computador, 0 = qualquer)
    string nome = "";
    readonly bool[] posicoes = { true, true, true, true };
    string? pais;
    int? div;
    SEquipa? equipa;
    string fMin = "", fMax = "", vMax = "";
    int nota, lesao, comp;
    bool estrela, estrangeiros, disponiveis, semHumanas;

    List<LinhaScout> achados = new();
    int mostrar = Pagina;
    LinhaScout? aberto;

    // a tela
    readonly StackPanel lista = new() { Spacing = 10 };
    readonly TextBlock conta;
    readonly ContentControl seletor = new();
    readonly TextBox caixaNome;
    readonly Clicavel[] bPos = new Clicavel[4];
    readonly CUi.Botao bFiltros;
    readonly Panel camada = new() { IsVisible = false };   // as folhas por cima
    readonly ScrollViewer rolagem;
    Action? aoFiltrar;   // a folha dos filtros aberta: o "Ver N" do rodape

    static bool R => CUi.Retro;

    public ScoutCelular(Ferramentas.Contexto ctx)
    {
        this.ctx = ctx;
        CarregarListas();
        colunas = new()
        {
            new("pos", T("Pos"), T("Posição"), l => l.Pos, false),
            new("nome", T("Nome"), T("Nome"), l => l.Nome, false),
            new("estrela", "✱", T("Estrela"), l => l.J.Estrela != 0 ? 1 : 0, true),
            new("score", "Score", "Score", l => l.Score, true),
            new("impacto", T("Impacto"), T("Impacto"), l => l.Impacto, true),
            new("forca", T("Força"), T("Força"), l => (short)l.J.Forca, true),
            new("nota", T("Nota"), T("Nota"), l => (short)l.J.Nota, true),
            new("lesao", T("Lesão"), T("Lesão"), l => (short)l.J.Lesao, false),
            new("comp", T("Comport."), T("Comportamento"), l => (short)l.J.Comportamento, false),
            new("equipa", T("Equipa"), T("Equipa"), l => l.Equipa, false),
            new("div", T("Div."), T("Divisão"), l => l.Div, false),
            new("pais", T("País"), T("País"), l => NomePais(l.Pais), false),
            new("valor", T("Valor"), T("Valor"), l => l.Valor, true),
            new("salario", T("Salário"), T("Salário"), l => (int)l.J.Salario, true),
            new("jogos", T("Jogos"), T("Jogos"), l => l.Jogos, true),
            new("golos", T("Golos"), T("Golos"), l => l.Golos, true),
            new("lesoes", T("Lesões"), T("Lesões"), l => l.Lesoes, true),
            new("amar", T("Amar."), T("Amarelos na carreira"), l => l.Amarelos, true),
            new("expuls", T("Expuls."), T("Expulsões"), l => l.Expulsoes, true),
            new("sit", T("Sit."), T("Situação"), l => l.SitCurta, false),
        };
        OrdemPadrao();

        conta = CUi.Txt("", 13, R ? W95.Amarelo : CUi.Fraco, FontWeight.SemiBold, true);
        caixaNome = CUi.Caixa("", 30, T("Parte do nome"));
        caixaNome.TextChanged += (_, _) =>
        {
            var t = (caixaNome.Text ?? "").Trim();
            if (t == nome) return;
            nome = t;
            Filtrar();
        };
        bFiltros = new CUi.Botao(T("Filtros"), false, AbrirFiltros);

        rolagem = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new Border
            {
                Padding = new Thickness(12, 12, 12, 16), VerticalAlignment = VerticalAlignment.Top,
                Child = new StackPanel { Spacing = 12, Children = { Topo(), conta, lista } },
            },
        };
        Content = new Panel { Background = CUi.Fundo, Children = { rolagem, camada } };
        PintarPosicoes();
        ListarSaves();
        // Esc (o teclado): fecha a folha de cima, como o Voltar do Android
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && camada.Children.Count > 0 && CUi.Folha.Abertas.Count > 0)
            {
                e.Handled = true;
                CUi.Folha.Voltar();
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
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
    static string NomeDivisao(int d) => d < 4 ? T("{0}ª Divisão", d + 1) : d == 4 ? T("Distrital") : "—";
    static string DivCurta(int d) => d < 4 ? T("{0}ª", d + 1) : d == 4 ? T("Dist.") : "—";
    string Din(long v) => dados == null ? v.ToString() : FormatoDinheiro.Valor(dados.Moeda, v);

    // ---------------------------------------------------------------- as cores
    static IBrush H(string s) => new SolidColorBrush(Color.Parse(s));
    static readonly IBrush OuroRetro = H("#806000"), FaceRetro = H("#C0C0C0");
    static readonly BoxShadows Solto = BoxShadows.Parse("inset 1 1 0 0 #FFFFFF, inset -1 -1 0 0 #000000, inset 2 2 0 0 #DFDFDF, inset -2 -2 0 0 #808080");
    static readonly BoxShadows Premido = BoxShadows.Parse("inset 1 1 0 0 #000000, inset -1 -1 0 0 #FFFFFF, inset 2 2 0 0 #808080, inset -2 -2 0 0 #DFDFDF");
    static readonly BoxShadows Afundada = BoxShadows.Parse("inset 1 1 0 0 #A6A6A6, inset -1 -1 0 0 #FFFFFF, inset 2 2 0 0 #9E9E9E, inset -2 -2 0 0 #9E9E9E");
    static IBrush Ouro => R ? OuroRetro : TemaVerde.Destaque;
    static IBrush Forte => R ? W95.Marinho : TemaVerde.Destaque;
    static IBrush Bom => CUi.Bom;
    static IBrush Mau => R ? W95.Vermelho : TemaVerde.Vermelho;
    // o texto dentro das folhas (no Retro, o cinza da janela: preto)
    static IBrush TextoFolha => R ? W95.Preto : CUi.Texto;
    static IBrush FracoFolha => R ? H("#404040") : CUi.Fraco;

    // ---------------------------------------------------------------- o topo
    Control Topo()
    {
        var p = new StackPanel { Spacing = 10 };
        // o jogo gravado: a escolha, recarregar a pasta e abrir um arquivo de fora
        var linha = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto,8,Auto") };
        linha.Children.Add(seletor);
        var recarregar = Icone("relogio", T("Recarregar"), ListarSaves);
        Grid.SetColumn(recarregar, 2); linha.Children.Add(recarregar);
        var abrir = Icone("pasta", T("Abrir arquivo…"), AbrirArquivo);
        Grid.SetColumn(abrir, 4); linha.Children.Add(abrir);
        if (R) p.Children.Add(CR95.Txt(T("Jogo gravado:"), 14, W95.Preto));
        p.Children.Add(linha);
        if (R) p.Children.Add(CR95.Txt(T("Nome"), 14, W95.Preto));
        p.Children.Add(caixaNome);
        // as posicoes (ligadas e desligadas, como os botoes do computador)
        var pos = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 8 };
        for (int i = 0; i < 4; i++)
        {
            int k = i;
            var t = CUi.Txt(T(TextosMercado.Posicoes[i]), 13.5, null, FontWeight.Bold);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            // (o texto grande encolhe para caber: "Defensor" e "Atacante" inteiros)
            var b = new Clicavel { Child = new Viewbox { Child = t, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly }, MinHeight = 44, Padding = new Thickness(2, 4), CorFoco = null, CornerRadius = new CornerRadius(R ? 0 : 12), BorderThickness = new Thickness(R ? 0 : 1.5) };
            b.Clique += () => { posicoes[k] = !posicoes[k]; PintarPosicoes(); Filtrar(); };
            bPos[i] = b;
            Grid.SetColumn(b, i);
            pos.Children.Add(b);
        }
        p.Children.Add(pos);
        var ordenar = new CUi.Botao(T("Ordenar"), false, AbrirOrdem);
        var ajuda = new CUi.Botao("?", true, AbrirAjuda) { Padding = new Thickness(0, 8), MinWidth = 52 };
        var bs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*,10,Auto") };
        bFiltros.HorizontalAlignment = ordenar.HorizontalAlignment = HorizontalAlignment.Stretch;
        bs.Children.Add(bFiltros);
        Grid.SetColumn(ordenar, 2); bs.Children.Add(ordenar);
        Grid.SetColumn(ajuda, 4); bs.Children.Add(ajuda);
        p.Children.Add(bs);
        // no Retro, a janela cinza do computador; no Moderno, o cartao verde
        return CUi.Cartao_(null, p);
    }

    // um botao quadrado com o icone (recarregar, abrir)
    static Control Icone(string icone, string dica, Action clique)
    {
        var b = new CUi.Botao("", false, clique) { Width = 48, MinHeight = 48, Padding = new Thickness(0) };
        var ic = Ui.Icone(icone, 22, R ? W95.Preto : TemaVerde.Texto);
        ic.HorizontalAlignment = HorizontalAlignment.Center;
        ic.VerticalAlignment = VerticalAlignment.Center;
        b.Child = ic;
        ToolTip.SetTip(b, dica);
        return b;
    }

    void PintarPosicoes()
    {
        for (int i = 0; i < 4; i++)
        {
            var b = bPos[i];
            bool m = posicoes[i];
            var t = (TextBlock)((Viewbox)b.Child!).Child!;
            if (R)
            {
                b.Background = m ? EstiloFerramenta.CoresPosicao[i] : FaceRetro;
                b.BoxShadow = m ? Premido : Solto;
                t.Foreground = m ? W95.Branco : W95.Preto;
            }
            else
            {
                b.Background = m ? EstiloFerramenta.CoresPosicao[i] : TemaVerde.BotaoSecundario;
                b.BorderBrush = m ? EstiloFerramenta.CoresPosicao[i] : TemaVerde.Borda;
                t.Foreground = m ? Brushes.White : TemaVerde.TextoFraco;
            }
        }
    }

    // ---------------------------------------------------------------- os saves
    void ListarSaves()
    {
        var pasta = PastaSaves();
        saves = new();
        if (externo is { } x && File.Exists(x.Caminho)) saves.Add(x);
        try
        {
            if (Directory.Exists(pasta))
                foreach (var f in Directory.GetFiles(pasta, "*.e98", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive })
                             .Select(f => new FileInfo(f)).Where(f => f.Length > 0).OrderByDescending(f => f.LastWriteTimeUtc))
                    saves.Add((f.FullName, Path.GetFileNameWithoutExtension(f.Name).ToUpperInvariant() + "  ·  " + f.LastWriteTime.ToString("dd/MM/yyyy HH:mm")));
        }
        catch { }
        var antes = arquivo ?? ultimoSave;
        int i = antes != null ? saves.FindIndex(s => s.Caminho == antes) : -1;
        if (saves.Count == 0)
        {
            dados = null;
            arquivo = null;
            Seletor();
            Filtrar();
            return;
        }
        Carregar(saves[i >= 0 ? i : 0].Caminho);
    }

    string PastaSaves() => Environment.GetEnvironmentVariable("RETROFOOT_JOGOS") is { Length: > 0 } j ? j : Path.Combine(ctx.RaizDados, PastaDados.Saves);

    string RotuloAtual() => arquivo != null && saves.FirstOrDefault(s => s.Caminho == arquivo) is { Rotulo: { } r } ? r : "";

    // a escolha do jogo gravado: no Retro, o TComboBox do computador; no Moderno, a linha que
    // abre a lista numa folha
    void Seletor()
    {
        if (R)
        {
            var c = new CR95.Combo(saves.Select(s => s.Rotulo).ToList(), saves.FindIndex(s => s.Caminho == arquivo), 15);
            c.Mudou += i => { if (i >= 0 && i < saves.Count) Carregar(saves[i].Caminho); };
            seletor.Content = c;
            return;
        }
        var t = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(CUi.Txt(T("Jogo gravado"), 12, CUi.Fraco));
        t.Children.Add(CUi.Txt(saves.Count == 0 ? "—" : RotuloAtual(), 15, null, FontWeight.Bold));
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        g.Children.Add(t);
        var seta = CUi.Txt("▾", 16, CUi.Fraco);
        Grid.SetColumn(seta, 1); g.Children.Add(seta);
        seletor.Content = new CUi.Item(g, AbrirSaves) { MinHeight = 48 };
    }

    void AbrirSaves()
    {
        if (saves.Count == 0) { AbrirArquivo(); return; }
        Action fechar = null!;
        var linhas = saves.Select(s => (Control)CUi.Txt(s.Rotulo, 15, null, FontWeight.SemiBold)).ToList();
        var (l, marcar) = CUi.ListaEscolha(linhas, i => { fechar(); Carregar(saves[i].Caminho); });
        marcar(saves.FindIndex(s => s.Caminho == arquivo));
        fechar = Abrir(new CUi.Folha(T("Jogo gravado"), l, null, fechar: () => fechar()));
    }

    void Carregar(string arq)
    {
        try
        {
            dados = SaveScout.Ler(arq, bosman, plop);
            arquivo = arq;
            if (externo?.Caminho != arq) ultimoSave = arq;
        }
        catch (Exception ex)
        {
            dados = null;
            arquivo = arq;
            Seletor();
            achados = new();
            conta.Text = T("Não consegui ler o jogo gravado: {0}", ex.Message);
            lista.Children.Clear();
            return;
        }
        // os filtros que dependem do save (pais, divisao, equipa) voltam a "todos"
        pais = null; div = null; equipa = null;
        aberto = null;
        Seletor();
        Filtrar();
    }

    // um .e98 de fora (Downloads, Drive...): o seletor do sistema; copia para a pasta temporaria
    async void AbrirArquivo()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        try
        {
            var op = new FilePickerOpenOptions { Title = T("Abrir arquivo…"), AllowMultiple = false };
            // (no Android o filtro por extensao esconde os .e98: o tipo deles e desconhecido)
            if (!OperatingSystem.IsAndroid())
                op.FileTypeFilter = new[] { new FilePickerFileType("e98") { Patterns = new[] { "*.e98", "*.E98" } } };
            var fs = await top.StorageProvider.OpenFilePickerAsync(op);
            if (fs.Count == 0) return;
            var f = fs[0];
            var destino = Path.Combine(Path.GetTempPath(), "retrofoot-scout.e98");
            await using (var s = await f.OpenReadAsync())
            await using (var o = File.Create(destino))
                await s.CopyToAsync(o);
            externo = (destino, Path.GetFileNameWithoutExtension(f.Name).ToUpperInvariant() + "  ·  " + T("Arquivo"));
            arquivo = destino;
            ListarSaves();
        }
        catch (Exception ex) { ctx.Mensagem(T("Não consegui ler o jogo gravado: {0}", ex.Message)); }
    }

    // ---------------------------------------------------------------- filtrar e ordenar
    static int? Numero(string t)
    {
        var s = new string(t.Where(char.IsDigit).ToArray());
        return s.Length > 0 && long.TryParse(s, out var v) ? (int)Math.Min(v, int.MaxValue) : null;
    }

    static string SemAcento(string s) => new string(s.Normalize(NormalizationForm.FormD)
        .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());

    int FiltrosLigados() =>
        (pais != null ? 1 : 0) + (div != null ? 1 : 0) + (equipa != null ? 1 : 0) + (Numero(fMin) != null || Numero(fMax) != null ? 1 : 0)
        + (Numero(vMax) != null ? 1 : 0) + (nota > 0 ? 1 : 0) + (lesao > 0 ? 1 : 0) + (comp > 0 ? 1 : 0)
        + (estrela ? 1 : 0) + (estrangeiros ? 1 : 0) + (disponiveis ? 1 : 0) + (semHumanas ? 1 : 0);

    void Filtrar()
    {
        int n = FiltrosLigados();
        bFiltros.Texto = n > 0 ? T("Filtros") + " (" + n + ")" : T("Filtros");
        if (dados == null)
        {
            achados = new();
            lista.Children.Clear();
            conta.Text = saves.Count == 0 ? T("Nenhum jogo gravado em {0}.", PastaSaves()) : "";
            aoFiltrar?.Invoke();
            return;
        }
        var termo = SemAcento(nome);
        int? fmin = Numero(fMin), fmax = Numero(fMax), vmax = Numero(vMax);
        int notaMin = nota > 0 ? nota + 1 : 0;
        int lesaoMax = lesao > 0 ? lesao - 1 : 99;
        int compMax = comp > 0 ? comp - 1 : 99;
        var r = dados.Linhas.Where(l =>
            (termo.Length == 0 || SemAcento(l.Nome).Contains(termo, StringComparison.OrdinalIgnoreCase)) &&
            (l.Pos < 0 || posicoes[l.Pos]) &&
            (pais == null || l.Pais == pais) && (div == null || l.Div == div) && (equipa == null || l.E == equipa) &&
            (fmin == null || (short)l.J.Forca >= fmin) && (fmax == null || (short)l.J.Forca <= fmax) &&
            (vmax == null || l.Valor <= vmax) &&
            (short)l.J.Nota >= notaMin && (short)l.J.Lesao <= lesaoMax && (short)l.J.Comportamento <= compMax &&
            (!estrela || l.J.Estrela != 0) && (!estrangeiros || l.Estrangeiro) &&
            (!disponiveis || !l.Indisponivel) && (!semHumanas || !l.Humana)).ToList();
        IOrderedEnumerable<LinhaScout>? o = null;
        foreach (var c in criterios)
        {
            var k = c.C.Chave;
            o = o == null ? (c.Desc ? r.OrderByDescending(k) : r.OrderBy(k)) : (c.Desc ? o.ThenByDescending(k) : o.ThenBy(k));
        }
        achados = (o ?? r.OrderBy(x => 0)).ThenByDescending(x => (short)x.J.Forca).ThenByDescending(x => (short)x.J.Nota).ToList();
        mostrar = Pagina;
        conta.Text = T("{0} de {1} jogadores", achados.Count, dados.Linhas.Count) + " · " + TextoOrdem();
        Lista();
        aoFiltrar?.Invoke();
    }

    string TextoOrdem() => string.Join(", ", criterios.Select(c => (c.Desc ? "▼ " : "▲ ") + c.C.Nome));

    void OrdemPadrao()
    {
        criterios.Clear();
        criterios.Add(new Criterio { C = colunas.First(c => c.Id == "forca"), Desc = true });
    }

    // o clique no titulo do computador (juntar: o Shift+clique)
    void Ordenar(Coluna c)
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
        if (criterios.Count == 0) OrdemPadrao();
        Filtrar();
    }

    // ---------------------------------------------------------------- a lista
    void Lista()
    {
        lista.Children.Clear();
        if (dados == null) return;
        if (achados.Count == 0)
        {
            lista.Children.Add(CUi.Cartao_(null, CUi.Txt(T("Nenhum jogador encontrado."), 15, R ? W95.Preto : CUi.Fraco, FontWeight.Normal, true)));
            return;
        }
        var linhas = new List<Control>();
        int n = Math.Min(mostrar, achados.Count);
        for (int i = 0; i < n; i++) linhas.Add(Linha(achados[i]));
        lista.Children.Add(CUi.Grupo(linhas));
        if (n < achados.Count)
            lista.Children.Add(new CUi.Botao(T("Mostrar mais ({0})", achados.Count - n), false, () => { mostrar += Pagina; Lista(); }) { HorizontalAlignment = HorizontalAlignment.Stretch });
    }

    // a coluna que vai ao lado da forca: a da ordem (sem ela, ou se e a forca, o Score)
    Coluna ColunaLado()
    {
        var c = criterios[0].C;
        return c.Id is "forca" or "nome" or "pos" or "equipa" or "div" or "pais" or "estrela" ? colunas.First(x => x.Id == "score") : c;
    }

    (string Texto, IBrush? Cor) Valor(string id, LinhaScout l)
    {
        var j = l.J;
        return id switch
        {
            "score" => (l.Score.ToString(), Ouro),
            "impacto" => (ScoutScore.TextoImpacto(l.Impacto), l.Impacto > 0.004 ? Bom : l.Impacto < -0.004 ? Mau : null),
            "forca" => (((short)j.Forca).ToString(), null),
            "nota" => (((short)j.Nota).ToString(), null),
            "lesao" => (((short)j.Lesao).ToString(), null),
            "comp" => (TextosMercado.Comportamento(j), null),
            "valor" => (Din(l.Valor), null),
            "salario" => (Din((int)j.Salario), null),
            "jogos" => (l.Jogos.ToString(), null),
            "golos" => (l.Golos.ToString(), null),
            "lesoes" => (l.Lesoes.ToString(), null),
            "amar" => (l.Amarelos.ToString(), null),
            "expuls" => (l.Expulsoes.ToString(), null),
            "sit" => (l.SitCurta, l.Indisponivel || l.Estrangeiro ? Mau : Bom),
            _ => ("", null),
        };
    }

    // a posicao na cor dela (G amarelo, D azul, M verde, A vermelho)
    static Control Pos(int pos, double tam = 30, bool longo = false)
    {
        var t = CUi.Txt(pos >= 0 ? (longo ? T(TextosMercado.Posicoes[pos]) : Posicao(pos)) : "?", longo ? 13 : 14, Brushes.White, FontWeight.Bold);
        t.Tag = "fixo";
        t.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border
        {
            Background = pos >= 0 ? EstiloFerramenta.CoresPosicao[pos] : Brushes.Gray, CornerRadius = new CornerRadius(R ? 0 : 7),
            MinWidth = tam, Height = tam, Padding = new Thickness(longo ? 8 : 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = t,
            BorderBrush = R ? W95.Preto : null, BorderThickness = new Thickness(R ? 1 : 0),
        };
    }

    Control Linha(LinhaScout l)
    {
        var j = l.J;
        bool sel = R && l == aberto;
        IBrush? tx = sel ? W95.Branco : null;
        IBrush fraco = sel ? W95.Branco : CUi.Fraco;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,10,*,Auto,Auto") };
        g.Children.Add(Pos(l.Pos));
        var meio = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var l1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (CUi.Bandeira(l.Pais, 18) is { } b) l1.Children.Add(b);
        l1.Children.Add(CUi.Txt(l.Nome, 15.5, tx, FontWeight.Bold));
        if (j.Estrela != 0) l1.Children.Add(CUi.Txt("✱", 15, sel ? W95.Branco : Ouro, FontWeight.Bold));
        meio.Children.Add(l1);
        var l2 = new FilaCorta();
        l2.Children.Add(NomeEquipa(l.E, l.Equipa, 12.5, l.Humana, fraco));
        l2.Children.Add(CUi.Txt("· " + DivCurta(l.Div), 12.5, fraco));
        var sit = l.SitCurta;
        if (sit.Length > 0) l2.Children.Add(CUi.Txt("· " + sit, 12.5, sel ? W95.Branco : l.Indisponivel || l.Estrangeiro ? Mau : Bom, FontWeight.SemiBold));
        meio.Children.Add(l2);
        Grid.SetColumn(meio, 2); g.Children.Add(meio);
        // a coluna da ordem: o titulo pequeno e o valor
        var c = ColunaLado();
        var (v, cor) = Valor(c.Id, l);
        var lado = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), MaxWidth = 120 };
        var tt = CUi.Txt(c.Titulo, 11, fraco);
        tt.HorizontalAlignment = HorizontalAlignment.Right;
        var tv = CUi.Txt(v, 14.5, sel ? W95.Branco : cor, FontWeight.SemiBold);
        tv.HorizontalAlignment = HorizontalAlignment.Right;
        lado.Children.Add(tt);
        lado.Children.Add(tv);
        Grid.SetColumn(lado, 3); g.Children.Add(lado);
        var f = CUi.Txt(((short)j.Forca).ToString(), 19, sel ? W95.Branco : Forte, FontWeight.Bold);
        f.TextAlignment = TextAlignment.Right;
        f.MinWidth = 42;
        f.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(f, 4); g.Children.Add(f);
        var t = CUi.Toque(g, () => AbrirJogador(l), 58);
        if (sel) t.Background = MercadoRetro.Selecao;
        return t;
    }

    // ---------------------------------------------------------------- as folhas
    // uma folha por cima da tela (o veu escurece o resto; o toque nele fecha); devolve o fechar
    Action Abrir(CUi.Folha f)
    {
        var veu = new Panel { Background = TemaVerde.Veu };
        bool fechada = false;
        void Fechar()
        {
            if (fechada) return;
            fechada = true;
            camada.Children.Remove(veu);
            camada.IsVisible = camada.Children.Count > 0;
        }
        f.AoVoltar ??= Fechar;
        f.HorizontalAlignment = HorizontalAlignment.Stretch;
        f.VerticalAlignment = VerticalAlignment.Bottom;
        veu.SizeChanged += (_, e) => f.MaxHeight = e.NewSize.Height * 0.92;
        veu.PointerPressed += (_, e) => { if (e.Source == veu) { e.Handled = true; Fechar(); } };
        veu.Children.Add(f);
        camada.Children.Add(veu);
        camada.IsVisible = true;
        return Fechar;
    }

    // ---------------------------------------------------------------- o jogador
    void AbrirJogador(LinhaScout l)
    {
        if (dados == null) return;
        var antes = aberto;
        aberto = l;
        if (R && antes != l) Lista();
        var j = l.J;
        var corpo = new StackPanel { Spacing = 12 };

        // o jogador: a posicao, o pais e todas as colunas
        var cab = new StackPanel { Spacing = 8 };
        var pp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        pp.Children.Add(Pos(l.Pos, 28, true));
        if (CUi.Bandeira(l.Pais, 22) is { } b) pp.Children.Add(b);
        pp.Children.Add(CUi.Txt(NomePais(l.Pais) + " (" + l.Pais + ")", 14.5, TextoFolha));
        cab.Children.Add(pp);
        var ls = new List<Control>
        {
            Par("Score", l.Score.ToString(), Ouro),
            Par(T("Impacto"), ScoutScore.TextoImpacto(l.Impacto) + " " + T("pontos por época"), l.Impacto > 0.004 ? Bom : l.Impacto < -0.004 ? Mau : null),
            Par(T("Força"), ((short)j.Forca).ToString(), Forte),
            Par(T("Nota"), ((short)j.Nota).ToString()),
            Par(T("Lesão"), ((short)j.Lesao).ToString()),
            Par(T("Comportamento"), TextosMercado.Comportamento(j)),
            Par(T("Valor"), Din(l.Valor)),
            Par(T("Salário"), Din((int)j.Salario)),
            Par(T("Jogos"), l.Jogos.ToString()),
            Par(T("Golos"), l.Golos.ToString()),
            Par(T("Lesões"), l.Lesoes.ToString()),
            Par(T("Amarelos na carreira"), l.Amarelos.ToString()),
            Par(T("Expulsões"), l.Expulsoes.ToString()),
        };
        var sit = new List<string>();
        if ((short)j.Suspensao > 0) sit.Add(T("suspenso por {0} jogos", (short)j.Suspensao));
        if ((short)j.JogosLesao > 0) sit.Add(T("lesionado por {0} jogos", (short)j.JogosLesao));
        if (l.Acumulados == 1) sit.Add(T("1 amarelo acumulado"));
        else if (l.Acumulados > 1) sit.Add(T("{0} amarelos acumulados", l.Acumulados));
        if (l.Estrangeiro) sit.Add(T("conta como estrangeiro"));
        else if (l.Situacao.Length > 0) sit.Add(l.Situacao);
        ls.Add(Par(T("Situação"), sit.Count == 0 ? T("disponível") : string.Join(", ", sit), sit.Count == 0 ? Bom : l.Indisponivel || l.Estrangeiro ? Mau : null));
        cab.Children.Add(Grade(ls));
        corpo.Children.Add(cab);

        // a equipa: as cores dela, o pais, a divisao, o treinador, o dinheiro
        var e = l.E;
        var (letra, fundo) = TextosMercado.Cores(e);
        corpo.Children.Add(Secao(T("Equipa")));
        var eq = new StackPanel { Spacing = 6 };
        var faixa = new Border
        {
            Background = fundo, CornerRadius = new CornerRadius(R ? 0 : 10), Padding = new Thickness(12, 8),
            BorderBrush = R ? W95.Preto : TemaVerde.Borda, BorderThickness = new Thickness(1),
        };
        var fx = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (!R) fx.Children.Add(CUi.Escudo(e, 30));
        var nt = CUi.Txt(SaveScout.Texto(e.NomeCompleto), 16, letra, FontWeight.Bold);
        nt.Tag = "fixo";
        fx.Children.Add(nt);
        faixa.Child = fx;
        eq.Children.Add(faixa);
        var treinador = dados.S.Treinadores.FirstOrDefault(t => t.ID == e.Treinador);
        var pe = SaveScout.Texto(e.Pais).ToUpperInvariant();
        var info = new List<Control>
        {
            Par(T("País"), NomePais(pe)),
            Par(T("Divisão"), NomeDivisao(l.Div)),
        };
        if (treinador != null) info.Add(Par(T("Treinador"), SaveScout.Texto(treinador.Nome) + (dados.Humanas.Contains(e.ID) ? " (" + T("humano") + ")" : "")));
        info.Add(Par(T("Dinheiro"), Din((int)e.Dinheiro)));
        eq.Children.Add(Grade(info));
        corpo.Children.Add(eq);

        // o plantel: o toque num colega abre a ficha dele
        var plantel = dados.Linhas.Where(x => x.E == e).OrderBy(x => x.Pos).ThenByDescending(x => (short)x.J.Forca).ToList();
        corpo.Children.Add(Secao(T("Plantel") + " · " + T("{0} jogadores", plantel.Count)));
        Action fechar = null!;
        var linhas = new List<Control>();
        foreach (var x in plantel)
        {
            bool eu = x == l;
            IBrush? c = eu && R ? W95.Branco : null;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,10,*,Auto,Auto") };
            g.Children.Add(Pos(x.Pos, 26));
            var n = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            n.Children.Add(CUi.Txt(x.Nome, 15, c, eu ? FontWeight.Bold : FontWeight.Medium));
            if (x.J.Estrela != 0) n.Children.Add(CUi.Txt("✱", 14, c ?? Ouro, FontWeight.Bold));
            Grid.SetColumn(n, 2); g.Children.Add(n);
            var sc = CUi.Txt(x.Score.ToString(), 13, c ?? Ouro, FontWeight.SemiBold);
            sc.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(sc, 3); g.Children.Add(sc);
            var f = CUi.Txt(((short)x.J.Forca).ToString(), 17, c ?? Forte, FontWeight.Bold);
            f.MinWidth = 36; f.TextAlignment = TextAlignment.Right;
            Grid.SetColumn(f, 4); g.Children.Add(f);
            var xx = x;
            var t = CUi.Toque(g, eu ? null : () => { fechar(); AbrirJogador(xx); }, 46);
            if (eu) t.Background = R ? MercadoRetro.Selecao : TemaVerde.Selecao;
            linhas.Add(t);
        }
        corpo.Children.Add(CUi.Grupo(linhas));
        var bFechar = new CUi.Botao(T("Fechar"), true, () => fechar()) { HorizontalAlignment = HorizontalAlignment.Stretch };
        fechar = Abrir(new CUi.Folha(l.Nome + (j.Estrela != 0 ? " ✱" : ""), corpo, bFechar, SaveScout.Texto(e.NomeCurto), fechar: () => fechar()));
    }

    // o nome da equipa: no Retro, a caixa nas cores dela; no Moderno, o escudo e o nome (que
    // corta com as reticencias quando nao cabe)
    static Control NomeEquipa(SEquipa e, string nome, double tam, bool negrito, IBrush cor)
    {
        if (R) return CUi.NomeEquipa(e, nome, tam, negrito);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,6,*"), VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(CUi.Escudo(e, tam + 6));
        var t = CUi.Txt(nome, tam, cor, negrito ? FontWeight.Bold : FontWeight.Medium);
        Grid.SetColumn(t, 2); g.Children.Add(t);
        return g;
    }

    // uma linha rotulo / valor dentro da folha
    static Control Par(string rotulo, string valor, IBrush? cor = null)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*"), MinHeight = 30 };
        g.Children.Add(CUi.Txt(rotulo, 14, FracoFolha));
        var v = CUi.Txt(valor, 15, cor ?? TextoFolha, FontWeight.SemiBold, true);
        v.TextAlignment = TextAlignment.Right;
        v.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(v, 2); g.Children.Add(v);
        return g;
    }

    // as linhas num quadro (Moderno: o cartao com os fios; Retro: o TBevel do computador)
    static Control Grade(IEnumerable<Control> linhas)
    {
        var p = new StackPanel();
        foreach (var l in linhas) p.Children.Add(l);
        if (R) return CR95.Bevel(p, new Thickness(10, 6));
        return new Border { Background = TemaVerde.CartaoHub, BorderBrush = TemaVerde.Borda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 6), Child = p };
    }

    static Control Secao(string s)
    {
        var t = CUi.SecaoFolha(s);
        t.Margin = new Thickness(2, 4, 0, 0);
        return t;
    }

    // ---------------------------------------------------------------- a ajuda
    void AbrirAjuda()
    {
        var p = new StackPanel { Spacing = 14 };
        foreach (var (t, x) in ScoutScore.Ajuda())
        {
            var b = new StackPanel { Spacing = 3 };
            b.Children.Add(CUi.Txt(t, 15.5, R ? W95.Marinho : TemaVerde.Destaque, FontWeight.Bold, true));
            b.Children.Add(CUi.Txt(x, 14, TextoFolha, FontWeight.Normal, true));
            p.Children.Add(b);
        }
        Action fechar = null!;
        fechar = Abrir(new CUi.Folha(T("O que significa cada coluna"), p, new CUi.Botao(T("Fechar"), true, () => fechar()) { HorizontalAlignment = HorizontalAlignment.Stretch }, fechar: () => fechar()));
    }

    // ---------------------------------------------------------------- a ordem
    void AbrirOrdem()
    {
        var corpo = new ContentControl();
        void Montar()
        {
            var p = new StackPanel { Spacing = 10 };
            const string explica = "Cada toque junta a coluna como mais um critério; outro toque inverte, o terceiro tira.";
            if (R)
            {
                p.Children.Add(CR95.Marca(T("Juntar colunas"), juntar, v => { juntar = v; Montar(); }));
                p.Children.Add(CR95.Txt(T(explica), 13, W95.Cinzento));
            }
            else p.Children.Add(CUi.Grupo(new[] { CUi.Interruptor(T("Juntar colunas"), T(explica), juntar, v => { juntar = v; Montar(); }) }));
            var linhas = new List<Control>();
            foreach (var c in colunas)
            {
                int k = criterios.FindIndex(x => x.C == c);
                bool m = k >= 0;
                IBrush? cor = m && R ? W95.Branco : null;
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                g.Children.Add(CUi.Txt(c.Nome, 15, cor, m ? FontWeight.Bold : FontWeight.Normal));
                if (m)
                {
                    var s = (criterios[k].Desc ? "▼" : "▲") + (criterios.Count > 1 ? " " + (k + 1) : "");
                    var a = CUi.Txt(s, 15, cor ?? (R ? W95.Marinho : TemaVerde.Destaque), FontWeight.Bold);
                    Grid.SetColumn(a, 1); g.Children.Add(a);
                }
                var cc = c;
                var t = CUi.Toque(g, () => { Ordenar(cc); Montar(); }, 46);
                if (m) t.Background = R ? MercadoRetro.Selecao : TemaVerde.Selecao;
                linhas.Add(t);
            }
            p.Children.Add(CUi.Grupo(linhas));
            corpo.Content = p;
        }
        Montar();
        Action fechar = null!;
        var padrao = new CUi.Botao(T("Padrão"), false, () => { OrdemPadrao(); Filtrar(); Montar(); });
        var ok = new CUi.Botao(T("OK"), true, () => fechar());
        fechar = Abrir(new CUi.Folha(T("Ordenar"), corpo, CUi.Botoes(padrao, ok), fechar: () => fechar()));
    }

    // ---------------------------------------------------------------- os filtros
    void AbrirFiltros()
    {
        var corpo = new ContentControl();
        void Montar() => corpo.Content = Filtros(Montar);
        Montar();
        Action fechar = null!;
        var limpar = new CUi.Botao(T("Limpar"), false, () => { LimparFiltros(); Montar(); });
        var ver = new CUi.Botao("", true, () => fechar());
        void Contar() => ver.Texto = T("Ver {0}", achados.Count);
        Contar();
        aoFiltrar = Contar;
        var f = new CUi.Folha(T("Filtros"), corpo, CUi.Botoes(limpar, ver), fechar: () => fechar());
        var fc = Abrir(f);
        fechar = () => { aoFiltrar = null; fc(); };
        f.AoVoltar = fechar;
    }

    void LimparFiltros()
    {
        nome = ""; caixaNome.Text = "";
        for (int i = 0; i < 4; i++) posicoes[i] = true;
        pais = null; div = null; equipa = null;
        fMin = fMax = vMax = "";
        nota = lesao = comp = 0;
        estrela = estrangeiros = disponiveis = semHumanas = false;
        OrdemPadrao();
        PintarPosicoes();
        Filtrar();
    }

    Control Filtros(Action refazer)
    {
        var p = new StackPanel { Spacing = R ? 4 : 14 };
        if (dados == null) return p;
        void Campo(string rotulo, Control c)
        {
            if (R) { p.Children.Add(CR95.Rotulado(rotulo, c)); return; }
            p.Children.Add(new StackPanel { Spacing = 8, Children = { CUi.SecaoFolha(rotulo), c } });
        }

        // o pais do jogador e a equipa: uma lista com a busca, noutra folha
        var porPais = dados.Linhas.GroupBy(l => l.Pais).OrderBy(g => NomePais(g.Key), StringComparer.CurrentCultureIgnoreCase).ToList();
        string tPais = pais == null ? T("Todos ({0})", dados.Linhas.Count) : NomePais(pais) + " (" + porPais.FirstOrDefault(g => g.Key == pais)?.Count() + ")";
        Campo(T("País do jogador"), Escolher(pais, tPais, () => EscolherNaLista(T("País do jogador"),
            new[] { ((string?)null, T("Todos ({0})", dados.Linhas.Count)) }.Concat(porPais.Select(g => ((string?)g.Key, $"{NomePais(g.Key)} ({g.Count()})"))).ToList(),
            pais, k => { pais = k; Filtrar(); refazer(); })));

        var divs = new List<int?> { null };
        divs.AddRange(dados.Linhas.Select(l => l.Div).Distinct().OrderBy(d => d).Select(d => (int?)d));
        Campo(T("Divisão"), Opcoes(divs.Select(d => d == null ? T("Todas") : R ? NomeDivisao(d.Value) : DivCurta(d.Value)).ToList(), divs.IndexOf(div), i => { div = divs[i]; Filtrar(); }));

        string tEq = equipa == null ? T("Todas") : SaveScout.Texto(equipa.NomeCurto);
        Campo(T("Equipa"), Escolher(equipa, tEq, () => EscolherNaLista(T("Equipa"),
            new[] { ((SEquipa?)null, T("Todas")) }.Concat(dados.Equipas.Select(e => ((SEquipa?)e, (dados.Humanas.Contains(e.ID) ? "★ " : "") + SaveScout.Texto(e.NomeCurto)))).ToList(),
            equipa, k => { equipa = k; Filtrar(); refazer(); })));

        // a forca (de - ate) e o valor maximo: o teclado de numeros
        var forca = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), ColumnSpacing = 8 };
        var cMin = NumeroCaixa(fMin, T("Mínimo"), s => { fMin = s; Filtrar(); });
        var cMax = NumeroCaixa(fMax, T("Máximo"), s => { fMax = s; Filtrar(); });
        forca.Children.Add(cMin);
        var ate = CUi.Txt("–", 16, TextoFolha);
        Grid.SetColumn(ate, 1); forca.Children.Add(ate);
        Grid.SetColumn(cMax, 2); forca.Children.Add(cMax);
        Campo(T("Força"), forca);

        var notas = new[] { T("Qualquer") }.Concat(Enumerable.Range(2, 9).Select(n => R ? T("{0} ou mais", n) : n + "+")).ToList();
        Campo(T("Nota"), Opcoes(notas, nota, i => { nota = i; Filtrar(); }));
        var lesoes = new[] { T("Qualquer") }.Concat(Enumerable.Range(0, 10).Select(n => R ? T("até {0}", n) : "≤ " + n)).ToList();
        Campo(T("Lesão"), Opcoes(lesoes, lesao, i => { lesao = i; Filtrar(); }));
        var comps = new[] { T("Qualquer"), T("Só Fair Play") }.Concat(TextosMercado.Comportamentos.Skip(1).Select(c => T("Até {0}", T(c)))).ToList();
        Campo(T("Comportamento"), Opcoes(comps, comp, i => { comp = i; Filtrar(); }));
        Campo(T("Valor até"), NumeroCaixa(vMax, T("Qualquer"), s => { vMax = s; Filtrar(); }));

        // as quatro caixas de marcar do computador
        var marcas = new (string Texto, bool Valor, Action<bool> Mudou)[]
        {
            (T("Só com estrela (✱)"), estrela, v => { estrela = v; Filtrar(); }),
            (T("Só estrangeiros (contam no limite)"), estrangeiros, v => { estrangeiros = v; Filtrar(); }),
            (T("Esconder suspensos e lesionados"), disponiveis, v => { disponiveis = v; Filtrar(); }),
            (T("Esconder as equipas humanas"), semHumanas, v => { semHumanas = v; Filtrar(); }),
        };
        if (R)
        {
            var m = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var (t, v, mu) in marcas) m.Children.Add(CR95.Marca(t, v, mu));
            p.Children.Add(m);
        }
        else p.Children.Add(CUi.Grupo(marcas.Select(x => CUi.Interruptor(x.Texto, null, x.Valor, x.Mudou))));
        return p;
    }

    // a escolha de uma opcao: no Retro, o TComboBox; no Moderno, as pilulas
    static Control Opcoes(IReadOnlyList<string> itens, int atual, Action<int> escolher)
    {
        if (R)
        {
            var c = new CR95.Combo(itens, Math.Max(0, atual), 15);
            c.Mudou += escolher;
            return c;
        }
        return CUi.Escolha(itens, Math.Max(0, atual), escolher).Painel;
    }

    // a caixa que abre uma lista (pais, equipa): no Retro, o TComboBox; no Moderno, a linha
    static Control Escolher(object? valor, string texto, Action abrir)
    {
        if (R)
        {
            // o mesmo desenho do CR95.Combo (a caixa branca afundada e o botao da seta), que aqui
            // abre a lista com a busca numa folha
            var t = CR95.Txt(texto, 15, W95.Preto, false, false);
            t.Margin = new Thickness(8, 0, 4, 0);
            var seta = new Border
            {
                Width = 32, Background = FaceRetro, BoxShadow = Solto, Margin = new Thickness(0, 2, 2, 2),
                Child = new Pintor((dc, sz) =>
                {
                    double cx = Math.Floor(sz.Width / 2), cy = Math.Floor(sz.Height / 2);
                    for (int i = 0; i < 6; i++) dc.FillRectangle(W95.Preto, new Rect(cx - 5.5 + i, cy - 3 + i, 11 - 2 * i, 1));
                }),
            };
            var gr = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            gr.Children.Add(t);
            Grid.SetColumn(seta, 1); gr.Children.Add(seta);
            var c = new Clicavel { Background = W95.Branco, BoxShadow = Afundada, MinHeight = 44, Padding = new Thickness(2, 0, 0, 0), Child = gr, CorFoco = null };
            c.Clique += abrir;
            return c;
        }
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        g.Children.Add(CUi.Txt(texto, 15, valor != null ? TemaVerde.Destaque : null, valor != null ? FontWeight.Bold : FontWeight.Medium));
        var s = CUi.Txt("›", 20, CUi.Fraco);
        Grid.SetColumn(s, 1); g.Children.Add(s);
        var it = new CUi.Item(g, abrir);
        if (valor != null) it.Marcar(true);
        return it;
    }

    static TextBox NumeroCaixa(string texto, string dica, Action<string> mudou)
    {
        var t = CUi.Caixa(texto, 10, dica);
        t.HorizontalContentAlignment = HorizontalAlignment.Center;
        TextInputOptions.SetContentType(t, TextInputContentType.Number);
        t.TextChanged += (_, _) => mudou(t.Text ?? "");
        return t;
    }

    // uma lista longa (paises, equipas) com a busca, numa folha por cima dos filtros
    void EscolherNaLista<TK>(string titulo, IReadOnlyList<(TK Chave, string Texto)> itens, TK atual, Action<TK> escolheu)
    {
        Action fechar = null!;
        // (a altura fica: a folha nao encolhe enquanto se escreve a busca)
        var corpo = new StackPanel { Spacing = 10, MinHeight = 520 };
        var busca = CUi.Caixa("", 30, T("Procurar"));
        var lista = new ContentControl();
        corpo.Children.Add(busca);
        corpo.Children.Add(lista);
        void Encher()
        {
            var q = SemAcento((busca.Text ?? "").Trim());
            var ks = new List<int>();
            var linhas = new List<Control>();
            for (int i = 0; i < itens.Count; i++)
            {
                if (i > 0 && q.Length > 0 && !SemAcento(itens[i].Texto).Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
                var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                if (itens[i].Chave is string cod) { if (CUi.Bandeira(cod, 22) is { } b) s.Children.Add(b); else s.Children.Add(new Border { Width = 22 }); }
                else if (itens[i].Chave is SEquipa e) s.Children.Add(R ? CUi.NomeEquipa(e, itens[i].Texto, 15) : CUi.Escudo(e, 26));
                if (itens[i].Chave is not SEquipa || !R) s.Children.Add(CUi.Txt(itens[i].Texto, 15, i == 0 ? CUi.Fraco : null, i == 0 ? FontWeight.SemiBold : FontWeight.Normal));
                linhas.Add(s);
                ks.Add(i);
            }
            var (l, marcar) = CUi.ListaEscolha(linhas, k => { fechar(); escolheu(itens[ks[k]].Chave); });
            int at = ks.FindIndex(k => EqualityComparer<TK>.Default.Equals(itens[k].Chave, atual));
            if (at >= 0) marcar(at);
            lista.Content = l;
        }
        busca.TextChanged += (_, _) => Encher();
        Encher();
        fechar = Abrir(new CUi.Folha(titulo, corpo, null, fechar: () => fechar()));
    }

    // uma fila de lado em que o primeiro (o nome da equipa) fica com o que sobra dos outros e e
    // cortado (com as reticencias), sem empurrar a divisao e a situacao para fora
    sealed class FilaCorta : Panel
    {
        const double Espaco = 6;

        protected override Size MeasureOverride(Size disponivel)
        {
            double resto = 0, alto = 0;
            for (int i = 1; i < Children.Count; i++)
            {
                Children[i].Measure(new Size(double.PositiveInfinity, disponivel.Height));
                resto += Children[i].DesiredSize.Width + Espaco;
                alto = Math.Max(alto, Children[i].DesiredSize.Height);
            }
            if (Children.Count == 0) return default;
            Children[0].Measure(new Size(Math.Max(0, disponivel.Width - resto), disponivel.Height));
            alto = Math.Max(alto, Children[0].DesiredSize.Height);
            return new Size(Children[0].DesiredSize.Width + resto, alto);
        }

        protected override Size ArrangeOverride(Size tamanho)
        {
            double x = 0;
            foreach (var c in Children)
            {
                c.Arrange(new Rect(x, 0, c.DesiredSize.Width, tamanho.Height));
                x += c.DesiredSize.Width + Espaco;
            }
            return tamanho;
        }
    }
}
