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

namespace RetroFoot.App;

// A Classificacao no visual Original (Visual.cs): o TclassificationDlg (seg06), desenhado
// como o original em 800x600:
//  - o formulario do DFM (588 de largura, fundo verde), com 4 listas de desenho proprio, uma
//    por divisao (1a e 2a a esquerda, 3a e 4a a direita), 16 pontos por linha;
//  - cada linha (DrawItem seg06:03eb -> TTeam.Formatar seg03:327d com 8 -> seg14:0ad6) vem nas
//    cores da equipa: nome curto (120), V, E, D (20 cada), GM (30) ":" (10) GS (15), pontos (30);
//  - o titulo e "Classificacao - Na jornada" (Run, seg06:02ad) ou so "Classificacao" sem
//    numero; depois de cada jornada o relogio fecha a janela ao fim de 10 tiques de 1 s
//    (Timer1Timer, seg06:0a50); aberta pelo menu nao fecha sozinha;
//  - clicar no fundo fecha (OnClick = GoOn); clicar numa equipa abre o plantel dela
//    (ListBoxDivisionNClick, seg06:07f4) com o relogio parado; Enter e Esc tambem fecham.
// Do launcher (tools/divisoes/paginas.py, paginas.asm, classif.asm): ate 14 equipas por
// divisao as listas crescem; acima disso, 2 paginas (1a+2a e 3a+4a) com as setas < e >
// (cinzenta a que nao leva a lado nenhum; mudar de pagina zera o relogio), e a janela abre na
// pagina com mais treinadores humanos (empate: a 1a). Da versao em Go: o cabecalho das
// colunas por cima de cada lista. Como o cabecalho ocupa o lugar das setas do launcher, com
// 2 paginas as setas descem para baixo das listas.
// No 16:9 (Formato.Largo) nao ha paginas: acima de 14 equipas as 4 listas ficam lado a lado
// numa janela larga (1181 de cliente) desenhada em 1200x675, a mesma proporcao da tela; ate
// 14 fica igual ao 4:3 (as 4 ja cabem).
sealed class TelaClassificacaoOriginal : Control
{
    readonly double L = 800, A = 600;
    static readonly FontFamily Arial = new("avares://RetroFoot.App/Assets/Fontes#Liberation Sans");
    static readonly FontFamily Sans = new("avares://RetroFoot.App/Assets/Fontes#MS Sans Serif");
    static readonly IBrush Verde = new SolidColorBrush(Color.FromRgb(0, 128, 0));
    static readonly IBrush Amarelo = new SolidColorBrush(Color.FromRgb(255, 255, 0));
    static readonly IBrush Prata = new SolidColorBrush(Color.FromRgb(192, 192, 192));
    static readonly IBrush Cinza = new SolidColorBrush(Color.FromRgb(128, 128, 128));
    static readonly IBrush Titulo = new SolidColorBrush(Color.FromRgb(50, 150, 250));
    static readonly IBrush Branco = Brushes.White, Preto = Brushes.Black;

    // Acrescento (desligado, como todos: o original nao destaca): nome das equipas de
    // treinadores humanos em negrito.
    public static bool DestacarHumanos { get; set; }

    const int Linha = 16;                 // ItemHeight
    const double LarguraLista = 273;
    // (fora do original) a posicao de cada equipa, pequena, a esquerda da lista: a janela
    // ganha estes pontos de largura e as listas andam o mesmo para a direita
    const double MargemPosicao = 8;

    public event Action? Fechou;

    readonly Jogo98 _s;
    readonly Dictionary<ushort, Equipa> _equipas = new();
    readonly Dictionary<ushort, Treinador> _treinadores = new();
    readonly int _n;                      // equipas por divisao (as da 1a)
    readonly bool _paginas;               // mais de 14 no 4:3: 2 paginas
    readonly bool _lado;                  // mais de 14 no 16:9: as 4 lado a lado
    readonly double _alturaLista, _alturaCliente;
    readonly Rect _janela, _cliente;
    readonly string _titulo;
    int _pagina;                          // 0 = 1a+2a, 1 = 3a+4a (so com _paginas)
    int _tiques;                          // +0x1A0
    DispatcherTimer? _relogio;
    bool _fechou;

