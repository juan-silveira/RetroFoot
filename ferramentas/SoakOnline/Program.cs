using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
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
        var s = JogoNovo(raiz, cfg, semente);
        var hs = s.Treinadores.Where(t => t.Humano != 0).ToList();
        var timeHost = s.Equipas.First(q => q.Treinador == hs[0].ID);
        var timeCli = s.Equipas.First(q => q.Treinador == hs[1].ID);
        var op = cfg.OpcoesDoJogo(s, raiz) with
        {
            Marcas = RetroFoot.Motor.Patrocinio.Marcas.Ler(raiz),
            Patrocinios = true, VerPenaltisCPU = true, VerSorteio = 0, VerExpulsoes = true, VerLesoes = true,
        };
        TelaLeilao.EsperaInicio = 0; TelaLeilao.EsperaFim = 0;

        // a sala: o host e o cliente, cada um com o seu time
        var banco = new BancoMemoria();
        var sh = Sala.Criar(banco, "host", "Host", "0.2.0").GetAwaiter().GetResult();
        var sc = Sala.Entrar(banco, "cli", sh.Codigo, "Cliente").GetAwaiter().GetResult();
        sh.DarEquipa("host", timeHost.ID).GetAwaiter().GetResult();
        sh.DarEquipa("cli", timeCli.ID).GetAwaiter().GetResult();
        sh.DefinirPrazo(1).GetAwaiter().GetResult();

        // o que aconteceu: as perguntas para o cliente e as janelas de cada lado
        var perguntas = new ConcurrentDictionary<string, int>();
        var respostas = new ConcurrentDictionary<string, int>();
        var tipoDe = new ConcurrentDictionary<string, string>();
        banco.Observar($"{sh.Raiz}/perguntas/cli", (c, v) =>
        {
            if (c == "/" && v is JsonObject q && (string?)q["tipo"] is { } tipo && (string?)q["id"] is { } id && tipoDe.TryAdd(id, tipo))
            {
                perguntas.AddOrUpdate(tipo, 1, (_, x) => x + 1);
                Console.WriteLine($"  -> pergunta ao cliente: {tipo} {q.ToJsonString()}");
            }
        });
        banco.Observar($"{sh.Raiz}/respostas/cli", (c, v) =>
        {
            foreach (var (id, r) in c == "/" ? Nos.Filhos(v) : v == null ? [] : new[] { (c.Trim('/'), v) })
            {
                var tipo = tipoDe.GetValueOrDefault(id, "?");
                respostas.AddOrUpdate(tipo, 1, (_, x) => x + 1);
                Console.WriteLine($"  <- resposta do cliente: {tipo} {r.ToJsonString()}");
            }
        });
        // as paralisacoes: o intervalo de todos, e as do cliente (1 parou, 3 goleiro expulso, 4 intervalo)
        var pausas = new ConcurrentDictionary<string, int>();
        banco.Observar($"{sh.Raiz}/rodada/aoVivo/pausa", (c, v) =>
        {
            if (c != "/" || v is not JsonObject o) return;
            string k = (bool?)o["coletivo"] == true ? "intervalo-de-todos" : $"{o["uid"]}-motivo{o["motivo"]}";
            pausas.AddOrUpdate(k, 1, (_, x) => x + 1);
            if (!k.StartsWith("intervalo")) Console.WriteLine($"  .. pausa {o.ToJsonString()}");
        });
        int noticias = 0;
        banco.Observar($"{sh.Raiz}/rodada/noticia", (c, v) => { if (c == "/" && v != null) Interlocked.Increment(ref noticias); });
        var janelasHost = new ConcurrentDictionary<string, int>();
        var janelasCli = new ConcurrentDictionary<string, int>();

        // o host
        var jh = new Window { Width = 1280, Height = 720 };
        var fh = new Border();
        jh.Content = fh;
        jh.Show();
        var arq = Path.Combine(Path.GetTempPath(), $"soakonline-{Environment.ProcessId}.e98");
        var sessao = new Sessao(s, c => fh.Child = c, op, arq) { MsPorMinuto = 0 };
        sessao.J.Treinador = "Host e Cliente";
        sessao.J.M.Retomar = -1;
        var host = new HostOnline(sh, sessao.J, x => Dispatcher.UIThread.Invoke(x));
        sessao.LigarOnline(host);
        sessao.Erro += ex => { Console.WriteLine("EXCECAO no motor: " + ex); codigo = 1; cts.Cancel(); };
        sessao.Saiu += () => { Console.WriteLine("o motor saiu"); cts.Cancel(); };

        // o cliente
        var jc = new Window { Width = 1280, Height = 720 };
        var fc = new Border();
        jc.Content = fc;
        jc.Show();
        var cli = new ClienteOnline(sc);
        var remoto = new JogoRemoto(c => fc.Child = c, () => { Console.WriteLine("o cliente saiu do jogo"); codigo = 1; cts.Cancel(); }, sc, cli);

        int ano0 = sessao.J.M.Ano;
        Console.WriteLine($"N={n} {(mod ? "Modificada" : "Original")} temporadas={temporadas} semente={semente} sala={sh.Codigo} host={timeHost.ID} cliente={timeCli.ID}");
        sessao.Comecar();

        var camadaDialogo = typeof(Sessao).GetField("camadaDialogo", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var camadaTela = typeof(Sessao).GetField("camadaTela", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var remSessao = typeof(JogoRemoto).GetField("_sessao", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var remAoVivo = typeof(JogoRemoto).GetField("_sessaoAoVivo", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var remJornada = typeof(JogoRemoto).GetField("_jornada", BindingFlags.NonPublic | BindingFlags.Instance)!;
        string Estado(Sessao? ss, Control? fora, ConcurrentDictionary<string, int> janelas)
        {
            if (ss == null) return $"tela={fora?.GetType().Name} sem-sessao";
            var tela = (camadaTela.GetValue(ss) as Border)?.Child;
            var dl = (camadaDialogo.GetValue(ss) as Grid)!.Children.Select(c => c.Tag?.GetType().Name ?? c.GetType().Name).ToList();
            foreach (var d in dl) janelas.TryAdd(d, 0);
            return $"tela={tela?.GetType().Name} dialogos=[{string.Join(",", dl)}]";
        }
        var remPergunta = typeof(JogoRemoto).GetField("_sessaoPergunta", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Sessao? SessaoCli() => (remJornada.GetValue(remoto) != null ? remAoVivo.GetValue(remoto) : remSessao.GetValue(remoto) ?? remPergunta.GetValue(remoto)) as Sessao;

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
            else if (dialogo && k % 2 == 0) Tecla(w, Key.Down, PhysicalKey.ArrowDown, null);
            // (no cliente, fora da tela da equipa e das janelas, o Enter cairia no Sair da pagina)
            if (!e.Contains("sem-sessao") && (w == jh || dialogo || e.StartsWith("tela=TelaEquipa"))) Tecla(w, Key.Enter, PhysicalKey.Enter, "\r");
        }

        (int, int) progresso = (-1, -1);
        string eh = "", ec = "";
        var desde = DateTime.UtcNow; var desdeH = DateTime.UtcNow; var desdeC = DateTime.UtcNow;
        var ultimoLog = DateTime.UtcNow;
        long teclas = 0;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        t.Tick += (_, _) =>
        {
            var m = sessao.J.M;
            var p = (m.Ano, (int)m.Jornada);
            var e1 = Estado(sessao, fh.Child, janelasHost);
            var e2 = Estado(SessaoCli(), fc.Child, janelasCli);
            if (p != progresso) { progresso = p; desde = DateTime.UtcNow; }
            if (e1 != eh) { eh = e1; desdeH = DateTime.UtcNow; desde = DateTime.UtcNow; }
            if (e2 != ec) { ec = e2; desdeC = DateTime.UtcNow; desde = DateTime.UtcNow; }
            if (Environment.GetEnvironmentVariable("SOAK_VERBOSO") == "1" && DateTime.UtcNow == desde) Console.WriteLine($"  {m.Ano}/{m.Jornada} H {e1} | C {e2}");
            if (m.Ano - ano0 >= temporadas && m.Jornada >= 2 && m.Jornada <= 4)
            {
                Console.WriteLine($"OK: {temporadas} temporadas ({m.Ano}), {teclas} teclas");
                cts.Cancel();
                return;
            }
            if ((DateTime.UtcNow - ultimoLog).TotalSeconds >= 10)
            {
                ultimoLog = DateTime.UtcNow;
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} ano {m.Ano} jornada {m.Jornada} H {e1} | C {e2}");
            }
            if ((DateTime.UtcNow - desde).TotalSeconds > travou)
            {
                Console.WriteLine($"TRAVOU ha {travou}s: ano {m.Ano} jornada {m.Jornada} H {e1} | C {e2}");
                codigo = 2; cts.Cancel();
                return;
            }
            Jogar(jh, e1, (DateTime.UtcNow - desdeH).TotalSeconds, teclas);
            Jogar(jc, e2, (DateTime.UtcNow - desdeC).TotalSeconds, teclas);
            teclas++;
        };
        t.Start();
        Dispatcher.UIThread.MainLoop(cts.Token);
        Console.WriteLine("perguntas ao cliente: " + string.Join(", ", perguntas.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}")));
        Console.WriteLine("respostas do cliente: " + string.Join(", ", respostas.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}")));
        Console.WriteLine($"avisos de transferencia: {noticias}");
        Console.WriteLine("paralisacoes: " + string.Join(", ", pausas.OrderBy(x => x.Key).Select(x => $"{x.Key}={x.Value}")));
        Console.WriteLine("janelas no host: " + string.Join(", ", janelasHost.Keys.OrderBy(x => x)));
        Console.WriteLine("janelas no cliente: " + string.Join(", ", janelasCli.Keys.OrderBy(x => x)));
        host.Dispose(); cli.Dispose();
        try { File.Delete(arq); } catch { }
        return codigo;
    }

    static string? AcharJogo()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir != null; i++, dir = Path.GetDirectoryName(dir))
            if (RetroFoot.Motor.Arquivos.PastaClassica.Em(Path.GetDirectoryName(dir)).FirstOrDefault() is string g) return Path.GetFullPath(g);
        return null;
    }

    // como o SoakUI com 2 humanos: o 2o vai para a 1a divisao
    static Jogo98 JogoNovo(string raiz, Configuracoes cfg, int semente)
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
            Humanos = new List<string> { "Host", "Cliente" },
        }, g);
        e.IniciarTemporada();
        e.PrepararJornada(1, g);
        var s = Jogo98.Ler(e.Save(Exportar.DataHora(DateTime.Now)).Gravar());
        var hs = s.Treinadores.Where(t => t.Humano != 0).ToList();
        var eh = s.Equipas.First(q => q.Treinador == hs[1].ID);
        var ec = s.Equipas.First(q => q.ID == s.Divisoes[0].Equipas[0]);
        (eh.Treinador, ec.Treinador) = (ec.Treinador, eh.Treinador);
        return Jogo98.Ler(s.Gravar());
    }
}
