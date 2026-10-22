using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RetroFoot.Motor.Online;

// O projeto Firebase do RetroFoot (gratuito, plano Spark): o Realtime Database e a chave da Web
// API (nao e segredo; a protecao sao as regras do banco, docs/firebase-regras.json)
public static class ConfigFirebase
{
    public const string Banco = "https://retrofootapp-default-rtdb.firebaseio.com";
    public const string Chave = "AIzaSyAc5RNhR-6u0uxIXZBx6xII6Y1uhvAg15o";
}

// A identidade anonima do aparelho no Firebase Auth (sem login para o jogador): criada uma vez
// (accounts:signUp) e guardada no arquivo; o token de 1 hora e renovado com o refresh token
public sealed class IdentidadeFirebase
{
    readonly HttpClient _http;
    readonly string _chave;
    readonly string? _arquivo;
    readonly SemaphoreSlim _trava = new(1, 1);
    string? _refresh, _token;
    DateTime _expira;

    public string Uid { get; private set; } = "";

    public IdentidadeFirebase(HttpClient http, string? arquivo, string chave = ConfigFirebase.Chave)
    {
        _http = http; _arquivo = arquivo; _chave = chave;
        try
        {
            if (arquivo != null && File.Exists(arquivo) && JsonNode.Parse(File.ReadAllText(arquivo)) is JsonObject o)
            {
                Uid = (string?)o["uid"] ?? "";
                _refresh = (string?)o["refresh"];
            }
        }
        catch { }
    }

    public async Task<string> Token(CancellationToken ct = default)
    {
        await _trava.WaitAsync(ct);
        try
        {
            if (_token != null && DateTime.UtcNow < _expira) return _token;
            JsonObject r;
            if (_refresh != null)
            {
                try
                {
                    r = await Post($"https://securetoken.googleapis.com/v1/token?key={_chave}",
                        new FormUrlEncodedContent(new[] { KeyValuePair.Create("grant_type", "refresh_token"), KeyValuePair.Create("refresh_token", _refresh) }), ct);
                    _token = (string?)r["id_token"];
                    _refresh = (string?)r["refresh_token"] ?? _refresh;
                    Uid = (string?)r["user_id"] ?? Uid;
                    _expira = DateTime.UtcNow.AddSeconds(int.Parse((string?)r["expires_in"] ?? "3600") - 300);
                    Guardar();
                    return _token!;
                }
                catch (HttpRequestException) { throw; }
                catch { _refresh = null; }   // a identidade sumiu: uma nova
            }
            r = await Post($"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={_chave}",
                new StringContent("""{"returnSecureToken":true}""", Encoding.UTF8, "application/json"), ct);
            _token = (string?)r["idToken"];
            _refresh = (string?)r["refreshToken"];
            Uid = (string?)r["localId"] ?? "";
            _expira = DateTime.UtcNow.AddSeconds(int.Parse((string?)r["expiresIn"] ?? "3600") - 300);
            Guardar();
            return _token!;
        }
        finally { _trava.Release(); }
    }

    public void Esquecer() { _token = null; }

    async Task<JsonObject> Post(string url, HttpContent corpo, CancellationToken ct)
    {
        using var resp = await _http.PostAsync(url, corpo, ct);
        var txt = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"auth {(int)resp.StatusCode}: {txt}");
        return JsonNode.Parse(txt) as JsonObject ?? throw new InvalidOperationException("auth: resposta vazia");
    }

    void Guardar()
    {
        if (_arquivo == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_arquivo)!);
            File.WriteAllText(_arquivo, new JsonObject { ["uid"] = Uid, ["refresh"] = _refresh }.ToJsonString());
        }
        catch { }
    }
}

// O banco da sala no Firebase Realtime Database, pela API REST (GET/PUT/PATCH/POST/DELETE em
// <caminho>.json?auth=<token>; ETag e if-match para a escrita condicional; text/event-stream
// para observar). Sem SDK: so HttpClient, igual no computador e no Android.
public sealed class BancoFirebase : IBanco
{
    readonly HttpClient _http;
    readonly IdentidadeFirebase _id;
    readonly string _url;

    public BancoFirebase(HttpClient http, IdentidadeFirebase id, string url = ConfigFirebase.Banco)
    {
        _http = http; _id = id; _url = url.TrimEnd('/');
    }

    public string Uid => _id.Uid;

    async Task<string> Url(string caminho, CancellationToken ct) =>
        $"{_url}/{caminho.Trim('/')}.json?auth={Uri.EscapeDataString(await _id.Token(ct))}";

    async Task<HttpResponseMessage> Enviar(Func<string, HttpRequestMessage> pedido, string caminho, CancellationToken ct, bool aceitar412 = false)
    {
        for (int tentativa = 0; ; tentativa++)
        {
            var resp = await _http.SendAsync(pedido(await Url(caminho, ct)), ct);
            if (resp.StatusCode == HttpStatusCode.Unauthorized && tentativa == 0) { resp.Dispose(); _id.Esquecer(); continue; }
            if (resp.IsSuccessStatusCode || (aceitar412 && resp.StatusCode == HttpStatusCode.PreconditionFailed)) return resp;
            var txt = await resp.Content.ReadAsStringAsync(ct);
            resp.Dispose();
            throw new InvalidOperationException($"firebase {(int)resp.StatusCode} em {caminho}: {txt}");
        }
    }

