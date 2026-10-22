using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RetroFoot.Motor.Online;

// O banco da sala (docs/multiplayer-online.md): uma arvore JSON com caminhos "salas/ABC123/...",
// no modelo do Firebase Realtime Database (que e o de verdade, BancoFirebase, por REST e SSE).
// BancoMemoria e o mesmo num processo so, para os testes e para jogar na mesma maquina.
public interface IBanco
{
    Task<JsonNode?> Ler(string caminho, CancellationToken ct = default);
    // PUT: troca o no (null apaga)
    Task Gravar(string caminho, JsonNode? valor, CancellationToken ct = default);
    // PATCH: troca so os filhos dados
    Task Atualizar(string caminho, JsonObject filhos, CancellationToken ct = default);
    // POST: um filho novo com chave unica, em ordem de criacao; devolve a chave
    Task<string> Acrescentar(string caminho, JsonNode valor, CancellationToken ct = default);
    // a escrita condicional (ETag do Firebase): so grava se o no ainda for o lido
    Task<(JsonNode? Valor, string Etag)> LerComEtag(string caminho, CancellationToken ct = default);
    Task<bool> GravarSe(string caminho, JsonNode? valor, string etag, CancellationToken ct = default);
    // as mudancas debaixo do caminho (o SSE do Firebase): primeiro o no inteiro com o caminho
    // relativo "/", depois cada mudanca com o caminho relativo dela
    IDisposable Observar(string caminho, Action<string, JsonNode?> mudou);
}

public static class Nos
{
    // os filhos de um no como (chave, valor). O Firebase devolve como lista um no cujas chaves
    // sao 0, 1, 2... (as vagas da sala): os dois formatos, sem os buracos (null)
    public static IEnumerable<(string Chave, JsonNode Valor)> Filhos(JsonNode? n)
    {
        if (n is JsonObject o)
            foreach (var (k, v) in o) { if (v != null) yield return (k, v); }
        else if (n is JsonArray a)
            for (int i = 0; i < a.Count; i++) { if (a[i] is { } v) yield return (i.ToString(), v); }
    }
}

public sealed class BancoMemoria : IBanco
{
    readonly object _trava = new();
    JsonObject _raiz = new();
    readonly List<(string Caminho, Action<string, JsonNode?> Mudou)> _observadores = new();
    long _contador;

    static string[] Partes(string caminho) => caminho.Split('/', StringSplitOptions.RemoveEmptyEntries);
    static string Normal(string caminho) => "/" + string.Join('/', Partes(caminho));

    JsonNode? No(string caminho)
    {
        JsonNode? n = _raiz;
        foreach (var p in Partes(caminho))
        {
            if (n is not JsonObject o || !o.TryGetPropertyValue(p, out n)) return null;
        }
        return n;
    }

    void Por(string caminho, JsonNode? valor)
    {
        var ps = Partes(caminho);
        if (ps.Length == 0) { _raiz = valor as JsonObject ?? new JsonObject(); return; }
        var o = _raiz;
        for (int i = 0; i < ps.Length - 1; i++)
        {
            if (o[ps[i]] is not JsonObject f)
            {
                if (valor == null) return;
                f = new JsonObject();
                o[ps[i]] = f;
            }
            o = f;
        }
        if (valor == null) o.Remove(ps[^1]);
        else o[ps[^1]] = valor.DeepClone();
        // (como o Firebase: nos vazios somem)
        Podar(_raiz);
    }

    static void Podar(JsonObject o)
    {
        var vazios = new List<string>();
        foreach (var (k, v) in o)
            if (v is JsonObject f) { Podar(f); if (f.Count == 0) vazios.Add(k); }
        foreach (var k in vazios) o.Remove(k);
    }

    static string Etag(JsonNode? n) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(n?.ToJsonString() ?? "null")));

    // avisa quem observa um caminho que contem o mudado ou esta dentro dele
    void Avisar(string mudado)
    {
        List<(string, Action<string, JsonNode?>)> obs;
        lock (_trava) obs = new(_observadores);
        mudado = Normal(mudado);
        foreach (var (c, f) in obs)
        {
            if (mudado == c || mudado.StartsWith(c == "/" ? "/" : c + "/", StringComparison.Ordinal))
            {
                string rel = c == "/" ? mudado : mudado.Length == c.Length ? "/" : mudado[c.Length..];
                JsonNode? v;
                lock (_trava) v = No(mudado)?.DeepClone();
                f(rel, v);
            }
            else if (c.StartsWith(mudado == "/" ? "/" : mudado + "/", StringComparison.Ordinal))
            {
                JsonNode? v;
                lock (_trava) v = No(c)?.DeepClone();
                f("/", v);
            }
        }
    }

    public Task<JsonNode?> Ler(string caminho, CancellationToken ct = default)
    {
        lock (_trava) return Task.FromResult(No(caminho)?.DeepClone());
    }

    public Task Gravar(string caminho, JsonNode? valor, CancellationToken ct = default)
    {
        lock (_trava) Por(caminho, valor);
        Avisar(caminho);
        return Task.CompletedTask;
    }

    public Task Atualizar(string caminho, JsonObject filhos, CancellationToken ct = default)
    {
        lock (_trava)
            foreach (var (k, v) in filhos) Por(caminho.TrimEnd('/') + "/" + k, v);
        foreach (var (k, _) in filhos) Avisar(caminho.TrimEnd('/') + "/" + k);
        return Task.CompletedTask;
    }

    public async Task<string> Acrescentar(string caminho, JsonNode valor, CancellationToken ct = default)
    {
        // (as chaves do Firebase ordenam pela hora; aqui um contador com zeros a esquerda)
        string chave = "-" + Interlocked.Increment(ref _contador).ToString("D12");
        await Gravar(caminho.TrimEnd('/') + "/" + chave, valor, ct);
        return chave;
    }

    public Task<(JsonNode? Valor, string Etag)> LerComEtag(string caminho, CancellationToken ct = default)
    {
        lock (_trava)
        {
            var n = No(caminho)?.DeepClone();
            return Task.FromResult((n, Etag(n)));
        }
    }

    public Task<bool> GravarSe(string caminho, JsonNode? valor, string etag, CancellationToken ct = default)
    {
        lock (_trava)
        {
            if (Etag(No(caminho)) != etag) return Task.FromResult(false);
            Por(caminho, valor);
        }
        Avisar(caminho);
        return Task.FromResult(true);
    }

    public IDisposable Observar(string caminho, Action<string, JsonNode?> mudou)
    {
        var c = Normal(caminho);
        var item = (c, mudou);
        JsonNode? agora;
        lock (_trava)
        {
            _observadores.Add(item);
            agora = No(c)?.DeepClone();
        }
        mudou("/", agora);
        return new Saida(() => { lock (_trava) _observadores.Remove(item); });
    }

    sealed class Saida(Action fim) : IDisposable
    {
        public void Dispose() => fim();
    }
}
