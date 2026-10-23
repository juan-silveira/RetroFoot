using System;
using System.Collections.Generic;
using System.Text;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Patrocinio;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;
using RetroFoot.Motor.Temporada;
using TRegras = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.Motor.Ciclo;

// as opcoes do elif98.ini ([Options], seg05:21ea), fora do save
public sealed record Opcoes
{
    public bool VerExpulsoes;              // viewRedCardEvents       DS:2B30
    public bool VerLesoes;                 // viewInjuryEvents        DS:2B31
    public bool SubstituirNoIntervalo;     // halfTimeSubstitutions   DS:2B32
    public bool VerPenaltisCPU;            // viewComputerPenalties   DS:2B33
    public short MostrarChicotadas;        // viewPsychologicalLashes DS:2B34
    public short VerSorteio;               // viewGetTeams            DS:2B3A
    public short GravarQuando;             // saveGameWhen            DS:2B3C
    public short Velocidade;               // gameTimeProfile         DS:2D3C
    public bool AddIn13;                   // seg11:30bc(0x13)
    // (Configuracoes) a copia registada: nivel de registo dos add-ins (DS:29DE = 0x64) e a
    // compra direta (MercadoJogo.Comprar); false = a copia de avaliacao
    public bool Registrado;
    // limite de jogadores, amarelos e 5-0-5 (default = o original); o N vem do save
    public TRegras Regras;
    // CTRGROUP (BOSMAN.TXE e PLOP.TXE da pasta do jogo); null = as do 98.003
    public List<string>? Bosman, PLOP;
    // os patrocinadores do launcher (opcao PATROC; Patrocinio/Patrocinios.cs): desligados, o
    // motor e o Random ficam como sem eles
    public bool Patrocinios;
    // SPONSORS\MARCAS.DAT da pasta do jogo (Marcas.Ler); null = o arquivo nao abre
    public Marcas? Marcas;
    // os EXEs do launcher zeram os amarelos acumulados no comeco de cada epoca (patrocinio.asm,
    // desde 2026-10-14), com as regras do launcher (Regras.CartoesAmarelos). Os ORACnn.EXE do
    // oraculo sao de 2026-10-11, de antes disso: o roteiro do oraculo desliga.
    public bool AmarelosZeramNaEpoca = true;
    // RetroFoot (versao Atual): o publico pesa no arbitro (Mundo.BonusDaTorcida: +0 a +3 ao
    // mandante pelo publico comparado ao do preco de tabela, +2 sem mexer no preco); o oraculo (os EXEs do launcher) desliga
    public bool TorcidaNoArbitro = true;

    // as do elif98.ini do launcher
    public static Opcoes Padrao => new() { SubstituirNoIntervalo = true, MostrarChicotadas = 1, GravarQuando = 2, Velocidade = 6 };
}

// as decisoes dos treinadores humanos; null = o que o computador ou a janela fariam
public sealed class Humano
{
    // a tela da equipa (TprepareGameDlg) antes de cada jogo; true = sair do jogo
    public Func<JogoEmCurso, Equipa, Jogo?, bool>? Preparar;
    // quem bate o penalti (seg08:328b)
    public Func<Equipa, List<Jogador>, Jogador?, Jogador?>? Batedor;
    // a janela de substituicao (seg03:a5ad)
    public Action<JogoAtual, int, Jogador?, int>? Substituicao;
    public LanceHumano? Lance;
    // (jogo online) o leilao da CPU para todos os humanos de uma vez (MercadoJogo.LanceDeTodos)
    public Func<IReadOnlyList<Equipa>, Equipa, Jogador, Dictionary<ushort, (int Milhares, bool Ok)>?>? LanceDeTodos;
    public Func<Equipa, Jogador, int, int, bool>? PedirAumento;
    public Func<Treinador, Equipa, bool>? Convite;
    // depois de cada tique do relogio da jornada (MinutePassed, seg10:069b): a interface
    // desenha e espera o tempo do minuto
    public Action<Partida.Jornada>? Minuto;
    // a TPenaltiesDlg (seg11:0d46) quando ha cobrancas: ja decidido, a interface so mostra
    public Action<JogoAtual, Desempate>? Desempate;
    // cada lance novo ou mudado
    public Action<JogoAtual, Lance>? Lance2;
    // a TjourneyDlg abre (titulo, Taca) e fecha: a interface mostra a tela da jornada e, no
    // fim, espera o jogador (o motor fica parado ate o gancho voltar)
    public Action<byte[], Partida.Jornada>? ComecoJornada;
    public Action<Partida.Jornada>? FimJornada;
    // a proposta de patrocinio na janela de Sim/Nao (askMoreSalaryDlg do patch): true = Sim.
    // null: Nao
    public Func<Proposta, bool>? Patrocinio;
    // a janela de penalti depois da cobranca, com humano num dos lados (atacante, defensor,
    // batedor, resultado): so mostra
    public Action<Equipa, Equipa, Jogador, int>? Penalti;
}

