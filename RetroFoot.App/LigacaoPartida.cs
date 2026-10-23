using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;
using Mem = RetroFoot.Motor.Partida.Memoria;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;
using TRegras = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.App;

// Liga os dialogos da partida aos ganchos do motor (Humano), todos na thread do MOTOR e com
// s.EsperarDialogo: a janela abre por cima da jornada e o motor espera ela fechar.
//  - Substituicao: lesao (motivo 2) -> TelaSubstituicaoForcada, se ha substituicoes;
//    goleiro expulso (3) e intervalo (4) -> TelaSubstituicao;
//  - Batedor: TelaPenalti (o humano escolhe; a janela mostra o resultado no tique 3);
//  - Desempate: TelaPenaltis;
//  - Minuto: entre dois minutos, atende o pedido de detalhes de um jogo (PedirDetalhes, o
//    clique num jogo da jornada; motivo 1).
// Chamar Ligar(s) depois de criar a Sessao e antes de s.Comecar().
public static class LigacaoPartida
{
    // add-ins (sempre ligados: Registo.Nivel = 0x64)
    public static bool SubstituicoesDuranteJogo;   // 0x12: Substituir tambem no motivo 1
    public static bool VerEscalacaoDosOutros;      // 0x14: a parte de baixo para equipas do computador

    sealed class Estado
    {
        public readonly object Trava = new();
        public Jornada? Jornada;                   // a jornada em curso (entre Comeco e Fim)
        public (JogoAtual Jogo, int Lado)? Pedido; // o clique num jogo, a atender no proximo minuto
    }

    static readonly ConditionalWeakTable<Sessao, Estado> Estados = new();

    public static void Ligar(Sessao s)
    {
        var h = s.J.H;
        var st = Estados.GetValue(s, _ => new Estado());
        // o motor abre estas janelas no meio de um tique (o intervalo no 45, a lesao, o
        // penalti), antes do Minuto desse tique: a jornada e redesenhada antes, senao o relogio
        // ficava no minuto anterior (o 44' com a janela do intervalo aberta)
        h.Substituicao = (jg, lado, saiu, motivo) => { Redesenhar(s, st); Substituicao(s, jg, lado, saiu, motivo); };
        h.Batedor = (atacante, titulares, sugerido) => { Redesenhar(s, st); return Batedor(s, st, atacante, titulares, sugerido); };
        // o humano defende: a janela com o batedor do computador e o resultado ja sorteado
        // (quando o humano cobra, a janela ja abriu no Batedor)
        h.Penalti = (atacante, defensor, batedor, res) =>
        {
            if (Mem.Humano(s.TreinadorDa(atacante))) return;
            s.EsperarDialogo<SJogador>(fim =>
            {
                var t = new TelaPenalti(atacante, "", new List<SJogador>(), batedor, _ => res);
                t.Terminou += fim;
                return t;
            });
        };
        // o sorteio da Taca (opcao DS:2B3A)
        s.J.M.G.Sorteio = _ => MostrarSorteio(s);
        h.Desempate = (jg, d) => s.EsperarDialogo<bool>(fim =>
        {
            var t = new TelaPenaltis(jg, d);
            t.Fechou += () => fim(true);
            return t;
        });
        var comeco = h.ComecoJornada;
        h.ComecoJornada = (titulo, jn) =>
        {
            lock (st.Trava) { st.Jornada = jn; st.Pedido = null; }
            // o titulo da jornada no idioma escolhido (so a tela; o motor guarda o original)
            var t = Mundo.TituloNaTela(System.Text.Encoding.Latin1.GetString(titulo));
            comeco?.Invoke(System.Text.Encoding.Latin1.GetBytes(t), jn);
        };
        // os efeitos sonoros (Som.cs): golo e aviso nos lances dos jogos com humano, o apito
        // no comeco, no intervalo e no fim da jornada
        var lance2 = h.Lance2;
        h.Lance2 = (jg, l) => { lance2?.Invoke(jg, l); SomDoLance(jg, l); };
        bool intervaloTocado = false;
        var minuto = h.Minuto;
        h.Minuto = jn =>
        {
            if (jn.Minuto == 1 && jn.Acrescimo == 0) { Som.Tocar(Som.Efeito.Apito); intervaloTocado = false; }
            else if (jn.NoIntervalo && !intervaloTocado) { Som.Tocar(Som.Efeito.Apito); intervaloTocado = true; }
            minuto?.Invoke(jn);
            (JogoAtual, int)? p;
            lock (st.Trava) { p = st.Pedido; st.Pedido = null; }
            // as trocas feitas pela janela de detalhes (add-in 0x12) vao para a linha do tempo
            if (p is var (jg, lado))
            {
                // (jogo online: o host parou o jogo de todos; os outros veem o aviso)
                s.Online?.AvisarPausa(jg.Equipa[lado]?.ID ?? 0, true);
                jn.ObservarTrocas(jg, lado, null, false, () => Detalhes(s, jg, lado, jn));
                s.Online?.AvisarPausa(0, false);
            }
        };
        var fimJornada = h.FimJornada;
        h.FimJornada = jn =>
        {
            Som.Tocar(Som.Efeito.ApitoFinal);
            lock (st.Trava) { st.Jornada = null; st.Pedido = null; }
            fimJornada?.Invoke(jn);
        };
    }

