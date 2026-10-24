using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;

namespace RetroFoot.Motor.Online;

// O host do jogo online (docs/multiplayer-online.md): roda o motor de verdade e, no primeiro
// tecnico humano de cada rodada (o gancho Preparar), para tudo e abre a preparacao de todos ao
// mesmo tempo:
//  - publica o retrato (o jogo parado ali) no banco; cada cliente prepara o time numa copia;
//  - executa os comandos que chegam (emprestimo, estadio, compra, venda, renovacao) na hora,
//    com um retrato novo depois de cada um que muda o jogo; guarda o Preparo de cada um;
//  - espera todos marcarem PRONTO (o host tambem, na tela dele) e segue;
//  - no gancho de cada equipa humana da rodada aplica o Preparo dela (sem ele, ou com uma
//    escalacao que ja nao fecha, a automatica).
// Os comandos e o estado do jogo so mudam numa thread de cada vez: naInterface roda a acao na
// thread da interface do host (onde a tela dele mexe no mesmo jogo); nos testes, na hora.
public sealed class HostOnline : IDisposable
{
    readonly Sala _sala;
    readonly JogoEmCurso _j;
    readonly ExecutorComandos _ex;
    readonly Action<Action> _naInterface;
    readonly BlockingCollection<Action> _fila = new();
    readonly Dictionary<ushort, Preparo> _preparos = new();
    readonly HashSet<string> _comandosVistos = new();
    Dictionary<string, JogadorSala> _jogadores = new();
    readonly List<IDisposable> _observando = new();
    readonly Timer _vida;
    bool _localPronto;
    volatile bool _naBarreira;
    int _versao;
    // esta execucao do host (um host que volta, ou um host novo, recomeca as versoes)
    readonly string _instancia = Guid.NewGuid().ToString("N")[..8];

    // a rodada da barreira atual ("2027-5-L"; "T" na Taca)
    public string Rodada { get; private set; } = "";
    // o time de cada uid (o do host tambem)
    public IReadOnlyDictionary<string, ushort> Times => _jogadores.Values.Where(x => x.Equipa != 0).ToDictionary(x => x.Uid, x => x.Equipa);
    // a tela da equipa do host (na thread da interface): termina quando ele marca PRONTO
    // (true = o host saiu do jogo). null: o host prepara no automatico
    public Func<Equipa, Jogo?, Task<bool>>? PrepararLocal;
    bool _sair;
    // avisos para a tela do host (quem esta pronto)
    public event Action<IReadOnlyList<JogadorSala>>? JogadoresMudaram;

    public Sala Sala => _sala;

    public HostOnline(Sala sala, JogoEmCurso j, Action<Action>? naInterface = null)
    {
        _sala = sala; _j = j;
        // (o pedido de aumento dos humanos e os leiloes da CPU logo depois da rodada, antes da preparacao dos humanos)
        j.M.Online = true;
        _ex = new ExecutorComandos(j);
        _naInterface = naInterface ?? (a => a());
        _observando.Add(sala.B.Observar($"{sala.Raiz}/jogadores", (_, _) => _fila.Add(AtualizarJogadores)));
        // (fora da preparacao o motor esta jogando: os comandos sao recusados na hora, sem fila)
        _observando.Add(sala.B.Observar($"{sala.Raiz}/comandos", (c, v) => { if (_naBarreira) _fila.Add(() => Comandos(c, v)); else Comandos(c, v); }));
        _observando.Add(sala.B.Observar($"{sala.Raiz}/respostas", Respostas));
        // os pedidos para entrar (a qualquer hora, nao so na preparacao)
        _observando.Add(sala.B.Observar($"{sala.Raiz}/jogadores", (_, _) => _ = VerPedidos()));
        // (fora da preparacao e da rodada, so recusa; na rodada, parar/trocar/retomar vao para a
        // fila da rodada, atendida pelo motor entre os minutos)
        _vida = new Timer(_ => _ = Vida(), null, 0, 10_000);
        foreach (var t in j.M.S.Transferencias) _transfVistas.Add(t);
        MapearTimes();
    }

    async Task Vida()
    {
        try { await _sala.SinalDeVida(); } catch { }
    }

    // um time humano que nao e o do host (de outro aparelho, mesmo que o dono tenha saido): as
    // perguntas dele nao abrem janela no host
    public bool Remota(Equipa? e) =>
        e != null && e.ID != TimeDoHost && MercadoJogo.HumanoAtivo(_j.M.TreinadorDa(e));

    public ushort? MeuTime => TimeDoHost;
    ushort? TimeDoHost => _jogadores.TryGetValue(_sala.Uid, out var h) && h.Equipa != 0 ? h.Equipa : null;

    void AtualizarJogadores()
    {
        var l = _sala.Jogadores().GetAwaiter().GetResult();
        _jogadores = l.ToDictionary(x => x.Uid);
        JogadoresMudaram?.Invoke(l);
    }

    static string Id(Mundo m, Jogo? jogo) => $"{m.Ano}-{m.Jornada}-{(jogo != null && jogo.Taca == TipoJogo.Taca ? "T" : "L")}";

    // o gancho Preparar de uma equipa humana (na thread do motor); false = joga (nunca sai)
    public bool Preparar(Equipa e, Jogo? jogo)
    {
        if (_jogadores.Count == 0) AtualizarJogadores();
        string id = Id(_j.M, jogo);
        if (id != Rodada) Barreira(e, jogo, id);
        if (_sair)
        {
            // (o modo amigo: o save volta a ser single player antes de gravar)
            // (Bifurcar: o aplicativo grava antes uma copia com os amigos, o jogo online, e tira
            // os amigos do save ele mesmo)
            if (ModoAmigo && !Passou && !Bifurcar) TirarConvidados();
            return true;
        }
        if (Remota(e))
        {
            if (_preparos.Remove(e.ID, out var p)) _ex.Executar(e.ID, p);
            else Escalacao.Automatico(_j.R, e);
        }
        else if (!ExecutorComandos.EscalacaoValida(e)) Escalacao.Automatico(_j.R, e);
        else Memoria.MudarSubstituicoes(e, (short)Math.Min(e.Jogadores.Count(p => Memoria.Escalacao(p) == Memoria.EscReserva), Escalacao.MaxSubstituicoes));
        return false;
    }

    // os times que jogam na rodada da barreira (na Taca, so os que seguem nela): so eles preparam
    HashSet<ushort> _jogam = new();
    public bool Joga(ushort equipa) => _jogam.Contains(equipa);

