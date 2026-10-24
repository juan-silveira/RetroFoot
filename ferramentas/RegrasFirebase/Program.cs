// dotnet run --project ferramentas/RegrasFirebase: as regras do banco da sala no Firebase de verdade
// (cada linha OK/FALHA; a sala de teste e apagada no fim)
using System.Text.Json.Nodes;
using RetroFoot.Motor.Online;
var http = new HttpClient();
BancoFirebase Novo() => new(http, new IdentidadeFirebase(http, null));
async Task<bool> Pode(Func<Task> f) { try { await f(); return true; } catch (Exception e) { if (!e.Message.Contains("401")) Console.WriteLine("   (erro: " + e.Message.Split('\n')[0] + ")"); return false; } }
int falhas = 0;
void Ok(string o, bool esperado, bool real) { Console.WriteLine($"{(esperado == real ? "OK  " : "FALHA")} {o}: {real}"); if (esperado != real) falhas++; }
JsonNode Ts() => new JsonObject { [".sv"] = "timestamp" };
var cod = "T" + Random.Shared.Next(10000, 99999);
var A = Novo(); var B = Novo(); var C = Novo();
foreach (var x in new[] { A, B, C }) await x.Ler("salas/" + cod + "/info");
Ok("A cria a sala", true, await Pode(() => A.Atualizar("salas/" + cod, new JsonObject {
    ["info"] = new JsonObject { ["criador"] = A.Uid, ["criada"] = Ts(), ["versao"] = "0.2.0" },
    ["host"] = A.Uid, ["hostVivo"] = Ts(), ["vagas/0"] = A.Uid,
    ["jogadores/" + A.Uid] = new JsonObject { ["nome"] = "Juan", ["vaga"] = 0 } })));