    // o som de um lance novo: so nos jogos com treinador humano (como o aviso do original)
    static void SomDoLance(JogoAtual jg, Lance l)
    {
        if (jg.Treinador[Lado.Casa] is not { Humano: not 0 } && jg.Treinador[Lado.Visit] is not { Humano: not 0 }) return;
        if (l.Tipo is TipoLance.Gol or TipoLance.Penalti) Som.Tocar(Som.Efeito.Golo);
        else if (l.Tipo is TipoLance.Expulsao or TipoLance.Lesao) Som.Tocar(Som.Efeito.Aviso);
    }

    // Na thread da INTERFACE (o clique num jogo da TjourneyDlg): pede a janela de detalhes do
    // jogo (TsubstitutionDlg, motivo 1) para a equipa do lado clicado. O motor a abre entre
    // dois minutos, parado como o relogio do original. Fora de uma jornada, nada.
    public static void PedirDetalhes(Sessao s, JogoAtual jg, int lado)
    {
        // (jogo online, noutro aparelho: o pedido de parar vai para o host)
        if (s.Remoto is { } r) { r.PedirPausa(jg, lado); return; }
        if (!Estados.TryGetValue(s, out var st)) return;
        lock (st.Trava)
            if (st.Jornada != null && jg.Equipa[lado] != null) st.Pedido = (jg, lado);
    }

    // Na thread do MOTOR: o sorteio da Taca (TsorteioDlg), para um gancho do motor chamado em
    // Mundo.JogarTaca logo depois de Epoca.OrdenarTacaPorNome (os jogos ja criados).
    public static void MostrarSorteio(Sessao s) => s.EsperarDialogo<bool>(fim =>
    {
        var m = s.J.M;
        var t = new TelaSorteio(s.S, m.Equipa, e => e != null && Mem.Humano(m.TreinadorDa(e)));
        t.Fechou += () => fim(true);
        return t;
    });

    // ---------------------------------------------------------------- os ganchos

    static void Substituicao(Sessao s, JogoAtual jg, int lado, SJogador? saiu, int motivo)
    {
        var e = jg.Equipa[lado];
        if (e == null) return;
        if (motivo == 2)
        {
            // seg06:37ba: so com substituicoes
            if (saiu == null || Mem.Substituicoes(e) <= 0) return;
            s.EsperarDialogo<bool>(fim =>
            {
                var t = new TelaSubstituicaoForcada(e, saiu);
                t.Fechou += () => fim(true);
                return t;
            });
            return;
        }
        Janela(s, jg, lado, motivo);
    }