    static StringContent Json(JsonNode? n) => new(n?.ToJsonString() ?? "null", Encoding.UTF8, "application/json");

    public async Task<JsonNode?> Ler(string caminho, CancellationToken ct = default)
    {
        using var r = await Enviar(u => new HttpRequestMessage(HttpMethod.Get, u), caminho, ct);
        return JsonNode.Parse(await r.Content.ReadAsStringAsync(ct));
    }

    public async Task Gravar(string caminho, JsonNode? valor, CancellationToken ct = default)
    {
        if (valor == null) { using var d = await Enviar(u => new HttpRequestMessage(HttpMethod.Delete, u), caminho, ct); return; }
        using var r = await Enviar(u => new HttpRequestMessage(HttpMethod.Put, u) { Content = Json(valor) }, caminho, ct);
    }

    public async Task Atualizar(string caminho, JsonObject filhos, CancellationToken ct = default)
    {
        using var r = await Enviar(u => new HttpRequestMessage(HttpMethod.Patch, u) { Content = Json(filhos) }, caminho, ct);
    }

    public async Task<string> Acrescentar(string caminho, JsonNode valor, CancellationToken ct = default)
    {
        using var r = await Enviar(u => new HttpRequestMessage(HttpMethod.Post, u) { Content = Json(valor) }, caminho, ct);
        return (string?)JsonNode.Parse(await r.Content.ReadAsStringAsync(ct))?["name"] ?? "";
    }

    public async Task<(JsonNode? Valor, string Etag)> LerComEtag(string caminho, CancellationToken ct = default)
    {
        using var r = await Enviar(u =>
        {
            var m = new HttpRequestMessage(HttpMethod.Get, u);
            m.Headers.Add("X-Firebase-ETag", "true");
            return m;
        }, caminho, ct);
        string etag = r.Headers.TryGetValues("ETag", out var v) ? string.Join("", v) : "";
        return (JsonNode.Parse(await r.Content.ReadAsStringAsync(ct)), etag);
    }

    public async Task<bool> GravarSe(string caminho, JsonNode? valor, string etag, CancellationToken ct = default)
    {
        using var r = await Enviar(u =>
        {
            var m = new HttpRequestMessage(valor == null ? HttpMethod.Delete : HttpMethod.Put, u);
            if (valor != null) m.Content = Json(valor);
            m.Headers.TryAddWithoutValidation("if-match", etag);
            return m;
        }, caminho, ct, aceitar412: true);
        return r.StatusCode != HttpStatusCode.PreconditionFailed;
    }

    // o SSE do Firebase: "put" (troca o no do caminho) e "patch" (troca filhos); "keep-alive";
    // "cancel" (regras) e "auth_revoked" (token vencido): de novo, com um token novo. Caiu a
    // ligacao: de novo depois de um tempo (o "put" inicial manda o no inteiro outra vez)
    public IDisposable Observar(string caminho, Action<string, JsonNode?> mudou)
    {
        var cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            int espera = 1000;
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var pedido = new HttpRequestMessage(HttpMethod.Get, await Url(caminho, cts.Token));
                    pedido.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                    using var resp = await _http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    if (resp.StatusCode == HttpStatusCode.Unauthorized) { _id.Esquecer(); continue; }
                    resp.EnsureSuccessStatusCode();
                    using var leitor = new StreamReader(await resp.Content.ReadAsStreamAsync(cts.Token));
                    string? evento = null;
                    espera = 1000;
                    while (!cts.IsCancellationRequested && await leitor.ReadLineAsync(cts.Token) is { } linha)
                    {
                        if (linha.StartsWith("event:")) { evento = linha[6..].Trim(); continue; }
                        if (!linha.StartsWith("data:")) continue;
                        var dados = linha[5..].Trim();
                        if (evento is "auth_revoked" or "cancel") { _id.Esquecer(); break; }
                        if (evento is not ("put" or "patch") || JsonNode.Parse(dados) is not JsonObject o) continue;
                        var p = (string?)o["path"] ?? "/";
                        var d = o["data"]?.DeepClone();
                        if (evento == "put") mudou(p, d);
                        else if (d is JsonObject filhos)
                            foreach (var (k, v) in filhos) mudou(p.TrimEnd('/') + "/" + k, v?.DeepClone());
                    }
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested) { return; }
                catch { }
                try { await Task.Delay(espera, cts.Token); } catch { return; }
                espera = Math.Min(espera * 2, 15000);
            }
        });
        return new Saida(cts);
    }

    sealed class Saida(CancellationTokenSource cts) : IDisposable
    {
        public void Dispose() { cts.Cancel(); cts.Dispose(); }
    }
}
