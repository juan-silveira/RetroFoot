using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using RetroFoot.App;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.NovoJogo;
using RetroFoot.Motor.Online;
using RetroFoot.Motor.Save;
using NovoOpcoes = RetroFoot.Motor.NovoJogo.Opcoes;

// O soak do jogo online: o host (Sessao + HostOnline, como a TelaInicio) e o cliente (ClienteOnline +
// JogoRemoto, como a TelaSala) de verdade, cada um na sua janela headless, no banco da memoria. Um
// "jogador" em cada janela aperta Enter (e A na tela da equipa). Anota cada pergunta do host para o
// cliente (aumento, lance, convite, patrocinio, batedor, lesao) e cada janela aberta nos dois lados.
// Codigo 0 = jogou tudo; 1 = excecao; 2 = travou.
//
//   dotnet run --project ferramentas/SoakOnline -- [N] [mod|orig] [temporadas] [semente]
sealed class SoakApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
    }
}

static class Programa
{
    static int Main(string[] a)
    {
        int n = a.Length > 0 ? int.Parse(a[0]) : 8;
        bool mod = a.Length > 1 ? a[1] == "mod" : n != 8;
        int temporadas = a.Length > 2 ? int.Parse(a[2]) : 1;
        int semente = a.Length > 3 ? int.Parse(a[3]) : 1;
        int travou = int.TryParse(Environment.GetEnvironmentVariable("TRAVOU"), out var tv) ? tv : 120;
        var raiz = Environment.GetEnvironmentVariable("RETROFOOT_JOGO") ?? AcharJogo();
        if (raiz == null) { Console.Error.WriteLine("pasta do jogo nao encontrada"); return 3; }

        Sessao.PausaFimJornadaMs = 0;
        AppBuilder.Configure<SoakApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
        int codigo = 0;
        var cts = new CancellationTokenSource();
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Console.WriteLine("EXCECAO na interface: " + e.Exception);
            codigo = 1; e.Handled = true; cts.Cancel();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Console.WriteLine("EXCECAO fatal: " + e.ExceptionObject);

        var cfg = new Configuracoes
        {
            Modificada = mod, EquipasPorDivisao = n, CartoesAmarelos = mod, Patrocinios = mod,
        };
        // SOAK_CLIENTES=1..5 convidados; SOAK_AMIGO=1 o modo "jogar com um amigo" (o save e do host,
        // os convidados recebem um time da 4a divisao); SOAK_FIREBASE=1 o banco de verdade
        int nCli = Math.Clamp(int.TryParse(Environment.GetEnvironmentVariable("SOAK_CLIENTES"), out var nc) ? nc : 1, 1, 5);
        bool amigo = Environment.GetEnvironmentVariable("SOAK_AMIGO") == "1";
        bool firebase = Environment.GetEnvironmentVariable("SOAK_FIREBASE") == "1";
        var s = JogoNovo(raiz, cfg, semente, amigo ? 1 : 1 + nCli);
        var hs = s.Treinadores.Where(t => t.Humano != 0).ToList();
        var timeHost = s.Equipas.First(q => q.Treinador == hs[0].ID);
        var op = cfg.OpcoesDoJogo(s, raiz) with
        {
            Marcas = RetroFoot.Motor.Patrocinio.Marcas.Ler(raiz),
            Patrocinios = true, VerPenaltisCPU = true, VerSorteio = 0, VerExpulsoes = true, VerLesoes = true,
        };
        TelaLeilao.EsperaInicio = 0; TelaLeilao.EsperaFim = 0;

        // os bancos: um so na memoria, ou cada aparelho com a sua identidade anonima no Firebase
        var http = new System.Net.Http.HttpClient(new System.Net.Http.SocketsHttpHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        var pasta = Path.Combine(Path.GetTempPath(), $"soakonline-{Environment.ProcessId}");
        Directory.CreateDirectory(pasta);
        var memoria = new BancoMemoria();
        (IBanco B, string Uid) Banco(string quem)
        {
            if (!firebase) return (memoria, quem);
            var b = new BancoFirebase(http, new IdentidadeFirebase(http, Path.Combine(pasta, quem + ".json")));
            Esperar(() => b.Ler("salas/AAAAAA/info"));
            return (b, b.Uid);
        }
        var (bh, uidHost) = Banco("host");
        var sh = Esperar(() => Sala.Criar(bh, uidHost, "Host", "0.2.0", "Soak"));
        if (amigo) Esperar(() => sh.DefinirModo(Sala.ModoAmigo));
        Esperar(() => sh.DarEquipa(uidHost, timeHost.ID));
        Esperar(() => sh.DefinirPrazo(1));
        var banco = bh;

        // o que aconteceu: as perguntas a cada convidado e as janelas de cada lado
        var perguntas = new ConcurrentDictionary<string, int>();
        var respostas = new ConcurrentDictionary<string, int>();
        var tipoDe = new ConcurrentDictionary<string, string>();
        var nomeDe = new ConcurrentDictionary<string, string>();
        var pausas = new ConcurrentDictionary<string, int>();
        string? goleiroDe = null;   // o uid do convidado com o goleiro expulso (a pausa aberta)
        int goleiroJanela = 0;
        int noticias = 0;
        var janelasHost = new ConcurrentDictionary<string, int>();

        // o host
        var jh = new Window { Width = 1280, Height = 720 };
        var fh = new Border();
        jh.Content = fh;
        jh.Show();
        var arq = Path.Combine(pasta, "JOGO.e98");
        var sessao = new Sessao(s, c => fh.Child = c, op, arq) { MsPorMinuto = 0 };
        sessao.J.Treinador = "Soak";
        sessao.J.M.Retomar = -1;

        // os convidados: cada um com o seu banco, a sua janela e o seu JogoRemoto
        var clientes = new List<(string Nome, string Uid, Sala Sala, ClienteOnline Cli, JogoRemoto Rem, Window W, Border F, ConcurrentDictionary<string, int> Janelas)>();
        // SOAK_ENTRAR_DEPOIS=1: os convidados entram com o jogo ja comecado (na 1a preparacao do
        // host), pedem para entrar e o host permite
        bool depois = Environment.GetEnvironmentVariable("SOAK_ENTRAR_DEPOIS") == "1";
        void CriarCliente(int i)
        {
            string nome = "Cliente" + (i + 1);
            var (bc, uid) = Banco("cli" + (i + 1));
            var sc = Esperar(() => Sala.Entrar(bc, uid, sh.Codigo, nome));
            nomeDe[uid] = nome;
            if (!amigo) Esperar(() => sh.DarEquipa(uid, s.Equipas.First(q => q.Treinador == hs[i + 1].ID).ID));
            var w = new Window { Width = 1280, Height = 720 };
            var f = new Border();
            w.Content = f;
            w.Show();
            var cli = new ClienteOnline(sc);
            var rem = new JogoRemoto(c => f.Child = c, () => { Console.WriteLine($"o {nome} saiu do jogo"); codigo = 1; cts.Cancel(); }, sc, cli);
            clientes.Add((nome, uid, sc, cli, rem, w, f, new ConcurrentDictionary<string, int>()));
            Console.WriteLine($"  == {nome} entrou na sala");
            banco.Observar($"{sh.Raiz}/perguntas/{uid}", (c, v) =>
            {
                if (c == "/" && v is JsonObject q && (string?)q["tipo"] is { } tipo && (string?)q["id"] is { } id && tipoDe.TryAdd(uid + id, tipo))
                {
                    perguntas.AddOrUpdate(tipo, 1, (_, x) => x + 1);
                    var txt = q.ToJsonString();
                    Console.WriteLine($"  -> pergunta ao {nome}: {tipo} {(txt.Length > 160 ? txt[..160] + "…" : txt)}");
                }
            });
            banco.Observar($"{sh.Raiz}/respostas/{uid}", (c, v) =>
            {
                foreach (var (id, r) in c == "/" ? Nos.Filhos(v) : v == null ? [] : new[] { (c.Trim('/'), v) })
                {
                    var tipo = tipoDe.GetValueOrDefault(uid + id, "?");
                    respostas.AddOrUpdate(tipo, 1, (_, x) => x + 1);
                    Console.WriteLine($"  <- resposta do {nome}: {tipo} {r.ToJsonString()}");
                }
            });
        }
        if (!depois) for (int i = 0; i < nCli; i++) CriarCliente(i);
        // as paralisacoes: o intervalo de todos, e as dos convidados (1 parou, 3 goleiro expulso, 4 intervalo)
        banco.Observar($"{sh.Raiz}/rodada/aoVivo/pausa", (c, v) =>
        {
            if (c != "/") return;
            if (v is not JsonObject o) { goleiroDe = null; return; }
            string uid = (string?)o["uid"] ?? "";
            string k = (bool?)o["coletivo"] == true ? "intervalo-de-todos" : $"{nomeDe.GetValueOrDefault(uid, uid)}-motivo{o["motivo"]}";
            pausas.AddOrUpdate(k, 1, (_, x) => x + 1);
            if ((int?)o["motivo"] == 3) goleiroDe = uid;
            if (!k.StartsWith("intervalo")) Console.WriteLine($"  .. pausa {o.ToJsonString()}");
        });
        banco.Observar($"{sh.Raiz}/rodada/noticia", (c, v) => { if (c == "/" && v != null) Interlocked.Increment(ref noticias); });
        // (modo amigo) quem entrou e com que time; quem mudou de time
        banco.Observar($"{sh.Raiz}/jogadores", (c, v) =>
        {
            var p = c.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 2 && p[1] == "equipa") Console.WriteLine($"  == {nomeDe.GetValueOrDefault(p[0], "host")} agora com o time {v}");
        });

        var host = new HostOnline(sh, sessao.J, x => Dispatcher.UIThread.Invoke(x)) { ModoAmigo = amigo };
        host.PedidoDeEntrada += j => { Console.WriteLine($"  == pedido para entrar: {j.Nome} (permitido)"); _ = host.ResponderEntrada(j.Uid, true); };
        sessao.LigarOnline(host);
        if (amigo) sessao.NoComecoDoMotor = host.ComecarModoAmigo;
        sessao.Erro += ex => { Console.WriteLine("EXCECAO no motor: " + ex); codigo = 1; cts.Cancel(); };
        sessao.Saiu += () => { Console.WriteLine("o motor saiu"); cts.Cancel(); };
        sessao.Notificou += txt => Console.WriteLine("  [aviso no host] " + txt);

        int ano0 = sessao.J.M.Ano;
        Console.WriteLine($"N={n} {(mod ? "Modificada" : "Original")} temporadas={temporadas} semente={semente} sala={sh.Codigo} convidados={nCli}" +
            (amigo ? " modo-amigo" : "") + (firebase ? " FIREBASE" : " memoria") + $" host={timeHost.ID}");
        sessao.Comecar();

        var camadaDialogo = typeof(Sessao).GetField("camadaDialogo", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var camadaTela = typeof(Sessao).GetField("camadaTela", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var remSessao = typeof(JogoRemoto).GetField("_sessao", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var remAoVivo = typeof(JogoRemoto).GetField("_sessaoAoVivo", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var remJornada = typeof(JogoRemoto).GetField("_jornada", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var remPergunta = typeof(JogoRemoto).GetField("_sessaoPergunta", BindingFlags.NonPublic | BindingFlags.Instance)!;
        string Estado(Sessao? ss, Control? fora, ConcurrentDictionary<string, int> janelas)
        {
            if (ss == null) return $"tela={fora?.GetType().Name} sem-sessao";
            var tela = (camadaTela.GetValue(ss) as Border)?.Child;
            var dl = (camadaDialogo.GetValue(ss) as Grid)!.Children.Select(c => c.Tag?.GetType().Name ?? c.GetType().Name).ToList();
            foreach (var d in dl) janelas.TryAdd(d, 0);
            return $"tela={tela?.GetType().Name} dialogos=[{string.Join(",", dl)}]";
        }
        Sessao? SessaoCli(JogoRemoto r) => (remJornada.GetValue(r) != null ? remAoVivo.GetValue(r) : remSessao.GetValue(r) ?? remPergunta.GetValue(r)) as Sessao;

        void Tecla(Window w, Key k, PhysicalKey p, string? txt)
        {
            w.KeyPress(k, RawInputModifiers.None, p, txt);
            w.KeyRelease(k, RawInputModifiers.None, p, null);
        }
        // um "jogador": na tela da equipa sem janelas, A (automatico) e Enter (Jogar); numa janela,
        // Down (o 1o da lista) e Enter; parado 8 s, Escape (so com janela aberta)
        void Jogar(Window w, string e, double parado, long k)
        {
            bool dialogo = !e.Contains("dialogos=[]") && !e.Contains("sem-sessao");
            if (parado > 8 && dialogo && (int)(parado * 50) % 400 == 0) Tecla(w, Key.Escape, PhysicalKey.Escape, null);
            if (e.StartsWith("tela=TelaEquipa") && e.Contains("dialogos=[]")) Tecla(w, Key.A, PhysicalKey.A, "a");
            // (no convidado sobre a pagina da sala, o Down levaria o foco para o Sair dela)
            else if (dialogo && k % 2 == 0 && (w == jh || e.StartsWith("tela=Tela"))) Tecla(w, Key.Down, PhysicalKey.ArrowDown, null);
            // (no convidado, fora da tela da equipa e das janelas, o Enter cairia no Sair da pagina;
            // e so com a tela parada ha 0,3 s: a janela que acabou de fechar ainda aparece um instante)
            if (!e.Contains("sem-sessao") && (w == jh || ((dialogo || e.StartsWith("tela=TelaEquipa") || e.StartsWith("tela=TelaSorteioTimes") || e.StartsWith("tela=TelaClassificacao")) && parado > 0.3))) Tecla(w, Key.Enter, PhysicalKey.Enter, "\r");
        }

        (int, int) progresso = (-1, -1);
        string eh = "";
        var ec = new List<string>();
        var desdeC = new List<DateTime>();
        var desde = DateTime.UtcNow; var desdeH = DateTime.UtcNow;
        var ultimoLog = DateTime.UtcNow;
        long teclas = 0;
        bool convidou = false, demitiu = false;
        string ultimoHost = "";
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        t.Tick += (_, _) =>
        {
            var m = sessao.J.M;
            var p = (m.Ano, (int)m.Jornada);
            var e1 = Estado(sessao, fh.Child, janelasHost);
            if (p != progresso) { progresso = p; desde = DateTime.UtcNow; }
            if (e1 != eh) { eh = e1; desdeH = DateTime.UtcNow; desde = DateTime.UtcNow; }
            if (depois && clientes.Count == 0 && m.Jornada >= 1 && e1.StartsWith("tela=TelaEquipa"))
                for (int i = 0; i < nCli; i++) CriarCliente(i);
            while (ec.Count < clientes.Count) { ec.Add(""); desdeC.Add(DateTime.UtcNow); }
            var es = new string[clientes.Count];
            for (int i = 0; i < clientes.Count; i++)
            {
                es[i] = Estado(SessaoCli(clientes[i].Rem), clientes[i].F.Child, clientes[i].Janelas);
                if (es[i] != ec[i]) { ec[i] = es[i]; desdeC[i] = DateTime.UtcNow; desde = DateTime.UtcNow; }
                // o goleiro expulso: a janela das trocas aberta no aparelho do dono
                if (goleiroDe == clientes[i].Uid && es[i].Contains("TelaSubstituicao"))
                {
                    goleiroDe = null; goleiroJanela++;
                    Console.WriteLine($"  .. goleiro expulso: a janela das trocas abriu no {clientes[i].Nome}");
                }
            }
            string todos = $"H {e1} | " + string.Join(" | ", es.Select((x, i) => $"C{i + 1} {x}"));
            if (m.Ano - ano0 >= temporadas && m.Jornada >= 2 && m.Jornada <= 4)
            {
                Console.WriteLine($"OK: {temporadas} temporadas ({m.Ano}), {teclas} teclas");
                cts.Cancel();
                return;
            }
            if ((DateTime.UtcNow - ultimoLog).TotalSeconds >= 10)
            {
                ultimoLog = DateTime.UtcNow;
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} ano {m.Ano} jornada {m.Jornada} {todos}");
            }
            if ((DateTime.UtcNow - desde).TotalSeconds > travou)
            {
                Console.WriteLine($"TRAVOU ha {travou}s: ano {m.Ano} jornada {m.Jornada} {todos}");
                codigo = 2; cts.Cancel();
                return;
            }
            // SOAK_CONVITE=1: um convite de outro clube ao tecnico do 1o convidado (raro de verdade: um
            // clube 11 a 19 mais forte demitindo), pelo gancho do motor, numa thread como a do motor
            if (!convidou && clientes.Count > 0 && Environment.GetEnvironmentVariable("SOAK_CONVITE") == "1" && m.Jornada >= 3 && e1.StartsWith("tela=TelaEquipa"))
            {
                var eq = Esperar(() => sh.Jogadores()).FirstOrDefault(x => x.Uid == clientes[0].Uid)?.Equipa ?? 0;
                if (eq != 0 && m.Equipa(eq) is { } dele && m.TreinadorDa(dele) is { } tc)
                {
                    convidou = true;
                    var outra = m.Equipa(timeHost.ID)!;
                    new Thread(() => Console.WriteLine($"  == convite ao {clientes[0].Nome}: aceitou={sessao.J.H.Convite!(tc, outra)}")) { IsBackground = true }.Start();
                }
            }
            // SOAK_DEMITIR_HOST=1: o tecnico do host demitido (fica sem clube) na jornada 3
            if (!demitiu && Environment.GetEnvironmentVariable("SOAK_DEMITIR_HOST") == "1" && m.Jornada >= 3 && e1.StartsWith("tela=TelaEquipa"))
            {
                demitiu = true;
                var cpu = m.S.Treinadores.First(x => x.Humano == 0 && m.EquipaDoTreinador(x.ID) == null);
                m.Equipa(timeHost.ID)!.Treinador = cpu.ID;
                Console.WriteLine("  == o tecnico do host foi demitido");
            }
            if (demitiu && e1 != ultimoHost) { ultimoHost = e1; Console.WriteLine($"  .. host: {e1}"); }
            Jogar(jh, e1, (DateTime.UtcNow - desdeH).TotalSeconds, teclas);
            for (int i = 0; i < clientes.Count; i++) Jogar(clientes[i].W, es[i], (DateTime.UtcNow - desdeC[i]).TotalSeconds, teclas);
            teclas++;
        };
        t.Start();
        Dispatcher.UIThread.MainLoop(cts.Token);
        Console.WriteLine("perguntas aos convidados: " + string.Join(", ", perguntas.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}")));
        Console.WriteLine("respostas dos convidados: " + string.Join(", ", respostas.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}")));
        Console.WriteLine($"avisos de transferencia: {noticias}");
        Console.WriteLine("paralisacoes: " + string.Join(", ", pausas.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}")) + $" (goleiro expulso com a janela aberta: {goleiroJanela})");
        Console.WriteLine("janelas no host: " + string.Join(", ", janelasHost.Keys.OrderBy(x => x)));
        foreach (var c in clientes) Console.WriteLine($"janelas no {c.Nome}: " + string.Join(", ", c.Janelas.Keys.OrderBy(x => x)));
        var resumo = Esperar(() => Sala.LerResumo(banco, uidHost, sh.Codigo));
        Console.WriteLine($"resumo da sala: {resumo}");
        host.Dispose();
        foreach (var c in clientes) c.Cli.Dispose();
        // (modo amigo) o save volta a ser so do host: nenhum tecnico dos convidados
        if (amigo)
        {
            host.TirarConvidados();
            var sobra = sessao.J.M.S.Treinadores.Count(x => x.Humano != 0);
            Console.WriteLine($"modo amigo: tecnicos humanos no save depois de tirar os convidados = {sobra} (esperado 1)");
            if (sobra != 1) codigo = 1;
        }
        if (firebase) try { Esperar(() => sh.Apagar()); } catch { }
        try { Directory.Delete(pasta, true); } catch { }
        return codigo;
    }

    // (fora do laco da interface o Dispatcher e o contexto: esperar um Task do Firebase ali travava)
    static T Esperar<T>(Func<Task<T>> f) => Task.Run(f).GetAwaiter().GetResult();
    static void Esperar(Func<Task> f) => Task.Run(f).GetAwaiter().GetResult();

    static string? AcharJogo()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir != null; i++, dir = Path.GetDirectoryName(dir))
            if (RetroFoot.Motor.Arquivos.PastaClassica.Em(Path.GetDirectoryName(dir)).FirstOrDefault() is string g) return Path.GetFullPath(g);
        return null;
    }

    // o jogo novo com h humanos: o 1o (o host) onde caiu; os outros nos primeiros times da 1a divisao
    static Jogo98 JogoNovo(string raiz, Configuracoes cfg, int semente, int h)
    {
        var d = Dados.Ler(raiz);
        var (bosman, _) = cfg.ListasEstrangeiros(raiz);
        if (bosman != null) d.Bosman = d.Paises.Where(p => bosman.Contains(p.Codigo)).ToList();
        var sel = d.ContarEquipas();
        sel.Minimo = cfg.RegrasDoJogoNovo.MinimoEquipas;
        sel.Todos();
        sel.Confirmar(new Aleatorio((uint)semente * 7919u));
        var g = new Aleatorio((uint)semente * 104729u + 1);
        var e = Criacao.Criar(d, new NovoOpcoes
        {
            Moeda = Tabelas.MoedaReais, Ano = 2026, Regras = cfg.RegrasDoJogoNovo,
            Humanos = Enumerable.Range(0, h).Select(i => i == 0 ? "Host" : "Cliente" + i).ToList(),
        }, g);
        e.IniciarTemporada();
        e.PrepararJornada(1, g);
        var s = Jogo98.Ler(e.Save(Exportar.DataHora(DateTime.Now)).Gravar());
        var hs = s.Treinadores.Where(t => t.Humano != 0).ToList();
        var host = s.Equipas.First(q => q.Treinador == hs[0].ID);
        int k = 0;
        for (int i = 1; i < hs.Count; i++)
        {
            var eh = s.Equipas.First(q => q.Treinador == hs[i].ID);
            if (s.Divisoes[0].Equipas.Contains(eh.ID)) continue;
            while (s.Divisoes[0].Equipas[k] == host.ID || hs.Any(t => t.ID == s.Equipas.First(q => q.ID == s.Divisoes[0].Equipas[k]).Treinador)) k++;
            var ec = s.Equipas.First(q => q.ID == s.Divisoes[0].Equipas[k]);
            (eh.Treinador, ec.Treinador) = (ec.Treinador, eh.Treinador);
        }
        return Jogo98.Ler(s.Gravar());
    }
}
