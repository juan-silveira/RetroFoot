using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Save;
using Mem = RetroFoot.Motor.Partida.Memoria;
using T = RetroFoot.App.TextosMercado;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;

namespace RetroFoot.App;

// O resultado da venda pela pagina Vender da tela da equipa
enum ResultadoVenda { Cancelado, NaoVendido, Vendido }

// A janela do leilao (TsellPlayerDlg, seg02:3002), fiel ao DFM (576x336, amarela).
//
// Como o original anda (Timer1Timer, seg02:3649): a janela abre com a ficha do jogador e o
// painel do lance escondido; o relogio (1000 ms) da dois tiques parado e passa a 20 ms; no
// tique seguinte TODAS as equipas da CPU licitam de uma vez (nao ha animacao equipa a
// equipa) ate chegar uma equipa humana, que para o relogio e mostra o PanelHumanOffer
// (cores dessa equipa, nome do treinador, "Dinheiro em caixa: ...", a caixa do lance com
// PasswordChar 'X' se o treinador esconde as ofertas). O OK (seg02:3bda) so aceita um lance
// que caiba no dinheiro (senao fica parado); texto que nao e numero conta como 0. No fim
// (seg02:35f4), textTransferred = "Vendido ao X por Y" ou "Nao houve ofertas", e a janela
// fecha sozinha 50 tiques depois (~1 s). Nao ha como fechar a mao (FormCloseQuery).
// O original abre esta janela em TODO leilao (3:5b37), tambem nos da CPU.
//
// Aqui: Executar() faz esse ciclo numa thread que nao e a da interface (a do motor, ou uma
// tarefa quando o humano vende pela tela da equipa); Lance() e o gancho LanceHumano.
sealed class TelaLeilao : DialogoMercado
{
    // ate as licitacoes e do resultado ao fechar (ms). O original esperava 2 tiques de 1000 ms
    // antes das licitacoes (a demora de 2-3 s que se notava); aqui so o bastante para ler a ficha
    public static int EsperaInicio = 700, EsperaFim = 1000;

    const double CW = 576, CH = 336;
    static readonly IBrush Amarelo = W95.Amarelo;
    // a letra do formulario e a System (negrito); a MS Sans Serif do app desenha mal o til
    static readonly Fonte Sistema = W95.Arial13b;

    readonly Sessao s;
    readonly SEquipa v;
    readonly SJogador p;
    readonly Ficha ficha;
    readonly Canvas painel;
    readonly TextoMercado coachName, cash, label7;
    readonly CampoMercado oferta;
    readonly Botao ok;
    SEquipa? comprador;
    Control? logoComprador;
    Action<(int, bool)>? resposta;
    // no celular (Celular.cs): a folha do leilao (TelaLeilaoCelular.cs); a logica fica aqui
    readonly LeilaoCelular? cel;

    // vendedor, jogador, preco base (null = desconhecido: so o gancho do lance sem a janela
    // do leilao aberta) e o salario pretendido (Salario com o +0x79 do vendedor)
    TelaLeilao(Sessao s, SEquipa v, SJogador p, int? preco, int salario)
        : base(Idioma.T("Venda de jogador por leilão"), CW, CH, Amarelo, fechar: false)
    {
        // (Moderno: a ficha e o painel no tema escuro; as mesmas posicoes do painel do lance)
        this.s = s; this.v = v; this.p = p;
        var moeda = Moeda.DoSave(s.S);
        ficha = new Ficha(s, v, p, preco, salario, moeda, s.J.M.Regras.CartoesAmarelos);
        Por(ficha, 0, 0, CW, CH);

        // PanelHumanOffer (12, 252, 561x73), escondido ate um humano licitar
        painel = Por(new Canvas { IsVisible = false }, 12, 252, 561, 73);
        if (Moderno)
            painel.Children.Add(new Border
            {
                Width = 552, Height = 73, Background = MM.Barra, BorderBrush = MM.Azul, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
            });
        coachName = PorNoPainel(new TextoMercado { Fonte = W95.Arial13b }, 8, 8, 137, 16);
        // Label7: "Oferta (milhares de" + sufixo da moeda + ")" (2:3092)
        label7 = PorNoPainel(new TextoMercado { Fonte = W95.Arial13, Texto = Idioma.T("Oferta (milhares de{0})", moeda.Sufixo) }, 8, 28, 170, 16);
        cash = PorNoPainel(new TextoMercado { Fonte = W95.Arial13 }, 8, 54, 400, 16);
        oferta = PorNoPainel(new CampoMercado { Fonte = W95.Arial13b, MaxLen = 5 }, 176, 24, 89, 24);
        ok = PorNoPainel(BotaoGlifo(Idioma.T("OK"), "leilao-ok", W95.Arial13), 400, 5, 64, 64);
        if (Moderno)
        {
            Canvas.SetLeft(oferta, 196); Canvas.SetTop(oferta, 22); oferta.Width = 110; oferta.Height = 28;
            label7.Width = 186;
            MM.Cobrir(ok, painel, padrao: true);
        }
        ok.Clique += Ok;
        AoEnter = () => { if (painel.IsVisible) Ok(); };
        if (Celular.Ativo)
        {
            // a mesma janela, desenhada pela folha: o texto do lance vai para a caixa daqui
            cel = new LeilaoCelular(s, v, p, preco, salario, s.J.M.Regras.CartoesAmarelos);
            cel.Mudou += t => oferta.Texto = t;
            cel.Licitou += () => { Ok(); if (painel.IsVisible) cel.Excedeu(); };
            Content = cel;
            Width = Height = double.NaN;
        }
    }

