using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
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
        _observando.Add(sala.B.Observar($"{sala.Raiz}/comandos", (c, v) => _fila.Add(() => Comandos(c, v))));
        _vida = new Timer(_ => _ = Vida(), null, 0, 10_000);
    }

    async Task Vida()
    {
        try { await _sala.B.Gravar($"{_sala.Raiz}/hostVivo", new JsonObject { [".sv"] = "timestamp" }); } catch { }
    }

    // um time humano que nao e o do host (de outro aparelho, mesmo que o dono tenha saido): as
    // perguntas dele nao abrem janela no host
    public bool Remota(Equipa? e) =>
        e != null && e.ID != TimeDoHost && MercadoJogo.HumanoAtivo(_j.M.TreinadorDa(e));

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
        if (_sair) return true;
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
        Rodada = id;
        _preparos.Clear();
        _jogam = _j.M.S.Jogos.SelectMany(x => new[] { x.Casa, x.Fora }).Where(x => x != 0).ToHashSet();
        _localPronto = TimeDoHost is not ushort th || PrepararLocal == null || !_jogam.Contains(th);
        // (quem estava PRONTO para um host que caiu prepara de novo: o Preparo se perdeu)
        foreach (var j in _jogadores.Values.Where(x => x.Uid != _sala.Uid && x.Estado == Estados.Pronto))
            try { _sala.B.Atualizar($"{_sala.Raiz}/jogadores/{j.Uid}", new JsonObject { ["estado"] = Estados.Preparando }).GetAwaiter().GetResult(); } catch { }
        AtualizarJogadores();
        Publicar(e, jogo);
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
        while (!TodosProntos() && !_sair) _fila.Take()();
    }

    bool TodosProntos() =>
        _localPronto && _jogadores.Values.Where(x => x.Equipa != 0 && x.Uid != _sala.Uid && _jogam.Contains(x.Equipa) && x.Estado != Estados.Saiu).All(x => x.Estado == Estados.Pronto && x.Rodada == Rodada);

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
            if (!_comandosVistos.Add(uid + "/" + chave)) continue;
            Resposta r;
            try
            {
                var c = Comando.Ler((string?)valor["c"] ?? "");
                if (!_jogadores.TryGetValue(uid, out var j) || j.Equipa == 0) r = Resposta.Recusado("sem time");
                else if (Rodada.Length == 0) r = Resposta.Recusado("fora da preparacao");
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
        if (mudou && _ultimo.E != null) Publicar(_ultimo.E, _ultimo.Jogo);
    }

    public void Dispose()
    {
        _vida.Dispose();
        foreach (var o in _observando) o.Dispose();
    }
}
