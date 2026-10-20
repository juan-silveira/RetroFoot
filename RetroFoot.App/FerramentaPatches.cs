using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using RetroFoot.Motor.Arquivos;
using static RetroFoot.App.Idioma;
using C = RetroFoot.App.Tema;

namespace RetroFoot.App;

// A ferramenta Patches do hub: o "Aplicar Patch" do launcher do jogo classico (a biblioteca de
// patches) dentro do RetroFoot. Os .zip importados ficam em <pasta de dados>/patches; aplicar
// poe as equipas, paises, arbitros, bandeiras, patrocinadores e logos na pasta de dados (no
// formato novo) e guarda o que foi substituido; reverter volta tudo a como estava. A logica fica
// no motor (Motor/Arquivos/BibliotecaPatches.cs); aqui so a pagina, com as pecas e as cores do
// visual (Ui.cs, Tema; como o Editor de Save); 4:3 e 16:9.
static class FerramentaPatches
{
    [ModuleInitializer]
    internal static void Registar() =>
        Ferramentas.Registrar(Ferramentas.Patches, "Patches", "Aplique patches de equipas e países", "peca",
            ctx => new TelaPatches(ctx));
}

public sealed class TelaPatches : UserControl
{
    readonly Ferramentas.Contexto ctx;
    readonly string raiz;
    readonly Grid corpo = new() { ColumnSpacing = 10 };
    readonly Panel sobre = new();
    readonly ListBox lista;
    readonly Border detalhe = new();
    List<BibliotecaPatches.Info> patches = new();
    string? escolhido;
    bool substituirTodas;
    bool ocupado;