    public TelaClassificacaoOriginal(Jogo98 s, int rodada, bool relogio)
    {
        _s = s;
        foreach (var e in s.Equipas) _equipas[e.ID] = e;
        foreach (var t in s.Treinadores) _treinadores[t.ID] = t;

        _n = s.Divisoes.Count > 0 ? Math.Max(s.Divisoes[0].Equipas.Count, 1) : 8;
        _lado = _n > 14 && Formato.Largo;
        _paginas = _n > 14 && !_lado;
        if (_lado) { L = 1200; A = 675; }
        Width = L;
        Height = A;
        ClipToBounds = true;
        Focusable = true;
        _alturaLista = _n * Linha + 2;
        // 8 equipas: 41 + 130 + 46 + 130 + 24 = 371, o ClientHeight do DFM
        _alturaCliente = _paginas ? 41 + _alturaLista + 6 + 26 + 10
            : _lado ? 41 + _alturaLista + 24
            : 41 + 2 * _alturaLista + 46 + 24;
        double larguraCliente = (_lado ? 10 + 3 * 296 + LarguraLista + 10 : 588) + MargemPosicao;
        double w = larguraCliente + 6, h = _alturaCliente + 6 + 19;
        _janela = new Rect(Math.Floor((L - w) / 2), Math.Floor((A - h) / 2), w, h);
        _cliente = new Rect(_janela.X + 3, _janela.Y + 3 + 19, larguraCliente, _alturaCliente);
        _titulo = rodada > 0 ? T("Classificação - {0}ª rodada", rodada) : T("Classificação");
        if (_paginas) _pagina = PaginaDosHumanos();

        if (relogio)
        {
            _relogio = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _relogio.Tick += (_, _) => { if (++_tiques == 10) Fechar(); };
            _relogio.Start();
        }
        DetachedFromVisualTree += (_, _) => _relogio?.Stop();
        AttachedToVisualTree += (_, _) => Focus();
    }

    // classif.asm: humanos (treinador com +7 != 0) na 1a+2a e na 3a+4a; mais em baixo -> 2a pagina
    int PaginaDosHumanos()
    {
        int cima = 0, baixo = 0;
        for (int d = 0; d < 4 && d < _s.Divisoes.Count; d++)
            foreach (var id in _s.Divisoes[d].Equipas)
                if (_equipas.TryGetValue(id, out var e) && _treinadores.TryGetValue(e.Treinador, out var t) && t.Humano != 0)
                {
                    if (d < 2) cima++;
                    else baixo++;
                }
        return baixo > cima ? 1 : 0;
    }

    // ---------------------------------------------------------------- geometria
    int Visiveis => _paginas ? 2 : 4;

    // a lista k (0..3 sem paginas: 1a, 2a, 3a, 4a; com paginas: esquerda, direita)
    Rect Lista(int k)
    {
        double x, y;
        if (_paginas || _lado) { x = 10 + MargemPosicao + 296 * k; y = 41; }
        else { x = 10 + MargemPosicao + 296 * (k / 2); y = 41 + (k % 2) * (_alturaLista + 46); }
        return new Rect(_cliente.X + x, _cliente.Y + y, LarguraLista, _alturaLista);
    }

    int DivisaoDa(int k) => _paginas ? _pagina * 2 + k : k;

    // as setas do launcher (TLabel 30x26, Arial -21 negrito, fundo prateado)
    Rect Seta(int i) => new(_cliente.X + 517 + MargemPosicao + 31 * i, _cliente.Y + 41 + _alturaLista + 6, 30, 26);

