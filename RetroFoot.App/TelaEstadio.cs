using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RetroFoot.Motor.Mercado;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// A TstadiumDlg (7:28a3, aberta por Equipa > Estádio, 4:413c): o desenho das bancadas
// (PaintBoxStadiumPaint), a capacidade, o preco de uma bancada de 5000 lugares e os botoes
// Empréstimo (so com o add-in 0x13, registado), Construir e Fechar. Atualizar (7:2958) liga o
// Construir se preco <= dinheiro e bancadas < 24; construir (7:3036) mostra o aviso vermelho.
// O original nao mostra a ocupacao (EstadioReal) nesta janela.
public static class TelaEstadio
{
    // abre a janela por cima da tela atual (thread da interface, motor parado); registado: o
    // botao Empréstimo (add-in 0x13); fechou: ao fechar. Ao construir (e no emprestimo aberto
    // daqui) dispara TelaDinheiro.Mudou.
    public static void Abrir(Sessao s, SEquipa e, bool registado, Action? fechou = null) =>
        Abrir(s, e, registado, fechou, null, null);

    internal static void Abrir(Sessao s, SEquipa e, bool registado, Action? fechou, Action<Control>? abrir, Action<Control>? fechar)
    {
        if (Celular.Ativo) { ClubeCelular.Estadio(s, e, registado, fechou, abrir, fechar); return; }
        if (Visual.Moderno) { AbrirModerno(s, e, registado, fechou, abrir, fechar); return; }
        var mc = s.Mercado;
        var j = new Janela(T("Estádio"), 474, 372, W95.Cor(128, 255, 128));   // Color = $80FF80
        j.Por(new Desenho(() => e.Estadio), 71, 8, 332, 241);
        var tCap = j.Por(new Rotulo { Fonte = TelaDinheiro.Arial16b, Auto = true }, 8, 232, 200, 19);
        var tPreco = j.Por(new Rotulo { Fonte = TelaDinheiro.Arial13, Auto = true }, 16, 280, 300, 40);
        // labelWarning (AutoSize, taCenter): o texto do pt-BR e mais largo que os 363 do DFM; o
        // rotulo cresce mantendo o centro
        string textoAviso = T("As novas arquibancadas só estarão disponíveis no próximo jogo");
        double la = Math.Max(363, Math.Ceiling(W95.Txt(textoAviso, TelaDinheiro.Arial13, W95.Preto).WidthIncludingTrailingWhitespace) + 2);
        var aviso = j.Por(new Rotulo
        {
            Texto = textoAviso, Fonte = TelaDinheiro.Arial13,
            Cor = W95.Vermelho, Fundo = W95.Branco, Alin = TextAlignment.Center, IsVisible = false,
        }, Math.Floor(55 + (363 - la) / 2), 352, la, 16);
        var fechar0 = j.Por(BotaoMargem.Ok(T("Fechar"), TelaDinheiro.Arial9, -1), 392, 280, 64, 64);
        var emprestimo = j.Por(new BotaoMargem { Texto = T("Empréstimo"), Glifo = W95.Imagem("Botoes/EMPRESTIMO.png"), NumGlifos = 2, IsVisible = registado }, 232, 280, 64, 64);
        var construir = j.Por(new BotaoMargem { Texto = T("Construir"), Glifo = W95.Imagem("Botoes/CONSTRUIR.png") }, 312, 280, 64, 64);
        ToolTip.SetTip(emprestimo, T("Pedir empréstimo"));
        ToolTip.SetTip(construir, T("Construir mais 5000 lugares"));

        void Atualizar()
        {
            tCap.Texto = T("{0} lugares", FormatoDinheiro.Numero(MercadoJogo.Capacidade(e)));
            tPreco.Texto = T("Preço de uma arquibancada com\n5000 lugares: ") + FormatoDinheiro.Valor(s.S, mc.PrecoBancada(e));
            construir.Habilitado = mc.PodeConstruir(e);
            j.InvalidateTudo();
        }
        construir.Clique += () =>
        {
            if (!mc.PodeConstruir(e)) return;
            if (s.Remoto is { } rmB) { rmB.Executar(new RetroFoot.Motor.Online.ConstruirBancada()); return; }
            mc.ConstruirBancada(e);
            aviso.IsVisible = true;
            Atualizar();
            TelaDinheiro.Avisar(e);
        };
        // 7:30b1: a janela do emprestimo por cima; na volta, o preco/Construir de novo
        emprestimo.Clique += () => { if (registado) TelaEmprestimo.Abrir(s, e, Atualizar, abrir, fechar); };
        Atualizar();
        var d = TelaDinheiro.Abrir(s, j, abrir, fechar);
        if (fechou != null) d.Fechou += fechou;
        fechar0.Clique += d.Fechar;
        j.AoEnter = d.Fechar;
    }