    // (jogo online, noutro aparelho, fora da thread da interface) a janela da paralisacao: a de
    // detalhes (motivo 1) ou a do intervalo/goleiro expulso (4/3), sobre a copia ao vivo
    internal static void JanelaOnline(Sessao s, JogoAtual jg, int lado, int motivo, Jornada jn)
    {
        if (jg.Equipa[lado] is not { } e) return;
        if (motivo == 1) { Detalhes(s, jg, lado, jn); return; }
        Jornada.OrdenarPorPosicaoNome(e);
        Janela(s, jg, lado, motivo);
    }

    // o motor ja reordenou o elenco nos motivos 3 e 4 (Jornada.JanelaSubstituicao)
    static void Janela(Sessao s, JogoAtual jg, int lado, int motivo) => s.EsperarDialogo<bool>(fim =>
    {
        var t = new TelaSubstituicao(jg, lado, motivo, s.J.Op, s.J.M.Regras.CartoesAmarelos,
            SubstituicoesDuranteJogo, VerEscalacaoDosOutros);
        t.Fechou += () => fim(true);
        return t;
    });

    // motivo 1: a TsubstitutionDlg ordena o elenco (14:0e0d com 11:1826) ao abrir. No visual
    // Moderno, a linha do tempo (TelaDetalhesModerna; o elenco e ordenado do mesmo jeito), com
    // a TsubstitutionDlg pelo "Escalação" quando ela mostraria as listas
    static void Detalhes(Sessao s, JogoAtual jg, int lado, Jornada jn)
    {
        if (jg.Equipa[lado] is not { } e) return;
        Jornada.OrdenarPorPosicaoNome(e);
        if (!Visual.Moderno) { Janela(s, jg, lado, 1); return; }
        int acr = Math.Min(jn.Acrescimo, jn.AcrescimosDoJogo(jg));
        s.EsperarDialogo<bool>(fim => DetalhesModernos(s, jg, lado, jn.Minuto, acr, jn.NoIntervalo, () => fim(true)));
    }

    // na thread do motor: a jornada com o estado de agora
    static void Redesenhar(Sessao s, Estado st)
    {
        Jornada? jn;
        lock (st.Trava) jn = st.Jornada;
        if (jn != null) s.AtualizarJornada(jn);
    }

    // (jogo online) o jogo de outro time aberto so para ver, na thread da interface, sem parar a
    // partida (o relogio segue; a tela da jornada fecha a janela no intervalo e no fim): sem trocas
    // e sem reordenar o elenco (o motor do host pode estar mexendo nele)
    internal static Control DetalhesSoVer(Sessao s, JogoAtual jg, int lado, Jornada jn, Action fim)
    {
        if (!Visual.Moderno)
        {
            var t = new TelaSubstituicao(jg, lado, 1, s.J.Op, s.J.M.Regras.CartoesAmarelos, false, VerEscalacaoDosOutros);
            t.Fechou += fim;
            return t;
        }
        return DetalhesModernos(s, jg, lado, jn.Minuto, Math.Min(jn.Acrescimo, jn.AcrescimosDoJogo(jg)), jn.NoIntervalo, fim, false);
    }

    internal static Control DetalhesModernos(Sessao s, JogoAtual jg, int lado, int minuto, int acrescimo, bool noIntervalo, Action fim, bool trocas = true)
    {
        bool completa = P98.Humano(jg, lado) || VerEscalacaoDosOutros;
        Func<Control>? escalacao = completa ? () =>
        {
            var c = new TelaSubstituicao(jg, lado, 1, s.J.Op, s.J.M.Regras.CartoesAmarelos, trocas && SubstituicoesDuranteJogo, VerEscalacaoDosOutros);
            c.Fechou += fim;
            return c;
        } : null;
        var t = new TelaDetalhesModerna(jg, minuto, acrescimo, noIntervalo, escalacao);
        t.Fechou += fim;
        return t;
    }

