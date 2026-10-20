using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using RetroFoot.Motor.Arquivos;
using static RetroFoot.App.Idioma;
using Rgba = RetroFoot.Motor.Arquivos.EscudoProcessador.Rgba;

namespace RetroFoot.App;

// O escudo no Editor de Equipas (e os logos das ligas): qualquer imagem (PNG, JPG, WEBP, SVG,
// BMP) e adaptada ao padrao do jogo por EscudoProcessador (corta a borda, tira o fundo de uma
// cor so se o jogador quiser, centra num quadrado transparente de 256x256) e gravada em PNG na
// pasta logos/ com a linha no logos.tsv (ou leagues.tsv). Tambem: escolher da colecao do jogo,
// "Automatico" (o que o jogo acha sozinho pelo pais e nome) e "Sem escudo". O da equipa so vai
// para o disco ao Gravar; o das ligas na hora. Portado de src-cross/TeamEditorEscudo.cs do
// launcher (ramo editor-escudos).
public sealed partial class TelaEditorEquipas
{
    // a previa do visual Classico do jogo: a Arial e o relevo do Win95 (W95 em TelaInicio.cs)
    internal static readonly FontFamily Arial = new("avares://RetroFoot.App/Assets/Fontes#Liberation Sans");
    internal static readonly BoxShadows Relevo = BoxShadows.Parse("inset 1 1 0 0 #A6A6A6, inset -1 -1 0 0 #9E9E9E, inset 2 2 0 0 #FFFFFF, inset -2 -2 0 0 #A6A6A6");
    internal static readonly BoxShadows RelevoAfundado = BoxShadows.Parse("inset 1 1 0 0 #6A6A6A, inset -1 -1 0 0 #6A6A6A, inset 2 2 0 0 #A6A6A6, inset -2 -2 0 0 #F5F5F5");

    internal enum TipoEscudo { Automatico, Nenhum, Imagem, Colecao }

    internal sealed class EscolhaEscudo
    {
        public TipoEscudo Tipo;
        public Rgba? Imagem;              // 256x256 (Imagem)
        public string? Colecao;           // o arquivo de Assets/Escudos (Colecao)
        public Bitmap? Bitmap;            // para mostrar
    }

    EscolhaEscudo? escudoPendente;

    internal static string Requisitos => T("Qualquer imagem PNG, JPG, WEBP, SVG ou BMP. O ideal: quadrada ou quase, fundo transparente, pelo menos 128 px. O RetroFoot corta as sobras, pode tirar um fundo de cor única e centra o escudo num quadrado de 256 px, como os do jogo.");

    // ---------------------------------------------------------------- aba Escudo
    Control AbaEscudo()
    {
        var e = edit!;
        var (bmp, origem) = EscudoAtual(e);
        var quadro = new Border
        {
            Width = 132, Height = 132, CornerRadius = new CornerRadius(10), Background = TemaEditor.Xadrez,
            Padding = new Thickness(6),
            Child = bmp != null ? Escudos.Controle(bmp, 120) : TemaEditor.EscudoCores(e.CorLetra, e.CorFundo, 90),
        };
        ToolTip.SetTip(quadro, T("Arraste uma imagem para aqui"));
        Soltar(quadro, f => Adaptar(f, T("Escudo de {0}", e.NomeAbreviado), r => Escolher(new EscolhaEscudo { Tipo = TipoEscudo.Imagem, Imagem = r, Bitmap = EscudoArquivo.Bitmap(r) })));

        var info = new StackPanel { Spacing = 6 };
        info.Children.Add(tema.T(origem, 12.5, null, true));
        if (escudoPendente != null) info.Children.Add(tema.T(T("(vai para a pasta logos/ ao Gravar)"), 11.5, tema.Fraco));
        var bts = new WrapPanel { ItemSpacing = 5, LineSpacing = 5 };
        bts.Children.Add(tema.Botao(T("Escolher imagem…"), () => _ = EscolherImagem(T("Escudo de {0}", e.NomeAbreviado),
            r => Escolher(new EscolhaEscudo { Tipo = TipoEscudo.Imagem, Imagem = r, Bitmap = EscudoArquivo.Bitmap(r) })), true, "pasta"));
        var daPasta = tema.Botao(T("Da pasta de dados…"), () => EscolherDaPasta(T("Escudo de {0}", e.NomeAbreviado),
            r => Escolher(new EscolhaEscudo { Tipo = TipoEscudo.Imagem, Imagem = r, Bitmap = EscudoArquivo.Bitmap(r) })), false, "pasta");
        ToolTip.SetTip(daPasta, T("Uma imagem copiada para a pasta logos/ da pasta de dados"));
        bts.Children.Add(daPasta);
        bts.Children.Add(tema.Botao(T("Da coleção…"), () => EscolherDaColecao(e.Pais, LogosDados.Normalizar(e.NomeAbreviado),
            a => Escolher(new EscolhaEscudo { Tipo = TipoEscudo.Colecao, Colecao = a, Bitmap = Escudos.DoPacote(a) }))));
        var auto = tema.Botao(T("Automático"), () => Escolher(new EscolhaEscudo { Tipo = TipoEscudo.Automatico, Bitmap = PacoteDe(e) }));
        ToolTip.SetTip(auto, T("O logo que o jogo acha sozinho pelo país e nome (ou o escudo nas cores)"));
        bts.Children.Add(auto);
        bts.Children.Add(tema.Botao(T("Sem escudo"), () => Escolher(new EscolhaEscudo { Tipo = TipoEscudo.Nenhum })));
        if (bmp != null)
            bts.Children.Add(tema.Botao(T("Cores do escudo"), () => CoresDoEscudo(bmp)));
        info.Children.Add(bts);
        var req = tema.T(Requisitos, 11.5, tema.Fraco);
        info.Children.Add(req);

        var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12 };
        topo.Children.Add(quadro);
        Grid.SetColumn(info, 1);
        topo.Children.Add(info);

