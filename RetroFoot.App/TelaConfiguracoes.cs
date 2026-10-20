using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using static RetroFoot.App.ConfigEstilo;
using static RetroFoot.App.Idioma;
using TRegras = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.App;

// As Configuracoes (as do launcher: versao, equipas por divisao, amarelos, patrocinios,
// estrangeiros e idioma; e o visual e o formato da tela), em quatro secoes:
//   Jogo       a versao (Original / Modificada) e as equipas por divisao;
//   Regras     cartoes amarelos, patrocinios, acrescimos, sobem e descem por divisao (K),
//              estrangeiros e o desempate (so leitura);
//   Aparencia  o visual (Original / Moderno) e o formato (4:3 / 16:9);
//   Idioma     portugues de Portugal (o original), do Brasil, ingles ou espanhol (Idioma.cs);
// No visual Original, uma janela do Windows 95 com as quatro abas (como a TOptionsDlg) no
// verde; no Moderno, o verde e amarelo do hub com as secoes a esquerda (e o resumo a direita quando ha
// largura). Na versao Original as adicoes aparecem desligadas e travadas; a escolha da
// Modificada fica guardada e volta quando ela e escolhida de novo (como o launcher).
//
// Com um jogo em curso (RetroFoot > Configuracoes... na tela da equipa), um aviso mostra as
// regras desse jogo: o N vem do save e o resto das opcoes com que foi aberto; o que mudar
// aqui vale da proxima vez que um jogo for aberto.
public sealed partial class TelaConfiguracoes : UserControl
{
    // true = salvou (o config.json foi gravado); false = cancelou
    public event Action<bool>? Fechou;

    readonly Configuracoes _cfg;      // a editada (copia)
    readonly Sessao? _emCurso;
    bool _fechou;
    Action? _atualizar;               // o do visual montado
    Action<Key, KeyModifiers>? _tecla;
    Action<string> _erro = _ => { };
    readonly string _idiomaInicial;   // o de antes de abrir (volta a ele no Cancelar)
    Notebook? _nb;                    // as abas do visual Original

    int IndiceIdioma => Math.Max(0, Array.IndexOf(Configuracoes.Idiomas, _cfg.Idioma));

    // o idioma muda na hora: a tela se refaz no idioma escolhido, na secao Idioma (no lugar
    // desta, com quem esperava o Fechou); Salvar grava, Cancelar volta ao de antes
    void TrocarIdioma(int i)
    {
        if (i < 0 || i >= Configuracoes.Idiomas.Length || Configuracoes.Idiomas[i] == _cfg.Idioma) return;
        _cfg.Idioma = Configuracoes.Idiomas[i];
        Idioma.Aplicar(_cfg.Idioma);
        var nova = new TelaConfiguracoes(_cfg, _emCurso, _idiomaInicial) { Fechou = Fechou };
        _fechou = true;
        switch (Parent)
        {
            case Decorator d: d.Child = nova; break;
            case ContentControl c: c.Content = nova; break;
            case Panel p: p.Children[p.Children.IndexOf(this)] = nova; break;
        }
    }

    static readonly string[] Secoes = { "Jogo", "Regras", "Aparência", "Idioma", "Dados" };

    public TelaConfiguracoes(Configuracoes? cfg = null, Sessao? emCurso = null) : this(cfg, emCurso, null) { }