    public TelaPatches(Ferramentas.Contexto ctx)
    {
        this.ctx = ctx;
        raiz = ctx.RaizDados;
        lista = Ui.Lista();
        Directory.CreateDirectory(BibliotecaPatches.Pasta(raiz));
        corpo.ColumnDefinitions = new ColumnDefinitions(ctx.Largo ? "380,*" : "292,*");
        corpo.Children.Add(Esquerda());
        Grid.SetColumn(detalhe, 1);
        corpo.Children.Add(detalhe);
        var tudo = new Panel { Children = { corpo, sobre } };
        Content = tudo;
        // arrastar .zip do gestor de arquivos para a pagina tambem importa
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, (_, e) => e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None);
        DragDrop.AddDropHandler(this, (_, e) =>
        {
            var fs = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().ToList();
            if (fs is { Count: > 0 }) ImportarArquivos(fs);
        });
        lista.SelectionChanged += (_, _) =>
        {
            if (lista.SelectedItem is ListBoxItem { Tag: string n } && n != escolhido) { escolhido = n; Escolheu(); }
        };
        Recarregar();
    }

    // ---------------------------------------------------------------- pecas dos dois visuais
    static IBrush CorTexto => Ui.CorTexto;
    static IBrush CorFraca => Ui.CorFraca;
    static IBrush CorRotulo => Ui.CorRotulo;
    static IBrush CorDestaque => C.Azul;

    static TextBlock Txt(string s, double tam = 12.5, IBrush? cor = null, bool negrito = false) => Ui.Txt(s, tam, cor ?? CorTexto, negrito);

    static TextBlock Tit(string s, double tam)
    {
        var t = Ui.Titulo(s, tam - 2);
        t.TextWrapping = TextWrapping.Wrap;
        return t;
    }

    static Border Cartao(Control conteudo, Thickness pad) => Ui.Cartao(conteudo, pad);

    static Control Botao(string texto, Action acao, bool principal = false, string? icone = null) => Ui.Botao(texto, acao, principal, icone);

    // ---------------------------------------------------------------- a biblioteca (esquerda)
    Control Esquerda()
    {
        var d = new DockPanel();
        var topo = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        var t = Txt(T("Biblioteca de patches"), 14, C.CorTitulo, true);
        topo.Children.Add(t);
        var imp = Botao(T("Importar patch (.zip)"), Importar, true, "pasta");
        imp.HorizontalAlignment = HorizontalAlignment.Stretch;
        topo.Children.Add(imp);
        DockPanel.SetDock(topo, Dock.Top);
        d.Children.Add(topo);
        var baixo = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        var dica = Txt(T("Também pode arrastar os .zip para aqui ou copiá-los para a pasta patches."), 11, CorFraca);
        dica.TextWrapping = TextWrapping.Wrap;
        baixo.Children.Add(dica);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
        var atual = Botao(T("Atualizar"), Recarregar, false, "relogio");
        g.Children.Add(atual);
        if (DadosJogo.PodeAbrirPasta)
        {
            var ab = Botao(T("Abrir pasta"), () => DadosJogo.AbrirPasta(BibliotecaPatches.Pasta(raiz)), false, "pasta");
            Grid.SetColumn(ab, 1);
            g.Children.Add(ab);
        }
        else Grid.SetColumnSpan(atual, 2);
        baixo.Children.Add(g);
        DockPanel.SetDock(baixo, Dock.Bottom);
        d.Children.Add(baixo);
        d.Children.Add(Ui.Afundado(lista));
        return Cartao(d, new Thickness(10));
    }

    void Recarregar()
    {
        try { patches = BibliotecaPatches.Listar(raiz); }
        catch (Exception e) { patches = new(); ctx.Mensagem(e.Message); }
        // os aplicados cujo zip saiu da biblioteca continuam na lista (podem ser revertidos)
        foreach (var r in BibliotecaPatches.RegistosAplicados(raiz))
            if (patches.All(p => p.Nome != r.Nome))
                patches.Add(new BibliotecaPatches.Info { Nome = r.Nome, Titulo = r.Titulo, Aplicado = true, AplicadoEm = r.Data, Erro = "sem-zip" });
        lista.Items.Clear();
        foreach (var p in patches)
        {
            var linha = new StackPanel { Spacing = 1, Margin = new Thickness(2, 1) };
            var cab = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (p.Aplicado)
                cab.Children.Add(new Border
                {
                    Background = Ui.Hex(Visual.Moderno ? "#2E5E3E" : "#1E7B34"), CornerRadius = new CornerRadius(8), Padding = new Thickness(5, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = T("Aplicado"), FontFamily = C.Inter, FontSize = 10.5, FontWeight = FontWeight.Bold, Foreground = Brushes.White },
                });
            cab.Children.Add(new TextBlock { Text = p.Titulo, FontFamily = C.Inter, FontSize = 13, FontWeight = FontWeight.Bold, Foreground = C.Texto, TextTrimming = TextTrimming.CharacterEllipsis });
            linha.Children.Add(cab);
            linha.Children.Add(new TextBlock { Text = Resumo(p), FontFamily = C.Inter, FontSize = 11, Foreground = C.TextoFraco, TextTrimming = TextTrimming.CharacterEllipsis });
            lista.Items.Add(new ListBoxItem { Content = linha, Tag = p.Nome, Padding = new Thickness(4, 3) });
        }
        if (escolhido == null || patches.All(p => p.Nome != escolhido)) escolhido = patches.FirstOrDefault()?.Nome;
        var i = patches.FindIndex(p => p.Nome == escolhido);
        lista.SelectedIndex = i;
        Escolheu();
    }

    internal static string Resumo(BibliotecaPatches.Info p)
    {
        if (p.Erro == "sem-zip") return T("O .zip saiu da biblioteca");
        if (p.Erro != null) return T("Zip com erro");
        var partes = new List<string>();
        if (p.Equipas > 0) partes.Add(p.Equipas == 1 ? T("1 equipa") : T("{0} equipas", p.Equipas));
        if (p.Paises) partes.Add(T("países"));
        if (p.Arbitros) partes.Add(T("árbitros"));
        if (p.Bandeiras > 0) partes.Add(p.Bandeiras == 1 ? T("1 bandeira") : T("{0} bandeiras", p.Bandeiras));
        if (p.Logos > 0) partes.Add(p.Logos == 1 ? T("1 logo") : T("{0} logos", p.Logos));
        if (p.Patrocinios > 0) partes.Add(T("patrocinadores"));
        return partes.Count > 0 ? string.Join(", ", partes) : T("Nada para aplicar");
    }

    // ---------------------------------------------------------------- o patch escolhido (direita)
    void Escolheu()
    {
        var p = patches.FirstOrDefault(x => x.Nome == escolhido);
        if (p == null) { detalhe.Child = Vazio(); return; }
        substituirTodas = p.PastaEquipas;
        Detalhe(p);
    }

    Control Vazio()
    {
        var s = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 440 };
        var circ = new Border
        {
            Width = 84, Height = 84, CornerRadius = new CornerRadius(42), Background = C.AzulFundo,
            BorderBrush = CorDestaque, BorderThickness = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Center,
            Child = Ui.Icone("peca", 44, CorDestaque),
        };
        s.Children.Add(circ);
        var t = Tit(T("Nenhum patch na biblioteca"), 22);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        s.Children.Add(t);
        var x = Txt(T("Importe um patch .zip Retrô (pastas EQUIPAS, FLAGS, COUNTRY.TXE...) ou um patch de equipas e logos exportado pelo Editor de Equipas."), 13);
        x.TextAlignment = TextAlignment.Center;
        s.Children.Add(x);
        var y = Txt(T("Aplicar um patch guarda os arquivos que ele substitui: pode revertê-lo quando quiser e voltar às equipas originais."), 12, CorFraca);
        y.TextAlignment = TextAlignment.Center;
        s.Children.Add(y);
        var b = Botao(T("Importar patch (.zip)"), Importar, true, "pasta");
        b.HorizontalAlignment = HorizontalAlignment.Center;
        s.Children.Add(b);
        return Cartao(s, new Thickness(24));
    }

    void Detalhe(BibliotecaPatches.Info p0)
    {
        var p = p0;
        BibliotecaPatches.Analise? an = null;
        if (p.Erro == null)
        {
            try
            {
                an = BibliotecaPatches.Analisar(raiz, p.Nome, substituirTodas);
                an.Info.Aplicado = p.Aplicado;
                an.Info.AplicadoEm = p.AplicadoEm;
                p = an.Info;
            }
            catch (Exception e) { p.Erro = e.Message; }
        }
        var s = new StackPanel { Spacing = 8 };
        var t = Tit(p.Titulo, 21);
        t.TextWrapping = TextWrapping.Wrap;
        s.Children.Add(t);
        var meta = new List<string>();
        if (p.Autor.Length > 0) meta.Add(T("Autor: {0}", p.Autor));
        if (p.Versao.Length > 0) meta.Add(T("Versão {0}", p.Versao));
        meta.Add(p.RetroFoot ? T("Patch do RetroFoot") : T("Patch Retrô (.EFT)"));
        if (p.Tamanho > 0) meta.Add(Tamanho(p.Tamanho));
        if (p.Erro != "sem-zip") s.Children.Add(Txt(string.Join("  ·  ", meta), 11.5, CorFraca));
        if (p.Aplicado)
            s.Children.Add(Aviso("info", T("Aplicado em {0}.", p.AplicadoEm?.ToString("g", Cultura()) ?? ""), "#1E7B34"));
        if (p.Erro == "sem-zip")
            s.Children.Add(Aviso("info", T("O .zip deste patch já não está na biblioteca, mas o patch continua aplicado e pode ser revertido."), "#7A5A00"));
        else if (p.Erro != null)
            s.Children.Add(Aviso("fechar", T("Não consegui ler este zip: {0}", p.Erro), "#8B1E1E"));
        if (p.Descricao.Length > 0)
        {
            var desc = Txt(p.Descricao, 12.5);
            s.Children.Add(new Border
            {
                Background = C.FundoBarra, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 7),
                MaxHeight = 120, Child = new ScrollViewer { Content = desc },
            });
        }
        if (an != null)
        {
            // o que ele traz
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 12, RowSpacing = 3 };
            void Linha(string r, string v)
            {
                int l = g.RowDefinitions.Count;
                g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var a = Txt(r, 12.5, CorRotulo, true);
                var b = Txt(v, 12.5);
                Grid.SetRow(a, l); Grid.SetRow(b, l); Grid.SetColumn(b, 1);
                g.Children.Add(a); g.Children.Add(b);
            }
            if (p.Equipas > 0)
            {
                Linha(T("Equipas"), p.Aplicado ? p.Equipas.ToString() : T("{0} ({1} novas, {2} substituídas)", p.Equipas, an.EquipasNovas, an.EquipasSubstituidas));
                var top = p.EquipasPorPais.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
                var txt = string.Join(", ", top.Take(ctx.Largo ? 12 : 8).Select(x => $"{x.Key} {x.Value}"));
                if (top.Count > (ctx.Largo ? 12 : 8)) txt += T(" e mais {0}", top.Count - (ctx.Largo ? 12 : 8));
                Linha(T("Países das equipas"), txt);
            }
            if (p.Paises) Linha(T("Países"), T("a lista de países (countries.tsv)"));
            if (p.Arbitros) Linha(T("Árbitros"), T("a lista de árbitros (referees.tsv)"));
            if (p.Grupos > 0) Linha(T("Grupos"), T("Lei Bosman / PLOP ({0})", p.Grupos));
            if (p.Bandeiras > 0) Linha(T("Bandeiras"), p.Bandeiras.ToString());
            if (p.Logos > 0) Linha(T("Logos"), p.Logos.ToString());
            if (p.Patrocinios > 0) Linha(T("Patrocinadores"), p.Patrocinios.ToString());
            if (p.Ignorados > 0) Linha(T("Ignorados"), p.Ignorados == 1 ? T("1 arquivo (programa, imagem...)") : T("{0} arquivos (programas, imagens...)", p.Ignorados));
            if (g.Children.Count > 0) s.Children.Add(new Border { Background = C.FundoBarra, CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8), Child = g });

            if (!p.Aplicado && p.Equipas > 0)
            {
                s.Children.Add(Marca(T("Substituir todas as equipas (como trocar a pasta EQUIPAS inteira)"), substituirTodas, v =>
                {
                    substituirTodas = v;
                    Detalhe(p0);
                }));
                if (substituirTodas && an.EquipasRetiradas > 0)
                    s.Children.Add(Txt(T("{0} equipas que o patch não traz saem do jogo (voltam ao reverter).", an.EquipasRetiradas), 11.5, CorFraca));
            }
            // conflitos e avisos
            if (!p.Aplicado)
            {
                foreach (var c in an.Conflitos)
                    s.Children.Add(Aviso("info", T("Conflito com o patch aplicado \"{1}\" ({0} em comum). Este patch passa por cima; para reverter \"{1}\" será preciso reverter este antes.", c.Comuns, c.Titulo), "#7A5A00"));
                if (an.PaisesDesconhecidos.Count > 0)
                    s.Children.Add(Aviso("info", T("Equipas de países que não estão na lista de países: {0}.", string.Join(", ", an.PaisesDesconhecidos.Take(12))), "#7A5A00"));
            }
        }
        if (p.Aplicado)
            foreach (var b in BibliotecaPatches.Bloqueadores(raiz, p.Nome))
                s.Children.Add(Aviso("info", T("Reverta antes \"{0}\" (aplicado depois e mexe nos mesmos arquivos).", b.Titulo), "#7A5A00"));

        // os botoes
        var botoes = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 6 };
        if (p.Aplicado) botoes.Children.Add(Botao(T("Reverter"), () => Reverter(p), true, "voltar"));
        else if (p.Erro == null && !p.Vazio) botoes.Children.Add(Botao(T("Aplicar"), () => Aplicar(p, an), true, "jogar"));
        if (p.Erro != "sem-zip") botoes.Children.Add(Botao(T("Remover da biblioteca"), () => Remover(p), false, "fechar"));

        var d = new DockPanel();
        DockPanel.SetDock(botoes, Dock.Bottom);
        botoes.Margin = new Thickness(0, 8, 0, 0);
        d.Children.Add(botoes);
        d.Children.Add(new ScrollViewer { Content = s, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        detalhe.Child = Cartao(d, new Thickness(14, 12));
    }

    internal static CultureInfo Cultura()
    {
        try { return CultureInfo.GetCultureInfo(Idioma.Atual); } catch { return CultureInfo.InvariantCulture; }
    }

    internal static string Tamanho(long b) => b >= 1 << 20 ? $"{b / 1048576.0:0.0} MB" : $"{Math.Max(1, b / 1024)} KB";

    // uma faixa colorida com icone e texto (avisos e estado)
    // (o fundo da barra com a cor a esquerda)
    Control Aviso(string icone, string texto, string cor)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        var ic = Ui.Icone(icone, 16, Clara(cor));
        ic.VerticalAlignment = VerticalAlignment.Top;
        g.Children.Add(ic);
        var t = Txt(texto, 12, CorTexto);
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        return new Border { Background = C.FundoBarra, BorderBrush = Clara(cor), BorderThickness = new Thickness(3, 0, 0, 0), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 6), Child = g };
    }

    // as cores dos avisos
    static IBrush Clara(string cor) => cor switch { "#1E7B34" => C.Verde, "#8B1E1E" => C.Erro, _ => C.Amarelo };

    // a caixa de marcar (arredondada, na cor do destaque) com o texto ao lado
    Control Marca(string texto, bool marcada, Action<bool> mudou)
    {
        var visto = marcada ? new PathIcon { Data = Geometry.Parse("M21,7L9,19L3.5,13.5L4.91,12.09L9,16.17L19.59,5.59L21,7Z"), Width = 12, Height = 12, Foreground = C.TextoEscuro } : null;
        var caixa = new Border
        {
            Width = 17, Height = 17, CornerRadius = new CornerRadius(4), Background = marcada ? C.Azul : C.Fundo, BorderBrush = marcada ? C.Azul : C.TextoFraco,
            BorderThickness = new Thickness(2), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0), Child = visto,
        };
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
        g.Children.Add(caixa);
        var t = Txt(texto, 12.5, CorTexto, true);
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        var c = new Clicavel
        {
            Child = g, Padding = new Thickness(3, 2), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent, CorFoco = C.Azul, Hover = C.FundoHover,
        };
        c.Clique += () => mudou(!marcada);
        Avalonia.Automation.AutomationProperties.SetName(c, texto);
        return c;
    }

    // ---------------------------------------------------------------- acoes
    async void Importar()
    {
        if (ocupado) return;
        var top = TopLevel.GetTopLevel(this);
        if (top?.StorageProvider is not { CanOpen: true } sp) { ctx.Mensagem(T("Copie o .zip para a pasta patches:\n{0}", BibliotecaPatches.Pasta(raiz))); return; }
        IReadOnlyList<IStorageFile> arqs;
        try
        {
            arqs = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = T("Importar patch (.zip)"),
                AllowMultiple = true,
                FileTypeFilter = new[] { new FilePickerFileType(T("Patch (.zip)")) { Patterns = new[] { "*.zip", "*.ZIP" } } },
            });
        }
        catch (Exception e) { ctx.Mensagem(e.Message); return; }
        ImportarArquivos(arqs);
    }

    async void ImportarArquivos(IReadOnlyList<IStorageFile> arqs)
    {
        if (ocupado || arqs.Count == 0) return;
        var (ok, erros) = await ImportarPara(raiz, arqs);
        if (ok.Count > 0) escolhido = ok[^1];
        Recarregar();
        if (erros.Count > 0) ctx.Mensagem(string.Join("\n", erros));
    }

    // poe os .zip escolhidos na biblioteca (tambem a versao do celular): os nomes que entraram
    // e os erros, um por arquivo
    internal static async Task<(List<string> Ok, List<string> Erros)> ImportarPara(string raiz, IReadOnlyList<IStorageFile> arqs)
    {
        var ok = new List<string>();
        var erros = new List<string>();
        foreach (var f in arqs)
        {
            string? tmp = null;
            try
            {
                var cam = f.TryGetLocalPath();
                if (cam == null)
                {
                    // sem caminho local (Android): copia por um temporario com o mesmo nome
                    var dir = Path.Combine(Path.GetTempPath(), "retrofoot-import-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dir);
                    tmp = cam = Path.Combine(dir, Path.GetFileName(f.Name));
                    await using var de = await f.OpenReadAsync();
                    await using var para = File.Create(cam);
                    await de.CopyToAsync(para);
                }
                ok.Add(BibliotecaPatches.Adicionar(raiz, cam));
            }
            catch (InvalidDataException e) when (e.Message == "vazio") { erros.Add(T("{0}: não tem nada que o RetroFoot use (equipas, países, árbitros, bandeiras, logos).", f.Name)); }
            catch (Exception e) { erros.Add($"{f.Name}: {e.Message}"); }
            finally { if (tmp != null) try { Directory.Delete(Path.GetDirectoryName(tmp)!, true); } catch { } }
        }
        return (ok, erros);
    }

    void Aplicar(BibliotecaPatches.Info p, BibliotecaPatches.Analise? an)
    {
        bool todas = substituirTodas;
        Confirmar(T("Aplicar patch"), PerguntaAplicar(p, an, todas), T("Aplicar"), () => Trabalhar(T("A aplicar o patch..."),
            () => BibliotecaPatches.Aplicar(raiz, p.Nome, todas), MensagemAplicado));
    }

    // os textos das acoes (tambem a versao do celular)
    internal static string PerguntaAplicar(BibliotecaPatches.Info p, BibliotecaPatches.Analise? an, bool todas)
    {
        var avisos = new List<string>();
        if (an != null)
        {
            if (todas && an.EquipasRetiradas > 0) avisos.Add(T("{0} equipas que o patch não traz saem do jogo (voltam ao reverter).", an.EquipasRetiradas));
            foreach (var c in an.Conflitos) avisos.Add(T("Passa por cima do patch \"{1}\" ({0} em comum).", c.Comuns, c.Titulo));
        }
        var texto = T("Aplicar \"{0}\" na pasta de dados? Os arquivos substituídos ficam guardados para reverter.", p.Titulo);
        if (avisos.Count > 0) texto += "\n\n" + string.Join("\n", avisos);
        return texto;
    }

    internal static string MensagemAplicado(BibliotecaPatches.Resultado r) => r.Ok
        ? T("Patch aplicado. Equipas: {0}; outros arquivos: {1}.", r.Equipas, r.Arquivos)
            + (r.Retirados > 0 ? "\n" + T("{0} equipas saíram (voltam ao reverter).", r.Retirados) : "")
            + (r.Avisos.Count > 0 ? "\n\n" + T("{0} arquivos com problema ficaram de fora:", r.Avisos.Count) + "\n" + string.Join("\n", r.Avisos.Take(6)) : "")
            + "\n\n" + T("Vale para os jogos novos; os jogos gravados continuam como estavam.")
        : T("O patch não foi aplicado (nada mudou): {0}", Erro(r.Erro));

    internal static string MensagemRevertido(BibliotecaPatches.Resultado r) => r.Ok
        ? T("Patch revertido. Arquivos repostos: {0}; apagados: {1}.", r.Restaurados, r.Apagados)
            + (r.CopiaDasMudancas != null ? "\n\n" + T("Arquivos mudados depois do patch foram guardados em:\n{0}", r.CopiaDasMudancas) : "")
        : T("Não foi possível reverter: {0}", Erro(r.Erro));

    internal static string TextoBloqueado(IEnumerable<BibliotecaPatches.RegistoPatch> bloq) =>
        T("Reverta antes \"{0}\" (aplicado depois e mexe nos mesmos arquivos).", string.Join("\", \"", bloq.Select(b => b.Titulo)));

    internal static string PerguntaRemover(BibliotecaPatches.Info p)
    {
        var texto = T("Tirar \"{0}\" da biblioteca? O .zip é apagado da pasta patches.", p.Titulo);
        if (p.Aplicado) texto += "\n\n" + T("O patch continua aplicado e pode ser revertido depois.");
        return texto;
    }

    void Reverter(BibliotecaPatches.Info p)
    {
        var bloq = BibliotecaPatches.Bloqueadores(raiz, p.Nome);
        if (bloq.Count > 0) { ctx.Mensagem(TextoBloqueado(bloq)); return; }
        Confirmar(T("Reverter patch"), T("Voltar a pasta de dados a como estava antes de \"{0}\"?", p.Titulo), T("Reverter"), () => Trabalhar(T("A reverter o patch..."),
            () => BibliotecaPatches.Reverter(raiz, p.Nome), MensagemRevertido));
    }

    internal static string Erro(string e) => e switch
    {
        "aplicado" => T("o patch já está aplicado"),
        "zip" => T("o .zip não está na biblioteca"),
        "nao-aplicado" => T("o patch não está aplicado"),
        "bloqueado" => T("outro patch aplicado depois mexe nos mesmos arquivos"),
        _ => e,
    };

    void Remover(BibliotecaPatches.Info p)
    {
        Confirmar(T("Remover da biblioteca"), PerguntaRemover(p), T("Remover"), () =>
        {
            try { BibliotecaPatches.Remover(raiz, p.Nome); }
            catch (Exception e) { ctx.Mensagem(e.Message); }
            Recarregar();
        });
    }

    // corre fora da interface (um patch grande tem milhares de equipas), com a espera por cima
    void Trabalhar(string titulo, Func<BibliotecaPatches.Resultado> fazer, Func<BibliotecaPatches.Resultado, string> mensagem)
    {
        ocupado = true;
        var espera = new StackPanel { Spacing = 12, Margin = new Thickness(24, 18), Width = 300 };
        espera.Children.Add(Txt(titulo, 14, CorTexto, true));
        espera.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 14, Foreground = CorDestaque });
        Cobrir(Janela(T("Patches"), espera, null, "relogio"));
        Task.Run(() =>
        {
            BibliotecaPatches.Resultado r;
            try { r = fazer(); }
            catch (Exception e) { r = new BibliotecaPatches.Resultado { Erro = e.Message }; }
            Dispatcher.UIThread.Post(() =>
            {
                ocupado = false;
                sobre.Children.Clear();
                Escudos.Recarregar();
                Recarregar();
                ctx.Mensagem(mensagem(r));
            });
        });
    }

    // uma pergunta com dois botoes (a janelinha sobre o veu, como Ui.Caixa)
    void Confirmar(string titulo, string texto, string sim, Action acao)
    {
        if (ocupado) return;
        var corpo = new StackPanel { Spacing = 14, Margin = new Thickness(18, 16), Width = 420 };
        corpo.Children.Add(Txt(texto, 13.5));
        var bs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var bSim = Botao(sim, () => { sobre.Children.Clear(); acao(); }, true);
        var bNao = Botao(T("Cancelar"), Fechar);
        bs.Children.Add(bSim);
        bs.Children.Add(bNao);
        corpo.Children.Add(bs);
        var j = Janela(titulo, corpo, Fechar, "peca");
        Cobrir(j);
        j.Loaded += (_, _) => bSim.Focus();
        void Fechar() { sobre.Children.Clear(); Focus(); }
    }

    static Control Janela(string titulo, Control conteudo, Action? fechar, string icone) => Ui.Janela(titulo, conteudo, fechar, icone);

    void Cobrir(Control janela)
    {
        var veu = Ui.Veu(janela);
        veu.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !ocupado) { e.Handled = true; sobre.Children.Clear(); }
        };
        sobre.Children.Clear();
        sobre.Children.Add(veu);
    }
}