    C PorNoPainel<C>(C c, double x, double y, double w, double h) where C : Control
    {
        Canvas.SetLeft(c, x); Canvas.SetTop(c, y);
        c.Width = w; c.Height = h;
        painel.Children.Add(c);
        return c;
    }

    // ------------------------------------------------------------ o lance humano
    // Licitante (2:3787) para uma equipa humana: o painel nas cores dela
    void MostrarPainel(SEquipa c, Action<(int, bool)> r)
    {
        comprador = c;
        resposta = r;
        var t = s.J.M.TreinadorDa(c);
        var (letra, fundo) = T.Cores(c);
        if (!Moderno) painel.Background = fundo;
        foreach (var x in new[] { coachName, label7, cash }) { x.Cor = letra; x.InvalidateVisual(); }
        coachName.Texto = T.NomeTreinador(t);
        cash.Texto = Idioma.T("Dinheiro em caixa: ") + FormatoDinheiro.Valor(s.S, (int)c.Dinheiro);
        // +0x1E do treinador (B1A[4]): esconder as ofertas -> PasswordChar 'X'
        oferta.Senha = t != null && t.B1A[4] != 0 ? 'X' : '\0';
        oferta.Texto = "";
        // o logo do comprador (se houver) a direita do OK
        if (logoComprador != null) painel.Children.Remove(logoComprador);
        logoComprador = Escudos.Controle(c, 56);
        if (logoComprador != null) PorNoPainel(logoComprador, 488, 8, 56, 56);
        painel.IsVisible = true;
        Foco = oferta;
        if (cel != null)
        {
            // (no celular a caixa nao pega o foco sozinha: o teclado cobria a folha)
            cel.MostrarPainel(c, coachName.Texto, cash.Texto, oferta.Senha);
            Foco = null;
            return;
        }
        Focar();
    }

    // OKBtnClick (2:3bda): Val; texto invalido conta 0; lance x 1000 acima do dinheiro: nada
    void Ok()
    {
        if (!painel.IsVisible || comprador == null || resposta == null) return;
        bool valido = T.Val(oferta.Texto, out int m);
        if (!valido) m = 0;
        if ((long)m * 1000 > (int)comprador.Dinheiro) return;
        painel.IsVisible = false;
        Foco = null;
        Focus();
        cel?.EsconderPainel();
        var r = resposta;
        resposta = null; comprador = null;
        r((m, valido));
    }

    // na thread que nao e a da interface: mostra o painel e espera o OK
    (int, bool) PedirLance(SEquipa c)
    {
        var tcs = new TaskCompletionSource<(int, bool)>();
        Dispatcher.UIThread.Invoke(() => MostrarPainel(c, x => tcs.TrySetResult(x)));
        return tcs.Task.GetAwaiter().GetResult();
    }

    void MostrarResultado((string Texto, SEquipa? Comprador) r)
    {
        ficha.Transferido = r.Texto;
        ficha.Comprador = r.Comprador;
        ficha.InvalidateVisual();
        cel?.MostrarResultado(r.Texto, r.Comprador);
        // o jogador foi para um time humano: o som da compra
        if (r.Comprador != null && Mem.Humano(s.TreinadorDa(r.Comprador))) Som.Tocar(Som.Efeito.Compra);
    }

    // ------------------------------------------------------------ o ciclo do leilao
    [ThreadStatic] static TelaLeilao? aberta;
    static int mostrados;

    // A janela de um leilao: abre, espera os 2 tiques, chama leiloar(lance) (que licita e
    // vende: Mc.Leiloar), mostra o resultado e fecha. Na thread do motor ou numa tarefa,
    // nunca na da interface. Devolve o que leiloar devolveu (vendido).
    public static bool Executar(Sessao s, ushort vendedor, ushort jogador, int preco, Func<LanceHumano, bool> leiloar)
    {
        if (Dispatcher.UIThread.CheckAccess()) throw new InvalidOperationException("TelaLeilao.Executar na thread da interface");
        var v = MercadoJogo.Equipa(s.S, vendedor)!;
        var (_, k) = MercadoJogo.EquipaDoJogador(s.S, jogador);
        var p = v.Jogadores[k];
        int salario = s.Mercado.Salario(p, Ext.Bytes(v.B79));
        TelaLeilao t = null!;
        Dispatcher.UIThread.Invoke(() => { t = new TelaLeilao(s, v, p, preco / 1000 * 1000, salario); s.AbrirDialogo(t); });
        Interlocked.Increment(ref mostrados);
        try
        {
            Thread.Sleep(EsperaInicio);
            var antes = aberta;
            aberta = t;
            bool vendido;
            try { vendido = leiloar(Lance(s)); }
            finally { aberta = antes; }
            var res = Resultado(s, p, vendido);
            Dispatcher.UIThread.Invoke(() => t.MostrarResultado(res));
            Thread.Sleep(EsperaFim);
            return vendido;
        }
        finally
        {
            Dispatcher.UIThread.Invoke(() => s.FecharDialogo(t));
        }
    }

    // O gancho MercadoJogo.Janela: todo leilao do motor (dividas, salario recusado, venda da
    // CPU) dentro da janela, como o original (3:5b37). Ja dentro de uma janela (Executar da
    // venda pela tela da equipa) ou na thread da interface, so o leilao.
    internal static bool JanelaDoMotor(Sessao s, MercadoJogo.Leilao l, Func<bool> rodar)
    {
        if (aberta != null || Dispatcher.UIThread.CheckAccess()) return rodar();
        return Executar(s, l.Vendedor, l.Jogador, l.Base, _ => rodar());
    }