    // idiomaInicial != null: refeita por TrocarIdioma (na secao Idioma; Cancelar volta a ele)
    TelaConfiguracoes(Configuracoes? cfg, Sessao? emCurso, string? idiomaInicial)
    {
        _cfg = (cfg ?? Configuracoes.Ler()).Copia();
        _emCurso = emCurso;
        _idiomaInicial = idiomaInicial ?? Idioma.Atual;
        Focusable = true;
        FocusAdorner = null;
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Loaded);
        // no celular: a lista das secoes e uma pagina por secao (TelaConfiguracoesCelular.cs)
        if (Celular.Ativo) { MontarCelular(idiomaInicial != null ? 3 : -1); return; }
        if (Visual.Moderno) MontarModerno(); else MontarClassico();
        _atualizar?.Invoke();
        if (idiomaInicial != null) { if (_nb != null) _nb.Atual = 3; else Secao(3); }
    }

    // RETROFOOT_TELA=configuracoes: a tela sozinha (Salvar grava no config.json de verdade)
    public static Control Demo()
    {
        var t = new TelaConfiguracoes();
        t.Fechou += salvou => Console.WriteLine("RETROFOOT-CONFIGURACOES " + (salvou ? "salvou " + Configuracoes.Arquivo : "cancelou"));
        return t;
    }

    // ---------------------------------------------------------------- textos (dos dois visuais)
    static string TextoPastaDados => T("teams (equipas em JSON, uma pasta por país), logos, saves, patches, flags, sponsors, country-groups, countries.tsv e referees.tsv. Arquivos Retrô (EQUIPAS, .EFT, COUNTRY.TXE, REFEREE.TXE, CTRGROUP) deixados lá são convertidos no próximo jogo novo.");

    static string DescOriginal => T("As regras de 1998: 8 equipas por divisão, só cartões vermelhos, sem patrocínios.");
    static string DescModificada => T("Nota e Risco de lesão, a tática 5-0-5, até 30 jogadores, desempate por vitórias e as regras que você escolher.");

    string AjudaEquipas()
    {
        int n = _cfg.N, min = _cfg.MinimoDeEquipas(n), k = _cfg.SobemDescemPara(n);
        var t = n == 8
            ? T("8 equipas como no Retrô: 14 rodadas; o jogo novo precisa de pelo menos {0} equipas nos países escolhidos.", min).TrimEnd('.')
            : T("{0} equipas: {1} rodadas; o jogo novo precisa de pelo menos {2} equipas nos países escolhidos", n, 2 * (n - 1), min);
        return t + (k != 2 ? T("; sobem e descem {0}.", k) : ".");
    }

    // as opcoes de "Sobem e descem por divisao": Automatico, 1, 2, 3 e 4 (o indice e o K; 0 = automatico)
    static string[] OpcoesSobem => new[] { T("Automático"), "1", "2", "3", "4" };

    static string AjudaSobemFixa => T("Entre as divisões e do Distrital para a 4ª divisão (por sorteio). Fica gravado no jogo novo e não muda depois. No máximo 4 e menos de metade da divisão.");

    // o K que vale com o N escolhido (e por que, se o escolhido passa do maximo)
    string AjudaSobem()
    {
        int n = _cfg.N, k = _cfg.SobemDescemPara(n), max = TRegras.MaximoSobemDescem(n);
        if (!_cfg.Modificada) return T("Na versão Retrô sobem e descem 2, como em 1998.");
        if (_cfg.SobemDescem == 0) return T("Automático: com {0} equipas por divisão sobem e descem {1} (2, ou 4 com 16 ou mais equipas).", n, k);
        if (_cfg.SobemDescem > max) return T("Com {0} equipas por divisão o máximo é {1} (menos de metade da divisão): valem {1}.", n, max);
        return T("Sobem e descem {0}; o jogo novo precisa de pelo menos {1} equipas.", k, _cfg.MinimoDeEquipas(n));
    }

    string Desempate() => _cfg.Modificada
        ? T("Pontos, vitórias, saldo de golos e golos marcados.")
        : T("Pontos, saldo de golos e golos marcados (como no Retrô).");

    string Nota() => _emCurso != null
        ? T("As mudanças valem da próxima vez que um jogo for aberto; o visual e o formato da tela, na hora.")
        : T("Valem para o próximo jogo novo; o visual e o formato da tela, na hora. Um jogo gravado mantém o número de equipas com que começou.");

    // as regras do jogo aberto: N do save; amarelos, 5-0-5, patrocinios e o resto das opcoes
    // com que a Sessao comecou
    static string RegrasDoJogo(Sessao s)
    {
        var r = s.J.M.Regras;
        var op = s.J.Op;
        bool mod = r != TRegras.Original;
        var partes = new List<string>
        {
            mod ? T("versão Atual") : T("versão Retrô"),
            T("{0} equipas por divisão ({1} rodadas)", r.N, r.Jornadas),
            r.CartoesAmarelos ? T("cartões amarelos ligados") : T("só cartões vermelhos"),
        };
        if (mod) partes.Add(op.Patrocinios ? T("patrocínios ligados") : T("patrocínios desligados"));
        if (mod) partes.Add(r.Acrescimos ? T("acréscimos ligados") : T("acréscimos desligados"));
        partes.Add(T("sobem e descem {0}", r.SobemDescem));
        var t = string.Join(" · ", partes);
        return char.ToUpper(t[0]) + t[1..] + ".";
    }

    // ---------------------------------------------------------------- Moderno
    readonly List<(Border Item, TextBlock Nome, Control Pagina)> _secoes = new();
    int _secao;
    ScrollViewer _rolagem = null!;

    void MontarModerno()
    {
        FontFamily = Inter;
        Background = TemaVerde.FundoTela;

        // as escolhas
        var versao = new EscolhaModerna(new (string, string?)[] { (T("Retrô"), DescOriginal), (T("Atual"), DescModificada) }, _cfg.Modificada ? 1 : 0, lado: true);
        var listaN = new List<(string, string?)>();
        foreach (var n in Configuracoes.OpcoesEquipas) listaN.Add((n.ToString(), null));
        var equipas = new EscolhaModerna(listaN, Math.Max(0, Array.IndexOf(Configuracoes.OpcoesEquipas, _cfg.EquipasPorDivisao)), chips: true);
        var ajudaEquipas = Ajuda("");
        var cartoes = Interruptor(_cfg.CartoesAmarelos);
        var patrocinios = Interruptor(_cfg.Patrocinios);
        var acrescimos = Interruptor(_cfg.Acrescimos);
        var cincoSubst = Interruptor(_cfg.CincoSubstituicoes);
        var noveRes = Interruptor(_cfg.NoveReservas);
        var sobem = new EscolhaModerna(Array.ConvertAll(OpcoesSobem, o => (o, (string?)null)), _cfg.SobemDescem, chips: true);
        var ajudaSobem = new TextBlock { FontSize = 13.5, Foreground = Texto, TextWrapping = TextWrapping.Wrap };
        var sobemBloco = new StackPanel { Spacing = 10, Children = { sobem, ajudaSobem, Ajuda(AjudaSobemFixa) } };
        var estrangeiros = new EscolhaModerna(new (string, string?)[]
        {
            (T("Realístico"), T("Até 5 por equipa; a Lei Bosman e os países de língua portuguesa não contam.")),
            (T("Liberado"), T("Sem limite: todos os países entram na lista da Lei Bosman.")),
        }, _cfg.EstrangeirosLiberados ? 1 : 0, lado: true);
        var desempate = new TextBlock { FontSize = 14, Foreground = Texto, TextWrapping = TextWrapping.Wrap };
        var visual = new EscolhaModerna(new (string, string?)[]
        {
            (T("Retrô"), T("O aspecto Retrô: janelas do Windows 95 no verde, sem logos.")),
            (T("Moderno"), T("Tema verde e amarelo, cartões e os logos das equipas e das ligas.")),
        }, _cfg.Moderno ? 1 : 0, lado: true, figura: i => new Previa(i == 0 ? Previa.Tipo.Original : Previa.Tipo.Moderno));
        var formato = new EscolhaModerna(new (string, string?)[]
        {
            ("4:3", T("Como o Retrô: as telas no formato de 800x600.")),
            ("16:9", T("Janela larga: a jornada com as divisões lado a lado e as telas aproveitando a largura.")),
        }, _cfg.Largo ? 1 : 0, lado: true, figura: i => new Previa(i == 0 ? Previa.Tipo.Quatro : Previa.Tipo.Dezesseis));
        var idioma = new EscolhaModerna(new (string, string?)[]
        {
            (Nomes[0], T("Os textos do jogo Retrô.")),
            (Nomes[1], T("Os textos adaptados para o Brasil.")),
            (Nomes[2], T("Os textos traduzidos para inglês.")),
            (Nomes[3], T("Os textos traduzidos para espanhol.")),
        }, IndiceIdioma);

        // Regras travadas na Original
        var travado = new Border
        {
            Background = FundoAviso, CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12),
        };
        var travadoTxt = new StackPanel { Spacing = 8 };
        travadoTxt.Children.Add(Titulo(T("Na versão Retrô as regras são as de 1998"), 14.5, Azul));
        travadoTxt.Children.Add(Ajuda(T("Cartões amarelos, patrocínios e acréscimos só existem na versão Atual. A escolha fica guardada para quando você usar a Modificada.")));
        var usarMod = Botao(T("Usar a versão Atual"), false, () => versao.Mover(1));
        usarMod.HorizontalAlignment = HorizontalAlignment.Left;
        travadoTxt.Children.Add(usarMod);
        travado.Child = travadoTxt;
        var adicoes = new StackPanel { Spacing = 16 };
        adicoes.Children.Add(LinhaInterruptor(T("Cartões amarelos"), T("Três amarelos acumulados suspendem por um jogo. Desligados: só vermelhos, como no Retrô."), cartoes));
        adicoes.Children.Add(Separador());
        adicoes.Children.Add(LinhaInterruptor(T("Patrocínios"), T("Propostas e contratos de patrocínio master ao longo da temporada."), patrocinios));
        adicoes.Children.Add(Separador());
        adicoes.Children.Add(LinhaInterruptor(T("Acréscimos"), T("De 1 a 5 minutos no fim do 1º tempo e de 1 a 10 no fim do 2º, com golos, cartões e lesões (45'+2'). Desligados: como no Retrô."), acrescimos));
        adicoes.Children.Add(LinhaInterruptor(T("5 substituições"), T("A regra da FIFA: até 5 substituições por jogo. Desligado: 3, como no Retrô. Na prorrogação da Taça, mais uma."), cincoSubst));
        adicoes.Children.Add(LinhaInterruptor(T("9 reservas"), T("Até 9 reservas no banco (20 relacionados). Desligado: 5, como no Retrô."), noveRes));
        var equipasBloco = new StackPanel { Spacing = 10, Children = { equipas, ajudaEquipas } };

        // as paginas
        var aviso = _emCurso == null ? null : AvisoJogo(_emCurso);
        var pJogo = Pagina(T("Jogo"), T("A versão do jogo e o tamanho das divisões."),
            aviso, Cartao(T("Versão do jogo"), null, versao), Cartao(T("Equipas por divisão"), Etiqueta(T("Atual"), Azul, AzulFundo), equipasBloco));
        var pRegras = Pagina(T("Regras"), T("O que vale dentro do jogo."),
            travado, Cartao(T("Adições"), null, adicoes),
            Cartao(T("Sobem e descem por divisão"), Etiqueta(T("Atual"), Azul, AzulFundo), sobemBloco),
            Cartao(T("Jogadores estrangeiros"), null, estrangeiros),
            Cartao(T("Desempate na classificação"), null, desempate));
        var pAparencia = Pagina(T("Aparência"), T("Como as telas são desenhadas. Muda na hora, mesmo com um jogo aberto."),
            Cartao(T("Visual"), null, visual), Cartao(T("Formato da tela"), null, formato));
        var pIdioma = Pagina(T("Idioma"), T("Os textos do jogo."), Cartao(T("Idioma do jogo"), null, idioma));
        // a pasta de dados (DadosJogo): o caminho e o botao que a abre
        var dados = new StackPanel { Spacing = 12 };
        dados.Children.Add(new SelectableTextBlock { Text = DadosJogo.Raiz, FontSize = 14, Foreground = Texto, TextWrapping = TextWrapping.Wrap });
        dados.Children.Add(Ajuda(TextoPastaDados));
        if (DadosJogo.PodeAbrirPasta)
        {
            var abrir = Botao(T("Abrir pasta de dados"), true, () => DadosJogo.AbrirPasta());
            abrir.HorizontalAlignment = HorizontalAlignment.Left;
            dados.Children.Add(abrir);
        }
        var pDados = Pagina(T("Dados"), T("Equipas, logos, saves e patches numa pasta que você abre no gestor de arquivos."),
            Cartao(T("Pasta de dados"), null, dados));
        var paginas = new Panel { Children = { pJogo, pRegras, pAparencia, pIdioma, pDados } };
        var conteudo = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MaxWidth = 1180 };
        conteudo.Children.Add(paginas);
        _rolagem = new ScrollViewer
        {
            Content = new Border { Padding = new Thickness(28, 22, 28, 28), Child = conteudo },
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        // a navegacao
        var nav = new StackPanel { Spacing = 4, Margin = new Thickness(12, 20, 12, 12) };
        var subt = new[] { "Versão e equipas", "Cartões, patrocínios, estrangeiros", "Visual e formato", "Idioma do jogo", "Pasta de dados" };
        var pags = new Control[] { pJogo, pRegras, pAparencia, pIdioma, pDados };
        for (int i = 0; i < Secoes.Length; i++)
        {
            int k = i;
            var nome = new TextBlock { Text = T(Secoes[i]), FontSize = 15, FontWeight = FontWeight.SemiBold };
            var sub = new TextBlock { Text = T(subt[i]), FontSize = 12, Foreground = TextoFraco, TextTrimming = TextTrimming.CharacterEllipsis };
            var item = new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 9), Cursor = new Cursor(StandardCursorType.Hand),
                BorderThickness = new Thickness(1), Child = new StackPanel { Spacing = 1, Children = { nome, sub } },
            };
            item.PointerPressed += (_, a) => { a.Handled = true; Secao(k); };
            _secoes.Add((item, nome, pags[i]));
            nav.Children.Add(item);
        }
        var navBorda = new Border { Background = FundoBarra, BorderBrush = Linha, BorderThickness = new Thickness(0, 0, 1, 0), Child = nav };

        // o resumo (so com largura)
        var resumoTxt = new StackPanel { Spacing = 10 };
        var resumo = new Border
        {
            Width = 290, Margin = new Thickness(24, 0, 0, 0), IsVisible = false, VerticalAlignment = VerticalAlignment.Top,
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    Cartao(T("Resumo"), null, resumoTxt),
                    _emCurso == null ? Ajuda(Nota()) : Ajuda(T("O jogo em curso mantém as regras com que foi aberto.")),
                },
            },
        };

        // o topo e o rodape
        // a bola da marca a esquerda do titulo, como no hub
        var bola = Logotipo.ImagemBola(44);
        bola.VerticalAlignment = VerticalAlignment.Center;
        var topo = new Border
        {
            Background = FundoBarra, BorderBrush = Linha, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(20, 12, 24, 12),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 14,
                Children =
                {
                    bola,
                    new StackPanel
                    {
                        Spacing = 2, VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            Titulo(T("Configurações"), 24),
                            Ajuda(T("Por padrão, o jogo como em 1998: as adições ficam desligadas até você ligar aqui."), 13),
                        },
                    },
                },
            },
        };
        var erro = new TextBlock { Foreground = Erro, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        _erro = m => erro.Text = m;
        var nota = Ajuda(Nota(), 12.5);
        nota.VerticalAlignment = VerticalAlignment.Center;
        var esquerda = new Panel { Children = { nota, erro } };
        var botoes = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right,
            Children = { Botao(T("Cancelar"), false, () => Fechar(false)), Botao(T("Salvar"), true, () => Salvar(_erro)) },
        };
        var rodapeGrade = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,Auto"), Margin = new Thickness(24, 12) };
        rodapeGrade.Children.Add(esquerda);
        Grid.SetColumn(botoes, 2);
        rodapeGrade.Children.Add(botoes);
        var rodape = new Border { BorderBrush = Linha, BorderThickness = new Thickness(0, 1, 0, 0), Background = FundoBarra, Child = rodapeGrade };

        Grid.SetColumn(resumo, 1);
        conteudo.Children.Add(resumo);
        var meio = new Grid { ColumnDefinitions = new ColumnDefinitions("210,*") };
        meio.Children.Add(navBorda);
        Grid.SetColumn(_rolagem, 1);
        meio.Children.Add(_rolagem);
        var corpo = new DockPanel();
        DockPanel.SetDock(topo, Dock.Top);
        corpo.Children.Add(topo);
        DockPanel.SetDock(rodape, Dock.Bottom);
        corpo.Children.Add(rodape);
        corpo.Children.Add(meio);
        Content = Escuro(corpo);

        // com largura sobrando, o resumo a direita e a nota sai do rodape
        SizeChanged += (_, e) =>
        {
            bool largo = e.NewSize.Width >= 1180;
            resumo.IsVisible = largo;
            nota.IsVisible = !largo;
            meio.ColumnDefinitions[0].Width = new GridLength(e.NewSize.Width < 860 ? 180 : 230);
        };

        // os eventos
        versao.Mudou += i => { _cfg.Modificada = i == 1; _atualizar!(); };
        equipas.Mudou += i => { _cfg.EquipasPorDivisao = Configuracoes.OpcoesEquipas[i]; _atualizar!(); };
        cartoes.IsCheckedChanged += (_, _) => { if (_cfg.Modificada) { _cfg.CartoesAmarelos = cartoes.IsChecked == true; _atualizar!(); } };
        patrocinios.IsCheckedChanged += (_, _) => { if (_cfg.Modificada) { _cfg.Patrocinios = patrocinios.IsChecked == true; _atualizar!(); } };
        acrescimos.IsCheckedChanged += (_, _) => { if (_cfg.Modificada) { _cfg.Acrescimos = acrescimos.IsChecked == true; _atualizar!(); } };
        cincoSubst.IsCheckedChanged += (_, _) => { if (_cfg.Modificada) { _cfg.CincoSubstituicoes = cincoSubst.IsChecked == true; _atualizar!(); } };
        noveRes.IsCheckedChanged += (_, _) => { if (_cfg.Modificada) { _cfg.NoveReservas = noveRes.IsChecked == true; _atualizar!(); } };
        sobem.Mudou += i => { if (_cfg.Modificada) { _cfg.SobemDescem = i; _atualizar!(); } };
        estrangeiros.Mudou += i => { _cfg.EstrangeirosLiberados = i == 1; _atualizar!(); };
        visual.Mudou += i => { _cfg.VisualTela = i == 1 ? Configuracoes.VisualModerno : Configuracoes.VisualClassico; _atualizar!(); };
        formato.Mudou += i => { _cfg.FormatoTela = i == 1 ? Configuracoes.Formato169 : Configuracoes.Formato43; _atualizar!(); };
        idioma.Mudou += i => TrocarIdioma(i);

        // na Original, as adicoes aparecem como no jogo original (8, desligados) e travadas;
        // os valores da Modificada ficam no _cfg
        _atualizar = () =>
        {
            bool mod = _cfg.Modificada;
            equipasBloco.IsEnabled = mod;
            equipasBloco.Opacity = mod ? 1 : 0.45;
            adicoes.IsEnabled = mod;
            adicoes.Opacity = mod ? 1 : 0.45;
            travado.IsVisible = !mod;
            equipas.Selecionado = mod ? Array.IndexOf(Configuracoes.OpcoesEquipas, _cfg.EquipasPorDivisao) : 0;
            cartoes.IsChecked = mod && _cfg.CartoesAmarelos;
            patrocinios.IsChecked = mod && _cfg.Patrocinios;
            acrescimos.IsChecked = mod && _cfg.Acrescimos;
            cincoSubst.IsChecked = mod && _cfg.CincoSubstituicoes;
            noveRes.IsChecked = mod && _cfg.NoveReservas;
            sobemBloco.IsEnabled = mod;
            sobemBloco.Opacity = mod ? 1 : 0.45;
            sobem.Selecionado = mod ? _cfg.SobemDescem : 0;
            ajudaSobem.Text = AjudaSobem();
            ajudaEquipas.Text = AjudaEquipas();
            desempate.Text = Desempate();
            resumoTxt.Children.Clear();
            foreach (var (k, v) in Resumo())
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*") };
                g.Children.Add(new TextBlock { Text = k, FontSize = 12.5, Foreground = TextoFraco });
                var tv = new TextBlock { Text = v, FontSize = 13, Foreground = Texto, TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(tv, 1);
                g.Children.Add(tv);
                resumoTxt.Children.Add(g);
            }
        };

        // teclado: Esc cancela, Enter salva, Ctrl+Tab / PageDown e PageUp mudam de secao,
        // as setas mudam de secao quando nenhum controle tem o foco
        _tecla = (k, m) =>
        {
            if (k == Key.Tab && m.HasFlag(KeyModifiers.Control)) Secao((_secao + (m.HasFlag(KeyModifiers.Shift) ? Secoes.Length - 1 : 1)) % Secoes.Length);
            else if (k == Key.PageDown) Secao(Math.Min(_secao + 1, Secoes.Length - 1));
            else if (k == Key.PageUp) Secao(Math.Max(_secao - 1, 0));
        };
        Secao(0);
    }

    List<(string, string)> Resumo()
    {
        var r = new List<(string, string)> { (T("Versão"), _cfg.Modificada ? T("Atual") : T("Retrô")) };
        r.Add((T("Equipas"), T("{0} por divisão · {1} rodadas", _cfg.N, 2 * (_cfg.N - 1))));
        r.Add((T("Cartões"), _cfg.Modificada && _cfg.CartoesAmarelos ? T("Amarelos e vermelhos") : T("Só vermelhos")));
        r.Add((T("Patrocínios"), _cfg.Modificada && _cfg.Patrocinios ? T("Ligados") : T("Desligados")));
        r.Add((T("Acréscimos"), _cfg.Modificada && _cfg.Acrescimos ? T("Ligados") : T("Desligados")));
        r.Add((T("Substituições"), _cfg.Modificada ? (_cfg.CincoSubstituicoes ? "5 (+1)" : "3 (+1)") : "3"));
        r.Add((T("Reservas"), _cfg.Modificada && _cfg.NoveReservas ? "9" : "5"));
        int k = _cfg.SobemDescemPara(_cfg.N);
        r.Add((T("Sobem e descem"), _cfg.Modificada && _cfg.SobemDescem > 0 ? k.ToString() : T("Automático ({0})", k)));
        r.Add((T("Estrangeiros"), _cfg.EstrangeirosLiberados ? T("Liberado") : T("Realístico")));
        r.Add((T("Visual"), _cfg.Moderno ? T("Moderno") : T("Retrô")));
        r.Add((T("Formato"), _cfg.FormatoTela));
        r.Add((T("Idioma"), Nomes[IndiceIdioma]));
        return r;
    }

    void Secao(int k)
    {
        _secao = k;
        for (int i = 0; i < _secoes.Count; i++)
        {
            bool s = i == k;
            _secoes[i].Item.Background = s ? AzulFundo : Brushes.Transparent;
            _secoes[i].Item.BorderBrush = s ? Azul : Brushes.Transparent;
            _secoes[i].Nome.Foreground = s ? Azul : Texto;
            _secoes[i].Pagina.IsVisible = s;
        }
        _rolagem.Offset = default;
    }

    static Control Pagina(string titulo, string ajuda, params Control?[] cartoes)
    {
        var p = new StackPanel { Spacing = 14, MaxWidth = 860 };
        p.Children.Add(new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 4), Children = { Titulo(titulo, 21), Ajuda(ajuda, 13) } });
        foreach (var c in cartoes) if (c != null) p.Children.Add(c);
        return p;
    }

    static Control AvisoJogo(Sessao s)
    {
        var t = new StackPanel { Spacing = 4 };
        t.Children.Add(new TextBlock { Text = T("Jogo em curso"), FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Azul });
        t.Children.Add(new TextBlock { Text = RegrasDoJogo(s), FontSize = 13.5, Foreground = Texto, TextWrapping = TextWrapping.Wrap });
        t.Children.Add(Ajuda(T("O número de equipas vem do jogo gravado e não muda. O que você mudar aqui vale da próxima vez que um jogo for aberto.")));
        return new Border { Background = FundoAviso, CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 12), Child = t };
    }

    // o desenho pequeno de cada visual / formato
    sealed class Previa : Control
    {
        public enum Tipo { Original, Moderno, Quatro, Dezesseis }
        readonly Tipo _t;
        public Previa(Tipo t) { _t = t; Height = 74; Margin = new Thickness(0, 0, 0, 10); IsHitTestVisible = false; }

        public override void Render(DrawingContext dc)
        {
            double h = Bounds.Height;
            switch (_t)
            {
                case Tipo.Original:
                {
                    var r = new Rect(0, 0, h * 4 / 3, h);
                    dc.FillRectangle(W95.Verde, r);
                    var j = new Rect(r.X + 18, r.Y + 12, r.Width - 36, r.Height - 24);
                    dc.FillRectangle(W95.Face, j);
                    dc.FillRectangle(W95.TituloCor, new Rect(j.X, j.Y, j.Width, 7));
                    dc.FillRectangle(W95.Branco, new Rect(j.X + 5, j.Y + 12, j.Width - 24, 26));
                    for (int i = 0; i < 3; i++) dc.FillRectangle(W95.Azul, new Rect(j.X + 8, j.Y + 15 + 7 * i, 22 + 6 * i, 3));
                    break;
                }
                case Tipo.Moderno:
                {
                    var r = new Rect(0, 0, h * 4 / 3, h);
                    dc.DrawRectangle(TemaVerde.FundoTela, null, r, 6, 6);
                    for (int i = 0; i < 2; i++)
                    {
                        var c = new Rect(r.X + 8 + i * (r.Width - 8) / 2, r.Y + 10, (r.Width - 24) / 2, r.Height - 20);
                        dc.DrawRectangle(TemaVerde.Cartao, new Pen(TemaVerde.Borda, 1), c, 4, 4);
                        for (int l = 0; l < 4; l++)
                        {
                            dc.DrawEllipse(l % 2 == 0 ? Azul : Verde, null, new Point(c.X + 7, c.Y + 9 + 12 * l), 3, 3);
                            dc.FillRectangle(TextoFraco, new Rect(c.X + 13, c.Y + 8 + 12 * l, c.Width - 20, 2));
                        }
                    }
                    break;
                }
                default:
                {
                    double w = _t == Tipo.Quatro ? h * 4 / 3 : h * 16 / 9;
                    var r = new Rect(0.5, 0.5, w - 1, h - 1);
                    dc.DrawRectangle(TemaVerde.Cartao, new Pen(TemaVerde.Borda, 1), r, 5, 5);
                    var t = new FormattedText(_t == Tipo.Quatro ? "4:3" : "16:9", System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, new Typeface(Inter, FontStyle.Normal, FontWeight.SemiBold), 15, Texto);
                    dc.DrawText(t, new Point(r.X + (r.Width - t.Width) / 2, r.Y + (r.Height - t.Height) / 2));
                    break;
                }
            }
        }
    }

    // ---------------------------------------------------------------- Original
    // a janela "Configurações" do Windows 95 (TTabbedNotebook com as quatro secoes e OK /
    // Cancelar a direita, como a TOptionsDlg), centrada no verde; 16:9: o verde mais largo
    Janela _janela = null!;
    readonly List<Combo> _combos = new();

    void MontarClassico()
    {
        var tela = new Canvas { Width = Formato.Largo ? 1067 : 800, Height = 600, Background = W95.Verde, ClipToBounds = true };
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = tela };
        Background = W95.Verde;
        var j = _janela = new Janela(T("Configurações"), 588, 352, W95.Face);
        var nb = _nb = j.Por(new Notebook { FonteAbas = W95.Arial13, CorAbas = W95.Azul, TopoPagina = 29, AbasPorLinha = 5 }, 0, 0, 588, 352);
        var pJogo = nb.Pagina(T("Jogo"));
        var pRegras = nb.Pagina(T("Regras"));
        var pAparencia = nb.Pagina(T("Aparência"));
        var pIdioma = nb.Pagina(T("Idioma"));
        var pDados = nb.Pagina(T("Dados"));
        var a11 = W95.Arial11;

        Grupo NovoGrupo(Canvas p, string titulo, double x, double y, double w, double h, int indice, params string[] itens)
        {
            var g = nb.Por(p, new Grupo { Titulo = titulo }, x, y, w, h);
            g.Itens(itens);
            g.Indice = indice;
            return g;
        }
        Rotulo Texto(Canvas p, string t, double x, double y, double w, double h, Fonte? f = null, IBrush? cor = null) =>
            nb.Por(p, new Rotulo { Texto = t, Fonte = f ?? a11, Cor = cor ?? W95.Preto }, x, y, w, h);

        // Jogo
        var versao = NovoGrupo(pJogo, T("Versão do jogo"), 12, 10, 460, 74, _cfg.Modificada ? 1 : 0, T("Retrô"), T("Atual"));
        var descVersao = Texto(pJogo, "", 24, 92, 448, 30);
        Texto(pJogo, T("Equipas por divisão:"), 24, 136, 140, 16, W95.Arial13);
        var equipas = nb.Por(pJogo, new Combo { Fonte = W95.Arial13, AlturaItem = 16 }, 168, 132, 80, 22);
        foreach (var n in Configuracoes.OpcoesEquipas) equipas.Itens.Add(n.ToString());
        _combos.Add(equipas);
        var travEquipas = Texto(pJogo, T("(só na versão Atual)"), 258, 136, 200, 16, a11, W95.Cinzento);
        var ajudaEquipas = Texto(pJogo, "", 24, 164, 448, 44);
        if (_emCurso != null)
        {
            nb.Por(pJogo, new Bevel(), 12, 214, 460, 2);
            Texto(pJogo, T("Jogo em curso: ") + RegrasDoJogo(_emCurso), 24, 222, 448, 44, W95.Arial11b, W95.Marinho);
        }
        Texto(pJogo, Nota(), 24, 272, 448, 40, a11, W95.Cinzento);

        // Regras
        var trav = Texto(pRegras, T("Na versão Retrô as regras são as de 1998: só cartões vermelhos, sem patrocínios nem acréscimos."), 24, 8, 448, 30, W95.Arial11b, W95.Vermelho);
        var cartoes = nb.Por(pRegras, new Marca { Texto = T("Cartões amarelos") }, 24, 40, 210, 17);
        var patrocinios = nb.Por(pRegras, new Marca { Texto = T("Patrocínios") }, 24, 62, 210, 17);
        var acrescimos = nb.Por(pRegras, new Marca { Texto = T("Acréscimos") }, 24, 84, 210, 17);
        var cincoSubstC = nb.Por(pRegras, new Marca { Texto = T("5 substituições (FIFA)") }, 244, 40, 220, 17);
        var noveResC = nb.Por(pRegras, new Marca { Texto = T("9 reservas no banco") }, 244, 62, 220, 17);
        Texto(pRegras, T("Sobem e descem por divisão:"), 24, 112, 190, 16, W95.Arial13);
        var sobem = nb.Por(pRegras, new Combo { Fonte = W95.Arial13, AlturaItem = 16 }, 218, 108, 110, 22);
        sobem.Itens.AddRange(OpcoesSobem);
        _combos.Add(sobem);
        var ajudaSobem = Texto(pRegras, "", 24, 136, 448, 28);
        var estrangeiros = NovoGrupo(pRegras, T("Jogadores estrangeiros"), 12, 166, 460, 74, _cfg.EstrangeirosLiberados ? 1 : 0,
            T("Realístico: até 5 por equipa (Bosman e lusófonos não contam)"), T("Liberado: sem limite de estrangeiros"));
        nb.Por(pRegras, new Grupo { Titulo = T("Desempate na classificação") }, 12, 246, 460, 54);
        var desempate = Texto(pRegras, "", 24, 270, 440, 24, W95.Arial13);

        // Aparencia
        var visual = NovoGrupo(pAparencia, T("Visual"), 12, 10, 460, 74, _cfg.Moderno ? 1 : 0,
            T("Retrô: o aspecto do Windows 95, sem logos"), T("Moderno: tema escuro, cartões e logos"));
        var formato = NovoGrupo(pAparencia, T("Formato da tela"), 12, 96, 460, 74, _cfg.Largo ? 1 : 0,
            T("4:3: como o Retrô (800x600)"), T("16:9: janela larga, as telas aproveitam a largura"));
        Texto(pAparencia, T("O visual e o formato mudam na hora, mesmo com um jogo aberto."), 24, 184, 448, 30, a11, W95.Cinzento);

        // Idioma
        var idioma = NovoGrupo(pIdioma, T("Idioma do jogo"), 12, 10, 460, 114, IndiceIdioma,
            Nomes[0], Nomes[1], Nomes[2], Nomes[3]);

        // Dados: a pasta de dados e o botao que a abre
        nb.Por(pDados, new Grupo { Titulo = T("Pasta de dados") }, 12, 10, 460, 250);
        Texto(pDados, DadosJogo.Raiz, 24, 34, 436, 46, W95.Arial13b, W95.Marinho);
        Texto(pDados, TextoPastaDados, 24, 86, 436, 100, a11);
        if (DadosJogo.PodeAbrirPasta)
        {
            var abrir = nb.Por(pDados, new Botao { Texto = T("Abrir pasta de dados"), Fonte = W95.MsSans11b }, 24, 196, 180, 27);
            abrir.Clique += () => DadosJogo.AbrirPasta();
        }

        var bok = j.Por(Dlg.BotaoGrande(T("OK"), "Botoes/BBOK.png"), 500, 62, 64, 64);
        var bc = j.Por(Dlg.BotaoGrande(T("Cancelar"), "Botoes/BBCANCEL.png"), 500, 130, 64, 64);
        var erro = j.Por(new Rotulo { Fonte = W95.Arial11b, Cor = W95.Vermelho }, 488, 200, 92, 120);

        versao.Mudou += () => { _cfg.Modificada = versao.Indice == 1; _atualizar!(); };
        equipas.Mudou += () => { if (_cfg.Modificada && equipas.Indice >= 0) { _cfg.EquipasPorDivisao = Configuracoes.OpcoesEquipas[equipas.Indice]; ajudaEquipas.Texto = AjudaEquipas(); ajudaEquipas.InvalidateVisual(); ajudaSobem.Texto = AjudaSobem(); ajudaSobem.InvalidateVisual(); } };
        cartoes.Clique += () => { if (_cfg.Modificada) _cfg.CartoesAmarelos = cartoes.Marcado; };
        patrocinios.Clique += () => { if (_cfg.Modificada) _cfg.Patrocinios = patrocinios.Marcado; };
        acrescimos.Clique += () => { if (_cfg.Modificada) _cfg.Acrescimos = acrescimos.Marcado; };
        cincoSubstC.Clique += () => { if (_cfg.Modificada) _cfg.CincoSubstituicoes = cincoSubstC.Marcado; };
        noveResC.Clique += () => { if (_cfg.Modificada) _cfg.NoveReservas = noveResC.Marcado; };
        sobem.Mudou += () =>
        {
            if (!_cfg.Modificada || sobem.Indice < 0 || sobem.Indice == _cfg.SobemDescem) return;
            _cfg.SobemDescem = sobem.Indice;
            ajudaSobem.Texto = AjudaSobem(); ajudaSobem.InvalidateVisual();
            ajudaEquipas.Texto = AjudaEquipas(); ajudaEquipas.InvalidateVisual();
        };
        estrangeiros.Mudou += () => _cfg.EstrangeirosLiberados = estrangeiros.Indice == 1;
        visual.Mudou += () => _cfg.VisualTela = visual.Indice == 1 ? Configuracoes.VisualModerno : Configuracoes.VisualClassico;
        formato.Mudou += () => _cfg.FormatoTela = formato.Indice == 1 ? Configuracoes.Formato169 : Configuracoes.Formato43;
        idioma.Mudou += () => TrocarIdioma(idioma.Indice);

        _atualizar = () =>
        {
            bool mod = _cfg.Modificada;
            descVersao.Texto = mod ? DescModificada : DescOriginal;
            equipas.Indice = mod ? Math.Max(0, Array.IndexOf(Configuracoes.OpcoesEquipas, _cfg.EquipasPorDivisao)) : 0;
            equipas.IsEnabled = mod;
            equipas.Opacity = mod ? 1 : 0.5;
            travEquipas.IsVisible = !mod;
            ajudaEquipas.Texto = AjudaEquipas();
            trav.IsVisible = !mod;
            cartoes.Marcado = mod && _cfg.CartoesAmarelos;
            cartoes.Habilitado = mod;
            patrocinios.Marcado = mod && _cfg.Patrocinios;
            patrocinios.Habilitado = mod;
            acrescimos.Marcado = mod && _cfg.Acrescimos;
            acrescimos.Habilitado = mod;
            cincoSubstC.Marcado = mod && _cfg.CincoSubstituicoes; cincoSubstC.Habilitado = mod;
            noveResC.Marcado = mod && _cfg.NoveReservas; noveResC.Habilitado = mod;
            sobem.Indice = mod ? _cfg.SobemDescem : 0;
            sobem.IsEnabled = mod;
            sobem.Opacity = mod ? 1 : 0.5;
            ajudaSobem.Texto = AjudaSobem();
            desempate.Texto = Desempate();
            j.InvalidateTudo();
            foreach (var c in new Canvas[] { pJogo, pRegras }) foreach (var x in c.Children) x.InvalidateVisual();
        };

        bok.Clique += () => Salvar(m => { erro.Texto = m; erro.InvalidateVisual(); });
        bc.Clique += () => Fechar(false);
        j.AoEnter = () => Salvar(m => { erro.Texto = m; erro.InvalidateVisual(); });
        j.AoFechar = () => Fechar(false);
        j.Foco = this;
        _tecla = (k, m) =>
        {
            if (k == Key.Tab && m.HasFlag(KeyModifiers.Control)) nb.Atual = (nb.Atual + (m.HasFlag(KeyModifiers.Shift) ? 4 : 1)) % 5;
        };

        void Centrar()
        {
            tela.Width = Formato.Largo ? 1067 : 800;
            Canvas.SetLeft(j, Math.Floor((tela.Width - j.Width) / 2));
            Canvas.SetTop(j, Math.Floor((tela.Height - j.Height) / 2));
        }
        Centrar();
        tela.Children.Add(j);
        Action mudou = () => Dispatcher.UIThread.Post(Centrar);
        AttachedToVisualTree += (_, _) => { Formato.Mudou -= mudou; Formato.Mudou += mudou; };
        DetachedFromVisualTree += (_, _) => Formato.Mudou -= mudou;
    }

    // ---------------------------------------------------------------- comandos
    void Salvar(Action<string> erro)
    {
        try { _cfg.Gravar(); }
        catch (Exception ex) { erro(T("Não consegui gravar: ") + ex.Message); return; }
        // o formato e o visual valem na hora (a janela e as telas)
        Formato.Aplicar(_cfg.Largo);
        Visual.Aplicar(_cfg.Moderno);
        Idioma.Aplicar(_cfg.Idioma);
        Fechar(true);
    }

    void Fechar(bool salvou)
    {
        if (_fechou) return;
        _fechou = true;
        if (!salvou) Idioma.Aplicar(_idiomaInicial);
        Fechou?.Invoke(salvou);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        // no celular o Esc e o Voltar: a pagina da secao volta a lista; a lista cancela
        if (Celular.Ativo) { if (e.Key == Key.Escape) e.Handled = CUi.Folha.Voltar(); return; }
        // a lista de um Combo aberta: Enter / Esc fecham a lista
        if (_combos.Find(c => c.Aberto) is { } aberto && e.Key is Key.Enter or Key.Escape) { aberto.Fechar(); e.Handled = true; return; }
        if (e.Key == Key.Escape) { e.Handled = true; if (_janela != null) _janela.AoFechar!(); else Fechar(false); }
        else if (e.Key == Key.Enter && e.Source is not TextBox)
        {
            e.Handled = true;
            if (_janela != null) _janela.AoEnter!();
            else Salvar(_erro);
        }
        else if (_tecla != null) { _tecla(e.Key, e.KeyModifiers); }
    }
}
