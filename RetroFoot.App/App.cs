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

    public override void OnFrameworkInitializationCompleted()
    {
        // No celular, a tela adaptada ao toque; no computador, a classica (800x600, fiel).
        // RETROFOOT_CELULAR=1 mostra a do celular no computador, para comparar.
        // o idioma das Configuracoes (e a traducao do motor) antes da 1a tela
        _ = Idioma.Atual;
        bool celular = OperatingSystem.IsAndroid() || Environment.GetEnvironmentVariable("RETROFOOT_CELULAR") == "1";
        if (!celular) FolgaDaBarra();
        // o texto inteiro numa dica onde ficou cortado (so com o rato)
        if (!OperatingSystem.IsAndroid()) Dicas.Ligar();
        var raiz = new Border { Background = new SolidColorBrush(Color.FromRgb(0, 128, 0)) };
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
            _ when celular => new TelaCelular(),
            _ => TelaInicio.ComSobre(Mostrar),
        };
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
            desktop.MainWindow = janela;
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime atividade)
            atividade.MainViewFactory = () => raiz;
        else if (ApplicationLifetime is ISingleViewApplicationLifetime unica)
            unica.MainView = raiz;
        base.OnFrameworkInitializationCompleted();
    }

    // A barra de rolagem do Fluent fica por cima do conteudo: quando um ScrollViewer rola na
    // vertical, o conteudo ganha FolgaBarra pontos a direita (somados ao Padding que ele ja
    // tinha), para nada ficar debaixo da barra; sem barra, o Padding de antes.
    public const double FolgaBarra = 18;
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