Ok("A le a sala", true, await Pode(() => A.Ler("salas/" + cod)));
Ok("C (de fora) le a sala inteira", false, await Pode(() => C.Ler("salas/" + cod)));
Ok("C le a info (para entrar)", true, await Pode(() => C.Ler("salas/" + cod + "/info")));
Ok("C cria sala com o mesmo codigo", false, await Pode(() => C.Gravar("salas/" + cod + "/info", new JsonObject { ["criador"] = C.Uid, ["criada"] = Ts(), ["versao"] = "0.2.0" })));
Ok("B pega a vaga 1", true, await Pode(() => B.Gravar("salas/" + cod + "/vagas/1", JsonValue.Create(B.Uid))));
Ok("B entra como jogador", true, await Pode(() => B.Gravar("salas/" + cod + "/jogadores/" + B.Uid, new JsonObject { ["nome"] = "Joao", ["vaga"] = 1 })));
Ok("C toma a vaga do B", false, await Pode(() => C.Gravar("salas/" + cod + "/vagas/1", JsonValue.Create(C.Uid))));
Ok("C entra com a vaga do B", false, await Pode(() => C.Gravar("salas/" + cod + "/jogadores/" + C.Uid, new JsonObject { ["nome"] = "X", ["vaga"] = 1 })));
Ok("C usa a vaga 6", false, await Pode(() => C.Gravar("salas/" + cod + "/vagas/6", JsonValue.Create(C.Uid))));
Ok("B le a sala", true, await Pode(() => B.Ler("salas/" + cod)));
Ok("B escreve o retrato", false, await Pode(() => B.Gravar("salas/" + cod + "/retrato", JsonValue.Create("x"))));
Ok("A escreve o retrato (120 KB)", true, await Pode(() => A.Gravar("salas/" + cod + "/retrato", new JsonObject { ["rodada"] = 1, ["dados"] = new string('x', 120000) })));
Ok("B manda comando", true, await Pode(() => B.Acrescentar("salas/" + cod + "/comandos/" + B.Uid, new JsonObject { ["c"] = 1 })));
Ok("B manda comando como A", false, await Pode(() => B.Acrescentar("salas/" + cod + "/comandos/" + A.Uid, new JsonObject { ["c"] = 1 })));
Ok("B escreve pergunta", false, await Pode(() => B.Gravar("salas/" + cod + "/perguntas/" + B.Uid, JsonValue.Create(1))));
Ok("A escreve pergunta para B", true, await Pode(() => A.Gravar("salas/" + cod + "/perguntas/" + B.Uid, JsonValue.Create(1))));
Ok("B muda a info", false, await Pode(() => B.Atualizar("salas/" + cod + "/info", new JsonObject { ["versao"] = "x" })));
Ok("B cria no estranho", false, await Pode(() => B.Gravar("salas/" + cod + "/lixo", JsonValue.Create(1))));
Ok("B assume o host com o host vivo", false, await Pode(() => B.Gravar("salas/" + cod + "/host", JsonValue.Create(B.Uid))));
Ok("B apaga a sala", false, await Pode(() => B.Gravar("salas/" + cod, null)));
Ok("B muda o jogador de A", false, await Pode(() => B.Gravar("salas/" + cod + "/jogadores/" + A.Uid + "/nome", JsonValue.Create("x"))));
// observar
var vistos = new List<string>();
using (B.Observar("salas/" + cod + "/jogadores", (c, v) => { lock (vistos) vistos.Add(c); }))
{
    await Task.Delay(3000);
    await A.Gravar("salas/" + cod + "/jogadores/" + A.Uid + "/estado", JsonValue.Create("PRONTO"));
    await Task.Delay(3000);
}
lock (vistos) Ok("B ve o PRONTO de A ao vivo (" + string.Join(" ", vistos) + ")", true, vistos.Contains("/" + A.Uid + "/estado"));
// o host passa o host para um jogador da sala (e nao para quem esta fora)
Ok("A (host) passa o host para C (de fora)", false, await Pode(() => A.Gravar("salas/" + cod + "/host", JsonValue.Create(C.Uid))));
Ok("A (host) passa o host para B", true, await Pode(() => A.Gravar("salas/" + cod + "/host", JsonValue.Create(B.Uid))));
Ok("B (host novo) da sinal", true, await Pode(() => B.Gravar("salas/" + cod + "/hostVivo", Ts())));
Ok("B (host) devolve o host para A", true, await Pode(() => B.Gravar("salas/" + cod + "/host", JsonValue.Create(A.Uid))));
// o host "cai": o sinal de vida fica velho
Ok("A grava sinal velho", true, await Pode(() => A.Gravar("salas/" + cod + "/hostVivo", JsonValue.Create(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 120000))));
var (h, etag) = await B.LerComEtag("salas/" + cod + "/host");
Ok("B assume o host (60 s sem sinal)", true, await Pode(async () => { if (!await B.GravarSe("salas/" + cod + "/host", JsonValue.Create(B.Uid), etag)) throw new Exception("412"); }));
Ok("C (outro) assume com a etag velha", false, await Pode(async () => { if (!await A.GravarSe("salas/" + cod + "/host", JsonValue.Create(A.Uid), etag)) throw new Exception("412"); }));
Ok("B (host) da sinal", true, await Pode(() => B.Gravar("salas/" + cod + "/hostVivo", Ts())));
Ok("A (ex-host) escreve retrato", false, await Pode(() => A.Gravar("salas/" + cod + "/retrato", JsonValue.Create("y"))));
Ok("B (host) escreve retrato", true, await Pode(() => B.Gravar("salas/" + cod + "/retrato", JsonValue.Create("y"))));
// 6 vagas
for (int v = 2; v <= 5; v++) { var X = Novo(); await X.Ler("salas/" + cod + "/info"); await X.Gravar("salas/" + cod + "/vagas/" + v, JsonValue.Create(X.Uid)); }
var vn = await C.Ler("salas/" + cod + "/vagas");
int nv = vn is JsonArray arr ? arr.Count(x => x != null) : ((JsonObject)vn!).Count;
Ok("sala cheia com 6 (" + vn!.GetType().Name + ")", true, nv == 6);
bool entrou = false;
for (int v = 0; v <= 9 && !entrou; v++) entrou = await Pode(() => C.Gravar("salas/" + cod + "/vagas/" + v, JsonValue.Create(C.Uid)));
Ok("7o (C) entra", false, entrou);
Ok("B (host) apaga a sala", true, await Pode(() => B.Gravar("salas/" + cod, null)));
Console.WriteLine(falhas == 0 ? "TUDO OK" : $"{falhas} FALHAS");
return falhas == 0 ? 0 : 1;