    // (jogo online, noutro aparelho, fora da thread da interface) a escolha do batedor
    internal static SJogador? BatedorOnline(Sessao s, SEquipa atacante, List<SJogador> titulares, SJogador? sugerido) =>
        Batedor(s, Estados.GetValue(s, _ => new Estado()), atacante, titulares, sugerido);

    // seg08:328b com o humano cobrando. A cobranca e do motor (Jornada.Penalti, logo depois
    // deste gancho: Goleiro(defensor) e CobrarPenalti com o Random do jogo); a janela mostra o
    // mesmo resultado, calculado com uma copia do Random.
    static SJogador? Batedor(Sessao s, Estado st, SEquipa atacante, List<SJogador> titulares, SJogador? sugerido)
    {
        if (titulares.Count == 0) return sugerido;
        SEquipa? defensor = null;
        lock (st.Trava)
            if (st.Jornada != null)
                foreach (var jg in st.Jornada.Jogos)
                    if (jg.Equipa[Lado.Casa] == atacante) defensor = jg.Equipa[Lado.Visit];
                    else if (jg.Equipa[Lado.Visit] == atacante) defensor = jg.Equipa[Lado.Casa];
        string treinador = s.TreinadorDa(atacante) is { } tr ? P98.Texto(tr.Nome) : "";
        return s.EsperarDialogo<SJogador>(fim =>
        {
            var t = new TelaPenalti(atacante, treinador, titulares, null, b => Previsto(s, defensor, b));
            t.Terminou += fim;
            return t;
        });
    }

    static int Previsto(Sessao s, SEquipa? defensor, SJogador batedor)
    {
        var gk = defensor == null ? null : Penaltis.Goleiro(defensor);
        if (gk == null) return 0;
        return Penaltis.CobrarPenalti(new Aleatorio(s.J.R.Semente), gk, batedor);
    }

    // ---------------------------------------------------------------- demo

    // RETROFOOT_TELA=partida e RETROFOOT_DIALOGO = substituicao (padrao) | intervalo | goleiro |
    // detalhes | lesao | penalti | penaltis | sorteio: o dialogo sozinho com dados do save de
    // demo (RETROFOOT_SAVE ou Assets/Saves/20.e98); RETROFOOT_DIALOGO=motor: o jogo de verdade
    // (Sessao + Ligar), com a escalacao automatica no lugar da tela da equipa.
    public static Control Demo()
    {
        var raiz = new Border { Background = new ImmutableSolidColorBrush(Color.FromRgb(0, 128, 0)) };
        try
        {
            var bytes = SaveDemo();
            var s = Jogo98.Ler(bytes);
            int n = s.Divisoes.Count > 0 ? s.Divisoes[0].Equipas.Count : 8;
            var op = Opcoes.Padrao with { Regras = n > 8 ? TRegras.Launcher(n) : TRegras.Original };
            var qual = Environment.GetEnvironmentVariable("RETROFOOT_DIALOGO") ?? "substituicao";
            // a Sessao de hoje mostra a jornada direto (ComecoJornada chama o "mostrar" do
            // construtor, nao o this.mostrar), fora da camada dos dialogos: a demo poe as telas
            // dentro do host da Sessao (achado por reflexao, so aqui)
            Border? camada = null; Control? host = null;
            var sessao = new Sessao(s, c => { if (c == host) return; if (camada != null) camada.Child = c; else raiz.Child = c; },
                op, Path.Combine(Path.GetTempPath(), "retrofoot-demo-partida.e98"));
            const System.Reflection.BindingFlags bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            host = typeof(Sessao).GetField("host", bf)?.GetValue(sessao) as Control;
            camada = typeof(Sessao).GetField("camadaTela", bf)?.GetValue(sessao) as Border;
            if (host != null && camada != null) raiz.Child = host;
            if (int.TryParse(Environment.GetEnvironmentVariable("RETROFOOT_MS"), out var ms)) sessao.MsPorMinuto = ms;
            if (qual == "motor")
            {
                Ligar(sessao);
                sessao.J.H.Preparar = (j, e, _) => { Escalacao.Automatico(j.R, e); return false; };
                sessao.Erro += ex => raiz.Child = new TextBlock { Text = ex.ToString(), Foreground = Brushes.White };
                sessao.Comecar();
                return raiz;
            }
            raiz.Child = Quadro(Dialogo(sessao, qual));
        }
        catch (Exception ex)
        {
            raiz.Child = new TextBlock { Text = "partida: " + ex, Foreground = Brushes.White };
        }
        return raiz;
    }

