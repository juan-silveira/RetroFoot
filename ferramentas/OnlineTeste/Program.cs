// dotnet run --project ferramentas/OnlineTeste -- host <arquivo.e98> [rodadas] [segundos para esperar quem entra]
// dotnet run --project ferramentas/OnlineTeste -- cliente <CHAVE> [nome]
// O jogo online pelo Firebase de verdade, sem telas (docs/multiplayer-online.md).
using System.Text.Json.Nodes;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Online;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;
using TRegras = RetroFoot.Motor.Regras.Regras;

var http = new HttpClient();
var banco = new BancoFirebase(http, new IdentidadeFirebase(http, null));
await banco.Ler("salas/AAAAAA/info");
void Log(string s) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {s}");

if (args[0] == "host")
{
    var s = Jogo98.Ler(File.ReadAllBytes(args[1]));
    int rodadas = args.Length > 2 ? int.Parse(args[2]) : 3;
    int espera = args.Length > 3 ? int.Parse(args[3]) : 60;
    var sala = await Sala.Criar(banco, banco.Uid, "Host", "0.2.0");
    Log("SALA " + sala.Codigo);
    File.WriteAllText(Path.Combine(Path.GetTempPath(), "retrofoot-sala.txt"), sala.Codigo);
    var humanos = s.Equipas.Where(e => MercadoJogo.HumanoAtivo(s.Treinadores.FirstOrDefault(t => t.ID == e.Treinador))).ToList();
    var livres = s.Equipas.Where(e => !humanos.Contains(e) && s.Divisoes.Take(4).Any(d => d.Equipas.Contains(e.ID))).ToList();
    await sala.DarEquipa(banco.Uid, humanos[0].ID);
    // quem entra ganha um time (o treinador dele vira humano)
    var dados = new HashSet<string> { banco.Uid };
    var fim = DateTime.Now.AddSeconds(espera);
    while (DateTime.Now < fim)
    {
        foreach (var j in await sala.Jogadores())
            if (dados.Add(j.Uid))
            {
                var e = livres[0]; livres.RemoveAt(0);
                s.Treinadores.First(t => t.ID == e.Treinador).Humano = 1;
                await sala.DarEquipa(j.Uid, e.ID);
                Log($"{j.Nome} entrou: time {e.ID}");
            }
        if (dados.Count > 1 && args.Length <= 3) break;
        await Task.Delay(1000);
    }
    int barreiras = 0;
    HostOnline host = null!;
    var h = new Humano
    {
        Preparar = (j, e, jogo) =>
        {
            string antes = host.Rodada;
            bool sair = host.Preparar(e, jogo);
            if (host.Rodada != antes) { barreiras++; Log($"rodada {host.Rodada}: todos prontos"); if (barreiras > rodadas) return true; }
            return sair;
        },
    };
    var jg = new JogoEmCurso(s, (uint)Environment.TickCount, Opcoes.Padrao with { Regras = TRegras.DoSave(s, TRegras.Original), Registrado = true }, h);
    host = new HostOnline(sala, jg);
    host.JogadoresMudaram += l => Log("jogadores: " + string.Join(", ", l.Select(x => $"{x.Nome}={x.Estado}/{x.Rodada}")));
    jg.Ciclo();
    host.Dispose();
    Log($"FIM: {barreiras} barreiras; apagando a sala");
    await sala.Apagar();
}
else
{
    var sala = await Sala.Entrar(banco, banco.Uid, args[1], args.Length > 2 ? args[2] : "Cliente");
    Log("entrei na " + sala.Codigo);
    using var cli = new ClienteOnline(sala);
    string ultima = "";
    var acabou = new TaskCompletionSource();
    cli.NovoRetrato += (r, rodada, versao) => _ = Task.Run(async () =>
    {
        if (rodada == ultima) return;
        ultima = rodada;
        var minha = (await sala.Jogadores()).First(x => x.Uid == sala.Uid);
        var copia = Jogo98.Ler(r.Save);
        var e = copia.Equipas.First(x => x.ID == minha.Equipa);
        Escalacao.Automatico(new Aleatorio((uint)Environment.TickCount), e);
        // de vez em quando um comando que vai ao host
        var rr = await cli.Executar(new PedirEmprestimo());
        Log($"rodada {rodada} v{versao}: emprestimo {rr.Ok} {rr.Motivo}");
        var p = new Preparo(e.Jogadores.Select(x => new Escalado(x.ID, Memoria.Escalacao(x))).ToList(), 15000, new(), 0);
        var resp = await cli.Pronto(p);
        Log($"rodada {rodada}: PRONTO {resp.Ok} {resp.Motivo}");
    });
    // ate a sala sumir
    while (await banco.Ler($"salas/{sala.Codigo}/info") != null) await Task.Delay(3000);
    Log("a sala acabou");
}