    void Barreira(Equipa e, Jogo? jogo, string id)
    {
        // (quem esta na sala e o time de cada um: antes de tudo, o host tambem)
        AtualizarJogadores();
        if (SeguirTecnicos()) AtualizarJogadores();
        Ferias();
        ConferirTransferencias();
        _naRodada = false;
        _naBarreira = true;
        // (os resultados da rodada passada: ja foram lidos; so o host pode apaga-los; e o sorteio
        // dos times, ja visto)
        try { _sala.B.Gravar($"{_sala.Raiz}/resultados", null).GetAwaiter().GetResult(); } catch { }
        if (_versao == 0 && _sorteios == 0) try { _sala.B.Gravar($"{_sala.Raiz}/rodada/sorteio", null).GetAwaiter().GetResult(); } catch { }
        Rodada = id;
        _preparos.Clear();
        _forcados.Clear();
        _limite = null;
        _acabou = false;
        try { _prazo = PrazoFixo ?? TimeSpan.FromMinutes(_sala.Prazo().GetAwaiter().GetResult()); } catch { }
        try { Velocidade = _sala.Velocidade().GetAwaiter().GetResult(); } catch { }
        _jogam = _j.M.S.Jogos.SelectMany(x => new[] { x.Casa, x.Fora }).Where(x => x != 0).ToHashSet();
        _hostJoga = TimeDoHost is ushort th0 && _jogam.Contains(th0);
        _localPronto = TimeDoHost is not ushort th || PrepararLocal == null || !_jogam.Contains(th);
        // (quem estava PRONTO para um host que caiu prepara de novo: o Preparo se perdeu)
        foreach (var j in _jogadores.Values.Where(x => x.Uid != _sala.Uid && x.Estado == Estados.Pronto))
            try { _sala.B.Atualizar($"{_sala.Raiz}/jogadores/{j.Uid}", new JsonObject { ["estado"] = Estados.Preparando }).GetAwaiter().GetResult(); } catch { }
        AtualizarJogadores();
        Publicar(e, jogo);
        try { _sala.MarcarFase(Fases.Jogando).GetAwaiter().GetResult(); } catch { }
        if (!_localPronto && TimeDoHost is ushort t && _j.M.Equipa(t) is { } minha)
        {
            var jogoDele = _j.M.S.Jogos.FirstOrDefault(x => x.Casa == t || x.Fora == t);
            _ = _sala.MudarEstado(Estados.Preparando, Rodada);
            _naInterface(() => PrepararLocal!(minha, jogoDele).ContinueWith(t => _fila.Add(() =>
            {
                _localPronto = true;
                _sair = t.IsCompletedSuccessfully && t.Result;
                if (!_sair) _ = _sala.MudarEstado(Estados.Pronto, Rodada);
            })));
        }
        AtualizarJogadores();
        // (o host sem time nesta rodada: a espera pelos outros na tela dele)
        if (_localPronto) SemTimeNaRodada?.Invoke(TimeDoHost == null);
        while (!TodosProntos() && !_sair)
        {
            // (de segundo em segundo, mesmo sem novidade: o prazo e quem sumiu)
            if (_fila.TryTake(out var a, 1000)) a();
            if (_jogadores.Values.Any(x => x.Uid != _sala.Uid && (Ausente(x) || (ModoAmigo && x.Equipa == 0))))
            {
                int antes = _jogadores.Values.Count(x => x.Equipa != 0);
                Ferias();
                if (ModoAmigo) AtualizarJogadores();
                // (no modo amigo, quem chegou recebe o jogo com o time dele)
                if (ModoAmigo && _ultimo.E != null && _jogadores.Values.Count(x => x.Equipa != 0) != antes) Publicar(_ultimo.E, _ultimo.Jogo);
            }
            ConferirPrazo();
            // (as vendas e compras do host, na tela dele: o aviso para os outros na hora)
            _naInterface(ConferirTransferencias);
        }
        _naBarreira = false;
        // (o host saiu do jogo: a rodada nao comeca)
        if (Passou || _sair) return;
        // a rodada comecou: quem nao ficou pronto (o prazo acabou) vai para a espera
        try
        {
            _sala.B.Atualizar($"{_sala.Raiz}/rodada", new JsonObject { ["prazo"] = null, ["comecou"] = Rodada }).GetAwaiter().GetResult();
        }
        catch { }
    }

    bool TodosProntos() =>
        _localPronto && Faltam().Count == 0;

    // os tecnicos dos outros aparelhos que jogam na rodada e ainda nao estao prontos
    List<JogadorSala> Faltam() =>
        _jogadores.Values.Where(x => x.Equipa != 0 && x.Uid != _sala.Uid && _jogam.Contains(x.Equipa) && !Ausente(x) && !_forcados.Contains(x.Uid)
            && !(x.Estado == Estados.Pronto && x.Rodada == Rodada)).ToList();

    // (o host sem time na rodada, sem clube ou fora da Taca, nao tem PRONTO para dar: o prazo
    // comeca ja, senao a rodada esperaria para sempre por quem nao aperta Jogar)
    bool AlguemPronto() =>
        _localPronto || _jogadores.Values.Any(x => x.Uid != _sala.Uid && x.Estado == Estados.Pronto && x.Rodada == Rodada);

    // ---------------------------------------------------------------- o prazo
    // Depois do primeiro PRONTO da rodada, todos tem o prazo da sala (Sala.Prazo, 1 a 5 min); ele
    // vai para o banco (rodada/prazo) e cada aparelho mostra o relogio. Acabou: quem ainda nao
    // estava pronto vai com a escalacao que tiver (ou a automatica, se nao fecha).
    public static TimeSpan? PrazoFixo;   // (testes)
    TimeSpan _prazo = TimeSpan.FromMinutes(Sala.PrazoPadrao);
    DateTime? _limite;
    bool _acabou;
    readonly HashSet<string> _forcados = new();
    bool _hostJoga;
    // o relogio comecou (o tempo que falta) e o prazo acabou (a tela do host fecha sozinha)
    public event Action<TimeSpan>? PrazoComecou;
    // a barreira sem nada para o host preparar (true: ele esta sem clube; false: o time dele nao joga)
    public event Action<bool>? SemTimeNaRodada;
    public event Action? PrazoAcabou;

    void ConferirPrazo()
    {
        if (_limite == null)
        {
            if (!AlguemPronto() || TodosProntos()) return;
            _limite = DateTime.UtcNow + _prazo;
            // (com a hora do servidor: quem chega depois ve o tempo que falta, nao o prazo inteiro)
            try { _sala.B.Gravar($"{_sala.Raiz}/rodada/prazo", new JsonObject { ["rodada"] = Rodada, ["segundos"] = (int)_prazo.TotalSeconds, ["inicio"] = new JsonObject { [".sv"] = "timestamp" } }).GetAwaiter().GetResult(); } catch { }
            PrazoComecou?.Invoke(_prazo);
            return;
        }
        if (DateTime.UtcNow < _limite || _acabou) return;
        _acabou = true;
        foreach (var j in Faltam()) _forcados.Add(j.Uid);
        if (!_localPronto) PrazoAcabou?.Invoke();
    }

    // ---------------------------------------------------------------- ausentes e ferias
    // Quem sai do jogo (SAIU) ou some (o sinal de vida parado por PrazoAusente) fica de ferias,
    // o recurso do jogo: o computador cuida do time e a rodada nao espera por ele. Quando ele
    // volta, as ferias acabam na barreira seguinte (com o motor parado).
    public static TimeSpan PrazoAusente = TimeSpan.FromSeconds(90);
    readonly Dictionary<string, (long Visto, DateTime Quando)> _vistos = new();