    // ------------------------------------------------------------ Moderno
    // O estadio no visual Moderno: o desenho isometrico (EstadioIsometrico) num
    // cartao; a capacidade, as arquibancadas construidas (de 24), o preco da proxima e o
    // dinheiro em caixa; Empréstimo (registado), Construir e Fechar. No 4:3 o desenho fica em
    // cima; no 16:9, a esquerda, com os numeros ao lado.
    static void AbrirModerno(Sessao s, SEquipa e, bool registado, Action? fechou, Action<Control>? abrir, Action<Control>? fechar)
    {
        var mc = s.Mercado;
        var desenho = new EstadioIsometrico(e);
        var tCap = ClubeModerno.T("", 26, ClubeModerno.Texto, FontWeight.Bold);
        var tBancadas = ClubeModerno.T("", 12, ClubeModerno.Fraco);
        var barra = new ContentControl();
        var tPreco = ClubeModerno.T("", 15, ClubeModerno.Texto, FontWeight.SemiBold);
        var tDinheiro = ClubeModerno.T("", 15, ClubeModerno.Texto, FontWeight.SemiBold);
        var aviso = ClubeModerno.T(T("As novas arquibancadas só estarão disponíveis no próximo jogo"), 12.5, ClubeModerno.Verde, FontWeight.SemiBold);
        aviso.IsVisible = false;

        var cap = new StackPanel { Spacing = 6 };
        cap.Children.Add(ClubeModerno.Rotulo(T("Capacidade")));
        cap.Children.Add(tCap);
        cap.Children.Add(barra);
        cap.Children.Add(tBancadas);
        StackPanel Valor(string rotulo, TextBlock t)
        {
            var p = new StackPanel { Spacing = 4 };
            p.Children.Add(ClubeModerno.Rotulo(rotulo));
            p.Children.Add(t);
            return p;
        }
        var quadro = ClubeModerno.Secao(desenho, 8);
        Control corpo;
        double largura, altura;
        if (Formato.Largo)
        {
            desenho.Width = 440; desenho.Height = 310;
            var lado = new StackPanel { Spacing = 12 };
            lado.Children.Add(ClubeModerno.Secao(cap, 16));
            lado.Children.Add(ClubeModerno.Secao(Valor(T("Nova arquibancada (5000 lugares)"), tPreco)));
            lado.Children.Add(ClubeModerno.Secao(Valor(T("Dinheiro em caixa"), tDinheiro)));
            lado.Children.Add(aviso);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,18,*") };
            quadro.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(quadro);
            Grid.SetColumn(lado, 2);
            g.Children.Add(lado);
            corpo = g;
            largura = 780; altura = 480;
        }
        else
        {
            desenho.Width = 312; desenho.Height = 220; tCap.FontSize = 23;
            quadro.HorizontalAlignment = HorizontalAlignment.Center;
            var p = new StackPanel { Spacing = 12 };
            var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,14,*") };
            topo.Children.Add(quadro);
            var ladoCap = new StackPanel { Spacing = 12 };
            ladoCap.Children.Add(cap);
            Grid.SetColumn(ladoCap, 2);
            ladoCap.VerticalAlignment = VerticalAlignment.Center;
            topo.Children.Add(ladoCap);
            p.Children.Add(topo);
            var linha = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
            var c1 = ClubeModerno.Secao(Valor(T("Nova arquibancada (5000 lugares)"), tPreco));
            var c2 = ClubeModerno.Secao(Valor(T("Dinheiro em caixa"), tDinheiro));
            Grid.SetColumn(c2, 2);
            linha.Children.Add(c1);
            linha.Children.Add(c2);
            p.Children.Add(linha);
            p.Children.Add(aviso);
            corpo = p;
            largura = 600; altura = 470;
        }

        var j = new JanelaModerna(T("Estádio"), TelaDinheiro.Texto(e.NomeCompleto), new EscudoClube(e, 44), corpo, largura, altura);
        BotaoModerno? construir = null;
        void Atualizar()
        {
            tCap.Text = T("{0} lugares", FormatoDinheiro.Numero(MercadoJogo.Capacidade(e)));
            tBancadas.Text = T("{0} de 24 arquibancadas", e.Estadio);
            barra.Content = ClubeModerno.Progresso(e.Estadio / 24.0, ClubeModerno.Verde);
            tPreco.Text = FormatoDinheiro.Valor(s.S, mc.PrecoBancada(e));
            tDinheiro.Text = FormatoDinheiro.Valor(s.S, (int)e.Dinheiro);
            tDinheiro.Foreground = (int)e.Dinheiro < 0 ? ClubeModerno.Vermelho : ClubeModerno.Texto;
            if (construir != null) construir.Habilitado = mc.PodeConstruir(e);
            desenho.InvalidateVisual();
        }
        // 7:30b1: o emprestimo por cima; na volta, o preco/Construir de novo
        if (registado)
            ToolTip.SetTip(j.Botao(T("Empréstimo"), false, () => TelaEmprestimo.Abrir(s, e, Atualizar, abrir, fechar)), T("Pedir empréstimo"));
        construir = j.Botao(T("Construir"), true, () =>
        {
            if (!mc.PodeConstruir(e)) return;
            if (s.Remoto is { } rmB) { rmB.Executar(new RetroFoot.Motor.Online.ConstruirBancada()); return; }
            mc.ConstruirBancada(e);
            aviso.IsVisible = true;
            Atualizar();
            TelaDinheiro.Avisar(e);
        });
        ToolTip.SetTip(construir, T("Construir mais 5000 lugares"));
        j.Botao(T("Fechar"), false, j.Fechar);
        Atualizar();
        j.AoEnter = j.Fechar;
        if (fechou != null) j.Fechou += fechou;
        j.Abrir(abrir ?? (c => s.AbrirDialogo(c)), fechar ?? (c => s.FecharDialogo(c)));
    }

