using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Patrocinio;
using RetroFoot.Motor.Rodada;
using Esc = RetroFoot.Motor.Rodada.Escalacao;
using Mem = RetroFoot.Motor.Partida.Memoria;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// A tela da equipa no visual Moderno (Visual.cs), no estilo da jornada e da classificacao
// modernas: fundo escuro, cartoes arredondados, os logos (Escudos) e a fonte Inter. Mesmo
// estado, menus, atalhos e acoes da tela original (o Painel); so o desenho e os cliques mudam:
//  - em cima, a barra de menus (os mesmos menus) e o cabecalho com o logo, o nome, o pais, o
//    tecnico, a posicao na divisao e o Jogar (o mesmo da aba Escalacao);
//  - a esquerda, o elenco numa tabela agrupada por posicao (G, D, M, A), com a escalacao
//    (verde = titular, anel amarelo = reserva), S/L/A ao lado do nome e as colunas que cabem
//    (no 16:9 tambem os gols na temporada e, com as regras do launcher, a Nota);
//  - a direita, o cartao do proximo jogo (logos, casa/fora, rodada, arbitro, ultimo jogo) e
//    as mesmas paginas do Notebook (Jogo, Jogador, Financas, Escalacao, Adversario e
//    Patrocinio) em abas.
// Clique num jogador mostra a ficha; clicar de novo nele (ou espaco) muda a escalacao.
public sealed partial class TelaEquipa
{
    sealed partial class Painel
    {
        // as cores sao as do TemaVerde (o tema do Moderno)
        internal static readonly IBrush MFundo = TemaVerde.FundoTela;
        static readonly IBrush MBarra = TemaVerde.Barra, MCartao = TemaVerde.Cartao, MCartao2 = TemaVerde.CartaoForte;
        static readonly IBrush MLinha = TemaVerde.Linha, MTexto = TemaVerde.Texto, MFraco = TemaVerde.TextoFraco;
        static readonly IBrush MAzul = TemaVerde.Destaque, MVerde = TemaVerde.Verde, MSel = TemaVerde.Selecao, MSobre = TemaVerde.Hover;
        static readonly IBrush MVermelho = TemaVerde.Vermelho, MAmarelo = TemaVerde.Destaque, MLaranja = TemaVerde.Laranja;
        static readonly IBrush MEscuro = TemaVerde.SobreDestaque, MMenuFundo = TemaVerde.Menu, MApagado = TemaVerde.Apagado;
        static readonly IBrush MZebra = TemaVerde.Zebra;
        static readonly IPen MBorda = new Pen(TemaVerde.Borda, 1);
        static readonly IBrush[] MCorPosicao = { TemaVerde.Destaque, TemaVerde.Azul, TemaVerde.Verde, TemaVerde.Vermelho };
        static readonly string[] MNomesPosicao = { "Goleiros", "Defensores", "Meio-campistas", "Atacantes" };
        static readonly string[] MNomePosicao = { "Goleiro", "Defensor", "Meio-campista", "Atacante" };
        static readonly FontFamily Inter = new("fonts:Inter#Inter");

        const double MM = 12, MMenuA = 30, MBarraX = 6, MItemA = 26, MSeparadorA = 9, MTamMenu = 12.5;
        const double MLinhaA = 23, MTituloA = 28;

        // os cartoes (coordenadas da tela, sem a moldura: Cx = Cy = 0)
        Rect _mCab, _mElenco, _mJogo, _mAbas, _mPagina;
        // o que se clica, refeito a cada desenho (o ultimo por cima)
        readonly List<(Rect R, Action A)> _alvos = new();
        Rect _mIngressos, _mFecharArbitro;
        SJogador? _sobre;

        static IBrush Hex(string s) => new SolidColorBrush(Color.Parse(s));

        // ------------------------------------------------------------ medidas
        void MedidasModernas(double l, out Rect lista, out Rect itens, out Rect barra)
        {
            _mCab = new Rect(MM, MMenuA + 8, l - 2 * MM, 64);
            double topo = _mCab.Bottom + 8, fim = 600 - MM;
            double xs = _largo ? 612 : 444;
            _mElenco = new Rect(MM, topo, xs - MM, fim - topo);
            double xr = xs + MM, wr = l - MM - xr;
            _mJogo = new Rect(xr, topo, wr, 142);
            _mAbas = new Rect(xr, _mJogo.Bottom + 8, wr, 30);
            _mPagina = new Rect(xr, _mAbas.Bottom + 6, wr, fim - _mAbas.Bottom - 6);
            lista = _mElenco;
            double yi = _mElenco.Y + 40 + 26;
            itens = new Rect(_mElenco.X + 6, yi, _mElenco.Width - 6 - 16, _mElenco.Bottom - 8 - yi);
            barra = new Rect(_mElenco.Right - 14, yi, 10, itens.Height);
        }

        // o elenco agrupado: o titulo de cada posicao e os jogadores dela
        List<(SJogador? P, int Pos, double Y, double H)> LinhasModernas(List<SJogador> o)
        {
            var r = new List<(SJogador?, int, double, double)>();
            double y = 0;
            int ultima = -1;
            foreach (var p in o)
            {
                int pos = Math.Min((int)p.Posicao, 3);
                if (pos != ultima)
                {
                    r.Add((null, pos, y, MTituloA));
                    y += MTituloA;
                    ultima = pos;
                }
                r.Add((p, pos, y, MLinhaA));
                y += MLinhaA;
            }
            return r;
        }

