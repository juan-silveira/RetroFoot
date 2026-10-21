using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using RetroFoot.Motor.Partida;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// A jornada no celular (Celular.cs), em pe: a barra com o titulo, o minuto e a pausa; as
// partidas numa coluna (as divisoes em secoes), que rola; o ultimo lance de cada jogo embaixo
// dele; e o Continuar no fim. Nas duas peles do kit: no Retro as caixas das equipas nas cores
// delas, como no classico; no Moderno, as linhas com os logos. O mesmo instantaneo (Fotografar)
// e o mesmo relogio da tela do computador: so o desenho muda. Tocar num jogo abre os detalhes.
public sealed partial class TelaJornada
{
    ListaCelular? _listaCel;
    TextBlock? _minutoCel, _tituloCel;
    CUi.Botao? _continuarCel, _pausaCel;
    Control? _relogioCel;

    // o redesenho (a cada minuto, no fim, na pausa) vale para a tela que estiver em uso
    void Redesenhar()
    {
        sup.InvalidateVisual();
        if (_listaCel == null) return;
        _listaCel.Medir();
        _listaCel.InvalidateVisual();
        Avalonia.Threading.Dispatcher.UIThread.Post(SeguirHumano, Avalonia.Threading.DispatcherPriority.Render);
        _relogioCel?.InvalidateVisual();
        if (_relogioCel != null) _relogioCel.IsVisible = !acabou;
        _tituloCel!.Text = CUi.Retro ? titulo + (pausado ? "  (" + T("pausa") + ")" : "")
            : titulo.Trim() + (pausado ? "  ·  " + T("Pausa") : "");
        _minutoCel!.Text = StatusRelogio;
        _continuarCel!.IsVisible = acabou;
        _pausaCel!.IsVisible = !acabou;
        _pausaCel.Texto = pausado ? "▶" : "❚❚";
    }

    ScrollViewer? _rolarCel;
    bool _jogadorRolou;

    // poe a divisao do humano no alto, ate o jogador tocar na lista ou rolar
    void SeguirHumano()
    {
        if (_rolarCel == null || _listaCel == null || _jogadorRolou) return;
        _rolarCel.Offset = new Vector(0, _listaCel.InicioHumano());
    }

