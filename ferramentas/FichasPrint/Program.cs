using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using RetroFoot.App;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Save;

// uso: FichasPrint <save.e98> <pasta de saida> [plantel|leilao]
// (RETROFOOT_VISUAL=classico|moderno, RETROFOOT_CELULAR=1 para o celular)
sealed class App0 : Application
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
        AppBuilder.Configure<App0>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).WithInterFont().SetupWithoutStarting();
        var s = Jogo98.Ler(File.ReadAllBytes(a[0]));
        bool cel = Environment.GetEnvironmentVariable("RETROFOOT_CELULAR") == "1";
        var w = new Window { Width = cel ? 420 : 1280, Height = cel ? 860 : 800 };
        var raiz = new Border();
        w.Content = raiz;
        w.Show();
        var sessao = new Sessao(s, c => raiz.Child = c, Configuracoes.Ler().OpcoesDoJogo(s, Environment.GetEnvironmentVariable("RETROFOOT_JOGO") ?? ""));
        sessao.Mostrar(new Border { Background = Avalonia.Media.Brushes.DarkGreen });
        var humano = s.Equipas.First(e => s.Treinadores.Any(t => t.ID == e.Treinador && t.Humano != 0));
        // um atacante de outro time com gols, e uns jogos na temporada para a media
        var (v, p) = s.Equipas.Where(e => e != humano).SelectMany(e => e.Jogadores.Select(j => (e, j)))
            .OrderByDescending(x => Memoria.Historial(x.j, Memoria.HistGolos)).First();
        for (int i = 0; i < 12; i++) Memoria.SomaJogoNaTemporada(p);
        if (p.B14[0] == 0) p.B14[0] = 5;
        string modo = a.Length > 2 ? a[2] : "leilao";
        if (modo == "plantel") TelaPlantel.Abrir(sessao, v, humano, p.ID);
        else TelaLeilao.DeTodos.Abrir(sessao, v, p, 1_234_000);
        for (int i = 0; i < 20; i++) Dispatcher.UIThread.RunJobs();
        var f = w.CaptureRenderedFrame();
        var nome = Path.Combine(a[1], $"{modo}-{(cel ? "cel" : Environment.GetEnvironmentVariable("RETROFOOT_VISUAL") ?? "padrao")}.png");
        f!.Save(nome);
        Console.WriteLine(nome);
        return 0;
    }
}