    // a mesma janela depois do leilao ja feito pelo motor (ProporSalario recusado: o motor
    // leiloa la dentro). Como a CPU licita toda num tique so, a vista e a mesma.
    static void MostrarDepois(Sessao s, SEquipa v, SJogador p, bool vendido)
    {
        int salario = s.Mercado.Salario(p, Ext.Bytes(v.B79));
        TelaLeilao t = null!;
        Dispatcher.UIThread.Invoke(() => { t = new TelaLeilao(s, v, p, 0, salario); s.AbrirDialogo(t); });
        try
        {
            Thread.Sleep(EsperaInicio);
            var res = Resultado(s, p, vendido);
            Dispatcher.UIThread.Invoke(() => t.MostrarResultado(res));
            Thread.Sleep(EsperaFim);
        }
        finally { Dispatcher.UIThread.Invoke(() => s.FecharDialogo(t)); }
    }

    // 2:3a10 / 2:35f4: "Vendido ao <equipa> por <dinheiro>" ou "Nao houve ofertas"; e a
    // equipa que comprou (o logo dela no Moderno)
    static (string, SEquipa?) Resultado(Sessao s, SJogador p, bool vendido)
    {
        if (!vendido) return (Idioma.T("Não houve ofertas"), null);
        var (c, _) = MercadoJogo.EquipaDoJogador(s.S, p.ID);
        var tr = s.S.Transferencias.LastOrDefault(x => x.W06 == p.ID);
        long preco = tr != null ? (int)tr.D0C : 0;
        return (Idioma.T("Vendido ao {0} por {1}", T.NomeEquipa(c), FormatoDinheiro.Valor(s.S, preco)), c);
    }

    // O gancho LanceHumano (Humano.Lance / Interacao.Lance): com a janela do leilao aberta
    // nesta thread (Executar), o painel dela; senao (leilao da CPU que o motor faz sem
    // avisar a interface) uma janela so para o lance, sem o preco base, que fecha no OK.
    public static LanceHumano Lance(Sessao s) => (c, v, p) =>
    {
        var t = aberta;
        if (t != null) return t.PedirLance(c);
        int salario = s.Mercado.Salario(p, Ext.Bytes(v.B79));
        TelaLeilao j = null!;
        Dispatcher.UIThread.Invoke(() => { j = new TelaLeilao(s, v, p, null, salario); s.AbrirDialogo(j); });
        try { return j.PedirLance(c); }
        finally { Dispatcher.UIThread.Invoke(() => s.FecharDialogo(j)); }
    };

    // ------------------------------------------------------------ a venda pelo humano
    // Jogador > Vender na tela da equipa (TprepareGameDlg.Vender1Click, 4:3d0e): com
    // impedimento (3:5a6a), o texto do motivo vai para a pagina Mensagem (mensagem; sem ela,
    // uma caixa); senao, a pagina Vender (preco de venda em milhares). O Vender (4:455a)
    // poe o jogador em leilao com esse preco base (texto que nao e numero: nada acontece;
    // vazio = 0) e, vendido ou nao, marca +0x12 do jogador (so volta a tentar no proximo
    // jogo); depois o original refaz a escalacao (SelectPlayers, se vendeu) e volta a pagina
    // Jogo. Na thread da interface, com o motor parado na tela da equipa.
    public static void Vender(Sessao s, SEquipa e, SJogador p, Action<string>? mensagem = null, Action<ResultadoVenda>? fim = null)
    {
        int motivo = MercadoJogo.ImpedimentoVenda(e, p);
        if (motivo != 0)
        {
            var txt = Idioma.T(T.Motivos[Math.Min(motivo, T.Motivos.Length - 1)]);
            if (mensagem != null) mensagem(txt);
            else AvisoMercado.Abrir(s, Texto(e), txt);
            return;
        }
        var pag = new PaginaVender(s, e, p);
        pag.Cancelou += () => { s.FecharDialogo(pag); fim?.Invoke(ResultadoVenda.Cancelado); };
        pag.Vender += preco =>
        {
            s.FecharDialogo(pag);
            // (jogo online, sem o host: o host faz o leilao e manda o jogo novo)
            if (s.Remoto is { } rm)
            {
                rm.Executar(new RetroFoot.Motor.Online.Vender(p.ID, preco * 1000), r =>
                    fim?.Invoke(r.Motivo == "vendido" ? ResultadoVenda.Vendido : r.Ok ? ResultadoVenda.NaoVendido : ResultadoVenda.Cancelado));
                return;
            }
            Task.Run(() =>
            {
                bool vendido = false;
                try
                {
                    vendido = Executar(s, e.ID, p.ID, preco * 1000, lance => s.Mercado.Leiloar(e.ID, p.ID, preco * 1000, lance));
                    p.B0C[6] = 1;          // 4:463d
                    s.J.M.Reindexar();     // o leilao embaralha a lista das equipas
                }
                catch (Exception ex) { Console.Error.WriteLine("leilao: " + ex); }
                Dispatcher.UIThread.Post(() => fim?.Invoke(vendido ? ResultadoVenda.Vendido : ResultadoVenda.NaoVendido));
            });
        };
        s.AbrirDialogo(pag);
    }

    static string Texto(SEquipa e) => T.Texto(e.NomeCompleto);