    bool Ausente(JogadorSala j)
    {
        if (j.Estado is Estados.Saiu or Estados.Desistiu or Estados.Expulso or Estados.Pedindo) return true;
        if (!_vistos.TryGetValue(j.Uid, out var v) || v.Visto != j.Visto) { _vistos[j.Uid] = (j.Visto, DateTime.UtcNow); return false; }
        return DateTime.UtcNow - v.Quando > PrazoAusente;
    }

    // ---------------------------------------------------------------- o time de cada um
    // Quem joga e o tecnico, nao o time: aceitou o convite de outro clube, ou foi demitido e
    // contratado por outro, o aparelho dele passa a mandar no time novo (sem clube: nenhum, ate
    // o proximo convite). Na barreira, antes de publicar a rodada.
    readonly Dictionary<string, ushort> _tecnicoDe = new();   // uid -> o tecnico dele
    public event Action<string, ushort>? TimeMudou;              // nome, o time novo (0 = sem clube)

    bool SeguirTecnicos()
    {
        bool mudou = false;
        foreach (var j in _jogadores.Values.ToList())
        {
            if (!_tecnicoDe.TryGetValue(j.Uid, out var tid))
            {
                if (j.Equipa == 0 || _j.M.Equipa(j.Equipa) is not { } e0 || _j.M.TreinadorDa(e0) is not { Humano: not 0 } t0) continue;
                _tecnicoDe[j.Uid] = tid = t0.ID;
            }
            ushort agora = _j.M.EquipaDoTreinador(tid)?.ID ?? 0;
            if (agora == j.Equipa) continue;
            try { _sala.DarEquipa(j.Uid, agora).GetAwaiter().GetResult(); } catch { continue; }
            mudou = true;
            TimeMudou?.Invoke(j.Nome, agora);
        }
        return mudou;
    }

    // ---------------------------------------------------------------- o modo amigo
    // ---------------------------------------------------------------- entrar com o jogo comecado
    // Quem entra com a chave depois de o jogo comecar fica Pedindo; o host permite (Espectador:
    // assiste as rodadas e recebe um time da 4a divisao na proxima preparacao, com o sorteio para
    // todos) ou recusa (sai da sala). O host tambem pode expulsar: o aparelho dele sai na hora e o
    // tecnico sai do jogo na proxima preparacao (um do computador assume o time).
    public event Action<JogadorSala>? PedidoDeEntrada;
    // os times dos que entraram agora (o sorteio para todos)
    public event Action<IReadOnlyList<(string Nome, ushort Equipa)>>? NovosTimes;
    readonly ConcurrentDictionary<string, byte> _pedidosVistos = new();

    async Task VerPedidos()
    {
        try
        {
            // (cada pedido uma vez: a resposta do host demora um instante a chegar ao banco, e o
            // pedido nao pode abrir de novo nesse meio; quem voltar a pedir, depois, pergunta de novo)
            var l = await _sala.Jogadores();
            var pedindo = l.Where(j => j.Estado == Estados.Pedindo && j.Uid != _sala.Uid).Select(j => j.Uid).ToHashSet();
            foreach (var uid in _pedidosVistos.Keys)
                if (!pedindo.Contains(uid)) _pedidosVistos.TryRemove(uid, out _);
            foreach (var j in l)
                if (pedindo.Contains(j.Uid) && _pedidosVistos.TryAdd(j.Uid, 0)) PedidoDeEntrada?.Invoke(j);
        }
        catch { }
    }

    public async Task ResponderEntrada(string uid, bool permitir)
    {
        try
        {
            if (permitir) { await _sala.B.Atualizar($"{_sala.Raiz}/jogadores/{uid}", new JsonObject { ["estado"] = Estados.Espectador }); return; }
            var j = JogadorSala.De(uid, await _sala.B.Ler($"{_sala.Raiz}/jogadores/{uid}"));
            await _sala.B.Gravar($"{_sala.Raiz}/jogadores/{uid}", null);
            if (j != null && j.Vaga >= 0) await _sala.B.Gravar($"{_sala.Raiz}/vagas/{j.Vaga}", null);
        }
        catch { }
    }

    public Task Expulsar(string uid) =>
        uid == _sala.Uid ? Task.CompletedTask : _sala.B.Atualizar($"{_sala.Raiz}/jogadores/{uid}", new JsonObject { ["estado"] = Estados.Expulso });

    // na barreira: os espectadores recebem um time da 4a divisao (no modo amigo, AmigosEntramESaem)
    readonly List<(string Nome, ushort Equipa)> _novos = new();
    void Espectadores()
    {
        if (ModoAmigo) return;
        foreach (var j in _jogadores.Values.Where(x => x.Uid != _sala.Uid && x.Equipa == 0 && x.Estado == Estados.Espectador && !Ausente(x)).ToList())
        {
            (Treinador T, Equipa E)? r = null;
            _naInterface(() => r = Convidado.Entrar(_j, j.Nome, new Aleatorio((uint)Environment.TickCount)));
            if (r is not { } ok) continue;
            _tecnicoDe[j.Uid] = ok.T.ID;
            ComTime(j, ok.E.ID);
        }
    }

    void ComTime(JogadorSala j, ushort equipa)
    {
        try
        {
            _sala.B.Atualizar($"{_sala.Raiz}/jogadores/{j.Uid}", new JsonObject { ["equipa"] = (int)equipa, ["estado"] = Estados.Preparando }).GetAwaiter().GetResult();
        }
        catch { }
        _novos.Add((j.Nome, equipa));
    }

    // os novos desta barreira: o sorteio para todos (rodada/sorteio, com o n para ser visto uma vez)
    int _sorteios;
    void SortearNovos()
    {
        if (_novos.Count == 0) return;
        var l = _novos.ToList();
        _novos.Clear();
        PublicarSorteio(_sala, _j.M.S, l, ++_sorteios);
        NovosTimes?.Invoke(l);
    }

    // (Online/Convidado) um save single player com amigos, por uma sessao: quem entra assume um
    // time da 4a divisao; quem cai ou sai por agora fica de ferias (o computador cuida do time) e,
    // voltando na mesma sessao, continua com o tecnico dele; quem desiste sai do jogo. Quando o
    // host fecha o jogo, todos os amigos sao demitidos e saem do save (TirarConvidados)
    public bool ModoAmigo { get; init; }
    // o host quis, ao fechar, guardar tambem uma copia do jogo com os amigos como jogo online
    // (uma bifurcacao: o save dele volta a ser single player; a copia continua online com eles)
    public bool Bifurcar { get; set; }
    readonly Dictionary<string, ushort> _convidados = new();   // uid -> o tecnico dele
    public IReadOnlyDictionary<string, ushort> Convidados => _convidados;
    public event Action<string, bool>? ConvidadoMudou;           // nome, entrou

    bool TecnicoExiste(ushort tid) => _j.M.S.Treinadores.Any(t => t.ID == tid);