// Um jogo em curso: o laco da temporada e da jornada (Rodada), os 90 minutos (Partida), o
// mercado e a temporada, com um unico Random para tudo, como no original.
// (Traduzido de motor/jogo.)
public sealed class JogoEmCurso
{
    public Mundo M;
    public MercadoJogo Mc;
    public Opcoes Op;
    public Humano H;
    // os patrocinadores (usados so com Op.Patrocinios)
    public Patrocinios Pat;
    // o texto do cabecalho quando o jogo e gravado
    public string Treinador = "Juan";
    public Func<DateTime> Agora = () => DateTime.Now;
    // o save escrito quando um humano saiu (RetroFoot > Sair grava na hora, 4:3146 -> 5:1796)
    public byte[]? Gravado;
    // o humano saiu na preparacao de um jogo da Taca (o save grava DS:271A = 1)
    bool tacaEmPreparacao;

    public Aleatorio R => M.R;

    // a partir de um save lido, com a semente que o Randomize do ciclo (seg05:24e5) deixou
    public JogoEmCurso(Jogo98 s, uint semente, Opcoes op, Humano h)
    {
        M = new Mundo(s, semente);
        M.Regras = TRegras.DoSave(s, op.Regras);
        Op = op; H = h;
        Mc = new MercadoJogo(s, M.R) { Regras = M.Regras, Bosman = op.Bosman, PLOP = op.PLOP, LanceDeTodos = LanceDeTodos };
        M.Bosman = op.Bosman; M.PLOP = op.PLOP;
        M.OpcaoGestaoCPU = op.AddIn13;
        M.MostrarChicotadas = op.MostrarChicotadas;
        M.TorcidaNoArbitro = op.TorcidaNoArbitro;
        M.TacaModo = op.VerSorteio;
        Pat = new Patrocinios(M, op.Marcas) { Perguntar = p => h.Patrocinio?.Invoke(p) ?? false };
        M.G = new Rodada.Ganchos
        {
            Partida = Partida,
            PrepararHumano = (m, e, jogo) =>
            {
                if (h.Preparar == null || !h.Preparar(this, e, jogo)) return;
                // jogoSairClick (4:3146): DS:2724 = 1 e grava ja (5:1796), com DS:271E = a
                // equipa cuja preparacao ficou a meio e DS:271A = o jogo e da Taca (4:3165)
                m.Sair = true;
                m.Retomar = (short)e.ID;
                tacaEmPreparacao = jogo != null && jogo.Taca != 0;
                Gravado = Gravar();
            },
            AntesDaJornada = m => { if (Op.Patrocinios) Pat.Rodada(); },
            // o convite a um humano empregado quando um time demite o treinador depois de um
            // jogo (seg03:8bcc -> TinviteCoachDlg): a mesma pergunta do mercado (H.Convite).
            // Sem isto o Mundo respondia sempre "nao" e o humano nunca era chamado para um time
            // mais forte (a "promocao" do classico)
            AceitaConvite = (_, t, e) => H.Convite?.Invoke(t, e) ?? false,
            Penaltis = (m, jogo) =>
            {
                // seg11:0d46 (TPenaltiesDlg); o batedor de cada cobranca e automatico
                var pj = JogoDaPartida(jogo);
                var d = Penaltis.DesempateTaca(m.R, pj, op.VerPenaltisCPU, m.Jornada, m.Regras.Jornadas);
                if (!d.Rapido) h.Desempate?.Invoke(pj, d);
                return d.PassaVisitante;
            },
            PedidoDeAumento = (m, e, p) =>
            {
                m.S.Cabecalho.G271C = (ushort)m.Jornada;   // o mercado le a jornada do cabecalho
                return Mc.PedidoAumento(e, p, Interacao());
            },
            ColocarAVenda = (m, e, preco, p) =>
            {
                m.S.Cabecalho.G271C = (ushort)m.Jornada;
                Mc.Leiloar(e.ID, p.ID, preco, h.Lance);
                m.Reindexar();
            },
        };
    }