    // o numero de janelas de leilao ja abertas por Executar (para nao mostrar duas vezes)
    internal static int Mostrados => Volatile.Read(ref mostrados);
    internal static void MostrarDepoisDoMotor(Sessao s, SEquipa v, SJogador p, bool vendido) => MostrarDepois(s, v, p, vendido);

    // ================================================================== o desenho
    // a ficha (2:3002-34fd): os textos fixos, os do jogador e o resultado; o painel do
    // lance e um Canvas por cima
    sealed partial class Ficha : Control
    {
        public string Transferido = "";
        public SEquipa? Comprador;
        readonly SEquipa v;
        readonly SJogador p;
        readonly string basePreco, salario;
        readonly bool launcher;
        readonly IBrush letraV, fundoV;

        public Ficha(Sessao s, SEquipa v, SJogador p, int? preco, int sal, Moeda m, bool launcher)
        {
            this.v = v; this.p = p; this.launcher = launcher;
            basePreco = preco is int b ? FormatoDinheiro.Valor(m, b) : "";
            salario = FormatoDinheiro.Valor(m, sal);
            (letraV, fundoV) = T.Cores(v);
            IsHitTestVisible = false;
        }

        static void E(DrawingContext dc, string s, double x, double y, Fonte f, IBrush? cor = null) =>
            W95.Escreve(dc, s, f, cor ?? W95.Preto, x, y);

        static void Caixa(DrawingContext dc, string s, Rect r, Fonte f, IBrush cor, bool quebra = false, bool direita = false)
        {
            var t = W95.Txt(s, f, cor);
            if (quebra || direita)
            {
                t.MaxTextWidth = r.Width;
                if (direita) t.TextAlignment = TextAlignment.Right;
            }
            using (dc.PushClip(r)) dc.DrawText(t, r.TopLeft);
        }

        public override void Render(DrawingContext dc)
        {
            if (Visual.Moderno) { RenderModerno(dc); return; }
            var a13 = W95.Arial13; var a13b = W95.Arial13b; var a16b = W95.Arial16b; var a11 = W95.Arial11;
            E(dc, Idioma.T("Time"), 16, 24, a13);
            // textTeamName (AutoSize) nas cores do vendedor (2:34b5)
            var nome = W95.Txt(T.NomeEquipa(v), a16b, letraV);
            dc.FillRectangle(fundoV, new Rect(72, 24, Math.Ceiling(nome.WidthIncludingTrailingWhitespace), 19));
            dc.DrawText(nome, new Point(72, 24));
            // o logo do vendedor (se houver) logo depois do nome
            double xl = 72 + Math.Ceiling(nome.WidthIncludingTrailingWhitespace) + 6;
            if (Escudos.Escudo(v) is { } logo && xl + 24 <= 324)
                using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                    dc.DrawImage(logo, new Rect(xl, 21, 24, 24));
            E(dc, Idioma.T("Jogador"), 16, 52, a13);
            Caixa(dc, T.NomeJogador(p), new Rect(128, 52, 200, 16), a13b, W95.Preto);
            E(dc, Idioma.T("Posição"), 16, 80, a13);
            Caixa(dc, T.Posicao(p), new Rect(128, 80, 200, 16), a13b, W95.Preto);
            E(dc, Idioma.T("Força"), 16, 108, a13);
            Caixa(dc, p.Forca.ToString(), new Rect(128, 108, 200, 19), a16b, W95.Preto);
            E(dc, Idioma.T("Salário pretendido"), 16, 144, a13);
            Caixa(dc, salario, new Rect(128, 144, 200, 16), a13b, W95.Preto);
            E(dc, Idioma.T("Preço base"), 16, 172, a13);
            E(dc, basePreco, 128, 172, a13b);
            E(dc, Idioma.T("Nacionalidade"), 328, 8, a13);
            string pais = T.PaisDe(p.Pais);
            Caixa(dc, T.NomePais(pais), new Rect(328, 24, 144, 33), a13b, W95.Preto, quebra: true);
            T.Bandeira(dc, pais, new Rect(480, 8, 78, 58));

            // suspenso / lesionado (2:3363-348e): so com o jogo registado
            if (p.Suspensao > 0)
                Caixa(dc, p.Suspensao == 1 ? Idioma.T("Suspenso por 1 jogo") : Idioma.T("Suspenso por {0} jogos", p.Suspensao), new Rect(16, 200, 297, 16), a13b, W95.Vermelho);
            if (p.JogosLesao > 0)
                Caixa(dc, p.JogosLesao == 1 ? Idioma.T("Lesionado por 1 jogo") : Idioma.T("Lesionado por {0} jogos", p.JogosLesao), new Rect(16, 224, 297, 16), a13b, W95.Vermelho);

            // Panel1 (328, 72): Comportamento, golos e o Historial; no launcher, Nota e Risco de
            // lesao a 15 pontos (tools/divisoes/ficha.py) e os amarelos no Historial (historial.py)
            using (dc.PushTransform(Matrix.CreateTranslation(328, 72)))
            {
                double gy;
                if (launcher)
                {
                    E(dc, Idioma.T("Comportamento"), 0, 8, a11); E(dc, T.Comportamento(p), 104, 8, a11);
                    E(dc, Idioma.T("Nota"), 0, 23, a11); E(dc, p.Nota.ToString(), 104, 23, a11);
                    E(dc, Idioma.T("Risco de lesão"), 0, 38, a11); E(dc, p.Lesao.ToString(), 104, 38, a11);
                    E(dc, Idioma.T("Gols na Temporada"), 0, 53, a11); E(dc, Mem.Golos14(p).ToString(), 104, 53, a11);
                    gy = 68;
                }
                else
                {
                    E(dc, Idioma.T("Comportamento"), 0, 8, a11); E(dc, T.Comportamento(p), 88, 8, a11);
                    // "Gols na Temporada" (pt-BR) nao cabe antes de 96: o valor em 104, como no launcher
                    E(dc, Idioma.T("Gols na Temporada"), 0, 40, a11); E(dc, Mem.Golos14(p).ToString(), 104, 40, a11);
                    gy = 56;
                }
                // GroupBox2 (185x97): a moldura gravada e o titulo por cima
                var gb = new Rect(0, gy + 7, 185, 90);
                W95.Linhas(dc, gb, W95.Sombra, W95.Luz);
                W95.Linhas(dc, gb.Deflate(1), W95.Luz, W95.Sombra);
                var tit = W95.Txt(Idioma.T("Histórico"), a11, W95.Preto);
                dc.FillRectangle(Amarelo, new Rect(6, gy, tit.WidthIncludingTrailingWhitespace + 4, 14));
                dc.DrawText(tit, new Point(8, gy));
                (string, int, double)[] linhas = launcher
                    ? new[]
                    {
                        (Idioma.T("Jogos"), T.Hist(p, Mem.HistJogos), 16.0), (Idioma.T("Gols"), T.Hist(p, Mem.HistGolos), 32),
                        (Idioma.T("Cartões amarelos"), T.Amarelos(p), 48), (Idioma.T("Cartões vermelhos"), T.Hist(p, Mem.HistVermelhos), 64),
                        (Idioma.T("Lesões"), T.Hist(p, Mem.HistLesoes), 80),
                    }
                    : new[]
                    {
                        (Idioma.T("Jogos"), T.Hist(p, Mem.HistJogos), 24.0), (Idioma.T("Gols"), T.Hist(p, Mem.HistGolos), 40),
                        (Idioma.T("Cartões vermelhos"), T.Hist(p, Mem.HistVermelhos), 56), (Idioma.T("Lesões"), T.Hist(p, Mem.HistLesoes), 72),
                    };
                foreach (var (rot, val, y) in linhas)
                {
                    E(dc, rot, 8, gy + y, a11);
                    Caixa(dc, val.ToString(), new Rect(124, gy + y, 50, 14), a11, W95.Preto, direita: true);
                }
            }

            // textTransferred (16, 260, 513x49, WordWrap) na letra do formulario (System)
            if (Transferido.Length > 0) Caixa(dc, Transferido, new Rect(16, 260, 513, 49), Sistema, W95.Preto, quebra: true);
        }
    }

