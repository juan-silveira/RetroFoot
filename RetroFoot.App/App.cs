using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;

namespace RetroFoot.App;

public class App : Application
{
    // Do comeco do processo ate a 1a tela desenhada (mostrado no canto, para medir)
    public static readonly Stopwatch Relogio = Stopwatch.StartNew();

    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
    }

    // um erro que fecharia o jogo fica escrito em data/erros.log (com a hora e a pilha), para
    // saber o que foi; os da interface e das tarefas esquecidas ficam so no arquivo, sem fechar
    static void RegistrarErros()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Anotar("fatal", e.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => { Anotar("tarefa", e.Exception); e.SetObserved(); };
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) => Anotar("interface", e.Exception);
    }

    public static void Anotar(string onde, Exception? ex)
    {
        try
        {
            var arq = System.IO.Path.Combine(DadosJogo.Raiz, "erros.log");
            if (System.IO.File.Exists(arq) && new System.IO.FileInfo(arq).Length > 512 * 1024) System.IO.File.Delete(arq);
            System.IO.File.AppendAllText(arq, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{onde}] {ex}\n\n");
        }
        catch { }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // No celular, a tela adaptada ao toque; no computador, a classica (800x600, fiel).
        // RETROFOOT_CELULAR=1 mostra a do celular no computador, para comparar.
        // o idioma das Configuracoes (e a traducao do motor) antes da 1a tela
        _ = Idioma.Atual;
        RegistrarErros();
        bool celular = Celular.Ativo;
        if (!celular) FolgaDaBarra();
        // o texto inteiro numa dica onde ficou cortado (so com o rato)
        if (!OperatingSystem.IsAndroid()) Dicas.Ligar();
        var raiz = new Border { Background = new ImmutableSolidColorBrush(Color.FromRgb(0, 128, 0)) };
        Raiz = raiz;
        void Mostrar(Control t) => raiz.Child = t;
        // RETROFOOT_TELA=equipa|jornada|classificacao: uma tela sozinha, com dados de teste
        raiz.Child = Environment.GetEnvironmentVariable("RETROFOOT_TELA") switch
        {
            "equipa" => TelaEquipa.Demo(),
            "jornada" => TelaJornada.Demo(),
            "classificacao" => TelaClassificacao.Demo(),
            "configuracoes" => TelaConfiguracoes.Demo(),
            "dinheiro" => TelaDinheiro.Demo(),
            "procura" or "plantel" or "observados" => TelaProcura.Demo(),
            "leilao" => TelaLeilao.Demo(),
            "partida" => LigacaoPartida.Demo(),
            "dialogos" => DemoDialogos.Demo(),
            "hub" => new TelaHub(Mostrar),
            "inicio" => new TelaInicio(Mostrar),
            "online" => new TelaOnline(Mostrar, () => Mostrar(new TelaHub(Mostrar))),
            // a prova antiga da tela da equipa no celular (so para consulta)
            "celular" => new TelaCelular(),
            _ when celular => new TelaInicio(Mostrar),
            _ => TelaInicio.ComSobre(Mostrar),
        };
        // o Voltar do Android (e o Esc da previa): primeiro a janela do celular de cima
        // o Voltar que fecharia o app (nada mais responde a ele): antes, a pergunta se quer sair
        var camadaSair = new Grid { IsVisible = false, Background = TemaVerde.Veu };
        void PerguntarSair()
        {
            if (camadaSair.IsVisible) return;
            void Ficar() { camadaSair.IsVisible = false; camadaSair.Children.Clear(); }
            var f = CUi.PerguntaEmColuna(Idioma.T("Sair do RetroFoot?"), Idioma.T("O jogo em andamento que não foi salvo se perde."),
                new (string, bool, Action)[] { (Idioma.T("Sair"), true, () => { Ficar(); Fechar(); }), (Idioma.T("Cancelar"), false, Ficar) }, Ficar);
            f.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
            camadaSair.Children.Add(f);
            camadaSair.IsVisible = true;
        }
        // (o Android chama Voltar direto da atividade: o BackRequested do Avalonia nao chega)
        Voltar = () => { if (!CUi.Folha.Voltar()) PerguntarSair(); };
        if (celular)
            raiz.AttachedToVisualTree += (_, _) =>
            {
                if (TopLevel.GetTopLevel(raiz) is { } tl)
                    tl.BackRequested += (_, e) =>
                    {
                        if (e.Handled) return;
                        e.Handled = true;
                        Voltar();
                    };
            };
        // (no celular, a camada da pergunta por cima das telas)
        Control vista = celular ? new Grid { Children = { raiz, camadaSair } } : raiz;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            bool retrato = Environment.GetEnvironmentVariable("RETROFOOT_RETRATO") == "1";
            var janela = new Window
            {
                Title = TelaAcerca.Nome,
                Icon = Logotipo.IconeJanela(),
                // a tela do computador na caixa do formato (4:3 ou 16:9), preta em volta
                Content = celular || retrato ? raiz : new CaixaFormato { Child = raiz },
                Background = Brushes.Black,
                // RETROFOOT_RETRATO=1: janela do tamanho de um celular em pe (previa)
                Width = retrato ? 390 : 1280,
                Height = retrato ? 740 : 720,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            // no formato das Configuracoes (4:3 ou 16:9), pelo tamanho util da tela, no centro
            bool Dimensionar(bool mover)
            {
                var t = (mover ? janela.Screens.ScreenFromWindow(janela) : null) ?? janela.Screens.Primary;
                if (t == null) return false;
                var area = t.WorkingArea.Size.ToSize(t.Scaling);
                var tam = Formato.Janela(area);
                janela.Width = tam.Width;
                janela.Height = tam.Height;
                if (mover)
                {
                    var wa = t.WorkingArea;
                    janela.Position = new PixelPoint(wa.X + (int)((wa.Width - tam.Width * t.Scaling) / 2),
                        wa.Y + (int)((wa.Height - tam.Height * t.Scaling) / 2));
                }
                return true;
            }
            if (!retrato && !celular)
            {
                // (antes de abrir a janela as telas podem ainda nao ser conhecidas: entao ao abrir)
                if (!Dimensionar(false)) janela.Opened += (_, _) => Dimensionar(true);
                // mudou nas Configuracoes: a janela passa ao outro formato (se nao estiver maximizada)
                Formato.Mudou += () => { if (janela.WindowState == WindowState.Normal) Dimensionar(true); };
            }
            janela.Closing += (_, _) => AvisarFechando();
            desktop.MainWindow = janela;
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime atividade)
            atividade.MainViewFactory = () => vista;
        else if (ApplicationLifetime is ISingleViewApplicationLifetime unica)
            unica.MainView = vista;
        base.OnFrameworkInitializationCompleted();
    }

    // A barra de rolagem do Fluent fica por cima do conteudo: quando um ScrollViewer rola na
    // vertical, o conteudo ganha FolgaBarra pontos a direita (somados ao Padding que ele ja
    // tinha), para nada ficar debaixo da barra; sem barra, o Padding de antes.
    public const double FolgaBarra = 18;

    // o app vai fechar (a janela do computador fechando; a atividade do Android terminando): o
    // jogo com amigo apaga a sala, para os amigos serem desconectados
    public static event Action? Fechando;
    public static void AvisarFechando() { try { Fechando?.Invoke(); } catch { } }

    // fecha o app (o Android liga: termina a atividade); sem ele, o fim do programa do computador
    public static Action? FecharApp;
    // o Voltar do celular: a folha de cima, a pagina, ou a pergunta se quer sair do app
    public static Action? Voltar { get; private set; }
    static void Fechar()
    {
        if (FecharApp != null) { FecharApp(); return; }
        if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
    }

    // a raiz das telas (o Android a redesenha ao voltar ao app)
    public static Control? Raiz { get; private set; }
    static readonly ConditionalWeakTable<ScrollViewer, StrongBox<Thickness>> _padding = new();

    static void FolgaDaBarra()
    {
        static void Ver(ScrollViewer sv)
        {
            var antes = _padding.GetValue(sv, v => new StrongBox<Thickness>(v.Padding)).Value;
            bool rola = sv.VerticalScrollBarVisibility is Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                    or Avalonia.Controls.Primitives.ScrollBarVisibility.Visible
                && sv.Extent.Height > sv.Viewport.Height + 0.5;
            var p = rola ? new Thickness(antes.Left, antes.Top, antes.Right + FolgaBarra, antes.Bottom) : antes;
            if (sv.Padding != p) sv.Padding = p;
        }
        ScrollViewer.ExtentProperty.Changed.AddClassHandler<ScrollViewer>((sv, _) => Ver(sv));
        ScrollViewer.ViewportProperty.Changed.AddClassHandler<ScrollViewer>((sv, _) => Ver(sv));
    }
}
