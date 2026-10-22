using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RetroFoot.Motor.Online;

// A sala de um jogo online (docs/multiplayer-online.md) no banco (IBanco):
//   salas/<CODIGO>/info        criador, criada, versao (do RetroFoot)
//                  host        o uid de quem roda o jogo; hostVivo: o sinal de vida (ms)
//                  vagas/0..5  o uid de cada vaga (6 = o limite de tecnicos do jogo)
//                  jogadores/<uid>  nome, vaga, equipa (id; 0 = ainda sem time), estado, rodada
//                  retrato     rodada (a barreira), versao, dados (Retrato em base64)
//                  comandos/<uid>/<chave>   o comando (Comando.Json) do tecnico
//                  resultados/<uid>/<chave> a resposta do host
// Sem login: o uid e a identidade anonima do aparelho; entra-se com o codigo.
public sealed record JogadorSala(string Uid, string Nome, int Vaga, ushort Equipa, string Estado, string Rodada)
{
    public static JogadorSala? De(string uid, JsonNode? n) => n is JsonObject o
        ? new JogadorSala(uid, (string?)o["nome"] ?? "", (int?)o["vaga"] ?? -1, (ushort)((int?)o["equipa"] ?? 0),
            (string?)o["estado"] ?? Estados.Preparando, (string?)o["rodada"] ?? "")
        : null;
}

public static class Estados
{
    public const string Preparando = "preparando", Pronto = "pronto", Saiu = "saiu";
}

public sealed class Sala
{
    public const int Vagas = 6;
    // sem I e O, sem 0 e 1: faceis de ditar
    const string Letras = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public readonly IBanco B;
    public readonly string Codigo, Uid;
    public string Raiz => "salas/" + Codigo;

    Sala(IBanco b, string codigo, string uid) { B = b; Codigo = codigo; Uid = uid; }

    public static bool CodigoValido(string c) => c.Length == 6 && c.All(x => Letras.Contains(x) || char.IsAsciiDigit(x) || char.IsAsciiLetterUpper(x));

    static JsonNode Agora() => new JsonObject { [".sv"] = "timestamp" };

    // cria uma sala nova com um codigo livre; quem cria e o host e fica na vaga 0
    public static async Task<Sala> Criar(IBanco b, string uid, string nome, string versao, CancellationToken ct = default)
    {
        for (int tentativa = 0; tentativa < 20; tentativa++)
        {
            var codigo = new string(Enumerable.Range(0, 6).Select(_ => Letras[Random.Shared.Next(Letras.Length)]).ToArray());
            if (await b.Ler($"salas/{codigo}/info", ct) != null) continue;
            await b.Atualizar($"salas/{codigo}", new JsonObject
            {
                ["info"] = new JsonObject { ["criador"] = uid, ["criada"] = Agora(), ["versao"] = versao },
                ["host"] = uid,
                ["hostVivo"] = Agora(),
                ["vagas/0"] = uid,
                ["jogadores/" + uid] = new JsonObject { ["nome"] = Nome(nome), ["vaga"] = 0, ["estado"] = Estados.Preparando },
            }, ct);
            return new Sala(b, codigo, uid);
        }
        throw new InvalidOperationException("nao achei um codigo livre");
    }

    public sealed class SalaCheia : Exception { public SalaCheia() : base("sala cheia") { } }
    public sealed class SalaInexistente : Exception { public SalaInexistente() : base("sala nao existe") { } }

    // entra numa sala pelo codigo: a primeira vaga livre (escrita condicional: dois nao pegam a
    // mesma); quem ja estava na sala volta para a vaga dele
    public static async Task<Sala> Entrar(IBanco b, string uid, string codigo, string nome, CancellationToken ct = default)
    {
        codigo = codigo.Trim().ToUpperInvariant();
        if (!CodigoValido(codigo) || await b.Ler($"salas/{codigo}/info", ct) == null) throw new SalaInexistente();
        var s = new Sala(b, codigo, uid);
        var vagas = Nos.Filhos(await b.Ler($"salas/{codigo}/vagas", ct)).ToDictionary(x => x.Chave, x => (string?)x.Valor);
        int minha = vagas.FirstOrDefault(x => x.Value == uid).Key is { } k ? int.Parse(k) : -1;
        for (int v = 0; v < Vagas && minha < 0; v++)
        {
            if (vagas.ContainsKey(v.ToString())) continue;
            var (atual, etag) = await b.LerComEtag($"salas/{codigo}/vagas/{v}", ct);
            if (atual != null) continue;
            try { if (await b.GravarSe($"salas/{codigo}/vagas/{v}", JsonValue.Create(uid), etag, ct)) minha = v; }
            catch (InvalidOperationException) { }   // (as regras recusam: outro pegou)
        }
        if (minha < 0) throw new SalaCheia();
        var ja = JogadorSala.De(uid, await Tentar(() => b.Ler($"{s.Raiz}/jogadores/{uid}", ct)));
        await b.Gravar($"{s.Raiz}/jogadores/{uid}", new JsonObject
        {
            ["nome"] = Nome(nome), ["vaga"] = minha, ["equipa"] = (int)(ja?.Equipa ?? 0), ["estado"] = ja?.Estado ?? Estados.Preparando,
        }, ct);
        return s;
    }

    static async Task<JsonNode?> Tentar(Func<Task<JsonNode?>> f)
    {
        try { return await f(); } catch (InvalidOperationException) { return null; }
    }

    static string Nome(string n) => n.Trim().Length == 0 ? "Técnico" : n.Trim()[..Math.Min(30, n.Trim().Length)];

    public async Task<List<JogadorSala>> Jogadores(CancellationToken ct = default) =>
        Nos.Filhos(await B.Ler($"{Raiz}/jogadores", ct)).Select(x => JogadorSala.De(x.Chave, x.Valor)).OfType<JogadorSala>().OrderBy(x => x.Vaga).ToList();

    public Task<JsonNode?> Host(CancellationToken ct = default) => B.Ler($"{Raiz}/host", ct);

    public Task MudarEstado(string estado, string rodada, CancellationToken ct = default) =>
        B.Atualizar($"{Raiz}/jogadores/{Uid}", new JsonObject { ["estado"] = estado, ["rodada"] = rodada }, ct);

    // o host da o time de um jogador (o id da equipa no jogo)
    public Task DarEquipa(string uid, ushort equipa, CancellationToken ct = default) =>
        B.Atualizar($"{Raiz}/jogadores/{uid}", new JsonObject { ["equipa"] = (int)equipa }, ct);

    public Task<string> Mandar(Comando c, CancellationToken ct = default) =>
        B.Acrescentar($"{Raiz}/comandos/{Uid}", new JsonObject { ["c"] = c.Json() }, ct);

    // sai da sala: a vaga fica livre (o host apaga a sala inteira)
    public async Task Sair(CancellationToken ct = default)
    {
        var j = JogadorSala.De(Uid, await B.Ler($"{Raiz}/jogadores/{Uid}", ct));
        await B.Gravar($"{Raiz}/jogadores/{Uid}", null, ct);
        if (j != null && j.Vaga >= 0) await B.Gravar($"{Raiz}/vagas/{j.Vaga}", null, ct);
    }

    public Task Apagar(CancellationToken ct = default) => B.Gravar(Raiz, null, ct);
}