    // PaintBoxStadiumPaint (7:2cee): pincel cinzento; os aneis de bancadas de fora para dentro
    // (3: as bancadas acima de 14, ate 10; 2: acima de 6, ate 8; 1: as primeiras 6) com a
    // funcao aninhada 7:2a7c; depois o relvado (anel 0, cheio) e as linhas. O TPaintBox nao
    // pinta o fundo (fica a cor da janela).
    //
    // 7:2a7c(anel L, maximo M, n): n = Min(M, n); a caixa tem W(L/6 + 0,4) por H(L/6 + 0,4),
    // centrada; caneta amarela-clara ($0080FFFF) de 2; com t = n 3,14 / M >= 3,14 e Ellipse,
    // senao Pie: um setor de 2t centrado no topo (do ponto em sen t a direita ao espelho a
    // esquerda, no sentido anti-horario).
    // (Moderno: as mesmas formas nas cores do tema escuro)
    internal sealed class Desenho(Func<int> bancadas, bool moderno = false) : Control
    {
        static readonly IBrush CinzaO = W95.Cor(128, 128, 128), VerdeO = W95.Cor(0, 128, 0);
        static readonly IBrush CinzaM = TemaVerde.Linha, VerdeM = TemaVerde.VerdeForte;
        static readonly Pen AmarelaO = new(W95.Cor(255, 255, 128), 2), AmarelaM = new(TemaVerde.Apagado, 2);
        static readonly Pen Branca2 = new(Brushes.White, 2), Branca3 = new(Brushes.White, 3);
        IBrush Cinza => moderno ? CinzaM : CinzaO;
        IBrush Verde => moderno ? VerdeM : VerdeO;
        Pen Amarela => moderno ? AmarelaM : AmarelaO;

        static int R(double v) => (int)Math.Round(v, MidpointRounding.ToEven);

        public override void Render(DrawingContext dc)
        {
            double W = Bounds.Width, H = Bounds.Height;
            int w = (int)W, h = (int)H, n = bancadas();
            using var _ = dc.PushClip(new Rect(Bounds.Size));
            void Anel(int l, int max, int k, IBrush pincel)
            {
                k = Math.Min(max, k);
                int mx = R(W * (l / 6.0 + 0.4)), my = R(H * (l / 6.0 + 0.4));
                int x1 = R((w - mx) / 2.0), y1 = R((h - my) / 2.0);
                int x2 = w - x1, y2 = h - y1;
                var c = new Point((x1 + x2) / 2.0, (y1 + y2) / 2.0);
                double a = (x2 - x1) / 2.0, b = (y2 - y1) / 2.0;
                double t = k * 3.14 / max;
                if (t >= 3.14) { dc.DrawEllipse(pincel, Amarela, c, a, b); return; }
                // as pontas no contorno, na direcao dos pontos de 7:2a7c
                var p1 = new Point(c.X + Math.Sin(t) * a, c.Y - Math.Cos(t) * b);
                var p2 = new Point(c.X - Math.Sin(t) * a, c.Y - Math.Cos(t) * b);
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(c, true);
                    ctx.LineTo(p1);
                    ctx.ArcTo(p2, new Size(a, b), 0, 2 * t > Math.PI, SweepDirection.CounterClockwise);
                    ctx.EndFigure(true);
                }
                dc.DrawGeometry(pincel, Amarela, g);
            }
            if (n > 14) Anel(3, 10, n - 14, Cinza);
            if (n > 6) Anel(2, 8, n - 6, Cinza);
            if (n > 0) Anel(1, 6, n, Cinza);
            Anel(0, 1, 1, Verde);
            // o campo: Rectangle (o contorno de x1 a x2-1), meio-campo, circulo e as areas
            void Ret(int x1, int y1, int x2, int y2, Pen p) =>
                dc.DrawRectangle(Verde, p, new Rect(x1 + 0.5, y1 + 0.5, x2 - x1 - 1, y2 - y1 - 1));
            Ret(R(W * 0.37), R(H * 0.37), R(W * 0.63), R(H * 0.63), Branca3);
            int d = R(W * 0.02);
            dc.DrawEllipse(Verde, Branca2, new Point(w / 2, h / 2), d, d);
            dc.DrawLine(Branca2, new Point(R(W * 0.5) + 0.5, R(H * 0.37)), new Point(R(W * 0.5) + 0.5, R(H * 0.63) - 2));
            Ret(R(W * 0.37), R(H * 0.45), R(W * 0.41), R(H * 0.55), Branca2);
            Ret(R(W * 0.59), R(H * 0.45), R(W * 0.63), R(H * 0.55), Branca2);
        }
    }
}
