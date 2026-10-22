using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.NovoJogo;
using RetroFoot.Motor.Save;
using static RetroFoot.App.ConfigEstilo;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// O comeco no visual Moderno (Visual.cs): no lugar das janelas do original (TselectGameDlg,
// TselectCountriesDlg, TcurrencyDlg, TgetTeamsDlg), paginas no verde e amarelo do hub (o
// quadro verde, cartoes meio transparentes com o fio dourado, a bola no topo), no mesmo fluxo:
//   inicio      "Jogo novo" (o nome do arquivo, 8 letras) e "Continuar um jogo" (os jogos
//               gravados com os treinadores e a data); Configuracoes e Sair no topo;
//   jogo novo   Paises (os cartoes com as bandeiras) -> Moeda -> Treinadores (ate 6, o
//               sorteio mostra a equipa de cada um com o logo) -> a Sessao.
// O que acontece em cada passo e o mesmo do Original (TelaInicio.cs); so o desenho muda.
// Em 16:9 as paginas usam a largura (os cartoes lado a lado, mais colunas de paises).
public sealed partial class TelaInicio
{
    readonly Panel _paginaM = new();
    readonly Panel _sobreM = new();
    Action? _enterM, _escM;
    Action<int>? _setasM;
    Control? _focoM;

    void MontarModerno()
    {
        FontFamily = Inter;
        Background = TemaVerde.FundoTela;
        _sobreM.IsVisible = false;
        Content = Escuro(new Panel { Children = { _paginaM, _sobreM } });
        AddHandler(KeyDownEvent, TeclaModerna, Avalonia.Interactivity.RoutingStrategies.Bubble);
    }

    void FocarModerno()
    {
        if (_sobreM.Children.LastOrDefault() is CaixaModerna c) { c.Focar(); return; }
        if (_focoM is { IsEffectivelyVisible: true } f && f.Focus()) return;
        Focus();
    }

    void TeclaModerna(object? _, KeyEventArgs e)
    {
        if (e.Handled || _sobreM.IsVisible) return;
        switch (e.Key)
        {
            case Key.Enter: _enterM?.Invoke(); break;
            case Key.Escape: _escM?.Invoke(); break;
            case Key.Up when _setasM != null && e.Source is not TextBox: _setasM(-1); break;
            case Key.Down when _setasM != null && e.Source is not TextBox: _setasM(1); break;
            default: return;
        }
        e.Handled = true;
    }

    // uma pagina: o topo (titulo, subtitulo e o que vai a direita), o corpo e o rodape
    void Pagina(Control topoDireita, string titulo, string subtitulo, Control corpo, Control? rodape,
        Action? enter, Action? esc, Control? foco, Action<int>? setas = null)
    {
        var cab = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        cab.Children.Add(Titulo(titulo, 26));
        if (subtitulo.Length > 0) cab.Children.Add(Ajuda(subtitulo, 13.5));
        // a bola da marca a esquerda do titulo, como no hub
        var bola = Logotipo.ImagemBola(46);
        bola.VerticalAlignment = VerticalAlignment.Center;
        var topoGrade = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,14,*,16,Auto"), MaxWidth = 1600, Margin = new Thickness(24, 12, 28, 12) };
        topoGrade.Children.Add(bola);
        Grid.SetColumn(cab, 2);
        topoGrade.Children.Add(cab);
        Grid.SetColumn(topoDireita, 4);
        topoDireita.VerticalAlignment = VerticalAlignment.Center;
        topoGrade.Children.Add(topoDireita);
        var topo = new Border { Background = FundoBarra, BorderBrush = Linha, BorderThickness = new Thickness(0, 0, 0, 1), Child = topoGrade };
        var d = new DockPanel();
        DockPanel.SetDock(topo, Dock.Top);
        d.Children.Add(topo);
        if (rodape != null)
        {
            var rb = new Border
            {
                Background = FundoBarra, BorderBrush = Linha, BorderThickness = new Thickness(0, 1, 0, 0),
                Child = new Border { MaxWidth = 1600, Margin = new Thickness(28, 12), Child = rodape },
            };
            DockPanel.SetDock(rb, Dock.Bottom);
            d.Children.Add(rb);
        }
        d.Children.Add(new Border { MaxWidth = 1600, Margin = new Thickness(28, 20, 28, 20), Child = corpo });
        _paginaM.Children.Clear();
        _paginaM.Children.Add(d);
        _enterM = enter; _escM = esc; _focoM = foco; _setasM = setas;
        Dispatcher.UIThread.Post(FocarModerno, DispatcherPriority.Loaded);
    }

