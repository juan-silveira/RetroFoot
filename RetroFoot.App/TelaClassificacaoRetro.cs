using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Rodada.Campos;
using static RetroFoot.App.Idioma;
using TRegras = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.App;

// A Classificacao no visual Retro como a do jogo classico com o launcher: a janela do Windows
// 95 (moldura prateada, barra azul com "Classificacao - Na rodada" e o X) sobre o verde, com a
// tabela nos verdes do jogo dentro:
//  - "Na Divisao" e as abas 1a a 4a no topo; colunas posicao, a bandeira nas cores da equipa
//    (fundo, letra, fundo), clube, Pts, PJ, VIT, E, DER, GM, GC, SG e as ultimas 5 (bolinhas);
//  - a barra a esquerda marca a zona: campeao (1o da 1a), sobe (os K primeiros da 2a a 4a) e
//    desce (os K ultimos), K = Regras.SobemDescem; a equipa do humano em azul; legenda embaixo;
//  - 4:3: uma divisao por vez (800x600 ampliado); 16:9: duas lado a lado (1a e 2a, 3a e 4a)
//    em 1067x600, as abas escolhem o par;
//  - abre na divisao com mais treinadores humanos; depois da jornada fecha sozinha em 10 s
//    (Timer1Timer, seg06:0a50; mudar de divisao recomeca); o X, o verde, Enter e Esc fecham;
//    clicar numa equipa abre o plantel dela com o relogio parado; as setas mudam a divisao.
sealed class TelaClassificacaoRetro : Control
{
    static readonly FontFamily Arial = new("avares://RetroFoot.App/Assets/Fontes#Liberation Sans");
    static readonly FontFamily Sans = new("avares://RetroFoot.App/Assets/Fontes#MS Sans Serif");
    static IBrush H(string s) => new SolidColorBrush(Color.Parse(s));
    static readonly IBrush Verde = H("#008000"), Prata = H("#C0C0C0"), Cinza = H("#808080"), Titulo = H("#3296FA");
    // os tons de verde do jogo: o cliente verde (clGreen) com a zebra mais escura, o titulo, o
    // cabecalho e as posicoes em amarelo como os rotulos do original; o humano em verde-escuro
    static readonly IBrush Fundo = H("#008000"), Zebra = H("#007000"), Humano = H("#004A00"), Fio = H("#006000");
    static readonly IBrush Texto = Brushes.White, Fraco = H("#FFFF00"), Legenda_ = H("#E8F5E0"), AbaSel = H("#FFFF00");
    static readonly IBrush Campeao = H("#FFD700"), Sobe = H("#4FA8FF"), Desce = H("#FF4040");
    static readonly IBrush Vitoria = H("#3CE63C"), Empate = H("#C0C0C0"), Derrota = H("#FF4040"), NaoJogou = H("#005A00");
    static readonly IPen Contorno = new Pen(H("#003A00"), 1);

    const double Margem = 10;

    public event Action? Fechou;

    readonly Jogo98 _s;
    readonly TRegras _regras;
    readonly Dictionary<ushort, Equipa> _equipas = new();
    readonly Dictionary<ushort, Treinador> _treinadores = new();
    readonly double L, A;
    readonly bool _largo;                 // 16:9: duas divisoes lado a lado
    readonly int _divisoes;
    readonly Rect _janela, _cliente;
    readonly string _titulo;
    int _divisao;                         // a da esquerda no 16:9 (par)
    int _tiques;
    DispatcherTimer? _relogio;
    bool _fechou;
    readonly List<(Rect R, Equipa E)> _linhas = new();
    readonly List<(Rect R, int D)> _abas = new();

    public TelaClassificacaoRetro(Jogo98 s, int rodada, bool relogio, TRegras regras)
    {
        _s = s;
        _regras = regras;
        foreach (var e in s.Equipas) _equipas[e.ID] = e;
        foreach (var t in s.Treinadores) _treinadores[t.ID] = t;
        _divisoes = Math.Clamp(s.Divisoes.Count, 1, 4);
        _largo = Formato.Largo && _divisoes > 1;
        L = _largo ? 1067 : 800; A = 600;
        Width = L; Height = A;
        ClipToBounds = true;
        Focusable = true;
        _janela = new Rect(Margem, Margem, L - 2 * Margem, A - 2 * Margem);
        _cliente = new Rect(_janela.X + 3, _janela.Y + 3 + 19, _janela.Width - 6, _janela.Height - 6 - 19);
        _titulo = rodada > 0 ? T("Classificação - {0}ª rodada", rodada) : T("Classificação");
        Mostrar(DivisaoDosHumanos());

        if (relogio)
        {
            _relogio = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _relogio.Tick += (_, _) => { if (++_tiques == 10) Fechar(); };
            _relogio.Start();
        }
        DetachedFromVisualTree += (_, _) => _relogio?.Stop();
        AttachedToVisualTree += (_, _) => Focus();
    }