    // os minutos de todos os jogos de S.Jogos: a TjourneyDlg (seg10:0200). Na Taca, os jogos
    // decididos no minuto 90 sao fechados ali mesmo e saem da lista (seg03:143e); os
    // outros, o laco da jornada fecha depois (seg05:01f6).
    bool Partida(Mundo m, byte[] titulo, bool taca)
    {
        var jn = new Partida.Jornada
        {
            R = m.R, Taca = taca, SubstituirNoIntervalo = Op.SubstituirNoIntervalo,
            Amarelos = m.Regras.CartoesAmarelos, Numero = m.Jornada, Ano = m.Ano, Acrescimos = m.Regras.Acrescimos, SubstituicaoExtra = m.Regras.SubstituicaoNaProrrogacao,
            Ganchos = new Partida.Ganchos { Substituicao = H.Substituicao, Batedor = H.Batedor, Lance = H.Lance2, Cobranca = H.Penalti },
        };
        foreach (var d in m.S.Jogos) jn.Jogos.Add(JogoDaPartida(d));
        if (taca)
            jn.Finalizar = jg =>
            {
                m.FimDeJogo(jg.Dados);
                m.S.Jogos.Remove(jg.Dados);
            };
        // no original cada repintura da janela principal (5:27e3) gasta um Random(20) numa
        // hora que depende do Windows; o motor nao sorteia isso (o oraculo tambem nao)
        H.ComecoJornada?.Invoke(titulo, jn);
        jn.Iniciar();
        while (jn.MinutoPassado()) H.Minuto?.Invoke(jn);
        H.FimJornada?.Invoke(jn);
        while (jn.Jogos.Count > 0)
        {
            jn.Finalizar?.Invoke(jn.Jogos[0]);
            jn.Jogos.RemoveAt(0);
        }
        return false;
    }

    public JogoAtual JogoDaPartida(Jogo d)
    {
        var jg = new JogoAtual { Dados = d };
        jg.Equipa[0] = M.Equipa(d.Casa);
        jg.Equipa[1] = M.Equipa(d.Fora);
        for (int l = 0; l < 2; l++) if (jg.Equipa[l] is { } e) jg.Treinador[l] = M.TreinadorDa(e);
        jg.Arbitro = M.S.Arbitros.FindLast(a => a.ID == d.Arbitro);
        return jg;
    }

    // a partir do ponto do save ate o fim da temporada ou ate um humano sair (seg05:11b8)
    public void Temporada() => M.Temporada(null);