    static byte[] SaveDemo()
    {
        var arq = Environment.GetEnvironmentVariable("RETROFOOT_SAVE");
        if (!string.IsNullOrEmpty(arq)) return File.ReadAllBytes(arq);
        using var r = AssetLoader.Open(new Uri("avares://RetroFoot.App/Assets/Saves/20.e98"));
        using var m = new MemoryStream();
        r.CopyTo(m);
        return m.ToArray();
    }

    // como a Sessao mostra: centrado num quadro de 800x600 ampliado
    static Control Quadro(Control d)
    {
        d.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        d.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        var q = new Grid { Width = 800, Height = 600 };
        q.Children.Add(d);
        return new Viewbox { Stretch = Stretch.Uniform, Child = q };
    }

    internal static Control Dialogo(Sessao sessao, string qual)
    {
        var m = sessao.J.M;
        var r = sessao.J.R;
        if (qual == "sorteio") return TelaSorteio.Demo(m.S, m);
        var (jg, lado) = JogoDemo(sessao);
        var tit = (int l) => jg.Equipa[l]!.Jogadores.Where(p => Mem.Escalacao(p) == Mem.EscTitular).ToList();
        var eh = jg.Equipa[lado]!;
        switch (qual)
        {
            case "lesao":
            {
                var saiu = tit(lado)[3];
                Mem.MudarEscalacao(saiu, Mem.EscFora);
                return new TelaSubstituicaoForcada(eh, saiu);
            }
            case "penalti":
            {
                var def = jg.Equipa[1 - lado]!;
                return new TelaPenalti(eh, sessao.TreinadorDa(eh) is { } t ? P98.Texto(t.Nome) : "", tit(lado), null,
                    b => Penaltis.Goleiro(def) is { } gk ? Penaltis.CobrarPenalti(new Aleatorio(r.Semente), gk, b) : 0);
            }
            case "penaltis":
            {
                var d = Penaltis.DesempateTaca(r, jg, true, 1, 14);
                return new TelaPenaltis(jg, d);
            }
            case "goleiro":
            {
                var gk = tit(lado).First(p => p.Posicao == 0);
                Mem.MudarEscalacao(gk, Mem.EscFora);
                Jornada.OrdenarPorPosicaoNome(eh);
                return new TelaSubstituicao(jg, lado, 3, sessao.J.Op, m.Regras.CartoesAmarelos);
            }
            case "detalhes":
                Jornada.OrdenarPorPosicaoNome(jg.Equipa[1 - lado]!);
                if (Visual.Moderno) return DetalhesModernos(sessao, jg, 1 - lado, 60, 0, false, () => Console.WriteLine("RETROFOOT-DETALHES fechou"));
                return new TelaSubstituicao(jg, 1 - lado, 1, sessao.J.Op, m.Regras.CartoesAmarelos);
            default:
                Jornada.OrdenarPorPosicaoNome(eh);
                return new TelaSubstituicao(jg, lado, 4, sessao.J.Op, m.Regras.CartoesAmarelos);
        }
    }