        // ------------------------------------------------------------ texto e formas
        static FormattedText TM(string s, double tam, IBrush cor, bool negrito = false, double max = 0, bool medio = false)
        {
            var t = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Inter, FontStyle.Normal, negrito ? FontWeight.Bold : medio ? FontWeight.Medium : FontWeight.Normal), tam * Acessibilidade.Texto, cor);
            if (max > 0)
            {
                t.MaxTextWidth = Math.Max(1, max);
                t.MaxLineCount = 1;
                t.Trimming = TextTrimming.CharacterEllipsis;
            }
            return t;
        }

        static double LarguraM(string s, double tam, bool negrito = false) => TM(s, tam, MTexto, negrito).WidthIncludingTrailingWhitespace;

        // o texto com o meio na altura yc
        static double EscreveM(DrawingContext dc, string s, double x, double yc, double tam, IBrush cor, bool negrito = false, double max = 0, bool medio = false)
        {
            var t = TM(s, tam, cor, negrito, max, medio);
            dc.DrawText(t, new Point(x, yc - t.Height / 2));
            return t.WidthIncludingTrailingWhitespace;
        }

        static void DireitaM(DrawingContext dc, string s, double xFim, double yc, double tam, IBrush cor, bool negrito = false)
        {
            var t = TM(s, tam, cor, negrito);
            dc.DrawText(t, new Point(xFim - t.WidthIncludingTrailingWhitespace, yc - t.Height / 2));
        }

        static void CentroM(DrawingContext dc, string s, double xc, double yc, double tam, IBrush cor, bool negrito = false, double max = 0)
        {
            var t = TM(s, tam, cor, negrito, max);
            dc.DrawText(t, new Point(xc - t.WidthIncludingTrailingWhitespace / 2, yc - t.Height / 2));
        }

        static void Cartao(DrawingContext dc, Rect r, IBrush? cor = null) => dc.DrawRectangle(cor ?? MCartao, cor == null ? MBorda : null, r.Deflate(0.5), 10, 10);

        static Color CorC(byte[] b, Color padrao) =>
            b.Length < 4 || (b[3] & 0x80) != 0 ? padrao : Color.FromRgb(b[0], b[1], b[2]);

        // o logo da equipa (ou o circulo nas cores dela)
        static void LogoM(DrawingContext dc, SEquipa? e, Rect r)
        {
            if (e == null) return;
            TelaJornada.Logo(dc, Escudos.Escudo(e), CorC(e.CorFundo, Colors.White), CorC(e.CorLetra, Colors.Black), r);
        }

        static void BandeiraM(DrawingContext dc, string pais, Rect r)
        {
            var b = Imagem($"Bandeiras/{pais}.png");
            if (b == null) return;
            using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                dc.DrawImage(b, r);
        }

        // um botao em forma de pilula; principal = cheio
        Rect BotaoM(DrawingContext dc, double x, double yc, string texto, bool principal, bool hab, Action? acao, double tam = 12.5, double minimo = 0)
        {
            var t = TM(texto, tam, principal && hab ? MEscuro : hab ? MAzul : MFraco, true);
            double w = Math.Max(minimo, t.WidthIncludingTrailingWhitespace + 28), h = tam + 16;
            var r = new Rect(x, yc - h / 2, w, h);
            if (principal) dc.DrawRectangle(hab ? TemaVerde.BotaoPrimario : MLinha, null, r, h / 2, h / 2);
            else dc.DrawRectangle(null, new Pen(hab ? MLinha : MCartao2, 1.2), r.Deflate(0.6), h / 2, h / 2);
            dc.DrawText(t, new Point(r.X + (w - t.WidthIncludingTrailingWhitespace) / 2, yc - t.Height / 2));
            if (hab && acao != null) _alvos.Add((r, acao));
            return r;
        }

        // a barra do moral (as cores do medidor do original: 4:1d5a e 3:5836)
        static void MoralM(DrawingContext dc, Rect r, SEquipa e)
        {
            double m = Moral(e);
            IBrush cor = m > 1.25 ? MVerde : m < 0.65 ? MVermelho : MAmarelo;
            double v = Math.Clamp(Math.Round(m * 10, MidpointRounding.ToEven) / 20, 0, 1);
            dc.DrawRectangle(MLinha, null, r, r.Height / 2, r.Height / 2);
            if (v > 0) dc.DrawRectangle(cor, null, new Rect(r.X, r.Y, Math.Max(r.Height, r.Width * v), r.Height), r.Height / 2, r.Height / 2);
        }

        // ------------------------------------------------------------ selos do estado
        // amarelos acumulados (o numero no cartao amarelo; 2 = pendurado, dois cartoes
        // empilhados como na jornada), suspensao (os jogos no cartao vermelho) e lesao (os
        // jogos num quadrado branco com a cruz vermelha)
        const int SeloAmarelo = 0, SeloVermelho = 1, SeloLesao = 2;
        static readonly IBrush CartaoAmarelo = Hex("#fbd000"), CartaoVermelho = Hex("#e53935"), CruzVermelha = Hex("#d32f2f");

        static List<(int Tipo, int N)> Selos(SJogador p, bool launcher)
        {
            var r = new List<(int, int)>();
            if (p.Suspensao > 0) r.Add((SeloVermelho, p.Suspensao));
            else if (launcher && Amarelos(p) > 0) r.Add((SeloAmarelo, Amarelos(p)));
            if (p.JogosLesao > 0) r.Add((SeloLesao, p.JogosLesao));
            return r;
        }

        static string DicaSelo(int tipo, int n) => tipo switch
        {
            SeloAmarelo => n == 1 ? T("1 amarelo acumulado")
                : T("{0} amarelos acumulados", n) + "\n" + T("Pendurado: mais um amarelo e fica suspenso"),
            SeloVermelho => n == 1 ? T("Suspenso por 1 jogo") : T("Suspenso por {0} jogos", n),
            _ => n == 1 ? T("Lesionado por 1 jogo") : T("Lesionado por {0} jogos", n),
        };

        // desenha o selo com o meio em yc a partir de x; devolve o retangulo dele
        static Rect Selo(DrawingContext dc, double x, double yc, int tipo, int n)
        {
            string num = n.ToString();
            double h = 16, w = Math.Max(12, LarguraM(num, 10, true) + 5);
            var borda = new Pen(MEscuro, 0.8);
            if (tipo == SeloLesao)
            {
                w = Math.Max(16, LarguraM(num, 10, true) + 10);
                var r = new Rect(x, yc - h / 2, w, h);
                dc.DrawRectangle(Brushes.White, borda, r, 2.5, 2.5);
                // a cruz vermelha pequena no canto e os jogos
                double cx = r.X + 4, cy = r.Y + 4;
                dc.FillRectangle(CruzVermelha, new Rect(cx - 2.5, cy - 0.8, 5, 1.6));
                dc.FillRectangle(CruzVermelha, new Rect(cx - 0.8, cy - 2.5, 1.6, 5));
                CentroM(dc, num, r.Center.X + 2, yc + 1, 10, CruzVermelha, true);
                return r;
            }
            if (tipo == SeloAmarelo && n >= 2)
            {
                // pendurado: o cartao de tras um pouco deslocado
                dc.DrawRectangle(CartaoAmarelo, borda, new Rect(x + 3, yc - h / 2 - 2, w, h), 2, 2);
                var rf = new Rect(x, yc - h / 2 + 1, w, h);
                dc.DrawRectangle(CartaoAmarelo, borda, rf, 2, 2);
                CentroM(dc, num, rf.Center.X, rf.Center.Y, 10, MEscuro, true);
                return new Rect(x, yc - h / 2 - 2, w + 3, h + 3);
            }
            var rc = new Rect(x, yc - h / 2, w, h);
            dc.DrawRectangle(tipo == SeloAmarelo ? CartaoAmarelo : CartaoVermelho, borda, rc, 2, 2);
            CentroM(dc, num, rc.Center.X, yc, 10, tipo == SeloAmarelo ? MEscuro : Brushes.White, true);
            return rc;
        }

        // as dicas (tooltips) dos selos, refeitas a cada desenho
        readonly List<(Rect R, string Texto)> _dicas = new();
        string? _dica;
        Avalonia.Threading.DispatcherTimer? _relogioDica;

        // o ponteiro sobre um selo: a dica dele depois de meio segundo
        void AtualizarDica(Point pc)
        {
            string? d = null;
            for (int i = _dicas.Count - 1; i >= 0 && d == null; i--)
                if (_dicas[i].R.Contains(pc)) d = _dicas[i].Texto;
            if (d == _dica) return;
            _dica = d;
            _relogioDica?.Stop();
            ToolTip.SetIsOpen(this, false);
            ToolTip.SetTip(this, d);
            if (d == null) return;
            _relogioDica ??= new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _relogioDica.Tick -= AbrirDica;
            _relogioDica.Tick += AbrirDica;
            _relogioDica.Start();
        }

        void AbrirDica(object? o, EventArgs e)
        {
            _relogioDica?.Stop();
            if (_dica != null && IsPointerOver) ToolTip.SetIsOpen(this, true);
        }

        string Lugar()
        {
            var m = _s.J.M;
            int dv = m.DivisaoDa(_e);
            if (dv < 0) return "";
            if (dv >= 4) return T("Distrital");
            return LugarNaDivisao(m.S.Divisoes[dv].Equipas.IndexOf(_e.ID) + 1, Pascal(m.S.Divisoes[dv].Nome, 20));
        }

        // ------------------------------------------------------------ desenho
        void RenderModerno(DrawingContext dc)
        {
            _alvos.Clear();
            _dicas.Clear();
            ComecarQuadro();
            dc.FillRectangle(MFundo, new Rect(0, 0, L, A));
            CabecalhoModerno(dc);
            ElencoModerno(dc);
            JogoModerno(dc);
            AbasModernas(dc);
            Cartao(dc, _mPagina);
            var pg = _mPagina.Deflate(14);
            using (dc.PushClip(_mPagina))
                switch (_pagina)
                {
                    case PgJogo: JogoPaginaModerna(dc, pg); break;
                    case PgJogador: JogadorModerno(dc, pg); break;
                    case PgFinancas: FinancasModernas(dc, pg); break;
                    case PgEscalacao: EscalacaoModerna(dc, pg); break;
                    case PgAdversario: AdversarioModerno(dc, pg); break;
                    case PgPatrocinio: PatrocinioModerno(dc, pg); break;
                    case PgMensagem:
                        dc.DrawText(TM(_mensagem, 14, MTexto).Com(t => t.MaxTextWidth = pg.Width), new Point(pg.X, pg.Y + 20));
                        break;
                }
            BarraMenusModerna(dc);
            if (_arbitro) ArbitroModerno(dc);
            DesenharMenuAberto(dc);
            FimQuadro();
        }

        void BarraMenusModerna(DrawingContext dc)
        {
            dc.FillRectangle(MBarra, new Rect(0, 0, L, MMenuA));
            var pos = PosicoesMenu();
            for (int i = 0; i < _menus.Count; i++)
            {
                var r = new Rect(BarraX + pos[i].X0, 3, pos[i].X1 - pos[i].X0, MMenuA - 6);
                bool aberto = i == _menuAberto;
                if (aberto) dc.DrawRectangle(MLinha, null, r, 6, 6);
                CentroM(dc, _menus[i].Texto, r.Center.X, r.Center.Y, MTamMenu, aberto ? MTexto : MFraco);
            }
            DireitaM(dc, T("Temporada {0}", _s.J.M.Ano), L - MM - 4, MMenuA / 2, 12, MFraco);
        }

        void MenuAbertoModerno(DrawingContext dc)
        {
            var m = MenuDe(_menuAberto);
            var cx = CaixaMenu(_menuAberto);
            dc.DrawRectangle(TemaVerde.Sombra, null, cx.Translate(new Vector(0, 3)).Inflate(1), 9, 9);
            dc.DrawRectangle(MMenuFundo, MBorda, cx, 8, 8);
            double y = cx.Y + 6;
            for (int k = 0; k < m.Itens.Count; k++)
            {
                var it = m.Itens[k];
                if (it.Texto == "-")
                {
                    dc.FillRectangle(MLinha, new Rect(cx.X + 10, y + 4, cx.Width - 20, 1));
                    y += MSeparadorA;
                    continue;
                }
                bool hab = it.Habilitado?.Invoke() ?? true;
                bool sobre = k == _itemSobre && hab;
                if (sobre) dc.DrawRectangle(MLinha, null, new Rect(cx.X + 4, y, cx.Width - 8, MItemA), 5, 5);
                var cor = !hab ? MApagado : MTexto;
                EscreveM(dc, it.Texto, cx.X + 16, y + MItemA / 2, MTamMenu, cor);
                if (it.Atalho != "") DireitaM(dc, it.Atalho, cx.Right - 14, y + MItemA / 2, 11.5, hab ? MFraco : MApagado);
                y += MItemA;
            }
        }

        void CabecalhoModerno(DrawingContext dc)
        {
            var c = _mCab;
            Cartao(dc, c);
            LogoM(dc, _e, new Rect(c.X + 12, c.Y + 8, 48, 48));
            double x = c.X + 72;
            // a direita: (16:9) o caixa e o moral e o Jogar
            double xd = c.Right - 14;
            double wj = Math.Max(110, LarguraM(T("Jogar"), 14, true) + 28);
            var jogar = BotaoBrilho(dc, xd - wj, c.Center.Y, "jogar-cab", T("Jogar"), true, JogarHabilitado(), Jogar, 14, 110);
            xd = jogar.X - 20;
            if (_largo)
            {
                double wm = 120;
                EscreveM(dc, T("Moral"), xd - wm, c.Y + 22, 11, MFraco);
                MoralM(dc, new Rect(xd - wm, c.Y + 36, wm, 8), _e);
                xd -= wm + 24;
                string din = Dinheiro((int)_e.Dinheiro);
                double wd = Math.Max(LarguraM(din, 14, true), 90);
                EscreveM(dc, T("Dinheiro em caixa"), xd - wd, c.Y + 22, 11, MFraco);
                EscreveM(dc, din, xd - wd, c.Y + 41, 14, (int)_e.Dinheiro < 0 ? MVermelho : MTexto, true);
                xd -= wd + 24;
            }
            double max = xd - x - 8;
            EscreveM(dc, Texto(_e.NomeCompleto), x, c.Y + 22, 19, MTexto, true, max);
            // pais, tecnico e a posicao na divisao (com o logo da liga, num jogo de um pais so)
            double y2 = c.Y + 45;
            string pais = PaisDe(_e.Pais);
            BandeiraM(dc, pais, new Rect(x, y2 - 7, 14, 14));
            double xs = x + 20;
            xs += EscreveM(dc, NomePais(pais), xs, y2, 12, MFraco, max: Math.Max(1, x + max - xs));
            string tec = _s.TreinadorDa(_e) is { } t ? Texto(t.Nome) : "";
            if (tec != "" && xs < x + max - 40)
            {
                xs += EscreveM(dc, "  ·  " + T("Técnico") + " ", xs, y2, 12, MFraco);
                xs += EscreveM(dc, tec, xs, y2, 12, MTexto, medio: true, max: Math.Max(1, x + max - xs));
            }
            string lugar = Lugar();
            if (lugar != "" && xs < x + max - 60)
            {
                xs += EscreveM(dc, "  ·  ", xs, y2, 12, MFraco);
                int dv = _s.J.M.DivisaoDa(_e);
                if (Escudos.Liga(_s.S, dv) is { } liga && xs + 20 < x + max)
                {
                    TelaJornada.Imagem(dc, liga, new Rect(xs, y2 - 7, 14, 14));
                    xs += 19;
                }
                EscreveM(dc, lugar, xs, y2, 12, MFraco, max: Math.Max(1, x + max - xs));
            }
        }

        // ------------------------------------------------------------ elenco
        // as colunas da direita (titulo, largura, valor); o nome fica com o resto
        List<(string T, double W, Func<SJogador, string>? V)> ColunasModernas()
        {
            // o salario cresce com o maior valor do elenco (moeda e milhares variam): o numero
            // fica a 8 da borda e com 10 de folga antes da bandeira do pais
            double wSal = _largo ? 74 : 64;
            foreach (var p in _e.Jogadores) wSal = Math.Max(wSal, LarguraM(Dinheiro(p.Salario), 12.5) + 18);
            var r = new List<(string, double, Func<SJogador, string>?)>
            {
                ("Força", 42, p => p.Forca.ToString()),
                ("País", _largo ? 64 : 40, null),
                ("Salário", wSal, p => Dinheiro(p.Salario)),
                ("Jogos", _largo ? 46 : 42, p => Hist(p, Mem.HistJogos).ToString()),
                ("Gols", _largo ? 42 : 36, p => Hist(p, Mem.HistGolos).ToString()),
            };
            if (_largo) r.Add(("Temp.", 46, p => Mem.Golos14(p).ToString()));
            if (_largo && _launcher) r.Add(("Nota", 42, p => p.Nota.ToString()));
            return r;
        }

        void ElencoModerno(DrawingContext dc)
        {
            var c = _mElenco;
            Cartao(dc, c);
            int tit = 0, res = 0;
            foreach (var p in _e.Jogadores)
            {
                int esc = Mem.Escalacao(p);
                if (esc == Mem.EscTitular) tit++; else if (esc == Mem.EscReserva) res++;
            }
            EscreveM(dc, T("Elenco"), c.X + 16, c.Y + 22, 16, MTexto, true);
            string resumo = T("{0} jogadores  ·  {1} titulares  ·  {2} reservas", _e.Jogadores.Count, tit, res);
            DireitaM(dc, resumo, c.Right - 16, c.Y + 22, 12, MFraco);

            var cols = ColunasModernas();
            double xNome = RItens.X + 46;
            double xFim = RItens.Right - 6;
            // o cabecalho das colunas
            double yh = c.Y + 40 + 13;
            dc.FillRectangle(MLinha, new Rect(c.X + 10, c.Y + 40 + 25, c.Width - 20, 1));
            // clicar num titulo ordena o elenco por ele (dentro de cada posicao); de novo inverte;
            // "Jogador" volta a ordem do nome. A seta fica antes do titulo (nao corta na borda)
            string Seta(string col) => _ordemCol == col ? (_ordemDesc ? "▼ " : "▲ ") : "";
            double wj = EscreveM(dc, Seta("Jogador") + T("Jogador"), xNome, yh, 11, _ordemCol == "Jogador" ? MAmarelo : MFraco);
            _alvos.Add((new Rect(xNome - 4, yh - 9, wj + 8, 18), () => Ordenar("Jogador")));
            double xc = xFim;
            for (int i = cols.Count - 1; i >= 0; i--)
            {
                string col = cols[i].T, txt = Seta(col) + T(col);
                var cor = _ordemCol == col ? MAmarelo : MFraco;
                if (cols[i].V == null) CentroM(dc, txt, xc - cols[i].W / 2, yh, 11, cor);
                else DireitaM(dc, txt, xc, yh, 11, cor);
                _alvos.Add((new Rect(xc - cols[i].W, yh - 9, cols[i].W, 18), () => Ordenar(col)));
                xc -= cols[i].W;
            }
            double xMaxNome = xc - 8;

            Rolar(_rolagem);
            using (dc.PushClip(new Rect(c.X, RItens.Y, c.Width, RItens.Height)))
            {
                double y0 = RItens.Y - _rolagem;
                var linhas = Linhas();
                for (int k = 0; k < linhas.Count; k++)
                {
                    var (p, pos, ly, lh) = linhas[k];
                    double y = y0 + ly;
                    if (y + lh < RItens.Y || y > RItens.Bottom) continue;
                    if (p == null)
                    {
                        int n = 0;
                        for (int j = k + 1; j < linhas.Count && linhas[j].P != null; j++) n++;
                        double yc = y + lh / 2 + 3;
                        dc.DrawRectangle(MCorPosicao[pos], null, new Rect(RItens.X + 6, yc - 6, 3, 12), 1.5, 1.5);
                        double w = EscreveM(dc, T(MNomesPosicao[pos]).ToUpperInvariant(), RItens.X + 16, yc, 11, MTexto, true);
                        EscreveM(dc, n.ToString(), RItens.X + 16 + w + 6, yc, 11, MFraco);
                        continue;
                    }
                    LinhaModerna(dc, p, pos, new Rect(RItens.X, y, RItens.Width, lh), cols, xNome, xMaxNome, xFim);
                }
            }
            // a barra de rolagem fina
            var (cur, rola) = CursorBarra();
            if (rola)
            {
                dc.DrawRectangle(MCartao2, null, new Rect(RBarra.X + 3, RBarra.Y, 4, RBarra.Height), 2, 2);
                dc.DrawRectangle(_arrasteBarra ? MFraco : MApagado, null, new Rect(cur.X + 2, cur.Y, 6, cur.Height), 3, 3);
            }
        }

        void LinhaModerna(DrawingContext dc, SJogador p, int pos, Rect r, List<(string T, double W, Func<SJogador, string>? V)> cols,
            double xNome, double xMaxNome, double xFim)
        {
            bool sel = p == _sel;
            var fundo = new Rect(r.X, r.Y + 1, r.Width, r.Height - 2);
            if (sel)
            {
                dc.DrawRectangle(MSel, null, fundo, 5, 5);
                dc.DrawRectangle(MAzul, null, new Rect(r.X, r.Y + 4, 3, r.Height - 8), 1.5, 1.5);
            }
            else if (p == _sobre) dc.DrawRectangle(MSobre, null, fundo, 5, 5);
            double yc = r.Center.Y;
            bool fora = p.JogosLesao > 0 || p.Suspensao > 0;
            // a escalacao: titular (bola verde), reserva (anel amarelo), fora (ponto apagado)
            var ce = new Point(r.X + 14, yc);
            switch (Mem.Escalacao(p))
            {
                case Mem.EscTitular: dc.DrawEllipse(MVerde, null, ce, 4.5, 4.5); break;
                case Mem.EscReserva: dc.DrawEllipse(null, new Pen(MAmarelo, 1.8), ce, 4, 4); break;
                default: dc.DrawEllipse(MLinha, null, ce, 2.5, 2.5); break;
            }
            // a posicao
            var rp = new Rect(r.X + 24, yc - 8, 16, 16);
            dc.DrawRectangle(MCartao2, null, rp, 4, 4);
            CentroM(dc, Posicao(pos), rp.Center.X, yc, 10.5, MCorPosicao[pos], true);
            // o nome e, logo depois, os selos: amarelos, suspensao, lesao
            var cor = fora ? MFraco : MTexto;
            var selos = Selos(p, _launcher);
            double max = Math.Max(1, xMaxNome - xNome - 24 * selos.Count);
            var nomeJ = NomeJogador(p);
            double wn = EscreveM(dc, nomeJ, xNome, yc, 13, cor, sel, max, medio: !sel);
            double xb = xNome + Math.Min(wn, max) + 6;
            // o nome cortado: a dica com ele inteiro, pela dica do painel (a geral demorava)
            if (r.Bottom > RItens.Y && r.Y < RItens.Bottom && TM(nomeJ, 13, cor, sel, 0, !sel).WidthIncludingTrailingWhitespace > max)
                _dicas.Add((new Rect(xNome, r.Y, Math.Min(wn, max), r.Height), nomeJ));
            foreach (var (tipo, n) in selos)
            {
                var rs = Selo(dc, xb, yc, tipo, n);
                // a dica so na parte visivel da lista
                if (rs.Bottom > RItens.Y && rs.Y < RItens.Bottom) _dicas.Add((rs.Inflate(2), DicaSelo(tipo, n)));
                xb = rs.Right + 5;
            }
            // as colunas, da direita para a esquerda
            double xc = xFim;
            for (int i = cols.Count - 1; i >= 0; i--)
            {
                var (t, w, v) = cols[i];
                if (v != null)
                {
                    bool salario = t == "Salário";
                    DireitaM(dc, v(p), xc - (salario ? 8 : 0), yc, 12.5, t == "Força" ? MTexto : salario ? MTexto : MFraco, t == "Força");
                    // o salario ja revisto nesta temporada (o "+" do original)
                    if (salario && !MercadoJogo.PodeMudarSalario(p)) EscreveM(dc, "+", xc - 6, yc - 3, 10, MFraco, true);
                }
                else
                {
                    // o pais: os estrangeiros (com * os que a Lei Bosman deixa de contar)
                    string pais = PaisDe(p.Pais);
                    if (p.B35 != 0)
                    {
                        if (_largo)
                        {
                            BandeiraM(dc, pais, new Rect(xc - w + 6, yc - 7, 14, 14));
                            EscreveM(dc, (p.B35 == 1 ? "*" : "") + pais, xc - w + 24, yc, 11, MFraco);
                        }
                        else BandeiraM(dc, pais, new Rect(xc - w / 2 - 7, yc - 7, 14, 14));
                    }
                }
                xc -= w;
            }
        }

        // ------------------------------------------------------------ proximo jogo
        void JogoModerno(DrawingContext dc)
        {
            var c = _mJogo;
            Cartao(dc, c);
            if (_jogo == null || _adv == null)
            {
                CentroM(dc, T("Sem jogo nesta rodada"), c.Center.X, c.Center.Y, 13, MFraco);
                return;
            }
            var m = _s.J.M;
            // o titulo: a divisao (com o logo da liga) e a rodada, ou a Taca; casa ou fora a direita
            string rodada = Br(Pascal(_jogo.B04, 40));
            Bitmap? logo = null;
            if (_jogo.Taca != 0) logo = Escudos.Taca(_s.S);
            else
            {
                int dv = m.DivisaoDa(_e);
                if (dv is >= 0 and <= 3)
                {
                    rodada = Pascal(m.S.Divisoes[dv].Nome, 20) + "  ·  " + rodada;
                    logo = Escudos.Liga(_s.S, dv);
                }
            }
            double x = c.X + 14, yt = c.Y + 18;
            if (logo != null) { TelaJornada.Imagem(dc, logo, new Rect(x, yt - 8, 16, 16)); x += 22; }
            bool casa = _jogo.Casa == _e.ID;
            string lado = casa ? T("CASA") : T("FORA");
            var tl = TM(lado, 10.5, casa ? MVerde : MAzul, true);
            var rl = new Rect(c.Right - 14 - tl.Width - 14, yt - 9, tl.Width + 14, 18);
            dc.DrawRectangle(MCartao2, null, rl, 9, 9);
            dc.DrawText(tl, new Point(rl.X + 7, yt - tl.Height / 2));
            EscreveM(dc, rodada, x, yt, 12, MFraco, max: rl.X - 8 - x);

            // mandante a esquerda, visitante a direita, como nos placares
            var (eqC, eqF) = casa ? (_e, _adv) : (_adv, _e);
            double ym = c.Y + 58, wl = c.Width * 0.34;
            void Lado(SEquipa eq, double xc)
            {
                LogoM(dc, eq, new Rect(xc - 18, ym - 18, 36, 36));
                CentroM(dc, Texto(eq.NomeCurto), xc, ym + 30, 12.5, eq == _e ? MTexto : MTexto, eq == _e, wl);
            }
            double xa = c.X + c.Width * 0.22, xb = c.Right - c.Width * 0.22;
            Lado(eqC, xa);
            Lado(eqF, xb);
            CentroM(dc, "×", c.Center.X, ym - 4, 22, MFraco);
            CentroM(dc, T("Próximo jogo"), c.Center.X, ym + 16, 10.5, MFraco);
            // o adversario abre a aba Adversario
            double xadv = _adv == eqC ? xa : xb;
            _alvos.Add((new Rect(xadv - wl / 2, ym - 22, wl, 64), () => _pagina = PgAdversario));

            // o arbitro (clique: a Ajuda do Arbitro) e o ultimo jogo
            double y3 = c.Bottom - 30;
            dc.FillRectangle(MLinha, new Rect(c.X + 12, y3 - 12, c.Width - 24, 1));
            var arb = _s.S.Arbitros.FindLast(a => a.ID == _jogo.Arbitro);
            double xx = c.X + 14;
            xx += EscreveM(dc, T("Árbitro") + "  ", xx, y3, 11.5, MFraco);
            if (arb != null)
            {
                string na = Texto(arb.Nome) + " (" + NomePais(PaisDe(arb.Pais)) + ")";
                double w = EscreveM(dc, na, xx, y3, 11.5, MAzul, medio: true, max: c.Right - 14 - xx);
                if (_jogo.Casa != 0 && _jogo.Fora != 0)
                    _alvos.Add((new Rect(xx - 2, y3 - 9, w + 4, 18), () => _arbitro = true));
            }
            string ult = UltimoConfronto();
            double y4 = y3 + 17;
            if (ult != "")
            {
                double w = EscreveM(dc, T("Último jogo") + "  ", c.X + 14, y4, 11.5, MFraco);
                IBrush cr = ult.StartsWith(T("V") + " ") ? MVerde : ult.StartsWith(T("D") + " ") ? MVermelho : MTexto;
                EscreveM(dc, ult, c.X + 14 + w, y4, 11.5, cr, true);
            }
        }

        // ------------------------------------------------------------ abas
        void AbasModernas(DrawingContext dc)
        {
            var c = _mAbas;
            dc.DrawRectangle(MCartao, MBorda, c.Deflate(0.5), 15, 15);
            var nomes = (_patrocinio ? AbasPatrocinio : AbasOriginais).Select(T).ToArray();
            double w = (c.Width - 6) / nomes.Length;
            double tam = 12;
            foreach (var n in nomes) while (tam > 9 && LarguraM(n, tam, true) > w - 8) tam -= 0.5;
            for (int i = 0; i < nomes.Length; i++)
            {
                var r = new Rect(c.X + 3 + i * w, c.Y + 3, w, c.Height - 6);
                bool sel = _pagina == i;
                // a aba escolhida: a pilula amarela (como o Sim do hub)
                if (sel) dc.DrawRectangle(TemaVerde.BotaoPrimario, null, r, 12, 12);
                CentroM(dc, nomes[i], r.Center.X, r.Center.Y, tam, sel ? MEscuro : MFraco, sel);
                int pg = i;
                _alvos.Add((r, () => _pagina = pg));
            }
        }

        // ------------------------------------------------------------ pagina Jogo
        void JogoPaginaModerna(DrawingContext dc, Rect pg)
        {
            // a tabela das duas equipas (V, E, D, gols, pontos) como a ListBoxClassification
            double y = pg.Y + 8;
            string[] tit = { "V", "E", "D", "Gols", "Pts" };
            double[] xs = { pg.Right - 140, pg.Right - 116, pg.Right - 92, pg.Right - 40, pg.Right - 2 };
            EscreveM(dc, T("Campanha"), pg.X, y, 11, MFraco);
            for (int k = 0; k < 5; k++) DireitaM(dc, T(tit[k]), xs[k], y, 11, MFraco);
            y += 14;
            foreach (var eq in new[] { _e, _adv })
            {
                if (eq == null) continue;
                y += 14;
                LogoM(dc, eq, new Rect(pg.X, y - 9, 18, 18));
                EscreveM(dc, Texto(eq.NomeCurto), pg.X + 26, y, 13, MTexto, eq == _e, xs[0] - 30 - pg.X - 26);
                short W(int off) => Campos.EqW(eq, off);
                DireitaM(dc, W(Campos.EqVitorias).ToString(), xs[0], y, 13, MTexto);
                DireitaM(dc, W(Campos.EqEmpates).ToString(), xs[1], y, 13, MTexto);
                DireitaM(dc, W(Campos.EqDerrotas).ToString(), xs[2], y, 13, MTexto);
                DireitaM(dc, $"{W(Campos.EqGolosPro)}:{W(Campos.EqGolosContra)}", xs[3], y, 13, MTexto);
                DireitaM(dc, W(Campos.EqPontos).ToString(), xs[4], y, 13, MTexto, true);
                y += 14;
            }
            y += 14;
            dc.FillRectangle(MLinha, new Rect(pg.X, y, pg.Width, 1));
            y += 24;
            EscreveM(dc, T("Dinheiro em caixa"), pg.X, y, 11, MFraco);
            EscreveM(dc, Dinheiro((int)_e.Dinheiro), pg.X, y + 22, 18, (int)_e.Dinheiro < 0 ? MVermelho : MTexto, true, pg.Width);
            y += 62;
            EscreveM(dc, T("Moral"), pg.X, y, 11, MFraco);
            MoralM(dc, new Rect(pg.X, y + 14, pg.Width, 10), _e);
        }

        // ------------------------------------------------------------ pagina Jogador
        void JogadorModerno(DrawingContext dc, Rect pg)
        {
            var p = _sel;
            if (p == null)
            {
                CentroM(dc, T("Escolha um jogador na lista para ver a ficha dele."), pg.Center.X, pg.Center.Y, 13, MFraco, max: pg.Width);
                return;
            }
            int pos = Math.Min((int)p.Posicao, 3);
            var rp = new Rect(pg.X, pg.Y + 2, 30, 30);
            dc.DrawRectangle(MCartao2, null, rp, 8, 8);
            CentroM(dc, Posicao(pos), rp.Center.X, rp.Center.Y, 15, MCorPosicao[pos], true);
            double xt = pg.X + 40;
            double xd = pg.Right;
            // embaixo, a direita: Vender (o atalho do Jogador > Vender, tecla V) e, se pode,
            // Renovar contrato
            const double hBotoes = 34;
            {
                double yb = pg.Bottom - hBotoes / 2 + 2, xb = pg.Right;
                if (MercadoJogo.PodeMudarSalario(p))
                {
                    double w = LarguraM(T("Renovar contrato..."), 11.5, true) + 28;
                    xb = BotaoM(dc, xb - w, yb, T("Renovar contrato..."), false, true, Renovar, 11.5).X - 8;
                }
                double wv = LarguraM(T("Vender"), 11.5, true) + 28;
                BotaoM(dc, xb - wv, yb, T("Vender"), false, true, Vender, 11.5);
            }
            // a Forca em destaque, num selo ao lado do nome
            {
                double wf = Math.Max(54, LarguraM(p.Forca.ToString(), 18, true) + 16);
                var rf = new Rect(xd - wf - 4, pg.Y + 1, wf, 38);
                dc.DrawRectangle(MSel, new Pen(MAzul, 1.2), rf, 9, 9);
                CentroM(dc, T("Força").ToUpperInvariant(), rf.Center.X, rf.Y + 9, 8.5, MFraco, true);
                CentroM(dc, p.Forca.ToString(), rf.Center.X, rf.Y + 25, 18, MTexto, true);
                xd = rf.X - 8;
            }
            EscreveM(dc, NomeJogador(p), xt, pg.Y + 11, 16, MTexto, true, xd - xt);
            string pais = PaisDe(p.Pais);
            BandeiraM(dc, pais, new Rect(xt, pg.Y + 24, 13, 13));
            string sub = NomePais(pais) + "  ·  " + T(MNomePosicao[pos]);
            EscreveM(dc, sub, xt + 19, pg.Y + 31, 11.5, MFraco, max: pg.Right - xt - 19);
            double y = pg.Y + 46;
            if (p.B35 == 1)
            {
                EscreveM(dc, T("Não é considerado jogador estrangeiro, ao abrigo da Lei Bosman"), pg.X, y + 2, 10.5, MFraco, max: pg.Width);
                y += 14;
            }
            // a escalacao e o estado
            double xc = pg.X;
            void Estado(string s, IBrush cor)
            {
                var t = TM(s, 11, cor, true);
                var r = new Rect(xc, y, t.WidthIncludingTrailingWhitespace + 14, 18);
                dc.DrawRectangle(MCartao2, null, r, 9, 9);
                dc.DrawText(t, new Point(r.X + 7, r.Center.Y - t.Height / 2));
                xc = r.Right + 6;
            }
            switch (Mem.Escalacao(p))
            {
                case Mem.EscTitular: Estado(T("Titular"), MVerde); break;
                case Mem.EscReserva: Estado(T("Reserva"), MAmarelo); break;
                default: Estado(T("Fora da escalação"), MFraco); break;
            }
            // os mesmos selos da lista, com o texto ao lado (2 amarelos: "pendurado")
            foreach (var (tipo, n) in Selos(p, _launcher))
            {
                string txt = tipo == SeloAmarelo && n >= 2 ? T("Pendurado") : DicaSelo(tipo, n).Split('\n')[0];
                var t = TM(txt, 11, tipo == SeloVermelho ? MLaranja : tipo == SeloAmarelo ? MAmarelo : MVermelho, true);
                var r = new Rect(xc, y, t.WidthIncludingTrailingWhitespace + 14 + 24, 18);
                // sem lugar na linha: o selo desce para a seguinte (antes era descartado, e no
                // 4:3 a lesao sumia depois de "Fora da escalacao" e "Pendurado")
                if (r.Right > pg.Right && xc > pg.X) { y += 22; xc = pg.X; r = new Rect(xc, y, r.Width, 18); }
                dc.DrawRectangle(MCartao2, null, r, 9, 9);
                var rs = Selo(dc, r.X + 6, r.Center.Y, tipo, n);
                dc.DrawText(t, new Point(r.X + 30, r.Center.Y - t.Height / 2));
                _dicas.Add((r, DicaSelo(tipo, n)));
                xc = r.Right + 6;
            }
            y += 28;

            string comp = p.Comportamento < Comportamentos.Length ? T(Comportamentos[p.Comportamento]) : p.Comportamento.ToString();
            // a Forca vai no selo do topo; os gols da temporada abrem a tabela
            var ficha = new List<(string, string)> { (_largo ? "Gols na temporada" : "Gols na temp.", Mem.Golos14(p).ToString()),
                ("Salário", Dinheiro(p.Salario)), ("Comportamento", comp) };
            if (_launcher) { ficha.Add(("Nota", p.Nota.ToString())); ficha.Add(("Risco de lesão", p.Lesao.ToString())); }
            // o Historial do original como uma tabela: rotulo a esquerda, numero a direita
            var hist = new List<(string, string)> { ("Jogos", Hist(p, Mem.HistJogos).ToString()), ("Gols", Hist(p, Mem.HistGolos).ToString()) };
            if (_launcher) hist.Add(("Cartões amarelos", Amarelos(p).ToString()));
            hist.Add(("Cartões vermelhos", Hist(p, Mem.HistVermelhos).ToString()));
            hist.Add(("Lesões", Hist(p, Mem.HistLesoes).ToString()));
            // Ficha e Historico como duas tabelas lado a lado (rotulo a esquerda, numero a
            // direita, como no original); estreito, uma embaixo da outra
            bool lado = pg.Width >= 300;
            if (lado)
            {
                int n = Math.Max(ficha.Count, hist.Count);
                double lh = Math.Clamp((pg.Bottom - hBotoes - y - 18 - 8) / n, 18, 24);
                double w = (pg.Width - 10) / 2;
                Tabela(dc, new Rect(pg.X, y, w, 0), "Ficha", ficha, lh);
                Tabela(dc, new Rect(pg.X + w + 10, y, w, 0), "Histórico", hist, lh);
            }
            else
            {
                double lh = Math.Clamp((pg.Bottom - hBotoes - y - 2 * 18 - 12) / (ficha.Count + hist.Count), 18, 24);
                y = Tabela(dc, new Rect(pg.X, y, pg.Width, 0), "Ficha", ficha, lh);
                Tabela(dc, new Rect(pg.X, y + 6, pg.Width, 0), "Histórico", hist, lh);
            }
        }

        // Ficha e Historial: titulo da secao e uma tabela num cartao, linhas alternadas, o rotulo a
        // esquerda e o numero a direita (como o GroupBox do original)
        static double Tabela(DrawingContext dc, Rect pg, string titulo, List<(string R, string V)> itens, double linhaH)
        {
            double y = pg.Y;
            EscreveM(dc, T(titulo).ToUpperInvariant(), pg.X, y + 7, 10.5, MFraco, true);
            y += 18;
            var c = new Rect(pg.X, y, pg.Width, itens.Count * linhaH + 2);
            dc.DrawRectangle(MCartao2, new Pen(MLinha, 1), c, 8, 8);
            using (dc.PushClip(new RoundedRect(c.Deflate(1), 7)))
                for (int i = 0; i < itens.Count; i++)
                {
                    var r = new Rect(c.X + 1, c.Y + 1 + i * linhaH, c.Width - 2, linhaH);
                    if (i % 2 == 1) dc.FillRectangle(MZebra, r);
                    if (i > 0) dc.FillRectangle(MLinha, new Rect(r.X, r.Y, r.Width, 1));
                    var tv = TM(itens[i].V, 13, MTexto, true);
                    double wv = tv.WidthIncludingTrailingWhitespace;
                    EscreveM(dc, T(itens[i].R), r.X + 10, r.Center.Y, 11.5, MFraco, max: r.Width - wv - 26);
                    dc.DrawText(tv, new Point(r.Right - 10 - wv, r.Center.Y - tv.Height / 2));
                }
            return c.Bottom;
        }

        // os numeros em blocos de 3 por linha, com o titulo da secao em cima
        static double Blocos(DrawingContext dc, Rect pg, double y, string titulo, List<(string R, string V)> itens, double alto)
        {
            EscreveM(dc, T(titulo).ToUpperInvariant(), pg.X, y + 7, 10.5, MFraco, true);
            y += 18;
            for (int i = 0; i < itens.Count; i++)
            {
                // a ultima linha incompleta divide a largura toda entre os que sobram
                int naLinha = Math.Min(3, itens.Count - i / 3 * 3);
                double w = (pg.Width - 6 * (naLinha - 1)) / naLinha;
                var r = new Rect(pg.X + (i % 3) * (w + 6), y + (i / 3) * alto, w, alto - 5);
                dc.DrawRectangle(MCartao2, null, r, 7, 7);
                if (alto >= 40)
                {
                    EscreveM(dc, T(itens[i].R), r.X + 9, r.Y + r.Height * 0.32, 10, MFraco, max: r.Width - 12);
                    EscreveM(dc, itens[i].V, r.X + 9, r.Y + r.Height * 0.70, 13.5, MTexto, true, r.Width - 12);
                }
                else
                {
                    // bloco baixo: o rotulo a esquerda e o numero a direita, na mesma linha
                    var tv = TM(itens[i].V, 13, MTexto, true);
                    double wv = tv.WidthIncludingTrailingWhitespace;
                    EscreveM(dc, T(itens[i].R), r.X + 9, r.Center.Y, 10.5, MFraco, max: r.Width - wv - 24);
                    dc.DrawText(tv, new Point(r.Right - 9 - wv, r.Center.Y - tv.Height / 2));
                }
            }
            return y + Math.Ceiling(itens.Count / 3.0) * alto;
        }

        // ------------------------------------------------------------ pagina Financas
        void FinancasModernas(DrawingContext dc, Rect pg)
        {
            var b = MercadoJogo.Balanco(_e);
            long receita = (long)b[0] + b[1] + b[2], gasto = (long)b[3] + b[4] + b[5] + b[6], saldo = b.Sum(x => (long)x);
            // (rotulo, valor, 0 = normal, 1 = titulo, 2 = total, 3 = espaco)
            var bal = new List<(string R, string V, int K)>
            {
                (T("Temporada {0}", _s.J.M.Ano), "", 1),
                ("Bilheteria", Dinheiro(b[0]), 0), ("Jogadores vendidos", Dinheiro(b[1]), 0), ("Prêmios", Dinheiro(b[2]), 0),
                ("Salários", Dinheiro(b[4]), 0), ("Jogadores comprados", Dinheiro(b[3]), 0),
                ("Arquibancadas", Dinheiro(b[5]), 0), ("Juros", Dinheiro(b[6]), 0),
                ("Receita total", Dinheiro(receita), 2), ("Gasto total", Dinheiro(gasto), 2), ("Saldo", Dinheiro(saldo), 2),
            };
            var res = new List<(string R, string V, int K)>
            {
                ("Total de salários", Dinheiro(MercadoJogo.FolhaSalarial(_e)), 0),
                ("Juros do empréstimo", Dinheiro(MercadoJogo.Juros((int)_e.D6D)), 0),
                ("Dinheiro em caixa", Dinheiro((int)_e.Dinheiro), 2),
            };
            // (no launcher o preco e o campo com as setas, e a previsao ocupa mais 2 linhas)
            if (_jogo != null && !_launcher) res.Add(("Preço do ingresso", Dinheiro(Ext.Bytes(_jogo.B36.AsSpan(0, 10)).Round()), 0));
            bool duas = _largo;
            int extra = _launcher ? (_jogo != null ? (duas ? 5 : 3) : 1) : 0;
            int n = duas ? Math.Max(bal.Count, res.Count + extra + 1) : bal.Count + 1 + res.Count + extra;
            double passo = Math.Min(22, (pg.Height - 4) / n);
            double w = duas ? (pg.Width - 24) / 2 : pg.Width;
            double y = pg.Y + passo / 2;
            void Linhas(List<(string R, string V, int K)> l, double x)
            {
                foreach (var (r, v, k) in l)
                {
                    IBrush cor = k == 0 ? MFraco : MTexto;
                    if (k == 2 && r == "Saldo") cor = saldo < 0 ? MVermelho : MVerde;
                    bool emprestimo = r == "Juros do empréstimo" && Registo.AddIn(0x13);
                    double wv = v == "" ? 0 : LarguraM(v, 12.5, k != 0) + 10;
                    EscreveM(dc, T(r), x, y, k == 1 ? 12.5 : 12, emprestimo ? MAzul : k == 1 ? MTexto : MFraco, k != 0, w - wv);
                    if (v != "") DireitaM(dc, v, x + w, y, 12.5, cor, k != 0);
                    // o juro do emprestimo e o rotulo dele (4:3e45, 4:3e27), com o add-in 0x13
                    if (emprestimo) _alvos.Add((new Rect(x, y - passo / 2, w, passo), () => TelaEmprestimo.Abrir(_s, _e, Voltar)));
                    if (k == 2 && r == "Receita total") dc.FillRectangle(MLinha, new Rect(x, y - passo / 2, w, 1));
                    y += passo;
                }
            }
            Linhas(bal, pg.X);
            if (duas) y = pg.Y + passo / 2 + passo;
            else { dc.FillRectangle(MLinha, new Rect(pg.X, y - passo / 2 + passo / 2, pg.Width, 1)); y += passo; }
            double xr = duas ? pg.X + w + 24 : pg.X;
            Linhas(res, xr);
            // launcher (ingressos.py): o % do preco de tabela nos jogos em casa
            if (_launcher)
            {
                EscreveM(dc, T("Preço do ingresso"), xr, y, 12, MFraco, max: duas ? w : w - 130);
                // (16:9: a coluna e estreita; o campo na linha de baixo)
                if (duas) y += passo;
                double xf = xr + w;
                var mais = new Rect(xf - 22, y - 11, 22, 22);
                var menos = new Rect(xf - 22 - 76 - 22, y - 11, 22, 22);
                foreach (var (r, s, d) in new[] { (menos, "−", -1), (mais, "+", 1) })
                {
                    dc.DrawEllipse(MCartao2, null, r.Center, 11, 11);
                    CentroM(dc, s, r.Center.X, r.Center.Y, 14, MTexto, true);
                    int dd = d;
                    _alvos.Add((r, () => MudarIngressos(dd)));
                }
                // o preco direto (as setas mudam o % da tabela) e a previsao do proximo em casa
                int pct = Ingressos.Percentual(_e);
                string pr = _jogo != null ? Dinheiro(IngressosPrevisao.PrecoCom(Ext.Bytes(_jogo.B36.AsSpan(0, 10)), pct)) : pct + "%";
                CentroM(dc, pr, (menos.Right + mais.X) / 2, y, 13, MTexto, true);
                _mIngressos = new Rect(menos.X, y - 11, mais.Right - menos.X, 22);
                if (_jogo != null)
                {
                    var ls = LinhasPrevisao3(pct);
                    if (!duas) ls = new[] { ls[0] + ": " + ls[1], ls[2] };
                    for (int i = 0; i < ls.Length; i++)
                        if (ls[i].Length > 0) EscreveM(dc, ls[i], xr, y + (i + 1) * passo, 11.5, MFraco, max: w);
                }
            }
        }

        // ------------------------------------------------------------ pagina Escalacao
        void EscalacaoModerna(DrawingContext dc, Rect pg)
        {
            double hb = 40;
            // os titulares, os reservas e o aviso da escalacao incompleta ficam no campinho
            var campo = new Rect(pg.X, pg.Y, pg.Width, pg.Height - hb - 10);
            using (dc.PushClip(new RoundedRect(campo, 8)))
                CampinhoIsometrico(dc, new Rect(Math.Round(campo.X), Math.Round(campo.Y), Math.Round(campo.Width), Math.Round(campo.Height)));
            double yb = pg.Bottom - hb / 2;
            var ra = BotaoBrilho(dc, pg.X, yb, "automatico", T("Automático"), false, true, () => { Esc.Automatico(_s.J.R, _e); DepoisDeEscalar(); }, 12);
            BotaoBrilho(dc, ra.Right + 8, yb, "melhores", T("Melhores"), false, true, () => { Esc.Melhores(_s.J.R, _e); DepoisDeEscalar(); _jogarForcado = true; }, 12);
            var tj = TM(T("Jogar"), 14, MEscuro, true);
            double wj = Math.Max(_largo ? 120 : 96, tj.WidthIncludingTrailingWhitespace + 30);
            BotaoBrilho(dc, pg.Right - wj, yb, "jogar-esc", T("Jogar"), true, JogarHabilitado(), Jogar, 14, wj);
        }

        // ------------------------------------------------------------ pagina Adversario
        void AdversarioModerno(DrawingContext dc, Rect pg)
        {
            if (_adv == null)
            {
                CentroM(dc, T("Sem adversário nesta rodada"), pg.Center.X, pg.Center.Y, 13, MFraco);
                return;
            }
            // a faixa nas cores do adversario
            var fx = new Rect(pg.X, pg.Y, pg.Width, 52);
            dc.DrawRectangle(_advFundo, null, fx, 8, 8);
            LogoM(dc, _adv, new Rect(fx.X + 10, fx.Y + 8, 36, 36));
            EscreveM(dc, Texto(_adv.NomeCurto), fx.X + 56, fx.Center.Y, 17, _advLetra, true, fx.Width - 66);
            double y = fx.Bottom + 24;
            EscreveM(dc, T("Moral"), pg.X, y, 11, MFraco);
            MoralM(dc, new Rect(pg.X, y + 14, pg.Width, 10), _adv);
            y += 52;
            EscreveM(dc, T("Técnico"), pg.X, y, 11, MFraco);
            EscreveM(dc, _s.TreinadorDa(_adv) is { } t ? Texto(t.Nome) : "", pg.X, y + 20, 15, MTexto, true, pg.Width);
            y += 46;
            string pais = PaisDe(_adv.Pais);
            int dv = _s.J.M.DivisaoDa(_adv);
            string div = dv < 0 ? "" : dv >= 4 ? T("Distrital")
                : LugarNaDivisao(_s.J.M.S.Divisoes[dv].Equipas.IndexOf(_adv.ID) + 1, Pascal(_s.J.M.S.Divisoes[dv].Nome, 20));
            BandeiraM(dc, pais, new Rect(pg.X, y - 7, 14, 14));
            EscreveM(dc, NomePais(pais) + (div != "" ? "  ·  " + div : ""), pg.X + 20, y, 12, MFraco, max: pg.Width - 20);
            // 4:3774 e o calendario: o plantel do adversario so com a compra directa (add-in 0x10)
            double yb = pg.Bottom - 20;
            var adv = _adv;
            var rc = BotaoM(dc, pg.X, yb, T("Calendário"), false, true, () => Campeonato.Calendario(_s, adv, Voltar));
            if (Registo.AddIn(0x10)) BotaoM(dc, rc.Right + 8, yb, T("Elenco"), false, true, () => TelaPlantel.Abrir(_s, adv, _e, 0, Voltar));
        }

        // ------------------------------------------------------------ pagina Patrocinio
        void PatrocinioModerno(DrawingContext dc, Rect pg)
        {
            // duas secoes separadas por uma linha: o master (logo grande, nome, valor e
            // temporadas) e o pontual (titulo proprio; logo pequeno a esquerda e o nome, o valor
            // por jogo e os jogos a direita). Antes o logo do pontual ficava logo abaixo das
            // linhas do master e parecia ser dele.
            var marcas = _s.J.Op.Marcas;
            double xc = pg.Center.X;
            void Placa(Bitmap img, Rect r, double raio)
            {
                dc.DrawRectangle(Brushes.White, null, r, raio, raio);
                var sz = img.Size;
                double e = Math.Min(1, Math.Min((r.Width - 12) / sz.Width, (r.Height - 8) / sz.Height));
                dc.DrawImage(img, new Rect(r.Center.X - sz.Width * e / 2, r.Center.Y - sz.Height * e / 2, sz.Width * e, sz.Height * e));
            }
            // a altura do bloco do pontual (titulo + linha) e o que sobra fica para o master
            const double AltPontual = 96;
            double divisoria = pg.Bottom - AltPontual;

            CentroM(dc, T("Patrocinador master"), xc, pg.Y + 16, 13, MFraco, true);
            var mc = _e.MarcaMaster() != 0 ? marcas?.Registro(_e.MarcaMaster()) : null;
            if (mc != null)
            {
                // o logo do master e sempre maior que o do pontual (36 de altura): ocupa o que as
                // tres linhas deixam, de 48 a 80
                double hl = Math.Clamp(divisoria - (pg.Y + 32) - 74, 48, 80);
                if (TelaPatrocinio.Logo(marcas!.CaminhoLogo(mc)) is { } img)
                    Placa(img, new Rect(xc - hl * 1.1, pg.Y + 30, 2.2 * hl, hl), 8);
                double y = pg.Y + 30 + hl + 16;
                CentroM(dc, mc.Nome, xc, y, 17, MTexto, true, pg.Width);
                CentroM(dc, T("{0} por temporada", Dinheiro(_e.ValorMaster())), xc, y + 22, 12, MFraco, max: pg.Width);
                int n = _e.EpocasMaster();
                CentroM(dc, T(n == 1 ? "{0} temporada restante" : "{0} temporadas restantes", (char)('0' + n)), xc, y + 39, 12, MFraco, max: pg.Width);
            }
            else CentroM(dc, T("Sem patrocinador"), xc, (pg.Y + divisoria) / 2, 18, MTexto, true);

            // a divisoria e a secao do pontual
            dc.FillRectangle(MLinha, new Rect(pg.X, divisoria, pg.Width, 1));
            CentroM(dc, T("Patrocínio pontual"), xc, divisoria + 18, 13, MFraco, true);
            var mp = _e.JogosPontual() != 0 ? marcas?.Registro(_e.MarcaPontual()) : null;
            double yl = divisoria + 34, hp = 36;
            if (mp != null)
            {
                int j = _e.JogosPontual();
                var img = TelaPatrocinio.Logo(marcas!.CaminhoLogo(mp));
                string l1 = mp.Nome;
                string l2 = T("{0} por jogo", Dinheiro(_e.ValorPontual()));
                string l3 = T(j == 1 ? "{0} jogo restante" : "{0} jogos restantes", (char)('0' + j));
                // o logo e o texto centrados juntos na largura da pagina
                double wl = img != null ? 2 * hp + 14 : 0;
                double wt = Math.Max(LarguraM(l1, 15, true), Math.Max(LarguraM(l2, 12), LarguraM(l3, 12)));
                double x0 = Math.Max(pg.X, xc - (wl + wt) / 2);
                // o logo menor, no meio da altura das tres linhas
                if (img != null) Placa(img, new Rect(x0, yl + 8, 2 * hp, hp), 6);
                double xt = x0 + wl, maxT = pg.Right - xt;
                EscreveM(dc, l1, xt, yl + 9, 15, MTexto, true, maxT);
                EscreveM(dc, l2, xt, yl + 28, 12, MFraco, max: maxT);
                EscreveM(dc, l3, xt, yl + 45, 12, MFraco, max: maxT);
            }
            else CentroM(dc, T("Nenhum no momento"), xc, yl + hp / 2, 12.5, MFraco);
        }

        // ------------------------------------------------------------ Ajuda do Arbitro
        // a mesma janela (a balanca e, com as regras do launcher, os dois times e as barras),
        // num cartao escuro no meio da tela
        void ArbitroModerno(DrawingContext dc)
        {
            if (_jogo == null) return;
            dc.FillRectangle(TemaVerde.Veu, new Rect(0, 0, L, A));
            double w = 440, h = ArbitroCompleto ? 420 : 250;
            var c = new Rect(Math.Floor((L - w) / 2), Math.Floor((A - h) / 2), w, h);
            dc.DrawRectangle(MCartao, new Pen(TemaVerde.Ouro, 1.2), c, 12, 12);
            var arb = _s.S.Arbitros.FindLast(a => a.ID == _jogo.Arbitro);
            EscreveM(dc, arb != null ? Texto(arb.Nome) : T("Tendência do Árbitro"), c.X + 18, c.Y + 24, 15, MTexto, true, w - 36);
            if (arb != null) EscreveM(dc, NomePais(PaisDe(arb.Pais)), c.X + 18, c.Y + 44, 11.5, MFraco);
            var casa = _s.Equipa(_jogo.Casa);
            var fora = _s.Equipa(_jogo.Fora);
            int vc = _jogo.W4C, vf = _jogo.W4E;
            // a balanca do original (FormPaint, seg06:2b3a) na cor do texto, centrada
            using (dc.PushTransform(Matrix.CreateTranslation(c.X + w / 2 - 165, c.Y + 10)))
                BalancaM(dc, (short)(vc - vf), casa, fora);
            _mFecharArbitro = BotaoM(dc, c.Right - 18 - 90, c.Bottom - 26, T("Fechar"), true, true, null, 13, 90);
            if (!ArbitroCompleto) return;
            double y = c.Y + 236;
            foreach (var (eq, v, pen) in new[] { (casa, vc, PenaltiCasa), (fora, vf, PenaltiFora) })
            {
                if (eq == null) continue;
                LogoM(dc, eq, new Rect(c.X + 18, y - 9, 18, 18));
                EscreveM(dc, Texto(eq.NomeCurto), c.X + 44, y, 13, MTexto, true, 170);
                int k = Math.Min(v, 15);
                if (_launcher) DireitaM(dc, T("Pênalti: {0}%   Cartões: -{1}%", pen[k], 2 * (k + 1)), c.Right
 - 18, y, 11.5, MFraco);
                var r = new Rect(c.X + 18, y + 16, w - 36, 8);
                dc.DrawRectangle(MLinha, null, r, 4, 4);
                if (v > 0) dc.DrawRectangle(MAzul, null, new Rect(r.X, r.Y, Math.Max(8, r.Width * Math.Clamp(v / 15.0, 0, 1)), 8), 4, 4);
                y += 56;
            }
        }

        static void BalancaM(DrawingContext dc, short d, SEquipa? casa, SEquipa? fora)
        {
            var pen = new Pen(MTexto, 3, lineCap: PenLineCap.Round);
            void Lin(double x0, double y0, double x1, double y1) => dc.DrawLine(pen, new Point(x0, y0), new Point(x1, y1));
            void Prato(double x, double y, SEquipa? eq)
            {
                Lin(x, y, x, y + 30);
                Lin(x, y + 30, x - 25, y + 60);
                Lin(x - 25, y + 60, x + 25, y + 60);
                Lin(x + 25, y + 60, x, y + 30);
                string n = eq == null ? "" : Texto(eq.NomeCurto);
                if (n.Length > 20) n = n[..20];
                CentroM(dc, n, x, y + 30 + 52, 12, MTexto, true);
            }
            Lin(140, 190, 190, 190);
            Lin(165, 190, 165, 85);
            Prato(65, 85 + 3 * d, casa);
            Lin(65, 85 + 3 * d, 265, 85 - 3 * d);
            Prato(265, 85 - 3 * d, fora);
        }

        // ------------------------------------------------------------ cliques
        void CliqueModerno(Point pc)
        {
            for (int i = _alvos.Count - 1; i >= 0; i--)
                if (_alvos[i].R.Contains(pc))
                {
                    MarcarPremido(pc);
                    _alvos[i].A();
                    InvalidateVisual();
                    return;
                }
        }

        // o jogador sob o ponteiro (o realce da linha) e a mao nos alvos
        void SobreModerno(Point pc)
        {
            var s = RItens.Contains(pc) ? JogadorEm(pc.Y) : null;
            bool mao = s != null || _alvos.Any(a => a.R.Contains(pc));
            Cursor = mao ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            PonteiroModerno(pc);
            AtualizarDica(pc);
            if (s != _sobre) { _sobre = s; InvalidateVisual(); }
        }
    }
}

static class ExtensoesTexto
{
    public static FormattedText Com(this FormattedText t, Action<FormattedText> f) { f(t); return t; }
}