    // (modo amigo) antes de o motor comecar: quem ja esta na sala recebe o time, para participar
    // desde o comeco (os leiloes e as perguntas de antes da 1a preparacao) e o save vindo do
    // jogo sozinho termina a rodada em que parou na ordem dele
    public void ComecarModoAmigo()
    {
        if (!ModoAmigo) return;
        _j.M.RetomarNaOrdemDosJogos = true;
        AtualizarJogadores();
        AmigosEntramESaem();
        AtualizarJogadores();
    }

    // o sorteio dos times para os outros aparelhos ao mesmo tempo que o do host (rodada/sorteio:
    // os tecnicos e o jogo, para os nomes e escudos); sai na 1a barreira
    public static void PublicarSorteio(Sala sala, Jogo98 s, IReadOnlyList<(string Nome, ushort Equipa)> times, int n = 0)
    {
        try
        {
            sala.B.Gravar($"{sala.Raiz}/rodada/sorteio", new JsonObject
            {
                ["n"] = n,
                ["times"] = new JsonArray(times.Select(t => (JsonNode)new JsonObject { ["nome"] = t.Nome, ["equipa"] = (int)t.Equipa }).ToArray()),
                ["save"] = AoVivo.Comprimir(s.Gravar()),
            }).GetAwaiter().GetResult();
        }
        catch { }
    }

    // os tecnicos da sala com o time de cada um, na ordem das vagas (o sorteio dos times)
    public IReadOnlyList<(string Nome, ushort Equipa)> TimesDaSala() =>
        _jogadores.Values.Where(x => x.Equipa != 0).OrderBy(x => x.Vaga).Select(x => (x.Nome, x.Equipa)).ToList();

    void AmigosEntramESaem()
    {
        // quem chegou sem time: o tecnico dele (se ja entrou nesta sessao) ou um novo, num time da 4a divisao
        // (o sorteio nao gasta o Random do jogo). Quem tem tecnico e ficou sem clube (demitido)
        // nao ganha outro: espera um convite, como no jogo local
        foreach (var j in _jogadores.Values.Where(x => x.Uid != _sala.Uid && x.Equipa == 0 && !Ausente(x) && x.Estado != Estados.Pedindo).ToList())
        {
            if (_convidados.TryGetValue(j.Uid, out var tid) && TecnicoExiste(tid))
            {
                if (_j.M.EquipaDoTreinador(tid) is { } eq)
                {
                    try { _sala.DarEquipa(j.Uid, eq.ID).GetAwaiter().GetResult(); } catch { }
                    _tecnicoDe[j.Uid] = tid;
                    ConvidadoMudou?.Invoke(j.Nome, true);
                }
                continue;
            }
            (Treinador T, Equipa E)? r = null;
            _naInterface(() => r = Convidado.Entrar(_j, j.Nome, new Aleatorio((uint)Environment.TickCount)));
            if (r is not { } ok) continue;
            _convidados[j.Uid] = ok.T.ID;
            _tecnicoDe[j.Uid] = ok.T.ID;
            // (quem entrou com o jogo ja comecado: o sorteio para todos; os do comeco ja o viram)
            if (j.Estado == Estados.Espectador) ComTime(j, ok.E.ID);
            else try { _sala.DarEquipa(j.Uid, ok.E.ID).GetAwaiter().GetResult(); } catch { }
            ConvidadoMudou?.Invoke(j.Nome, true);
        }
    }

    // quem desistiu de vez (nos dois modos): o tecnico dele sai do jogo (um do computador assume o
    // time) e a vaga fica livre; voltando, entra como um tecnico novo
    void Desistencias()
    {
        foreach (var j in _jogadores.Values.Where(x => x.Uid != _sala.Uid && x.Estado is Estados.Desistiu or Estados.Expulso).ToList())
        {
            if (TecnicoDoConvidado(j) is ushort tid) _naInterface(() => Convidado.Sair(_j, tid));
            _convidados.Remove(j.Uid);
            _tecnicoDe.Remove(j.Uid);
            try
            {
                _sala.B.Gravar($"{_sala.Raiz}/jogadores/{j.Uid}", null).GetAwaiter().GetResult();
                if (j.Vaga >= 0) _sala.B.Gravar($"{_sala.Raiz}/vagas/{j.Vaga}", null).GetAwaiter().GetResult();
            }
            catch { }
            ConvidadoMudou?.Invoke(j.Nome, false);
        }
    }

    // o host fechou o jogo: todos os amigos sao demitidos e saem do save (um tecnico do computador
    // assume cada time)
    public void TirarConvidados()
    {
        foreach (var j in _jogadores.Values.Where(x => x.Uid != _sala.Uid).ToList())
            if (TecnicoDoConvidado(j) is ushort tid) _naInterface(() => Convidado.Sair(_j, tid));
        _convidados.Clear();
    }

    // o tecnico de um amigo: o que ele recebeu ao entrar; sem esse registro (o host mudou), o do
    // time dele, se for humano e nao o do host
    ushort? TecnicoDoConvidado(JogadorSala j)
    {
        ushort hostTec = _j.M.Equipa(TimeDoHost ?? 0)?.Treinador ?? 0;
        if (_convidados.TryGetValue(j.Uid, out var tid)) return tid != hostTec && TecnicoExiste(tid) ? tid : null;
        if (_tecnicoDe.TryGetValue(j.Uid, out tid) && tid != hostTec && TecnicoExiste(tid)) return tid;
        if (j.Equipa != 0 && _j.M.Equipa(j.Equipa) is { } e && _j.M.TreinadorDa(e) is { Humano: not 0 } t && t.ID != hostTec) return t.ID;
        return null;
    }

    // na barreira: as ferias de quem esta ausente, e o fim delas para quem voltou
    void Ferias()
    {
        Desistencias();
        if (ModoAmigo) AmigosEntramESaem();
        else Espectadores();
        SortearNovos();
        foreach (var j in _jogadores.Values.Where(x => x.Equipa != 0 && x.Uid != _sala.Uid))
        {
            if (_j.M.Equipa(j.Equipa) is not { } e || _j.M.TreinadorDa(e) is not { Humano: not 0 } t) continue;
            bool ausente = Ausente(j);
            if (ausente != (t.B06 != 0))
            {
                _naInterface(() => MercadoJogo.Ferias(t, ausente));
                Mudou?.Invoke(j, ausente);
            }
        }
    }

    // um tecnico entrou de ferias (true) ou voltou (false): o aviso para a tela do host
    public event Action<JogadorSala, bool>? Mudou;

    // o retrato do jogo parado na equipa e (o motor esta parado; o estado nao muda debaixo)
    void Publicar(Equipa e, Jogo? jogo)
    {
        Retrato r = null!;
        _naInterface(() => r = _j.Fotografar(e, jogo));
        _versao++;
        _sala.B.Gravar($"{_sala.Raiz}/retrato", new JsonObject
        {
            ["rodada"] = Rodada, ["versao"] = _versao, ["host"] = _instancia, ["dados"] = Convert.ToBase64String(r.Bytes()),
        }).GetAwaiter().GetResult();
        _ultimo = (e, jogo);
        Resumir(jogo);
    }