    // ================================================================== Moderno
    // A ficha no visual Moderno com a mesma disposicao da classica (2:3002): a esquerda Time,
    // Jogador, Posicao, Forca, Salario pretendido, Preco base e o suspenso / lesionado; a
    // direita a Nacionalidade com a bandeira, o Comportamento (Nota e Risco no launcher), os
    // golos e o Historico; embaixo o resultado, com os logos do vendedor e do comprador.
    sealed partial class Ficha
    {
        static void T_(DrawingContext dc, string s, double x, double y, double tam, IBrush cor, FontWeight peso = FontWeight.Normal,
            double largura = 0, TextAlignment alin = TextAlignment.Left, int linhas = 2)
        {
            var t = MM.Txt(s, tam, cor, peso: peso);
            if (largura > 0) { t.MaxTextWidth = largura; t.TextAlignment = alin; t.MaxLineCount = linhas; t.Trimming = TextTrimming.CharacterEllipsis; }
            dc.DrawText(t, new Point(x, y));
        }

        // o logo de uma equipa (ou o circulo nas cores dela) num quadrado
        static void LogoEquipa(DrawingContext dc, SEquipa e, Rect r)
        {
            if (Escudos.Escudo(e) is { } logo)
            {
                if (Escudos.PrecisaDisco(logo)) dc.DrawEllipse(TemaVerde.DiscoLogo, null, r.Center, r.Width / 2 + 2, r.Height / 2 + 2);
                MM.Glifo(dc, logo, new Rect(logo.Size), r, true);
                return;
            }
            var (letra, fundo) = T.Cores(e);
            dc.DrawEllipse(fundo, new Pen(letra, 2), r.Center, r.Width / 2 - 1, r.Height / 2 - 1);
        }

