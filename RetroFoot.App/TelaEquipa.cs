using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Patrocinio;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;
using Esc = RetroFoot.Motor.Rodada.Escalacao;
using Mem = RetroFoot.Motor.Partida.Memoria;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;
using SJogo = RetroFoot.Motor.Save.Jogo;
using TRegras = RetroFoot.Motor.Regras.Regras;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// A tela da equipa humana antes de cada jogo: a TprepareGameDlg do jogo classico (seg04:11f3),
// desenhada a mao como o Delphi desenhava, nas posicoes do DFM (versao em Go: assets/
// formularios/TprepareGameDlg.json) com as larguras do launcher (lista 16 pontos mais larga
// para a barra de rolagem, janela de 648). A janela fica no centro de uma tela de 800x600
// (poScreenCenter) e o Viewbox amplia para o tamanho da janela do aparelho. No formato 16:9
// (Formato.Largo) a janela e larga: o elenco com mais colunas, o Notebook ao lado dele e as
// paginas Jogo e Adversario (o jogo, o arbitro, o dinheiro, os morais) numa coluna a direita.
// No visual Moderno (Visual.cs) a mesma tela e desenhada de outro jeito: TelaEquipaModerna.cs.
//
// O motor esta parado enquanto a tela esta aberta: ela le e muda o save (a escalacao, +0x10
// de cada jogador) a vontade. Jogar dispara Terminou(false); RetroFoot > Sair, Terminou(true)
// (a Sessao grava). As escalacoes automaticas usam o Random do jogo (sessao.J.R), como o
// original. O que as EXEs do launcher acrescentam (amarelos, Nota e Risco de lesao,
// campinho, % do preco dos ingressos, chances na Ajuda do Arbitro, 5-0-5) so aparece com
// as regras do launcher (Regras.CartoesAmarelos).
public sealed partial class TelaEquipa : UserControl
{
    // ESBOCO (contrato): dispara Terminou(false) em "Jogar" e Terminou(true) em RetroFoot >
    // Sair (a Sessao grava).
    public event Action<bool>? Terminou;
    bool _terminou;

    readonly Sessao _s;
    readonly SEquipa _e;
    readonly SJogo? _jogo;
    Painel? _painel;

    public TelaEquipa(Sessao s, SEquipa e, SJogo? jogo)
    {
        _s = s; _e = e; _jogo = jogo;
        Montar();
        // o formato, o visual ou o idioma mudou (nas Configuracoes, com esta tela por baixo): refaz
        AttachedToVisualTree += (_, _) =>
        {
            if (_painel != null && (_painel.Largo != Formato.Largo || _painel.Moderno != Visual.Moderno || _painel.Idioma != Idioma.Atual)) Montar();
        };
    }

