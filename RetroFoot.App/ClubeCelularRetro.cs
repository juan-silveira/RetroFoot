using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RetroFoot.Motor.Mercado;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// O Retro das janelas do clube e das mensagens no celular: as janelas do computador (TelaEstadio,
// TelaEmprestimo, TelaBalanco, Dlg.Perguntar/PedirTexto, TelaAcerca) adaptadas ao toque, em pe:
// as mesmas cores (o verde-claro do estadio, o amarelo do emprestimo, as cores da equipa no
// balanco, o verde do Sobre), os mesmos textos e os TBitBtn com os glifos do original. As regras
// sao as mesmas das janelas do Moderno (ClubeCelular.cs).
static partial class ClubeCelular
{
    // ---------------------------------------------------------------- pecas do Retro
    // um TBitBtn no toque: o glifo (Assets/<arquivo>, glifos lado a lado: o primeiro ligado, o
    // segundo desligado) acima do texto (emCima, os botoes de 64x64) ou antes dele, ampliado
    static CUi.Botao BotaoGlifo(string texto, bool principal, Action clique, string arquivo, int glifos = 2, bool emCima = true)
    {
        var b = new CUi.Botao(texto, principal, clique);
        if (W95.Imagem(arquivo) is not { } bmp || b.Child is not TextBlock t) return b;
        int w = bmp.PixelSize.Width / glifos, h = bmp.PixelSize.Height;
        IImage Parte(int i) => new CroppedBitmap(bmp, new PixelRect(i * w, 0, w, h));
        var ligado = Parte(0);
        var desligado = glifos > 1 ? Parte(1) : null;
        // o dobro, sem borrar (os glifos sao de 16 ou 18 pontos)
        double esc = emCima ? 2 : 1.5;
        var img = new Image { Width = w * esc, Height = h * esc, Source = ligado, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapInterpolationMode(img, emCima ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);
        void Estado()
        {
            bool on = b.IsEffectivelyEnabled;
            img.Source = on || desligado == null ? ligado : desligado;
            // (com um glifo so, o desligado do Windows 95 e o mesmo apagado)
            img.Opacity = on || desligado != null ? 1 : 0.45;
        }
        b.PropertyChanged += (_, e) => { if (e.Property == InputElement.IsEffectivelyEnabledProperty) Estado(); };
        b.Child = null;
        if (emCima)
        {
            // (a mesma altura para os glifos de 16 e de 18: os textos ficam alinhados)
            var caixa = new Border { Height = 36, Child = img };
            t.FontSize = 14 * Acessibilidade.Texto;
            b.Padding = new Thickness(4, 6);
            b.Child = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Children = { caixa, t } };
        }
        else
            b.Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Children = { img, t } };
        Estado();
        return b;
    }

    // o fundo da janela do computador em toda a folha (o corpo e o rodape, sem o fio)
    static void FundoFolha(CUi.Folha f, IBrush fundo)
    {
        if (f.Content is not Border b) return;
        b.Background = fundo;
        if (b.Child is DockPanel d)
            foreach (var c in d.Children)
                if (c is Border { Child: ContentControl } r) { r.Background = fundo; r.BorderThickness = new Thickness(0); }
    }

    // um botao so, no meio do rodape (o OK de 64x64 do computador, mais largo para o dedo)
    static Control Sozinho(CUi.Botao b)
    {
        b.HorizontalAlignment = HorizontalAlignment.Center;
        b.MinWidth = 140;
        return b;
    }

    static TextBlock Rot(string s, double tam, IBrush cor, bool negrito = false, bool quebra = false) =>
        CUi.Txt(s, tam, cor, negrito ? FontWeight.Bold : FontWeight.Normal, quebra);

    // ---------------------------------------------------------------- o estadio
    // a TstadiumDlg: a janela verde-clara ($80FF80) com o desenho das bancadas no tamanho do
    // original, a capacidade em negrito, o preco de uma arquibancada e o aviso vermelho;
    // Empréstimo (registado), Construir e Fechar com os glifos embaixo
    static void EstadioRetro(Sessao s, SEquipa e, bool registado, Action? fechou, Action<Control>? abrir, Action<Control>? fechar)
    {
        var mc = s.Mercado;
        var desenho = new TelaEstadio.Desenho(() => e.Estadio) { Width = 332, Height = 241, HorizontalAlignment = HorizontalAlignment.Center };
        var tCap = Rot("", 18, W95.Preto, true);
        var tPreco = Rot("", 15, W95.Preto, false, true);
        var aviso = new Border
        {
            Background = W95.Branco, Padding = new Thickness(6, 4), IsVisible = false,
            Child = Rot(T("As novas arquibancadas só estarão disponíveis no próximo jogo"), 14, W95.Vermelho, false, true),
        };
        ((TextBlock)aviso.Child).TextAlignment = TextAlignment.Center;

        var corpo = new StackPanel { Spacing = 10 };
        corpo.Children.Add(desenho);
        corpo.Children.Add(tCap);
        corpo.Children.Add(tPreco);
        corpo.Children.Add(aviso);

        Action fecharF = () => { };
        Action atualizar = () => { };
        var construir = BotaoGlifo(T("Construir"), false, () =>
        {
            if (!mc.PodeConstruir(e)) return;
            mc.ConstruirBancada(e);
            aviso.IsVisible = true;
            atualizar();
            TelaDinheiro.Avisar(e);
        }, "Botoes/CONSTRUIR.png", 1);
        ToolTip.SetTip(construir, T("Construir mais 5000 lugares"));
        var bs = new List<Control>();
        if (registado)
        {
            // 7:30b1: o emprestimo por cima; na volta, o preco/Construir de novo
            var emp = BotaoGlifo(T("Empréstimo"), false, () => Emprestimo(s, e, () => atualizar(), abrir, fechar), "Botoes/EMPRESTIMO.png");
            ToolTip.SetTip(emp, T("Pedir empréstimo"));
            bs.Add(emp);
        }
        bs.Add(construir);
        bs.Add(BotaoGlifo(T("Fechar"), true, () => fecharF(), "Botoes/BBOK.png"));
        var f = new CUi.Folha(T("Estádio"), corpo, CUi.Botoes(bs.ToArray()), fechar: () => fecharF());
        FundoFolha(f, W95.Cor(128, 255, 128));
        atualizar = () =>
        {
            tCap.Text = T("{0} lugares", FormatoDinheiro.Numero(MercadoJogo.Capacidade(e)));
            tPreco.Text = T("Preço de uma arquibancada com\n5000 lugares: ") + FormatoDinheiro.Valor(s.S, mc.PrecoBancada(e));
            construir.Habilitado = mc.PodeConstruir(e);
            desenho.InvalidateVisual();
        };
        atualizar();
        fecharF = Abrir(s, f, abrir, fechar, fechou);
        Teclas(f, () => fecharF(), () => fecharF());
    }

    // ---------------------------------------------------------------- o emprestimo
    // a TloanDlg: a janela amarela ($80FFFF); o capital em divida e o juro a pagar com o valor
    // grande ao lado; os botoes do passo (o + pede, o - paga) e Fechar; o dinheiro em caixa; o
    // fio e as tres explicacoes
    static void EmprestimoRetro(Sessao s, SEquipa e, Action? fechou, Action<Control>? abrir, Action<Control>? fechar)
    {
        var mc = s.Mercado;
        var banco = mc.AbrirBanco(e);
        string passo = FormatoDinheiro.Valor(s.S, mc.PassoEmprestimo);
        var tDivida = Rot("", 20, W95.Preto, true);
        var tJuro = Rot("", 20, W95.Preto, true);
        var tDinheiro = Rot("", 15, W95.Preto);
        // as colunas do original: o rotulo a esquerda e o valor a partir dos 144 pontos
        var valores = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 14 };
        void Par(int linha, string rotulo, TextBlock v)
        {
            var r = Rot(rotulo, 14, W95.Preto, true, true);
            r.VerticalAlignment = v.VerticalAlignment = VerticalAlignment.Top;
            r.Margin = new Thickness(0, 4, 8, 0);
            Grid.SetRow(r, linha); Grid.SetRow(v, linha); Grid.SetColumn(v, 1);
            valores.Children.Add(r);
            valores.Children.Add(v);
        }
        Par(0, T("Capital em dívida"), tDivida);
        Par(1, T("Juro a pagar"), tJuro);

        Action atualizar = () => { };
        Action fecharF = () => { };
        var pedir = BotaoGlifo(passo, false, () =>
        {
            if (!banco.PodePedir(e)) return;
            mc.PedirEmprestimo(e);
            atualizar();
            TelaDinheiro.Avisar(e);
        }, "Botoes/PEDIR.png");
        var pagar = BotaoGlifo(passo, false, () =>
        {
            if (!mc.PodePagar(e)) return;
            mc.PagarEmprestimo(e);
            atualizar();
            TelaDinheiro.Avisar(e);
        }, "Botoes/PAGAR.png");
        ToolTip.SetTip(pedir, T("Pedir empréstimo"));
        var botoes = CUi.Botoes(pedir, pagar, BotaoGlifo(T("Fechar"), true, () => fecharF(), "Botoes/BBOK.png"));

        var caixa = new StackPanel { Spacing = 2, Margin = new Thickness(150, 0, 0, 0) };
        caixa.Children.Add(Rot(T("Dinheiro em caixa"), 14, W95.Preto, true));
        caixa.Children.Add(tDinheiro);

        var notas = new StackPanel { Spacing = 10 };
        foreach (var n in new[]
        {
            T("Os juros a pagar pelo empréstimo são de 5% do capital em dívida por cada jogo realizado"),
            T("O montante em dívida poderá ser reduzido sempre que o desejar, ou aumentado, se tal lhe for permitido."),
            T("O montante máximo a emprestar está condicionado pela confiança que o banco tem em si, neste momento."),
        })
            notas.Children.Add(Rot(n, 13.5, W95.Preto, false, true));

        var corpo = new StackPanel { Spacing = 14 };
        corpo.Children.Add(valores);
        corpo.Children.Add(botoes);
        corpo.Children.Add(caixa);
        // Panel1: o fio de 1 ponto que separa as explicacoes
        corpo.Children.Add(new Border { Height = 1, Background = W95.Sombra, Margin = new Thickness(-14, 0) });
        corpo.Children.Add(notas);

        var f = new CUi.Folha(T("Empréstimos"), corpo, null, fechar: () => fecharF());
        FundoFolha(f, W95.Cor(255, 255, 128));
        atualizar = () =>
        {
            int d = (int)e.D6D;
            tDivida.Text = FormatoDinheiro.Valor(s.S, d);
            tJuro.Text = FormatoDinheiro.Valor(s.S, MercadoJogo.Juros(d));
            tDinheiro.Text = FormatoDinheiro.Valor(s.S, (int)e.Dinheiro);
            pedir.Habilitado = banco.PodePedir(e);
            pagar.Habilitado = mc.PodePagar(e);
        };
        atualizar();
        fecharF = Abrir(s, f, abrir, fechar, fechou);
        Teclas(f, () => fecharF(), () => fecharF());
    }

    // ---------------------------------------------------------------- o balanco
    // a TbalanceDlg: a janela e a lista nas cores da equipa, as tres temporadas lado a lado
    // (a atual, a anterior e a de antes) como na TThreeBalances; em pe o rotulo vai numa linha e
    // os tres valores embaixo, cada um na coluna do seu ano. Temporada, Receitas e Gastos em
    // negrito; OK embaixo
    static void BalancoRetro(Sessao s, SEquipa e, ReadOnlyMemory<byte>[] t, Action? fechou, Action<Control>? abrir, Action<Control>? fechar)
    {
        var (letra, fundo) = TelaDinheiro.Cores(e);
        int ano = s.S.Cabecalho.Ano;
        long V(int k, int slot) => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(t[k].Span.Slice(4 * slot));
        long Soma(int k, params int[] slots) { long x = 0; foreach (var sl in slots) x += V(k, sl); return x; }
        string M(long v) => FormatoDinheiro.Valor(s.S, v);

        var lista = new StackPanel { Spacing = 1 };
        Grid Colunas(Func<int, string> valor, bool negrito)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 6 };
            for (int k = 0; k < 3; k++)
            {
                var v = Rot(valor(k), 14, letra, negrito);
                v.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(v, k);
                g.Children.Add(v);
            }
            return g;
        }
        void Linha(string rotulo, Func<int, string>? valor, bool negrito = false)
        {
            var p = new StackPanel { Margin = new Thickness(0, 0, 0, valor != null ? 5 : 1) };
            p.Children.Add(Rot(rotulo, 14, letra, negrito));
            if (valor != null) p.Children.Add(Colunas(valor, negrito));
            lista.Children.Add(p);
        }
        Linha(T("Temporada"), k => (ano - k).ToString(), true);
        Linha(T("Receitas"), null, true);
        Linha(T("Bilheteria"), k => M(V(k, 0)));
        Linha(T("Jogadores vendidos"), k => M(V(k, 1)));
        Linha(T("Prêmios"), k => M(V(k, 2)));
        Linha(T("Gastos"), null, true);
        Linha(T("Salários"), k => M(V(k, 4)));
        Linha(T("Jogadores comprados"), k => M(V(k, 3)));
        Linha(T("Arquibancadas"), k => M(V(k, 5)));
        Linha(T("Juros"), k => M(V(k, 6)));
        lista.Children.Add(new Border { Height = 10 });
        Linha(T("Receita total"), k => M(Soma(k, 0, 1, 2)));
        Linha(T("Gasto total"), k => M(Soma(k, 3, 4, 5, 6)));
        Linha(T("Saldo"), k => M(Soma(k, 0, 1, 2, 3, 4, 5, 6)));

        Action fecharF = () => { };
        var ok = BotaoGlifo(T("OK"), true, () => fecharF(), "Botoes/BBOK.png");
        var f = new CUi.Folha(TelaDinheiro.Texto(e.NomeCompleto), CUi.GrupoRetro(null, lista), Sozinho(ok), fechar: () => fecharF());
        FundoFolha(f, fundo);
        fecharF = Abrir(s, f, abrir, fechar, fechou);
        Teclas(f, () => fecharF(), () => fecharF());
    }

    // ---------------------------------------------------------------- as mensagens
    // o MessageBox Sim/Nao do computador: o texto e Sim e Nao (nessa ordem) com os glifos do
    // classico; o x, Voltar e Esc = Nao, Enter = Sim
    static void PerguntarRetro(Dialogos d, string titulo, string texto, Action sim, Action? nao)
    {
        CUi.Folha? f = null;
        bool respondeu = false;
        void Responder(bool s)
        {
            if (respondeu) return;
            respondeu = true;
            d.Fechar(f!);
            if (s) sim(); else nao?.Invoke();
        }
        var bs = CUi.Botoes(BotaoGlifo(T("Sim"), true, () => Responder(true), "Dialogos/BBYES.png", emCima: false),
            BotaoGlifo(T("Não"), false, () => Responder(false), "Dialogos/BBNO.png", emCima: false));
        var corpo = Rot(texto, 15, W95.Preto, false, true);
        corpo.Margin = new Thickness(4, 8);
        f = new CUi.Folha(titulo, corpo, bs, fechar: () => Responder(false));
        Teclas(f, () => Responder(true), () => Responder(false));
        d.Abrir(f);
    }

    // a TInputDlg: o rotulo e a caixa dentro do Bevel; OK e Cancelar com os glifos
    static void PedirTextoRetro(Dialogos d, string titulo, string rotulo, string inicial, int maximo, Action<string> ok, bool apara)
    {
        CUi.Folha? f = null;
        bool fechou = false;
        var caixa = CUi.Caixa(inicial, maximo);
        // a Edit do Windows 95: no foco e com o dedo em cima continua afundada, sem o azul do Fluent
        foreach (var k in new[] { "TextControlBorderBrushFocused", "TextControlBorderBrushPointerOver" })
            caixa.Resources[k] = W95.Escura;
        caixa.Resources["TextControlBackgroundFocused"] = W95.Branco;
        caixa.Resources["TextControlBackgroundPointerOver"] = W95.Branco;
        void Ok()
        {
            if (fechou) return;
            var t = caixa.Text ?? "";
            if (apara) { t = t.Trim(); if (t.Length == 0) return; }
            fechou = true;
            d.Fechar(f!);
            ok(t);
        }
        void Cancelar() { if (fechou) return; fechou = true; d.Fechar(f!); }
        var dentro = new StackPanel { Spacing = 12 };
        dentro.Children.Add(Rot(rotulo, 15, W95.Preto, false, true));
        dentro.Children.Add(caixa);
        var bs = CUi.Botoes(BotaoGlifo(T("OK"), true, Ok, "Botoes/BBOK.png", emCima: false),
            BotaoGlifo(T("Cancelar"), false, Cancelar, "Botoes/BBCANCEL.png", emCima: false));
        f = new CUi.Folha(titulo, CUi.GrupoRetro(null, dentro), bs, fechar: Cancelar);
        Teclas(f, Ok, Cancelar, caixa);
        f.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => { caixa.Focus(); caixa.SelectAll(); }, DispatcherPriority.Loaded);
        d.Abrir(f);
    }

    // a mensagem com OK: o texto e o OK com o visto verde
    static Control CaixaRetro(string titulo, string texto, Action ok)
    {
        var corpo = Rot(texto, 15, W95.Preto, false, true);
        corpo.Margin = new Thickness(4, 8);
        var f = new CUi.Folha(titulo, corpo, CUi.Botoes(BotaoGlifo(T("OK"), true, ok, "Botoes/BBOK.png", emCima: false)), fechar: ok);
        Teclas(f, ok, ok);
        return new Panel { Background = TemaVerde.Veu, Children = { f } };
    }

    // ---------------------------------------------------------------- o Sobre
    // a TaboutDlg do RetroFoot: a janela verde com o logo, o slogan amarelo e a versao em branco;
    // OK com o visto e, ao lado, os Extras (no computador ficam no menu)
    static void SobreRetro(Dialogos d, Action? fechou)
    {
        CUi.Folha? f = null;
        bool fechado = false;
        void Fechar()
        {
            if (fechado) return;
            fechado = true;
            d.Fechar(f!);
            fechou?.Invoke();
        }
        var corpo = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 6) };
        var logo = Logotipo.Logo(60);
        logo.HorizontalAlignment = HorizontalAlignment.Center;
        corpo.Children.Add(logo);
        var slogan = Rot(TelaAcerca.Slogan, 19, W95.Amarelo, true);
        slogan.HorizontalAlignment = HorizontalAlignment.Center;
        slogan.Margin = new Thickness(0, 6, 0, 0);
        corpo.Children.Add(slogan);
        var ver = Rot(T("Versão {0}", TelaAcerca.Versao), 14, W95.Branco, true);
        ver.HorizontalAlignment = HorizontalAlignment.Center;
        corpo.Children.Add(ver);

        var extras = new CUi.Botao(T("Extras"), false, () => Extras(d));
        var ok = BotaoGlifo(T("OK"), true, Fechar, "Botoes/BBOK.png");
        f = new CUi.Folha(T("Sobre"), corpo, CUi.Botoes(extras, ok), fechar: Fechar);
        FundoFolha(f, W95.Verde);
        Teclas(f, Fechar, Fechar);
        d.Abrir(f);
    }

    // TAddInsDlg: a lista branca afundada dentro do Bevel, os nomes em negrito; OK embaixo
    static void ExtrasRetro(Dialogos d)
    {
        var nomes = Nomes();
        CUi.Folha? f = null;
        void Fechar() => d.Fechar(f!);
        var linhas = nomes.Select(x => (Control)new Border { Padding = new Thickness(8, 5), Child = Rot(x, 15, W95.Preto, true, true) });
        var ok = BotaoGlifo(T("OK"), true, Fechar, "Botoes/BBOK.png");
        f = new CUi.Folha(T("Extras"), CUi.GrupoRetro(null, CUi.Grupo(linhas)), Sozinho(ok), fechar: Fechar);
        Teclas(f, Fechar, Fechar);
        d.Abrir(f);
    }
}
