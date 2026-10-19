using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;
using Equipa = RetroFoot.Motor.Save.Equipa;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// A tela da jornada (TjourneyDlg, seg10), maximizada, fiel ao original: o titulo da janela em
// cima e a area cliente embaixo, desenhados a mao como o Paint do Delphi (seg10:04a1, cada jogo
// em seg03:9ecd, o ultimo lance em seg03:c25e). Medidas e cores portadas de
// versao em Go: ui/jogo/jornada.go.
//
// Diferente do original (paginas de 20 linhas num quadro de 800x600), todos os jogos ficam
// na mesma tela: as linhas sao arrumadas em colunas (uma divisao nao se parte entre colunas
// quando cabe inteira numa) e o quadro e ampliado para a janela mantendo a proporcao. O numero
// de colunas e o que deixa as linhas maiores na janela; com 8 equipas e uma coluna de 800x600,
// como o original. So quando nem assim fica legivel (letra abaixo de ~9 pontos) ha paginas.
// Isso no formato 16:9 (Formato.Largo); no 4:3 a tela e a do original: uma coluna de 800x600
// com paginas de 20 linhas (uma divisao nao se parte entre paginas) e as setas.
//
// A Sessao cria a tela no comeco da jornada, chama Minuto a cada tique do relogio (com o motor
// parado) e Fim quando o relogio acaba; a tela chama "continuar" quando o jogador fecha.
// Como o motor volta a andar logo depois de cada Minuto, a tela guarda um instantaneo (Quadro)
// dos jogos e so desenha a partir dele.
//
// No visual Moderno (Visual.cs) a mesma tela e desenhada de outro jeito: TelaJornadaModerna.cs.
//
// Teclas: Enter/Espaco/Esc (ou clique) no fim fecham; P ou Pause param o relogio; + e - mudam
// a velocidade (Sessao.MsPorMinuto); setas e PageUp/PageDown mudam de pagina.
public sealed partial class TelaJornada : UserControl
{
    // o original fecha sozinho depois de Fim + 5 minutos (seg10:08DB: CanClose = 1 e
    // TCustomForm.Close); aqui, por padrao, a tela espera o jogador
    public bool FecharSozinha { get; set; }

    readonly Sessao s;
    readonly Superficie sup;
    string titulo;
    List<Linha> linhas = new();
    Arranjo? arranjo;
    int minuto, fim = 90, contagem = -1;
    int acrescimo;                          // Jornada.Acrescimo: o n de "45'+n'"
    bool noIntervalo;                       // Jornada.NoIntervalo
    bool taca, acabou;
    int pagina;                             // DS:2B38
    Action? continuar;
    bool pausado;
    DispatcherFrame? pausa;

    // relogio (TGauge "clock", gkPie): cor da frente, minimo, maximo e progresso
    Color corRelogio = Rgb(0xFF0000);       // clBlue
    int relMin, relMax = 60, relProg;

