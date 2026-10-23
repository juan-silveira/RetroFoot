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
// Visto: o sinal de vida do aparelho (a hora do servidor, ms; o cliente manda de tempos em tempos)
public sealed record JogadorSala(string Uid, string Nome, int Vaga, ushort Equipa, string Estado, string Rodada, long Visto = 0)
{
    public static JogadorSala? De(string uid, JsonNode? n) => n is JsonObject o
        ? new JogadorSala(uid, (string?)o["nome"] ?? "", (int?)o["vaga"] ?? -1, (ushort)((int?)o["equipa"] ?? 0),
            (string?)o["estado"] ?? Estados.Preparando, (string?)o["rodada"] ?? "", (long?)o["visto"] ?? 0)
        : null;
}

// a fase da sala (info/fase), para quem espera nela
public static class Fases
{
    public const string Criando = "criando", Continuando = "continuando", Jogando = "jogando", Pausado = "pausado";
}

public static class Estados
{
    // Saiu: por agora (o tecnico fica de ferias e volta com a mesma chave); Desistiu: de vez (o
    // tecnico sai do jogo e o computador assume o time)
    public const string Preparando = "preparando", Pronto = "pronto", Saiu = "saiu", Desistiu = "desistiu";
}

public sealed class Sala
{
    public const int Vagas = 6;
    // sem I e O, sem 0 e 1: faceis de ditar
    const string Letras = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public readonly IBanco B;
    public readonly string Codigo, Uid;
    public string Raiz => "salas/" + Codigo;
    // o nome da sala (info/nome), para mostrar junto da chave
    public string Nome { get; private set; } = "";

    Sala(IBanco b, string codigo, string uid) { B = b; Codigo = codigo; Uid = uid; }

    public static bool CodigoValido(string c) => c.Length == 6 && c.All(x => Letras.Contains(x) || char.IsAsciiDigit(x) || char.IsAsciiLetterUpper(x));

    static JsonNode Agora() => new JsonObject { [".sv"] = "timestamp" };

    // cria uma sala nova com um codigo livre; quem cria e o host e fica na vaga 0
    public static async Task<Sala> Criar(IBanco b, string uid, string nome, string versao, CancellationToken ct = default) =>
        await Criar(b, uid, nome, versao, null, ct);

    // nomeSala: o nome que todos veem na lista das salas (vazio: "Sala de <nome>")
    // codigo: a chave que se quer (o host que volta a um jogo cuja sala acabou: a mesma chave,
    // se estiver livre, para os outros acharem a sala na lista deles)
    public static async Task<Sala> Criar(IBanco b, string uid, string nome, string versao, string? nomeSala, CancellationToken ct = default, string? codigoDesejado = null)
    {
        for (int tentativa = 0; tentativa < 20; tentativa++)
        {
            var codigo = tentativa == 0 && codigoDesejado != null && CodigoValido(codigoDesejado) ? codigoDesejado
                : new string(Enumerable.Range(0, 6).Select(_ => Letras[Random.Shared.Next(Letras.Length)]).ToArray());
            if (await b.Ler($"salas/{codigo}/info", ct) != null) continue;
            await b.Atualizar($"salas/{codigo}", new JsonObject
            {
                ["info"] = new JsonObject { ["criador"] = uid, ["criada"] = Agora(), ["versao"] = versao, ["nome"] = NomeDaSala(nomeSala, nome) },
                ["host"] = uid,
                ["hostVivo"] = Agora(),
                ["vagas/0"] = uid,
                ["jogadores/" + uid] = new JsonObject { ["nome"] = NomeTecnico(nome), ["vaga"] = 0, ["estado"] = Estados.Preparando },
            }, ct);
            return new Sala(b, codigo, uid) { Nome = NomeDaSala(nomeSala, nome) };
        }
        throw new InvalidOperationException("nao achei um codigo livre");
    }

    // o host volta para a sala dele (o jogo continuado de um save): null se a sala nao existe
    // mais ou se o host agora e outro
    public static async Task<Sala?> Reabrir(IBanco b, string uid, string codigo, CancellationToken ct = default)
    {
        if (!CodigoValido(codigo) || await b.Ler($"salas/{codigo}/info", ct) is not { } info) return null;
        if ((string?)await b.Ler($"salas/{codigo}/host", ct) != uid) return null;
        var s = new Sala(b, codigo, uid) { Nome = (string?)info["nome"] ?? "" };
        await s.SinalDeVida(ct);
        // (o retrato e a rodada da vez anterior: ninguem entra num jogo velho antes do host
        // continuar)
        await s.Pausar(ct);
        return s;
    }