    void MontarCelular()
    {
        _tituloCel = CUi.Txt("", 16, CUi.TextoBarra, FontWeight.Bold);
        _minutoCel = CUi.Txt("", 16, CUi.Retro ? W95.Amarelo : TemaVerde.Destaque, FontWeight.Bold);
        _pausaCel = new CUi.Botao("❚❚", false, () => Pausar(!pausado)) { Padding = new Thickness(14, 4), MinHeight = 40 };
        var cab = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,10,Auto") };
        cab.Children.Add(_tituloCel);
        Grid.SetColumn(_pausaCel, 3);
        cab.Children.Add(_pausaCel);
        Border barra;
        if (CUi.Retro)
        {
            // o Retro: a barra azul-marinho da jornada do computador (o titulo em branco) e o
            // relogio de pizza (TGauge) com os acrescimos a esquerda, no lugar do minuto escrito
            _tituloCel.Foreground = W95.Branco;
            _tituloCel.FontSize = 14 * Acessibilidade.Texto;
            _tituloCel.TextWrapping = TextWrapping.Wrap;
            _relogioCel = new RelogioCelular(this) { Width = 76, Height = 40 };
            Grid.SetColumn(_relogioCel, 1);
            cab.Children.Add(_relogioCel);
            barra = new Border { Background = W95.Marinho, Padding = new Thickness(10, 4, 8, 4), Child = cab, BorderBrush = W95.Face, BorderThickness = new Thickness(0, 0, 0, 1) };
        }
        else
        {
            Grid.SetColumn(_minutoCel, 1);
            cab.Children.Add(_minutoCel);
            barra = new Border { Background = CUi.Barra, Padding = new Thickness(14, 8), Child = cab };
        }
        _listaCel = new ListaCelular(this);
        // (no Retro, poucos jogos ficam no alto, como no computador)
        if (CUi.Retro) _listaCel.VerticalAlignment = VerticalAlignment.Top;
        var rolar = new ScrollViewer { Content = _listaCel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        // a tela abre na divisao do humano (uma vez; depois o jogador rola a vontade)
        // e a acompanha enquanto o jogador nao rola (os lances aumentam as divisoes de cima)
        _rolarCel = rolar;
        rolar.AddHandler(PointerPressedEvent, (_, _) => _jogadorRolou = true, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        rolar.AddHandler(PointerWheelChangedEvent, (_, _) => _jogadorRolou = true, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        rolar.AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(SeguirHumano, Avalonia.Threading.DispatcherPriority.Loaded);
        _continuarCel = new CUi.Botao(T("Continuar"), true, Seguir) { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 48, IsVisible = false };
        var rodape = new Border { Padding = new Thickness(12, 6), Child = _continuarCel };
        var d = new DockPanel { Background = CUi.Fundo };
        DockPanel.SetDock(barra, Dock.Top);
        d.Children.Add(barra);
        DockPanel.SetDock(rodape, Dock.Bottom);
        d.Children.Add(rodape);
        d.Children.Add(rolar);
        Content = d;
        Background = CUi.Fundo;
        Redesenhar();
    }

    // as partidas, desenhadas a mao (redesenha a cada minuto sem recriar controles)
    sealed class ListaCelular : Control
    {
        readonly TelaJornada t;
        const double ACab = 30, AJogo = 46, ALance = 18;

        public ListaCelular(TelaJornada t) { this.t = t; Medir(); }

        // a altura do conteudo (a ScrollViewer rola)
        public void Medir()
        {
            _cabHumano = null;   // (as linhas sao refeitas a cada minuto)
            if (CUi.Retro) { Height = AlturaRetro(); return; }
            double h = 8;
            foreach (var l in t.linhas)
            {
                if (l.Cabecalho != null) h += ACab;
                else if (!l.Vazia) h += AJogo + (ComLance(l) ? ALance : 0) + 4;
            }
            Height = h + 8;
        }

        internal static FormattedText TxtR(string s, double tam, IBrush cor, bool negrito = true) => Txt(s, tam, cor, negrito);

        static FormattedText Txt(string s, double tam, IBrush cor, bool negrito = false) =>
            new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(CUi.Fonte, FontStyle.Normal, negrito ? FontWeight.Bold : FontWeight.Normal), tam * Acessibilidade.Texto, cor);

        public override void Render(DrawingContext dc)
        {
            if (CUi.Retro) { RenderRetro(dc); return; }
            double w = Bounds.Width, x0 = 10, wl = w - 20, y = 8;
            string status = t.StatusRelogio;
            foreach (var l in t.linhas)
            {
                if (l.Cabecalho != null)
                {
                    // a divisao do humano: o titulo em amarelo numa pilula (abre nela)
                    bool hum = CabecalhoHumano(l);
                    var tc = Txt(TituloNoIdioma(l.Cabecalho).Trim().ToUpperInvariant(), 13, hum ? TemaVerde.SobreDestaque : CUi.TituloSecao, true);
                    var pt = new Point(x0 + 4, y + ACab - tc.Height - 4);
                    if (hum) dc.DrawRectangle(TemaVerde.BotaoPrimario, null, new Rect(pt.X - 8, pt.Y - 3, tc.Width + 16, tc.Height + 6), 10, 10);
                    dc.DrawText(tc, pt);
                    y += ACab;
                    continue;
                }
                if (l.Vazia) continue;
                double hj = AJogo + (ComLance(l) ? ALance : 0);
                var r = new Rect(x0, y, wl, hj);
                // o cartao do jogo
                if (CUi.Retro) { dc.FillRectangle(W95.Face, r); dc.DrawRectangle(new Pen(W95.Preto, 1), r.Deflate(0.5)); }
                else dc.DrawRectangle(TemaVerde.CartaoHub, new Pen(TemaVerde.Borda, 1), r, 12, 12);
                double meio = r.X + r.Width / 2, yc = r.Y + AJogo / 2;
                // o placar e o minuto do jogo
                var pl = Txt($"{l.GC} - {l.GF}", 18, CUi.Texto, true);
                dc.DrawText(pl, new Point(meio - pl.Width / 2, yc - pl.Height / 2 + 4));
                var st = Txt(t.StatusDaLinha(l, status), 10, CUi.Retro ? W95.Marinho : TemaVerde.Destaque, true);
                dc.DrawText(st, new Point(meio - st.Width / 2, r.Y + 3));
                // as equipas, nas cores delas (o Retro) ou com o logo (o Moderno)
                double larg = (r.Width - 70) / 2 - 8;
                Equipa(dc, l.Casa, l.LetraCasa, l.FundoCasa, l.LogoCasa, new Rect(r.X + 6, yc - 15, larg, 30), true, Humano(l, Lado.Casa));
                Equipa(dc, l.Fora, l.LetraFora, l.FundoFora, l.LogoFora, new Rect(meio + 35, yc - 15, larg, 30), false, Humano(l, Lado.Visit));
                // o ultimo lance
                if (ComLance(l))
                {
                    double yl = r.Y + AJogo - 2;
                    Icones.Desenhar(dc, l.LanceTipo, l.SegundoAmarelo, new Rect(r.X + 10, yl + 2, 12, 12));
                    string txt = MinutoTexto.Texto(l.LanceMin, l.LanceAcr) + " " + l.LanceNome.Trim();
                    if (l.LanceTipo == TipoLance.Penalti) txt += " (" + T("pen.") + ")";
                    else if (l.LanceTipo == TipoLance.GolAnulado) txt += " - " + T("Anulado");
                    var tl = Txt(txt, 12, CUi.Fraco);
                    using (dc.PushClip(new Rect(r.X + 26, yl, r.Width - 32, ALance)))
                        dc.DrawText(tl, new Point(r.X + 28, yl + (ALance - tl.Height) / 2));
                }
                y += hj + 4;
            }
        }

        // ------------------------------------------------ o Retro: como a classificacao
        // No verde, cada divisao com o nome em amarelo e os jogos numa linha so: a caixa do
        // mandante nas cores dele, o placar em amarelo e a caixa do visitante; compacto, para
        // caberem duas divisoes por tela. A divisao do humano fica numa moldura amarela (e a
        // tela abre nela); nos jogos do humano, o ultimo lance embaixo. Tocar abre os detalhes.
        const double RCab = 30, RJogo = 25, RLance = 19, RTopo = 6, RFim = 8;
        static readonly IBrush RAmarelo = W95.Amarelo, RVerde = W95.Verde;

        // os blocos (o cabecalho e os jogos dele) e se o humano joga ali
        List<(Linha? Cab, List<Linha> Jogos, bool Humano)> Blocos()
        {
            var r = new List<(Linha?, List<Linha>, bool)>();
            Linha? cab = null;
            var jogos = new List<Linha>();
            void Fechar() { if (cab != null || jogos.Count > 0) r.Add((cab, jogos, jogos.Exists(x => x.Humanos > 0))); }
            foreach (var l in t.linhas)
            {
                if (l.Cabecalho != null) { Fechar(); cab = l; jogos = new List<Linha>(); }
                else if (!l.Vazia) jogos.Add(l);
            }
            Fechar();
            return r;
        }

        static double AlturaJogo(Linha l) => RJogo + (l.Humanos > 0 && l.LanceTipo != 0 ? RLance : 0) + 2;

        double AlturaRetro()
        {
            double h = RTopo;
            foreach (var (cab, jogos, _) in Blocos())
            {
                if (cab != null) h += RCab;
                foreach (var l in jogos) h += AlturaJogo(l);
                h += RFim;
            }
            return h + 10;
        }

        // onde comeca a divisao do humano (a tela abre nela)
        public double InicioHumano()
        {
            if (!CUi.Retro) return InicioHumanoModerno();
            double y = RTopo;
            foreach (var (cab, jogos, humano) in Blocos())
            {
                if (humano) return Math.Max(0, y - 4);
                if (cab != null) y += RCab;
                foreach (var l in jogos) y += AlturaJogo(l);
                y += RFim;
            }
            return 0;
        }

        // o Moderno: o cabecalho da divisao em que o humano joga (a 1a com ele)
        double InicioHumanoModerno()
        {
            double y = 8;
            foreach (var (cab, jogos, humano) in Blocos())
            {
                if (humano) return Math.Max(0, y - 4);
                if (cab != null) y += ACab;
                foreach (var l in jogos) y += AJogo + (ComLance(l) ? ALance : 0) + 4;
            }
            return 0;
        }

        // a divisao do humano (o cabecalho fica destacado no Moderno)
        HashSet<Linha>? _cabHumano;
        bool CabecalhoHumano(Linha l)
        {
            if (_cabHumano == null)
            {
                _cabHumano = new HashSet<Linha>();
                foreach (var (cab, _, humano) in Blocos()) if (humano && cab != null) _cabHumano.Add(cab);
            }
            return _cabHumano.Contains(l);
        }

        // o jogo na altura y (o toque)
        Linha? JogoRetro(double py)
        {
            double y = RTopo;
            foreach (var (cab, jogos, _) in Blocos())
            {
                if (cab != null) y += RCab;
                foreach (var l in jogos)
                {
                    double h = AlturaJogo(l);
                    if (py >= y && py < y + h) return l;
                    y += h;
                }
                y += RFim;
            }
            return null;
        }

        void RenderRetro(DrawingContext dc)
        {
            double w = Bounds.Width, x0 = 8, xd = w - 8, y = RTopo;
            double larg = xd - x0, ws = 46, bw = (larg - ws) / 2;
            foreach (var (cab, jogos, humano) in Blocos())
            {
                double ini = y;
                if (cab != null)
                {
                    var tc = Txt(TituloNoIdioma(cab.Cabecalho!).Trim(), 17, humano ? W95.Preto : RAmarelo, true);
                    double xt = x0 + 4;
                    if (humano) dc.FillRectangle(RAmarelo, new Rect(x0 - 4, y + 2, larg + 8, RCab - 4));
                    if (cab.LogoCabecalho != null)
                    {
                        TelaJornada.Imagem(dc, cab.LogoCabecalho, new Rect(xt, y + 5, RCab - 10, RCab - 10));
                        xt += RCab - 4;
                    }
                    dc.DrawText(tc, new Point(xt, y + (RCab - tc.Height) / 2));
                    y += RCab;
                }
                foreach (var l in jogos)
                {
                    bool hc = Humano(l, Lado.Casa), hf = Humano(l, Lado.Visit);
                    Caixa(dc, new Rect(x0, y, bw, RJogo - 1), l.Casa, l.LetraCasa, l.FundoCasa, hc);
                    var pl = Txt(l.GC + " - " + l.GF, 15, RAmarelo, true);
                    dc.DrawText(pl, new Point(x0 + bw + (ws - pl.Width) / 2, y + (RJogo - 1 - pl.Height) / 2));
                    Caixa(dc, new Rect(xd - bw, y, bw, RJogo - 1), l.Fora, l.LetraFora, l.FundoFora, hf);
                    double h = AlturaJogo(l);
                    if (l.Humanos > 0 && l.LanceTipo != 0)
                        LanceRetro(dc, l, new Rect(x0 + 4, y + RJogo, larg - 8, RLance));
                    y += h;
                }
                // a moldura amarela da divisao do humano
                if (humano) dc.DrawRectangle(new Pen(RAmarelo, 2), new Rect(x0 - 4, ini + 2, larg + 8, y - ini));
                y += RFim;
            }
        }

        // a caixa da equipa nas cores dela (no verde da tela, o contorno na cor da letra)
        static void Caixa(DrawingContext dc, Rect r, string nome, Color letra, Color fundo, bool humano)
        {
            var bl = new SolidColorBrush(letra);
            dc.FillRectangle(new SolidColorBrush(fundo), r);
            if (fundo == Color.FromRgb(0, 128, 0)) dc.DrawRectangle(new Pen(bl, 1), r.Deflate(0.5));
            var tn = Txt(nome, 13.5, bl, true);
            if (humano) dc.FillRectangle(W95.Amarelo, new Rect(r.X, r.Bottom - 3, r.Width, 3));
            using (dc.PushClip(r.Deflate(2)))
                dc.DrawText(tn, new Point(r.X + 5, r.Center.Y - tn.Height / 2));
        }

        // o ultimo lance (seg03:c25e): "Nome 58'"; cartoes e lesao com o desenho antes
        static void LanceRetro(DrawingContext dc, Linha l, Rect r)
        {
            if (l.LanceTipo == 0) return;
            string txt = l.LanceNome.Trim() + " " + MinutoTexto.Texto(l.LanceMin, l.LanceAcr);
            if (l.LanceTipo == TipoLance.Penalti) txt += " (" + T("pen.") + ")";
            else if (l.LanceTipo == TipoLance.GolAnulado) txt += " - " + T("Anulado");
            double x = r.X, q = 13, yq = r.Y + (r.Height - q) / 2;
            if (l.LanceTipo is TipoLance.Amarelo or TipoLance.Expulsao or TipoLance.Lesao)
            {
                switch (l.LanceTipo)
                {
                    case TipoLance.Amarelo: dc.FillRectangle(RAmarelo, new Rect(x + 2, yq, q, q)); break;
                    case TipoLance.Expulsao:
                        if (l.SegundoAmarelo) dc.FillRectangle(RAmarelo, new Rect(x, yq - 2, q, q));
                        dc.FillRectangle(W95.Vermelho, new Rect(x + 2, yq, q, q));
                        break;
                    default:
                        dc.FillRectangle(W95.Branco, new Rect(x + 2, yq, q, q));
                        dc.FillRectangle(W95.Vermelho, new Rect(x + 3, yq + q / 2 - 1, q - 2, 2));
                        dc.FillRectangle(W95.Vermelho, new Rect(x + 2 + q / 2 - 1, yq + 1, 2, q - 2));
                        break;
                }
                x += q + 7;
            }
            var tl = Txt(txt, 13.5, RAmarelo);
            using (dc.PushClip(new Rect(x, r.Y, Math.Max(0, r.Right - x), r.Height)))
                dc.DrawText(tl, new Point(x, r.Y + (r.Height - tl.Height) / 2));
        }

        // o ultimo lance embaixo do jogo so nos jogos do humano, como no Retro: em todos, os
        // cartoes cresciam e a lista mexia a cada lance (os outros, no toque: os detalhes)
        static bool ComLance(Linha l) => l.LanceTipo != 0 && l.Humanos > 0;

        static bool Humano(Linha l, int lado) => l.Jogo?.Treinador[lado] is { Humano: not 0 };

        void Equipa(DrawingContext dc, string nome, Color letra, Color fundo, Avalonia.Media.Imaging.Bitmap? logo, Rect r, bool casa, bool humano)
        {
            var fl = new SolidColorBrush(letra);
            if (CUi.Retro)
            {
                // a caixa nas cores da equipa, como na jornada do classico
                dc.FillRectangle(new SolidColorBrush(fundo), r);
                var tn = Txt(nome, 14, fl, humano);
                using (dc.PushClip(r.Deflate(2)))
                    dc.DrawText(tn, new Point(casa ? r.Right - 6 - tn.Width : r.X + 6, r.Center.Y - tn.Height / 2));
                return;
            }
            double q = 24, xl = casa ? r.Right - q : r.X;
            if (logo != null) TelaJornada.Imagem(dc, logo, new Rect(xl, r.Center.Y - q / 2, q, q));
            else dc.DrawEllipse(new SolidColorBrush(fundo), new Pen(fl, 2), new Point(xl + q / 2, r.Center.Y), q / 2 - 1, q / 2 - 1);
            var t2 = Txt(nome, 14, CUi.Texto, humano);
            double area = r.Width - q - 8;
            double xt = casa ? Math.Max(r.X, r.Right - q - 8 - t2.Width) : r.X + q + 8;
            using (dc.PushClip(new Rect(casa ? r.X : r.X + q + 8, r.Y, area, r.Height)))
                dc.DrawText(t2, new Point(xt, r.Center.Y - t2.Height / 2));
        }

        // tocar num jogo: os detalhes dele (do lado da metade tocada); no fim, continuar
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (t.acabou) { t.Seguir(); return; }
            var p = e.GetPosition(this);
            if (CUi.Retro)
            {
                if (JogoRetro(p.Y) is { Jogo: { } jr }) LigacaoPartida.PedirDetalhes(t.s, jr, p.X < Bounds.Width / 2 ? Lado.Casa : Lado.Visit);
                return;
            }
            double y = 8;
            foreach (var l in t.linhas)
            {
                if (l.Cabecalho != null) { y += ACab; continue; }
                if (l.Vazia) continue;
                double hj = AJogo + (ComLance(l) ? ALance : 0);
                if (p.Y >= y && p.Y < y + hj && l.Jogo is { } jg)
                {
                    LigacaoPartida.PedirDetalhes(t.s, jg, p.X < Bounds.Width / 2 ? Lado.Casa : Lado.Visit);
                    return;
                }
                y += hj + 4;
            }
        }
    }

    // o relogio do Retro (o TGauge gkPie do computador, DesenharRelogio): a elipse branca com a
    // fatia na cor do tempo; nos acrescimos, "+n'" em amarelo a esquerda
    sealed class RelogioCelular : Control
    {
        readonly TelaJornada t;
        public RelogioCelular(TelaJornada t) { this.t = t; IsHitTestVisible = false; }

        public override void Render(DrawingContext dc)
        {
            if (t.acabou) return;
            double d = Math.Min(Bounds.Height, 40) - 2;
            var r = new Rect(Bounds.Width - d - 1, (Bounds.Height - d) / 2, d, d);
            var frente = new SolidColorBrush(t.corRelogio);
            var c = r.Center;
            double rx = r.Width / 2 - 0.5, ry = r.Height / 2 - 0.5;
            dc.DrawEllipse(W95.Branco, new Pen(frente, 1), c, rx, ry);
            if (t.acrescimo > 0)
            {
                var ta = ListaCelular.TxtR("+" + t.acrescimo + "'", 15, W95.Amarelo);
                dc.DrawText(ta, new Point(r.X - 5 - ta.Width, c.Y - ta.Height / 2));
            }
            int prog = Math.Clamp(t.relProg, t.relMin, t.relMax);
            double frac = t.relMax > t.relMin ? (double)(prog - t.relMin) / (t.relMax - t.relMin) : 0;
            if (frac <= 0) return;
            if (frac >= 1) { dc.DrawEllipse(frente, null, c, rx, ry); return; }
            double ang = frac * 2 * Math.PI;
            var fim = new Point(c.X + rx * Math.Sin(ang), c.Y - ry * Math.Cos(ang));
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(c, true);
                ctx.LineTo(new Point(c.X, c.Y - ry));
                ctx.ArcTo(fim, new Size(rx, ry), 0, frac > 0.5, SweepDirection.Clockwise);
                ctx.EndFigure(true);
            }
            dc.DrawGeometry(frente, null, g);
        }
    }
}