        var prev = Previas(escudoPendente?.Tipo == TipoEscudo.Nenhum ? null : bmp, e);
        var d = new StackPanel { Spacing = 10 };
        d.Children.Add(topo);
        d.Children.Add(prev);
        return new ScrollViewer { Content = d };
    }

    // o escudo que vale agora (o escolhido e ainda nao gravado, ou o da pasta/jogo) e de onde vem
    (Bitmap? Bmp, string Origem) EscudoAtual(EquipaEditavel e) => EscudoAtual(raiz, escudoPendente, e);

    // (tambem o do celular: FerramentaEditorEquipasCelular.cs)
    internal static (Bitmap? Bmp, string Origem) EscudoAtual(string raiz, EscolhaEscudo? pendente, EquipaEditavel e)
    {
        if (pendente is { } p)
            return p.Tipo switch
            {
                TipoEscudo.Imagem => (p.Bitmap, T("Imagem nova, adaptada")),
                TipoEscudo.Colecao => (p.Bitmap, T("Da coleção do jogo: {0}", p.Colecao ?? "")),
                TipoEscudo.Nenhum => (null, T("Sem escudo: o jogo desenha o escudo nas cores")),
                _ => (p.Bitmap, p.Bitmap != null ? T("Automático: da coleção do jogo") : T("Automático: sem logo (o jogo desenha o escudo nas cores)")),
            };
        var b = Escudos.Escudo(e.Pais, e.NomeCompleto, e.NomeAbreviado);
        var l = LogosDados.Achar(raiz, LogosDados.Indice, e.Pais, e.NomeCompleto, e.NomeAbreviado);
        if (l != null) return l.Arquivo == LogosDados.SemLogo ? (null, T("Sem escudo (escolhido)")) : (b, T("Escolhido: logos/{0}", l.Arquivo));
        foreach (var n in new[] { e.NomeCompleto, e.NomeAbreviado })
            if (n.Trim().Length > 0 && File.Exists(Path.Combine(raiz, PastaDados.Logos, LogosDados.ArquivoEquipa(e.Pais, n))))
                return (b, T("Da pasta logos/: {0}", LogosDados.ArquivoEquipa(e.Pais, n)));
        return b != null ? (b, T("Automático: da coleção do jogo")) : (null, T("Automático: sem logo (o jogo desenha o escudo nas cores)"));
    }

    internal static Bitmap? PacoteDe(EquipaEditavel e) =>
        Escudos.ArquivoDoPacote(e.Pais, e.NomeCompleto, e.NomeAbreviado) is { } a ? Escudos.DoPacote(a) : null;

    void Escolher(EscolhaEscudo es)
    {
        sobre.Children.Clear();
        escudoPendente = es;
        aba = 2;
        MostrarDireita();
    }

    // a imagem arrastada para o controle
    static void Soltar(Control c, Action<string> abrir)
    {
        DragDrop.SetAllowDrop(c, true);
        c.AddHandler(DragDrop.DragOverEvent, (_, a) => a.DragEffects = DragDropEffects.Copy);
        c.AddHandler(DragDrop.DropEvent, (_, a) =>
        {
            var f = a.DataTransfer.TryGetFiles()?.FirstOrDefault()?.TryGetLocalPath();
            if (f != null) { a.Handled = true; abrir(f); }
        });
    }

    async Task EscolherImagem(string titulo, Action<Rgba> usar, bool liga = false)
    {
        var sp = Arquivos;
        if (sp is not { CanOpen: true })
        {
            // sem o seletor do sistema: as imagens que o jogador copiou para logos/
            EscolherDaPasta(titulo, usar, liga);
            return;
        }
        var fs = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = titulo, AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(T("Imagens")) { Patterns = EscudoArquivo.Extensoes.SelectMany(x => new[] { "*." + x, "*." + x.ToUpperInvariant() }).ToArray() },
            },
        });
        if (fs.Count > 0 && fs[0].TryGetLocalPath() is { } c) Adaptar(c, titulo, usar, liga);
    }

    // as imagens da pasta logos/ da pasta de dados (o jogador copia para la pelo gestor de
    // arquivos: "Abrir pasta de dados" no hub); serve tambem quando nao ha seletor de arquivos
    void EscolherDaPasta(string titulo, Action<Rgba> usar, bool liga = false)
    {
        var pasta = Path.Combine(raiz, PastaDados.Logos);
        Directory.CreateDirectory(pasta);
        var arqs = Directory.GetFiles(pasta)
            .Where(a => EscudoArquivo.Extensoes.Contains(Path.GetExtension(a).TrimStart('.').ToLowerInvariant()))
            .OrderBy(a => Path.GetFileName(a), StringComparer.CurrentCultureIgnoreCase).ToList();
        var p = new DockPanel();
        var topo = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 6) };
        topo.Children.Add(tema.T(T("Copie a imagem para a pasta logos/ da pasta de dados e escolha-a aqui:"), 12));
        var cam = tema.Linha1(pasta, 11, tema.Fraco);
        topo.Children.Add(cam);
        DockPanel.SetDock(topo, Dock.Top);
        p.Children.Add(topo);
        var bp = tema.Botoes(
            tema.Botao(T("Abrir pasta de dados"), () => DadosJogo.AbrirPasta(pasta), false, "pasta"),
            tema.Botao(T("Atualizar"), () => EscolherDaPasta(titulo, usar, liga)),
            tema.Botao(T("Cancelar"), () => sobre.Children.Clear()));
        DockPanel.SetDock(bp, Dock.Bottom);
        p.Children.Add(bp);
        var grade = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 };
        foreach (var a in arqs.Take(200))
        {
            var arq = a;
            var s = new StackPanel { Spacing = 2, Width = 92 };
            Control mini;
            try
            {
                if (Path.GetExtension(a).Equals(".svg", StringComparison.OrdinalIgnoreCase)) mini = Ui.Icone("escudo", 44, tema.Fraco);
                else
                {
                    using var fs = File.OpenRead(a);
                    mini = new Image { Source = Bitmap.DecodeToWidth(fs, 88), Width = 44, Height = 44, Stretch = Stretch.Uniform };
                }
            }
            catch { mini = Ui.Icone("aviso", 30, tema.Erro); }
            mini.HorizontalAlignment = HorizontalAlignment.Center;
            s.Children.Add(new Border { Background = TemaEditor.Xadrez, Width = 48, Height = 48, Padding = new Thickness(2), Child = mini, HorizontalAlignment = HorizontalAlignment.Center });
            s.Children.Add(new TextBlock { Text = Path.GetFileName(a), FontFamily = tema.Fonte, FontSize = 10, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxHeight = 26, Foreground = tema.ListaTexto });
            var c = new Clicavel { Child = s, Padding = new Thickness(3), CornerRadius = new CornerRadius(4), Hover = tema.Hover };
            c.Clique += () => Adaptar(arq, titulo, usar, liga);
            ToolTip.SetTip(c, arq);
            grade.Children.Add(c);
        }
        Control corpo = arqs.Count > 0 ? new ScrollViewer { Content = grade, Height = 300 }
            : new Border { Height = 120, Child = tema.T(T("A pasta logos/ não tem imagens (PNG, JPG, WEBP, SVG ou BMP)."), 12, tema.Fraco).Ajustar(t => { t.HorizontalAlignment = HorizontalAlignment.Center; t.TextAlignment = TextAlignment.Center; }) };
        p.Children.Add(tema.Funda(corpo));
        AbrirModal(T("Imagens da pasta de dados"), p, largo ? 760 : 640);
    }

    // ---------------------------------------------------------------- adaptar uma imagem
    // a janelinha com a imagem original, o resultado no padrao do jogo, a opcao de tirar o
    // fundo, os avisos e as previas nas telas
    internal void Adaptar(string arquivo, string titulo, Action<Rgba> usar, bool liga = false)
    {
        var (img, erro) = EscudoArquivo.Abrir(arquivo);
        bool tirar = true;
        var p = new StackPanel { Spacing = 8 };
        Bitmap? original = null;
        if (img != null)
        {
            // a original reduzida (so para mostrar)
            var lado = Math.Max(img.Largura, img.Altura);
            original = lado > 512 ? EscudoArquivo.Bitmap(Reduzida(img, 512)) : EscudoArquivo.Bitmap(img);
        }
        var areaRes = new Border();
        void Montar()
        {
            p.Children.Clear();
            EscudoProcessador.Resultado? r = img != null ? EscudoProcessador.Processar(img, tirar) : null;
            var err = erro ?? r?.Erro;
            Bitmap? res = r?.Imagem != null ? EscudoArquivo.Bitmap(r.Imagem) : null;
            Control Quadro(string rotulo, Bitmap? b, string legenda)
            {
                var s = new StackPanel { Spacing = 3 };
                s.Children.Add(tema.Linha1(rotulo, 11.5, tema.Rotulo, true));
                s.Children.Add(new Border
                {
                    Width = 138, Height = 138, Background = TemaEditor.Xadrez, CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(5),
                    Child = b != null ? new Image { Source = b, Stretch = Stretch.Uniform } : tema.T("—", 30, tema.Fraco),
                });
                s.Children.Add(tema.Linha1(legenda, 10.5, tema.Fraco));
                return s;
            }
            var lado = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            lado.Children.Add(Quadro(T("Original"), original, img != null ? $"{img.Largura}x{img.Altura} px · {Path.GetExtension(arquivo).TrimStart('.').ToUpperInvariant()}" : Path.GetFileName(arquivo)));
            var seta = Ui.Icone("seta", 22, tema.Fraco);
            lado.Children.Add(seta);
            lado.Children.Add(Quadro(T("No padrão do jogo"), res, res != null ? T("{0}x{0} px, PNG transparente", EscudoProcessador.Tamanho) : ""));
            var col = new StackPanel { Spacing = 6, Width = largo ? 360 : 250 };
            if (err != null)
            {
                col.Children.Add(tema.T("✗ " + T("Esta imagem não serve:"), 13, tema.Erro, true));
                col.Children.Add(tema.T(err, 12.5, tema.Erro));
            }
            else if (r != null) col.Children.Add(tema.T("✓ " + T("Imagem adaptada"), 13, tema.Ok, true));
            if (r != null)
            {
                // a opcao aparece tambem quando tirar o fundo nao deixou nada (para desligar)
                if (r.FundoUniforme)
                {
                    var cb = new CheckBox
                    {
                        IsChecked = tirar, FontFamily = tema.Fonte, FontSize = 12.5, Foreground = tema.Texto,
                        Content = T("Tirar o fundo de cor única (#{0})", r.CorDoFundo.ToString("X6")),
                    };
                    cb.IsCheckedChanged += (_, _) => { tirar = cb.IsChecked == true; Montar(); };
                    col.Children.Add(cb);
                }
                else if (r.TemTransparencia && err == null) col.Children.Add(tema.T(T("O fundo já é transparente."), 12, tema.Fraco));
                foreach (var a in r.Avisos) col.Children.Add(tema.T("! " + a, 12, tema.Destaque));
            }
            col.Children.Add(tema.T(Requisitos, 11, tema.Fraco));
            lado.Children.Add(col);
            p.Children.Add(lado);
            if (res != null && !liga && edit != null) p.Children.Add(Previas(res, edit));
            else if (res != null && liga) p.Children.Add(PreviasLiga(res, titulo));
            var usarB = tema.Botao(T("Usar este escudo"), () => { if (r?.Imagem != null) { sobre.Children.Clear(); usar(r.Imagem); } }, true);
            TemaEditor.Ativo(usarB, err == null && r?.Imagem != null);
            var outra = tema.Botao(T("Outra imagem…"), () => _ = EscolherImagem(titulo, usar, liga), false, "pasta");
            p.Children.Add(tema.Botoes(usarB, outra, tema.Botao(T("Cancelar"), () => sobre.Children.Clear())));
        }
        Montar();
        AbrirModal(titulo, new ScrollViewer { Content = p, MaxHeight = 520 }, largo ? 900 : 760);
    }

    internal static Rgba Reduzida(Rgba img, int lado)
    {
        // reduz mantendo a proporcao (so para mostrar a original)
        double esc = (double)lado / Math.Max(img.Largura, img.Altura);
        int w = Math.Max(1, (int)(img.Largura * esc)), h = Math.Max(1, (int)(img.Altura * esc));
        var r = new Rgba(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = Math.Min(img.Largura - 1, (int)(x / esc)), sy = Math.Min(img.Altura - 1, (int)(y / esc));
                Array.Copy(img.Px, (sy * img.Largura + sx) * 4, r.Px, (y * w + x) * 4, 4);
            }
        return r;
    }

    // ---------------------------------------------------------------- previas
    // como o escudo fica na tela da equipa e na classificacao, nos dois visuais
    Control Previas(Bitmap? logo, EquipaEditavel e)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 10 };
        var curto = e.NomeAbreviado.Length > 0 ? e.NomeAbreviado : "ABC";

        Control Logo(double tam)
        {
            if (logo == null) return TemaEditor.EscudoCores(e.CorLetra, e.CorFundo, tam);
            var im = Escudos.Controle(logo, tam)!;
            // logo escuro no fundo escuro: o circulo claro por tras, como nas telas modernas
            return Escudos.Escuro(logo)
                ? new Border { Width = tam + 6, Height = tam + 6, CornerRadius = new CornerRadius((tam + 6) / 2), Background = Ui.Hex("#E8EAED"), Child = im }
                : im;
        }

        // Moderno: o cabecalho da equipa e uma linha da classificacao
        var m = new StackPanel { Spacing = 5 };
        m.Children.Add(tema.Linha1(T("Visual Moderno"), 11.5, tema.Rotulo, true));
        var cab = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        cab.Children.Add(Logo(38));
        var nm = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nm.Children.Add(new TextBlock { Text = e.NomeCompleto, FontFamily = ConfigEstilo.Inter, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = ConfigEstilo.Texto, TextTrimming = TextTrimming.CharacterEllipsis });
        nm.Children.Add(new TextBlock { Text = NomePais(e.Pais) + "  ·  " + T("1ª Divisão"), FontFamily = ConfigEstilo.Inter, FontSize = 11, Foreground = ConfigEstilo.TextoFraco });
        Grid.SetColumn(nm, 1);
        cab.Children.Add(nm);
        m.Children.Add(new Border { Background = Ui.Hex("#24282e"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7), Child = cab });
        var linhas = new StackPanel { Background = Ui.Hex("#202124") };
        linhas.Children.Add(LinhaModerna("1", Logo(18), curto, "31", false));
        linhas.Children.Add(LinhaModerna("2", TemaEditor.EscudoCores(0xFFFFFF, 0xC00000, 18), T("Outra equipa"), "28", true));
        m.Children.Add(new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = linhas, BorderBrush = Ui.Hex("#3c4043"), BorderThickness = new Thickness(1) });
        g.Children.Add(m);

        // Classico: o original nao tem logos; valem as cores da equipa
        var c = new StackPanel { Spacing = 5 };
        c.Children.Add(tema.Linha1(T("Visual Retrô"), 11.5, tema.Rotulo, true));
        c.Children.Add(new Border
        {
            Background = TemaEditor.Pincel(e.CorFundo), Padding = new Thickness(8, 4), BoxShadow = Relevo,
            Child = new TextBlock { Text = e.NomeCompleto, FontFamily = Arial, FontSize = 15, FontWeight = FontWeight.Bold, Foreground = TemaEditor.Pincel(e.CorLetra), TextTrimming = TextTrimming.CharacterEllipsis },
        });
        var tab = new StackPanel { Background = Brushes.White };
        tab.Children.Add(LinhaClassica("1", curto, "31", TemaEditor.Pincel(e.CorFundo), TemaEditor.Pincel(e.CorLetra)));
        tab.Children.Add(LinhaClassica("2", T("Outra equipa"), "28", Brushes.White, Brushes.Black));
        c.Children.Add(new Border { BoxShadow = RelevoAfundado, Padding = new Thickness(2), Background = Brushes.White, Child = tab });
        c.Children.Add(tema.T(T("O visual Retrô não mostra logos: valem as cores da equipa."), 10.5, tema.Fraco));
        Grid.SetColumn(c, 1);
        g.Children.Add(c);
        return g;
    }

    internal static Control LinhaModerna(string pos, Control logo, string nome, string pts, bool zebra)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("22,Auto,*,30"), Height = 26, Background = zebra ? Ui.Hex("#25262a") : Brushes.Transparent };
        g.Children.Add(new TextBlock { Text = pos, FontFamily = ConfigEstilo.Inter, FontSize = 12, Foreground = ConfigEstilo.TextoFraco, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
        logo.Margin = new Thickness(4, 0, 6, 0);
        Grid.SetColumn(logo, 1);
        g.Children.Add(logo);
        var t = new TextBlock { Text = nome, FontFamily = ConfigEstilo.Inter, FontSize = 13, Foreground = ConfigEstilo.Texto, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(t, 2);
        g.Children.Add(t);
        var p = new TextBlock { Text = pts, FontFamily = ConfigEstilo.Inter, FontSize = 13, FontWeight = FontWeight.Bold, Foreground = ConfigEstilo.Texto, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(p, 3);
        g.Children.Add(p);
        return g;
    }

    internal static Control LinhaClassica(string pos, string nome, string pts, IBrush fundo, IBrush letra)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,30"), Height = 18 };
        g.Children.Add(new TextBlock { Text = pos, FontFamily = Arial, FontSize = 12, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
        var n = new Border
        {
            Background = fundo, Padding = new Thickness(3, 0),
            Child = new TextBlock { Text = nome, FontFamily = Arial, FontSize = 12, FontWeight = FontWeight.Bold, Foreground = letra, VerticalAlignment = VerticalAlignment.Center },
        };
        Grid.SetColumn(n, 1);
        g.Children.Add(n);
        var p = new TextBlock { Text = pts, FontFamily = Arial, FontSize = 12, Foreground = Brushes.Black, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 4, 0) };
        Grid.SetColumn(p, 2);
        g.Children.Add(p);
        return g;
    }

    // o logo de uma liga: o separador da divisao na Classificacao moderna
    Control PreviasLiga(Bitmap logo, string nome)
    {
        var s = new StackPanel { Spacing = 5 };
        s.Children.Add(tema.Linha1(T("Visual Moderno (a Classificação e a tela da equipa)"), 11.5, tema.Rotulo, true));
        var abas = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Background = Ui.Hex("#202124") };
        var a1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(12, 8) };
        a1.Children.Add(Escudos.Controle(logo, 18)!);
        a1.Children.Add(new TextBlock { Text = nome, FontFamily = ConfigEstilo.Inter, FontSize = 13, Foreground = ConfigEstilo.Azul, VerticalAlignment = VerticalAlignment.Center });
        abas.Children.Add(new Border { BorderBrush = ConfigEstilo.Azul, BorderThickness = new Thickness(0, 0, 0, 3), Child = a1 });
        s.Children.Add(new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = abas });
        s.Children.Add(tema.T(T("O visual Retrô não mostra logos. O logo da liga aparece quando todas as equipas do jogo são do mesmo país."), 10.5, tema.Fraco));
        return s;
    }

    // ---------------------------------------------------------------- colecao
    void EscolherDaColecao(string? pais, string procurar, Action<string> usar)
    {
        var col = Escudos.Colecao();
        var p = new DockPanel();
        var busca0 = tema.Entrada();
        busca0.Watermark = T("Procurar na coleção…");
        var soPais = new CheckBox { Content = T("Só de {0}", pais != null ? NomePais(pais) : "—"), IsChecked = pais != null && col.Any(i => i.Pais == pais), FontFamily = tema.Fonte, FontSize = 12.5, Foreground = tema.Texto };
        TemaEditor.Ativo(soPais, pais != null);
        var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10, Margin = new Thickness(0, 0, 0, 6) };
        topo.Children.Add(busca0);
        Grid.SetColumn(soPais, 1);
        topo.Children.Add(soPais);
        DockPanel.SetDock(topo, Dock.Top);
        p.Children.Add(topo);
        var conta = tema.Linha1("", 11, tema.Fraco);
        DockPanel.SetDock(conta, Dock.Bottom);
        p.Children.Add(conta);
        var grade = new WrapPanel { ItemSpacing = 4, LineSpacing = 4 };
        var sv = new ScrollViewer { Content = grade, Height = 330 };
        p.Children.Add(tema.Funda(sv));
        const int Max = 120;
        void Mostrar()
        {
            grade.Children.Clear();
            var q = LogosDados.Normalizar(busca0.Text ?? "");
            var l = col.Where(i => (soPais.IsChecked != true || i.Pais == pais) && (q.Length == 0 || LogosDados.Normalizar(i.Nome).Contains(q))).ToList();
            foreach (var i in l.Take(Max))
            {
                var it = i;
                var s = new StackPanel { Spacing = 2, Width = 78 };
                var b = Escudos.DoPacote(i.Arquivo);
                s.Children.Add(b != null ? Escudos.Controle(b, 44)!.Ajustar(x => x.HorizontalAlignment = HorizontalAlignment.Center) : new Border { Height = 44 });
                s.Children.Add(new TextBlock { Text = i.Nome, FontFamily = tema.Fonte, FontSize = 10, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxHeight = 26, Foreground = tema.ListaTexto });
                var c = new Clicavel { Child = s, Padding = new Thickness(3), CornerRadius = new CornerRadius(4), Hover = tema.Hover };
                c.Clique += () => { sobre.Children.Clear(); usar(it.Arquivo); };
                ToolTip.SetTip(c, i.Arquivo);
                grade.Children.Add(c);
            }
            conta.Text = l.Count > Max ? T("{0} logos (mostrando {1}; procure pelo nome)", l.Count, Max) : T("{0} logos", l.Count);
        }
        busca0.TextChanged += (_, _) => Mostrar();
        soPais.IsCheckedChanged += (_, _) => Mostrar();
        if (soPais.IsChecked != true) busca0.Text = procurar;
        Mostrar();
        var bp = tema.Botoes(tema.Botao(T("Cancelar"), () => sobre.Children.Clear()));
        DockPanel.SetDock(bp, Dock.Bottom);
        p.Children.Insert(0, bp);
        AbrirModal(T("Coleção de escudos do jogo"), p, largo ? 860 : 720);
    }

    // as cores do jogo sugeridas pelo escudo (letra, fundo)
    void CoresDoEscudo(Bitmap b)
    {
        var px = EscudoArquivo.DeBitmap(b);
        if (px == null || edit == null) return;
        var sug = EscudoProcessador.Sugestoes(px);
        var p = new StackPanel { Spacing = 8 };
        p.Children.Add(tema.T(T("Escolha as cores da equipa (letra sobre fundo), tiradas das 16 cores do jogo que mais aparecem no escudo:"), 12));
        var w = new WrapPanel { ItemSpacing = 6, LineSpacing = 6 };
        foreach (var (letra, fundo) in sug)
        {
            var (l, f) = (letra, fundo);
            var c = new Clicavel
            {
                Background = TemaEditor.Pincel(f), Padding = new Thickness(12, 6), CornerRadius = new CornerRadius(4), BorderBrush = Ui.Hex("#808080"), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = edit.NomeAbreviado, FontFamily = Arial, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = TemaEditor.Pincel(l) },
            };
            c.Clique += () => { edit.CorLetra = l; edit.CorFundo = f; sobre.Children.Clear(); MostrarDireita(); };
            w.Children.Add(c);
        }
        p.Children.Add(w);
        p.Children.Add(tema.Botoes(tema.Botao(T("Cancelar"), () => sobre.Children.Clear())));
        AbrirModal(T("Cores do escudo"), p, 460);
    }

    // ---------------------------------------------------------------- gravar
    // grava a escolha pendente (ou leva a linha do logo junto, se a equipa mudou de nome/pais)
    void AplicarEscudo(EquipaEditavel? antes, EquipaEditavel depois) => GravarEscudo(raiz, escudoPendente, antes, depois);

    // (tambem o do celular: FerramentaEditorEquipasCelular.cs)
    internal static void GravarEscudo(string raiz, EscolhaEscudo? pendente, EquipaEditavel? antes, EquipaEditavel depois)
    {
        var logos = Path.Combine(raiz, PastaDados.Logos);
        var velhos = antes == null ? Array.Empty<string>() : new[] { antes.NomeCompleto, antes.NomeAbreviado };
        var novos = new[] { depois.NomeCompleto, depois.NomeAbreviado };
        var paisVelho = antes?.Pais ?? depois.Pais;
        bool mudou = antes != null && (antes.Pais.Trim().ToUpperInvariant() != depois.Pais.Trim().ToUpperInvariant()
            || antes.NomeCompleto != depois.NomeCompleto || antes.NomeAbreviado != depois.NomeAbreviado);
        var p = pendente;
        if (p == null)
        {
            if (!mudou) return;
            var l = LogosDados.Achar(raiz, LogosDados.Indice, paisVelho, velhos);
            if (l != null)
            {
                LogosDados.Remover(raiz, LogosDados.Indice, paisVelho, velhos);
                LogosDados.Definir(raiz, LogosDados.Indice, depois.Pais, depois.NomeCompleto, l.Arquivo);
                return;
            }
            // o logo pela convencao do nome velho: vai para o nome novo
            foreach (var n in velhos.Where(n => n.Trim().Length > 0))
            {
                var de = Path.Combine(logos, LogosDados.ArquivoEquipa(paisVelho, n));
                var para = Path.Combine(logos, LogosDados.ArquivoEquipa(depois.Pais, depois.NomeCompleto));
                if (File.Exists(de) && !File.Exists(para)) { File.Move(de, para); break; }
            }
            return;
        }
        LogosDados.Remover(raiz, LogosDados.Indice, paisVelho, velhos);
        var arq = LogosDados.ArquivoEquipa(depois.Pais, depois.NomeCompleto);
        switch (p.Tipo)
        {
            case TipoEscudo.Imagem when p.Imagem != null:
                EscudoArquivo.GravarPng(p.Imagem, Path.Combine(logos, arq));
                LogosDados.Definir(raiz, LogosDados.Indice, depois.Pais, depois.NomeCompleto, arq, novos);
                break;
            case TipoEscudo.Colecao when p.Colecao != null:
                Directory.CreateDirectory(logos);
                using (var s = AssetLoader.Open(new Uri("avares://RetroFoot.App/Assets/Escudos/" + p.Colecao)))
                using (var f = File.Create(Path.Combine(logos, arq))) s.CopyTo(f);
                LogosDados.Definir(raiz, LogosDados.Indice, depois.Pais, depois.NomeCompleto, arq, novos);
                break;
            case TipoEscudo.Nenhum:
                LogosDados.Definir(raiz, LogosDados.Indice, depois.Pais, depois.NomeCompleto, LogosDados.SemLogo, novos);
                break;
            default:
                // automatico: sem linha no indice e sem o arquivo da convencao (vai para backup/)
                LogosDados.Remover(raiz, LogosDados.Indice, depois.Pais, novos);
                foreach (var (pa, n) in velhos.Select(n => (paisVelho, n)).Concat(novos.Select(n => (depois.Pais, n))).Where(x => x.n.Trim().Length > 0))
                {
                    var c = Path.Combine(logos, LogosDados.ArquivoEquipa(pa, n));
                    if (!File.Exists(c)) continue;
                    var bk = Path.Combine(raiz, PastaDados.Backup, DateTime.Now.ToString("yyyyMMdd-HHmmss"), PastaDados.Logos, Path.GetFileName(c));
                    Directory.CreateDirectory(Path.GetDirectoryName(bk)!);
                    File.Move(c, bk, true);
                }
                break;
        }
    }

    // ---------------------------------------------------------------- ligas
    internal static readonly (string Nivel, string Nome)[] NiveisLiga = { ("1", "1ª Divisão"), ("2", "2ª Divisão"), ("3", "3ª Divisão"), ("4", "4ª Divisão"), ("T", "Taça") };

    void AbrirLigas(string? pais = null)
    {
        pais ??= PaisDoFiltro ?? (edit != null ? EquipaJson.PastaPais(edit.Pais) : null) ?? "POR";
        var p = new StackPanel { Spacing = 6 };
        var escolha = tema.Escolha();
        var lp = paises.Select(x => new ItemPais(EquipaJson.PastaPais(x.Codigo), x.Nome, 0)).OrderBy(x => x.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
        escolha.ItemsSource = lp;
        escolha.ItemTemplate = new FuncDataTemplate<ItemPais>((x, _) => x == null ? new Panel() : LinhaPais(x.Codigo, x.Nome));
        escolha.SelectedItem = lp.FirstOrDefault(x => x.Codigo == pais);
        escolha.SelectionChanged += (_, _) => { if (escolha.SelectedItem is ItemPais x && x.Codigo != pais) AbrirLigas(x.Codigo); };
        var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        topo.Children.Add(tema.Linha1(T("País"), 12, tema.Rotulo, true));
        Grid.SetColumn(escolha, 1);
        topo.Children.Add(escolha);
        p.Children.Add(topo);
        p.Children.Add(tema.T(T("Os logos das divisões e da taça aparecem no visual Moderno quando todas as equipas do jogo são deste país. Mudanças gravadas na hora."), 11.5, tema.Fraco));
        var linhas = new StackPanel { Spacing = 4 };
        foreach (var (nivel, nome0) in NiveisLiga)
        {
            var nv = nivel;
            var nome = T(nome0);
            var l = LogosDados.Ler(raiz, LogosDados.Ligas).FirstOrDefault(x => x.Pais == pais && x.Chave.Trim().ToUpperInvariant() == nv);
            Bitmap? b;
            string origem;
            if (l != null)
            {
                b = l.Arquivo == LogosDados.SemLogo ? null : Escudos.DoJogadorAgora(l.Arquivo);
                origem = l.Arquivo == LogosDados.SemLogo ? T("Sem logo (escolhido)") : T("Escolhido: logos/{0}", l.Arquivo);
            }
            else
            {
                var a = Escudos.LigaDoPacote(pais, nv);
                b = a != null ? Escudos.DoPacote(a) : null;
                origem = a != null ? T("Automático: da coleção do jogo") : T("Automático: sem logo");
            }
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*,Auto"), ColumnSpacing = 8 };
            var q = new Border { Width = 36, Height = 36, Background = TemaEditor.Xadrez, CornerRadius = new CornerRadius(4), Padding = new Thickness(2), Child = b != null ? Escudos.Controle(b, 32) : null };
            Soltar(q, f => Adaptar(f, nome, r => GravarLiga(pais, nv, r, null), true));
            g.Children.Add(q);
            var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            tx.Children.Add(tema.Linha1(nome, 12.5, null, true));
            tx.Children.Add(tema.Linha1(origem, 11, tema.Fraco));
            Grid.SetColumn(tx, 1);
            g.Children.Add(tx);
            var bts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            bts.Children.Add(tema.Botao(T("Imagem…"), () => _ = EscolherImagem(nome, r => GravarLiga(pais, nv, r, null), true)));
            bts.Children.Add(tema.Botao(T("Coleção…"), () => EscolherDaColecao(pais, "", a => GravarLiga(pais, nv, null, a))));
            bts.Children.Add(tema.Botao(T("Automático"), () => { LogosDados.Remover(raiz, LogosDados.Ligas, pais, nv); Escudos.Recarregar(); AbrirLigas(pais); }));
            bts.Children.Add(tema.Botao(T("Sem logo"), () => { LogosDados.Definir(raiz, LogosDados.Ligas, pais, nv, LogosDados.SemLogo); Escudos.Recarregar(); AbrirLigas(pais); }));
            Grid.SetColumn(bts, 2);
            g.Children.Add(bts);
            linhas.Children.Add(tema.Caixa(g, null, new Thickness(6, 4)));
        }
        p.Children.Add(linhas);
        p.Children.Add(tema.Botoes(tema.Botao(T("Fechar"), () => sobre.Children.Clear(), true)));
        AbrirModal(T("Logos das ligas"), p, largo ? 700 : 640);
    }

    void GravarLiga(string pais, string nivel, Rgba? img, string? colecao)
    {
        try
        {
            if (!GravarLogoLiga(raiz, pais, nivel, img, colecao)) return;
            AbrirLigas(pais);
        }
        catch (Exception x) { Mensagem(T("Erro ao gravar:") + "\n" + x.Message); }
    }

    // grava o logo de uma liga (a imagem adaptada ou o da colecao) e a linha no leagues.tsv
    // (tambem o do celular: FerramentaEditorEquipasCelular.cs)
    internal static bool GravarLogoLiga(string raiz, string pais, string nivel, Rgba? img, string? colecao)
    {
        var arq = LogosDados.ArquivoLiga(pais, nivel);
        var cam = Path.Combine(raiz, PastaDados.Logos, arq);
        if (img != null) EscudoArquivo.GravarPng(img, cam);
        else if (colecao != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cam)!);
            using var s = AssetLoader.Open(new Uri("avares://RetroFoot.App/Assets/Escudos/" + colecao));
            using var f = File.Create(cam);
            s.CopyTo(f);
        }
        else return false;
        LogosDados.Definir(raiz, LogosDados.Ligas, pais, nivel, arq);
        Escudos.Recarregar();
        return true;
    }
}

static class ControleExt
{
    public static T Ajustar<T>(this T c, Action<T> f) { f(c); return c; }
}