    public TelaJornada(Sessao s, byte[] titulo, Jornada jn)
    {
        this.s = s;
        this.titulo = TituloNoIdioma(System.Text.Encoding.Latin1.GetString(titulo));
        sup = new Superficie(this);
        Content = sup;
        Background = new SolidColorBrush(Verde);
        Focusable = true;
        Fotografar(jn);
        AttachedToVisualTree += (_, _) => { Focus(); Visual.Mudou -= VisualMudou; Visual.Mudou += VisualMudou; };
        DetachedFromVisualTree += (_, _) => Visual.Mudou -= VisualMudou;
        // como no classico: o botao direito durante a jornada abre a Gestao de Tecnicos (o unico
        // jeito de tirar de ferias um tecnico que esta sozinho); o relogio para enquanto ela esta aberta
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed || acabou || gestaoAberta) return;
            e.Handled = true;
            gestaoAberta = true;
            bool estava = pausado;
            Pausar(true);
            TelaTreinadores.Gestao(s, () => { gestaoAberta = false; if (!estava) Pausar(false); });
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
    }

    // os lances mostrados ao lado do placar (Opcoes > Jogo; gravado em Configuracoes.LancesNaJornada)
    public const int MostraGols = 1, MostraAnulados = 2, MostraAmarelos = 4, MostraVermelhos = 8, MostraLesoes = 16;
    public static readonly (int Bit, string Nome)[] TiposDeLance =
        { (MostraGols, "Gols"), (MostraAnulados, "Gols anulados"), (MostraAmarelos, "Amarelos"), (MostraVermelhos, "Vermelhos"), (MostraLesoes, "Lesões") };
    static int mostrar = Configuracoes.Ler().LancesNaJornada;

    public static int LancesMostrados
    {
        get => mostrar;
        set
        {
            mostrar = value & 0x1F;
            try { var c = Configuracoes.Ler(); c.LancesNaJornada = mostrar; c.Gravar(); }
            catch (Exception ex) { Console.Error.WriteLine("config: " + ex.Message); }
        }
    }

    static bool Mostra(int tipo) => tipo switch
    {
        TipoLance.Gol or TipoLance.Penalti => (mostrar & MostraGols) != 0,
        TipoLance.GolAnulado => (mostrar & MostraAnulados) != 0,
        TipoLance.Amarelo => (mostrar & MostraAmarelos) != 0,
        TipoLance.Expulsao => (mostrar & MostraVermelhos) != 0,
        TipoLance.Lesao => (mostrar & MostraLesoes) != 0,
        _ => true,
    };

    bool gestaoAberta;

    // o visual mudou nas Configuracoes: o arranjo e refeito no outro
    void VisualMudou()
    {
        arranjo = null;
        sup.InvalidateVisual();
    }

    // um tique do relogio (MinutePassed, seg10:069b), com o motor parado
    public void Minuto(Jornada jn)
    {
        Atualizar(jn);
        if (pausado)
        {
            // o motor espera aqui (o Invoke da Sessao so volta quando a pausa acaba)
            pausa = new DispatcherFrame();
            Dispatcher.UIThread.PushFrame(pausa);
            pausa = null;
        }
    }

    // o instantaneo e o relogio, sem a pausa: tambem antes de uma janela que o motor abre no
    // meio de um tique (o intervalo e as lesoes vem antes do Minuto desse tique)
    public void Atualizar(Jornada jn)
    {
        Fotografar(jn);
        int m = jn.Minuto;
        if (m > 0 && m <= jn.Fim)
        {
            // no 45 o relogio passa a vermelho (45..105); no 90 da Taca com prorrogacao, a
            // roxo (90..150); o progresso e o minuto
            if (m >= 90 && jn.Fim > 90) { corRelogio = Rgb(0x800080); relMin = 90; relMax = 150; }
            else if (m >= 45) { corRelogio = Rgb(0x0000FF); relMin = 45; relMax = 105; }
            relProg = m;
        }
        sup.InvalidateVisual();
    }

    // o relogio acabou: a tela espera o jogador e chama "continuar"
    public void Fim(Jornada jn, Action continuar)
    {
        Fotografar(jn);
        acabou = true;
        this.continuar = continuar;
        sup.InvalidateVisual();
        Focus();
        if (FecharSozinha) Seguir();
    }

    void Seguir()
    {
        var c = continuar;
        continuar = null;
        c?.Invoke();
    }

    void Pausar(bool p)
    {
        pausado = p;
        if (!p && pausa != null) pausa.Continue = false;
        sup.InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Pausar(false);
    }

    // ---------------------------------------------------------------- o instantaneo

    sealed class Linha
    {
        public string? Cabecalho;           // jogo sem equipas com +0x2D = 2: nome da divisao
        public Bitmap? LogoCabecalho;       // logo da liga/taca (jogo de um pais so), ou null
        public Bitmap? LogoCasa, LogoFora;  // logos das equipas (Escudos), ou null
        public bool Vazia;                  // jogo sem as duas equipas (nao desenha nada)
        public bool Taca;                   // +0x2D = 1: desce uma linha
        public uint Publico;
        public string Casa = "", Fora = "";
        public Color LetraCasa, FundoCasa, LetraFora, FundoFora;
        public int GC, GF;
        public int Humanos;                 // 0 a 2 (jornada.asm: a pagina com mais)
        public int LanceTipo, LanceMin;     // 0 = sem lances
        public int LanceAcr;                // Lance.Acrescimo
        public int Acrescimos;              // os acrescimos do jogo no tempo atual (0 fora deles)
        public string LanceNome = "";
        public bool SegundoAmarelo;
        public int VermCasa, VermFora;      // expulsoes de cada lado (o visual Moderno)
        public JogoAtual? Jogo;             // para o clique (os detalhes do jogo)
    }

    void Fotografar(Jornada jn)
    {
        var nova = new List<Linha>(jn.Jogos.Count);
        foreach (var jg in jn.Jogos)
        {
            var l = new Linha { Jogo = jg };
            var d = jg.Dados;
            if (jg.Equipa[0] is not { } casa || jg.Equipa[1] is not { } fora)
            {
                if (d.Taca == TipoJogo.SemEquipas)
                {
                    l.Cabecalho = System.Text.Encoding.Latin1.GetString(d.B04, 1, Math.Min(d.B04[0], (byte)40));
                    l.LogoCabecalho = LogoDoCabecalho(l.Cabecalho, jn.Taca);
                }
                else l.Vazia = true;
                nova.Add(l);
                continue;
            }
            l.Taca = d.Taca == TipoJogo.Taca;
            l.Publico = d.D40;
            l.Acrescimos = jn.AcrescimosDoJogo(jg);
            l.Casa = NomeEquipa(casa);
            l.Fora = NomeEquipa(fora);
            l.LogoCasa = Escudos.Escudo(casa);
            l.LogoFora = Escudos.Escudo(fora);
            (l.LetraCasa, l.FundoCasa) = (Cor(casa.CorLetra), Cor(casa.CorFundo));
            (l.LetraFora, l.FundoFora) = (Cor(fora.CorLetra), Cor(fora.CorFundo));
            l.GC = Memoria.Golos(casa);
            l.GF = Memoria.Golos(fora);
            // jornada.asm: humano quando o treinador tem +7 != 0
            l.Humanos = (jg.Treinador[0] is { Humano: not 0 } ? 1 : 0) + (jg.Treinador[1] is { Humano: not 0 } ? 1 : 0);
            foreach (var ln in jg.Lances)
                if (ln.Tipo == TipoLance.Expulsao)
                {
                    if (ln.Lado == Lado.Casa) l.VermCasa++; else l.VermFora++;
                }
            // o ultimo lance dos tipos escolhidos (o menu do botao direito)
            if (System.Linq.Enumerable.LastOrDefault(jg.Lances, x => Mostra(x.Tipo)) is { } ult)
            {
                l.LanceTipo = ult.Tipo;
                l.LanceMin = ult.Minuto;
                l.LanceAcr = ult.Acrescimo;
                l.SegundoAmarelo = ult.SegundoAmarelo;
                var cif = ult.Jogador?.NomeCifrado;
                if (cif != null && cif.Length > 0)
                {
                    var nome = Cifra.TextoEFT(cif).Texto;
                    l.LanceNome = System.Text.Encoding.Latin1.GetString(nome, 0, Math.Min(nome.Length, 20));
                }
            }
            nova.Add(l);
        }
        linhas = nova;
        minuto = jn.Minuto;
        acrescimo = jn.Acrescimo;
        noIntervalo = jn.NoIntervalo;
        fim = jn.Fim;
        taca = jn.Taca;
        // a lista mudou (no minuto 90 da Taca saem os jogos decididos): o arranjo e refeito
        if (nova.Count != contagem) arranjo = null;
        contagem = nova.Count;
    }

    // o logo da liga da divisao com esse nome (1a a 4a) ou, sem divisao com esse nome num dia
    // de Taca, o da taca; so em jogo de um pais so (Escudos.Liga)
    readonly Dictionary<string, Bitmap?> logosCabecalho = new();
    Bitmap? LogoDoCabecalho(string nome, bool diaDeTaca)
    {
        if (logosCabecalho.TryGetValue(nome, out var b)) return b;
        var dv = s.S.Divisoes;
        int k = dv.FindIndex(d => System.Text.Encoding.Latin1.GetString(d.Nome, 1, Math.Min((int)d.Nome[0], 20)).Trim() == nome.Trim());
        b = k >= 0 ? Escudos.Liga(s.S, k) : diaDeTaca ? Escudos.Taca(s.S) : null;
        return logosCabecalho[nome] = b;
    }

    static string NomeEquipa(Equipa e) =>
        e.NomeCurto.Length == 0 ? "" : System.Text.Encoding.Latin1.GetString(Cifra.TextoEFT(e.NomeCurto).Texto);

    // TColor do Delphi: $00BBGGRR
    static Color Cor(byte[] c) => Color.FromRgb(c[0], c[1], c[2]);
    static Color Rgb(int tcolor) => Color.FromRgb((byte)tcolor, (byte)(tcolor >> 8), (byte)(tcolor >> 16));

    // ---------------------------------------------------------------- o arranjo

    // as linhas em colunas de ate R linhas, as colunas em paginas de C colunas
    sealed class Coluna
    {
        public readonly List<(int Item, int Linha)> Itens = new();
        public bool Topo;                   // comeca por um jogo: linha em branco e a moldura de cima
        public int Linhas;
    }

    sealed class Arranjo
    {
        public int C, R, Contagem;
        public bool Largo;
        public Size Janela;
        public List<Coluna> Colunas = new();
        public int[] Pagina = Array.Empty<int>();   // a pagina de cada item
        public int Paginas => (Colunas.Count + C - 1) / C;
        public bool Moderno;
        public double W, Hq;                // o tamanho minimo do quadro
    }

    // Uma divisao (o cabecalho e os jogos dela) vai inteira para a coluna seguinte quando nao
    // cabe no resto desta mas cabe numa nova; uma coluna que comeca por um jogo ganha a linha
    // em branco de cima, como os jogos da Taca no original (seg03:9ecd: +1 na posicao).
    List<Coluna> Empacotar(int R)
    {
        var cols = new List<Coluna>();
        Coluna? c = null;
        for (int i = 0; i < linhas.Count; i++)
        {
            int precisa = 1;
            if (linhas[i].Cabecalho != null)
            {
                int bloco = 1;
                while (i + bloco < linhas.Count && linhas[i + bloco].Cabecalho == null) bloco++;
                precisa = Math.Min(bloco, R);
            }
            if (c == null || c.Linhas + precisa > R)
            {
                c = new Coluna { Topo = linhas[i].Cabecalho == null };
                c.Linhas = c.Topo ? 1 : 0;
                cols.Add(c);
            }
            c.Itens.Add((i, c.Linhas));
            c.Linhas++;
        }
        return cols;
    }

    // o maior bloco (o cabecalho de uma divisao ou da Taca e os seus jogos)
    int MaiorBloco()
    {
        int maior = 0;
        for (int i = 0; i < linhas.Count; i++)
        {
            if (linhas[i].Cabecalho == null) continue;
            int bloco = 1;
            while (i + bloco < linhas.Count && linhas[i + bloco].Cabecalho == null) bloco++;
            maior = Math.Max(maior, bloco);
        }
        return maior;
    }

    const int LinhasPorPagina = 20;         // o original: paginas de 20 linhas no 800x600
    const double EscalaMinima = 0.7;        // letra 13 * 0,7 = ~9 pontos na janela
    const int LarguraColuna = 700;          // com 2 ou mais colunas (o lance cabe ate ~690)

    static double LarguraQuadro(int c) => c == 1 ? L : c * LarguraColuna;
    static double AlturaConteudo(int r, bool paginas) => Titulo + 50 + H / 2 + (H + 2) * r + (paginas ? 32 + 13 : 14);
    static double AlturaQuadro(int r, bool paginas) => Math.Max(A, AlturaConteudo(r, paginas));

    Arranjo Arrumar(Size janela)
    {
        if (arranjo != null && arranjo.Janela == janela && arranjo.Contagem == linhas.Count && arranjo.Largo == Formato.Largo
            && arranjo.Moderno == Visual.Moderno) return arranjo;
        if (Visual.Moderno) return Concluir(ArrumarModerno(janela), janela);
        double bw = janela.Width > 0 ? janela.Width : 1600, bh = janela.Height > 0 ? janela.Height : 1000;
        int n = Math.Max(linhas.Count, 1);
        Arranjo? melhor = null;
        double escala = 0;
        if (!Formato.Largo)
        {
            // 4:3: como o original, uma coluna com paginas de 20 linhas; com mais equipas, como
            // o EXE do launcher (N + 2 linhas), cabem sempre 2 divisoes por pagina
            int rp = Math.Max(LinhasPorPagina, 2 * MaiorBloco());
            var cols = Empacotar(rp);
            melhor = new Arranjo { C = 1, R = rp, Colunas = cols, W = L, Hq = AlturaQuadro(rp, cols.Count > 1) };
            escala = 1;
        }
        // todas as linhas numa pagina: com C colunas, o menor R que cabe
        else for (int c = 1; c <= 8; c++)
        {
            int r = Math.Max(2, (n + c - 1) / c);
            List<Coluna> cols;
            while ((cols = Empacotar(r)).Count > c) r++;
            double e = Math.Min(bw / LarguraQuadro(c), bh / AlturaQuadro(r, false));
            if (e > escala + 1e-9)
            {
                escala = e;
                melhor = new Arranjo { C = c, R = r, Colunas = cols, W = LarguraQuadro(c), Hq = AlturaQuadro(r, false) };
            }
            if (cols.Count < c) break;      // mais colunas ficariam vazias
        }
        if (melhor == null || escala < EscalaMinima)
        {
            // nao cabe legivel: paginas com as colunas e linhas que cabem na escala minima
            double lw = bw / EscalaMinima, lh = bh / EscalaMinima;
            int c = lw >= 2 * LarguraColuna ? (int)(lw / LarguraColuna) : 1;
            int r = Math.Max(2, (int)((lh - (Titulo + 50 + H / 2 + 45)) / (H + 2)));
            melhor = new Arranjo { C = c, R = r, Colunas = Empacotar(r), W = LarguraQuadro(c), Hq = AlturaQuadro(r, true) };
        }
        return Concluir(melhor, janela);
    }

    // a pagina de cada item e a pagina mostrada (o mesmo nos dois visuais)
    Arranjo Concluir(Arranjo melhor, Size janela)
    {
        melhor.Janela = janela;
        melhor.Moderno = Visual.Moderno;
        melhor.Largo = Formato.Largo;
        melhor.Contagem = linhas.Count;
        melhor.Pagina = new int[linhas.Count];
        for (int k = 0; k < melhor.Colunas.Count; k++)
            foreach (var (i, _) in melhor.Colunas[k].Itens) melhor.Pagina[i] = k / melhor.C;
        // a pagina com mais jogos de humanos: quando a tela abre e quando as paginas mudam
        if (arranjo == null || arranjo.Paginas != melhor.Paginas || arranjo.Contagem != melhor.Contagem)
            pagina = MelhorPagina(melhor);
        arranjo = melhor;
        pagina = Math.Clamp(pagina, 0, UltimaPagina);
        return melhor;
    }

    // jornada.asm: conta os humanos dos jogos de cada pagina; fica a primeira com mais, ou a 0
    int MelhorPagina(Arranjo a)
    {
        var conta = new Dictionary<int, int>();
        for (int i = 0; i < linhas.Count; i++)
            if (linhas[i].Humanos > 0)
                conta[a.Pagina[i]] = conta.GetValueOrDefault(a.Pagina[i]) + linhas[i].Humanos;
        int melhor = 0, max = 0;
        foreach (var (p, c) in conta)
            if (c > max || (c == max && p < melhor)) { melhor = p; max = c; }
        return melhor;
    }

    int UltimaPagina => arranjo == null ? 0 : Math.Max(arranjo.Paginas - 1, 0);

    void MudarPagina(int d)
    {
        int p = Math.Clamp(pagina + d, 0, UltimaPagina);
        if (p == pagina) return;
        pagina = p;
        sup.InvalidateVisual();
    }

    // ---------------------------------------------------------------- teclado

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter or Key.Space or Key.Escape when acabou:
                Seguir();
                break;
            case Key.P or Key.Pause when !acabou:
                Pausar(!pausado);
                break;
            case Key.Add or Key.OemPlus:
                s.MsPorMinuto = Math.Max(s.MsPorMinuto / 2, 0);
                break;
            case Key.Subtract or Key.OemMinus:
                s.MsPorMinuto = Math.Clamp(s.MsPorMinuto == 0 ? 15 : s.MsPorMinuto * 2, 15, 1000);
                break;
            case Key.Right or Key.PageDown or Key.Down:
                MudarPagina(1);
                break;
            case Key.Left or Key.PageUp or Key.Up:
                MudarPagina(-1);
                break;
            default:
                base.OnKeyDown(e);
                return;
        }
        e.Handled = true;
    }

    // ---------------------------------------------------------------- desenho

    static readonly Color Verde = Color.FromRgb(0, 128, 0);           // clGreen, o fundo
    static readonly IBrush VerdeB = new SolidColorBrush(Verde);
    static readonly IBrush Amarelo = new SolidColorBrush(Color.FromRgb(255, 255, 0));
    static readonly IBrush Marinho = new SolidColorBrush(Color.FromRgb(0, 0, 128));   // a moldura
    static readonly IBrush Vermelho = new SolidColorBrush(Color.FromRgb(255, 0, 0));
    static readonly IBrush Branco = Brushes.White, Preto = Brushes.Black;
    static readonly IBrush Face = new SolidColorBrush(Color.FromRgb(192, 192, 192));
    static readonly IBrush Sombra = new SolidColorBrush(Color.FromRgb(128, 128, 128));
    static readonly IBrush Luz = new SolidColorBrush(Color.FromRgb(223, 223, 223));
    static readonly FontFamily Arial = new("avares://RetroFoot.App/Assets/Fontes#Liberation Sans");
    static readonly FontFamily Sans = new("avares://RetroFoot.App/Assets/Fontes#MS Sans Serif");

    const int L = 800, A = 600;             // o quadro do original (o minimo aqui)
    const int Titulo = 19;                  // a barra de titulo da janela maximizada
    // altura das linhas: Round(altura da tela / 30) (seg03:9ecd)
    const int H = (A + 15) / 30;
    // o quadro desta vez (o tamanho minimo do arranjo esticado a proporcao da janela) e a
    // area cliente
    double QL = L, QA = A;
    double CW => QL;
    double CH => QA - Titulo;
    // a distancia entre as linhas: H + 2 no original; quando a janela e mais alta que o
    // arranjo, ate 6 pontos a mais (para nao sobrar verde embaixo)
    double Passo = H + 2;
    // TGauge e TButtons (FormCreate/Run, seg10:08ec e 10:0200): relogio de 45 a 50 pontos da
    // direita; as setas 32x32 no canto de baixo
    Rect Relogio => new(QL - 50, 5, 45, 45);
    Rect Proxima => new(QL - 32 - 5, CH - 32 - 5, 32, 32);
    Rect Anterior => new(QL - 2 * 32 - 5, CH - 32 - 5, 32, 32);

    // o titulo que o motor grava no save (Jogos.cs: "3ª Jornada", "3ª Jornada - 1998",
    // "TAÇA - Meias-finais", "Taça - <fase> - 1998") no idioma da tela; so na tela, o save
    // fica como esta
    static readonly string[] FasesTaca = { "Meias-finais", "Quartos de final", "Oitavos de final", "FINAL" };
    internal static string TituloNoIdioma(string s)
    {
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(\d+)ª Jornada", m => T("{0}ª Jornada", m.Groups[1].Value));
        s = System.Text.RegularExpressions.Regex.Replace(s, @"(\d+) avos de final", m => T("{0} avos de final", m.Groups[1].Value));
        foreach (var f in FasesTaca) s = s.Replace(f, T(f));
        return s.Replace("TAÇA", T("TAÇA")).Replace("Taça", T("Taça"));
    }


    static FormattedText Txt(string t, double tam, IBrush cor, bool negrito = false, FontFamily? f = null) =>
        new(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(f ?? Arial, FontStyle.Normal, negrito ? FontWeight.Bold : FontWeight.Normal), tam, cor);

    static void Ret(DrawingContext dc, IBrush b, double x, double y, double w, double h) =>
        dc.FillRectangle(b, new Rect(x, y, w, h));

    bool BotoesVisiveis => UltimaPagina > 0;

    void Desenhar(DrawingContext dc)
    {
        if (arranjo == null) return;
        // barra de titulo (BorderIcons = []: so o texto)
        Ret(dc, Marinho, 0, 0, CW, Titulo - 1);
        Ret(dc, Face, 0, Titulo - 1, CW, 1);
        dc.DrawText(Txt(titulo + (pausado ? "  (" + T("pausa") + ")" : ""), 11, Branco, true, Sans), new Point(4, 2));

        using (dc.PushTransform(Matrix.CreateTranslation(0, Titulo)))
        using (dc.PushClip(new Rect(0, 0, CW, CH)))
        {
            Ret(dc, VerdeB, 0, 0, CW, CH);
            DesenharJogos(dc);
            DesenharRelogio(dc);
            if (BotoesVisiveis)
            {
                Botao(dc, Anterior, "<", pagina > 0);
                Botao(dc, Proxima, ">", pagina < UltimaPagina);
            }
        }
    }

    void DesenharJogos(DrawingContext dc)
    {
        var a = arranjo!;
        double lc = CW / a.C;               // a largura de cada coluna nesta janela
        for (int k = pagina * a.C; k < Math.Min(a.Colunas.Count, (pagina + 1) * a.C); k++)
        {
            var col = a.Colunas[k];
            double x0 = Math.Round((k - pagina * a.C) * lc), w = Math.Round((k - pagina * a.C + 1) * lc) - x0;
            using (dc.PushTransform(Matrix.CreateTranslation(x0, 0)))
            {
                using (dc.PushClip(new Rect(0, 0, w - 13, CH)))
                    foreach (var (i, k2) in col.Itens) DesenharLinha(dc, linhas[i], Passo * k2 + 50, w);
                // a moldura azul-marinho (seg10:05ad): numa coluna que comeca por um jogo (a
                // Taca, sem cabecalhos) tambem a linha de cima
                double topo = H / 2 + 50;
                double baixo = Passo * col.Linhas + topo;
                if (col.Topo) Ret(dc, Marinho, 12, topo, w - 24, 1);
                Ret(dc, Marinho, 12, topo, 1, baixo + 1 - topo);
                Ret(dc, Marinho, w - 12, topo, 1, baixo + 1 - topo);
                Ret(dc, Marinho, 12, baixo, w - 23, 1);
            }
        }
    }

    static void DesenharLinha(DrawingContext dc, Linha l, double y, double w)
    {
        if (l.Vazia) return;
        if (l.Cabecalho != null)
        {
            // a linha da moldura atravessa e o nome da divisao vem por cima, em fundo verde
            Ret(dc, Marinho, 12, y + H / 2 - 1, w - 24, 1);
            var t = Txt(TituloNoIdioma(l.Cabecalho), 13, Amarelo, true);
            double xt = 64;
            if (l.LogoCabecalho != null)
            {
                Ret(dc, VerdeB, 64, y, H + 2, H);
                Logo(dc, l.LogoCabecalho, 64 + 1, y);
                xt = 64 + H + 4;
            }
            Ret(dc, VerdeB, xt, y + 1, t.Width, t.Height);
            dc.DrawText(t, new Point(xt, y + 1));
            return;
        }
        // publico, alinhado a direita ate 57
        var pub = Txt(FormatoDinheiro.Numero(l.Publico), 13, Amarelo);
        dc.DrawText(pub, new Point(57 - pub.Width, y));
        Caixa(dc, 60, y, l.Casa, l.LetraCasa, l.FundoCasa, l.LogoCasa);
        dc.DrawText(Txt(l.GC.ToString(), 13, Amarelo, true), new Point(241 + 4, y + 1));
        dc.DrawText(Txt(l.GF.ToString(), 13, Amarelo, true), new Point(257 + 4, y + 1));
        Caixa(dc, 273, y, l.Fora, l.LetraFora, l.FundoFora, l.LogoFora);
        Lance(dc, 453, y, l);
    }

    // um logo de H x H (um pouco menor, centrado na linha)
    static void Logo(DrawingContext dc, Bitmap b, double x, double y)
    {
        using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            dc.DrawImage(b, new Rect(x, y + 1, H - 2, H - 2));
    }

    // a caixa de uma equipa: 180 de largura, nas cores dela; com o fundo verde como a tela, o
    // contorno na cor da letra
    // (com logo da equipa: o logo pequeno a esquerda e o nome logo depois)
    static void Caixa(DrawingContext dc, double x, double y, string nome, Color letra, Color fundo, Bitmap? logo = null)
    {
        var r = new Rect(x, y, 180, H);
        dc.FillRectangle(new SolidColorBrush(fundo), r);
        var bl = new SolidColorBrush(letra);
        using (dc.PushClip(r))
        {
            if (logo != null) Logo(dc, logo, x + 2, y);
            dc.DrawText(Txt(nome, 13, bl), new Point(x + (logo != null ? H + 3 : 4), y + 1));
        }
        if (fundo == Verde)
        {
            Ret(dc, bl, x, y, 180, 1);
            Ret(dc, bl, x, y + H - 2, 180, 1);
            Ret(dc, bl, x, y, 1, H - 1);
            Ret(dc, bl, x + 179 - 1, y, 1, H - 1);
        }
    }

    // o ultimo lance do jogo (seg03:c25e): "nome minuto'"; cartoes e lesao com o desenho antes
    static void Lance(DrawingContext dc, double x, double y, Linha l)
    {
        if (l.LanceTipo == 0) return;
        string txt = l.LanceNome + " " + MinutoTexto.Texto(l.LanceMin, l.LanceAcr);
        switch (l.LanceTipo)
        {
            case TipoLance.Penalti: txt += " (" + T("pen.") + ")"; break;
            case TipoLance.GolAnulado: txt += " - " + T("Anulado"); break;
        }
        if (l.LanceTipo is TipoLance.Amarelo or TipoLance.Expulsao or TipoLance.Lesao)
        {
            double q = H - 6;
            switch (l.LanceTipo)
            {
                case TipoLance.Amarelo:
                    // cartoes.asm, parte 3: o desenho da expulsao em clYellow
                    Ret(dc, Amarelo, x + 3, y + 3, q, q);
                    break;
                case TipoLance.Expulsao:
                    // o vermelho do 2o amarelo: o amarelo 2 pontos acima e a esquerda, o
                    // vermelho por cima (ATRAS, como na versao 2002 do jogo classico)
                    if (l.SegundoAmarelo) Ret(dc, Amarelo, x + 1, y + 1, q, q);
                    Ret(dc, Vermelho, x + 3, y + 3, q, q);
                    break;
                default:
                    // lesao: quadrado branco com a cruz vermelha
                    Ret(dc, Branco, x + 3, y + 3, q, q);
                    Ret(dc, Vermelho, x + 3 + 1, y + 3 + q / 2 - 1, q - 2, 2);
                    Ret(dc, Vermelho, x + 3 + q / 2 - 1, y + 3 + 1, 2, q - 2);
                    break;
            }
            dc.DrawText(Txt(txt, 13, Amarelo), new Point(x + 4 + H, y + 1));
            return;
        }
        dc.DrawText(Txt(txt, 13, Amarelo), new Point(x + 4, y + 1));
    }

    // o TGauge gkPie (Gauges.pas): elipse branca, a fatia na cor da frente a partir do
    // meio-dia no sentido horario e o contorno na cor da frente
    void DesenharRelogio(DrawingContext dc)
    {
        var r = Relogio;
        var frente = new SolidColorBrush(corRelogio);
        var c = r.Center;
        double rx = r.Width / 2 - 0.5, ry = r.Height / 2 - 0.5;
        dc.DrawEllipse(Branco, new Pen(frente, 1), c, rx, ry);
        // nos acrescimos, "+n'" a esquerda do relogio (o relogio fica parado no 45 ou no 90)
        if (acrescimo > 0 && !acabou)
        {
            var ta = Txt("+" + acrescimo + "'", 16, Amarelo, true);
            dc.DrawText(ta, new Point(r.X - 6 - ta.Width, c.Y - ta.Height / 2));
        }
        int prog = Math.Clamp(relProg, relMin, relMax);
        double frac = relMax > relMin ? (double)(prog - relMin) / (relMax - relMin) : 0;
        if (frac <= 0) return;
        if (frac >= 1) { dc.DrawEllipse(frente, null, c, rx, ry); return; }
        double ang = frac * 2 * Math.PI;
        var fim = new Point(c.X + rx * Math.Sin(ang), c.Y - ry * Math.Cos(ang));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(c, true);
            ctx.LineTo(new Point(c.X, c.Y - ry));
            ctx.ArcTo(fim, new Size(rx, ry), 0, frac > 0.5, SweepDirection.Clockwise);
            ctx.EndFigure(true);
        }
        dc.DrawGeometry(frente, null, g);
    }

    // TButton do Windows 95: face cinza, borda dupla saltada, ">"/"<" em Arial negrito 27
    static void Botao(DrawingContext dc, Rect r, string texto, bool ativo)
    {
        double x = r.X, y = r.Y, w = r.Width, h = r.Height;
        Ret(dc, Face, x, y, w, h);
        Ret(dc, Branco, x, y, w - 1, 1);
        Ret(dc, Branco, x, y, 1, h - 1);
        Ret(dc, Preto, x, y + h - 1, w, 1);
        Ret(dc, Preto, x + w - 1, y, 1, h);
        Ret(dc, Luz, x + 1, y + 1, w - 3, 1);
        Ret(dc, Luz, x + 1, y + 1, 1, h - 3);
        Ret(dc, Sombra, x + 1, y + h - 2, w - 2, 1);
        Ret(dc, Sombra, x + w - 2, y + 1, 1, h - 2);
        var t = Txt(texto, 27, ativo ? Preto : Sombra, true);
        var p = new Point(x + (w - t.Width) / 2, y + (h - t.Height) / 2);
        if (!ativo) dc.DrawText(Txt(texto, 27, Branco, true), p + new Point(1, 1));
        dc.DrawText(t, p);
    }

    void Clique(Point p)
    {
        if (arranjo == null) return;
        if (arranjo.Moderno) { CliqueModerno(p); return; }
        var c = p - new Point(0, Titulo);
        if (BotoesVisiveis && Proxima.Contains(c)) { MudarPagina(1); return; }
        if (BotoesVisiveis && Anterior.Contains(c)) { MudarPagina(-1); return; }
        // o clique na caixa de uma equipa: os detalhes do jogo (TsubstitutionDlg, motivo 1),
        // que o motor abre entre dois minutos
        if (!acabou && EquipaEm(c) is var (jg, lado)) { LigacaoPartida.PedirDetalhes(s, jg, lado); return; }
        if (acabou) Seguir();
    }

    // o jogo e o lado da caixa de equipa no ponto c (area cliente), como DesenharJogos e
    // DesenharLinha: casa em 60..240, visitante em 273..453, H de altura
    (JogoAtual, int)? EquipaEm(Point c)
    {
        var a = arranjo;
        if (a == null) return null;
        double lc = CW / a.C;
        for (int k = pagina * a.C; k < Math.Min(a.Colunas.Count, (pagina + 1) * a.C); k++)
        {
            double x0 = Math.Round((k - pagina * a.C) * lc);
            double x = c.X - x0;
            int lado = x >= 60 && x < 240 ? Lado.Casa : x >= 273 && x < 453 ? Lado.Visit : -1;
            if (lado < 0) continue;
            foreach (var (i, k2) in a.Colunas[k].Itens)
            {
                double y = Passo * k2 + 50;
                if (c.Y >= y && c.Y < y + H && linhas[i].Jogo is { } jg && linhas[i].Cabecalho == null && !linhas[i].Vazia)
                    return (jg, lado);
            }
        }
        return null;
    }

    // O quadro ampliado para a janela mantendo a proporcao, como um Viewbox, mas esticado do
    // lado que sobra (o titulo, a moldura e as colunas vao de ponta a ponta, sem faixas).
    sealed class Superficie : Control
    {
        readonly TelaJornada t;
        double escala = 1;
        public Superficie(TelaJornada t)
        {
            this.t = t;
            ClipToBounds = true;
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            InvalidateVisual();
        }

        public override void Render(DrawingContext dc)
        {
            var b = Bounds.Size;
            if (b.Width <= 0 || b.Height <= 0) return;
            var a = t.Arrumar(b);
            escala = Math.Min(b.Width / a.W, b.Height / a.Hq);
            if (a.Moderno)
            {
                t.QL = b.Width / escala;
                t.QA = b.Height / escala;
                using (dc.PushTransform(Matrix.CreateScale(escala, escala)))
                    t.DesenharModerno(dc);
                return;
            }
            t.QL = b.Width / escala;
            t.QA = b.Height / escala;
            // (so alem dos 600 do original: com 8 equipas as linhas ficam como no original)
            double sobra = t.QA - Math.Max(A, AlturaConteudo(a.R, a.Paginas > 1));
            t.Passo = H + 2 + Math.Clamp(Math.Floor(sobra / a.R), 0, 6);
            using (dc.PushTransform(Matrix.CreateScale(escala, escala)))
                t.Desenhar(dc);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            t.Focus();
            var p = e.GetPosition(this);
            t.Clique(new Point(p.X / escala, p.Y / escala));
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------------- demo

    // RETROFOOT_TELA=jornada: uma jornada de verdade, com o motor numa thread como na Sessao.
    // O save vem de RETROFOOT_SAVE (caminho de um .e98) ou do Assets/Saves/20.e98. A escalacao
    // dos humanos e automatica; depois de cada jornada vem a seguinte.
    public static Control Demo()
    {
        var host = new ContentControl { Background = new SolidColorBrush(Verde) };
        host.AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(host) is Window w) w.Title = TelaAcerca.Nome + " - jornada (demo)";
        };
        try
        {
            byte[] bytes;
            var arq = Environment.GetEnvironmentVariable("RETROFOOT_SAVE");
            if (!string.IsNullOrEmpty(arq)) bytes = File.ReadAllBytes(arq);
            else
            {
                using var r = AssetLoader.Open(new Uri("avares://RetroFoot.App/Assets/Saves/20.e98"));
                using var m = new MemoryStream();
                r.CopyTo(m);
                bytes = m.ToArray();
            }
            // a Sessao da a tela o numero de equipas e a velocidade; o jogo e o da demo
            var sessao = new Sessao(Jogo98.Ler(bytes), _ => { });
            if (int.TryParse(Environment.GetEnvironmentVariable("RETROFOOT_MS"), out var ms)) sessao.MsPorMinuto = ms;
            TelaJornada? tela = null;
            var h = new Humano
            {
                Preparar = (j, e, _) => { Escalacao.Automatico(j.R, e); return false; },
                Substituicao = (jg, lado, saiu, motivo) =>
                {
                    if (motivo != 4 && saiu != null) JogoEmCurso.SubstituicaoForcada(jg.Equipa[lado]!, saiu, 0);
                },
                ComecoJornada = (titulo, jn) => Dispatcher.UIThread.Invoke(() =>
                {
                    tela = new TelaJornada(sessao, titulo, jn);
                    host.Content = tela;
                }),
                Minuto = jn =>
                {
                    Dispatcher.UIThread.Invoke(() => tela?.Minuto(jn));
                    int ms = sessao.MsPorMinuto;
                    if (ms > 0) Thread.Sleep(ms);
                },
                FimJornada = jn =>
                {
                    var fim = new ManualResetEventSlim();
                    Dispatcher.UIThread.Post(() => tela!.Fim(jn, fim.Set));
                    fim.Wait();
                },
            };
            // um save com mais de 8 equipas e dos executaveis do launcher (amarelos e 30 jogadores)
            var save = Jogo98.Ler(bytes);
            int n = save.Divisoes.Count > 0 ? save.Divisoes[0].Equipas.Count : 8;
            var rg = n > 8 ? RetroFoot.Motor.Regras.Regras.Launcher(n) : RetroFoot.Motor.Regras.Regras.Original;
            // RETROFOOT_ACRESCIMOS=1: com os acrescimos (Regras.Acrescimos)
            if (Environment.GetEnvironmentVariable("RETROFOOT_ACRESCIMOS") == "1") rg = rg with { Acrescimos = true };
            var op = RetroFoot.Motor.Ciclo.Opcoes.Padrao with { Regras = rg };
            var jogo = new JogoEmCurso(save, (uint)Environment.TickCount, op, h);
            new Thread(() =>
            {
                try { jogo.Ciclo(); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                    Dispatcher.UIThread.Post(() => host.Content = new TextBlock { Text = ex.ToString(), Foreground = Brushes.White });
                }
            }) { IsBackground = true, Name = "motor-demo" }.Start();
        }
        catch (Exception ex)
        {
            host.Content = new TextBlock { Text = "jornada: " + ex, Foreground = Brushes.White };
        }
        return host;
    }
}