    // so as demos: um jogo do humano, com as duas equipas escaladas e uns lances (e os extras
    // da linha do tempo); devolve o jogo e o lado do humano
    internal static (JogoAtual Jogo, int Lado) JogoDemo(Sessao sessao)
    {
        var m = sessao.J.M;
        var r = sessao.J.R;
        var hum = m.S.Equipas.First(e => Mem.Humano(m.TreinadorDa(e)));
        var dados = m.S.Jogos.FirstOrDefault(j => j.Casa == hum.ID || j.Fora == hum.ID);
        if (dados == null)
        {
            // so a demo: os jogos do save saem sem ser fechados
            m.S.Jogos.Clear();
            for (int d = 0; d <= 3 && d < m.S.Divisoes.Count; d++) m.CriarJogosDivisao(d);
            dados = m.S.Jogos.First(j => j.Casa == hum.ID || j.Fora == hum.ID);
        }
        var jg = sessao.J.JogoDaPartida(dados);
        int lado = jg.Equipa[Lado.Casa] == hum ? Lado.Casa : Lado.Visit;
        foreach (var e in jg.Equipa)
        {
            Escalacao.Automatico(r, e!);
            Mem.ZerarPartida(e!);
            Mem.MudarSubstituicoes(e!, 3);
        }
        PreJogo.PrepararJogo(r, jg, m.S.Arbitros);
        var tit = (int l) => jg.Equipa[l]!.Jogadores.Where(p => Mem.Escalacao(p) == Mem.EscTitular).ToList();
        void Poe(int l, int min, int tipo, SJogador p)
        {
            jg.Lances.Add(new Lance { Lado = l, Minuto = min, Tipo = tipo, JogadorID = p.ID, Posicao = p.Posicao, Jogador = p });
        }
        Poe(Lado.Casa, 12, TipoLance.Gol, tit(Lado.Casa)[^1]);
        Poe(Lado.Visit, 20, TipoLance.Amarelo, tit(Lado.Visit)[3]);
        Poe(Lado.Visit, 31, TipoLance.Penalti, tit(Lado.Visit)[^2]);
        Poe(Lado.Casa, 38, TipoLance.Lesao, tit(Lado.Casa)[2]);
        Poe(Lado.Visit, 41, TipoLance.Gol, tit(Lado.Visit)[^1]);
        Poe(Lado.Visit, 42, TipoLance.GolAnulado, tit(Lado.Visit)[^3]);
        Poe(Lado.Casa, 44, TipoLance.Expulsao, tit(Lado.Casa)[4]);
        // os extras da linha do tempo moderna: penalti defendido, a troca depois da lesao e
        // uma no intervalo (Ordem = lances anteriores)
        var res = (int l) => jg.Equipa[l]!.Jogadores.Where(p => Mem.Escalacao(p) == Mem.EscReserva).ToList();
        jg.Extras.Add(new LanceExtra { Lado = Lado.Casa, Minuto = 27, Ordem = 2, Tipo = TipoExtra.PenaltiFalhado,
            Batedor = tit(Lado.Casa)[^2], Goleiro = tit(Lado.Visit)[0], Resultado = ResultadoPenalti.Defesa });
        if (res(Lado.Casa).Count > 0)
            jg.Extras.Add(new LanceExtra { Lado = Lado.Casa, Minuto = 38, Ordem = 4, Tipo = TipoExtra.Substituicao, Saiu = tit(Lado.Casa)[2], Entrou = res(Lado.Casa)[0] });
        if (res(Lado.Visit).Count > 0)
            jg.Extras.Add(new LanceExtra { Lado = Lado.Visit, Minuto = 45, Ordem = 7, Intervalo = true, Tipo = TipoExtra.Substituicao, Saiu = tit(Lado.Visit)[^1], Entrou = res(Lado.Visit)[^1] });
        jg.Extras.Add(new LanceExtra { Lado = Lado.Visit, Minuto = 52, Ordem = 7, Tipo = TipoExtra.PenaltiFalhado,
            Batedor = tit(Lado.Visit)[^2], Goleiro = tit(Lado.Casa)[0], Resultado = ResultadoPenalti.Poste });
        return (jg, lado);
    }
}