        void RenderModerno(DrawingContext dc)
        {
            // a coluna da esquerda: rotulo e valor nas linhas da classica
            var ce = new Rect(8, 8, 314, 240);
            MM.Cartao_(dc, ce, MM.Barra, 10, MM.Linha);
            void Linha(string rot, double y) => T_(dc, rot, 18, y + 1, 12, MM.Fraco, FontWeight.Medium, 116, linhas: 1);
            // Time: o nome nas cores do vendedor e o logo
            Linha(Idioma.T("Time"), 24);
            var nome = MM.Txt(T.NomeEquipa(v), 15, letraV, peso: FontWeight.Bold);
            double wn = Math.Min(nome.WidthIncludingTrailingWhitespace, 130);
            nome.MaxTextWidth = wn + 1; nome.MaxLineCount = 1; nome.Trimming = TextTrimming.CharacterEllipsis;
            var rn = new Rect(136, 20, wn + 14, 24);
            dc.DrawRectangle(fundoV, null, rn, 6, 6);
            dc.DrawText(nome, new Point(rn.X + 7, rn.Center.Y - nome.Height / 2));
            LogoEquipa(dc, v, new Rect(rn.Right + 8, 18, 28, 28));
            Linha(Idioma.T("Jogador"), 52);
            T_(dc, T.NomeJogador(p), 136, 51, 13.5, MM.Texto, FontWeight.SemiBold, 178, linhas: 1);
            Linha(Idioma.T("Posição"), 80);
            T_(dc, T.Posicao(p), 136, 79, 13.5, MM.Texto, FontWeight.SemiBold, 178, linhas: 1);
            Linha(Idioma.T("Força"), 108);
            var tf = MM.Txt(p.Forca.ToString(), 17, MM.Texto, peso: FontWeight.Bold);
            var rf = new Rect(136, 103, Math.Max(40, tf.WidthIncludingTrailingWhitespace + 16), 26);
            dc.DrawRectangle(MM.AzulFundo, new Pen(MM.Azul, 1), rf, 7, 7);
            dc.DrawText(tf, new Point(rf.Center.X - tf.WidthIncludingTrailingWhitespace / 2, rf.Center.Y - tf.Height / 2));
            dc.FillRectangle(MM.Linha, new Rect(18, 136, ce.Width - 20, 1));
            Linha(Idioma.T("Salário pretendido"), 144);
            T_(dc, salario, 136, 143, 13.5, MM.Texto, FontWeight.SemiBold, 178, linhas: 1);
            Linha(Idioma.T("Preço base"), 172);
            T_(dc, basePreco.Length > 0 ? basePreco : "–", 136, 171, 13.5, MM.Texto, FontWeight.SemiBold, 178, linhas: 1);
            // suspenso / lesionado (2:3363-348e)
            if (p.Suspensao > 0)
                T_(dc, p.Suspensao == 1 ? Idioma.T("Suspenso por 1 jogo") : Idioma.T("Suspenso por {0} jogos", p.Suspensao), 18, 200, 12.5, MM.Vermelho, FontWeight.SemiBold, 296, linhas: 1);
            if (p.JogosLesao > 0)
                T_(dc, p.JogosLesao == 1 ? Idioma.T("Lesionado por 1 jogo") : Idioma.T("Lesionado por {0} jogos", p.JogosLesao), 18, 222, 12.5, MM.Vermelho, FontWeight.SemiBold, 296, linhas: 1);

            // a direita: a Nacionalidade (o nome e a bandeira)
            var cn = new Rect(330, 8, 238, 62);
            MM.Cartao_(dc, cn, MM.Barra, 10, MM.Linha);
            string pais = T.PaisDe(p.Pais);
            T_(dc, Idioma.T("Nacionalidade").ToUpperInvariant(), 342, 15, 10, MM.Fraco, FontWeight.SemiBold, 130, linhas: 1);
            T_(dc, T.NomePais(pais), 342, 31, 13, MM.Texto, FontWeight.SemiBold, 134);
            T.Bandeira(dc, pais, new Rect(488, 14, 70, 50));

            // o Panel1 e o Historico
            var cp = new Rect(330, 76, 238, 172);
            MM.Cartao_(dc, cp, MM.Barra, 10, MM.Linha);
            var itens = new List<(string, string)> { (Idioma.T("Comportamento"), T.Comportamento(p)) };
            if (launcher) { itens.Add((Idioma.T("Nota"), p.Nota.ToString())); itens.Add((Idioma.T("Risco de lesão"), p.Lesao.ToString())); }
            itens.Add((Idioma.T("Gols na Temporada"), Mem.Golos14(p).ToString()));
            var hist = new List<(string, int)> { (Idioma.T("Jogos"), T.Hist(p, Mem.HistJogos)), (Idioma.T("Gols"), T.Hist(p, Mem.HistGolos)) };
            if (launcher) hist.Add((Idioma.T("Cartões amarelos"), T.Amarelos(p)));
            hist.Add((Idioma.T("Cartões vermelhos"), T.Hist(p, Mem.HistVermelhos)));
            hist.Add((Idioma.T("Lesões"), T.Hist(p, Mem.HistLesoes)));
            double passo = launcher ? 14.5 : 16, y = cp.Y + 8;
            foreach (var (r, val) in itens)
            {
                T_(dc, r, 342, y, 11.5, MM.Fraco, FontWeight.Normal, 124, linhas: 1);
                T_(dc, val, 468, y, 11.5, MM.Texto, FontWeight.SemiBold, 90, TextAlignment.Right, 1);
                y += passo;
            }
            y += 4;
            T_(dc, Idioma.T("Histórico").ToUpperInvariant(), 342, y, 10, MM.Fraco, FontWeight.SemiBold);
            y += 15;
            var ch = new Rect(338, y, 222, cp.Bottom - 8 - y);
            MM.Cartao_(dc, ch, MM.Cartao, 7, MM.Linha);
            double lh = (ch.Height - 4) / hist.Count;
            for (int k = 0; k < hist.Count; k++)
            {
                double yl = ch.Y + 2 + k * lh;
                if (k > 0) dc.FillRectangle(MM.Linha, new Rect(ch.X + 1, yl, ch.Width - 2, 1));
                var tr = MM.Txt(hist[k].Item1, 11, MM.Fraco);
                dc.DrawText(tr, new Point(ch.X + 10, yl + (lh - tr.Height) / 2));
                var tv = MM.Txt(hist[k].Item2.ToString(), 11.5, MM.Texto, peso: FontWeight.SemiBold);
                dc.DrawText(tv, new Point(ch.Right - 10 - tv.WidthIncludingTrailingWhitespace, yl + (lh - tv.Height) / 2));
            }

            // o resultado: os logos do vendedor e (vendido) do comprador e o texto
            if (Transferido.Length > 0)
            {
                bool vendeu = Comprador != null;
                var rr = new Rect(12, 256, 552, 64);
                MM.Cartao_(dc, rr, vendeu ? TemaVerde.Selecao : MM.Barra, 10, vendeu ? MM.Vivo : MM.Borda);
                double xt = rr.X + 16, xf = rr.Right - 16;
                LogoEquipa(dc, v, new Rect(xt, rr.Center.Y - 18, 36, 36));
                xt += 44;
                if (Comprador != null)
                {
                    // vendedor -> comprador
                    var seta = new Pen(MM.Vivo, 2, lineCap: PenLineCap.Round);
                    double sy = rr.Center.Y;
                    dc.DrawLine(seta, new Point(xt, sy), new Point(xt + 18, sy));
                    dc.DrawLine(seta, new Point(xt + 12, sy - 5), new Point(xt + 18, sy));
                    dc.DrawLine(seta, new Point(xt + 12, sy + 5), new Point(xt + 18, sy));
                    xt += 26;
                    LogoEquipa(dc, Comprador, new Rect(xt, rr.Center.Y - 18, 36, 36));
                    xt += 48;
                }
                var t = MM.Txt(Transferido, 15, vendeu ? MM.Vivo : MM.Texto, peso: FontWeight.SemiBold);
                t.MaxTextWidth = xf - xt;
                t.MaxLineCount = 2;
                t.Trimming = TextTrimming.CharacterEllipsis;
                dc.DrawText(t, new Point(xt, rr.Y + Math.Floor((rr.Height - t.Height) / 2)));
            }
        }
    }

