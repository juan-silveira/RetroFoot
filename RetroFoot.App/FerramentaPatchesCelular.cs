using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using RetroFoot.Motor.Arquivos;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// A ferramenta Patches no celular (Ferramentas.RegistrarCelular): a mesma biblioteca do
// computador (FerramentaPatches.cs; a logica em Motor/Arquivos/BibliotecaPatches.cs) em pe e ao
// toque, nas duas peles do kit (CelularUi.cs):
//   a pagina   a lista dos patches (tocar abre o patch), Atualizar e, no rodape, Importar
//              (o seletor de arquivos do Android: o .zip vem da memoria do celular);
//   a folha    o patch escolhido: o que ele traz, substituir todas as equipas, os conflitos e
//              os avisos, Aplicar ou Reverter e Remover da biblioteca;
//   perguntas e a espera tambem em folhas, por cima da pagina (o Voltar do Android as fecha).
static class FerramentaPatchesCelular
{
    [ModuleInitializer]
    internal static void Registar() => Ferramentas.RegistrarCelular(Ferramentas.Patches, ctx => new TelaPatchesCelular(ctx));
}

public sealed class TelaPatchesCelular : UserControl
{
    readonly Ferramentas.Contexto ctx;
    readonly string raiz;
    readonly ContentControl pagina = new();
    readonly Panel sobre = new() { IsVisible = false };
    List<BibliotecaPatches.Info> patches = new();
    bool ocupado;

    static IBrush H(string s) => new ImmutableSolidColorBrush(Color.Parse(s));
    // as cores do estado (aplicado, aviso, erro) nas duas peles
    static IBrush CorBom => CUi.Retro ? H("#006400") : TemaVerde.Verde;
    static IBrush CorAviso => CUi.Retro ? H("#806000") : TemaVerde.Ouro;
    static IBrush CorErro => CUi.Erro;

    public TelaPatchesCelular(Ferramentas.Contexto ctx)
    {
        this.ctx = ctx;
        raiz = ctx.RaizDados;
        Directory.CreateDirectory(BibliotecaPatches.Pasta(raiz));
        Content = new Panel { Children = { pagina, sobre } };
        Recarregar();
    }

    // ---------------------------------------------------------------- a pagina (a biblioteca)
    void Recarregar()
    {
        try { patches = BibliotecaPatches.Listar(raiz); }
        catch (Exception e) { patches = new(); ctx.Mensagem(e.Message); }
        // os aplicados cujo zip saiu da biblioteca continuam na lista (podem ser revertidos)
        foreach (var r in BibliotecaPatches.RegistosAplicados(raiz))
            if (patches.All(p => p.Nome != r.Nome))
                patches.Add(new BibliotecaPatches.Info { Nome = r.Nome, Titulo = r.Titulo, Aplicado = true, AplicadoEm = r.Data, Erro = "sem-zip" });
        Montar();
    }

    void Montar()
    {
        var corpo = new StackPanel { Spacing = 12 };
        Control miolo = patches.Count == 0 ? Vazio() : CUi.Grupo(patches.Select(Linha));
        var dica = CUi.Txt(T("Toque em Importar e escolha os .zip guardados no celular."), 13, null, FontWeight.Normal, true);
        var atual = Botao(T("Atualizar"), false, Recarregar);
        var acoes = DadosJogo.PodeAbrirPasta
            ? CUi.Botoes(atual, Botao(T("Abrir pasta"), false, () => DadosJogo.AbrirPasta(BibliotecaPatches.Pasta(raiz))))
            : CUi.Botoes(atual);
        if (CUi.Retro)
        {
            // a janela do computador: a barra de titulo e a caixa de lista branca afundada
            dica.Foreground = W95.Preto;
            corpo.Children.Add(CUi.Cartao_(T("Biblioteca de patches"), new StackPanel { Spacing = 10, Children = { miolo, dica, acoes } },
                patches.Count > 0 ? CUi.Txt(patches.Count.ToString(), 14, W95.Preto, FontWeight.Bold) : null));
        }
        else
        {
            corpo.Children.Add(CUi.TituloSecao_(patches.Count > 0 ? T("Biblioteca de patches") + " · " + patches.Count : T("Biblioteca de patches")));
            corpo.Children.Add(miolo);
            dica.Foreground = CUi.Fraco;
            corpo.Children.Add(dica);
            corpo.Children.Add(acoes);
        }
        var imp = Botao(T("Importar patch (.zip)"), true, Importar);
        imp.HorizontalAlignment = HorizontalAlignment.Stretch;
        pagina.Content = CUi.Pagina(new Border(), corpo, imp);
    }

