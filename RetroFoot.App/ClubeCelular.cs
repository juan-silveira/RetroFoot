using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RetroFoot.Motor.Mercado;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// As janelas do clube e as mensagens no celular (Celular.cs), nas folhas do kit (CelularUi.cs),
// nas duas peles: o estadio, o emprestimo, o balanco, as perguntas Sim/Nao e o pedido de um
// texto (Dlg.Perguntar / PedirTexto), a mensagem com OK (Ui.Caixa) e o Sobre com os Extras.
// As regras continuam as das janelas do computador (MercadoJogo, Banco...): aqui so se mostra
// e se recolhe o toque. Enter e Esc continuam a valer com teclado.
static class ClubeCelular
{
    // ---------------------------------------------------------------- pecas
    // a folha recebe Enter (o botao principal) e Esc (fechar) como as janelas do computador
    static void Teclas(CUi.Folha f, Action? enter, Action? esc, Control? foco = null)
    {
        f.Focusable = true;
        f.FocusAdorner = null;
        f.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Handled) return;
            if (e.Key == Key.Enter && enter != null) { enter(); e.Handled = true; }
            else if (e.Key == Key.Escape && esc != null) { esc(); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Bubble);
        f.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (foco is { IsEffectivelyVisible: true }) foco.Focus(); else f.Focus();
        }, DispatcherPriority.Loaded);
    }

    // uma linha "rotulo .... valor" dentro de um Grupo (com a margem das linhas)
    static Control Linha(string rotulo, string valor, IBrush? cor = null, bool negrito = false) =>
        new Border { Padding = new Thickness(12, 3), MinHeight = 36, Child = CUi.Linha_(rotulo, valor, cor, negrito) };

    // o valor grande com o rotulo em cima (a capacidade, a divida)
    static (StackPanel Painel, TextBlock Valor) Destaque(string rotulo)
    {
        var v = CUi.Txt("", 26, null, FontWeight.Bold);
        var p = new StackPanel { Spacing = 4 };
        p.Children.Add(CUi.SecaoFolha(rotulo));
        p.Children.Add(v);
        return (p, v);
    }

    // a barra de progresso (0 a 1): no Retro a do Windows 95 (afundada, blocos azul-marinho)
    static Control Barra(Func<double> fracao, Func<IBrush> cor) => new Pintor((dc, s) =>
    {
        var r = new Rect(s);
        double f = Math.Clamp(fracao(), 0, 1);
        if (CUi.Retro)
        {
            dc.FillRectangle(W95.Branco, r);
            W95.Linhas(dc, r, W95.Sombra, W95.Luz);
            double w = Math.Floor(f * (r.Width - 4));
            for (double x = 2; x < 2 + w; x += 9)
                dc.FillRectangle(cor(), new Rect(x, 2, Math.Min(7, 2 + w - x), r.Height - 4));
            return;
        }
        dc.DrawRectangle(TemaVerde.Linha, null, r, s.Height / 2, s.Height / 2);
        double l = f * s.Width;
        if (l > 0) dc.DrawRectangle(cor(), null, new Rect(0, 0, Math.Max(l, s.Height), s.Height), s.Height / 2, s.Height / 2);
    }) { Height = CUi.Retro ? 16 : 8 };

    // abre a folha (pela Sessao ou pelo "abrir" de quem chama) e devolve o que a fecha (uma vez)
    static Action Abrir(Sessao s, CUi.Folha f, Action<Control>? abrir, Action<Control>? fechar, Action? fechou)
    {
        bool fechado = false;
        void Fechar()
        {
            if (fechado) return;
            fechado = true;
            (fechar ?? (c => s.FecharDialogo(c)))(f);
            fechou?.Invoke();
        }
        f.AoVoltar = Fechar;
        (abrir ?? s.AbrirDialogo)(f);
        return Fechar;
    }

    // a folha com o "fechar" ligado depois (o x e o Voltar chamam o que Abrir devolve)
    static CUi.Folha Folha(string titulo, Control corpo, Control? rodape, string? sub, SEquipa? e, Action fechar) =>
        new(titulo, corpo, rodape, sub, e != null ? CUi.Escudo(e, 34) : null, fechar: fechar);

    // ---------------------------------------------------------------- o estadio
    // o desenho no alto (o do classico no Retro; o isometrico no Moderno), a capacidade com as
    // arquibancadas de 24, o preco da proxima e o dinheiro; Empréstimo (registado) e Construir
    // embaixo. Construir so liga com MercadoJogo.PodeConstruir (dinheiro e menos de 24).
    public static void Estadio(Sessao s, SEquipa e, bool registado, Action? fechou, Action<Control>? abrir, Action<Control>? fechar)
    {
        var mc = s.Mercado;
        Control desenho;
        if (CUi.Retro)
        {
            // o TPaintBox de 332x241 na cor da janela do original ($80FF80), na largura da folha
            var d = new TelaEstadio.Desenho(() => e.Estadio) { Width = 332, Height = 241 };
            desenho = new Border
            {
                Background = W95.Cor(128, 255, 128), BorderBrush = W95.Escura, BorderThickness = new Thickness(1), Padding = new Thickness(4),
                Child = new Viewbox { Stretch = Stretch.Uniform, MaxHeight = 210, Child = d },
            };
        }
        else
            desenho = new Border
            {
                Background = TemaVerde.CartaoHub, BorderBrush = TemaVerde.Borda, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
                Padding = new Thickness(6), Child = new EstadioIsometrico(e) { Height = 210 },
            };

        var (cap, tCap) = Destaque(T("Capacidade"));
        var tBancadas = CUi.Txt("", 13, CUi.Fraco);
        cap.Children.Add(Barra(() => e.Estadio / 24.0, () => CUi.Retro ? W95.Marinho : TemaVerde.Verde));
        cap.Children.Add(tBancadas);
        var tPreco = CUi.Txt("", 15, null, FontWeight.SemiBold);
        var tDinheiro = CUi.Txt("", 15, null, FontWeight.SemiBold);
        Control LinhaV(string rotulo, TextBlock v)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 30 };
            g.Children.Add(CUi.Txt(rotulo, 14, CUi.Fraco, FontWeight.Normal, true));
            v.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            return new Border { Padding = new Thickness(12, 8), MinHeight = 44, Child = g };
        }
        var aviso = CUi.Txt(T("As novas arquibancadas só estarão disponíveis no próximo jogo"), 14, CUi.Retro ? CUi.Erro : CUi.Bom, FontWeight.SemiBold, true);
        aviso.IsVisible = false;
        aviso.TextAlignment = TextAlignment.Center;
        aviso.HorizontalAlignment = HorizontalAlignment.Center;

        var corpo = new StackPanel { Spacing = 12 };
        corpo.Children.Add(desenho);
        corpo.Children.Add(cap);
        corpo.Children.Add(CUi.Grupo(new[] { LinhaV(T("Nova arquibancada (5000 lugares)"), tPreco), LinhaV(T("Dinheiro em caixa"), tDinheiro) }));
        corpo.Children.Add(aviso);

        Action fecharF = () => { };
        Action atualizar = () => { };
        var construir = new CUi.Botao(T("Construir"), true, () =>
        {
            if (!mc.PodeConstruir(e)) return;
            mc.ConstruirBancada(e);
            aviso.IsVisible = true;
            atualizar();
            TelaDinheiro.Avisar(e);
        });
        ToolTip.SetTip(construir, T("Construir mais 5000 lugares"));
        var bs = new List<Control>();
        if (registado)
        {
            // 7:30b1: o emprestimo por cima; na volta, o preco/Construir de novo
            var emp = new CUi.Botao(T("Empréstimo"), false, () => Emprestimo(s, e, () => atualizar(), abrir, fechar));
            ToolTip.SetTip(emp, T("Pedir empréstimo"));
            bs.Add(emp);
        }
        bs.Add(construir);
        var f = Folha(T("Estádio"), corpo, CUi.Botoes(bs.ToArray()), TelaDinheiro.Texto(e.NomeCompleto), e, () => fecharF());
        atualizar = () =>
        {
            tCap.Text = T("{0} lugares", FormatoDinheiro.Numero(MercadoJogo.Capacidade(e)));
            tBancadas.Text = T("{0} de 24 arquibancadas", e.Estadio);
            tPreco.Text = FormatoDinheiro.Valor(s.S, mc.PrecoBancada(e));
            tDinheiro.Text = FormatoDinheiro.Valor(s.S, (int)e.Dinheiro);
            tDinheiro.Foreground = (int)e.Dinheiro < 0 ? CUi.Erro : CUi.Texto;
            construir.Habilitado = mc.PodeConstruir(e);
            foreach (var c in cap.Children) c.InvalidateVisual();
            desenho.InvalidateVisual();
            if (desenho is Border { Child: Viewbox { Child: { } dd } }) dd.InvalidateVisual();
            if (desenho is Border { Child: EstadioIsometrico iso }) iso.InvalidateVisual();
        };
        atualizar();
        fecharF = Abrir(s, f, abrir, fechar, fechou);
        Teclas(f, () => fecharF(), () => fecharF());
    }

    // ---------------------------------------------------------------- o emprestimo
    // a divida em destaque com a barra do limite do banco (fixo ao abrir, Banco), o juro por
    // jogo e o dinheiro; Pagar e Pedir (um passo cada) embaixo; as explicacoes do original
    public static void Emprestimo(Sessao s, SEquipa e, Action? fechou, Action<Control>? abrir, Action<Control>? fechar)
    {
        var mc = s.Mercado;
        var banco = mc.AbrirBanco(e);
        string passo = FormatoDinheiro.Valor(s.S, mc.PassoEmprestimo);
        var (div, tDivida) = Destaque(T("Capital em dívida"));
        var tLimite = CUi.Txt("", 13, CUi.Fraco);
        int Divida() => (int)e.D6D;
        div.Children.Add(Barra(() => banco.Limite > 0 ? (double)Divida() / banco.Limite : 0,
            () => Divida() >= banco.Limite ? CUi.Erro : CUi.Retro ? W95.Marinho : TemaVerde.Destaque));
        div.Children.Add(tLimite);
        var tJuro = CUi.Txt("", 15, CUi.Erro, FontWeight.SemiBold);
        var tDinheiro = CUi.Txt("", 15, null, FontWeight.SemiBold);
        Control LinhaV(string rotulo, TextBlock v)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 30 };
            g.Children.Add(CUi.Txt(rotulo, 14, CUi.Fraco));
            v.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            return new Border { Padding = new Thickness(12, 8), MinHeight = 44, Child = g };
        }
        var notas = new StackPanel { Spacing = 8 };
        foreach (var n in new[]
        {
            T("Os juros a pagar pelo empréstimo são de 5% do capital em dívida por cada jogo realizado."),
            T("O montante em dívida poderá ser reduzido sempre que o desejar, ou aumentado, se tal lhe for permitido."),
            T("O montante máximo a emprestar está condicionado pela confiança que o banco tem em si, neste momento."),
        })
            notas.Children.Add(CUi.Txt(n, 13, CUi.Fraco, FontWeight.Normal, true));

        var corpo = new StackPanel { Spacing = 12 };
        corpo.Children.Add(div);
        corpo.Children.Add(CUi.Grupo(new[] { LinhaV(T("Juro por jogo"), tJuro), LinhaV(T("Dinheiro em caixa"), tDinheiro) }));
        corpo.Children.Add(notas);

        Action atualizar = () => { };
        var pagar = new CUi.Botao(T("−  Pagar {0}", passo), false, () =>
        {
            if (!mc.PodePagar(e)) return;
            mc.PagarEmprestimo(e);
            atualizar();
            TelaDinheiro.Avisar(e);
        });
        var pedir = new CUi.Botao(T("+  Pedir {0}", passo), true, () =>
        {
            if (!banco.PodePedir(e)) return;
            mc.PedirEmprestimo(e);
            atualizar();
            TelaDinheiro.Avisar(e);
        });
        // o passo nao cabe numa linha com o texto em pe: o valor (inteiro) na linha de baixo
        foreach (var b in new[] { pagar, pedir })
            if (b.Child is TextBlock tb)
            {
                tb.Text = tb.Text?.Replace(" " + passo, "\n" + passo).Replace(passo, passo.Replace(' ', '\u00A0'));
                tb.TextWrapping = TextWrapping.Wrap;
                tb.TextTrimming = TextTrimming.None;
                tb.TextAlignment = TextAlignment.Center;
                b.Padding = new Thickness(10, 6);
            }
        Action fecharF = () => { };
        var f = Folha(T("Empréstimos"), corpo, CUi.Botoes(pagar, pedir), TelaDinheiro.Texto(e.NomeCompleto), e, () => fecharF());
        atualizar = () =>
        {
            int d = Divida();
            tDivida.Text = FormatoDinheiro.Valor(s.S, d);
            tLimite.Text = T("Limite do banco: ") + FormatoDinheiro.Valor(s.S, banco.Limite);
            tJuro.Text = FormatoDinheiro.Valor(s.S, MercadoJogo.Juros(d));
            tDinheiro.Text = FormatoDinheiro.Valor(s.S, (int)e.Dinheiro);
            tDinheiro.Foreground = (int)e.Dinheiro < 0 ? CUi.Erro : CUi.Texto;
            pedir.Habilitado = banco.PodePedir(e);
            pagar.Habilitado = mc.PodePagar(e);
            foreach (var c in div.Children) c.InvalidateVisual();
        };
        atualizar();
        fecharF = Abrir(s, f, abrir, fechar, fechou);
        Teclas(f, () => fecharF(), () => fecharF());
    }

    // ---------------------------------------------------------------- o balanco
    // as tres temporadas nao cabem lado a lado em pe: uma de cada vez (as abas com os anos), as
    // receitas e os gastos em grupos, os totais e o saldo pelo sinal; embaixo, o saldo das tres
    // (tocar numa mostra-a)
    public static void Balanco(Sessao s, SEquipa e, ReadOnlyMemory<byte>[] t, Action? fechou, Action<Control>? abrir, Action<Control>? fechar)
    {
        int ano = s.S.Cabecalho.Ano;
        long V(int k, int slot) => BinaryPrimitives.ReadInt32LittleEndian(t[k].Span.Slice(4 * slot));
        long Soma(int k, params int[] slots) { long x = 0; foreach (var sl in slots) x += V(k, sl); return x; }
        string M(long v) => FormatoDinheiro.Valor(s.S, v);
        IBrush Sinal(long v) => v > 0 ? CUi.Bom : v < 0 ? CUi.Erro : CUi.Fraco;
        int[] todos = { 0, 1, 2, 3, 4, 5, 6 };

        var miolo = new ContentControl();
        Action<int> marcarAba = _ => { };
        void Mostrar(int k)
        {
            var p = new StackPanel { Spacing = 8 };
            p.Children.Add(CUi.SecaoFolha(T("Receitas")));
            p.Children.Add(CUi.Grupo(new[]
            {
                Linha(T("Bilheteria"), M(V(k, 0))),
                Linha(T("Jogadores vendidos"), M(V(k, 1))),
                Linha(T("Prêmios"), M(V(k, 2))),
                Linha(T("Receita total"), M(Soma(k, 0, 1, 2)), CUi.Bom, true),
            }));
            var g = CUi.SecaoFolha(T("Gastos"));
            g.Margin = new Thickness(0, 6, 0, 0);
            p.Children.Add(g);
            p.Children.Add(CUi.Grupo(new[]
            {
                Linha(T("Salários"), M(V(k, 4))),
                Linha(T("Jogadores comprados"), M(V(k, 3))),
                Linha(T("Arquibancadas"), M(V(k, 5))),
                Linha(T("Juros"), M(V(k, 6))),
                Linha(T("Gasto total"), M(Soma(k, 3, 4, 5, 6)), CUi.Erro, true),
            }));
            long saldo = Soma(k, todos);
            var sg = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 6, 0, 0) };
            sg.Children.Add(CUi.Txt(T("Saldo"), 17, null, FontWeight.Bold));
            var sv = CUi.Txt(M(saldo), 20, Sinal(saldo), FontWeight.Bold);
            Grid.SetColumn(sv, 1);
            sg.Children.Add(sv);
            p.Children.Add(sg);
            miolo.Content = p;
        }
        void Escolher(int k) { marcarAba(k); Mostrar(k); }

        var anos = new[] { ano.ToString(), (ano - 1).ToString(), (ano - 2).ToString() };
        var (fila, marcar) = CUi.Abas(anos, Mostrar);
        marcarAba = marcar;
        if (fila is ScrollViewer { Content: Control filaP }) filaP.Margin = new Thickness(0, 0, 0, 4);

        var resumo = new List<Control>();
        for (int k = 0; k < 3; k++)
        {
            int kk = k;
            long sd = Soma(k, todos);
            var gr = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            gr.Children.Add(CUi.Txt(T("Temporada") + " " + anos[k], 15, null, k == 0 ? FontWeight.Bold : FontWeight.Medium));
            var v = CUi.Txt(M(sd), 15, Sinal(sd), FontWeight.SemiBold);
            Grid.SetColumn(v, 1);
            gr.Children.Add(v);
            resumo.Add(CUi.Toque(gr, () => Escolher(kk), 44));
        }

        var corpo = new StackPanel { Spacing = 8 };
        corpo.Children.Add(fila);
        corpo.Children.Add(miolo);
        var rs = CUi.SecaoFolha(T("Saldo") + " · " + anos[2] + "–" + anos[0]);
        rs.Margin = new Thickness(0, 10, 0, 0);
        corpo.Children.Add(rs);
        corpo.Children.Add(CUi.Grupo(resumo));
        Mostrar(0);

        Action fecharF = () => { };
        var ok = new CUi.Botao(T("OK"), true, () => fecharF()) { HorizontalAlignment = HorizontalAlignment.Stretch };
        var f = Folha(TelaDinheiro.Texto(e.NomeCompleto), corpo, ok, T("Balanço financeiro"), e, () => fecharF());
        fecharF = Abrir(s, f, abrir, fechar, fechou);
        Teclas(f, () => fecharF(), () => fecharF());
    }

    // ---------------------------------------------------------------- as perguntas
    // o MessageBox Sim/Nao: Voltar e Esc = Nao, Enter = Sim
    public static void Perguntar(Dialogos d, string titulo, string texto, Action sim, Action? nao)
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
        f = CUi.Pergunta(titulo, texto, new (string, bool, Action)[] { (T("Não"), false, () => Responder(false)), (T("Sim"), true, () => Responder(true)) },
            () => Responder(false));
        Teclas(f, () => Responder(true), () => Responder(false));
        d.Abrir(f);
    }

    // a TInputDlg: o rotulo e a caixa (o teclado do telefone sobe); Cancelar e OK embaixo.
    // vazio = como o computador (quem chama decide). apara: o Moderno das Configuracoes apara e
    // nao aceita vazio
    public static void PedirTexto(Dialogos d, string titulo, string rotulo, string inicial, int maximo, Action<string> ok, bool apara = false)
    {
        CUi.Folha? f = null;
        bool fechou = false;
        var caixa = CUi.Caixa(inicial, maximo);
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
        var corpo = new StackPanel { Spacing = 10 };
        corpo.Children.Add(CUi.Txt(rotulo, 15, null, FontWeight.Normal, true));
        corpo.Children.Add(caixa);
        if (maximo > 0)
        {
            var conta = CUi.Txt("", 12.5, CUi.Fraco);
            conta.HorizontalAlignment = HorizontalAlignment.Right;
            void Contar() => conta.Text = $"{(caixa.Text ?? "").Length}/{maximo}";
            caixa.TextChanged += (_, _) => Contar();
            Contar();
            corpo.Children.Add(conta);
        }
        f = new CUi.Folha(titulo, corpo, CUi.Botoes(new CUi.Botao(T("Cancelar"), false, Cancelar), new CUi.Botao(T("OK"), true, Ok)), fechar: Cancelar);
        Teclas(f, Ok, Cancelar, caixa);
        f.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => { caixa.Focus(); caixa.SelectAll(); }, DispatcherPriority.Loaded);
        d.Abrir(f);
    }

    // a mensagem com OK (Ui.Caixa): a folha sobre o veu, num controle que quem chama poe por cima
    public static Control Caixa(string titulo, string texto, Action ok)
    {
        var f = CUi.Pergunta(titulo, texto, new (string, bool, Action)[] { (T("OK"), true, ok) }, ok);
        Teclas(f, ok, ok);
        return new Panel { Background = TemaVerde.Veu, Children = { f } };
    }

    // ---------------------------------------------------------------- o Sobre
    // o logo, o slogan e a versao; os extras ativos num toque (a TAddInsDlg)
    public static void Sobre(Dialogos d, Action? fechou)
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
        var topo = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 4) };
        var logo = Logotipo.Logo(64);
        logo.HorizontalAlignment = HorizontalAlignment.Center;
        // no Retro o nome (verde e ouro) vai no fundo verde do classico, como no Sobre do computador
        topo.Children.Add(CUi.Retro
            ? new Border { Background = W95.Verde, BorderBrush = W95.Escura, BorderThickness = new Thickness(1), Padding = new Thickness(14, 10), Child = logo }
            : logo);
        var slogan = CUi.Txt(TelaAcerca.Slogan, 18, CUi.Retro ? W95.Marinho : TemaVerde.Destaque, FontWeight.Bold);
        slogan.HorizontalAlignment = HorizontalAlignment.Center;
        topo.Children.Add(slogan);
        var ver = CUi.Etiqueta(T("Versão {0}", TelaAcerca.Versao));
        ver.HorizontalAlignment = HorizontalAlignment.Center;
        topo.Children.Add(ver);

        int n = Nomes().Count;
        var linha = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        var tx = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        tx.Children.Add(CUi.Txt(T("Extras"), 15, null, FontWeight.SemiBold));
        tx.Children.Add(CUi.Txt(T("{0} extras ativos", n), 12.5, CUi.Fraco));
        linha.Children.Add(tx);
        var seta = CUi.Txt("›", 22, CUi.Fraco);
        Grid.SetColumn(seta, 2);
        linha.Children.Add(seta);

        var corpo = new StackPanel { Spacing = 14 };
        corpo.Children.Add(topo);
        corpo.Children.Add(CUi.Grupo(new Control[] { CUi.Toque(linha, () => Extras(d)) }));
        var ok = new CUi.Botao(T("OK"), true, Fechar) { HorizontalAlignment = HorizontalAlignment.Stretch };
        f = new CUi.Folha(T("Sobre"), corpo, ok, TelaAcerca.Nome, Logotipo.ImagemBola(32), fechar: Fechar);
        Teclas(f, Fechar, Fechar);
        d.Abrir(f);
    }

    static List<string> Nomes() => Registo.AddIns.Where(a => Registo.AddIn(a.Id)).Select(a => T(a.Nome))
        .OrderBy(x => x, StringComparer.Ordinal).ToList();

    // TAddInsDlg: os add-ins ativos, por nome (Sorted)
    public static void Extras(Dialogos d)
    {
        var nomes = Nomes();
        CUi.Folha? f = null;
        void Fechar() => d.Fechar(f!);
        var linhas = nomes.Select(x =>
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), MinHeight = 30 };
            var v = CUi.Txt("✓", 15, CUi.Bom, FontWeight.Bold);
            v.Width = 24;
            g.Children.Add(v);
            var t = CUi.Txt(x, 15, null, FontWeight.Normal, true);
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            return (Control)new Border { Padding = new Thickness(12, 6), MinHeight = 40, Child = g };
        });
        var ok = new CUi.Botao(T("OK"), true, Fechar) { HorizontalAlignment = HorizontalAlignment.Stretch };
        f = new CUi.Folha(T("Extras"), CUi.Grupo(linhas), ok, T("{0} extras ativos", nomes.Count), fechar: Fechar);
        Teclas(f, Fechar, Fechar);
        d.Abrir(f);
    }
}
