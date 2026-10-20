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

    // o redesenho (a cada minuto, no fim, na pausa) vale para a tela que estiver em uso
    void Redesenhar()
    {
        sup.InvalidateVisual();
        if (_listaCel == null) return;
        _listaCel.Medir();
        _listaCel.InvalidateVisual();
        _tituloCel!.Text = titulo.Trim() + (pausado ? "  ·  " + T("Pausa") : "");
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
        Grid.SetColumn(_minutoCel, 1);
        cab.Children.Add(_minutoCel);
        Grid.SetColumn(_pausaCel, 3);
        cab.Children.Add(_pausaCel);
        var barra = new Border { Background = CUi.Barra, Padding = new Thickness(14, 8), Child = cab };
        _listaCel = new ListaCelular(this);
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
            double h = 8;
            foreach (var l in t.linhas)
            {
                if (l.Cabecalho != null) h += ACab;
                else if (!l.Vazia) h += AJogo + (l.LanceTipo != 0 ? ALance : 0) + 4;
            }
            Height = h + 8;
        }

        static FormattedText Txt(string s, double tam, IBrush cor, bool negrito = false) =>
            new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(CUi.Fonte, FontStyle.Normal, negrito ? FontWeight.Bold : FontWeight.Normal), tam * Acessibilidade.Texto, cor);

        public override void Render(DrawingContext dc)
        {
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
}