    // assumir o host de uma sala cujo host sumiu (as regras so deixam com o sinal de vida
    // parado ha mais de 60 s; a escrita condicional faz so um assumir)
    public async Task<bool> Assumir(CancellationToken ct = default)
    {
        try
        {
            var (h, etag) = await B.LerComEtag($"{Raiz}/host", ct);
            if ((string?)h == Uid) return true;
            if (!await B.GravarSe($"{Raiz}/host", JsonValue.Create(Uid), etag, ct)) return false;
            await B.Gravar($"{Raiz}/hostVivo", Agora(), ct);
            return true;
        }
        catch (InvalidOperationException) { return false; }   // (as regras recusaram: o host esta vivo)
    }

    public sealed class SalaCheia : Exception { public SalaCheia() : base("sala cheia") { } }
    public sealed class SalaInexistente : Exception { public SalaInexistente() : base("sala nao existe") { } }

    // entra numa sala pelo codigo: a primeira vaga livre (escrita condicional: dois nao pegam a
    // mesma); quem ja estava na sala volta para a vaga dele
    public static async Task<Sala> Entrar(IBanco b, string uid, string codigo, string nome, CancellationToken ct = default)
    {
        codigo = codigo.Trim().ToUpperInvariant();
        if (!CodigoValido(codigo) || await b.Ler($"salas/{codigo}/info", ct) is not { } info) throw new SalaInexistente();
        var s = new Sala(b, codigo, uid) { Nome = (string?)info["nome"] ?? "" };
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
            ["nome"] = NomeTecnico(nome), ["vaga"] = minha, ["equipa"] = (int)(ja?.Equipa ?? 0), ["estado"] = Estados.Preparando,
        }, ct);
        return s;
    }

    static async Task<JsonNode?> Tentar(Func<Task<JsonNode?>> f)
    {
        try { return await f(); } catch (InvalidOperationException) { return null; }
    }

    public static string NomeDaSala(string? nomeSala, string tecnico) =>
        string.IsNullOrWhiteSpace(nomeSala) ? "Sala de " + NomeTecnico(tecnico) : nomeSala.Trim()[..Math.Min(40, nomeSala.Trim().Length)];

    // o que a lista das salas mostra (de quem ja esteve nela): o nome, o ano e a rodada do jogo
    // (info, o host grava a cada rodada), o time e a divisao deste tecnico. null: a sala acabou
    public sealed record Resumo(string Codigo, string Nome, int Ano, string Rodada, string Time, string Divisao, bool Host, string Fase, string Modo = "");

    public static async Task<Resumo?> LerResumo(IBanco b, string uid, string codigo, CancellationToken ct = default)
    {
        if (!CodigoValido(codigo) || await b.Ler($"salas/{codigo}/info", ct) is not JsonObject info) return null;
        var host = (string?)await Tentar(() => b.Ler($"salas/{codigo}/host", ct));
        var eu = await Tentar(() => b.Ler($"salas/{codigo}/jogadores/{uid}", ct)) as JsonObject;
        return new Resumo(codigo, (string?)info["nome"] ?? "", (int?)info["ano"] ?? 0, (string?)info["rodada"] ?? "",
            (string?)eu?["time"] ?? "", (string?)eu?["divisao"] ?? "", host == uid, (string?)info["fase"] ?? "", (string?)info["modo"] ?? "");
    }

    static string NomeTecnico(string n) => n.Trim().Length == 0 ? "Técnico" : n.Trim()[..Math.Min(30, n.Trim().Length)];

    public async Task<List<JogadorSala>> Jogadores(CancellationToken ct = default) =>
        Nos.Filhos(await B.Ler($"{Raiz}/jogadores", ct)).Select(x => JogadorSala.De(x.Chave, x.Valor)).OfType<JogadorSala>().OrderBy(x => x.Vaga).ToList();

    public Task<JsonNode?> Host(CancellationToken ct = default) => B.Ler($"{Raiz}/host", ct);

    public Task MudarEstado(string estado, string rodada, CancellationToken ct = default) =>
        B.Atualizar($"{Raiz}/jogadores/{Uid}", new JsonObject { ["estado"] = estado, ["rodada"] = rodada }, ct);

    // ---------------------------------------------------------------- as regras da sala
    // o tempo (minutos, 1 a 5) que todos tem para ficar PRONTOS depois do primeiro PRONTO da
    // rodada; acabou, quem nao estiver pronto vai com a escalacao automatica (info/prazo)
    public const int PrazoPadrao = 5;

    public async Task<int> Prazo(CancellationToken ct = default) =>
        Math.Clamp((int?)await B.Ler($"{Raiz}/info/prazo", ct) ?? PrazoPadrao, 1, 5);

    // a velocidade da partida (as do jogo: 1 lento a 6 ultrassonico; info/velocidade)
    public const int VelocidadePadrao = 2;

    public async Task<int> Velocidade(CancellationToken ct = default) =>
        Math.Clamp((int?)await B.Ler($"{Raiz}/info/velocidade", ct) ?? VelocidadePadrao, 1, 6);

    public Task DefinirVelocidade(int v, CancellationToken ct = default) =>
        B.Atualizar($"{Raiz}/info", new JsonObject { ["velocidade"] = Math.Clamp(v, 1, 6) }, ct);

    public Task DefinirPrazo(int minutos, CancellationToken ct = default) =>
        B.Atualizar($"{Raiz}/info", new JsonObject { ["prazo"] = Math.Clamp(minutos, 1, 5) }, ct);

    // o sinal de vida do host (HostOnline de 10 em 10 s; na sala, antes do jogo, a TelaSala)
    public async Task SinalDeVida(CancellationToken ct = default)
    {
        await B.Gravar($"{Raiz}/hostVivo", Agora(), ct);
        await Visto(ct);   // (o host tambem aparece conectado na lista)
    }

    // o host parou o jogo (saiu, ou reabriu a sala para continuar): sem retrato, quem esta na
    // sala espera ele comecar de novo
    public async Task Pausar(CancellationToken ct = default)
    {
        await B.Gravar($"{Raiz}/retrato", null, ct);
        await B.Gravar($"{Raiz}/rodada", null, ct);
        await MarcarFase(Fases.Pausado, ct);
    }

    // o que o host esta fazendo antes do jogo ("criando": o jogo novo sendo montado, os times
    // sorteados), para quem espera na sala
    public Task MarcarFase(string fase, CancellationToken ct = default) =>
        B.Atualizar($"{Raiz}/info", new JsonObject { ["fase"] = fase }, ct);

    // o modo da sala: "amigo" = um save single player do host com amigos (Convidado); vazio = o
    // jogo online comum
    public const string ModoAmigo = "amigo";
    public Task DefinirModo(string modo, CancellationToken ct = default) =>
        B.Atualizar($"{Raiz}/info", new JsonObject { ["modo"] = modo }, ct);
    public async Task<string> Modo(CancellationToken ct = default) => (string?)await B.Ler($"{Raiz}/info/modo", ct) ?? "";

    // o host da o time de um jogador (o id da equipa no jogo)
    public Task DarEquipa(string uid, ushort equipa, CancellationToken ct = default) =>
        B.Atualizar($"{Raiz}/jogadores/{uid}", new JsonObject { ["equipa"] = (int)equipa }, ct);

    public Task<string> Mandar(Comando c, CancellationToken ct = default) =>
        B.Acrescentar($"{Raiz}/comandos/{Uid}", new JsonObject { ["c"] = c.Json() }, ct);

    // a diferenca (ms) entre o relogio do servidor e o deste aparelho: grava o sinal de vida
    // (a hora do servidor) e le de volta; com ela, o Visto de cada um diz quem esta conectado
    public async Task<long> DiferencaDoRelogio(CancellationToken ct = default)
    {
        await Visto(ct);
        long agora = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long servidor = (long?)await B.Ler($"{Raiz}/jogadores/{Uid}/visto", ct) ?? agora;
        return servidor - agora;
    }

    // conectado: o sinal de vida dele (a cada 15 s) chegou ha menos de 40 s
    public static bool Conectado(JogadorSala j, long diferenca) =>
        j.Visto > 0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + diferenca - j.Visto < 40_000;

    // o sinal de vida do aparelho (ClienteOnline, a cada 15 s)
    public Task Visto(CancellationToken ct = default) =>
        B.Atualizar($"{Raiz}/jogadores/{Uid}", new JsonObject { ["visto"] = Agora() }, ct);

    // sai do jogo em andamento: a vaga e o time ficam (volta com a mesma chave); o treinador fica
    // de ferias (o computador cuida do time) e a rodada nao espera por ele
    public Task Ausentar(CancellationToken ct = default) =>
        B.Atualizar($"{Raiz}/jogadores/{Uid}", new JsonObject { ["estado"] = Estados.Saiu }, ct);

    // desiste do jogo de vez: o host tira o tecnico dele do jogo (um do computador assume o time)
    // e libera a vaga
    public Task Desistir(CancellationToken ct = default) =>
        B.Atualizar($"{Raiz}/jogadores/{Uid}", new JsonObject { ["estado"] = Estados.Desistiu }, ct);

    // sai da sala antes do jogo: a vaga fica livre (o host apaga a sala inteira)
    public async Task Sair(CancellationToken ct = default)
    {
        var j = JogadorSala.De(Uid, await B.Ler($"{Raiz}/jogadores/{Uid}", ct));
        await B.Gravar($"{Raiz}/jogadores/{Uid}", null, ct);
        if (j != null && j.Vaga >= 0) await B.Gravar($"{Raiz}/vagas/{j.Vaga}", null, ct);
    }

    public Task Apagar(CancellationToken ct = default) => B.Gravar(Raiz, null, ct);
}
