using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RetroFoot.Motor.Arquivos;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// O hub do RetroFoot (a 1a tela depois do Sobre): a capa da marca com o Jogar e os botoes das
// ferramentas, Configuracoes, Sobre, a pasta de dados e Sair. Um tema so, o mesmo nos dois
// visuais; as ferramentas e as Configuracoes abrem no visual escolhido (Ui.cs).
// Jogar abre o comeco de sempre (TelaInicio: jogo novo ou continuar); as ferramentas vem de
// Ferramentas.Todas (as sem tela abrem "Em breve"). Cada ferramenta abre na pagina inteira,
// com a barra de cima (Voltar, o icone e o titulo).
// A area logica e 800x600 (4:3) ou 1067x600 (16:9), num Viewbox, como as telas do Classico.
public sealed class TelaHub : UserControl
{
    readonly Action<Control> mostrar;
    readonly Grid quadro = new() { Height = 600 };
    readonly Panel pagina = new();
    readonly Panel sobre = new();
    Control? foco;
    Action? aoEsc;
    readonly List<Control> ordem = new();   // os botoes/cartoes, na ordem das setas

    static bool M => Visual.Moderno;

    public TelaHub(Action<Control> mostrar)
    {
        this.mostrar = mostrar;
        Focusable = true;
        quadro.Children.Add(pagina);
        quadro.Children.Add(sobre);
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = quadro };
        DadosJogo.Preparar();
        Escudos.Recarregar();
        Largura();
        MontarHub();
        ProcurarAtualizacao();
        // RETROFOOT_FERRAMENTA=<id> (com RETROFOOT_TELA=hub): abre a ferramenta direto (testes, capturas)
        if (Environment.GetEnvironmentVariable("RETROFOOT_FERRAMENTA") is { Length: > 0 } fid && Ferramentas.Achar(fid) is { } fx)
            Dispatcher.UIThread.Post(() => AbrirFerramenta(fx), DispatcherPriority.Loaded);
        Action mudou = () => Dispatcher.UIThread.Post(() => { Largura(); MontarHub(); });
        Action ferr = () => Dispatcher.UIThread.Post(MontarHub);
        AttachedToVisualTree += (_, _) =>
        {
            Formato.Mudou -= mudou; Formato.Mudou += mudou;
            Ferramentas.Mudou -= ferr; Ferramentas.Mudou += ferr;
        };
        DetachedFromVisualTree += (_, _) => { Formato.Mudou -= mudou; Ferramentas.Mudou -= ferr; };
    }

    void Largura() => quadro.Width = Formato.Largo ? 1067 : 800;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(Focar, DispatcherPriority.Loaded);
    }

    void Focar()
    {
        if (sobre.Children.Count > 0) return;
        if (foco is { IsEffectivelyVisible: true } f && f.Focus()) return;
        Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || sobre.Children.Count > 0) return;
        if (e.Key == Key.Escape && aoEsc != null) { aoEsc(); e.Handled = true; }
        // setas: vao para o botao mais perto naquela direcao
        else if (e.Key is Key.Right or Key.Down or Key.Left or Key.Up && ordem.Count > 0)
        {
            var atual = ordem.Find(c => c.IsFocused);
            (atual == null ? ordem[0] : Vizinho(atual, e.Key) ?? atual).Focus(NavigationMethod.Directional);
            e.Handled = true;
        }
        // Enter sem nada com foco: Jogar
        else if (e.Key == Key.Enter && ordem.Count > 0 && !ordem.Any(c => c.IsFocused))
        {
            ((Clicavel)ordem[0]).Clicar();
            e.Handled = true;
        }
    }

    // o botao mais perto de c na direcao da seta (o desvio de lado pesa mais)
    Control? Vizinho(Control c, Key k)
    {
        Point? Centro(Control x) => x.TranslatePoint(new Point(x.Bounds.Width / 2, x.Bounds.Height / 2), quadro);
        if (Centro(c) is not { } o) return null;
        Control? melhor = null;
        double menor = double.MaxValue;
        foreach (var x in ordem)
        {
            if (x == c || Centro(x) is not { } p) continue;
            double dx = p.X - o.X, dy = p.Y - o.Y;
            double frente = k switch { Key.Right => dx, Key.Left => -dx, Key.Down => dy, _ => -dy };
            double lado = k is Key.Right or Key.Left ? Math.Abs(dy) : Math.Abs(dx);
            if (frente <= 1) continue;
            double d = frente + lado * 2;
            if (d < menor) { menor = d; melhor = x; }
        }
        return melhor;
    }

    // o que o hub oferece, na ordem: Jogar, as ferramentas e o resto
    sealed record Item(string Id, string Icone, string Titulo, string Descricao, Action Clique);

    List<Item> Itens()
    {
        var l = new List<Item>
        {
            new("jogar", "jogar", T("Jogar"), T("Comece um jogo novo ou continue um jogo gravado."), Jogar),
            new("online", "jogar", T("Jogo online"), T("Jogue com amigos em outros aparelhos (até 6 técnicos)."), () => mostrar(new TelaOnline(mostrar, () => mostrar(new TelaHub(mostrar))))),
        };
        foreach (var f in Ferramentas.Todas)
        {
            var ff = f;
            l.Add(new(f.Id, f.Icone, T(f.Titulo), T(f.Descricao), () => AbrirFerramenta(ff)));
        }
        l.Add(new("config", "config", T("Configurações"), T("Idioma, visual, formato e regras do jogo"), AbrirConfiguracoes));
        l.Add(new("sobre", "info", T("Sobre"), T("Versão e créditos"), AbrirSobre));
        if (DadosJogo.PodeAbrirPasta)
            l.Add(new("pasta", "pasta", T("Abrir pasta de dados"), T("Equipas, logos, saves e patches"), AbrirPasta));
        l.Add(new("sair", "sair", T("Sair"), T("Fecha o RetroFoot"), Sair));
        return l;
    }

    void MontarHub()
    {
        ordem.Clear();
        pagina.Children.Clear();
        pagina.Children.Add(Hub());
        aoEsc = PerguntarSair;
        Dispatcher.UIThread.Post(Focar, DispatcherPriority.Loaded);
    }

    static string Convertido(PastaDados.Relatorio r) =>
        T("Arquivos Retrô convertidos: {0} equipas", r.Equipas) + (r.Outros.Count > 0 ? "; " + string.Join(", ", r.Outros) : "");

    // ---------------------------------------------------------------- o hub
    // Um tema so (o mesmo no Classico e no Moderno): a capa da marca enche a tela (16:9 ou 4:3,
    // cortada no centro), o nome RETROFOOT em cima da bola, o Jogar amarelo embaixo dela e os
    // outros botoes em duas colunas dos lados (ferramentas a esquerda, o resto a direita), sem
    // tapar a bola. A bola da capa ocupa x 315..750 (16:9) ou 185..615 (4:3) e y 87..512.
    static readonly Color VerdeEscuro = TemaVerde.Cores.MarcaVerdeEscuro, Verde = TemaVerde.Cores.MarcaVerde,
        Amarelo = TemaVerde.Cores.MarcaAmarelo, Ouro = TemaVerde.Cores.MarcaOuro;

    static IBrush Cor(Color c, byte a) => new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));

    // as cores das ferramentas (a barra de cima da pagina de cada uma, no visual escolhido)
    static Color CorItem(string id) => !M ? Amarelo : Color.Parse(id switch
    {
        Ferramentas.EditorEquipas => "#8ab4f8", Ferramentas.EditorSave => "#c58af9", Ferramentas.Scout => "#fdd663",
        Ferramentas.Patches => "#81c995", Ferramentas.Arbitros => "#f28b82", _ => "#8ab4f8",
    });

    static IBrush FundoChip(Color cor) => M ? new SolidColorBrush(Color.FromArgb(0x2E, cor.R, cor.G, cor.B)) : UiClassico.VerdeTopo;

    Control Hub()
    {
        bool largo = Formato.Largo;
        quadro.Background = new SolidColorBrush(VerdeEscuro);
        var raiz = new Grid();
        raiz.Children.Add(Logotipo.ImagemCapa(largo));

        // o nome por cima da bola (na grade de tatica da capa)
        var nome = Logotipo.ImagemNome(largo ? 66 : 60);
        nome.HorizontalAlignment = HorizontalAlignment.Center;
        nome.VerticalAlignment = VerticalAlignment.Top;
        nome.Margin = new Thickness(0, 12, 0, 0);
        nome.Effect = new DropShadowEffect { OffsetX = 0, OffsetY = 3, BlurRadius = 12, Color = Color.FromArgb(0xA0, 0, 0, 0) };
        raiz.Children.Add(nome);

        // a versao, pequena, no canto de cima
        var versao = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 12, 14, 0),
            Padding = new Thickness(9, 3), CornerRadius = new CornerRadius(10), Background = Cor(VerdeEscuro, 0xB0),
            BorderBrush = Cor(Amarelo, 0x40), BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = T("Versão {0}", TelaAcerca.Versao), FontFamily = Tema.Inter, FontSize = 11, Foreground = Cor(Colors.White, 0xD0) },
        };
        ToolTip.SetTip(versao, TelaAcerca.Slogan);
        raiz.Children.Add(versao);

        var itens = Itens();
        var jogar = BotaoJogar(itens[0]);
        foco = jogar;
        ordem.Add(jogar);
        raiz.Children.Add(jogar);

        // as colunas dos lados: as ferramentas a esquerda, o resto a direita
        var resto = new HashSet<string> { "config", "sobre", "pasta", "sair" };
        double larg = largo ? 272 : 166, alt = largo ? 64 : 52;
        StackPanel Coluna(IEnumerable<Item> l, HorizontalAlignment lado)
        {
            var p = new StackPanel
            {
                Spacing = largo ? 12 : 10, Width = larg, HorizontalAlignment = lado, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(largo ? 24 : 10, 0, largo ? 24 : 10, 0),
            };
            foreach (var it in l)
            {
                var b = Botao(it, largo);
                b.MinHeight = alt;
                ordem.Add(b);
                p.Children.Add(b);
            }
            return p;
        }
        raiz.Children.Add(Coluna(itens.Skip(1).Where(i => !resto.Contains(i.Id)), HorizontalAlignment.Left));
        raiz.Children.Add(Coluna(itens.Where(i => resto.Contains(i.Id)), HorizontalAlignment.Right));
        return Ui.Pagina(raiz);
    }

    // o Jogar: a pilula amarela/ouro da marca embaixo da bola
    static Clicavel BotaoJogar(Item it)
    {
        IBrush Degrade(string a, string b, string c) => new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse(a), 0), new GradientStop(Color.Parse(b), 0.45), new GradientStop(Color.Parse(c), 1) },
        };
        var conteudo = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Ui.Icone("jogar", 22, new SolidColorBrush(VerdeEscuro)),
                new TextBlock
                {
                    Text = it.Titulo, FontFamily = Tema.Inter, FontSize = 25, FontWeight = FontWeight.ExtraBold,
                    Foreground = new SolidColorBrush(VerdeEscuro), VerticalAlignment = VerticalAlignment.Center,
                },
            },
        };
        var c = new Clicavel
        {
            Child = conteudo, MinWidth = 250, Height = 58, Padding = new Thickness(36, 0, 40, 0), CornerRadius = new CornerRadius(29),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 16),
            Background = Degrade("#FCE46A", "#F8D734", "#DDA917"), Hover = Degrade("#FFEC8A", "#FFE04A", "#EDB92A"),
            BorderBrush = Cor(Colors.White, 0x90), BorderThickness = new Thickness(2), CorFoco = Brushes.White,
            Solto = BoxShadows.Parse("0 6 18 0 #90000000"), Premido = BoxShadows.Parse("0 2 6 0 #90000000"),
        };
        c.BoxShadow = c.Solto.Value;
        Premir(c);
        ToolTip.SetTip(c, it.Descricao);
        c.Clique += it.Clique;
        Ui.Nome(c, it.Titulo);
        return c;
    }

    // um botao dos lados: o icone amarelo num circulo, o titulo branco (e a descricao no 16:9);
    // fundo verde-escuro meio transparente
    static Clicavel Botao(Item it, bool largo)
    {
        string dica = it.Descricao;
        if (it.Id == "pasta")
        {
            dica = DadosJogo.Raiz;
            if (DadosJogo.UltimaConversao is { } r && (r.Equipas > 0 || r.Outros.Count > 0)) dica += "\n" + Convertido(r);
        }
        var chip = new Border
        {
            Width = largo ? 36 : 30, Height = largo ? 36 : 30, CornerRadius = new CornerRadius(largo ? 18 : 15), VerticalAlignment = VerticalAlignment.Center,
            Background = Cor(Color.Parse("#062A15"), 0x99), Child = Ui.Icone(it.Icone, largo ? 19 : 16, new SolidColorBrush(Amarelo)),
        };
        var textos = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        textos.Children.Add(new TextBlock
        {
            Text = it.Titulo, FontFamily = Tema.Inter, FontSize = largo ? 15 : 13, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap, MaxLines = largo ? 2 : 3, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = largo ? 18 : 15.5,
        });
        if (largo)
            textos.Children.Add(new TextBlock
            {
                Text = it.Descricao, FontFamily = Tema.Inter, FontSize = 11.5, Foreground = Ui.Hex("#C8E6D2"),
                TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 14,
            });
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = largo ? 12 : 9 };
        g.Children.Add(chip);
        Grid.SetColumn(textos, 1);
        g.Children.Add(textos);
        var c = new Clicavel
        {
            Child = g, Padding = largo ? new Thickness(12, 8, 12, 8) : new Thickness(9, 6, 8, 6), CornerRadius = new CornerRadius(12),
            Background = Cor(VerdeEscuro, 0xC4), Hover = Cor(Verde, 0xE6),
            BorderBrush = Cor(Amarelo, 0x48), BorderThickness = new Thickness(1.5), CorFoco = new SolidColorBrush(Amarelo),
            Solto = BoxShadows.Parse("0 4 12 0 #60000000"), Premido = BoxShadows.Parse("0 1 3 0 #60000000"),
        };
        c.BoxShadow = c.Solto.Value;
        Premir(c);
        ToolTip.SetTip(c, dica);
        c.Clique += it.Clique;
        Ui.Nome(c, it.Titulo);
        return c;
    }

    // premido: encolhe um pouco
    static void Premir(Clicavel c)
    {
        c.RenderTransformOrigin = RelativePoint.Center;
        c.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(c).Properties.IsLeftButtonPressed) c.RenderTransform = new ScaleTransform(0.96, 0.96);
        }, RoutingStrategies.Tunnel);
        c.AddHandler(PointerReleasedEvent, (_, _) => c.RenderTransform = null, RoutingStrategies.Tunnel);
        c.PointerExited += (_, _) => c.RenderTransform = null;
    }

    // Esc no hub: pergunta se sai do RetroFoot
    void PerguntarSair()
    {
        void Fechar() { sobre.Children.Clear(); Focar(); }
        Clicavel B(string texto, bool sim, Action a)
        {
            var b = new Clicavel
            {
                Child = new TextBlock
                {
                    Text = texto, FontFamily = Tema.Inter, FontSize = 14, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = sim ? new SolidColorBrush(VerdeEscuro) : Brushes.White,
                },
                MinWidth = 96, Padding = new Thickness(16, 8), CornerRadius = new CornerRadius(18),
                Background = sim ? new SolidColorBrush(Amarelo) : Cor(Verde, 0x80), Hover = sim ? new SolidColorBrush(Ouro) : Cor(Verde, 0xD0),
                BorderBrush = Cor(Colors.White, 0x40), BorderThickness = new Thickness(2), CorFoco = Brushes.White,
            };
            Premir(b);
            b.Clique += a;
            Ui.Nome(b, texto);
            return b;
        }
        var sim = B(T("Sim"), true, Sair);
        var nao = B(T("Não"), false, Fechar);
        var caixa = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MinWidth = 320,
            Padding = new Thickness(26, 22), CornerRadius = new CornerRadius(16), Background = Cor(Color.Parse("#0A4523"), 0xF2),
            BorderBrush = new SolidColorBrush(Amarelo), BorderThickness = new Thickness(1.5), BoxShadow = BoxShadows.Parse("0 12 36 0 #A0000000"),
            Child = new StackPanel
            {
                Spacing = 18,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center,
                        Children =
                        {
                            Ui.Icone("sair", 20, new SolidColorBrush(Amarelo)),
                            new TextBlock { Text = T("Sair do RetroFoot?"), FontFamily = Tema.Inter, FontSize = 18, FontWeight = FontWeight.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center },
                        },
                    },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, Children = { sim, nao } },
                },
            },
        };
        var veu = new Panel { Background = Ui.Hex("#80000000"), Children = { caixa } };
        veu.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Fechar(); }
            else if (e.Key is Key.Left or Key.Right) { (sim.IsFocused ? nao : sim).Focus(NavigationMethod.Directional); e.Handled = true; }
        };
        sobre.Children.Clear();
        sobre.Children.Add(veu);
        Dispatcher.UIThread.Post(() => sim.Focus(NavigationMethod.Directional), DispatcherPriority.Loaded);
    }

    // ---------------------------------------------------------------- atualizacao (OTA)
    // uma vez por execucao: ao abrir o hub, procura uma versao nova (Atualizacao.cs) e, se
    // houver, pergunta se atualiza agora
    static bool _procurouAtualizacao;

    void ProcurarAtualizacao()
    {
        if (_procurouAtualizacao || !Atualizacao.Suportada) return;
        _procurouAtualizacao = true;
        Atualizacao.LimparAntigo();
        Task.Run(Atualizacao.Procurar).ContinueWith(t =>
        {
            if (t.Result is { } nova) Dispatcher.UIThread.Post(() => OferecerAtualizacao(nova));
        });
    }

    void OferecerAtualizacao(Atualizacao.Nova nova)
    {
        void Fechar() { sobre.Children.Clear(); Focar(); }
        var texto = Ui.Txt(T("A versão {0} do RetroFoot está disponível (você tem a {1}). Seus saves, equipas e configurações ficam como estão.",
            nova.Versao.ToString(3), Atualizacao.Atual.ToString(3)), 13.5);
        texto.TextWrapping = TextWrapping.Wrap;
        var estado = Ui.Txt("", 12.5);
        var depois = Ui.Botao(T("Depois"), Fechar);
        Clicavel? agora = null;
        bool baixando = false;
        agora = Ui.Botao(T("Atualizar agora"), async () =>
        {
            if (baixando) return;
            baixando = true;
            agora!.IsEnabled = depois.IsEnabled = false;
            var prog = new Progress<double>(f => estado.Text = T("Baixando... {0}%", (int)Math.Round(f * 100)));
            try
            {
                estado.Text = T("Baixando... {0}%", 0);
                var exe = await Atualizacao.Instalar(nova, prog);
                estado.Text = T("Pronto. Abrindo a versão nova...");
                await Task.Delay(600);
                Atualizacao.Reabrir(exe, Sair);
            }
            catch (Exception ex)
            {
                estado.Text = T("Não consegui atualizar: {0}", ex.Message);
                baixando = false;
                agora.IsEnabled = depois.IsEnabled = true;
            }
        }, true);
        var botoes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { depois, agora } };
        var corpo = new StackPanel { Spacing = 14, Margin = new Thickness(18, 14), Width = 420, Children = { texto, estado, botoes } };
        sobre.Children.Clear();
        sobre.Children.Add(Ui.Veu(Ui.Janela(T("Nova versão do RetroFoot"), corpo, () => { if (!baixando) Fechar(); }, "info"), () => { if (!baixando) Fechar(); }));
        Dispatcher.UIThread.Post(() => agora.Focus(), DispatcherPriority.Loaded);
    }

    // ---------------------------------------------------------------- acoes
    void AbrirPasta()
    {
        if (!DadosJogo.AbrirPasta()) Mensagem(T("Pasta de dados"), T("Não consegui abrir a pasta. Ela fica em:\n{0}", DadosJogo.Raiz));
    }

    void Mensagem(string titulo, string texto)
    {
        sobre.Children.Clear();
        sobre.Children.Add(Ui.Caixa(titulo, texto, () => { sobre.Children.Clear(); Focar(); }));
    }

    // o comeco de sempre (TelaInicio: TselectGameDlg ou o inicio Moderno)
    void Jogar() => mostrar(new TelaInicio(mostrar));

    void AbrirConfiguracoes()
    {
        var t = new TelaConfiguracoes(Configuracoes.Ler());
        t.Fechou += _ => mostrar(new TelaHub(mostrar));
        mostrar(t);
    }

    // o Sobre do visual escolhido (Classico ou Moderno), por cima do hub
    void AbrirSobre()
    {
        var d = new Dialogos(c =>
        {
            c.HorizontalAlignment = HorizontalAlignment.Center;
            c.VerticalAlignment = VerticalAlignment.Center;
            sobre.Children.Add(new Border { Background = Ui.Hex("#66000000") });
            sobre.Children.Add(c);
            Dispatcher.UIThread.Post(() => c.Focus());
        }, c => { sobre.Children.Clear(); Focar(); });
        TelaAcerca.Abrir(d);
    }

    static void Sair()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
    }

    // ---------------------------------------------------------------- ferramentas
    void AbrirFerramenta(Ferramentas.Ferramenta f)
    {
        var ctx = new Ferramentas.Contexto
        {
            RaizDados = DadosJogo.Preparar(),
            Voltar = MontarHub,
            Mensagem = t => Mensagem(T(f.Titulo), t),
        };
        Control conteudo;
        try { conteudo = f.Criar != null ? f.Criar(ctx) : EmBreve(f); }
        catch (Exception ex) { conteudo = EmBreve(f, ex.Message); }
        ordem.Clear();
        pagina.Children.Clear();
        pagina.Children.Add(PaginaFerramenta(f, conteudo));
        aoEsc = MontarHub;
        Dispatcher.UIThread.Post(Focar, DispatcherPriority.Loaded);
    }

    // a barra de cima (voltar, o icone e o titulo) e a ferramenta embaixo
    Control PaginaFerramenta(Ferramentas.Ferramenta f, Control conteudo)
    {
        quadro.Background = Tema.FundoPagina;
        var voltar = Ui.BotaoIcone("voltar", MontarHub, T("Voltar"), 34);
        foco = voltar;
        var cor = CorItem(f.Id);
        var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 12, Margin = new Thickness(12, 8, 16, 8) };
        topo.Children.Add(voltar);
        var chip = new Border
        {
            // no Moderno o icone amarelo no circulo escuro, como nos botoes do hub
            Width = M ? 34 : 32, Height = M ? 34 : 32, CornerRadius = new CornerRadius(M ? 17 : 9), Background = M ? TemaVerde.CirculoIcone : UiClassico.Cartao,
            Child = Ui.Icone(f.Icone, 18, M ? TemaVerde.Destaque : new SolidColorBrush(cor)),
        };
        Grid.SetColumn(chip, 1);
        topo.Children.Add(chip);
        var t = Ui.Titulo(T(f.Titulo), M ? 18 : 20);
        t.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(t, 2);
        topo.Children.Add(t);
        var barra = new Border { Background = Tema.FundoBarra, BorderBrush = Tema.Linha, BorderThickness = new Thickness(0, 0, 0, 1), Child = topo };
        var d = new DockPanel();
        DockPanel.SetDock(barra, Dock.Top);
        d.Children.Add(barra);
        d.Children.Add(new Border { Padding = new Thickness(12, 10, 12, 12), Child = conteudo });
        return Ui.Pagina(d);
    }

    // a pagina de uma ferramenta que ainda nao existe
    static Control EmBreve(Ferramentas.Ferramenta f, string? erro = null)
    {
        var p = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 520 };
        p.Children.Add(new Border { Width = 84, Height = 84, CornerRadius = new CornerRadius(20), Background = Tema.AzulFundo, HorizontalAlignment = HorizontalAlignment.Center, Child = Ui.Icone(f.Icone, 42, Tema.Azul) });
        var t = Ui.Titulo(T("Em breve"), 28);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        p.Children.Add(t);
        var x = Ui.Txt(erro ?? T("Esta ferramenta ainda está a ser feita. Enquanto isso, os arquivos podem ser editados na pasta de dados."), 14);
        x.TextAlignment = TextAlignment.Center;
        p.Children.Add(x);
        var d = Ui.Txt(T(f.Descricao), 12.5, Ui.CorFraca);
        d.TextAlignment = TextAlignment.Center;
        p.Children.Add(d);
        if (DadosJogo.PodeAbrirPasta)
        {
            var b = Ui.Botao(T("Abrir pasta de dados"), () => DadosJogo.AbrirPasta(), false, "pasta");
            b.HorizontalAlignment = HorizontalAlignment.Center;
            p.Children.Add(b);
        }
        var c = Ui.Cartao(p, new Thickness(30, 26));
        c.HorizontalAlignment = HorizontalAlignment.Center;
        c.VerticalAlignment = VerticalAlignment.Center;
        return c;
    }
}