    // uma caixa por cima da pagina (mensagens, o "a preparar"); null tira
    void Sobrepor(Control? c)
    {
        _sobreM.Children.Clear();
        _sobreM.IsVisible = c != null;
        if (c == null) { Dispatcher.UIThread.Post(FocarModerno, DispatcherPriority.Loaded); return; }
        _sobreM.Children.Add(new Border { Background = TemaVerde.Veu });
        c.HorizontalAlignment = HorizontalAlignment.Center;
        c.VerticalAlignment = VerticalAlignment.Center;
        _sobreM.Children.Add(c);
    }

    void ConfirmarModerno(string titulo, string texto, Action sim)
    {
        var c = new CaixaModerna(titulo, 460);
        c.Corpo.Children.Add(new TextBlock { Text = texto, FontSize = 14.5, Foreground = Texto, TextWrapping = TextWrapping.Wrap });
        void Sim() { Sobrepor(null); sim(); }
        void Nao() => Sobrepor(null);
        c.Rodape((T("Não"), false, Nao), (T("Sim"), true, Sim));
        c.AoEnter = Sim; c.AoFechar = Nao;
        Sobrepor(c);
    }

    void MensagemModerna(string titulo, string texto, Action? depois)
    {
        var c = new CaixaModerna(titulo, 460);
        c.Corpo.Children.Add(new TextBlock { Text = texto, FontSize = 14.5, Foreground = Texto, TextWrapping = TextWrapping.Wrap });
        void Fim() { Sobrepor(null); depois?.Invoke(); }
        c.Rodape((T("OK"), true, Fim));
        c.AoEnter = Fim; c.AoFechar = Fim;
        Sobrepor(c);
    }