    void Montar()
    {
        var p = _painel = new Painel(this, _s, _e, _jogo);
        // no celular: a tela em pe do kit (TelaEquipaCelular.cs), com o Painel de controlador
        if (Celular.Ativo)
        {
            Background = CUi.Fundo;
            Content = new Painel.EquipaCelular(this, p, _s, _e);
            return;
        }
        // as sobras quando a janela do sistema tem outra proporcao
        Background = p.Moderno ? Painel.MFundo : Brushes.Black;
        // so a janela da equipa (sem o verde em volta), ampliada mantendo a proporcao dela
        var c = new Canvas { Width = p.JanL, Height = p.JanA, ClipToBounds = true };
        Canvas.SetLeft(p, -p.Jx);
        Canvas.SetTop(p, -p.Jy);
        c.Children.Add(p);
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = c };
    }

    void Terminar(bool sair)
    {
        if (_terminou) return;
        _terminou = true;
        Terminou?.Invoke(sair);
    }

    // RETROFOOT_TELA=equipa: o save do oraculo j3 (8 equipas, humano Juan no JUVENTUS-BR),
    // numa Sessao cujo "mostrar" troca o conteudo. RETROFOOT_LAUNCHER=1 usa as regras do
    // launcher (5-0-5, amarelos); RETROFOOT_MOTOR=1 comeca o motor de verdade (a tela vem do
    // gancho Preparar, com o jogo da jornada).
    public static Control Demo()
    {
        var raiz = new Border { Background = new SolidColorBrush(Color.FromRgb(0, 128, 0)) };
        var arq = AcharSaveDemo();
        if (arq == null)
            return new TextBlock { Text = "Demo: não achei RetroFoot.Motor.Testes/Oraculo/j3-entrada.e98 (ou RETROFOOT_SAVE)", Foreground = Brushes.White };
        var s = Jogo98.Ler(File.ReadAllBytes(arq));
        bool launcher = Environment.GetEnvironmentVariable("RETROFOOT_LAUNCHER") == "1";
        var op = Opcoes.Padrao with { Regras = launcher ? TRegras.Launcher(8) : TRegras.Original };
        // RETROFOOT_PATROC=1: os patrocinadores (a aba Patrocinio), com o MARCAS.DAT de RETROFOOT_JOGO
        // ou do launcher instalado
        if (Environment.GetEnvironmentVariable("RETROFOOT_PATROC") == "1")
            op = op with
            {
                Patrocinios = true,
                Marcas = Marcas.Ler(Environment.GetEnvironmentVariable("RETROFOOT_JOGO") is { Length: > 0 } pj ? pj
                    : Motor.Arquivos.PastaClassica.Instalada() ?? ""),
            };
        // o "Gravar" da Demo vai para a pasta temporaria, nunca para o save do oraculo
        var tmp = Path.Combine(Path.GetTempPath(), "retrofoot-demo-equipa.e98");
        var sessao = new Sessao(s, c => raiz.Child = c, op, tmp);
        sessao.Saiu += () => raiz.Child = Aviso("O jogo acabou (Sair): gravado em " + tmp);
        sessao.Erro += ex => raiz.Child = Aviso("Erro no motor: " + ex);
        if (Environment.GetEnvironmentVariable("RETROFOOT_MOTOR") == "1")
        {
            sessao.Comecar();
            return raiz;
        }
        var m = sessao.J.M;
        var e = m.S.Equipas.FirstOrDefault(x => Mem.Humano(m.TreinadorDa(x)));
        if (e == null) return Aviso("Demo: o save não tem equipa humana");
        // RETROFOOT_PATROC_CONTRATO=1 (com RETROFOOT_PATROC=1): um master (2 temporadas) e um
        // pontual (3 jogos) de exemplo, para ver a aba Patrocinio com os logos
        if (Environment.GetEnvironmentVariable("RETROFOOT_PATROC_CONTRATO") == "1")
        {
            e.SetB(RetroFoot.Motor.Patrocinio.Contrato.MMarca, 3); e.SetB(RetroFoot.Motor.Patrocinio.Contrato.MAnos, 2);
            e.SetD(RetroFoot.Motor.Patrocinio.Contrato.MValor, 850000);
            e.SetB(RetroFoot.Motor.Patrocinio.Contrato.PMarca, 7); e.SetB(RetroFoot.Motor.Patrocinio.Contrato.PJogos, 3);
            e.SetD(RetroFoot.Motor.Patrocinio.Contrato.PValor, 50000);
        }
        var jogo = m.S.Jogos.FirstOrDefault(j => j.Casa == e.ID || j.Fora == e.ID);
        if (jogo == null)
        {
            // como o comeco da jornada (seg05:0c41): os jogos das 4 divisoes
            m.LimparJogos();
            for (int d = 0; d <= 3 && d < m.S.Divisoes.Count; d++) m.CriarJogosDivisao(d);
            jogo = m.S.Jogos.FirstOrDefault(j => j.Casa == e.ID || j.Fora == e.ID);
        }
        var t = new TelaEquipa(sessao, e, jogo);
        t.Terminou += sair => raiz.Child = Aviso(sair
            ? "RetroFoot > Sair: aqui a Sessao gravaria o save e o jogo acabaria."
            : "Jogar: aqui o motor seguiria para a jornada.");
        raiz.Child = t;
        return raiz;
    }

    static Control Aviso(string s) => new TextBlock
    {
        Text = s, Foreground = Brushes.White, FontSize = 18, Margin = new Thickness(20),
        TextWrapping = TextWrapping.Wrap,
    };

    static string? AcharSaveDemo()
    {
        var env = Environment.GetEnvironmentVariable("RETROFOOT_SAVE");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        foreach (var ini in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var d = new DirectoryInfo(ini); d != null; d = d.Parent)
            {
                var f = Path.Combine(d.FullName, "RetroFoot.Motor.Testes", "Oraculo", "j3-entrada.e98");
                if (File.Exists(f)) return f;
            }
        return null;
    }

    // ==================================================================================
    // O desenho e os cliques, em pontos de uma tela de 800x600
    // ==================================================================================
    sealed partial class Painel : Control
    {
        // ------------------------------------------------------------ medidas
        // No formato 16:9 (Formato.Largo) a janela e larga (1067x600, a tela toda): o elenco
        // com mais colunas, o Notebook (sem as abas Jogo e Adversario) ao lado dele, tao alto
        // quanto a lista, e as duas paginas numa coluna a direita, com a caixa do adversario em
        // cima.
        readonly bool _largo, _moderno;
        internal bool Largo => _largo;
        internal string Idioma { get; }
        internal bool Moderno => _moderno;
        readonly double L = 800, A = 600;
        // a janela (moldura do Wine: borda de 3, titulo de 25, menu de 19) e a area cliente
        internal readonly double JanL = 654, JanA = 485;
        internal readonly double Jx, Jy;
        readonly double Cx, Cy, CL = 648, CA = 434;
        const double MenuY = 29, MenuA = 19;
        // componentes (coordenadas da area cliente, as do DFM)
        readonly Rect RLista = new(4, 72, 369, 353);
        readonly Rect RItens = new(6, 74, 349, 349);     // sem a borda e a barra
        readonly Rect RBarra = new(355, 74, 16, 349);
        readonly Point PNotebook = new(374, 72);
        readonly Rect RNotebook = new(374, 72, 273, 329);
        readonly Rect RTabSet = new(384, 400, 257, 25);
        readonly Rect ROponente = new(376, 32, 265, 33);
        // a coluna da pagina Jogo e os titulos das colunas (so no 16:9)
        readonly Rect RJogoLargo, RTitulos;
        // a pagina Jogo: a tabela, "Arbitro", o nome dele, "Ultimo jogo", o dinheiro, "Moral" e
        // o medidor; no 16:9, espalhados na altura da coluna
        readonly double[] YJogo = { 16, 96, 120, 168, 232, 272, 296 };
        // dentro do Notebook
        static readonly Rect RJogar = new(104, 232, 64, 64);
        static readonly Rect RCalendario = new(112, 256, 64, 64);
        static readonly Rect RPlantel = new(184, 256, 64, 64);
        static readonly Rect RRenovar = new(8 + 56, 280 + 12, 129, 25);
        static readonly Rect RVender = new(8 + 56 + 129 + 6, 280 + 12, 68, 25);   // o atalho do Jogador > Vender
        static readonly Rect RNomeAdv = new(8, 8, 241, 25);
        Rect RArbitro => new(16, YJogo[2], 201, 16);                      // textReferee (OnClick)
        // o campo de % do preco dos ingressos (ingressos.py: TSpinEdit 204, 305, 52 x 24)
        static readonly Rect RIngressos = new(150, 307, 107, 22);   // o preco do ingresso (antes o %)

        const int PgJogo = 0, PgJogador = 1, PgFinancas = 2, PgEscalacao = 3, PgAdversario = 4, PgPatrocinio = 5, PgMensagem = 7;
        static readonly string[] AbasOriginais = { "Jogo", "Jogador", "Finanças", "Escalação", "Adversário" };
        // os patrocinadores do launcher (patrocinio.py): a pagina "Patrocínio" na posicao 5 e a
        // fonte das abas Arial -9 (6 abas nao cabiam com a MS Sans Serif)
        static readonly string[] AbasPatrocinio = { "Jogo", "Jogador", "Finanças", "Escalação", "Adversário", "Patrocínio" };
        // no 16:9 as paginas Jogo e Adversario ficam sempre a vista (na coluna da direita): as
        // abas sao Jogador, Financas, Escalacao (e Patrocinio)
        int[] PaginasAbas => _largo
            ? (_patrocinio ? new[] { PgJogador, PgFinancas, PgEscalacao, PgPatrocinio } : new[] { PgJogador, PgFinancas, PgEscalacao })
            : (_patrocinio ? new[] { 0, 1, 2, 3, 4, 5 } : new[] { 0, 1, 2, 3, 4 });
        string[] Abas => PaginasAbas.Select(pg => T(AbasPatrocinio[pg])).ToArray();
        int PaginaInicial => _largo && !_moderno ? PgJogador : PgJogo;
        FontFamily FonteAbas => _patrocinio ? Arial : SansSerif;
        double TamAbas => _patrocinio ? 9 : 11;
        // a folga de cada aba alem do texto (as 6 abas cabem nos 257 pontos do TTabSet)
        double FolgaAbas => _patrocinio ? 8 : 14;

        // ------------------------------------------------------------ fontes e cores
        static readonly FontFamily Arial = new("avares://RetroFoot.App/Assets/Fontes#Liberation Sans");
        static readonly FontFamily Times = new("avares://RetroFoot.App/Assets/Fontes#Liberation Serif");
        static readonly FontFamily SansSerif = new("avares://RetroFoot.App/Assets/Fontes#MS Sans Serif");
        static readonly IBrush Face = Pincel(245, 245, 245), Branco = Brushes.White, Preto = Brushes.Black;
        static readonly IBrush Sombra = Pincel(166, 166, 166), Escura = Pincel(106, 106, 106), Luz3D = Pincel(227, 227, 227);
        static readonly IBrush CorTitulo = Pincel(50, 150, 250), Cinza = Pincel(128, 128, 128);
        static readonly IBrush Destaque = Pincel(50, 150, 250);

        static IBrush Pincel(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));

        // ------------------------------------------------------------ estado
        readonly TelaEquipa _tela;
        readonly Sessao _s;
        readonly SEquipa _e;
        readonly SJogo? _jogo;
        readonly SEquipa? _adv;
        readonly bool _launcher, _505;
        readonly bool _patrocinio;     // Opcoes.Patrocinios: a aba Patrocinio
        readonly IBrush _letra, _fundo, _advLetra, _advFundo;
        readonly List<Menu> _menus;

        int _pagina = PgJogo;
        SJogador? _sel;                // o jogador mostrado na aba Jogador
        string _mensagem = "";
        bool _jogarForcado;            // o Melhores (4:34d1) liga o Jogar mesmo invalido
        bool _arbitro;                 // a Ajuda do Arbitro (TRefereeAidDlg) aberta
        double _rolagem;
        int _menuAberto = -1, _itemSobre = -1;
        // o menu do botao direito num jogador do elenco (_menuAberto = Contexto)
        const int Contexto = 1000;
        Menu? _contexto;
        Point _contextoPos;
        // arraste na lista (toque) e na barra de rolagem
        Point? _arraste;
        double _rolagemInicio;
        bool _moveu, _arrasteBarra;

        record Item(string Texto, string Atalho = "", Action? Acao = null, Func<bool>? Habilitado = null);
        record Menu(string Texto, List<Item> Itens);

        public Painel(TelaEquipa tela, Sessao s, SEquipa e, SJogo? jogo)
        {
            _tela = tela; _s = s; _e = e; _jogo = jogo;
            s.EquipaHumana = e;
            _largo = Formato.Largo;
            _moderno = Visual.Moderno;
            Idioma = RetroFoot.App.Idioma.Atual;
            if (_moderno)
            {
                // o visual Moderno ocupa a tela toda (sem a moldura da janela)
                JanL = L = _largo ? 1067 : 800; JanA = A = 600;
                CL = L; CA = A;
                MedidasModernas(L, out RLista, out RItens, out RBarra);
            }
            else if (_largo)
            {
                JanL = L = 1067; JanA = A = 600;
                CL = JanL - 6; CA = JanA - 51;
                // a coluna da pagina Jogo encostada a direita, com a caixa do adversario em cima
                // (onde o original a tem) e o Notebook entre ela e a lista, da altura da lista
                double xj = CL - 4 - 273, xn = xj - 8 - 273, alto = CA - 76;
                RJogoLargo = new(xj, 72, 273, alto);
                ROponente = new(xj + 2, 32, 265, 33);
                PNotebook = new(xn, 72);
                RNotebook = new(xn, 72, 273, alto - 25);
                RTabSet = new(xn + 10, 72 + alto - 25, 257, 25);
                // a pagina Jogo um pouco mais junta e, embaixo dela, a Adversario (ColunaLarga)
                YJogo = new double[] { 0, 52, 72, 104, 144, 184, 204 };
                RLista = new(4, 72, xn - 8 - 4, alto);
                // os titulos das colunas na 1a linha da lista
                RTitulos = new(6, 74, RLista.Width - 20, 16);
                RItens = new(6, 90, RLista.Width - 20, RLista.Height - 20);
                RBarra = new(RItens.Right, 74, 16, RLista.Height - 4);
            }
            Jx = Math.Floor((L - JanL) / 2); Jy = Math.Floor((A - JanA) / 2);
            Cx = _moderno ? 0 : Jx + 3; Cy = _moderno ? 0 : Jy + 48;
            _pagina = PaginaInicial;
            Width = L; Height = A;
            ClipToBounds = true;
            Focusable = true;
            var rg = s.J.M.Regras;
            _launcher = rg.CartoesAmarelos;
            _505 = rg.Tatica505;
            _patrocinio = s.J.Op.Patrocinios;
            if (jogo != null) _adv = s.Equipa(jogo.Casa == e.ID ? jogo.Fora : jogo.Casa);
            (_letra, _fundo) = Cores(e);
            (_advLetra, _advFundo) = Cores(_adv);
            _menus = MontarMenus();
            ConferirEscalacao();
        }

        // o foco do teclado (F1-F12, A, M, T, espaco...) fica na tela
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _s.SemDialogos -= Voltar;
            _s.SemDialogos += Voltar;
            TelaDinheiro.Mudou -= DinheiroMudou;
            TelaDinheiro.Mudou += DinheiroMudou;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
        }

        // ============================================================ menus
        List<Menu> MontarMenus()
        {
            var r = _s.J.R;
            var escalar = new List<Item>();
            for (int i = 0; i < 12; i++)
            {
                int n = i + 1;
                escalar.Add(new Item(Esc.Formacoes[i].Nome, "F" + n,
                    () => { Esc.EscalarFormacao(r, _e, n); DepoisDeEscalar(); },
                    () => Esc.FormacoesHabilitadas(_e)[n - 1]));
            }
            // tools/divisoes/tatica505.py: desligado (com a tecla T) se nao ha 1 G, 5 D e 5 A
            // disponiveis, como as outras (seg03:4b3e)
            if (_505) escalar.Add(new Item("5-0-5", "T", () => { Esc.EscalarTatica505(r, _e); DepoisDeEscalar(); },
                () => Esc.FormacaoPermitida(_e, Esc.Tatica505.CG, Esc.Tatica505.CD, Esc.Tatica505.CM, Esc.Tatica505.CA)));
            escalar.Add(new Item("-"));
            escalar.Add(new Item(T("Automático"), "A", () => { Esc.Automatico(r, _e); DepoisDeEscalar(); }));
            escalar.Add(new Item(T("Melhores"), "M", () => { Esc.Melhores(r, _e); DepoisDeEscalar(); _jogarForcado = true; }));
            // os itens que o original mostra so com o add-in (FormShow, 4:1eb4-1fcd: Visible
            // dos TMenuItem): Gestao 0x0E, Emprestimo 0x13, Sair sem gravar 0x46, Observados e
            // Procurar 0x0C, Contratacoes 0x10 (ligado se a equipa tem compras a chegar, +0xC7)
            bool Ai(int id) => Registo.AddIn(id);
            List<Item> Se(bool c, params Item[] it) => c ? it.ToList() : new();
            var menuJogo = new List<Item>
            {
                new(T("Opções..."), "", () => Campeonato.Opcoes(_s, Voltar)),
                new(T("Configurações..."), "", () => { var t = _tela; var c = new TelaConfiguracoes(emCurso: _s); c.Fechou += _ => { _s.Mostrar(t); Voltar(); }; _s.Mostrar(c); }),
                new(T("Gravar"), "", Gravar),
            };
            menuJogo.AddRange(Se(Ai(0x46), new Item(T("Sair sem salvar"), "Shift+Ctrl+F4", SairSemSalvar)));
            // "menu: Sair": grava e sai (pt-PT "Sair", pt-BR "Salvar e sair")
            menuJogo.Add(new(T("menu: Sair"), "Alt+F4", () => _tela.Terminar(true)));
            menuJogo.Add(new(T("Sobre..."), "", () => TelaAcerca.Abrir(_s, Voltar)));
            // (o Imprimir do original fica de fora: nao faz sentido hoje)
            var time = new List<Item>();
            time.AddRange(Se(Ai(0x13), new Item(T("Pedir / pagar empréstimo..."), "", () => TelaEmprestimo.Abrir(_s, _e, Voltar))));
            time.Add(new(T("Estádio..."), "", () => TelaEstadio.Abrir(_s, _e, Ai(0x13), Voltar)));
            time.Add(new(T("Histórico..."), "", () => TelaListas.HistoricoEquipa(_s, _e, Voltar)));
            var jogador = new List<Item> { new(T("Vender"), "V", Vender, () => _sel != null) };
            jogador.AddRange(Se(Ai(0x0C),
                new Item(T("Jogadores sob observação..."), "", () => TelaObservados.Abrir(_s, _e, AbrirPlantelDe, Voltar)),
                new Item(T("Procurar..."), "", () => TelaProcura.Abrir(_s, _e, AbrirPlantelDe, Voltar))));
            jogador.Add(new("-"));
            jogador.Add(new(T("Últimas transferências..."), "", () => Campeonato.UltimasTransferencias(_s)));
            jogador.AddRange(Se(Ai(0x10), new Item(T("Contratações..."), "", () => TelaListas.Contratacoes(_s, _e, Voltar), () => (short)_e.WC7 > 0)));
            var tecnico = new List<Item> { new(T("História..."), "", () => Campeonato.Historia(_s, _e)) };
            tecnico.AddRange(Se(Ai(0x0E), new Item(T("Gestão de técnicos..."), "", () => Campeonato.Gestao(_s, Voltar))));
            tecnico.Add(new(T("Ranking..."), "", () => Campeonato.Ranking(_s)));
            tecnico.Add(new("-"));
            tecnico.Add(new(T("Perfil..."), "", () => Campeonato.Perfil(_s, _e, Voltar)));
            return new List<Menu>
            {
                new(TelaAcerca.Nome, menuJogo),
                new(T("Escalar"), escalar),
                new(T("Time"), time),
                new(T("Jogador"), jogador),
                new(T("Campeonato"), new()
                {
                    new(T("Classificação..."), "C", Classificacao),
                    new(T("Artilheiros..."), "", () => Campeonato.Artilheiros(_s)),
                    new(T("Calendário..."), "", () => Campeonato.Calendario(_s, _e, Voltar)),
                    new("-"),
                    new(T("Últimos vencedores..."), "", () => Campeonato.UltimosVencedores(_s)),
                    new(T("Títulos..."), "", () => TelaTitulos.Abrir(_s)),
                    new(T("Maiores artilheiros..."), "", () => Campeonato.ArtilheirosDeSempre(_s)),
                }),
                new(T("Técnico"), tecnico),
            };
        }

        // ------------------------------------------------------------ as outras janelas
        // depois de cada janela (o ultimo dialogo fechou, Sessao.SemDialogos): a tela se refaz
        // (o dinheiro, o elenco e a escalacao podem ter mudado) e recupera o teclado
        void Voltar()
        {
            if (_sel != null && !_e.Jogadores.Contains(_sel)) _sel = null;
            ConferirEscalacao();
            InvalidateVisual();
            Focus();
            Redesenhou?.Invoke();
        }

        // a tela do celular (TelaEquipaCelular.cs), que nao e o Painel, se refaz junto: a
        // renovacao de contrato termina depois de a janela fechar (o + do salario revisto)
        internal Action? Redesenhou;

        void DinheiroMudou(SEquipa e) { InvalidateVisual(); Redesenhou?.Invoke(); }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _s.SemDialogos -= Voltar;
            TelaDinheiro.Mudou -= DinheiroMudou;
        }

        // o plantel da equipa do jogador escolhido nos observados e na procura, com esta
        // equipa a comprar (3:d55c modo 3)
        void AbrirPlantelDe(ushort id)
        {
            var (eq, _) = MercadoJogo.EquipaDoJogador(_s.S, id);
            if (eq != null) TelaPlantel.Abrir(_s, eq, _e, id, Voltar);
        }

        // Campeonato > Classificacao (CampeonatoClassificaoClick, 4:325c): a classificacao sem
        // o relogio, no lugar da tela, e a volta
        void Classificacao()
        {
            var t = _tela;
            var c = new TelaClassificacao(_s, 0, relogio: false);
            c.Fechou += () => { _s.Mostrar(t); Voltar(); };
            _s.Mostrar(c);
        }

        // RetroFoot > Sair sem salvar (Sairsemgravar1Click, 4:3f25): o jogo acaba sem gravar
        void SairSemSalvar()
        {
            _s.SemGravar = true;
            _tela.Terminar(true);
        }

        // Jogador > Vender (Vender1Click, 4:3d0e e sellBtnClick, 4:455a): o leilao com a
        // janela; vendido, a escalacao zera (SelectPlayers(0, 0, 0, 0, sem banco), 4:462c);
        // vendido ou nao, a pagina Jogo
        void Vender()
        {
            var p = _sel;
            if (p == null) return;
            TelaLeilao.Vender(_s, _e, p, Mensagem, r =>
            {
                if (r == ResultadoVenda.Cancelado) { Voltar(); return; }
                if (r == ResultadoVenda.Vendido) Esc.SelecionarJogadores(_s.J.R, _e, 0, 0, 0, 0, false, true);
                _pagina = PaginaInicial;
                Voltar();
            });
        }

        // o botao direito num jogador do elenco: escolhe-o e abre o menu dele (Vender,
        // Renovar contrato, Ver ficha), com as mesmas regras do menu Jogador e da aba Jogador
        void AbrirContexto(Point p)
        {
            _menuAberto = -1;
            _itemSobre = -1;
            var pc = Cliente(p);
            var j = RItens.Contains(pc) ? JogadorEm(pc.Y) : null;
            if (j == null) { InvalidateVisual(); return; }
            _sel = j;
            MostrarEscolhido();
            _contexto = new Menu("", new List<Item>
            {
                new(T("Vender"), "V", Vender, () => _sel != null),
                new(T("Renovar contrato..."), "", Renovar, () => _sel != null && MercadoJogo.PodeMudarSalario(_sel)),
                new("-"),
                new(T("Ver ficha"), "", MostrarFicha),
            });
            _contextoPos = p;
            _menuAberto = Contexto;
            InvalidateVisual();
        }

        // Renovar contrato (ChangeSalaryBtnClick, 4:3f87 e newSalaryBtnClick, 4:470c): recusado
        // o "Exijo", o leilao e, vendido ou nao, SelectPlayers (4:48c1) e a pagina Jogo
        void Renovar()
        {
            var p = _sel;
            if (p == null || !MercadoJogo.PodeMudarSalario(p)) return;
            TelaNovoSalario.Abrir(_s, _e, p, r =>
            {
                if (r is ResultadoSalario.Vendido or ResultadoSalario.NaoVendido)
                {
                    Esc.SelecionarJogadores(_s.J.R, _e, 0, 0, 0, 0, false, true);
                    _pagina = PaginaInicial;
                }
                Voltar();
            });
        }

        // RetroFoot > Gravar: o save como o Sair o gravaria (jogoSairClick, 4:3146): DS:271E =
        // esta equipa (a preparacao fica a meio) e DS:271A = 1 se o jogo e da Taca; depois o
        // motor continua como se nada fosse.
        void Gravar()
        {
            var m = _s.J.M;
            short retomar = m.Retomar;
            bool jogada = m.JornadaJogada;
            try
            {
                m.Retomar = (short)_e.ID;
                if (_jogo != null && _jogo.Taca != 0) m.JornadaJogada = true;
                _s.Gravar();
                Mensagem(T("Jogo salvo.") + "\n\n" + Path.GetFileName(_s.Arquivo));
            }
            catch (Exception ex) { Mensagem(T("Não foi possível salvar o jogo.") + "\n\n" + ex.Message); }
            finally { m.Retomar = retomar; m.JornadaJogada = jogada; }
        }

        void Mensagem(string s)
        {
            _mensagem = s;
            _pagina = PgMensagem;
            InvalidateVisual();
        }

        // ============================================================ escalacao
        // fora -> titular -> reserva -> fora; lesionado ou suspenso fica sempre fora
        void Alternar(SJogador p)
        {
            if (p.JogosLesao > 0 || p.Suspensao > 0) Mem.MudarEscalacao(p, Mem.EscFora);
            else Mem.MudarEscalacao(p, (ushort)((Mem.Escalacao(p) + 1) % 3));
            _jogarForcado = false;
            ConferirEscalacao();
        }

        // o fim de SelectPlayers (4:2ed1-2efc): a pagina Seleccao e o CheckSelection
        void DepoisDeEscalar()
        {
            _jogarForcado = false;
            ConferirEscalacao();
            _pagina = PgEscalacao;
            InvalidateVisual();
        }

        // TprepareGameDlg.CheckSelection (seg04:38b0): +0x4D = Min(reservas, 3); o Jogar fica
        // ativo se nenhum escalado esta lesionado ou suspenso, ha 1 goleiro titular, os
        // titulares de linha sao Min(disponiveis de linha, 10) e ha ate 5 reservas.
        // Diferenca (como na versao em Go): sem goleiro disponivel o original nao deixa
        // jogar (e preciso comprar um); como a compra ainda nao existe, aceita 0 goleiros.
        bool ConferirEscalacao()
        {
            int dispG = 0, dispL = 0, titG = 0, titL = 0, res = 0;
            bool ok = true;
            foreach (var p in _e.Jogadores)
            {
                bool disp = p.JogosLesao == 0 && p.Suspensao == 0;
                if (disp) { if (p.Posicao == 0) dispG++; else dispL++; }
                switch (Mem.Escalacao(p))
                {
                    case Mem.EscTitular:
                        if (p.Posicao == 0) titG++; else titL++;
                        if (!disp) ok = false;
                        break;
                    case Mem.EscReserva:
                        res++;
                        if (!disp) ok = false;
                        break;
                }
            }
            Mem.MudarSubstituicoes(_e, (short)Math.Min(res, Esc.MaxSubstituicoes));
            int precisaG = dispG == 0 ? 0 : 1;
            return ok && titG == precisaG && titL == Math.Min(dispL, 10) && res <= Esc.MaxReservas;
        }

        bool JogarHabilitado() => ConferirEscalacao() || _jogarForcado;

        void Jogar()
        {
            if (JogarHabilitado()) _tela.Terminar(false);
        }

        // ============================================================ dados
        static (IBrush Letra, IBrush Fundo) Cores(SEquipa? e)
        {
            if (e == null) return (Preto, Branco);
            return (CorDelphi(e.CorLetra, Preto), CorDelphi(e.CorFundo, Branco));
        }

        // TColor: $00BBGGRR; as cores do sistema (bit de cima) nao aparecem nas equipas
        static IBrush CorDelphi(byte[] b, IBrush padrao) =>
            b.Length < 4 || (b[3] & 0x80) != 0 ? padrao : new SolidColorBrush(Color.FromRgb(b[0], b[1], b[2]));

        static string Texto(byte[] b) => b.Length == 0 ? "" : Encoding.Latin1.GetString(Cifra.TextoEFT(b).Texto);
        static string NomeJogador(SJogador p) => Encoding.Latin1.GetString(RetroFoot.Motor.Partida.Jornada.NomeNaTela(p));
        static string PaisDe(byte[] b) => Texto(b).ToUpperInvariant();

        static string Pascal(byte[] b, int max)
        {
            if (b.Length == 0) return "";
            int n = Math.Min(Math.Min((int)b[0], max), b.Length - 1);
            return Encoding.Latin1.GetString(b, 1, n);
        }

        // os textos que o jogo poe em portugues de Portugal (o titulo do jogo, do save), no
        // idioma da tela (so na tela: o save fica como esta)
        static string Br(string s) => TelaJornada.TituloNoIdioma(s);

        // "Distrital" ou "3º lugar · 1ª Divisão" (o nome da divisao vem do save)
        static string LugarNaDivisao(int lugar, string divisao) => T("{0}º lugar · {1}", lugar, divisao);

        static Dictionary<string, string>? _paises;
        static string NomePais(string cod)
        {
            if (_paises == null)
            {
                _paises = new();
                try
                {
                    using var r = new StreamReader(AssetLoader.Open(new Uri("avares://RetroFoot.App/Assets/paises.tsv")));
                    for (string? l; (l = r.ReadLine()) != null;)
                    {
                        var p = l.Split('\t');
                        if (p.Length >= 2) _paises[p[0].Trim().ToUpperInvariant()] = p[1].Trim();
                    }
                }
                catch { }
            }
            return _paises.TryGetValue(cod, out var n) ? n : cod;
        }

        static readonly Dictionary<string, Bitmap?> _imagens = new();
        static Bitmap? Imagem(string caminho)
        {
            if (_imagens.TryGetValue(caminho, out var b)) return b;
            try { b = new Bitmap(AssetLoader.Open(new Uri("avares://RetroFoot.App/Assets/" + caminho))); }
            catch { b = null; }
            _imagens[caminho] = b;
            return b;
        }

        static readonly string[] Comportamentos = { "Fair play", "Cordeirinho", "Cavalheiro", "Caneleiro", "Caceteiro", "Sarrafeiro" };

        Moeda Moeda => Moeda.DoSave(_s.S);

        // o dinheiro com o simbolo da moeda do jogo (FormatoDinheiro)
        string Dinheiro(long v) => FormatoDinheiro.Valor(Moeda, v);

        static double Moral(SEquipa e) => Ext.DeReal48(e.Moral).Float64();

        // as linhas do elenco na ordem da tela: G, D, M, A e, em cada posicao, pelo nome
        // (so a vista: a ordem da lista da equipa, que o Random das escalacoes usa, nao muda)
        List<SJogador> Ordem()
        {
            var q = _e.Jogadores.Select((p, i) => (p, i)).OrderBy(x => x.p.Posicao);
            // a coluna escolhida no cabecalho (Ordenar), dentro de cada posicao
            if (_ordemCol != null && _ordemCol != "Jogador")
            {
                Func<SJogador, IComparable> k = _ordemCol switch
                {
                    "Força" => p => p.Forca,
                    "Salário" => p => p.Salario,
                    "Jogos" => p => Hist(p, Mem.HistJogos),
                    "Gols" => p => Hist(p, Mem.HistGolos),
                    "Temp." => p => Mem.Golos14(p),
                    "Nota" => p => p.Nota,
                    "País" => p => PaisDe(p.Pais),
                    _ => p => 0,
                };
                q = _ordemDesc ? q.ThenByDescending(x => k(x.p)) : q.ThenBy(x => k(x.p));
            }
            var nome = Comparer<byte[]>.Create((a, b) => a.AsSpan().SequenceCompareTo(b));
            q = _ordemCol == "Jogador" && _ordemDesc
                ? q.ThenByDescending(x => RetroFoot.Motor.Partida.Jornada.NomeNaTela(x.p), nome)
                : q.ThenBy(x => RetroFoot.Motor.Partida.Jornada.NomeNaTela(x.p), nome);
            return q.ThenBy(x => x.i).Select(x => x.p).ToList();
        }

        string? _ordemCol;
        bool _ordemDesc;

        // o clique num titulo de coluna: a 1a vez do maior para o menor (o nome e o pais em
        // ordem alfabetica), de novo inverte
        void Ordenar(string col)
        {
            if (_ordemCol == col) _ordemDesc = !_ordemDesc;
            else { _ordemCol = col; _ordemDesc = col is not ("Jogador" or "País"); }
            InvalidateVisual();
        }

        static double AlturaLinha(List<SJogador> o, int i) =>
            i < o.Count - 1 && o[i + 1].Posicao != o[i].Posicao ? 20 : 16;

        // as linhas da lista, do topo dela: o jogador (null = o titulo de uma posicao, so no
        // Moderno), a posicao, o y e a altura
        List<(SJogador? P, int Pos, double Y, double H)> Linhas()
        {
            var o = Ordem();
            if (_moderno) return LinhasModernas(o);
            var r = new List<(SJogador?, int, double, double)>();
            double y = 0;
            for (int i = 0; i < o.Count; i++)
            {
                double h = AlturaLinha(o, i);
                r.Add((o[i], o[i].Posicao, y, h));
                y += h;
            }
            return r;
        }

        double AlturaElenco()
        {
            var l = Linhas();
            return l.Count == 0 ? 0 : l[^1].Y + l[^1].H + (_moderno ? 6 : 0);
        }

        // historial de 4 bytes; com os amarelos do launcher nos 2 de cima, so os 2 de baixo
        int Hist(SJogador p, int qual)
        {
            int v = Mem.Historial(p, qual);
            // os 2 bytes de cima das lesoes e das expulsoes guardam os amarelos do launcher
            // (cartoes.asm): um save feito com o launcher os traz mesmo com as regras originais
            return qual == Mem.HistVermelhos || qual == Mem.HistLesoes ? v & 0xFFFF : v;
        }

        static int Amarelos(SJogador p) => Campos.JgB(p, Campos.JgAmarelosAcumulados);

        // ultimo jogo entre as duas (4:0ec3): na lista +0x65 do mandante (Eventos2: golos do
        // mandante e do visitante, ano), do ponto de vista do humano
        string UltimoConfronto()
        {
            if (_adv == null || _jogo == null) return "";
            var (casa, fora) = _jogo.Casa == _e.ID ? (_e, _adv) : (_adv, _e);
            EventoEquipa2? ev = null;
            foreach (var x in casa.Eventos2) if (x.W0A == fora.ID) ev = x;
            if (ev == null) return "";
            int meus = ev.B04, deles = ev.B05;
            if (casa != _e) (meus, deles) = (deles, meus);
            string l = meus > deles ? T("V") : meus < deles ? T("D") : T("E");
            return $"{l} {meus}:{deles} ({ev.D06})";
        }

        // ============================================================ desenho
        static FormattedText Txt(string s, double tam, IBrush cor, bool negrito = false, FontFamily? fonte = null,
            bool italico = false, double largura = 0)
        {
            var t = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(fonte ?? Arial, italico ? FontStyle.Italic : FontStyle.Normal, negrito ? FontWeight.Bold : FontWeight.Normal),
                tam * Acessibilidade.Texto, cor);
            if (largura > 0) t.MaxTextWidth = largura;
            return t;
        }

        static double Largura(string s, double tam, bool negrito = false, FontFamily? fonte = null) =>
            Txt(s, tam, Preto, negrito, fonte).WidthIncludingTrailingWhitespace;

        static void Escreve(DrawingContext dc, string s, double x, double y, double tam, IBrush cor,
            bool negrito = false, FontFamily? fonte = null, bool italico = false, double largura = 0) =>
            dc.DrawText(Txt(s, tam, cor, negrito, fonte, italico, largura), new Point(x, y));

        static void Direita(DrawingContext dc, string s, double xFim, double y, double tam, IBrush cor, bool negrito = false)
        {
            var t = Txt(s, tam, cor, negrito);
            dc.DrawText(t, new Point(xFim - t.WidthIncludingTrailingWhitespace, y));
        }

        static void Centro(DrawingContext dc, string s, Rect r, double y, double tam, IBrush cor, bool negrito = false)
        {
            var t = Txt(s, tam, cor, negrito);
            dc.DrawText(t, new Point(r.X + (r.Width - t.WidthIncludingTrailingWhitespace) / 2, y));
        }

        static void Linha(DrawingContext dc, IBrush cor, double x0, double y0, double x1, double y1) =>
            dc.DrawLine(new Pen(cor, 1), new Point(x0 + 0.5, y0 + 0.5), new Point(x1 + 0.5, y1 + 0.5));

        // moldura 3D de 1 ponto: claro em cima e a esquerda, escuro embaixo e a direita
        static void Moldura(DrawingContext dc, Rect r, IBrush claro, IBrush escuro)
        {
            dc.FillRectangle(claro, new Rect(r.X, r.Y, r.Width, 1));
            dc.FillRectangle(claro, new Rect(r.X, r.Y, 1, r.Height));
            dc.FillRectangle(escuro, new Rect(r.X, r.Bottom - 1, r.Width, 1));
            dc.FillRectangle(escuro, new Rect(r.Right - 1, r.Y, 1, r.Height));
        }

        static void Afundada(DrawingContext dc, Rect r)
        {
            Moldura(dc, r, Sombra, Branco);
            Moldura(dc, r.Deflate(1), Escura, Luz3D);
        }

        static void Saltada(DrawingContext dc, Rect r)
        {
            Moldura(dc, r, Luz3D, Escura);
            Moldura(dc, r.Deflate(1), Branco, Sombra);
        }

        public override void Render(DrawingContext dc)
        {
            if (_moderno) { RenderModerno(dc); return; }
            // a janela: moldura, titulo e menu do Wine
            var jan = new Rect(Jx, Jy, JanL, JanA);
            dc.FillRectangle(Face, jan);
            Moldura(dc, jan, Luz3D, Escura);
            Moldura(dc, jan.Deflate(1), Branco, Sombra);
            dc.FillRectangle(CorTitulo, new Rect(Jx + 3, Jy + 3, JanL - 6, 25));
            using (dc.PushClip(new Rect(Jx + 3, Jy + 3, JanL - 6, 25)))
            {
                // o logo da equipa (se houver) no lugar do icone da janela, antes do nome
                double xn = Jx + 5;
                if (Escudos.Escudo(_e) is { } logo)
                {
                    using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                        dc.DrawImage(logo, new Rect(Jx + 5, Jy + 5, 21, 21));
                    xn = Jx + 30;
                }
                Escreve(dc, Texto(_e.NomeCompleto), xn, Jy + 7, 13, Preto, fonte: Arial);
            }
            DesenharBarraMenu(dc);

            using (dc.PushTransform(Matrix.CreateTranslation(Cx, Cy)))
            using (dc.PushClip(new Rect(0, 0, CL, CA)))
            {
                dc.FillRectangle(_fundo, new Rect(0, 0, CL, CA));
                DesenharCabecalho(dc);
                DesenharElenco(dc);
                if (_largo)
                    using (dc.PushTransform(Matrix.CreateTranslation(RJogoLargo.X, RJogoLargo.Y)))
                    using (dc.PushClip(new Rect(0, 0, RJogoLargo.Width, RJogoLargo.Height)))
                    {
                        PaginaJogo(dc);
                        AdversarioLargo(dc);
                    }
                dc.FillRectangle(_fundo, RNotebook);
                using (dc.PushTransform(Matrix.CreateTranslation(PNotebook.X, PNotebook.Y)))
                using (dc.PushClip(new Rect(0, 0, RNotebook.Width, RNotebook.Height)))
                {
                    switch (_pagina)
                    {
                        case PgJogo: PaginaJogo(dc); break;
                        case PgJogador: PaginaJogador(dc); break;
                        case PgFinancas: PaginaFinancas(dc); break;
                        case PgEscalacao: PaginaEscalacao(dc); break;
                        case PgAdversario: PaginaAdversario(dc); break;
                        case PgPatrocinio: PaginaPatrocinio(dc); break;
                        case PgMensagem: PaginaMensagem(dc); break;
                    }
                }
                DesenharAbas(dc);
            }
            if (_arbitro) DesenharArbitro(dc);
            DesenharMenuAberto(dc);
        }

        // ------------------------------------------------------------ menu
        // a barra de menus: a do Wine (original) ou a do topo do Moderno
        double BarraX => _moderno ? MBarraX : Jx + 3;
        double BarraY => _moderno ? 0 : Jy + MenuY;
        double BarraA => _moderno ? MMenuA : MenuA;
        double AltItem => _moderno ? MItemA : ItemA;
        double AltSeparador => _moderno ? MSeparadorA : SeparadorA;
        double LarguraMenu(string s) => _moderno ? LarguraM(s, MTamMenu) : Largura(s, 11);

        List<(double X0, double X1)> PosicoesMenu()
        {
            var r = new List<(double, double)>();
            double x = 0;
            foreach (var m in _menus)
            {
                double w = LarguraMenu(m.Texto) + (_moderno ? 24 : 12);
                r.Add((x, x + w));
                x += w;
            }
            return r;
        }

        void DesenharBarraMenu(DrawingContext dc)
        {
            double bx = Jx + 3, by = Jy + MenuY;
            dc.FillRectangle(Branco, new Rect(bx, by, JanL - 6, MenuA));
            var pos = PosicoesMenu();
            for (int i = 0; i < _menus.Count; i++)
            {
                bool aberto = i == _menuAberto;
                if (aberto) dc.FillRectangle(Destaque, new Rect(bx + pos[i].X0, by, pos[i].X1 - pos[i].X0, MenuA));
                Escreve(dc, _menus[i].Texto, bx + pos[i].X0 + 5, by + 3, 11, aberto ? Branco : Preto);
            }
        }

        const double ItemA = 18, SeparadorA = 8;

        Menu MenuDe(int i) => i == Contexto && _contexto != null ? _contexto : _menus[i];

        Rect CaixaMenu(int i)
        {
            var m = MenuDe(i);
            double tw = 0, aw = 0, h = _moderno ? 12 : 4;
            foreach (var it in m.Itens)
            {
                if (it.Texto == "-") { h += AltSeparador; continue; }
                tw = Math.Max(tw, LarguraMenu(it.Texto));
                aw = Math.Max(aw, LarguraMenu(it.Atalho));
                h += AltItem;
            }
            double w = 20 + tw + (aw > 0 ? 24 + aw : 0) + 16;
            // o do botao direito: onde se clicou, dentro da tela
            if (i == Contexto)
                return new Rect(Math.Clamp(_contextoPos.X, 0, Math.Max(0, L - w)), Math.Clamp(_contextoPos.Y, 0, Math.Max(0, A - h)), w, h);
            double x = BarraX + PosicoesMenu()[i].X0;
            if (_moderno) x = Math.Min(x, L - 8 - w);
            return new Rect(x, BarraY + BarraA + (_moderno ? 2 : 0), w, h);
        }

        void DesenharMenuAberto(DrawingContext dc)
        {
            if (_menuAberto < 0) return;
            if (_moderno) { MenuAbertoModerno(dc); return; }
            var m = MenuDe(_menuAberto);
            var cx = CaixaMenu(_menuAberto);
            dc.FillRectangle(Branco, cx);
            Moldura(dc, cx, Luz3D, Escura);
            Moldura(dc, cx.Deflate(1), Branco, Sombra);
            double y = cx.Y + 2;
            for (int k = 0; k < m.Itens.Count; k++)
            {
                var it = m.Itens[k];
                if (it.Texto == "-")
                {
                    dc.FillRectangle(Sombra, new Rect(cx.X + 2, y + 3, cx.Width - 4, 1));
                    dc.FillRectangle(Branco, new Rect(cx.X + 2, y + 4, cx.Width - 4, 1));
                    y += SeparadorA;
                    continue;
                }
                bool hab = it.Habilitado?.Invoke() ?? true;
                bool sobre = k == _itemSobre && hab;
                if (sobre) dc.FillRectangle(Destaque, new Rect(cx.X + 2, y, cx.Width - 4, ItemA));
                var cor = !hab ? Sombra : sobre ? Branco : Preto;
                Escreve(dc, it.Texto, cx.X + 20, y + 2, 11, cor);
                if (it.Atalho != "") Direita(dc, it.Atalho, cx.Right - 16, y + 2, 11, cor);
                y += ItemA;
            }
        }

        int ItemEm(Point p)
        {
            if (_menuAberto < 0) return -1;
            var cx = CaixaMenu(_menuAberto);
            if (!cx.Contains(p)) return -1;
            double y = cx.Y + (_moderno ? 6 : 2);
            var itens = MenuDe(_menuAberto).Itens;
            for (int k = 0; k < itens.Count; k++)
            {
                double h = itens[k].Texto == "-" ? AltSeparador : AltItem;
                if (p.Y >= y && p.Y < y + h) return itens[k].Texto == "-" ? -1 : k;
                y += h;
            }
            return -1;
        }

        int MenuDaBarra(Point p)
        {
            double bx = BarraX, by = BarraY;
            if (p.Y < by || p.Y >= by + BarraA) return -1;
            var pos = PosicoesMenu();
            for (int i = 0; i < pos.Count; i++)
                if (p.X >= bx + pos[i].X0 && p.X < bx + pos[i].X1) return i;
            return -1;
        }

        void Executar(Item it)
        {
            _menuAberto = -1;
            _itemSobre = -1;
            if (it.Habilitado?.Invoke() ?? true) it.Acao?.Invoke();
            InvalidateVisual();
        }

        // ------------------------------------------------------------ cabecalho
        void DesenharCabecalho(DrawingContext dc)
        {
            var m = _s.J.M;
            Escreve(dc, _s.TreinadorDa(_e) is { } t ? Texto(t.Nome) : "", 48, 8, 19, _letra, true, Times);
            Escreve(dc, m.Ano.ToString(), CL - 64, 6, 13, _letra, true);
            Escreve(dc, T("Adversário"), ROponente.X, 8, 13, _letra);
            // a caixa do adversario (4:12a5-13fb): "  NOME" (na Taca com " (divisao)") e, na
            // 2a linha, "  CASA|FORA titulo", nas cores do adversario
            if (_adv != null && _jogo != null)
            {
                dc.FillRectangle(_advFundo, ROponente);
                string nome = "  " + Texto(_adv.NomeCurto);
                if (_jogo.Taca != 0 && m.DivisaoDa(_adv) is var d && d >= 0) nome += " (" + Pascal(m.S.Divisoes[d].Nome, 20) + ")";
                string lado = _jogo.Casa == _e.ID ? T("CASA") : T("FORA");
                using (dc.PushClip(ROponente))
                {
                    Escreve(dc, nome, ROponente.X, ROponente.Y, 13, _advLetra);
                    Escreve(dc, "  " + lado + " " + Br(Pascal(_jogo.B04, 40)), ROponente.X, ROponente.Y + 16, 13, _advLetra);
                }
            }
            string pais = PaisDe(_e.Pais);
            Bandeira(dc, pais, new Rect(8, 32, 39, 29));
            Escreve(dc, NomePais(pais), 56, 40, 13, _letra, largura: 121);
            int dv = m.DivisaoDa(_e);
            // textMyDivision, com a posicao no campeonato (pedido do usuario): a lista da
            // divisao ja esta na ordem da classificacao; a de indice 4 e o Distrital
            if (dv >= 0)
            {
                string div = dv >= 4 ? T("Distrital")
                    : LugarNaDivisao(m.S.Divisoes[dv].Equipas.IndexOf(_e.ID) + 1, Pascal(m.S.Divisoes[dv].Nome, 20));
                using (dc.PushClip(new Rect(180, 44, 194, 24))) Escreve(dc, div, 200, 48, 13, _letra);
            }
        }

        // as bandeiras do app (redondas), no quadro de 39x29 do TImage
        static void Bandeira(DrawingContext dc, string pais, Rect r)
        {
            var b = Imagem($"Bandeiras/{pais}.png");
            if (b == null) return;
            double lado = Math.Min(r.Width, r.Height);
            dc.DrawImage(b, new Rect(r.X + (r.Width - lado) / 2, r.Y + (r.Height - lado) / 2, lado, lado));
        }

        // ------------------------------------------------------------ elenco
        // ListBoxOfPlayersDrawItem (seg04:2cf0 -> 14:0ad6) com as colunas 0xCC3|4|0x1000: o
        // estado (fonte Symbol), a posicao, o nome, a forca, S (ou o A dos amarelos do
        // launcher), L, o pais dos estrangeiros e o salario (+ se ja revisto); a ultima
        // linha de cada posicao tem 20 pontos e o traco. A lista usa as cores da equipa
        // trocadas: fundo na cor da letra; o escolhido, com as cores trocadas de novo.
        void DesenharElenco(DrawingContext dc)
        {
            Afundada(dc, RLista);
            var fundoLista = _letra;
            var letraLista = _fundo;
            dc.FillRectangle(fundoLista, RItens);
            if (_largo) TitulosElenco(dc, fundoLista, letraLista);
            var o = Ordem();
            Rolar(_rolagem);
            using (dc.PushClip(RItens))
            {
                double y = RItens.Y - _rolagem;
                for (int i = 0; i < o.Count; i++)
                {
                    double h = AlturaLinha(o, i);
                    if (y + h > RItens.Y && y < RItens.Bottom)
                    {
                        var r = new Rect(RItens.X, y, RItens.Width, h);
                        var cor = letraLista;
                        if (o[i] == _sel)
                        {
                            dc.FillRectangle(letraLista, r);
                            cor = fundoLista;
                        }
                        LinhaJogador(dc, o[i], r, cor, h == 20);
                    }
                    y += h;
                }
            }
            DesenharBarra(dc);
        }

        void LinhaJogador(DrawingContext dc, SJogador p, Rect r, IBrush cor, bool separador)
        {
            const double t = 11;
            double x = r.X + 2, y = r.Y;
            switch (Mem.Escalacao(p))
            {
                case Mem.EscTitular: dc.FillRectangle(cor, new Rect(x + 1, y + 5, 3, 3)); break;
                case Mem.EscReserva: dc.FillRectangle(cor, new Rect(x, y + 6, 6, 1)); break;
            }
            if (separador) dc.FillRectangle(cor, new Rect(r.X, r.Bottom - 4, r.Width, 1));
            x += 20;
            Escreve(dc, Posicao(Math.Min((int)p.Posicao, 3)), x, y, t, cor, true);
            x += 15;
            // a forca alinhada a direita em x + 140; com 4 ou 5 algarismos ela cresce para a
            // esquerda e o nome e cortado antes dela
            string forca = p.Forca.ToString();
            double xf = x + 140 - Largura(forca, t, true) - 3;
            using (dc.PushClip(new Rect(x, y, Math.Max(1, Math.Min(120, xf - x)), r.Height))) Escreve(dc, NomeJogador(p), x, y, t, cor, true);
            x += 120;
            Direita(dc, forca, x + 20, y, t, cor, true);
            x += 30;
            if (p.Suspensao != 0) Escreve(dc, " S", x, y, t, cor, true);
            else if (_launcher && Amarelos(p) > 0) Escreve(dc, " A", x, y, t, cor, true);
            x += 20;
            if (p.JogosLesao != 0) Escreve(dc, " L", x, y, t, cor, true);
            x += 20;
            string pais = PaisDe(p.Pais);
            if (p.B35 == 1) Escreve(dc, "* " + pais, x, y, t, cor, true);
            else if (p.B35 != 0) Escreve(dc, pais, x, y, t, cor, true);
            x += 35;
            Direita(dc, Dinheiro(p.Salario), x + 55, y, t, cor, true);
            x += 65;
            if (!MercadoJogo.PodeMudarSalario(p)) Escreve(dc, "+", x, y, t, cor, true);
            if (!_largo) return;
            // 16:9: o historico (jogos e gols), os gols na temporada e, no launcher, a Nota
            x = r.X + ColunasLargas;
            Direita(dc, Hist(p, Mem.HistJogos).ToString(), x += PassoLargo, y, t, cor, true);
            Direita(dc, Hist(p, Mem.HistGolos).ToString(), x += PassoLargo, y, t, cor, true);
            Direita(dc, Mem.Golos14(p).ToString(), x += PassoLargo, y, t, cor, true);
            if (_launcher) Direita(dc, p.Nota.ToString(), x += PassoLargo, y, t, cor, true);
        }

        const double ColunasLargas = 334, PassoLargo = 31;   // a Nota longe da barra de rolagem

        // 16:9: a linha dos titulos das colunas, em cima da lista
        void TitulosElenco(DrawingContext dc, IBrush fundo, IBrush cor)
        {
            const double t = 10;
            var r = RTitulos;
            dc.FillRectangle(fundo, r);
            dc.FillRectangle(cor, new Rect(r.X, r.Bottom - 2, r.Width, 1));
            double x = r.X + 2, y = r.Y + 1;
            Escreve(dc, "P", x + 20, y, t, cor);
            string S(string c) => _ordemCol == c ? (_ordemDesc ? "▼" : "▲") : "";
            Escreve(dc, T("Nome") + S("Jogador"), x + 35, y, t, cor);
            Direita(dc, S("Força") + T("Força"), x + 175, y, t, cor);
            Escreve(dc, T("País") + S("País"), x + 225, y, t, cor);
            Direita(dc, S("Salário") + T("Salário"), x + 315, y, t, cor);
            x = r.X + ColunasLargas;
            Direita(dc, S("Jogos") + T("Jogos"), x += PassoLargo, y, t, cor);
            Direita(dc, S("Gols") + T("Gols"), x += PassoLargo, y, t, cor);
            Direita(dc, S("Temp.") + T("Temp."), x += PassoLargo, y, t, cor);
            if (_launcher) Direita(dc, S("Nota") + T("Nota"), x += PassoLargo, y, t, cor);
        }

        // 16:9 classico: a coluna sob o x (relativo a RTitulos) para ordenar
        string ColunaClassica(double x)
        {
            if (x < 140) return "Jogador";
            if (x < 182) return "Força";
            if (x < 262) return "País";
            if (x < ColunasLargas) return "Salário";
            string[] c = _launcher ? new[] { "Jogos", "Gols", "Temp.", "Nota" } : new[] { "Jogos", "Gols", "Temp." };
            return c[Math.Clamp((int)((x - ColunasLargas) / PassoLargo), 0, c.Length - 1)];
        }

        // a barra de rolagem vertical (os EXEs do launcher pedem WS_VSCROLL); no Moderno, fina
        // e sem as setas
        double Seta => _moderno ? 0 : 16;

        (Rect Cursor, bool Rola) CursorBarra()
        {
            double total = AlturaElenco(), vis = RItens.Height;
            double trilho = RBarra.Height - 2 * Seta;
            if (total <= vis) return (default, false);
            double h = Math.Max(_moderno ? 30 : 12, trilho * vis / total);
            double py = RBarra.Y + Seta + (trilho - h) * (_rolagem / (total - vis));
            return (new Rect(RBarra.X, py, RBarra.Width, h), true);
        }

        void DesenharBarra(DrawingContext dc)
        {
            dc.FillRectangle(Luz3D, RBarra);
            foreach (var (by, cima) in new[] { (RBarra.Y, true), (RBarra.Bottom - 16, false) })
            {
                var b = new Rect(RBarra.X, by, 16, 16);
                dc.FillRectangle(Face, b);
                Saltada(dc, b);
                // a setinha
                double cx = b.X + 7, cy = b.Y + (cima ? 6 : 9);
                for (int k = 0; k < 4; k++)
                    dc.FillRectangle(Preto, new Rect(cx - k, cy + (cima ? k : -k), 2 * k + 1, 1));
            }
            var (cur, rola) = CursorBarra();
            if (rola)
            {
                dc.FillRectangle(Face, cur);
                Saltada(dc, cur);
            }
        }

        void Rolar(double v)
        {
            _rolagem = Math.Clamp(v, 0, Math.Max(0, AlturaElenco() - RItens.Height));
        }

        SJogador? JogadorEm(double yCliente)
        {
            double y0 = RItens.Y - _rolagem;
            foreach (var l in Linhas())
                if (yCliente >= y0 + l.Y && yCliente < y0 + l.Y + l.H) return l.P;
            return null;
        }

        // ------------------------------------------------------------ abas (TTabSet)
        List<(double X0, double X1)> PosicoesAbas()
        {
            var r = new List<(double, double)>();
            double x = 4;
            foreach (var a in Abas)
            {
                double w = Largura(a, TamAbas, fonte: FonteAbas) + FolgaAbas;
                r.Add((x, x + w));
                x += w - 4;
            }
            return r;
        }

        void DesenharAbas(DrawingContext dc)
        {
            var ts = RTabSet;
            dc.FillRectangle(_fundo, ts);
            var pos = PosicoesAbas();
            int sel = Array.IndexOf(PaginasAbas, _pagina);
            // a linha de cima, aberta embaixo da aba escolhida
            double gap0 = sel >= 0 ? ts.X + pos[sel].X0 + 2 : 0, gap1 = sel >= 0 ? ts.X + pos[sel].X1 - 1 : 0;
            for (int k = 0; k < 2; k++)
            {
                var cor = k == 0 ? Escura : Sombra;
                if (sel < 0) dc.FillRectangle(cor, new Rect(ts.X, ts.Y + k, ts.Width, 1));
                else
                {
                    dc.FillRectangle(cor, new Rect(ts.X, ts.Y + k, Math.Max(0, gap0 - ts.X), 1));
                    dc.FillRectangle(cor, new Rect(gap1, ts.Y + k, Math.Max(0, ts.Right - gap1), 1));
                }
            }
            // as nao escolhidas primeiro, a escolhida por cima
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < Abas.Length; i++)
                {
                    if ((i == sel) != (pass == 1)) continue;
                    double x0 = ts.X + pos[i].X0, x1 = ts.X + pos[i].X1, y0 = ts.Y + 2, y1 = ts.Y + 16;
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(x0, y0 - 2), true);
                        c.LineTo(new Point(x0 + 4, y1));
                        c.LineTo(new Point(x1 - 4, y1));
                        c.LineTo(new Point(x1, y0 - 2));
                        c.EndFigure(true);
                    }
                    dc.DrawGeometry(_fundo, null, g);
                    var pen = new Pen(Escura, 1);
                    dc.DrawLine(pen, new Point(x0 + 0.5, y0 - 1), new Point(x0 + 4.5, y1));
                    dc.DrawLine(pen, new Point(x0 + 4, y1 + 0.5), new Point(x1 - 4, y1 + 0.5));
                    dc.DrawLine(pen, new Point(x1 - 4, y1), new Point(x1 + 0.5, y0 - 1));
                    if (i == sel) dc.DrawLine(new Pen(Branco, 1), new Point(x0 + 1.5, y0), new Point(x0 + 5, y1 - 1));
                    Escreve(dc, Abas[i], x0 + FolgaAbas / 2, ts.Y + (_patrocinio ? 3 : 2), TamAbas, _letra, fonte: FonteAbas);
                }
        }

        int AbaEm(Point pc)
        {
            if (!RTabSet.Contains(pc)) return -1;
            var pos = PosicoesAbas();
            for (int i = 0; i < pos.Count; i++)
                if (pc.X >= RTabSet.X + pos[i].X0 + 3 && pc.X < RTabSet.X + pos[i].X1 - 3) return i;
            return -1;
        }

        // ------------------------------------------------------------ medidor (TBiGauge)
        static void Medidor(DrawingContext dc, Rect r, double valor, double max, IBrush barra, double margem)
        {
            dc.FillRectangle(Branco, r);
            Moldura(dc, r, Sombra, Sombra);
            var dentro = r.Deflate(1);
            Moldura(dc, dentro, Sombra, Branco);
            Moldura(dc, dentro.Deflate(1), Sombra, Branco);
            var area = dentro.Deflate(2 + margem);
            double w = area.Width * Math.Clamp(valor / max, 0, 1);
            if (w > 0) dc.FillRectangle(barra, new Rect(area.X, area.Y, w, area.Height));
        }

        // moralNoMedidor (4:1d5a): Value = Round(moral x 10) de 0 a 20; a barra (3:5836) verde
        // acima de 1,25, vermelha abaixo de 0,65, amarela entre os dois
        static void MedidorMoral(DrawingContext dc, Rect r, SEquipa e, double margem)
        {
            double m = Moral(e);
            IBrush cor = m > 1.25 ? Pincel(0, 255, 0) : m < 0.65 ? Pincel(255, 0, 0) : Pincel(255, 255, 0);
            Medidor(dc, r, Math.Round(m * 10, MidpointRounding.ToEven), 20, cor, margem);
        }

        // ------------------------------------------------------------ botao com imagem (TBitBtn)
        static void BotaoImagem(DrawingContext dc, Rect r, string imagem, string texto, bool hab, double margem = -1)
        {
            dc.FillRectangle(Face, r);
            Saltada(dc, r);
            var img = Imagem($"Imagens/{imagem}{(hab ? "" : "-desab")}.png");
            var t = Txt(texto, 9, hab ? Preto : Sombra);
            double gh = img?.Size.Height ?? 0, gw = img?.Size.Width ?? 0;
            double esp = 4;
            double gy = margem >= 0 ? r.Y + margem : r.Y + (r.Height - gh - esp - t.Height) / 2;
            if (img != null) dc.DrawImage(img, new Rect(r.X + (r.Width - gw) / 2, gy, gw, gh));
            double ty = margem >= 0 ? gy + gh + (r.Bottom - (gy + gh) - t.Height) / 2 : gy + gh + esp;
            dc.DrawText(t, new Point(r.X + (r.Width - t.WidthIncludingTrailingWhitespace) / 2, ty));
        }

        static void Botao(DrawingContext dc, Rect r, string texto)
        {
            dc.FillRectangle(Face, r);
            Saltada(dc, r);
            Centro(dc, texto, r, r.Y + (r.Height - 15) / 2, 13, Preto);
        }

        // ------------------------------------------------------------ pagina Jogo
        void PaginaJogo(DrawingContext dc)
        {
            // ListBoxClassification (4:35f7): a propria equipa e a adversaria, cada uma nas suas
            // cores (TTeam.Formatar com 8: nome, V, E, D, GP : GC, pontos)
            var y0 = YJogo;
            var rc = new Rect(0, y0[0], 273, 34);
            Moldura(dc, rc, Escura, Escura);
            var eqs = new[] { _e, _adv };
            for (int i = 0; i < 2; i++)
            {
                var eq = eqs[i];
                if (eq == null) continue;
                var (l, f) = Cores(eq);
                var r = new Rect(1, y0[0] + 1 + 16 * i, 271, 16);
                dc.FillRectangle(f, r);
                using (dc.PushClip(r))
                {
                    double x = r.X + 2, y = r.Y;
                    short W(int off) => Campos.EqW(eq, off);
                    Escreve(dc, Texto(eq.NomeCurto), x, y, 11, l);
                    x += 120;
                    Direita(dc, W(Campos.EqVitorias).ToString(), x + 20, y, 11, l); x += 20;
                    Direita(dc, W(Campos.EqEmpates).ToString(), x + 20, y, 11, l); x += 20;
                    Direita(dc, W(Campos.EqDerrotas).ToString(), x + 20, y, 11, l); x += 20;
                    Direita(dc, W(Campos.EqGolosPro).ToString(), x + 30, y, 11, l); x += 30;
                    Direita(dc, ":", x + 10, y, 11, l); x += 10;
                    Direita(dc, W(Campos.EqGolosContra).ToString(), x + 15, y, 11, l); x += 15;
                    Direita(dc, W(Campos.EqPontos).ToString(), x + 30, y, 11, l);
                }
            }
            if (_jogo != null)
            {
                Escreve(dc, T("Árbitro"), 16, y0[1], 11, _letra);
                var arb = _s.S.Arbitros.FindLast(a => a.ID == _jogo.Arbitro);
                if (arb != null)
                    using (dc.PushClip(RArbitro))
                        Escreve(dc, Texto(arb.Nome) + " (" + NomePais(PaisDe(arb.Pais)) + ")", 16, y0[2], 11, _letra, true);
            }
            string ult = UltimoConfronto();
            if (ult != "")
            {
                Escreve(dc, T("Último jogo"), 16, y0[3], 11, _letra);
                Escreve(dc, ult, 80, y0[3], 16, _letra, true);
            }
            Escreve(dc, T("Dinheiro em caixa") + "\n" + Dinheiro((int)_e.Dinheiro), 16, y0[4], 11, _letra, largura: 249);
            Escreve(dc, T("Moral"), 16, y0[5], 11, _letra);
            MedidorMoral(dc, new Rect(8, y0[6], 257, 25), _e, 3);
        }

        // ------------------------------------------------------------ pagina Jogador
        void PaginaJogador(DrawingContext dc)
        {
            var p = _sel;
            if (p == null)
            {
                // 16:9: a aba Jogador e a primeira, ainda sem jogador escolhido
                if (_largo) Escreve(dc, T("Escolha um jogador na lista para ver a ficha dele."), 8, 8, 13, _letra, largura: 250);
                return;
            }
            Escreve(dc, NomeJogador(p), 8, 0, 19, _letra);
            string pais = PaisDe(p.Pais);
            Bandeira(dc, pais, new Rect(8, 24, 39, 29));
            Escreve(dc, NomePais(pais), 56, 32, 13, _letra);
            if (p.B35 == 1)
                Escreve(dc, T("Não é considerado jogador estrangeiro, ao abrigo da Lei Bosman"), 8, 56, 13, _letra, largura: 233);

            // PanelPlayer (32, 96); no launcher, Nota e Risco de lesao abaixo do Comportamento
            // (tools/divisoes/ficha.py) e a linha dos amarelos no Historial (historial.py)
            using (dc.PushTransform(Matrix.CreateTranslation(32, 96)))
            {
                const double t = 11;
                string comp = p.Comportamento < Comportamentos.Length ? T(Comportamentos[p.Comportamento]) : p.Comportamento.ToString();
                double gy;
                if (_launcher)
                {
                    Escreve(dc, T("Comportamento"), 0, 0, t, _letra); Escreve(dc, comp, 104, 0, t, _letra);
                    Escreve(dc, T("Nota"), 0, 15, t, _letra); Escreve(dc, p.Nota.ToString(), 104, 15, t, _letra);
                    Escreve(dc, T("Risco de lesão"), 0, 30, t, _letra); Escreve(dc, p.Lesao.ToString(), 104, 30, t, _letra);
                    Escreve(dc, T("Gols na Temporada"), 0, 45, t, _letra); Escreve(dc, Mem.Golos14(p).ToString(), 104, 45, t, _letra);
                    gy = 60;
                }
                else
                {
                    Escreve(dc, T("Comportamento"), 0, 8, t, _letra); Escreve(dc, comp, 88, 8, t, _letra);
                    // o valor em 104 como no launcher: "Gols na Temporada" (pt-BR) nao cabe antes de 96
                    Escreve(dc, T("Gols na Temporada"), 0, 32, t, _letra); Escreve(dc, Mem.Golos14(p).ToString(), 104, 32, t, _letra);
                    gy = 48;
                }
                // o GroupBox do Historial
                var gb = new Rect(0, gy + 7, 185, 90);
                Moldura(dc, gb, Sombra, Branco);
                Moldura(dc, gb.Deflate(1), Branco, Sombra);
                double lt = Largura(T("Histórico"), t);
                dc.FillRectangle(_fundo, new Rect(6, gy, lt + 4, 14));
                Escreve(dc, T("Histórico"), 8, gy, t, _letra);
                var linhas = new List<(string, int, double)>();
                if (_launcher)
                {
                    linhas.Add(("Jogos", Hist(p, Mem.HistJogos), 16));
                    linhas.Add(("Gols", Hist(p, Mem.HistGolos), 32));
                    linhas.Add(("Cartões amarelos", Amarelos(p), 48));
                    linhas.Add(("Cartões vermelhos", Hist(p, Mem.HistVermelhos), 64));
                    linhas.Add(("Lesões", Hist(p, Mem.HistLesoes), 80));
                }
                else
                {
                    linhas.Add(("Jogos", Hist(p, Mem.HistJogos), 16));
                    linhas.Add(("Gols", Hist(p, Mem.HistGolos), 35));
                    linhas.Add(("Cartões vermelhos", Hist(p, Mem.HistVermelhos), 53));
                    linhas.Add(("Lesões", Hist(p, Mem.HistLesoes), 72));
                }
                foreach (var (rot, v, y) in linhas)
                {
                    Escreve(dc, T(rot), 8, gy + y, t, _letra);
                    Direita(dc, v.ToString(), 174, gy + y, t, _letra);
                }
            }
            // suspenso / lesionado (4:2251...); no launcher, sem suspensao, os amarelos
            double ys = _launcher ? 252 : 248, yl = _launcher ? 266 : 264;
            string sus = "";
            if (p.Suspensao > 0) sus = p.Suspensao == 1 ? T("Suspenso por 1 jogo") : T("Suspenso por {0} jogos", p.Suspensao);
            else if (_launcher && Amarelos(p) > 0) sus = Amarelos(p) == 1 ? T("1 amarelo acumulado") : T("{0} amarelos acumulados", Amarelos(p));
            if (sus != "") Escreve(dc, sus, 8, ys, 11, _letra, true, italico: true);
            if (p.JogosLesao > 0)
                Escreve(dc, p.JogosLesao == 1 ? T("Lesionado por 1 jogo") : T("Lesionado por {0} jogos", p.JogosLesao), 8, yl, 11, _letra, true, italico: true);
            if (MercadoJogo.PodeMudarSalario(p)) Botao(dc, RRenovar, T("Renovar contrato..."));
            Botao(dc, RVender, T("Vender"));
        }

        // ------------------------------------------------------------ pagina Financas
        void PaginaFinancas(DrawingContext dc)
        {
            // ListBoxBalance (TThreeBalances, 11:2094): 14 linhas, rotulo em 140 e valor a
            // direita em 70; Temporada, Receitas e Gastos em negrito
            var b = MercadoJogo.Balanco(_e);
            string[] rot = { "Temporada", "Receitas", "Bilheteria", "Jogadores vendidos", "Prêmios", "Gastos", "Salários",
                "Jogadores comprados", "Arquibancadas", "Juros", "", "Receita total", "Gasto total", "Saldo" };
            for (int i = 0; i < rot.Length; i++)
            {
                string v = i switch
                {
                    0 => _s.J.M.Ano.ToString(),
                    2 or 3 or 4 => Dinheiro(b[i - 2]),
                    6 => Dinheiro(b[4]),
                    7 => Dinheiro(b[3]),
                    8 or 9 => Dinheiro(b[i - 3]),
                    11 => Dinheiro((long)b[0] + b[1] + b[2]),
                    12 => Dinheiro((long)b[3] + b[4] + b[5] + b[6]),
                    13 => Dinheiro(b.Sum(x => (long)x)),
                    _ => "",
                };
                bool neg = i is 0 or 1 or 5;
                double y = 8 + 16 * i;
                using (dc.PushClip(new Rect(18, y, 140, 16))) Escreve(dc, T(rot[i]), 18, y, 13, _letra, neg);
                // o valor com a moeda: a direita em 257, como os totais de baixo (cabe "-R$ 12.345.678")
                Direita(dc, v, 257, y, 13, _letra, neg);
            }
            // (no launcher os totais sobem para caber a previsao do ingresso antes do preco)
            double yt = _launcher ? 237 : 251, yj = _launcher ? 251 : 264, yc = _launcher ? 265 : 288;
            Escreve(dc, T("Total de salários"), 16, yt, 11, _letra);
            Direita(dc, Dinheiro(MercadoJogo.FolhaSalarial(_e)), 257, yt - 3, 13, _letra, true);
            Escreve(dc, T("Juros do empréstimo"), 16, yj, 11, _letra);
            Direita(dc, Dinheiro(MercadoJogo.Juros((int)_e.D6D)), 257, yj, 13, _letra, true);
            Escreve(dc, T("Dinheiro em caixa"), 16, yc, 11, _letra);
            Direita(dc, Dinheiro((int)_e.Dinheiro), 257, yc, 13, _letra, true);
            if (_jogo != null)
            {
                Escreve(dc, T("Preço do ingresso"), 16, 312, 11, _letra);
                long preco = Ext.Bytes(_jogo.B36.AsSpan(0, 10)).Round();
                if (!_launcher) Escreve(dc, Dinheiro(preco), 112, 312, 13, _letra);
                else
                {
                    // launcher (ingressos.py): o preco direto (de 50% a 300% da tabela, de 25 em
                    // 25; as setas mudam o %) e, entre o balanco e os totais, a previsao do
                    // proximo jogo em casa (IngressosPrevisao)
                    int pct = Ingressos.Percentual(_e);
                    CampoNumero(dc, RIngressos, Dinheiro(IngressosPrevisao.PrecoCom(Ext.Bytes(_jogo.B36.AsSpan(0, 10)), pct)));
                    var (l1, l2) = LinhasPrevisao(pct);
                    using (dc.PushClip(new Rect(16, 280, 241, 26)))
                    {
                        Escreve(dc, l1, 16, 280, 11, _letra);
                        Escreve(dc, l2, 16, 292, 11, _letra);
                    }
                }
            }
        }

        // a previsao do proximo jogo em casa em duas linhas curtas (o adversario, o publico e a
        // ocupacao; a renda)
        (string, string) LinhasPrevisao(int pct)
        {
            var pv = IngressosPrevisao.ProximoEmCasa(_e, _jogo, pct, id => _s.Equipa(id));
            if (pv == null) return (T("Sem jogo em casa pela frente nesta temporada."), "");
            string adv = _s.Equipa(pv.Adversario) is { } a ? Texto(a.NomeCurto) : "";
            string N(int v) => v.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
            int Oc(int v) => pv.Capacidade > 0 ? (int)Math.Round(100.0 * v / pv.Capacidade) : 0;
            string pub = pv.Minimo == pv.Maximo ? $"{N(pv.Minimo)} ({Oc(pv.Minimo)}%)" : $"{N(pv.Minimo)}–{N(pv.Maximo)} ({Oc(pv.Minimo)}–{Oc(pv.Maximo)}%)";
            string renda = pv.RendaMinima == pv.RendaMaxima ? Dinheiro(pv.RendaMinima) : Dinheiro(pv.RendaMinima) + "–" + Dinheiro(pv.RendaMaxima);
            return ((pv.Definido ? T("Este jogo") : T("Próx. em casa")) + $" ({adv}): " + pub,
                T("Renda prevista") + ": " + renda);
        }

        // a mesma previsao em 3 linhas curtas (o Moderno 16:9, numa coluna estreita): o jogo, o
        // publico e a renda
        string[] LinhasPrevisao3(int pct)
        {
            var pv = IngressosPrevisao.ProximoEmCasa(_e, _jogo, pct, id => _s.Equipa(id));
            if (pv == null) return new[] { T("Sem jogo em casa pela frente nesta temporada."), "", "" };
            string adv = _s.Equipa(pv.Adversario) is { } a ? Texto(a.NomeCurto) : "";
            string N(int v) => v.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
            int Oc(int v) => pv.Capacidade > 0 ? (int)Math.Round(100.0 * v / pv.Capacidade) : 0;
            string pub = pv.Minimo == pv.Maximo ? $"{N(pv.Minimo)} ({Oc(pv.Minimo)}%)" : $"{N(pv.Minimo)}–{N(pv.Maximo)} ({Oc(pv.Minimo)}–{Oc(pv.Maximo)}%)";
            string renda = pv.RendaMinima == pv.RendaMaxima ? Dinheiro(pv.RendaMinima) : Dinheiro(pv.RendaMinima) + "–" + Dinheiro(pv.RendaMaxima);
            return new[]
            {
                (pv.Definido ? T("Este jogo") : T("Próx. em casa")) + $" ({adv})",
                T("Público") + ": " + pub,
                T("Renda prevista") + ": " + renda,
            };
        }

        // TSpinEdit: o edit afundado e o TSpinButton (2 botoes com setas) a direita
        static void CampoNumero(DrawingContext dc, Rect r, string valor)
        {
            dc.FillRectangle(Branco, r);
            Afundada(dc, r);
            using (dc.PushClip(r.Deflate(2))) Escreve(dc, valor, r.X + 4, r.Y + 3, 13, Preto);
            double bx = r.Right - 2 - 15, by = r.Y + 2, bh = (r.Height - 4) / 2;
            foreach (var (y, cima) in new[] { (by, true), (by + bh, false) })
            {
                var b = new Rect(bx, Math.Floor(y), 15, Math.Ceiling(bh));
                dc.FillRectangle(Face, b);
                Saltada(dc, b);
                double cx = b.X + 7, cy = b.Y + (cima ? 3 : 6);
                for (int k = 0; k < 3; k++)
                    dc.FillRectangle(Preto, new Rect(cx - k, cy + (cima ? k : -k), 2 * k + 1, 1));
            }
        }

        // o OnChange do campo (abas.asm): grava no time do tecnico, de 25 em 25, de 50 a 300
        void MudarIngressos(int passos)
        {
            Ingressos.Escolher(_e, Ingressos.Percentual(_e) + passos * Ingressos.Passo);
            // o jogo desta jornada em casa: o publico dele muda ja com o preco
            if (_jogo is { } j && j.Casa == _e.ID) _s.J.M.RecalcularPublico(j);
            InvalidateVisual();
        }

        // ------------------------------------------------------------ pagina Escalacao
        void PaginaEscalacao(DrawingContext dc)
        {
            if (_launcher) Campinho(dc, new Rect(8, 8, 256, 166));
            BotaoImagem(dc, RJogar, "jogar", T("Jogar"), JogarHabilitado(), 10);
        }

        // o campinho do launcher (tools/divisoes/campo.py e campo.asm): um TImage de 256x166
        // em (8, 8). Gramado RGB(76, 160, 60); caneta branca de 2 com o pincel do gramado:
        // contorno (3, 3)-(L-3, A-3), grande area (16% da largura, do topo a 22% da altura),
        // pequena (6%, 36%), circulo central de raio A/6 e linha do meio. Cada titular
        // (+0x10 = 1, na ordem da lista do time; setor +0x19, acima de 3 vale 3) e uma
        // bolinha de raio A/20 + 1 na cor da letra do time, contorno de 1 na cor do fundo,
        // em x = L x coluna% (6, 25, 55, 82) e y = A x k / (n + 1), com a letra do setor em
        // Arial 8 negrito na cor do fundo, em (x - meia letra, y - 7).
        void Campinho(DrawingContext dc, Rect r)
        {
            int w = (int)r.Width, h = (int)r.Height;
            var grama = Pincel(76, 160, 60);
            dc.FillRectangle(grama, r);
            var pen = new Pen(Branco, 2);
            // TCanvas.Rectangle/Ellipse(x1, y1, x2, y2): a borda dentro de x1..x2-1
            void Ret(int x1, int y1, int x2, int y2, IBrush? p, Pen c) =>
                dc.DrawRectangle(p, c, new Rect(r.X + x1 + 0.5, r.Y + y1 + 0.5, x2 - x1 - 1, y2 - y1 - 1));
            void Elipse(int x1, int y1, int x2, int y2, IBrush? p, Pen c) =>
                dc.DrawEllipse(p, c, new Rect(r.X + x1 + 0.5, r.Y + y1 + 0.5, x2 - x1 - 1, y2 - y1 - 1));
            Ret(3, 3, w - 3, h - 3, grama, pen);
            foreach (var (pl, pa) in new[] { (16, 22), (6, 36) })
            {
                int lx = w * pl / 100, ty = h * pa / 100;
                Ret(3, ty, lx + 3, h - ty, grama, pen);
                Ret(w - 3 - lx, ty, w - 3, h - ty, grama, pen);
            }
            int raio = h / 6, mx = w / 2, my = h / 2;
            Elipse(mx - raio, my - raio, mx + raio, my + raio, grama, pen);
            dc.DrawLine(pen, new Point(r.X + mx + 0.5, r.Y + 3), new Point(r.X + mx + 0.5, r.Y + h - 3));

            int[] col = { 6, 25, 55, 82 }, meia = { 4, 4, 5, 4 };
            var num = new int[4];
            var pos = new int[4];
            int Setor(SJogador p) => Math.Min((int)p.Posicao, 3);
            foreach (var p in _e.Jogadores) if (Mem.Escalacao(p) == Mem.EscTitular) num[Setor(p)]++;
            int rb = h / 20 + 1;
            var borda = new Pen(_fundo, 1);
            foreach (var p in _e.Jogadores)
            {
                if (Mem.Escalacao(p) != Mem.EscTitular) continue;
                int s = Setor(p);
                int x = w * col[s] / 100;
                pos[s]++;
                int y = h * pos[s] / (num[s] + 1);
                Elipse(x - rb, y - rb, x + rb, y + rb, _letra, borda);
                Escreve(dc, Posicao(s), r.X + x - meia[s], r.Y + y - 7, 11, _fundo, true);
            }
        }

        // ------------------------------------------------------------ Ajuda do Arbitro
        // TRefereeAidDlg (seg06:27b5): aberta pelo clique no nome do arbitro (textRefereeClick,
        // seg04:3416). Titulo: o nome do arbitro ("Tendencia do Arbitro" sem arbitro). Fundo
        // branco, a balanca (FormPaint, seg06:2b3a) e o "Fechar" (337, 148). A parte de baixo
        // (os 2 times e as barras da influencia V, de 0 a 15) e o add-in 0x23 (seg11:30bc):
        // sem ele a janela acaba 5 pontos abaixo do botao. Como todos os add-ins estao ligados
        // (Registo.Nivel = 0x64), ela aparece sempre.
        // No launcher (arbitro.py), o nome de cada time ganha a chance de penalti a favor no
        // jogo e os cartoes que o arbitro deixa de dar, com letra menor (-13 em vez de -16).
        const double ArbCliL = 412, ArbCliA = 397, ArbCliCurta = 212;
        static readonly Rect RArbFechar = new(337, 148, 64, 64);

        bool ArbitroCompleto => _launcher || Registo.AddIn(0x23);

        Rect JanelaArbitro()
        {
            double w = ArbCliL + 6, h = (ArbitroCompleto ? ArbCliA : ArbCliCurta) + 31;
            return new Rect(Math.Floor((L - w) / 2), Math.Floor((A - h) / 2), w, h);   // poScreenCenter
        }

        // chance de pelo menos 1 penalti a favor nos 90 minutos, V de 0 a 15 (arbitro.asm):
        // 1 - (1 - V/6000)^90 no mandante, V/7000 por minuto no visitante
        static readonly byte[] PenaltiCasa = { 0, 1, 3, 4, 6, 7, 9, 10, 11, 13, 14, 15, 16, 18, 19, 20 };
        static readonly byte[] PenaltiFora = { 0, 1, 3, 4, 5, 6, 7, 9, 10, 11, 12, 13, 14, 15, 16, 18 };

        void DesenharArbitro(DrawingContext dc)
        {
            if (_jogo == null) return;
            var jan = JanelaArbitro();
            dc.FillRectangle(Face, jan);
            Moldura(dc, jan, Luz3D, Escura);
            Moldura(dc, jan.Deflate(1), Branco, Sombra);
            var tit = new Rect(jan.X + 3, jan.Y + 3, jan.Width - 6, 25);
            dc.FillRectangle(CorTitulo, tit);
            var arb = _s.S.Arbitros.FindLast(a => a.ID == _jogo.Arbitro);
            using (dc.PushClip(tit))
                Escreve(dc, arb != null ? Texto(arb.Nome) : T("Tendência do Árbitro"), tit.X + 2, tit.Y + 4, 13, Preto);
            var cli = new Rect(jan.X + 3, jan.Y + 28, ArbCliL, jan.Height - 31);
            using (dc.PushTransform(Matrix.CreateTranslation(cli.X, cli.Y)))
            using (dc.PushClip(new Rect(0, 0, cli.Width, cli.Height)))
            {
                dc.FillRectangle(Branco, new Rect(0, 0, cli.Width, cli.Height));
                var casa = _s.Equipa(_jogo.Casa);
                var fora = _s.Equipa(_jogo.Fora);
                int vc = _jogo.W4C, vf = _jogo.W4E;
                Balanca(dc, (short)(vc - vf), casa, fora);
                BotaoImagem(dc, RArbFechar, "ok", T("Fechar"), true, 10);
                if (!ArbitroCompleto) return;
                // Bevel1 (bsFrame)
                var bv = new Rect(22, 236, 359, 153);
                Moldura(dc, bv, Sombra, Sombra);
                Moldura(dc, new Rect(bv.X + 1, bv.Y + 1, bv.Width - 1, bv.Height - 1), Branco, Branco);
                Moldura(dc, new Rect(bv.X, bv.Y, bv.Width - 1, bv.Height - 1), Sombra, Sombra);
                TimeArbitro(dc, casa, vc, PenaltiCasa, 248);
                TimeArbitro(dc, fora, vf, PenaltiFora, 320);
            }
        }

        // textHomeTeam/textAwayTeam nas cores do time (letra +0x42, fundo +0x46) e o TBiGauge
        // (Max 15, barra azul) abaixo
        void TimeArbitro(DrawingContext dc, SEquipa? eq, int v, byte[] penalti, double y)
        {
            if (eq == null) return;
            var (letra, fundo) = Cores(eq);
            var r = new Rect(34, y, 343, 24);
            dc.FillRectangle(fundo, r);
            string t = Texto(eq.NomeCurto);
            if (_launcher)
            {
                int k = Math.Min(v, 15);
                t += T("   Pênalti: {0}%   Cartões: -{1}%", penalti[k], 2 * (k + 1));
            }
            // MS Sans Serif no DFM; o do app nao tem o "ê" e o "õ" do texto do launcher
            using (dc.PushClip(r)) Escreve(dc, t, r.X, r.Y, _launcher ? 13 : 16, letra);
            Medidor(dc, new Rect(34, y + 32, 343, 24), v, 15, Pincel(0, 0, 255), 2);
        }

        // FormPaint (seg06:2b3a): caneta preta de 3; base (140,160)-(190,160), haste
        // (165,160)-(165,55) e o travessao de (65, 55 + 3d) a (265, 55 - 3d), d = V casa - V
        // fora; em cada ponta o prato (seg06:2971: fio de 30, triangulo de 50 de largura e 30
        // de altura) e o nome do time (seg06:2a7a: ate 20 letras, Arial 8, centrado, 45 abaixo
        // da ponta do fio)
        static void Balanca(DrawingContext dc, short d, SEquipa? casa, SEquipa? fora)
        {
            var pen = new Pen(Preto, 3, lineCap: PenLineCap.Round);
            void Lin(double x0, double y0, double x1, double y1) => dc.DrawLine(pen, new Point(x0, y0), new Point(x1, y1));
            void Prato(double x, double y, SEquipa? eq)
            {
                Lin(x, y, x, y + 30);
                Lin(x, y + 30, x - 25, y + 60);
                Lin(x - 25, y + 60, x + 25, y + 60);
                Lin(x + 25, y + 60, x, y + 30);
                string n = eq == null ? "" : Texto(eq.NomeCurto);
                if (n.Length > 20) n = n[..20];
                var t = Txt(n, 11, Preto);
                dc.DrawText(t, new Point(x - Math.Floor(t.WidthIncludingTrailingWhitespace / 2), y + 30 + 45));
            }
            Lin(140, 160, 190, 160);
            Lin(165, 160, 165, 55);
            Prato(65, 55 + 3 * d, casa);
            Lin(65, 55 + 3 * d, 265, 55 - 3 * d);
            Prato(265, 55 - 3 * d, fora);
        }

        // ------------------------------------------------------------ pagina Adversario
        void PaginaAdversario(DrawingContext dc)
        {
            if (_adv == null) return;
            dc.FillRectangle(_advFundo, RNomeAdv);
            Centro(dc, Texto(_adv.NomeCurto), RNomeAdv, RNomeAdv.Y + 3, 16, _advLetra);
            MedidorMoral(dc, new Rect(8, 48, 241, 25), _adv, 2);
            Escreve(dc, T("Técnico"), 16, 136, 13, _letra);
            Escreve(dc, _s.TreinadorDa(_adv) is { } t ? Texto(t.Nome) : "", 88, 136, 13, _letra);
            BotaoImagem(dc, RCalendario, "calendario", T("Calendário"), true);
            if (Registo.AddIn(0x10)) BotaoImagem(dc, RPlantel, "plantel", T("Elenco"), true);
        }

        // 16:9: os controles da pagina Adversario embaixo da pagina Jogo, na coluna da direita:
        // o nome nas cores do adversario, o moral dele, o tecnico e, rente ao fim da coluna, o
        // Calendario e o Elenco (nas mesmas colunas da pagina)
        double YAdvLargo => 262;
        Rect RCalendarioLargo => new(RCalendario.X, RJogoLargo.Height - 64, 64, 64);
        Rect RPlantelLargo => new(RPlantel.X, RJogoLargo.Height - 64, 64, 64);

        void AdversarioLargo(DrawingContext dc)
        {
            if (_adv == null) return;
            double y = YAdvLargo;
            var rn = new Rect(8, y, 257, 25);
            dc.FillRectangle(_advFundo, rn);
            Centro(dc, Texto(_adv.NomeCurto), rn, rn.Y + 3, 16, _advLetra);
            MedidorMoral(dc, new Rect(8, y + 40, 257, 25), _adv, 2);
            Escreve(dc, T("Técnico"), 16, y + 88, 13, _letra);
            Escreve(dc, _s.TreinadorDa(_adv) is { } t ? Texto(t.Nome) : "", 88, y + 88, 13, _letra);
            BotaoImagem(dc, RCalendarioLargo, "calendario", T("Calendário"), true);
            if (Registo.AddIn(0x10)) BotaoImagem(dc, RPlantelLargo, "plantel", T("Elenco"), true);
        }

        // ------------------------------------------------------------ pagina Patrocinio
        // a rotina aba do patch (patrocinio.asm): os controles da pagina do DFM (patrocinio.py),
        // todos centrados em Left 4, Width 265, na cor da letra da equipa: o titulo (Top 48,
        // Arial -15 negrito), o logo (TImage 120x60 em (76, 84), Center), o nome da marca (160,
        // -18 negrito), o valor por epoca (188, -13), as epocas que faltam (206, -13) e o
        // patrocinio pontual (240, -12). Sem contrato: "Sem patrocinador" e o logo some.
        void PaginaPatrocinio(DrawingContext dc)
        {
            var marcas = _s.J.Op.Marcas;
            void Rot(string t, double top, double alt, double tam, bool negrito = false)
            {
                var r = new Rect(4, top, 265, alt);
                using (dc.PushClip(r)) Centro(dc, t, r, top, tam, _letra, negrito);
            }
            Rot(T("Patrocinador master"), 48, 20, 15, true);
            var mc = _e.MarcaMaster() != 0 ? marcas?.Registro(_e.MarcaMaster()) : null;
            if (mc != null)
            {
                Rot(mc.Nome, 160, 22, 18, true);
                Rot(T("{0} por temporada", Dinheiro(_e.ValorMaster())), 188, 16, 13);
                int n = _e.EpocasMaster();
                Rot(T(n == 1 ? "{0} temporada restante" : "{0} temporadas restantes", (char)('0' + n)), 206, 16, 13);
                if (TelaPatrocinio.Logo(marcas!.CaminhoLogo(mc)) is { } img)
                {
                    var r = new Rect(76, 84, 120, 60);
                    var sz = img.Size;
                    using (dc.PushClip(r))
                        dc.DrawImage(img, new Rect(r.X + Math.Floor((r.Width - sz.Width) / 2), r.Y + Math.Floor((r.Height - sz.Height) / 2), sz.Width, sz.Height));
                }
            }
            else Rot(T("Sem patrocinador"), 160, 22, 18, true);
            var mp = _e.JogosPontual() != 0 ? marcas?.Registro(_e.MarcaPontual()) : null;
            if (mp != null)
            {
                int j = _e.JogosPontual();
                Rot(T(j == 1 ? "Patrocínio pontual: {0} - {1} jogo restante" : "Patrocínio pontual: {0} - {1} jogos restantes", mp.Nome, (char)('0' + j)), 240, 16, 12);
                // o logo do pontual abaixo da linha, como o do master (120x60, centrado)
                if (TelaPatrocinio.Logo(marcas!.CaminhoLogo(mp)) is { } img)
                {
                    var r = new Rect(76, 262, 120, 60);
                    var sz = img.Size;
                    using (dc.PushClip(r))
                        dc.DrawImage(img, new Rect(r.X + Math.Floor((r.Width - sz.Width) / 2), r.Y + Math.Floor((r.Height - sz.Height) / 2), sz.Width, sz.Height));
                }
            }
        }

        void PaginaMensagem(DrawingContext dc) => Escreve(dc, _mensagem, 24, 64, 16, _letra, largura: 225);

        // ============================================================ mouse e toque
        Point Cliente(Point p) => new(p.X - Cx, p.Y - Cy);

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            Focus();
            var p = e.GetPosition(this);
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed && !_arbitro)
            {
                AbrirContexto(p);
                e.Handled = true;
                return;
            }
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            // a Ajuda do Arbitro e modal: so o Fechar
            if (_arbitro)
            {
                var ja = JanelaArbitro();
                if (_moderno ? _mFecharArbitro.Contains(p) : RArbFechar.Contains(new Point(p.X - ja.X - 3, p.Y - ja.Y - 28)))
                { _arbitro = false; InvalidateVisual(); }
                e.Handled = true;
                return;
            }

            // 16:9 classico: o clique num titulo de coluna ordena o elenco
            if (!_moderno && Formato.Largo && _menuAberto < 0 && RTitulos.Contains(Cliente(p)))
            {
                Ordenar(ColunaClassica(Cliente(p).X - RTitulos.X));
                e.Handled = true;
                return;
            }

            // menus
            int mb = MenuDaBarra(p);
            if (_menuAberto >= 0)
            {
                int it = ItemEm(p);
                if (it >= 0) { Executar(MenuDe(_menuAberto).Itens[it]); e.Handled = true; return; }
                if (CaixaMenu(_menuAberto).Contains(p)) return;
                _menuAberto = mb == _menuAberto ? -1 : mb;
                _itemSobre = -1;
                InvalidateVisual();
                e.Handled = true;
                return;
            }
            if (mb >= 0)
            {
                _menuAberto = mb;
                _itemSobre = -1;
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            var pc = Cliente(p);
            // lista e barra de rolagem
            if (RBarra.Contains(pc))
            {
                var (cur, rola) = CursorBarra();
                if (!rola) return;
                if (pc.Y < RBarra.Y + Seta) Rolar(_rolagem - 16);
                else if (pc.Y >= RBarra.Bottom - Seta) Rolar(_rolagem + 16);
                else if (cur.Contains(pc))
                {
                    _arraste = pc; _rolagemInicio = _rolagem; _arrasteBarra = true;
                    e.Pointer.Capture(this);
                }
                else Rolar(_rolagem + (pc.Y < cur.Y ? -1 : 1) * (RItens.Height - 16));
                InvalidateVisual();
                return;
            }
            if (RItens.Contains(pc))
            {
                _arraste = pc; _rolagemInicio = _rolagem; _moveu = false; _arrasteBarra = false;
                e.Pointer.Capture(this);
                return;
            }
            if (_moderno) { CliqueModerno(pc); return; }
            // abas
            int aba = AbaEm(pc);
            if (aba >= 0) { _pagina = PaginasAbas[aba]; InvalidateVisual(); return; }
            // a caixa do adversario abre a aba Adversario (no 16:9 ela ja esta a vista)
            if (ROponente.Contains(pc) && _adv != null && !_largo) { _pagina = PgAdversario; InvalidateVisual(); return; }
            // 16:9: o nome do arbitro, o Calendario e o Elenco na coluna da direita
            if (_largo && RJogoLargo.Contains(pc))
            {
                var pj = new Point(pc.X - RJogoLargo.X, pc.Y - RJogoLargo.Y);
                if (_jogo != null && _jogo.Casa != 0 && _jogo.Fora != 0 && RArbitro.Contains(pj))
                { _arbitro = true; InvalidateVisual(); }
                else if (_adv != null && RCalendarioLargo.Contains(pj)) Campeonato.Calendario(_s, _adv, Voltar);
                else if (_adv != null && Registo.AddIn(0x10) && RPlantelLargo.Contains(pj)) TelaPlantel.Abrir(_s, _adv, _e, 0, Voltar);
                return;
            }
            // botoes do Notebook
            var pn = new Point(pc.X - PNotebook.X, pc.Y - PNotebook.Y);
            if (!RNotebook.Contains(pc)) return;
            switch (_pagina)
            {
                case PgEscalacao when RJogar.Contains(pn): Jogar(); break;
                case PgJogo when _jogo != null && _jogo.Casa != 0 && _jogo.Fora != 0 && RArbitro.Contains(pn):
                    _arbitro = true; InvalidateVisual(); break;
                // o juro do emprestimo e o rotulo dele (4:3e45, 4:3e27), com o add-in 0x13
                case PgFinancas when Registo.AddIn(0x13) && new Rect(16, _launcher ? 251 : 264, 241, 16).Contains(pn):
                    TelaEmprestimo.Abrir(_s, _e, Voltar); break;
                case PgFinancas when _launcher && RIngressos.Contains(pn):
                    // as setas do TSpinButton (o clique no texto nao muda nada)
                    if (pn.X >= RIngressos.Right - 17) MudarIngressos(pn.Y < RIngressos.Y + RIngressos.Height / 2 ? 1 : -1);
                    break;
                // 4:3774 e o calendario: o plantel do adversario (com esta equipa a comprar) so
                // com a compra directa (add-in 0x10, 4:1f5b), como o original
                case PgAdversario when RCalendario.Contains(pn) && _adv != null: Campeonato.Calendario(_s, _adv, Voltar); break;
                case PgAdversario when RPlantel.Contains(pn) && _adv != null && Registo.AddIn(0x10): TelaPlantel.Abrir(_s, _adv, _e, 0, Voltar); break;
                case PgAdversario when RNomeAdv.Contains(pn): break;
                case PgJogador when _sel != null && MercadoJogo.PodeMudarSalario(_sel) && RRenovar.Contains(pn):
                    Renovar(); break;
                case PgJogador when _sel != null && RVender.Contains(pn):
                    Vender(); break;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_menuAberto >= 0)
            {
                int mb = MenuDaBarra(p);
                if (mb >= 0 && mb != _menuAberto && _menuAberto != Contexto) { _menuAberto = mb; _itemSobre = -1; InvalidateVisual(); return; }
                int it = ItemEm(p);
                if (it != _itemSobre) { _itemSobre = it; InvalidateVisual(); }
                return;
            }
            if (_arraste is not Point ini) { if (_moderno) SobreModerno(Cliente(p)); return; }
            var pc = Cliente(p);
            if (_arrasteBarra)
            {
                double total = AlturaElenco(), vis = RItens.Height, trilho = RBarra.Height - 2 * Seta;
                var (cur, _) = CursorBarra();
                double livre = trilho - cur.Height;
                if (livre > 0) Rolar(_rolagemInicio + (pc.Y - ini.Y) * (total - vis) / livre);
                InvalidateVisual();
                return;
            }
            if (Math.Abs(pc.Y - ini.Y) > 6) _moveu = true;
            if (_moveu) { Rolar(_rolagemInicio - (pc.Y - ini.Y)); InvalidateVisual(); }
        }

        // clique num jogador: mostra-o na aba Jogador; clicar de novo no mesmo (ou o duplo
        // clique) alterna fora -> titular -> reserva
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_arraste is Point ini && !_moveu && !_arrasteBarra)
            {
                var j = JogadorEm(ini.Y);
                if (j != null)
                {
                    if (j == _sel) Alternar(j);
                    _sel = j;
                    MostrarFicha();
                    InvalidateVisual();
                }
            }
            _arraste = null;
            _arrasteBarra = false;
            e.Pointer.Capture(null);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            if (_arbitro) return;
            var pw = Cliente(e.GetPosition(this));
            if (_pagina == PgFinancas && _launcher && e.Delta.Y != 0 &&
                (_moderno ? _mIngressos.Contains(pw) : RIngressos.Contains(new Point(pw.X - PNotebook.X, pw.Y - PNotebook.Y))))
            {
                MudarIngressos(e.Delta.Y > 0 ? 1 : -1);
                e.Handled = true;
                return;
            }
            if (!RLista.Contains(Cliente(e.GetPosition(this)))) return;
            Rolar(_rolagem - e.Delta.Y * 16 * 3);
            InvalidateVisual();
            e.Handled = true;
        }

        // ============================================================ teclado
        protected override void OnKeyDown(KeyEventArgs e)
        {
            var mods = e.KeyModifiers;
            if (_arbitro)
            {
                if (e.Key is Key.Escape or Key.Enter or Key.Return or Key.Space) { _arbitro = false; InvalidateVisual(); }
                e.Handled = true;
                return;
            }
            if (_menuAberto >= 0 && e.Key == Key.Escape) { _menuAberto = -1; InvalidateVisual(); e.Handled = true; return; }
            // o "glitch" do original: Alt+Shift+F4 nao bate com o atalho do Sair (Alt+F4) e o
            // Windows fecha a janela da equipa como se fosse o Jogar, sem conferir a escalacao
            if (e.Key == Key.F4 && mods == (KeyModifiers.Alt | KeyModifiers.Shift)) { _tela.Terminar(false); e.Handled = true; return; }
            if (e.Key == Key.F4 && mods == KeyModifiers.Alt) { _tela.Terminar(true); e.Handled = true; return; }
            if (e.Key == Key.F4 && mods == (KeyModifiers.Shift | KeyModifiers.Control)) { Atalho("Shift+Ctrl+F4"); e.Handled = true; return; }
            if (mods != KeyModifiers.None) return;
            string? a = e.Key switch
            {
                >= Key.F1 and <= Key.F12 => "F" + (e.Key - Key.F1 + 1),
                Key.A => "A", Key.M => "M", Key.T => "T", Key.C => "C", Key.V => "V",
                _ => null,
            };
            if (a != null && Atalho(a)) { e.Handled = true; return; }
            switch (e.Key)
            {
                case Key.Space when _sel != null:
                    Alternar(_sel);
                    InvalidateVisual();
                    e.Handled = true;
                    break;
                // o Jogar e o botao padrao (Default) da aba Escalacao
                case Key.Enter or Key.Return when _pagina == PgEscalacao:
                    Jogar();
                    e.Handled = true;
                    break;
                case Key.Up: case Key.Down:
                    {
                        var o = Ordem();
                        int i = _sel == null ? -1 : o.IndexOf(_sel);
                        i = Math.Clamp(i + (e.Key == Key.Up ? -1 : 1), 0, o.Count - 1);
                        if (o.Count > 0) { _sel = o[i]; MostrarFicha(); MostrarEscolhido(); InvalidateVisual(); }
                        e.Handled = true;
                        break;
                    }
            }
        }

        // o escolhido na aba Jogador
        void MostrarFicha() => _pagina = PgJogador;

        // rola a lista ate o escolhido ficar visivel
        void MostrarEscolhido()
        {
            foreach (var (p, _, y, h) in Linhas())
                if (p != null && p == _sel)
                {
                    if (y < _rolagem) Rolar(y);
                    else if (y + h > _rolagem + RItens.Height) Rolar(y + h - RItens.Height);
                    return;
                }
        }

        bool Atalho(string a)
        {
            foreach (var m in _menus)
                foreach (var it in m.Itens)
                    if (it.Atalho == a)
                    {
                        Executar(it);
                        return true;
                    }
            return false;
        }
    }
}
