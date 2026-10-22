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

    public HostOnline(Sala sala, JogoEmCurso j, Action<Action>? naInterface = null)
    {
        _sala = sala; _j = j;
        _ex = new ExecutorComandos(j);
        _naInterface = naInterface ?? (a => a());
        _observando.Add(sala.B.Observar($"{sala.Raiz}/jogadores", (_, _) => _fila.Add(AtualizarJogadores)));
        // (fora da preparacao o motor esta jogando: os comandos sao recusados na hora, sem fila)
        _observando.Add(sala.B.Observar($"{sala.Raiz}/comandos", (c, v) => { if (_naBarreira) _fila.Add(() => Comandos(c, v)); else Comandos(c, v); }));
        _observando.Add(sala.B.Observar($"{sala.Raiz}/respostas", Respostas));
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
            if (ModoAmigo && !Passou) TirarConvidados();
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
        Ferias();
        ConferirTransferencias();
        _naRodada = false;
        _naBarreira = true;
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
        while (!TodosProntos() && !_sair)
        {
            // (de segundo em segundo, mesmo sem novidade: o prazo e quem sumiu)
            if (_fila.TryTake(out var a, 1000)) a();
            if (_jogadores.Values.Any(x => x.Uid != _sala.Uid && (Ausente(x) || (ModoAmigo && x.Equipa == 0))))
            {
                Ferias();
                // (no modo amigo, quem chegou recebe o jogo com o time dele)
                if (ModoAmigo && _ultimo.E != null) Publicar(_ultimo.E, _ultimo.Jogo);
            }
            ConferirPrazo();
            // (as vendas e compras do host, na tela dele: o aviso para os outros na hora)
            _naInterface(ConferirTransferencias);
        }
        _naBarreira = false;
        if (Passou) return;
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

    bool AlguemPronto() =>
        (_localPronto && _hostJoga) || _jogadores.Values.Any(x => x.Uid != _sala.Uid && x.Estado == Estados.Pronto && x.Rodada == Rodada);

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
    public event Action? PrazoAcabou;

    void ConferirPrazo()
    {
        if (_limite == null)
        {
            if (!AlguemPronto() || TodosProntos()) return;
            _limite = DateTime.UtcNow + _prazo;
            try { _sala.B.Gravar($"{_sala.Raiz}/rodada/prazo", new JsonObject { ["rodada"] = Rodada, ["segundos"] = (int)_prazo.TotalSeconds }).GetAwaiter().GetResult(); } catch { }
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
        if (j.Estado == Estados.Saiu) return true;
        if (!_vistos.TryGetValue(j.Uid, out var v) || v.Visto != j.Visto) { _vistos[j.Uid] = (j.Visto, DateTime.UtcNow); return false; }
        return DateTime.UtcNow - v.Quando > PrazoAusente;
    }

    // ---------------------------------------------------------------- o modo amigo
    // (Online/Convidado) um save single player com amigos: quem entra assume um time da 4a
    // divisao; quem sai (ou some) e despedido e sai do jogo (em vez das ferias); no fim do jogo
    // online todos os convidados saem e o save volta a ser so do host
    public bool ModoAmigo { get; init; }
    readonly Dictionary<string, ushort> _convidados = new();   // uid -> o tecnico dele
    public event Action<string, bool>? ConvidadoMudou;           // nome, entrou

    void AmigosEntramESaem()
    {
        // quem chegou: um time da 4a divisao (o sorteio nao gasta o Random do jogo)
        foreach (var j in _jogadores.Values.Where(x => x.Uid != _sala.Uid && x.Equipa == 0 && x.Estado != Estados.Saiu).ToList())
        {
            (Treinador T, Equipa E)? r = null;
            _naInterface(() => r = Convidado.Entrar(_j, j.Nome, new Aleatorio((uint)Environment.TickCount)));
            if (r is not { } ok) continue;
            _convidados[j.Uid] = ok.T.ID;
            try { _sala.DarEquipa(j.Uid, ok.E.ID).GetAwaiter().GetResult(); } catch { }
            ConvidadoMudou?.Invoke(j.Nome, true);
        }
        // quem saiu ou sumiu: despedido e fora do jogo; a vaga fica livre (voltando, e outro tecnico)
        foreach (var j in _jogadores.Values.Where(x => x.Uid != _sala.Uid && x.Equipa != 0 && Ausente(x)).ToList())
        {
            if (_j.M.Equipa(j.Equipa) is { } e && _j.M.TreinadorDa(e) is { Humano: not 0 } t && t.ID != (_j.M.Equipa(TimeDoHost ?? 0)?.Treinador ?? 0))
                _naInterface(() => Convidado.Sair(_j, t.ID));
            _convidados.Remove(j.Uid);
            try
            {
                _sala.B.Gravar($"{_sala.Raiz}/jogadores/{j.Uid}", null).GetAwaiter().GetResult();
                if (j.Vaga >= 0) _sala.B.Gravar($"{_sala.Raiz}/vagas/{j.Vaga}", null).GetAwaiter().GetResult();
            }
            catch { }
            ConvidadoMudou?.Invoke(j.Nome, false);
        }
        AtualizarJogadores();
    }

    // o fim do jogo online no modo amigo: todos os convidados saem
    public void TirarConvidados()
    {
        foreach (var j in _jogadores.Values.Where(x => x.Uid != _sala.Uid && x.Equipa != 0).ToList())
            if (_j.M.Equipa(j.Equipa) is { } e && _j.M.TreinadorDa(e) is { Humano: not 0 } t && e.ID != TimeDoHost)
                _naInterface(() => Convidado.Sair(_j, t.ID));
    }

    // na barreira: as ferias de quem esta ausente, e o fim delas para quem voltou
    void Ferias()
    {
        if (ModoAmigo) { AmigosEntramESaem(); return; }
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
        _envios.Add((caminho, valor));
    }

    // na thread do motor: o comeco da jornada (ComecoJornada) e cada minuto (Minuto)
    public void TransmitirInicio(byte[] titulo, Jornada jn)
    {
        _naRodada = true;
        while (_filaAoVivo.TryTake(out _)) { }
        ConferirTransferencias();
        Enviar($"{_sala.Raiz}/rodada/aoVivo", new JsonObject { ["rodada"] = Rodada, ["inicio"] = _emissor.Inicio(_j, titulo, jn) });
    }

    public void TransmitirMinuto(Jornada jn) => Enviar($"{_sala.Raiz}/rodada/aoVivo/tique", _emissor.Tique(jn));

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
    public static TimeSpan PrazoPergunta = TimeSpan.FromSeconds(45);
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
        string id = (++_pergunta).ToString("D6");
        dados["id"] = id;
        dados["tipo"] = tipo;
        dados["segundos"] = (int)(prazo ?? PrazoPergunta).TotalSeconds;
        try { _sala.B.Gravar($"{_sala.Raiz}/perguntas/{j.Uid}", dados).GetAwaiter().GetResult(); } catch { return null; }
        var fim = DateTime.UtcNow + (prazo ?? PrazoPergunta);
        JsonNode? r = null;
        string chave = j.Uid + "/" + id;
        while (DateTime.UtcNow < fim && !_respostas.TryRemove(chave, out r)) Thread.Sleep(150);
        try
        {
            _sala.B.Gravar($"{_sala.Raiz}/perguntas/{j.Uid}", null).GetAwaiter().GetResult();
            _sala.B.Gravar($"{_sala.Raiz}/respostas/{j.Uid}/{id}", null).GetAwaiter().GetResult();
        }
        catch { }
        return r;
    }

    // ---------------------------------------------------------------- a paralisacao
    // Quem pede (o Parar de outro aparelho, ou o host na tela dele) para o jogo de todos: a
    // pausa vai para o banco (rodada/aoVivo/pausa: quem e o time), os outros veem "fulano esta
    // fazendo substituicoes" e o motor espera as trocas (Trocar) e o Retomar dele, ou o prazo.
    // O intervalo dos times dos outros aparelhos usa o mesmo caminho (as trocas do intervalo).
    public static TimeSpan PrazoPausa = TimeSpan.FromSeconds(60);
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