    // os passos do jogo novo, no topo a direita
    static Control Passos(int atual)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        string[] nomes = { T("Países"), T("Moeda"), T("Treinadores") };
        for (int i = 0; i < nomes.Length; i++)
        {
            if (i > 0) p.Children.Add(new Border { Width = 22, Height = 1, Background = Linha, VerticalAlignment = VerticalAlignment.Center });
            bool feito = i < atual, este = i == atual;
            var num = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12),
                Background = este ? Azul : feito ? TemaVerde.CirculoIcone : TemaVerde.BotaoSecundario,
                BorderBrush = este || feito ? Azul : Linha, BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = feito ? "✓" : (i + 1).ToString(), FontSize = 12, FontWeight = FontWeight.Bold,
                    Foreground = este ? TextoEscuro : feito ? Azul : TextoFraco,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            p.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children = { num, new TextBlock { Text = nomes[i], FontSize = 13.5, FontWeight = FontWeight.SemiBold, Foreground = este ? Texto : TextoFraco, VerticalAlignment = VerticalAlignment.Center } },
            });
        }
        return p;
    }

    string ResumoRegras()
    {
        var r = cfg.RegrasDoJogoNovo;
        var partes = new List<string>
        {
            cfg.Modificada ? T("Versão Atual") : T("Versão Retrô"),
            T("{0} equipas por divisão ({1} rodadas)", r.N, r.Jornadas),
            r.CartoesAmarelos ? T("cartões amarelos") : T("só cartões vermelhos"),
        };
        if (cfg.Modificada && cfg.Patrocinios) partes.Add(T("patrocínios"));
        if (cfg.Modificada && cfg.Acrescimos) partes.Add(T("acréscimos"));
        if (cfg.Modificada) partes.Add(T("sobem e descem {0}", r.SobemDescem));
        if (cfg.EstrangeirosLiberados) partes.Add(T("estrangeiros liberados"));
        return string.Join(" · ", partes);
    }

    // ---------------------------------------------------------------- inicio
    void EscolherJogoModerno()
    {
        var jogos = ListarJogos();
        int sel = jogos.Count > 0 ? 0 : -1;

        // jogo novo
        var nome = CaixaTexto("", 8, T("NOME"));
        nome.FontSize = 18;
        nome.FontWeight = FontWeight.SemiBold;
        var aviso = new TextBlock { FontSize = 13, Foreground = Erro, TextWrapping = TextWrapping.Wrap, MinHeight = 18 };
        var comecar = Botao(T("Começar jogo novo"), true, () => { });
        comecar.HorizontalAlignment = HorizontalAlignment.Left;
        var regras = new Border
        {
            Background = FundoAviso, CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = T("Regras do jogo novo"), FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Azul },
                    new TextBlock { Text = ResumoRegras() + ".", FontSize = 13, Foreground = Texto, TextWrapping = TextWrapping.Wrap },
                },
            },
        };
        var mudar = new TextBlock
        {
            Text = T("Mudar nas Configurações"), FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Azul,
            Cursor = new Cursor(StandardCursorType.Hand), TextDecorations = TextDecorations.Underline,
        };
        mudar.PointerPressed += (_, a) => { a.Handled = true; AbrirConfiguracoes(); };
        ((StackPanel)regras.Child).Children.Add(mudar);
        var novo = Cartao(T("Jogo novo"), null, new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Ajuda(T("O nome do arquivo do jogo: até 8 letras ou algarismos."), 13),
                nome, aviso, comecar, regras,
            },
        }, new Thickness(22, 20));

        // continuar
        var linhas = new StackPanel { Spacing = 6 };
        var bordas = new List<Border>();
        var continuar = Botao(T("Continuar"), true, () => { });
        var excluir = Botao(T("Excluir"), false, () => { if (sel >= 0 && sel < jogos.Count) ExcluirJogo(jogos[sel].Nome, jogos[sel].Caminho); });
        var importar = Botao(T("Importar"), false, ImportarJogo);
        var exportar = Botao(T("Exportar"), false, () => { if (sel >= 0 && sel < jogos.Count) ExportarJogo(jogos[sel].Nome, jogos[sel].Caminho); });
        for (int i = 0; i < jogos.Count; i++)
        {
            int k = i;
            var g = jogos[i];
            var desc = g.Descricao.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var quem = desc.Length > 0 ? desc[0] : "";
            var quando = g.Quando;
            var grade = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,16,*") };
            grade.Children.Add(new Border
            {
                Width = 44, Height = 44, CornerRadius = new CornerRadius(22), Background = TemaVerde.CirculoIcone,
                Child = g.Equipa is { } eq ? new Escudo(eq) { Margin = new Thickness(4) }
                    : new TextBlock { Text = "E98", FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Azul, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            var meio = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var cab = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            cab.Children.Add(new TextBlock { Text = g.Nome, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Texto, VerticalAlignment = VerticalAlignment.Center });
            if (g.Ano > 0) cab.Children.Add(Etiqueta(T("Temporada {0}", g.Ano), Azul, TemaVerde.DestaqueFundo));
            // a data e a hora do save, pequenas, no cabecalho (as linhas dos humanos ficam largas)
            if (quando.Length > 0) cab.Children.Add(new TextBlock { Text = quando, FontSize = 11.5, Foreground = TextoFraco, VerticalAlignment = VerticalAlignment.Center });
            meio.Children.Add(cab);
            // os humanos com a equipa e a divisao; save ilegivel: o texto do cabecalho
            if (g.Humanos.Count > 0)
                foreach (var h in g.Humanos)
                    meio.Children.Add(new TextBlock { Text = h.ToString(), FontSize = 13, Foreground = TextoFraco, TextTrimming = TextTrimming.CharacterEllipsis });
            else if (quem.Length > 0) meio.Children.Add(new TextBlock { Text = quem, FontSize = 13, Foreground = TextoFraco, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(meio, 2);
            grade.Children.Add(meio);
            var b = new Border
            {
                Child = grade, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10), BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            b.PointerPressed += (_, a) => { a.Handled = true; Escolher(k); if (a.ClickCount >= 2) Abrir(); };
            bordas.Add(b);
            linhas.Children.Add(b);
        }
        var rolagem = new ScrollViewer { Content = linhas, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Control lista = jogos.Count > 0 ? rolagem : new Border
        {
            Padding = new Thickness(0, 30), Child = new StackPanel
            {
                Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center,
                Children = { Titulo(T("Nenhum jogo gravado"), 15, TextoFraco), Ajuda(T("Comece um jogo novo ao lado."), 13) },
            },
        };
        var cont = new DockPanel();
        var cabCont = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
        cabCont.Children.Add(Titulo(T("Continuar um jogo"), 16));
        var n = Etiqueta(jogos.Count == 1 ? T("1 jogo") : T("{0} jogos", jogos.Count), TextoFraco, TemaVerde.CartaoForte);
        Grid.SetColumn(n, 1);
        cabCont.Children.Add(n);
        DockPanel.SetDock(cabCont, Dock.Top);
        cont.Children.Add(cabCont);
        // (o Importar a esquerda, o Exportar, o Excluir e o Continuar a direita)
        var rodCont = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 12, 0, 0) };
        rodCont.Children.Add(importar);
        var dirCont = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { exportar, excluir, continuar } };
        Grid.SetColumn(dirCont, 2);
        rodCont.Children.Add(dirCont);
        DockPanel.SetDock(rodCont, Dock.Bottom);
        cont.Children.Add(rodCont);
        cont.Children.Add(lista);
        var contCartao = new Border
        {
            Background = FundoCartao, CornerRadius = new CornerRadius(12), Padding = new Thickness(22, 20),
            BorderBrush = Linha, BorderThickness = new Thickness(1.5), Child = cont, BoxShadow = BoxShadows.Parse("0 4 12 0 #40000000"),
        };

        var corpo = new Grid { ColumnDefinitions = new ColumnDefinitions("5*,20,7*") };
        var teclas = Cartao(T("Teclado"), null, new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Ajuda(T("Setas para cima e para baixo: escolhem o jogo gravado."), 13),
                Ajuda(T("Enter: abre o jogo escolhido (ou começa o novo, com o nome escrito)."), 13),
                Ajuda(T("Esc: volta ao menu."), 13),
            },
        }, new Thickness(22, 16));
        var esquerda = new StackPanel { Spacing = 20, Children = { novo, teclas } };
        corpo.Children.Add(esquerda);
        Grid.SetColumn(contCartao, 2);
        corpo.Children.Add(contCartao);

        var topoDir = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children = { Botao(T("Configurações"), false, AbrirConfiguracoes), Botao(T("Voltar"), false, SairDoPrograma) },
        };

        bool valido = false;
        void Atualizar()
        {
            var t = nome.Text ?? "";
            string? av = null;
            valido = NomeValido(t);
            if (t.Length > 0 && !valido) av = T("Nome do arquivo inválido: só letras (sem acento), algarismos, _ e -.");
            // (acrescimo) nao apaga um jogo gravado com o mesmo nome
            else if (valido && Achar(pastaJogos, t + ".e98") != null) { av = T("Já existe um jogo com esse nome."); valido = false; }
            aviso.Text = av ?? "";
            comecar.Habilitado = valido;
            continuar.Habilitado = sel >= 0;
            excluir.Habilitado = exportar.Habilitado = sel >= 0;
        }
        void Escolher(int k)
        {
            sel = k;
            for (int i = 0; i < bordas.Count; i++)
            {
                bool s = i == sel;
                bordas[i].Background = s ? AzulFundo : TemaVerde.BotaoSecundario;
                bordas[i].BorderBrush = s ? Azul : Linha;
            }
            if (sel >= 0 && sel < bordas.Count) bordas[sel].BringIntoView();
            Atualizar();
        }
        void Novo()
        {
            if (!valido) return;
            var arq = Path.Combine(pastaJogos, (nome.Text ?? "") + ".e98");
            Sobrepor(Preparando(T("A ler as equipas e os países...")));
            NovoJogo(arq);
        }
        void Abrir()
        {
            if (sel < 0 || sel >= jogos.Count) return;
            var g = jogos[sel];
            Jogo98 s;
            try { s = Jogo98.Ler(File.ReadAllBytes(g.Caminho)); }
            catch (Exception ex) { Mensagem(T("Erro"), ex.Message); return; }
            Comecar(s, g.Caminho, false);
        }
        // so letras e algarismos, em maiusculas (o nome de arquivo do DOS)
        nome.TextChanged += (_, _) =>
        {
            var t = nome.Text ?? "";
            var u = t.ToUpperInvariant();
            if (u != t) { int c = nome.CaretIndex; nome.Text = u; nome.CaretIndex = c; return; }
            Atualizar();
        };
        comecar.Clique = Novo;
        continuar.Clique = Abrir;
        Escolher(sel);
        Pagina(topoDir, TelaAcerca.Nome, TelaAcerca.Slogan, corpo, null,
            () => { if (nome.IsFocused) Novo(); else Abrir(); }, SairDoPrograma,
            jogos.Count > 0 ? this : nome, d => { if (jogos.Count > 0) Escolher(Math.Clamp(sel + d, 0, jogos.Count - 1)); });
        if (jogos.Count == 0) _focoM = nome;
    }

    static Control Preparando(string texto)
    {
        var c = new CaixaModerna(T("Jogo novo"), 380);
        c.Corpo.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12,
            Children = { new ProgressBar { IsIndeterminate = true, Width = 80, VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = texto, FontSize = 14, Foreground = Texto, VerticalAlignment = VerticalAlignment.Center } },
        });
        return c;
    }

    // ---------------------------------------------------------------- paises
    void SelecionarPaisesModerno(Dados d, SelecaoPaises sel, string arquivo)
    {
        Sobrepor(null);
        var nomeJogo = Path.GetFileNameWithoutExtension(arquivo);
        var tiles = new WrapPanel { ItemSpacing = 10, LineSpacing = 10 };
        var marcas = new List<(Border B, TextBlock Check, Pais P)>();
        var tTimes = new TextBlock { FontSize = 22, FontWeight = FontWeight.Bold };
        var tPaises = new TextBlock { FontSize = 22, FontWeight = FontWeight.Bold, Foreground = Texto };
        var tAviso = new TextBlock { FontSize = 13, Foreground = Amarelo, TextWrapping = TextWrapping.Wrap };
        var ok = Botao(T("Continuar"), true, () => { });
        var todas = Botao(T("Todos"), false, () => { });

        foreach (var p in sel.Lista)
        {
            var pais = p;
            var nomeP = Encoding.Latin1.GetString(p.Nome);
            Control bandeira = Bandeira(p.Codigo) is Bitmap bm
                ? new Image { Source = bm, Width = 36, Height = 24, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center }
                : new Border { Width = 36, Height = 24, Background = Linha, CornerRadius = new CornerRadius(3) };
            var check = new TextBlock { Text = "✓", FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Azul, VerticalAlignment = VerticalAlignment.Center };
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,10,*,Auto") };
            g.Children.Add(new Border { ClipToBounds = true, CornerRadius = new CornerRadius(3), Child = bandeira });
            var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            txt.Children.Add(new TextBlock { Text = nomeP, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Texto, TextTrimming = TextTrimming.CharacterEllipsis });
            txt.Children.Add(new TextBlock { Text = p.Vagas == 1 ? T("1 time") : T("{0} times", p.Vagas), FontSize = 12, Foreground = TextoFraco });
            Grid.SetColumn(txt, 2);
            g.Children.Add(txt);
            Grid.SetColumn(check, 3);
            g.Children.Add(check);
            var b = new Border
            {
                Width = 206, Child = g, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8), BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            b.PointerPressed += (_, a) => { a.Handled = true; sel.Alternar(pais); Atualizar(); };
            marcas.Add((b, check, pais));
            tiles.Children.Add(b);
        }

        Control Numero(string rotulo, TextBlock valor, string? extra = null)
        {
            var s = new StackPanel { Spacing = 0 };
            s.Children.Add(new TextBlock { Text = rotulo, FontSize = 12, Foreground = TextoFraco });
            s.Children.Add(valor);
            if (extra != null) s.Children.Add(new TextBlock { Text = extra, FontSize = 12, Foreground = TextoFraco });
            return new Border { Background = FundoCartao, BorderBrush = Linha, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 10), Child = s };
        }
        var numeros = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 0, 0, 14),
            Children =
            {
                Numero(T("Times selecionados"), tTimes, T("mínimo {0} · existem {1}", sel.MinimoDeEquipas, sel.Total)),
                Numero(T("Países selecionados"), tPaises, T("de {0}", sel.Lista.Count)),
            },
        };
        var cab = new DockPanel { Margin = new Thickness(0, 0, 0, 0) };
        DockPanel.SetDock(numeros, Dock.Left);
        cab.Children.Add(numeros);
        tAviso.VerticalAlignment = VerticalAlignment.Center;
        tAviso.Margin = new Thickness(10, 0, 0, 14);
        cab.Children.Add(tAviso);
        var corpo = new DockPanel();
        DockPanel.SetDock(cab, Dock.Top);
        corpo.Children.Add(cab);
        var rolar = new ScrollViewer { Content = tiles, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        corpo.Children.Add(rolar);
        // os cartoes dividem a largura (mais colunas em 16:9), sem sobra a direita
        rolar.SizeChanged += (_, e) =>
        {
            double w = e.NewSize.Width - 14;
            int col = Math.Max(1, (int)((w + 10) / 216));
            double lw = Math.Floor((w - 10 * (col - 1)) / col);
            foreach (var (b, _, _) in marcas) b.Width = lw;
        };

        void Atualizar()
        {
            foreach (var (b, c, p) in marcas)
            {
                b.Background = p.Selecionado ? AzulFundo : FundoCartao;
                b.BorderBrush = p.Selecionado ? Azul : Linha;
                c.IsVisible = p.Selecionado;
            }
            bool chega = sel.Escolhidas >= sel.MinimoDeEquipas;
            tTimes.Text = sel.Escolhidas.ToString();
            tTimes.Foreground = chega ? Verde : Texto;
            tPaises.Text = sel.Escolhidos.ToString();
            tAviso.Text = sel.Escolhidas > SelecaoPaises.MaximoEquipas
                ? T("O jogo só suporta {0} times. Alguns dos times dos países selecionados não farão parte deste jogo.", SelecaoPaises.MaximoEquipas)
                : chega ? "" : T("Faltam {0} times para o mínimo.", sel.MinimoDeEquipas - sel.Escolhidas);
            ok.Habilitado = chega;
            // "Todos" vira "Nenhum" quando ja estao todos escolhidos (acrescimo do Go)
            todas.Texto = sel.TodosEscolhidos ? T("Nenhum") : T("Todos");
        }
        void Ok()
        {
            if (sel.Escolhidas < sel.MinimoDeEquipas) return;
            try { sel.Confirmar(new Aleatorio(Randomize(DateTime.Now))); }
            catch (Exception ex) { Mensagem(T("Erro"), ex.Message); return; }
            EscolherMoeda(d, arquivo);
        }
        ok.Clique = Ok;
        todas.Clique = () => { if (sel.TodosEscolhidos) sel.Nenhum(); else sel.Todos(); Atualizar(); };
        var rodape = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,10,Auto,*,Auto") };
        rodape.Children.Add(Botao(T("Voltar"), false, EscolherJogo));
        Grid.SetColumn(todas, 2);
        rodape.Children.Add(todas);
        Grid.SetColumn(ok, 4);
        rodape.Children.Add(ok);
        Atualizar();
        Pagina(Passos(0), T("Jogo novo") + " · " + nomeJogo,
            T("Selecione os países com que quer jogar, de modo a totalizar pelo menos {0} times.", sel.MinimoDeEquipas),
            corpo, rodape, Ok, EscolherJogo, this);
    }

    // ---------------------------------------------------------------- moeda
    void EscolherMoedaModerno(Dados d, string arquivo)
    {
        var nomes = new List<(string, string?)>();
        foreach (int i in Tabelas.MoedasDoJogoNovo) nomes.Add((Tabelas.NomeMoeda(i), null));
        var moeda = new EscolhaModerna(nomes, 0, chips: true);
        void Ok() => EscolherTreinadores(d, Tabelas.MoedasDoJogoNovo[Math.Clamp(moeda.Selecionado, 0, Tabelas.MoedasDoJogoNovo.Length - 1)], arquivo);
        var corpo = new StackPanel
        {
            Spacing = 14, Width = 760, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0),
            Children = { Cartao(T("Com que moeda vai querer jogar?"), null, new StackPanel { Spacing = 12, Children = { moeda, Ajuda(T("Os preços, os salários e o dinheiro das equipas aparecem nesta moeda durante todo o jogo."), 13) } }, new Thickness(22, 20)) },
        };
        var rodape = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var ok = Botao(T("Continuar"), true, Ok);
        Grid.SetColumn(ok, 1);
        rodape.Children.Add(ok);
        Pagina(Passos(1), T("Jogo novo") + " · " + Path.GetFileNameWithoutExtension(arquivo), T("A moeda do jogo."), corpo, rodape, Ok, null, this,
            k => moeda.Mover(k));
        // (as setas para os lados tambem)
        _setasM = k => moeda.Mover(k);
    }

    // ---------------------------------------------------------------- treinadores
    void EscolherTreinadoresModerno(Dados d, int moeda, string arquivo)
    {
        var nomes = new TextBox[6];
        var equipas = new Border[6];
        var grade = new Grid { ColumnDefinitions = new ColumnDefinitions("110,250,16,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto") };
        void Celula(Control c, int linha, int coluna) { Grid.SetRow(c, linha); Grid.SetColumn(c, coluna); grade.Children.Add(c); }
        Celula(new TextBlock { Text = T("Nome"), FontSize = 12.5, Foreground = TextoFraco, Margin = new Thickness(0, 0, 0, 6) }, 0, 1);
        Celula(new TextBlock { Text = T("Equipa"), FontSize = 12.5, Foreground = TextoFraco, Margin = new Thickness(0, 0, 0, 6) }, 0, 3);
        for (int i = 0; i < 6; i++)
        {
            Celula(new TextBlock { Text = T("Treinador {0}", i + 1), FontSize = 14, Foreground = Texto, VerticalAlignment = VerticalAlignment.Center }, i + 1, 0);
            nomes[i] = CaixaTexto("", 20);
            nomes[i].Margin = new Thickness(0, 4);
            Celula(nomes[i], i + 1, 1);
            equipas[i] = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 4), Margin = new Thickness(0, 4), MinHeight = 38 };
            Celula(equipas[i], i + 1, 3);
        }
        var botao = Botao(T("Sortear equipas"), true, () => { });
        var dica = Ajuda(T("Até 6 treinadores humanos. Escreva os nomes e sorteie as equipas: cada um começa numa equipa da última divisão."), 13);
        var corpo = new StackPanel
        {
            Spacing = 14, Width = 760, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0),
            Children = { Cartao(null, null, new StackPanel { Spacing = 12, Children = { dica, grade } }, new Thickness(22, 20)) },
        };
        Jogo98? criado = null;
        void Clique()
        {
            if (criado != null)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
                    File.WriteAllBytes(arquivo, criado.Gravar());
                }
                catch (Exception ex) { Mensagem(T("Erro"), T("Não consegui gravar o jogo: {0}", ex.Message)); }
                Comecar(criado, arquivo, true);
                return;
            }
            var humanos = nomes.Select(n => (n.Text ?? "").Trim()).ToList();
            if (humanos.All(h => h.Length == 0)) return;
            Jogo98 s;
            try { s = CriarJogo(d, moeda, humanos, cfg.RegrasDoJogoNovo); }
            catch (Exception ex) { Mensagem(T("Erro"), ex.Message); return; }
            // a equipa de cada humano, na ordem dos campos
            var hs = s.Treinadores.Where(t => t.Humano != 0).ToList();
            int k = 0;
            for (int i = 0; i < 6; i++)
            {
                if (humanos[i].Length == 0 || k >= hs.Count) continue;
                var e = s.Equipas.FirstOrDefault(q => q.Treinador == hs[k].ID);
                k++;
                if (e == null) continue;
                var fundo = e.CorFundo.Length >= 3 ? Color.FromRgb(e.CorFundo[0], e.CorFundo[1], e.CorFundo[2]) : Colors.White;
                var letra = e.CorLetra.Length >= 3 ? Color.FromRgb(e.CorLetra[0], e.CorLetra[1], e.CorLetra[2]) : Colors.Black;
                equipas[i].Background = FundoAviso;
                equipas[i].Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 10,
                    Children =
                    {
                        new Escudo(e) { Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Center },
                        new Border
                        {
                            Background = new SolidColorBrush(fundo), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3), VerticalAlignment = VerticalAlignment.Center,
                            Child = new TextBlock { Text = TextoSave(e.NomeCurto), FontSize = 14, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(letra) },
                        },
                    },
                };
            }
            foreach (var n in nomes) n.IsEnabled = false;
            botao.Texto = T("Começar");
            dica.Text = T("As equipas foram sorteadas. Boa sorte!");
            criado = s;
            Focus();
        }
        botao.Clique = Clique;
        var rodape = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(botao, 1);
        rodape.Children.Add(botao);
        Pagina(Passos(2), T("Jogo novo") + " · " + Path.GetFileNameWithoutExtension(arquivo), T("Os treinadores humanos."), corpo, rodape, Clique, null, nomes[0]);
    }
}