    // ---------------------------------------------------------------- dados
    bool Humana(Equipa e) => _treinadores.TryGetValue(e.Treinador, out var t) && t.Humano != 0;

    int DivisaoDosHumanos()
    {
        int melhor = 0, max = 0;
        for (int d = 0; d < _divisoes; d++)
        {
            int n = Equipas(d).FindAll(Humana).Count;
            if (n > max) { max = n; melhor = d; }
        }
        return melhor;
    }

    List<Equipa> Equipas(int d)
    {
        var l = new List<Equipa>();
        if (d < _s.Divisoes.Count)
            foreach (var id in _s.Divisoes[d].Equipas)
                if (_equipas.TryGetValue(id, out var e)) l.Add(e);
        return l;
    }

    IBrush? Zona(int d, int i, int n)
    {
        int k = _regras.SobemDescem;
        if (d == 0 && i == 0) return Campeao;
        if (d > 0 && i < k) return Sobe;
        if (i >= n - k) return Desce;
        return null;
    }

    // as ultimas 5 do campeonato (do calendario), da mais antiga a mais recente: 1, 0, -1
    static List<int> Ultimas(Equipa e)
    {
        var r = new List<int>();
        foreach (var c in e.Calendario)
        {
            if (c.B07 != 0 || (short)c.W0A < 0 || (short)c.W0C < 0) continue;
            short gp = (short)c.W0A, gc = (short)c.W0C;
            r.Add(gp > gc ? 1 : gp == gc ? 0 : -1);
        }
        return r.Count > 5 ? r.GetRange(r.Count - 5, 5) : r;
    }

    static string Nome(byte[] cifrado) =>
        cifrado.Length == 0 ? "" : System.Text.Encoding.Latin1.GetString(Cifra.TextoEFT(cifrado).Texto);

    static IBrush Cor(byte[] c, IBrush padrao) =>
        c.Length < 4 || c[3] != 0 ? padrao : new SolidColorBrush(Color.FromRgb(c[0], c[1], c[2]));

    void Mostrar(int d)
    {
        d = Math.Clamp(d, 0, _divisoes - 1);
        _divisao = _largo ? Math.Min(d / 2 * 2, Math.Max(_divisoes - 2, 0)) : d;
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- desenho
    static FormattedText Txt(string s, double tam, IBrush cor, bool negrito = false, FontFamily? fonte = null) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(fonte ?? Arial, FontStyle.Normal, negrito ? FontWeight.Bold : FontWeight.Normal), tam * Acessibilidade.Texto, cor);

    static void Escreve(DrawingContext dc, string s, double x, double yMeio, double tam, IBrush cor, bool negrito = false, FontFamily? fonte = null)
    {
        var t = Txt(s, tam, cor, negrito, fonte);
        dc.DrawText(t, new Point(x, yMeio - t.Height / 2));
    }

    static void Centro(DrawingContext dc, string s, double x, double yMeio, double tam, IBrush cor, bool negrito = false)
    {
        var t = Txt(s, tam, cor, negrito);
        dc.DrawText(t, new Point(x - t.Width / 2, yMeio - t.Height / 2));
    }

    static void Borda(DrawingContext dc, Rect r, bool afundada)
    {
        var claro = new Pen(afundada ? Cinza : Brushes.White, 1);
        var sombra = new Pen(afundada ? Brushes.White : Cinza, 1);
        dc.DrawLine(claro, r.TopLeft, r.TopRight);
        dc.DrawLine(claro, r.TopLeft, r.BottomLeft);
        dc.DrawLine(sombra, r.BottomLeft, r.BottomRight);
        dc.DrawLine(sombra, r.TopRight, r.BottomRight);
    }

    Rect BotaoFechar() => new(_janela.Right - 3 - 2 - 16, _janela.Y + 3 + 2, 16, 14);

