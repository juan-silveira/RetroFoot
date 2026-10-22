using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RetroFoot.Motor.Online;

// O lado de quem joga num aparelho que nao e o host (docs/multiplayer-online.md): recebe o
// retrato de cada rodada (e o novo, depois de um comando que muda o jogo), manda os comandos e
// espera a resposta do host, e marca PRONTO com o Preparo do time.
public sealed class ClienteOnline : IDisposable
{
    readonly Sala _sala;
    readonly List<IDisposable> _observando = new();
    readonly ConcurrentDictionary<string, TaskCompletionSource<Resposta>> _esperando = new();
    readonly ConcurrentDictionary<string, Resposta> _chegaram = new();
    int _versao;

    public string Rodada { get; private set; } = "";
    // um retrato novo (rodada nova, ou a mesma com o jogo mudado): na thread do banco
    public event Action<Retrato, string, int>? NovoRetrato;

    public ClienteOnline(Sala sala)
    {
        _sala = sala;
        _observando.Add(sala.B.Observar($"{sala.Raiz}/retrato", (c, v) => _ = Retrato(c, v)));
        _observando.Add(sala.B.Observar($"{sala.Raiz}/resultados/{sala.Uid}", Resultados));
    }

    async Task Retrato(string caminho, JsonNode? v)
    {
        try
        {
            if (caminho != "/") v = await _sala.B.Ler($"{_sala.Raiz}/retrato");
            if (v is not JsonObject o || (string?)o["dados"] is not { } dados) return;
            int versao = (int?)o["versao"] ?? 0;
            string rodada = (string?)o["rodada"] ?? "";
            lock (this)
            {
                if (versao <= _versao) return;
                _versao = versao;
                Rodada = rodada;
            }
            NovoRetrato?.Invoke(Online.Retrato.Ler(Convert.FromBase64String(dados)), rodada, versao);
        }
        catch (Exception ex) { Console.Error.WriteLine("retrato: " + ex.Message); }
    }

    void Resultados(string caminho, JsonNode? v)
    {
        var l = new List<(string, JsonNode)>();
        var partes = caminho.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (partes.Length == 0) foreach (var x in Nos.Filhos(v)) l.Add(x);
        else if (partes.Length == 1 && v != null) l.Add((partes[0], v));
        foreach (var (chave, r) in l)
        {
            var resp = new Resposta((bool?)r["ok"] ?? false, (string?)r["motivo"] ?? "");
            _chegaram[chave] = resp;
            if (_esperando.TryRemove(chave, out var t)) t.TrySetResult(resp);
            _ = _sala.B.Gravar($"{_sala.Raiz}/resultados/{_sala.Uid}/{chave}", null);
        }
    }

    // manda o comando e espera a resposta do host (o host executa na hora)
    public async Task<Resposta> Executar(Comando c, TimeSpan? prazo = null, CancellationToken ct = default)
    {
        var chave = await _sala.Mandar(c, ct);
        if (_chegaram.TryRemove(chave, out var ja)) return ja;
        var t = _esperando.GetOrAdd(chave, _ => new TaskCompletionSource<Resposta>(TaskCreationOptions.RunContinuationsAsynchronously));
        if (_chegaram.TryRemove(chave, out ja)) { _esperando.TryRemove(chave, out _); return ja; }
        var fim = await Task.WhenAny(t.Task, Task.Delay(prazo ?? TimeSpan.FromSeconds(30), ct));
        if (fim != t.Task) { _esperando.TryRemove(chave, out _); return Resposta.Recusado("o host nao respondeu"); }
        return await t.Task;
    }

    // o fim da preparacao: o Preparo e o PRONTO desta rodada
    public async Task<Resposta> Pronto(Preparo p, CancellationToken ct = default)
    {
        string rodada = Rodada;
        var r = await Executar(p, null, ct);
        if (r.Ok) await _sala.MudarEstado(Estados.Pronto, rodada, ct);
        return r;
    }

    public void Dispose()
    {
        foreach (var o in _observando) o.Dispose();
    }
}