    // o laco do jogo depois de carregado (seg05:24d6): enquanto ninguem sai, uma temporada
    // inteira (5:11b8), que comeca com NovaTemporada (5:0002) a nao ser quando o save foi
    // gravado no meio de uma, e acaba com o fim de temporada (5:038e)
    public void Ciclo(Action<Resultado>? aoFim = null)
    {
        M.G.FimDaTemporada = m =>
        {
            var t = Epoca();
            if (Op.Patrocinios) t.AntesDosPremios = Pat.Pagar;
            // a chicotada do humano rebaixado ao Distrital (5:037d) e a mesma 3:4f40 de depois
            // dos jogos, com a lista DS:2716 do mundo
            t.Chicotada = (e, flags) => { m.Chicotada(e, (int)flags); m.Reindexar(); };
            var r = t.FimDeTemporada();
            m.JanelaChicotadas();   // 5:0a84 -> 5:025a
            m.Reindexar();
            aoFim?.Invoke(r);
        };
        // o jogo novo (Retomar < 0) ja vem com a NewSeason feita: o que o patch faz depois dela
        if (M.Retomar < 0) InicioDaEpoca();
        while (!M.Sair)
        {
            M.Temporada(m => { Epoca().NovaTemporada(); m.Reindexar(); InicioDaEpoca(); });
            // a proxima temporada comeca do zero (5:038e zera DS:271E e DS:271A)
            M.Retomar = 0;
            M.JornadaJogada = false;
            M.S.Cabecalho.G271C = 1;
        }
    }

    // o "inicio" do patch dos patrocinios (seg05:11CE, depois da NewSeason): os amarelos
    // acumulados zeram (EXEs do launcher) e, com a opcao, os contratos e as propostas
    void InicioDaEpoca()
    {
        if (M.Regras.CartoesAmarelos && Op.AmarelosZeramNaEpoca) Patrocinios.ZerarAmarelos(M);
        if (Op.Patrocinios) Pat.Distribuir();
    }

    Epoca Epoca() => new(M.S, M.R)
    {
        Regras = M.Regras,
        MostrarSorteio = Op.VerSorteio,
        Interacao = Interacao(),
        Mc = { Bosman = Op.Bosman, PLOP = Op.PLOP, LanceDeTodos = LanceDeTodos },
    };

    // o save (seg05:124d): o texto do cabecalho e o nome do treinador humano e a data e
    // hora ("Juan\r11/10/2026 10:31"), cifrado duas vezes; depois DS:2B40 (jornada),
    // DS:271E, DS:271A...
    public byte[] Gravar()
    {
        var s = M.S;
        var c = s.Cabecalho;
        var t = Encoding.Latin1.GetBytes(Treinador + "\r" + Agora().ToString("d/M/yyyy HH:mm"));
        var t1 = Cifra.PorTextoEFT(t);
        c.Texto = Cifra.PorTextoEFT(t1.AsSpan(1));
        c.G271C = (ushort)M.Jornada;
        c.G271E = (ushort)M.Retomar;
        c.G271A = (byte)(M.JornadaJogada || (M.Retomar != 0 && tacaEmPreparacao) ? 1 : 0);
        return s.Gravar();
    }

    // o retrato do jogo online (Online/Retrato) parado na preparacao da equipa e (dentro do
    // gancho Preparar, antes de qualquer mudanca do humano): o save com a retomada nela, como o
    // Sair grava, e o que o save nao guarda
    public Online.Retrato Fotografar(Equipa e, Jogo? jogo)
    {
        var c = M.S.Cabecalho;
        short retomar = M.Retomar;
        bool taca = tacaEmPreparacao;
        var (texto, g271e, g271a) = (c.Texto, c.G271E, c.G271A);
        M.Retomar = (short)e.ID;
        tacaEmPreparacao = jogo != null && jogo.Taca != 0;
        try
        {
            return new Online.Retrato
            {
                Save = Gravar(),
                Semente = M.R.Semente,
                OrdemJogadores = new List<ushort>(M.OrdemJogadores),
                ForcaMedia = new Dictionary<ushort, short>(M.ForcasMedias),
                Demanda = M.DemandaPorPosicao(),
                Chicotadas = new List<Chicotada>(M.Chicotadas),
            };
        }
        finally
        {
            M.Retomar = retomar;
            tacaEmPreparacao = taca;
            (c.Texto, c.G271E, c.G271A) = (texto, g271e, g271a);
        }
    }

