using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using RetroFoot.App;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.NovoJogo;
using RetroFoot.Motor.Save;
using NovoOpcoes = RetroFoot.Motor.NovoJogo.Opcoes;
using Jogador = RetroFoot.Motor.Save.Jogador;

// O soak da interface: o jogo de verdade (Sessao, telas, dialogos, timers) numa janela
// headless, com um "jogador" que so aperta Enter (o botao padrao de cada tela: Jogar,
// Sim, OK, Continuar...). Se o motor nao avanca por TRAVOU segundos, mostra a tela e os
// dialogos abertos e o foco, e sai com codigo 2. Codigo 1 = excecao; 0 = jogou tudo.
//
//   dotnet run --project ferramentas/SoakUI -- [N] [mod|orig] [humanos] [temporadas] [semente]
//   (RETROFOOT_JOGO = pasta do jogo original; padrao <pasta ao lado do projeto>/game)
//   (ACRESCIMOS=1 = com os acrescimos, Regras.Acrescimos)
//   (SOBEM=1..4 = o K de "Sobem e descem por divisao", Regras.SobemDescemEscolhido; padrao automatico)
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
        int humanos = a.Length > 2 ? int.Parse(a[2]) : 1;
        int temporadas = a.Length > 3 ? int.Parse(a[3]) : 1;
        int semente = a.Length > 4 ? int.Parse(a[4]) : 1;
        int travou = int.TryParse(Environment.GetEnvironmentVariable("TRAVOU"), out var tv) ? tv : 60;
        var raiz = Environment.GetEnvironmentVariable("RETROFOOT_JOGO") ?? AcharJogo();
        if (raiz == null) { Console.Error.WriteLine("pasta do jogo nao encontrada"); return 3; }

        RetroFoot.App.Sessao.PausaFimJornadaMs = 0;   // sem a pausa de 3 s da tela final da jornada
        AppBuilder.Configure<SoakApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
        int codigo = 0;
        var cts = new CancellationTokenSource();
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Console.WriteLine("EXCECAO na interface: " + e.Exception);
            codigo = 1; e.Handled = true; cts.Cancel();
        };

        var cfg = new Configuracoes
        {
            Modificada = mod, EquipasPorDivisao = n, CartoesAmarelos = mod, Patrocinios = mod,
            // ACRESCIMOS=1: com os acrescimos (so na Modificada)
            Acrescimos = mod && Environment.GetEnvironmentVariable("ACRESCIMOS") == "1",
            SobemDescem = mod && int.TryParse(Environment.GetEnvironmentVariable("SOBEM"), out var k) && k is >= 1 and <= 4 ? k : 0,
        };
        var s = JogoNovo(raiz, cfg, humanos, semente);
        var op = cfg.OpcoesDoJogo(s, raiz) with
        {
            Marcas = RetroFoot.Motor.Patrocinio.Marcas.Ler(raiz),
            VerPenaltisCPU = true, VerSorteio = 0, VerExpulsoes = true, VerLesoes = true,
        };
        TelaLeilao.EsperaInicio = 0; TelaLeilao.EsperaFim = 0;
        var janela = new Window { Width = 1280, Height = 720 };
        var fundo = new Border();
        janela.Content = fundo;
        janela.Show();
        var arq = Path.Combine(Path.GetTempPath(), $"soakui-{Environment.ProcessId}.e98");
        var sessao = new Sessao(s, c => fundo.Child = c, op, arq) { MsPorMinuto = 0 };
        sessao.J.Treinador = "Soak";
        sessao.J.M.Retomar = -1;
        sessao.Erro += ex => { Console.WriteLine("EXCECAO no motor: " + ex); codigo = 1; cts.Cancel(); };
        sessao.Saiu += () => { Console.WriteLine("o motor saiu"); cts.Cancel(); };
        int ano0 = sessao.J.M.Ano;
        Console.WriteLine($"N={n} {(mod ? "Modificada" : "Original")} humanos={humanos} temporadas={temporadas} semente={semente} ano={ano0}" +
            (sessao.J.M.Regras.Acrescimos ? " acrescimos" : "") + $" K={sessao.J.M.Regras.SobemDescem}");
        sessao.Comecar();

        var camadaDialogo = typeof(Sessao).GetField("camadaDialogo", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var camadaTela = typeof(Sessao).GetField("camadaTela", BindingFlags.NonPublic | BindingFlags.Instance)!;
        string Estado()
        {
            var tela = (camadaTela.GetValue(sessao) as Border)?.Child;
            var dl = (camadaDialogo.GetValue(sessao) as Grid)!.Children.Select(c => (c.Tag?.GetType().Name ?? "?") + "#" + (c.Tag?.GetHashCode() % 1000)).ToList();
            var foco = janela.FocusManager?.GetFocusedElement();
            return $"tela={tela?.GetType().Name}#{tela?.GetHashCode() % 1000} dialogos=[{string.Join(",", dl)}] foco={foco?.GetType().Name}";
        }

        (int, int) progresso = (-1, -1);
        string estado = "";
        var desde = DateTime.UtcNow;
        var ultimoLog = DateTime.UtcNow;
        long teclas = 0;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        t.Tick += (_, _) =>
        {
            var m = sessao.J.M;
            var p = (m.Ano, (int)m.Jornada);
            var e = Estado();
            if (p != progresso || e != estado)
            {
                if (Environment.GetEnvironmentVariable("SOAK_VERBOSO") == "1") Console.WriteLine($"  {m.Ano}/{m.Jornada} {e}");
                progresso = p; estado = e; desde = DateTime.UtcNow;
            }
            if (m.Ano - ano0 >= temporadas && m.Jornada >= 2 && m.Jornada <= 4)
            {
                Console.WriteLine($"OK: {temporadas} temporadas ({m.Ano}), {teclas} teclas");
                cts.Cancel();
                return;
            }
            if ((DateTime.UtcNow - ultimoLog).TotalSeconds >= 10)
            {
                ultimoLog = DateTime.UtcNow;
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} ano {m.Ano} jornada {m.Jornada} {e}");
            }
            if ((DateTime.UtcNow - desde).TotalSeconds > travou)
            {
                Console.WriteLine($"TRAVOU ha {travou}s: ano {m.Ano} jornada {m.Jornada} {e}");
                var topo = (camadaDialogo.GetValue(sessao) as Grid)!.Children.LastOrDefault()?.Tag;
                if (topo != null)
                    foreach (var f in topo.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        var v = f.GetValue(topo);
                        string txt = v switch
                        {
                            Jogador j => $"jogador {j.ID} pos {j.Posicao}",
                            System.Collections.Generic.IEnumerable<Jogador> l => "[" + string.Join(",", l.Select(j => $"{j.ID}:pos{j.Posicao}")) + "]",
                            int or bool => v.ToString()!,
                            _ => "",
                        };
                        if (txt != "") Console.WriteLine($"  {f.Name} = {txt}");
                    }
                codigo = 2;
                cts.Cancel();
                return;
            }
            // a cada 8 segundos parado na mesma tela: Escape (fecha menus, recusa) e "A"
            // (escalacao automatica) antes do Enter
            double parado = (DateTime.UtcNow - desde).TotalSeconds;
            if (parado > 8 && (int)(parado * 50) % 400 == 0)
            {
                janela.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                janela.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            }
            // na tela da equipa sem dialogos: Seleccionar > Automatico (A) e Jogar (Enter)
            if (e.StartsWith("tela=TelaEquipa") && e.Contains("dialogos=[]"))
            {
                janela.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
                janela.KeyRelease(Key.A, RawInputModifiers.None, PhysicalKey.A, null);
            }
            // num dialogo com lista (substituicao forcada, penalti...): Down escolhe o 1o
            else if (!e.Contains("dialogos=[]") && teclas % 2 == 0)
            {
                janela.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                janela.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            }
            janela.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            janela.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            teclas++;
        };
        t.Start();
        Dispatcher.UIThread.MainLoop(cts.Token);
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

    // como a TelaInicio (todos os paises, moeda Reais); com 2+ humanos o 2o vai para a 1a divisao
    static Jogo98 JogoNovo(string raiz, Configuracoes cfg, int humanos, int semente)
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
            Humanos = Enumerable.Range(1, humanos).Select(i => "Bot" + i).ToList(),
        }, g);
        e.IniciarTemporada();
        e.PrepararJornada(1, g);
        var s = Jogo98.Ler(e.Save(Exportar.DataHora(DateTime.Now)).Gravar());
        if (humanos >= 2)
        {
            var hs = s.Treinadores.Where(t => t.Humano != 0).ToList();
            var eh = s.Equipas.First(q => q.Treinador == hs[1].ID);
            var ec = s.Equipas.First(q => q.ID == s.Divisoes[0].Equipas[0]);
            (eh.Treinador, ec.Treinador) = (ec.Treinador, eh.Treinador);
            s = Jogo98.Ler(s.Gravar());
        }
        return s;
    }
}