    // uma linha da lista: o titulo (com o selo Aplicado) e o resumo; tocar abre a folha
    Control Linha(BibliotecaPatches.Info p)
    {
        var cab = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (p.Aplicado) cab.Children.Add(Selo());
        cab.Children.Add(CUi.Txt(p.Titulo, 16, null, FontWeight.Bold));
        var t = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { cab } };
        var res = CUi.Txt(TelaPatches.Resumo(p), 13, p.Erro != null && p.Erro != "sem-zip" ? CorErro : CUi.Fraco, FontWeight.Normal, true);
        t.Children.Add(res);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        g.Children.Add(t);
        var seta = CUi.Txt("›", 24, CUi.Fraco);
        seta.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(seta, 1);
        g.Children.Add(seta);
        return CUi.Toque(g, () => Abrir(p.Nome), 60);
    }

    static Border Selo() => CUi.Etiqueta(T("Aplicado"), Brushes.White, CUi.Retro ? H("#1E7B34") : H("#2E5E3E"));

    Control Vazio()
    {
        var s = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        var cor = CUi.Retro ? W95.Marinho : TemaVerde.Destaque;
        s.Children.Add(new Border
        {
            Width = 76, Height = 76, CornerRadius = new CornerRadius(38), HorizontalAlignment = HorizontalAlignment.Center,
            Background = CUi.Retro ? W95.Branco : TemaVerde.DestaqueFundo, BorderBrush = cor, BorderThickness = new Thickness(2),
            Child = Ui.Icone("peca", 38, cor),
        });
        var t = CUi.Txt(T("Nenhum patch na biblioteca"), 18, null, FontWeight.Bold, true);
        t.TextAlignment = TextAlignment.Center;
        s.Children.Add(t);
        var x = CUi.Txt(T("Importe um patch .zip Retrô (pastas EQUIPAS, FLAGS, COUNTRY.TXE...) ou um patch de equipas e logos exportado pelo Editor de Equipas."), 14.5, null, FontWeight.Normal, true);
        x.TextAlignment = TextAlignment.Center;
        s.Children.Add(x);
        var y = CUi.Txt(T("Aplicar um patch guarda os arquivos que ele substitui: pode revertê-lo quando quiser e voltar às equipas originais."), 13, CUi.Fraco, FontWeight.Normal, true);
        y.TextAlignment = TextAlignment.Center;
        s.Children.Add(y);
        // (no Retro, dentro da caixa de lista branca; no Moderno, um cartao)
        if (CUi.Retro) return CUi.Grupo(new Control[] { new Border { Padding = new Thickness(14, 18), Child = s } });
        return CUi.Cartao_(null, new Border { Padding = new Thickness(4, 10), Child = s });
    }

    // ---------------------------------------------------------------- a folha do patch
    void Abrir(string nome, bool? todas = null)
    {
        var p0 = patches.FirstOrDefault(x => x.Nome == nome);
        if (p0 == null || ocupado) return;
        bool substituirTodas = todas ?? p0.PastaEquipas;
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
        var s = new StackPanel { Spacing = 12 };
        if (p.Aplicado)
            s.Children.Add(Aviso(T("Aplicado em {0}.", p.AplicadoEm?.ToString("g", TelaPatches.Cultura()) ?? ""), CorBom));
        if (p.Erro == "sem-zip")
            s.Children.Add(Aviso(T("O .zip deste patch já não está na biblioteca, mas o patch continua aplicado e pode ser revertido."), CorAviso));
        else if (p.Erro != null)
            s.Children.Add(Aviso(T("Não consegui ler este zip: {0}", p.Erro), CorErro));
        if (p.Descricao.Length > 0) s.Children.Add(Descricao(p.Descricao));
        if (an != null)
        {
            if (Conteudo(p, an) is { } traz) s.Children.Add(traz);
            if (!p.Aplicado && p.Equipas > 0)
            {
                string? retiradas = substituirTodas && an.EquipasRetiradas > 0
                    ? T("{0} equipas que o patch não traz saem do jogo (voltam ao reverter).", an.EquipasRetiradas) : null;
                var texto = T("Substituir todas as equipas (como trocar a pasta EQUIPAS inteira)");
                if (CUi.Retro)
                {
                    var m = new StackPanel { Spacing = 2 };
                    m.Children.Add(CR95.Marca(texto, substituirTodas, v => Dispatcher.UIThread.Post(() => Abrir(nome, v))));
                    if (retiradas != null) m.Children.Add(CR95.Txt(retiradas, 13, H("#404040")));
                    s.Children.Add(m);
                }
                else
                {
                    var i = CUi.Interruptor(texto, retiradas, substituirTodas, v => Dispatcher.UIThread.Post(() => Abrir(nome, v)));
                    s.Children.Add(new Border
                    {
                        Background = TemaVerde.BotaoSecundario, BorderBrush = TemaVerde.Borda, BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(12), Child = i, ClipToBounds = true,
                    });
                }
            }
            // conflitos e avisos
            if (!p.Aplicado)
            {
                foreach (var c in an.Conflitos)
                    s.Children.Add(Aviso(T("Conflito com o patch aplicado \"{1}\" ({0} em comum). Este patch passa por cima; para reverter \"{1}\" será preciso reverter este antes.", c.Comuns, c.Titulo), CorAviso));
                if (an.PaisesDesconhecidos.Count > 0)
                    s.Children.Add(Aviso(T("Equipas de países que não estão na lista de países: {0}.", string.Join(", ", an.PaisesDesconhecidos.Take(12))), CorAviso));
            }
        }
        if (p.Aplicado)
            foreach (var b in BibliotecaPatches.Bloqueadores(raiz, p.Nome))
                s.Children.Add(Aviso(T("Reverta antes \"{0}\" (aplicado depois e mexe nos mesmos arquivos).", b.Titulo), CorAviso));

        // os botoes (a acao principal e o remover)
        var bs = new List<Control>();
        var pp = p;
        if (p.Aplicado) bs.Add(Botao(T("Reverter"), true, () => Reverter(pp)));
        else if (p.Erro == null && !p.Vazio) bs.Add(Botao(T("Aplicar"), true, () => Aplicar(pp, an, substituirTodas), "Botoes/BBOK.png"));
        if (p.Erro != "sem-zip") bs.Add(Botao(T("Remover"), false, () => Remover(pp), "Botoes/EXCLUIR.png"));
        // (no Retro, a acao primeiro, como no computador; no Moderno, o principal a direita)
        if (!CUi.Retro) bs.Reverse();

        var meta = new List<string>();
        if (p.Autor.Length > 0) meta.Add(T("Autor: {0}", p.Autor));
        if (p.Versao.Length > 0) meta.Add(T("Versão {0}", p.Versao));
        meta.Add(p.RetroFoot ? T("Patch do RetroFoot") : T("Patch Retrô (.EFT)"));
        if (p.Tamanho > 0) meta.Add(TelaPatches.Tamanho(p.Tamanho));
        var ic = Ui.Icone("peca", 26, CUi.Retro ? W95.Preto : TemaVerde.Destaque);
        var folha = new CUi.Folha(p.Titulo, s, bs.Count > 0 ? CUi.Botoes(bs.ToArray()) : null,
            p.Erro != "sem-zip" ? string.Join("  ·  ", meta) : null, ic, fechar: Fechar);
        Mostrar(folha, true);
    }

    // a descricao do zip (o leia-me): as primeiras linhas e o Ver tudo
    static Control Descricao(string texto)
    {
        var t = CUi.Txt(texto.Trim(), 14, null, FontWeight.Normal, true);
        t.MaxLines = 6;
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        var s = new StackPanel { Spacing = 4, Children = { t } };
        // (so com o texto longo: mais linhas do que cabem ou muito texto)
        if (texto.Count(c => c == '\n') >= 6 || texto.Length > 260)
        {
            var mais = CUi.Txt(T("Ver tudo"), 14, CUi.Retro ? W95.Marinho : TemaVerde.Destaque, FontWeight.Bold);
            var toque = new Clicavel { Child = mais, Background = Brushes.Transparent, MinHeight = 36, CorFoco = null, HorizontalAlignment = HorizontalAlignment.Left };
            toque.Clique += () =>
            {
                bool aberto = t.MaxLines == 0;
                t.MaxLines = aberto ? 6 : 0;
                mais.Text = aberto ? T("Ver tudo") : T("Ver menos");
            };
            s.Children.Add(toque);
        }
        return Caixa(s);
    }

    // o que o patch traz: rotulo e valor, um embaixo do outro quando nao cabe
    Control? Conteudo(BibliotecaPatches.Info p, BibliotecaPatches.Analise an)
    {
        var linhas = new StackPanel { Spacing = 8 };
        void L(string r, string v)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*"), ColumnSpacing = 10 };
            var a = CUi.Txt(r, 14, CUi.Retro ? H("#404040") : CUi.Fraco, FontWeight.Bold, true);
            a.VerticalAlignment = VerticalAlignment.Top;
            var b = CUi.Txt(v, 14.5, null, FontWeight.Medium, true);
            b.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(b, 1);
            g.Children.Add(a); g.Children.Add(b);
            linhas.Children.Add(g);
        }
        if (p.Equipas > 0)
        {
            L(T("Equipas"), p.Aplicado ? p.Equipas.ToString() : T("{0} ({1} novas, {2} substituídas)", p.Equipas, an.EquipasNovas, an.EquipasSubstituidas));
            var top = p.EquipasPorPais.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
            var txt = string.Join(", ", top.Take(10).Select(x => $"{x.Key} {x.Value}"));
            if (top.Count > 10) txt += T(" e mais {0}", top.Count - 10);
            L(T("Países das equipas"), txt);
        }
        if (p.Paises) L(T("Países"), T("a lista de países (countries.tsv)"));
        if (p.Arbitros) L(T("Árbitros"), T("a lista de árbitros (referees.tsv)"));
        if (p.Grupos > 0) L(T("Grupos"), T("Lei Bosman / PLOP ({0})", p.Grupos));
        if (p.Bandeiras > 0) L(T("Bandeiras"), p.Bandeiras.ToString());
        if (p.Logos > 0) L(T("Logos"), p.Logos.ToString());
        if (p.Patrocinios > 0) L(T("Patrocinadores"), p.Patrocinios.ToString());
        if (p.Ignorados > 0) L(T("Ignorados"), p.Ignorados == 1 ? T("1 arquivo (programa, imagem...)") : T("{0} arquivos (programas, imagens...)", p.Ignorados));
        return linhas.Children.Count > 0 ? Caixa(linhas) : null;
    }

    // a caixa de um bloco da folha (no Retro, a moldura TGroupBox; no Moderno, o fundo mais escuro)
    static Control Caixa(Control c) => CUi.Retro
        ? CUi.GrupoRetro(null, c)
        : new Border { Background = TemaVerde.CartaoHub, BorderBrush = TemaVerde.Borda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10), Child = c };

    // uma faixa de aviso: a cor do estado a esquerda e o texto
    static Control Aviso(string texto, IBrush cor)
    {
        var t = CUi.Txt(texto, 14, CUi.Retro ? W95.Preto : null, FontWeight.Normal, true);
        if (CUi.Retro)
            return new Border
            {
                Background = H("#FFFFE1"), BorderBrush = W95.Preto, BorderThickness = new Thickness(1), Padding = new Thickness(0),
                Child = new Border { BorderBrush = cor, BorderThickness = new Thickness(5, 0, 0, 0), Padding = new Thickness(9, 7), Child = t },
            };
        return new Border
        {
            Background = TemaVerde.CartaoHub, BorderBrush = cor, BorderThickness = new Thickness(4, 0, 0, 0),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 8), Child = t,
        };
    }

    // um botao nas duas peles (no Retro, o TBitBtn com o glifo do computador ao lado)
    static CUi.Botao Botao(string texto, bool principal, Action acao, string? glifo = null) =>
        CUi.Retro && glifo != null ? CR95.Botao(texto, glifo, principal, acao, false) : new CUi.Botao(texto, principal, acao);

    // ---------------------------------------------------------------- por cima da pagina
    void Mostrar(CUi.Folha folha, bool fechaNoVeu)
    {
        var veu = new Panel { Background = TemaVerde.Veu };
        if (fechaNoVeu) veu.PointerPressed += (_, e) => { if (e.Source == veu) Fechar(); };
        // (a folha nunca passa da altura da tela: o corpo dela rola)
        folha.MaxHeight = Math.Max(240, (Bounds.Height > 0 ? Bounds.Height : 700) - 36);
        veu.Children.Add(folha);
        sobre.Children.Clear();
        sobre.Children.Add(veu);
        sobre.IsVisible = true;
    }

    void Fechar()
    {
        if (ocupado) return;
        sobre.Children.Clear();
        sobre.IsVisible = false;
    }

    // uma pergunta (Aplicar, Reverter, Remover) como folha
    void Confirmar(string titulo, string texto, string sim, Action acao)
    {
        if (ocupado) return;
        void Sim() { Fechar(); acao(); }
        var bSim = CUi.Retro ? CR95.Botao(sim, "Botoes/BBYES.png", true, Sim, false) : new CUi.Botao(sim, true, Sim);
        var bNao = CUi.Retro ? CR95.Botao(T("Cancelar"), "Botoes/BBCANCEL.png", false, Fechar, false) : new CUi.Botao(T("Cancelar"), false, Fechar);
        // (no Retro, o Sim primeiro, como no computador; no Moderno, o principal a direita)
        var bs = CUi.Retro ? CUi.Botoes(bSim, bNao) : CUi.Botoes(bNao, bSim);
        Mostrar(new CUi.Folha(titulo, CUi.Txt(texto, 15.5, null, FontWeight.Normal, true), bs, fechar: Fechar), true);
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
                // (o Android filtra pelos tipos MIME; alguns gestores dao o zip como octet-stream)
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(T("Patch (.zip)"))
                    {
                        Patterns = new[] { "*.zip", "*.ZIP" },
                        MimeTypes = new[] { "application/zip", "application/x-zip-compressed", "application/x-zip", "application/octet-stream" },
                    },
                },
            });
        }
        catch (Exception e) { ctx.Mensagem(e.Message); return; }
        if (arqs.Count == 0) return;
        Esperar(T("A importar o patch..."));
        List<string> ok, erros;
        try { (ok, erros) = await TelaPatches.ImportarPara(raiz, arqs); }
        catch (Exception e) { ok = new(); erros = new() { e.Message }; }
        ocupado = false;
        Fechar();
        Recarregar();
        // um so: abre a folha dele (o que traz e o Aplicar)
        if (ok.Count == 1 && erros.Count == 0) Abrir(ok[0]);
        if (erros.Count > 0) ctx.Mensagem(string.Join("\n", erros));
    }

    void Aplicar(BibliotecaPatches.Info p, BibliotecaPatches.Analise? an, bool todas) =>
        Confirmar(T("Aplicar patch"), TelaPatches.PerguntaAplicar(p, an, todas), T("Aplicar"), () => Trabalhar(T("A aplicar o patch..."),
            () => BibliotecaPatches.Aplicar(raiz, p.Nome, todas), TelaPatches.MensagemAplicado));

    void Reverter(BibliotecaPatches.Info p)
    {
        var bloq = BibliotecaPatches.Bloqueadores(raiz, p.Nome);
        if (bloq.Count > 0) { ctx.Mensagem(TelaPatches.TextoBloqueado(bloq)); return; }
        Confirmar(T("Reverter patch"), T("Voltar a pasta de dados a como estava antes de \"{0}\"?", p.Titulo), T("Reverter"), () => Trabalhar(T("A reverter o patch..."),
            () => BibliotecaPatches.Reverter(raiz, p.Nome), TelaPatches.MensagemRevertido));
    }

    void Remover(BibliotecaPatches.Info p) =>
        Confirmar(T("Remover da biblioteca"), TelaPatches.PerguntaRemover(p), T("Remover"), () =>
        {
            try { BibliotecaPatches.Remover(raiz, p.Nome); }
            catch (Exception e) { ctx.Mensagem(e.Message); }
            Recarregar();
        });

    // a espera numa folha sem fechar (o Voltar do Android nao a tira)
    void Esperar(string texto)
    {
        ocupado = true;
        var barra = new ProgressBar { IsIndeterminate = true, HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0, Height = 18, MinHeight = 18 };
        if (CUi.Retro)
        {
            barra.Foreground = W95.Marinho; barra.Background = W95.Branco; barra.BorderBrush = W95.Escura;
            barra.BorderThickness = new Thickness(1); barra.CornerRadius = new CornerRadius(0);
        }
        else barra.Foreground = TemaVerde.Destaque;
        var corpo = new StackPanel { Spacing = 14, Margin = new Thickness(0, 4, 0, 8), Children = { CUi.Txt(texto, 15, null, FontWeight.Normal, true), barra } };
        var f = new CUi.Folha(T("Patches"), corpo, rolar: false) { AoVoltar = null };
        Mostrar(f, false);
    }

    // corre fora da interface (um patch grande tem milhares de equipas), com a espera por cima
    void Trabalhar(string titulo, Func<BibliotecaPatches.Resultado> fazer, Func<BibliotecaPatches.Resultado, string> mensagem)
    {
        Esperar(titulo);
        Task.Run(() =>
        {
            BibliotecaPatches.Resultado r;
            try { r = fazer(); }
            catch (Exception e) { r = new BibliotecaPatches.Resultado { Erro = e.Message }; }
            Dispatcher.UIThread.Post(() =>
            {
                ocupado = false;
                Fechar();
                Escudos.Recarregar();
                Recarregar();
                ctx.Mensagem(mensagem(r));
            });
        });
    }
}
