using System;
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
        _relogioCel?.InvalidateVisual();
        if (_relogioCel != null) _relogioCel.IsVisible = !acabou;
        _tituloCel!.Text = CUi.Retro ? titulo + (pausado ? "  (" + T("pausa") + ")" : "")
            : titulo.Trim() + (pausado ? "  ·  " + T("Pausa") : "");
        _minutoCel!.Text = StatusRelogio;
        _continuarCel!.IsVisible = acabou;
        _pausaCel!.IsVisible = !acabou;
        _pausaCel.Texto = pausado ? "▶" : "❚❚";
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
            if (CUi.Retro) { Height = AlturaRetro(); return; }
            double h = 8;
            foreach (var l in t.linhas)
            {
                if (l.Cabecalho != null) h += ACab;
                else if (!l.Vazia) h += AJogo + (l.LanceTipo != 0 ? ALance : 0) + 4;
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
                    var tc = Txt(TituloNoIdioma(l.Cabecalho).Trim().ToUpperInvariant(), 13, CUi.TituloSecao, true);
                    dc.DrawText(tc, new Point(x0 + 4, y + ACab - tc.Height - 4));
                    y += ACab;
                    continue;
                }
                if (l.Vazia) continue;
                double hj = AJogo + (l.LanceTipo != 0 ? ALance : 0);
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
                if (l.LanceTipo != 0)
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

        // ------------------------------------------------ o Retro: a jornada do computador
        // Cada jogo em duas linhas sobre o verde, dentro da moldura azul-marinho: as caixas das
        // equipas nas cores delas com os golos em amarelo no meio; embaixo o publico e o ultimo
        // lance em amarelo ("Nome 58'", com o cartao ou a cruz da lesao antes). As divisoes
        // atravessam a moldura com o nome em amarelo, como na tela do computador.
        const double RCab = 32, RJogo = 30, RLance = 20, RPasso = RJogo + RLance + 8, RTopo = 12;
        static readonly IBrush RAmarelo = W95.Amarelo, RMarinho = W95.Marinho, RVerde = W95.Verde;

        double AlturaRetro()
        {
            double h = RTopo;
            foreach (var l in t.linhas)
                if (l.Cabecalho != null) h += RCab;
                else if (!l.Vazia) h += RPasso;
            return h + 14;
        }

        // o jogo na altura y (o toque)
        Linha? JogoRetro(double py)
        {
            double y = RTopo;
            foreach (var l in t.linhas)
            {
                if (l.Cabecalho != null) { y += RCab; continue; }
                if (l.Vazia) continue;
                if (py >= y && py < y + RPasso) return l;
                y += RPasso;
            }
            return null;
        }

        void RenderRetro(DrawingContext dc)
        {
            double w = Bounds.Width, xm = 6, xd = w - 7, y = RTopo;
            double x0 = xm + 6, wl = xd - 6 - x0;
            double meio = x0 + wl / 2, bw = (wl - 52) / 2;
            double topoMoldura = -1, primeira = -1;
            foreach (var l in t.linhas)
            {
                if (l.Cabecalho != null)
                {
                    // a linha da moldura atravessa e o nome vem por cima, no fundo verde
                    double ym = y + RCab / 2;
                    if (topoMoldura < 0) topoMoldura = ym;
                    dc.FillRectangle(RMarinho, new Rect(xm, ym, xd - xm + 1, 1));
                    var tc = Txt(TituloNoIdioma(l.Cabecalho), 14, RAmarelo, true);
                    double xt = x0 + 24;
                    if (l.LogoCabecalho != null)
                    {
                        dc.FillRectangle(RVerde, new Rect(xt - 2, y + 4, RCab - 4, RCab - 8));
                        TelaJornada.Imagem(dc, l.LogoCabecalho, new Rect(xt, y + 5, RCab - 10, RCab - 10));
                        xt += RCab - 4;
                    }
                    dc.FillRectangle(RVerde, new Rect(xt - 3, ym - tc.Height / 2, tc.Width + 6, tc.Height));
                    dc.DrawText(tc, new Point(xt, ym - tc.Height / 2));
                    y += RCab;
                    continue;
                }
                if (l.Vazia) continue;
                // (uma coluna que comeca por um jogo, a Taca: a linha de cima da moldura)
                if (topoMoldura < 0) { topoMoldura = y - 6; primeira = topoMoldura; }
                Caixa(dc, new Rect(x0, y, bw, RJogo - 2), l.Casa, l.LetraCasa, l.FundoCasa, Humano(l, Lado.Casa));
                var gc = Txt(l.GC.ToString(), 16, RAmarelo, true);
                var gf = Txt(l.GF.ToString(), 16, RAmarelo, true);
                dc.DrawText(gc, new Point(meio - 13 - gc.Width / 2, y + (RJogo - 2 - gc.Height) / 2));
                dc.DrawText(gf, new Point(meio + 13 - gf.Width / 2, y + (RJogo - 2 - gf.Height) / 2));
                Caixa(dc, new Rect(xd - 6 - bw, y, bw, RJogo - 2), l.Fora, l.LetraFora, l.FundoFora, Humano(l, Lado.Visit));
                // o publico (sob a caixa da casa) e o ultimo lance (a partir do meio)
                double yl = y + RJogo;
                var pub = Txt(FormatoDinheiro.Numero(l.Publico), 13, RAmarelo);
                dc.DrawText(pub, new Point(x0 + 2, yl + (RLance - pub.Height) / 2));
                LanceRetro(dc, l, new Rect(meio - 40, yl, xd - 6 - (meio - 40), RLance));
                y += RPasso;
            }
            if (topoMoldura < 0) return;
            double baixo = y + 2;
            if (primeira >= 0) dc.FillRectangle(RMarinho, new Rect(xm, primeira, xd - xm + 1, 1));
            dc.FillRectangle(RMarinho, new Rect(xm, topoMoldura, 1, baixo - topoMoldura));
            dc.FillRectangle(RMarinho, new Rect(xd, topoMoldura, 1, baixo - topoMoldura));
            dc.FillRectangle(RMarinho, new Rect(xm, baixo, xd - xm + 1, 1));
        }

        // a caixa da equipa nas cores dela (no verde da tela, o contorno na cor da letra)
        static void Caixa(DrawingContext dc, Rect r, string nome, Color letra, Color fundo, bool humano)
        {
            var bl = new SolidColorBrush(letra);
            dc.FillRectangle(new SolidColorBrush(fundo), r);
            if (fundo == Color.FromRgb(0, 128, 0)) dc.DrawRectangle(new Pen(bl, 1), r.Deflate(0.5));
            var tn = Txt(nome, 14.5, bl, humano);
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
                double hj = AJogo + (l.LanceTipo != 0 ? ALance : 0);
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