    // (o gancho do Humano lido na hora: o jogo online o liga depois de criar o jogo)
    Dictionary<ushort, (int Milhares, bool Ok)>? LanceDeTodos(IReadOnlyList<Equipa> quem, Equipa v, Jogador p) => H.LanceDeTodos?.Invoke(quem, v, p);

    Interacao Interacao() => new()
    {
        PedirAumento = H.PedirAumento, Lance = H.Lance, Convite = H.Convite, MostrarChicotadas = Op.MostrarChicotadas,
    };

    // ---------- o roteiro do oraculo ----------

    // a automacao do oraculo (ferramentas/oraculo/oraculo.sh), para comparar com o original:
    // 1a tela de equipa humana: Seleccionar > Automatico e Jogar; penalti: o 1o da lista;
    // substituicao forcada: entra o 1o da lista; intervalo: Continuar; pedido de aumento:
    // Enter (aceita); 2a tela de equipa humana: RetroFoot > Sair, que grava.
    public static Humano HumanoDoOraculo()
    {
        int telas = 0;
        return new Humano
        {
            Preparar = (j, e, _) =>
            {
                if (++telas > 1) return true;
                Escalacao.Automatico(j.R, e);
                return false;
            },
            Batedor = (_, titulares, sugerido) => titulares.Count > 0 ? titulares[0] : sugerido,
            Substituicao = (jg, lado, saiu, motivo) =>
            {
                if (motivo == 4 || saiu == null) return;   // intervalo: Continuar
                SubstituicaoForcada(jg.Equipa[lado]!, saiu, 0);
            },
        };
    }

    // a TforcedSubstitutionDlg (seg06:3969) com o k-esimo da lista: so com substituicoes; a
    // lista sao os reservas, na ordem do elenco; quem entra fica titular (sem somar jogo),
    // quem sai fica fora, a equipa gasta uma. Havendo goleiro no banco, goleiro so sai por
    // goleiro e linha por linha (Update, 6:3aaa).
    public static bool SubstituicaoForcada(Equipa e, Jogador saiu, int k)
    {
        if (Memoria.Substituicoes(e) <= 0) return false;
        int goleiros = 0;
        foreach (var p in e.Jogadores)
            if (Memoria.Escalacao(p) == Memoria.EscReserva && p.Posicao == 0) goleiros++;
        foreach (var p in e.Jogadores)
        {
            if (Memoria.Escalacao(p) != Memoria.EscReserva) continue;
            if (goleiros > 0 && (p.Posicao == 0) != (saiu.Posicao == 0)) continue;
            if (k > 0) { k--; continue; }
            Memoria.MudarEscalacao(p, Memoria.EscTitular);
            Memoria.MudarEscalacao(saiu, Memoria.EscFora);
            Memoria.MudarSubstituicoes(e, (short)(Memoria.Substituicoes(e) - 1));
            return true;
        }
        return false;
    }

    // carrega o save, faz o roteiro do oraculo com a semente 0 (a do ORACULO.EXE) e as regras
    // rg, e devolve o save gravado
    public static byte[] JogarComoOraculo(Jogo98 s, TRegras rg, List<string>? bosman, List<string>? plop)
    {
        var op = Opcoes.Padrao with { Regras = rg, Bosman = bosman, PLOP = plop, AmarelosZeramNaEpoca = false, TorcidaNoArbitro = false };
        var j = new JogoEmCurso(s, 0, op, HumanoDoOraculo()) { Agora = () => default };
        j.Ciclo();
        return j.Gravado ?? j.Gravar();
    }
}