    // o resumo da sala para a lista das salas de cada um: o ano e a rodada (info) e o time e a
    // divisao de cada tecnico (jogadores/<uid>); so quando muda
    string _resumo = "";
    void Resumir(Jogo? jogo)
    {
        var m = _j.M;
        string rodada = jogo != null && jogo.Taca == TipoJogo.Taca ? $"Taça, rodada {m.Jornada}" : $"rodada {m.Jornada}";
        var times = _jogadores.Values.Select(j =>
        {
            var e = j.Equipa == 0 ? null : m.Equipa(j.Equipa);
            int d = e == null ? -1 : m.DivisaoDa(e);
            var dn = d < 0 ? Array.Empty<byte>() : m.S.Divisoes[d].Nome;
            return (j.Uid, Time: e == null ? "" : MercadoJogo.Texto(e.NomeCurto).Trim(),
                Div: dn.Length == 0 ? "" : System.Text.Encoding.Latin1.GetString(dn, 1, Math.Min(dn[0], dn.Length - 1)).Trim());
        }).ToList();
        string novo = $"{m.Ano}|{rodada}|" + string.Join("|", times.Select(x => $"{x.Uid}:{x.Time}:{x.Div}"));
        if (novo == _resumo) return;
        _resumo = novo;
        try
        {
            _sala.B.Atualizar($"{_sala.Raiz}/info", new JsonObject { ["ano"] = m.Ano, ["rodada"] = rodada }).GetAwaiter().GetResult();
            foreach (var x in times)
                _sala.B.Atualizar($"{_sala.Raiz}/jogadores/{x.Uid}", new JsonObject { ["time"] = x.Time, ["divisao"] = x.Div }).GetAwaiter().GetResult();
        }
        catch { }
    }

    (Equipa E, Jogo? Jogo) _ultimo;

    // a resposta a um comando que chega com a rodada ja em jogo
    public const string Fora = "a rodada ja comecou";