    // ================================================================== demo
    // RETROFOOT_TELA=leilao: o save do oraculo (como a demo da tela da equipa) com o motor
    // ligado; quando a tela da equipa aparece, RETROFOOT_LEILAO escolhe o que abrir:
    //   vender (padrao): a pagina Vender do 1o jogador vendavel do humano
    //   salario: a renovacao de contrato do 1o que pode mudar de salario
    //   pedido0..pedido3: o TaskMoreSalaryDlg naquele modo
    //   lance: um leilao da CPU em que o humano da lance (a janela inteira, como seria com o
    //          gancho do motor)
    //   impedimento: Vender num jogador que nao pode ser vendido
    public static Control Demo()
    {
        var raiz = new Border { Background = new SolidColorBrush(Color.FromRgb(0, 128, 0)) };
        var arq = Environment.GetEnvironmentVariable("RETROFOOT_SAVE");
        if (string.IsNullOrEmpty(arq) || !System.IO.File.Exists(arq)) arq = AcharSave();
        if (arq == null) return new TextBlock { Text = "Demo: sem save do oráculo", Foreground = Brushes.White };
        var jogo = Jogo98.Ler(System.IO.File.ReadAllBytes(arq));
        bool launcher = Environment.GetEnvironmentVariable("RETROFOOT_LAUNCHER") == "1";
        var op = RetroFoot.Motor.Ciclo.Opcoes.Padrao with
        {
            Regras = launcher ? RetroFoot.Motor.Regras.Regras.Launcher(8) : RetroFoot.Motor.Regras.Regras.Original,
        };
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "retrofoot-demo-leilao.e98");
        var s = new Sessao(jogo, c => raiz.Child = c, op, tmp);
        LigacaoMercado.Ligar(s);
        s.Erro += ex => raiz.Child = new TextBlock { Text = ex.ToString(), Foreground = Brushes.White };
        s.Comecar();
        var qual = Environment.GetEnvironmentVariable("RETROFOOT_LEILAO") ?? "vender";
        void Quando()
        {
            if (!Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(raiz).OfType<TelaEquipa>().Any()) { DispatcherTimer.RunOnce(Quando, TimeSpan.FromMilliseconds(200)); return; }
            var m = s.J.M;
            var e = m.S.Equipas.First(x => Mem.Humano(m.TreinadorDa(x)));
            var vendavel = e.Jogadores.FirstOrDefault(x => MercadoJogo.ImpedimentoVenda(e, x) == 0) ?? e.Jogadores[0];
            void Msg(string t) => AvisoMercado.Abrir(s, "Mensagem", t);
            switch (qual)
            {
                case "salario":
                    TelaNovoSalario.Abrir(s, e, e.Jogadores.FirstOrDefault(MercadoJogo.PodeMudarSalario) ?? e.Jogadores[0],
                        r => Msg("Resultado: " + r));
                    break;
                case var x when x.StartsWith("pedido"):
                    int modo = x.Length > 6 ? x[6] - '0' : 0;
                    Task.Run(() =>
                    {
                        bool r = s.J.H.PedirAumento!(e, vendavel, s.Mercado.SalarioPedido(e, vendavel), modo);
                        Dispatcher.UIThread.Post(() => Msg("Respondeu: " + (r ? "Sim" : "Não")));
                    });
                    break;
                case "lance":
                    {
                        // a CPU que vende: a 1a outra equipa com jogador vendavel
                        var cpu = m.S.Equipas.First(x => x != e && !Mem.Humano(m.TreinadorDa(x)));
                        var pj = cpu.Jogadores.FirstOrDefault(x => MercadoJogo.ImpedimentoVenda(cpu, x) == 0) ?? cpu.Jogadores[0];
                        var t = m.TreinadorDa(e)!;
                        t.B1A[3] = 0;   // pergunta sempre
                        Task.Run(() =>
                        {
                            bool r = Executar(s, cpu.ID, pj.ID, 0, lance => s.Mercado.Leiloar(cpu.ID, pj.ID, 0, lance));
                            s.J.M.Reindexar();
                            Dispatcher.UIThread.Post(() => Msg("Vendido: " + r));
                        });
                        break;
                    }
                case "impedimento":
                    {
                        var pj = e.Jogadores.FirstOrDefault(x => MercadoJogo.ImpedimentoVenda(e, x) != 0);
                        if (pj == null) { pj = e.Jogadores[0]; pj.B14[2] = 1; }
                        Vender(s, e, pj, null, r => Msg("Resultado: " + r));
                        break;
                    }
                default:
                    Vender(s, e, vendavel, Msg, r => Msg("Resultado: " + r));
                    break;
            }
        }
        DispatcherTimer.RunOnce(Quando, TimeSpan.FromMilliseconds(300));
        return raiz;
    }

    static string? AcharSave()
    {
        foreach (var ini in new[] { AppContext.BaseDirectory, System.IO.Directory.GetCurrentDirectory() })
            for (var d = new System.IO.DirectoryInfo(ini); d != null; d = d.Parent)
            {
                var f = System.IO.Path.Combine(d.FullName, "RetroFoot.Motor.Testes", "Oraculo", "j3-entrada.e98");
                if (System.IO.File.Exists(f)) return f;
            }
        return null;
    }
}