    public override void Render(DrawingContext dc)
    {
        dc.FillRectangle(Verde, new Rect(0, 0, L, A));
        // a janela (bsDialog): moldura, barra do titulo com o X
        dc.FillRectangle(Prata, _janela);
        Borda(dc, _janela.Deflate(0.5), false);
        var barra = new Rect(_janela.X + 3, _janela.Y + 3, _janela.Width - 6, 18);
        dc.FillRectangle(Titulo, barra);
        // (o "fi" da MS Sans Serif vira uma ligadura larga: separado por um ZWNJ)
        Escreve(dc, _titulo.Replace("fi", "f\u200Ci"), barra.X + 4, barra.Y + 9, 11, Brushes.Black, true, Sans);
        var x = BotaoFechar();
        dc.FillRectangle(Prata, x);
        Borda(dc, x.Deflate(0.5), false);
        Centro(dc, "×", x.Center.X, x.Center.Y - 1, 13, Brushes.Black, true);
        dc.FillRectangle(Fundo, _cliente);

        _linhas.Clear();
        _abas.Clear();
        using (dc.PushClip(_cliente))
        {
            var c = _cliente.Deflate(new Thickness(12, 8, 12, 6));
            // as abas no topo, a direita
            double ax = c.Right;
            for (int d = _divisoes - 1; d >= 0; d--)
            {
                var r = new Rect(ax - 36, c.Y + 2, 36, 22);
                ax -= 37;
                bool sel = d == _divisao || (_largo && d == _divisao + 1);
                // botoes do Windows 95: o escolhido afundado e amarelo
                dc.FillRectangle(sel ? AbaSel : Prata, r);
                Borda(dc, r.Deflate(0.5), sel);
                Centro(dc, T("{0}ª", d + 1), r.Center.X, r.Center.Y, 13, Brushes.Black, true);
                _abas.Add((r, d));
            }
            // a legenda embaixo (duas colunas: as zonas e os resultados)
            const double hLeg = 46;
            var leg = new Rect(c.X, c.Bottom - hLeg, c.Width, hLeg);
            Legenda(dc, leg);
            var area = new Rect(c.X, c.Y, c.Width, c.Height - hLeg - 4);
            if (_largo)
            {
                double w = (area.Width - 20) / 2;
                Tabela(dc, new Rect(area.X, area.Y, w, area.Height), _divisao, true);
                if (_divisao + 1 < _divisoes) Tabela(dc, new Rect(area.X + w + 20, area.Y, w, area.Height), _divisao + 1, false);
            }
            else Tabela(dc, area, _divisao, true);
        }
    }

    void Legenda(DrawingContext dc, Rect r)
    {
        double y = r.Y + 7, passo = 14;
        (IBrush, string)[] zonas = { (Campeao, T("Campeão")), (Sobe, T("Sobe de divisão")), (Desce, T("Desce de divisão")) };
        (IBrush, string)[] res = { (Vitoria, T("Vitória")), (Empate, T("Empate")), (Derrota, T("Derrota")) };
        double x2 = r.X + r.Width / 2;
        for (int i = 0; i < 3; i++)
        {
            double yy = y + i * passo;
            dc.FillRectangle(zonas[i].Item1, new Rect(r.X, yy - 5, 4, 10));
            Escreve(dc, zonas[i].Item2, r.X + 10, yy, 10.5, Legenda_);
            dc.DrawEllipse(res[i].Item1, Contorno, new Point(x2 + 5, yy), 5, 5);
            Escreve(dc, res[i].Item2, x2 + 16, yy, 10.5, Legenda_);
        }
    }