    // os comandos: o no inteiro ("/") ou um comando novo ("/<uid>/<chave>")
    void Comandos(string caminho, JsonNode? v)
    {
        var lista = new List<(string Uid, string Chave, JsonNode Valor)>();
        var partes = caminho.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length == 0)
            foreach (var (uid, cs) in Nos.Filhos(v))
                foreach (var (k, c) in Nos.Filhos(cs)) lista.Add((uid, k, c));
        else if (partes.Length == 1)
            foreach (var (k, c) in Nos.Filhos(v)) lista.Add((partes[0], k, c));
        else if (partes.Length == 2 && v != null) lista.Add((partes[0], partes[1], v));
        bool mudou = false;
        foreach (var (uid, chave, valor) in lista)
        {
            lock (_comandosVistos) if (!_comandosVistos.Add(uid + "/" + chave)) continue;
            Resposta r;
            try
            {
                var c = Comando.Ler((string?)valor["c"] ?? "");
                if (!_naBarreira && _naRodada && c is Parar or Trocar or Retomar)
                {
                    _filaAoVivo.Add((uid, chave, c));
                    continue;
                }
                if (!_naBarreira) r = Resposta.Recusado(Fora);
                else if (!_jogadores.TryGetValue(uid, out var j) || j.Equipa == 0) r = Resposta.Recusado("sem time");
                else if (c is Preparo p) { _preparos[j.Equipa] = p; r = Resposta.Feito; }
                else
                {
                    r = Resposta.Recusado("?");
                    _naInterface(() => r = _ex.Executar(j.Equipa, c));
                    mudou |= r.Ok;
                }
            }
            catch (Exception ex) { r = Resposta.Recusado("comando invalido: " + ex.Message); }
            try
            {
                _sala.B.Gravar($"{_sala.Raiz}/resultados/{uid}/{chave}", new JsonObject { ["ok"] = r.Ok, ["motivo"] = r.Motivo, ["versao"] = _versao + (mudou ? 1 : 0) }).GetAwaiter().GetResult();
                _sala.B.Gravar($"{_sala.Raiz}/comandos/{uid}/{chave}", null).GetAwaiter().GetResult();
            }
            catch { }
        }
        if (mudou) ConferirTransferencias();
        if (mudou && _ultimo.E != null) Publicar(_ultimo.E, _ultimo.Jogo);
    }

    // ---------------------------------------------------------------- a rodada ao vivo
    // (fase 3) o host transmite a jornada: o inicio e um tique por minuto (AoVivo), na ordem,
    // por uma fila propria (o motor nao espera a rede)
    readonly AoVivo.Emissor _emissor = new();
    readonly BlockingCollection<(string Caminho, JsonNode? Valor)> _envios = new();
    Thread? _enviador;

    void Enviar(string caminho, JsonNode? valor)
    {
        if (_enviador == null)
        {
            _enviador = new Thread(() =>
            {
                foreach (var (c, v) in _envios.GetConsumingEnumerable())
                    try { _sala.B.Gravar(c, v).GetAwaiter().GetResult(); } catch { }
            }) { IsBackground = true, Name = "online-envios" };
            _enviador.Start();
        }
        // (depois do Dispose, o host saiu: nada mais vai para o banco)
        try { if (!_envios.IsAddingCompleted) _envios.Add((caminho, valor)); }
        catch (InvalidOperationException) { }
    }

    // na thread do motor: o comeco da jornada (ComecoJornada) e cada minuto (Minuto)
    public void TransmitirInicio(byte[] titulo, Jornada jn)
    {
        _naRodada = true;
        while (_filaAoVivo.TryTake(out _)) { }
        ConferirTransferencias();
        Enviar($"{_sala.Raiz}/rodada/aoVivo", new JsonObject { ["rodada"] = Rodada, ["inicio"] = _emissor.Inicio(_j, titulo, jn) });
    }

    // (o host na velocidade dele: com a rede atrasada, o minuto nao vai e o proximo leva tudo o que
    // mudou desde o ultimo enviado (o Emissor manda as diferencas para o que ja foi); os outros pulam
    // minutos mas ficam em dia. forcar: o ultimo minuto da jornada vai sempre)
    public void TransmitirMinuto(Jornada jn, bool forcar = false)
    {
        if (!forcar && _envios.Count > 0) return;
        Enviar($"{_sala.Raiz}/rodada/aoVivo/tique", _emissor.Tique(jn));
    }

    // a disputa de penaltis da Taca (a janela do host): a mesma para os outros, com as cobrancas
    int _penaltis;
    public void TransmitirPenaltis(JogoAtual jg, Desempate d) =>
        Enviar($"{_sala.Raiz}/rodada/aoVivo/penaltis", new JsonObject
        {
            ["n"] = ++_penaltis, ["c"] = (int)jg.Dados.Casa, ["v"] = (int)jg.Dados.Fora, ["pv"] = d.PassaVisitante, ["rapido"] = d.Rapido,
            ["somas"] = new JsonArray(d.Somas.Select(x => (JsonNode)(int)x).ToArray()),
            ["golos"] = new JsonArray(d.Golos.Select(x => (JsonNode)(int)x).ToArray()),
            ["cobrancas"] = new JsonArray(d.Cobrancas.Select(c => (JsonNode)new JsonArray(c.Lado, (int)c.Batedor.ID, c.Resultado)).ToArray()),
        });

    // a velocidade da partida das regras da sala (Opcoes.Velocidade, 1 a 6)
    public int Velocidade { get; private set; } = Sala.VelocidadePadrao;

    // ---------------------------------------------------------------- as transferencias
    // Cada transferencia nova (as ultimas transferencias do jogo: leiloes, compras) vira um
    // aviso para todos (rodada/noticia: jogador, comprador, preco, tipo), que cada aparelho
    // mostra num aviso no canto, sem janela (no jogo online o leilao nao abre janela)
    readonly HashSet<Transferencia> _transfVistas = new(ReferenceEqualityComparer.Instance);
    int _noticia;
    public event Action<Transferencia, ushort>? Transferiu;

    // o time de cada jogador na ultima conferencia (quem vendeu)
    Dictionary<ushort, ushort> _timeDe = new();

    void MapearTimes()
    {
        _timeDe = new();
        foreach (var e in _j.M.S.Equipas) foreach (var p in e.Jogadores) _timeDe[p.ID] = e.ID;
    }

    // so as que envolvem um tecnico humano (comprador ou vendedor): as da CPU entre si ficam
    // so nas ultimas transferencias
    void ConferirTransferencias()
    {
        foreach (var t in _j.M.S.Transferencias.ToList())
        {
            if (!_transfVistas.Add(t)) continue;
            // (o leilao de todos ja mostrou o resultado)
            if (_leiloados.TryRemove(t.W06, out _)) continue;
            ushort vendedor = _timeDe.GetValueOrDefault(t.W06);
            bool humano(ushort id) => _j.M.Equipa(id) is { } e && MercadoJogo.HumanoAtivo(_j.M.TreinadorDa(e));
            if (!humano(t.W0A) && !humano(vendedor)) continue;
            Enviar($"{_sala.Raiz}/rodada/noticia", new JsonObject
            {
                ["n"] = ++_noticia, ["jogador"] = (int)t.W06, ["comprador"] = (int)t.W0A, ["vendedor"] = (int)vendedor, ["preco"] = (long)t.D0C, ["tipo"] = (int)t.B04,
            });
            Transferiu?.Invoke(t, vendedor);
        }
        MapearTimes();
    }

    // ---------------------------------------------------------------- as perguntas (fase 2)
    // As janelas do motor para um time de outro aparelho (aumento, lance em leilao da CPU,
    // convite, patrocinio, batedor do penalti, substituicao por lesao): a pergunta vai para o
    // dono (perguntas/<uid>), o motor espera a resposta (respostas/<uid>/<id>) ate o prazo; sem
    // resposta (ou ele ausente), o padrao do motor.
    // (tudo que para o jogo para todos tem 30 s: as perguntas, a paralisacao, o intervalo)
    public static TimeSpan PrazoPergunta = TimeSpan.FromSeconds(30);
    // o motor espera a resposta de outro tecnico (nome, tipo); null: acabou
    public event Action<string?, string>? AguardandoResposta;
    readonly ConcurrentDictionary<string, JsonNode?> _respostas = new();
    int _pergunta;

    void Respostas(string caminho, JsonNode? v)
    {
        var partes = caminho.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length == 0)
        {
            foreach (var (uid, rs) in Nos.Filhos(v))
                foreach (var (id, r) in Nos.Filhos(rs)) _respostas[uid + "/" + id] = r.DeepClone();
        }
        else if (partes.Length == 1) { foreach (var (id, r) in Nos.Filhos(v)) _respostas[partes[0] + "/" + id] = r.DeepClone(); }
        else if (partes.Length == 2 && v != null) _respostas[partes[0] + "/" + partes[1]] = v;
    }

    // na thread do motor: pergunta ao dono do time e espera; null = sem resposta
    public JsonNode? Perguntar(ushort equipa, string tipo, JsonObject dados, TimeSpan? prazo = null)
    {
        // (fora da barreira a lista dos jogadores pode estar velha)
        AtualizarJogadores();
        if (DonoDe(equipa) is not { } j || j.Uid == _sala.Uid || Ausente(j)) return null;
        string id = Interlocked.Increment(ref _pergunta).ToString("D6");
        dados["id"] = id;
        dados["tipo"] = tipo;
        dados["segundos"] = (int)(prazo ?? PrazoPergunta).TotalSeconds;
        // antes do primeiro retrato (os patrocinios do comeco do jogo) o outro aparelho ainda nao
        // tem o jogo: vai junto, para a janela mostrar os nomes
        if (_versao == 0) dados["save"] = AoVivo.Comprimir(_j.M.S.Gravar());
        try { _sala.B.Gravar($"{_sala.Raiz}/perguntas/{j.Uid}", dados).GetAwaiter().GetResult(); } catch { return null; }
        var fim = DateTime.UtcNow + (prazo ?? PrazoPergunta);
        JsonNode? r = null;
        string chave = j.Uid + "/" + id;
        // (a tela do host: "aguardando fulano responder", ate a resposta ou o prazo)
        AguardandoResposta?.Invoke(j.Nome, tipo);
        try
        {
            var conferido = DateTime.UtcNow;
            while (DateTime.UtcNow < fim && !_respostas.TryRemove(chave, out r))
            {
                // (quem saiu do jogo nao responde mais)
                if (DateTime.UtcNow - conferido > TimeSpan.FromSeconds(1)) { conferido = DateTime.UtcNow; if (!AindaNoJogo(j.Uid)) break; }
                Thread.Sleep(150);
            }
        }
        finally { AguardandoResposta?.Invoke(null, tipo); }
        try
        {
            _sala.B.Gravar($"{_sala.Raiz}/perguntas/{j.Uid}", null).GetAwaiter().GetResult();
            _sala.B.Gravar($"{_sala.Raiz}/respostas/{j.Uid}/{id}", null).GetAwaiter().GetResult();
        }
        catch { }
        return r;
    }

    // varias perguntas de uma vez (o leilao da CPU para todos os tecnicos): cada dono recebe a
    // dele, e todas ficam abertas ate todos responderem (ou o prazo; e a tarefa local, a janela
    // do host, terminar); so entao as janelas fecham. Sem resposta: null.
    public Dictionary<ushort, JsonNode?> PerguntarTodos(IReadOnlyList<ushort> equipas, string tipo, Func<ushort, JsonObject> dados, Task? local = null, TimeSpan? prazo = null)
    {
        AtualizarJogadores();
        var abertas = new List<(ushort Equipa, string Uid, string Id)>();
        var r = new Dictionary<ushort, JsonNode?>();
        // (antes do primeiro retrato os outros aparelhos ainda nao tem o jogo: vai junto)
        string? save = null;
        foreach (var eq in equipas)
        {
            r[eq] = null;
            if (DonoDe(eq) is not { } j || j.Uid == _sala.Uid || Ausente(j)) continue;
            var d = dados(eq);
            string id = Interlocked.Increment(ref _pergunta).ToString("D6");
            d["id"] = id; d["tipo"] = tipo; d["todos"] = true;
            d["segundos"] = (int)(prazo ?? PrazoPergunta).TotalSeconds;
            if (_versao == 0) d["save"] = save ??= AoVivo.Comprimir(_j.M.S.Gravar());
            try { _sala.B.Gravar($"{_sala.Raiz}/perguntas/{j.Uid}", d).GetAwaiter().GetResult(); } catch { continue; }
            abertas.Add((eq, j.Uid, id));
        }
        var fim = DateTime.UtcNow + (prazo ?? PrazoPergunta);
        var faltam = abertas.ToList();
        var conferido = DateTime.UtcNow;
        while (DateTime.UtcNow < fim && (faltam.Count > 0 || local is { IsCompleted: false }))
        {
            foreach (var a in faltam.ToList())
                if (_respostas.TryRemove(a.Uid + "/" + a.Id, out var x)) { r[a.Equipa] = x; faltam.Remove(a); }
            // (quem saiu do jogo nao responde mais: de segundo em segundo, sai da espera)
            if (DateTime.UtcNow - conferido > TimeSpan.FromSeconds(1))
            {
                conferido = DateTime.UtcNow;
                foreach (var a in faltam.ToList())
                    if (!AindaNoJogo(a.Uid)) faltam.Remove(a);
            }
            Thread.Sleep(150);
        }
        foreach (var a in abertas)
            try
            {
                _sala.B.Gravar($"{_sala.Raiz}/perguntas/{a.Uid}", null).GetAwaiter().GetResult();
                _sala.B.Gravar($"{_sala.Raiz}/respostas/{a.Uid}/{a.Id}", null).GetAwaiter().GetResult();
            }
            catch { }
        return r;
    }

    // o leilao de todos comecou (rodada/leilaoAberto): quem nao licita ve a espera ate o resultado
    public void AvisarLeilaoAberto(ushort jogador, ushort vendedor, IEnumerable<ushort> licitantes)
    {
        var uids = licitantes.Select(e => DonoDe(e)?.Uid).OfType<string>().ToList();
        Enviar($"{_sala.Raiz}/rodada/leilaoAberto", new JsonObject
        {
            ["n"] = _leilao + 1, ["jogador"] = (int)jogador, ["vendedor"] = (int)vendedor,
            ["licitam"] = new JsonArray(uids.Select(u => (JsonNode)u).ToArray()),
        });
    }

    // o resultado do leilao de todos (rodada/leilao): o aviso em cada aparelho (vendido: para quem
    // e por quanto; senao, sem ofertas). A transferencia dele nao vira outro aviso
    int _leilao;
    readonly ConcurrentDictionary<ushort, byte> _leiloados = new();
    public void AvisarLeilao(ushort jogador, ushort vendedor, ushort comprador, int preco)
    {
        if (comprador != 0) _leiloados[jogador] = 0;
        Enviar($"{_sala.Raiz}/rodada/leilao", new JsonObject
        {
            ["n"] = ++_leilao, ["jogador"] = (int)jogador, ["vendedor"] = (int)vendedor, ["comprador"] = (int)comprador, ["preco"] = (long)preco,
        });
    }

    // o dono ainda esta no jogo (nao saiu, nao desistiu, nao foi expulso)
    bool AindaNoJogo(string uid)
    {
        try
        {
            var e = _sala.B.Ler($"{_sala.Raiz}/jogadores/{uid}/estado").GetAwaiter().GetResult();
            return e != null && (string?)e is not (Estados.Saiu or Estados.Desistiu or Estados.Expulso);
        }
        catch { return true; }
    }

    // ---------------------------------------------------------------- a paralisacao
    // Quem pede (o Parar de outro aparelho, ou o host na tela dele) para o jogo de todos: a
    // pausa vai para o banco (rodada/aoVivo/pausa: quem e o time), os outros veem "fulano esta
    // fazendo substituicoes" e o motor espera as trocas (Trocar) e o Retomar dele, ou o prazo.
    // O intervalo dos times dos outros aparelhos usa o mesmo caminho (as trocas do intervalo).
    public static TimeSpan PrazoPausa = TimeSpan.FromSeconds(30);
    volatile bool _naRodada;
    readonly BlockingCollection<(string Uid, string Chave, Comando C)> _filaAoVivo = new();

    void Responder(string uid, string chave, Resposta r)
    {
        try
        {
            _sala.B.Gravar($"{_sala.Raiz}/resultados/{uid}/{chave}", new JsonObject { ["ok"] = r.Ok, ["motivo"] = r.Motivo, ["versao"] = _versao }).GetAwaiter().GetResult();
            _sala.B.Gravar($"{_sala.Raiz}/comandos/{uid}/{chave}", null).GetAwaiter().GetResult();
        }
        catch { }
    }

    JogadorSala? DonoDe(ushort equipa) => _jogadores.Values.FirstOrDefault(x => x.Equipa == equipa);

    // na thread do motor, a cada minuto (antes de transmitir): os pedidos de parar
    public void NoMinuto(Jornada jn)
    {
        while (_filaAoVivo.TryTake(out var p))
        {
            if (p.C is not Parar) { Responder(p.Uid, p.Chave, Resposta.Recusado("o jogo nao esta parado")); continue; }
            Responder(p.Uid, p.Chave, Resposta.Feito);
            if (!_jogadores.TryGetValue(p.Uid, out var j) || j.Equipa == 0) continue;
            var jg = jn.Jogos.FirstOrDefault(x => x.Dados.Casa == j.Equipa || x.Dados.Fora == j.Equipa);
            if (jg == null) continue;
            int lado = jg.Dados.Casa == j.Equipa ? Lado.Casa : Lado.Visit;
            jn.ObservarTrocas(jg, lado, null, false, () => Pausa(jn, jg, lado, j, 1));
            TransmitirMinuto(jn);
        }
    }

    // na thread do motor: o intervalo (ou o goleiro expulso) de um time de outro aparelho
    public void SubstituicaoRemota(Jornada? jn, JogoAtual jg, int lado, int motivo)
    {
        if (jg.Equipa[lado] is not { } e || DonoDe(e.ID) is not { } j) return;
        Pausa(jn, jg, lado, j, motivo);
    }

    // a pausa comecou (o nome de quem para) ou acabou (null): o aviso na tela do host
    public event Action<string?>? PausaMudou;

    void Pausa(Jornada? jn, JogoAtual jg, int lado, JogadorSala j, int motivo)
    {
        var e = jg.Equipa[lado]!;
        PausaMudou?.Invoke(j.Nome);
        Enviar($"{_sala.Raiz}/rodada/aoVivo/pausa", new JsonObject
        {
            ["uid"] = j.Uid, ["nome"] = j.Nome, ["equipa"] = (int)e.ID, ["motivo"] = motivo, ["segundos"] = (int)PrazoPausa.TotalSeconds,
        });
        var fim = DateTime.UtcNow + PrazoPausa;
        while (DateTime.UtcNow < fim)
        {
            if (!_filaAoVivo.TryTake(out var p, 500)) continue;
            if (p.Uid != j.Uid)
            {
                // (outro pediu para parar: depois desta pausa)
                if (p.C is Parar) { _pendentes.Add(p); continue; }
                Responder(p.Uid, p.Chave, Resposta.Recusado("o jogo esta parado por outro tecnico"));
                continue;
            }
            if (p.C is Trocar t)
            {
                var r = ExecutorComandos.Substituir(e, t.Saiu, t.Entra, motivo);
                Responder(p.Uid, p.Chave, r);
                if (r.Ok && jn != null) TransmitirMinuto(jn);
                continue;
            }
            Responder(p.Uid, p.Chave, Resposta.Feito);
            if (p.C is Retomar) break;
        }
        Enviar($"{_sala.Raiz}/rodada/aoVivo/pausa", null);
        PausaMudou?.Invoke(null);
        foreach (var x in _pendentes) _filaAoVivo.Add(x);
        _pendentes.Clear();
    }

    readonly List<(string Uid, string Chave, Comando C)> _pendentes = new();

    // ---------------------------------------------------------------- o intervalo
    // No online o intervalo e de todos ao mesmo tempo: a pausa vai para todos (coletivo: os
    // times dos outros aparelhos que jogam), cada um abre a janela das trocas, o host abre a
    // dele (janelaLocal) e todos tem o mesmo prazo; acabou (ou todos deram Continuar), segue.
    // As trocas do time do gancho que chamou (chamador) ja sao observadas pela Jornada; as dos
    // outros, aqui (ObservarTrocas), para a linha do tempo.
    public void IntervaloColetivo(Jornada jn, JogoAtual chamador, int ladoChamador, Action? janelaLocal, TimeSpan prazo)
    {
        AtualizarJogadores();
        var remotos = new Dictionary<string, (JogoAtual Jg, int Lado)>();
        foreach (var jg in jn.Jogos)
            for (int l = 0; l < 2; l++)
                if (jg.Equipa[l] is { } e && Remota(e) && DonoDe(e.ID) is { } j && !Ausente(j)) remotos[j.Uid] = (jg, l);
        var fim = DateTime.UtcNow + prazo;
        Enviar($"{_sala.Raiz}/rodada/aoVivo/pausa", new JsonObject
        {
            ["uid"] = "*", ["coletivo"] = true, ["motivo"] = 4, ["segundos"] = (int)prazo.TotalSeconds,
            ["equipas"] = new JsonArray(remotos.Values.Select(x => (JsonNode)(int)x.Jg.Equipa[x.Lado]!.ID).ToArray()),
        });
        PausaMudou?.Invoke("*");
        janelaLocal?.Invoke();
        var faltam = new HashSet<string>(remotos.Keys);
        while (DateTime.UtcNow < fim && faltam.Count > 0)
        {
            if (!_filaAoVivo.TryTake(out var p, 300)) continue;
            if (!remotos.TryGetValue(p.Uid, out var alvo)) { Responder(p.Uid, p.Chave, Resposta.Recusado("intervalo")); continue; }
            switch (p.C)
            {
                case Trocar t:
                {
                    var e = alvo.Jg.Equipa[alvo.Lado]!;
                    Resposta r = Resposta.Recusado("?");
                    if (alvo.Jg == chamador && alvo.Lado == ladoChamador) r = ExecutorComandos.Substituir(e, t.Saiu, t.Entra, 4);
                    else jn.ObservarTrocas(alvo.Jg, alvo.Lado, null, true, () => r = ExecutorComandos.Substituir(e, t.Saiu, t.Entra, 4));
                    Responder(p.Uid, p.Chave, r);
                    if (r.Ok) TransmitirMinuto(jn);
                    break;
                }
                case Retomar:
                    faltam.Remove(p.Uid);
                    Responder(p.Uid, p.Chave, Resposta.Feito);
                    break;
                default:
                    Responder(p.Uid, p.Chave, Resposta.Feito);
                    break;
            }
        }
        Enviar($"{_sala.Raiz}/rodada/aoVivo/pausa", null);
        PausaMudou?.Invoke(null);
    }

    // a pausa simples do host (o botao de pausa da jornada): a partida de todos para e os outros
    // veem "fulano pausou a partida" (motivo 0: sem janela de trocas para ninguem)
    public void AvisarPausaSimples(bool comecou)
    {
        if (!comecou) { Enviar($"{_sala.Raiz}/rodada/aoVivo/pausa", null); return; }
        var j = _jogadores.GetValueOrDefault(_sala.Uid);
        Enviar($"{_sala.Raiz}/rodada/aoVivo/pausa", new JsonObject { ["uid"] = _sala.Uid, ["nome"] = j?.Nome ?? "Host", ["motivo"] = 0 });
    }

    // a pausa do proprio host (a janela de detalhes dele): o aviso para os outros
    public void AvisarPausa(ushort equipa, bool comecou)
    {
        if (!comecou) { Enviar($"{_sala.Raiz}/rodada/aoVivo/pausa", null); return; }
        var j = _jogadores.GetValueOrDefault(_sala.Uid);
        Enviar($"{_sala.Raiz}/rodada/aoVivo/pausa", new JsonObject { ["uid"] = _sala.Uid, ["nome"] = j?.Nome ?? "Host", ["equipa"] = (int)equipa, ["motivo"] = 1 });
    }

    // ---------------------------------------------------------------- passar o host
    // O host que precisa sair passa o host para outro tecnico (so na preparacao, com o motor
    // parado na barreira): publica o retrato de agora e grava o novo host na sala. O motor deste
    // aparelho sai (o gancho devolve "sair"); o aparelho do outro carrega o retrato e continua
    // dali (ClienteOnline/JogoRemoto). Quem tinha mandado o Preparo prepara de novo (o host novo
    // recomeca as versoes).
    public event Action<string>? HostPassado;

    public void PassarHost(string uid) => _fila.Add(() =>
    {
        if (!_naBarreira || _ultimo.E == null || uid == _sala.Uid || !_jogadores.ContainsKey(uid)) return;
        Publicar(_ultimo.E, _ultimo.Jogo);
        try { _sala.B.Gravar($"{_sala.Raiz}/host", JsonValue.Create(uid)).GetAwaiter().GetResult(); }
        catch { return; }
        _sair = true;
        Passou = true;
        HostPassado?.Invoke(uid);
    });

    // este aparelho passou o host (o motor saiu por isso, nao porque o host saiu do jogo)
    public bool Passou { get; private set; }

    public void Dispose()
    {
        _envios.CompleteAdding();
        _vida.Dispose();
        foreach (var o in _observando) o.Dispose();
    }
}