// a pagina Vender da TprepareGameDlg (Notebook, 273x329, nas cores da equipa) numa janela
// com o nome da equipa: "Vender", "Preco de venda (milhares de <moeda>)", o TEdit
// (MaxLength 6, maiusculas) e os botoes Vender e Cancelar
sealed class PaginaVender : DialogoMercado
{
    public event Action<int>? Vender;
    public event Action? Cancelou;
    internal readonly CampoMercado Preco;

    // (p: o jogador a vender, so para a folha do celular mostrar quem e)
    public PaginaVender(Sessao s, SEquipa e, SJogador? p = null)
        : base(T.Texto(e.NomeCompleto), 273, 329, T.Cores(e).Fundo, logo: Escudos.Escudo(e))
    {
        var (letra, _) = T.Cores(e);
        var m = Moeda.DoSave(s.S);
        Por(new TextoMercado { Texto = Idioma.T("Vender"), Fonte = new Fonte(W95.Arial13.Familia, 19, true), Cor = letra, Centro = true }, 0, 16, 273, 33);
        // labelSellPrice (4:11cf): "Preco de venda\r(milhares de " + nome da moeda + ")"
        Por(new TextoMercado { Texto = Idioma.T("Preço de venda\n(milhares de {0})", m.Nome), Fonte = W95.Arial13, Cor = letra, Quebra = true }, 16, 72, 169, 41);
        Preco = Por(new CampoMercado { Fonte = W95.Arial13, MaxLen = 6, Maiusculas = true }, 64, 128, 121, 24);
        var vender = Por(BotaoGlifo(Idioma.T("Vender"), "vender", W95.Arial11), 61, 208, 64, 64);
        var cancelar = Por(BotaoGlifo(Idioma.T("Cancelar"), "vender-cancelar", W95.Arial11), 149, 208, 64, 64);
        vender.Clique += ClicarVender;
        cancelar.Clique += () => Cancelou?.Invoke();
        J.AoFechar = () => Cancelou?.Invoke();
        AoEnter = ClicarVender;
        AoEsc = () => Cancelou?.Invoke();
        Foco = Preco;
        if (Celular.Ativo)
        {
            // no celular: a folha (MercadoFormsCelular.cs), com a caixa ligada a esta
            Content = MercadoFormsCelular.Vender(s, e, p, t => Preco.Texto = t, ClicarVender, () => Cancelou?.Invoke());
            Width = Height = double.NaN;
            Foco = null;
        }
    }

    // sellBtnClick (4:455a): vazio = 0; Val com erro: nada
    internal void ClicarVender()
    {
        var t = Preco.Texto;
        int v = 0;
        if (t.Length > 0 && !T.Val(t, out v)) return;
        Vender?.Invoke(v);
    }
}

// uma caixa de mensagem simples (o motivo de nao poder vender, quando quem chama nao tem a
// pagina Mensagem): TInputDlg sem caixa, como o Mensagem do Go
sealed class AvisoMercado : DialogoMercado
{
    AvisoMercado(string titulo, string texto, Action fechar) : base(titulo, 300, 150, W95.Face)
    {
        Por(new TextoMercado { Texto = texto, Fonte = W95.Arial13, Quebra = true }, 16, 16, 268, 80);
        var ok = Por(Botao.Ok(W95.MsSans11, false), 112, 108, 75, 25);
        ok.Clique += fechar;
        J.AoFechar = fechar;
        AoEnter = fechar;
        AoEsc = fechar;
        if (Celular.Ativo)
        {
            Content = CUi.Pergunta(titulo, texto, new (string, bool, Action)[] { (Idioma.T("OK"), true, fechar) }, fechar);
            Width = Height = double.NaN;
        }
    }

    public static void Abrir(Sessao s, string titulo, string texto)
    {
        AvisoMercado a = null!;
        a = new AvisoMercado(titulo, texto, () => s.FecharDialogo(a));
        s.AbrirDialogo(a);
    }
}