    // ---------------------------------------------------------------- desenho
    static FormattedText Txt(string s, double tam, IBrush cor, bool negrito = false, FontFamily? fonte = null) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(fonte ?? Arial, FontStyle.Normal, negrito ? FontWeight.Bold : FontWeight.Normal), tam * Acessibilidade.Texto, cor);

    static void Escreve(DrawingContext dc, string s, double x, double y, double tam, IBrush cor, bool negrito = false, FontFamily? fonte = null) =>
        dc.DrawText(Txt(s, tam, cor, negrito, fonte), new Point(x, y));

    static void Direita(DrawingContext dc, string s, double x, double y, double tam, IBrush cor, bool negrito = false)
    {
        var t = Txt(s, tam, cor, negrito);
        dc.DrawText(t, new Point(x - t.Width, y));
    }

    static void Borda(DrawingContext dc, Rect r, bool afundada)
    {
        var claro = new Pen(afundada ? Cinza : Branco, 1);
        var sombra = new Pen(afundada ? Branco : Cinza, 1);
        dc.DrawLine(claro, r.TopLeft, r.TopRight);
        dc.DrawLine(claro, r.TopLeft, r.BottomLeft);
        dc.DrawLine(sombra, r.BottomLeft, r.BottomRight);
        dc.DrawLine(sombra, r.TopRight, r.BottomRight);
    }

    public override void Render(DrawingContext dc)
    {
        // a janela (bsDialog): moldura, barra do titulo com o botao de fechar, cliente verde
        dc.FillRectangle(Prata, _janela);
        Borda(dc, _janela.Deflate(0.5), false);
        var barra = new Rect(_janela.X + 3, _janela.Y + 3, _janela.Width - 6, 18);
        dc.FillRectangle(Titulo, barra);
        Escreve(dc, _titulo, barra.X + 4, barra.Y + 2, 11, Preto, true, Sans);
        var x = BotaoFechar();
        dc.FillRectangle(Prata, x);
        Borda(dc, x.Deflate(0.5), false);
        Escreve(dc, "×", x.X + 4, x.Y - 2, 13, Preto, true, Sans);
        dc.FillRectangle(Verde, _cliente);

        using (dc.PushClip(_cliente))
        {
            for (int k = 0; k < Visiveis; k++) DesenhaDivisao(dc, k);
            if (_paginas)
            {
                DesenhaSeta(dc, 0, "<", _pagina > 0);
                DesenhaSeta(dc, 1, ">", _pagina < 1);
            }
        }
    }

    Rect BotaoFechar() => new(_janela.Right - 3 - 2 - 16, _janela.Y + 3 + 2, 16, 14);

    void DesenhaSeta(DrawingContext dc, int i, string s, bool ativa)
    {
        var r = Seta(i);
        dc.FillRectangle(Prata, r);
        var t = Txt(s, 21, ativa ? Preto : Cinza, true);
        dc.DrawText(t, new Point(r.X + (r.Width - t.Width) / 2, r.Y));
    }

    void DesenhaDivisao(DrawingContext dc, int k)
    {
        var r = Lista(k);
        int d = DivisaoDa(k);
        // o titulo (Label1..4: Left = lista + 1, Top = lista - 24; Arial -19 negrito, amarelo)
        Escreve(dc, T("{0}ª Divisão", d + 1), r.X + 1, r.Y - 24, 19, Amarelo, true);
        // o cabecalho das colunas, alinhado a direita como os numeros
        double cy = r.Y - 15;
        // (fora do original) os pontos logo depois do nome, antes das vitorias
        Direita(dc, T("pontos:P"), r.X + 2 + 145, cy, 11, Amarelo);
        Direita(dc, T("V"), r.X + 2 + 167, cy, 11, Amarelo);
        Direita(dc, T("E"), r.X + 2 + 187, cy, 11, Amarelo);
        Direita(dc, T("D"), r.X + 2 + 207, cy, 11, Amarelo);
        Direita(dc, T("GM"), r.X + 2 + 237, cy, 11, Amarelo);
        Direita(dc, T("GS"), r.X + 2 + 264, cy, 11, Amarelo);


        // a lista (Ctl3D = False: borda preta de 1 ponto; ParentColor: fundo verde)
        dc.DrawRectangle(new Pen(Preto, 1), r.Deflate(0.5));
        if (d >= _s.Divisoes.Count) return;
        var dentro = r.Deflate(1);
        using (dc.PushClip(dentro))
        {
            double y = dentro.Y;
            foreach (var id in _s.Divisoes[d].Equipas)
            {
                if (!_equipas.TryGetValue(id, out var e)) continue;
                DesenhaEquipa(dc, e, new Rect(dentro.X, y, dentro.Width, Linha));
                y += Linha;
            }
        }
        // as posicoes, fora da lista (no verde a esquerda dela)
        double yp = dentro.Y + 2;
        for (int i = 0; i < _s.Divisoes[d].Equipas.Count; i++, yp += Linha)
            Direita(dc, (i + 1).ToString(), r.X - 2, yp, 8, Amarelo);
    }

    // seg14:0ad6: fundo na cor do fundo da equipa; as colunas a partir de x + 2, o texto no
    // topo do item; os numeros alinhados a direita na largura da coluna
    void DesenhaEquipa(DrawingContext dc, Equipa e, Rect r)
    {
        var letra = Cor(e.CorLetra, Preto);
        dc.FillRectangle(Cor(e.CorFundo, Branco), r);
        bool negrito = DestacarHumanos && _treinadores.TryGetValue(e.Treinador, out var t) && t.Humano != 0;
        double x = r.X + 2, y = r.Y;
        using (dc.PushClip(new Rect(x, y, 120, Linha)))
            Escreve(dc, Nome(e.NomeCurto), x, y, 11, letra, negrito);
        x += 120;
        void Col(string s, double larg) { Direita(dc, s, x + larg, y, 11, letra); x += larg; }
        Col(EqW(e, EqPontos).ToString(), 25);
        Col(EqW(e, EqVitorias).ToString(), 22);
        Col(EqW(e, EqEmpates).ToString(), 20);
        Col(EqW(e, EqDerrotas).ToString(), 20);
        Col(EqW(e, EqGolosPro).ToString(), 30);
        Col(":", 10);
        Col(EqW(e, EqGolosContra).ToString(), 17);
    }

    static string Nome(byte[] cifrado) =>
        cifrado.Length == 0 ? "" : System.Text.Encoding.Latin1.GetString(Cifra.TextoEFT(cifrado).Texto);

    // TColor guardado em 4 bytes (00BBGGRR); as cores do sistema (0x80......) ficam no padrao
    static IBrush Cor(byte[] c, IBrush padrao) =>
        c.Length < 4 || c[3] != 0 ? padrao : new SolidColorBrush(Color.FromRgb(c[0], c[1], c[2]));

    // ---------------------------------------------------------------- mouse e toque
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetPosition(this);
        e.Handled = true;
        if (BotaoFechar().Contains(p)) { Fechar(); return; }
        if (!_cliente.Contains(p)) return;
        if (_paginas)
        {
            // a seta que nao leva a lado nenhum nao faz nada (e nao fecha)
            if (Seta(0).Contains(p)) { MudarPagina(0); return; }
            if (Seta(1).Contains(p)) { MudarPagina(1); return; }
        }
        for (int k = 0; k < Visiveis; k++)
            if (Lista(k).Contains(p))
            {
                // ListBoxDivisionNClick: o plantel da equipa da linha, com o relogio parado
                int d = DivisaoDa(k), i = (int)((p.Y - Lista(k).Y - 1) / Linha);
                if (TelaClassificacao.AbrirEquipa is { } abrir && d < _s.Divisoes.Count && i >= 0 && i < _s.Divisoes[d].Equipas.Count
                    && _equipas.TryGetValue(_s.Divisoes[d].Equipas[i], out var eq))
                {
                    _relogio?.Stop();
                    abrir(eq, _relogio != null, () => { if (!_fechou) { _relogio?.Start(); Focus(); } });
                }
                return;
            }
        Fechar();         // o fundo: GoOn
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape) { e.Handled = true; Fechar(); return; }
        if (_paginas && e.Key is Key.Left or Key.Right) { e.Handled = true; MudarPagina(e.Key == Key.Left ? 0 : 1); return; }
        base.OnKeyDown(e);
    }

    void MudarPagina(int p)
    {
        if (p == _pagina) return;
        _pagina = p;
        _tiques = 0;
        InvalidateVisual();
    }

    void Fechar()
    {
        if (_fechou) return;
        _fechou = true;
        _relogio?.Stop();
        Fechou?.Invoke();
    }
}