    // uma divisao na area r: o titulo (com espaco para as abas a direita na primeira), o
    // cabecalho das colunas e as linhas, que enchem a altura (ate 46 pontos)
    void Tabela(DrawingContext dc, Rect r, int d, bool primeira)
    {
        Escreve(dc, T("{0}ª Divisão", d + 1), r.X + 2, r.Y + 13, 19, Fraco, true);
        var eqs = Equipas(d);
        int n = Math.Max(eqs.Count, 1);
        double yCab = r.Y + 40;
        double topo = yCab + 10;
        double h = Math.Clamp(Math.Floor((r.Bottom - topo) / n), 12, 46);
        double fonte = Math.Clamp(Math.Round(h * 0.55 * 2) / 2, 9, 17);

        // as colunas: barra 4, posicao, bandeira, clube (o que sobra), 8 numeros, as ultimas 5
        double dot = Math.Clamp(Math.Floor(h * 0.5), 7, 13), passoDot = dot + Math.Max(3, Math.Round(dot * 0.35));
        double wUlt = 5 * passoDot + 6;
        double wPos = fonte * 2.2, wBand = Math.Round(h * 0.55) + 2;
        double fixo = 6 + wPos + 8 + wBand + 8 + wUlt + 8;
        double resto = r.Width - fixo;
        double wNum = Math.Clamp((resto - 130) / 8.4, fonte * 2.1, fonte * 3.4);
        double wSg = wNum * 1.4;
        double wNome = resto - 7 * wNum - wSg;
        double xPos = r.X + 6, xBand = xPos + wPos + 8, xNome = xBand + wBand + 8, xNum = xNome + wNome;
        double xUlt = xNum + 7 * wNum + wSg + 8;
        string[] cab = { "Pts", "PJ", "VIT", "E", "DER", "GM", "GC", "SG" };
        double ColX(int k) => xNum + k * wNum + (k == 7 ? wSg : wNum) / 2;
        double fc = Math.Clamp(fonte - 1.5, 9, 12);
        Escreve(dc, T("Clube"), xNome, yCab, fc, Fraco);
        for (int k = 0; k < cab.Length; k++) Centro(dc, T(cab[k]), ColX(k), yCab, fc, Fraco);
        Centro(dc, T("Últimas 5"), xUlt + wUlt / 2, yCab, fc, Fraco);

        for (int i = 0; i < eqs.Count; i++)
        {
            var e = eqs[i];
            var lr = new Rect(r.X, topo + i * h, r.Width, h);
            bool humana = Humana(e);
            if (humana) dc.FillRectangle(Humano, lr);
            else if (i % 2 == 1) dc.FillRectangle(Zebra, lr);
            dc.DrawLine(new Pen(Fio, 1), new Point(lr.X, lr.Bottom - 0.5), new Point(lr.Right, lr.Bottom - 0.5));
            double ym = lr.Y + h / 2;
            if (Zona(d, i, eqs.Count) is { } z) dc.FillRectangle(z, new Rect(lr.X, lr.Y + 2, 4, h - 4));
            var tp = Txt((i + 1).ToString(), fonte, Fraco);
            dc.DrawText(tp, new Point(xPos + wPos - tp.Width, ym - tp.Height / 2));
            Bandeira(dc, e, new Rect(xBand, ym - (h * 0.62) / 2, wBand, h * 0.62));
            using (dc.PushClip(new Rect(xNome, lr.Y, wNome - 4, h)))
                Escreve(dc, Nome(e.NomeCurto).ToUpper(CultureInfo.GetCultureInfo("pt-BR")), xNome, ym, fonte, Texto, humana);
            int v = EqW(e, EqVitorias), em = EqW(e, EqEmpates), de = EqW(e, EqDerrotas);
            int gm = EqW(e, EqGolosPro), gc = EqW(e, EqGolosContra), sg = gm - gc;
            int[] nums = { EqW(e, EqPontos), v + em + de, v, em, de, gm, gc };
            for (int k = 0; k < nums.Length; k++) Centro(dc, nums[k].ToString(), ColX(k), ym, fonte, Texto, k == 0);
            Centro(dc, sg > 0 ? "+" + sg : sg.ToString(), ColX(7), ym, fonte, Texto);
            var ult = Ultimas(e);
            double dx = xUlt + 3 + (passoDot - dot) / 2;
            for (int k = 0; k < 5; k++, dx += passoDot)
            {
                int j = k - (5 - ult.Count);
                IBrush cor = j < 0 ? NaoJogou : ult[j] > 0 ? Vitoria : ult[j] == 0 ? Empate : Derrota;
                dc.DrawEllipse(cor, Contorno, new Point(dx + dot / 2, ym), dot / 2, dot / 2);
            }
            _linhas.Add((lr, e));
        }
    }

    // a bandeira nas cores da equipa: tres faixas em pe (fundo, letra, fundo)
    static void Bandeira(DrawingContext dc, Equipa e, Rect r)
    {
        var f = Cor(e.CorFundo, Brushes.White);
        var l = Cor(e.CorLetra, Brushes.Black);
        double w = r.Width / 3;
        dc.FillRectangle(f, new Rect(r.X, r.Y, w, r.Height));
        dc.FillRectangle(l, new Rect(r.X + w, r.Y, w, r.Height));
        dc.FillRectangle(f, new Rect(r.X + 2 * w, r.Y, r.Width - 2 * w, r.Height));
        dc.DrawRectangle(new Pen(H("#55000000"), 1), r.Deflate(0.5));
    }

    // ---------------------------------------------------------------- mouse e teclado
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetPosition(this);
        e.Handled = true;
        if (BotaoFechar().Contains(p) || !_janela.Contains(p)) { Fechar(); return; }
        foreach (var (r, d) in _abas)
            if (r.Contains(p)) { Mostrar(d); _tiques = 0; return; }
        foreach (var (r, eq) in _linhas)
            if (r.Contains(p))
            {
                if (TelaClassificacao.AbrirEquipa is { } abrir)
                {
                    _relogio?.Stop();
                    abrir(eq, _relogio != null, () => { if (!_fechou) { _relogio?.Start(); Focus(); } });
                }
                return;
            }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int passo = _largo ? 2 : 1;
        switch (e.Key)
        {
            case Key.Enter or Key.Escape: Fechar(); break;
            case Key.Left when _divisao > 0: Mostrar(_divisao - passo); _tiques = 0; break;
            case Key.Right when _divisao + passo < _divisoes: Mostrar(_divisao + passo); _tiques = 0; break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }

    void Fechar()
    {
        if (_fechou) return;
        _fechou = true;
        _relogio?.Stop();
        Fechou?.Invoke();
    }
}
