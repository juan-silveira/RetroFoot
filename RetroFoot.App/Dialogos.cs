using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RetroFoot.Motor.Base;

using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// A base dos dialogos do original (menus Campeonato e Tecnico, fim de temporada, convite,
// opcoes, acerca): a Janela e os controles do TelaInicio (W95, Botao, Lista, Combo...) mais
// os que faltavam (TCheckBox, TRadioGroup, TTabbedNotebook, a linha formatada das listas),
// abertos por cima da tela atual pela Sessao (AbrirDialogo / EsperarDialogo).

// onde os dialogos abrem: a Sessao (por cima da tela, num quadro de 800x600) ou outro
// "abrir/fechar" (o TelaInicio, por exemplo)
public sealed class Dialogos
{
    readonly Action<Control> abrir;
    readonly Action<Control> fechar;

    public Dialogos(Action<Control> abrir, Action<Control> fechar)
    {
        this.abrir = abrir;
        this.fechar = fechar;
    }

    public static implicit operator Dialogos(Sessao s) => new(s.AbrirDialogo, c => s.FecharDialogo(c));

    internal CaixaDialogo Abrir(Janela j)
    {
        var c = new CaixaDialogo(j);
        abrir(c);
        return c;
    }

    internal void Fechar(Control c) => fechar(c);

    // um dialogo que nao e uma Janela (os do visual Moderno)
    internal void AbrirControle(Control c) => abrir(c);
    // um controle pronto (as janelas do visual Moderno)
    internal void Abrir(Control c) => abrir(c);
}

// a Janela dentro de uma caixa do tamanho dela: Enter = AoEnter (o botao padrao), Esc =
// AoFechar (o Cancelar ou o "x"); a lista de um Combo abre por cima, nesta caixa
sealed class CaixaDialogo : Canvas
{
    public readonly Janela J;

    public CaixaDialogo(Janela j)
    {
        J = j;
        Width = j.Width;
        Height = j.Height;
        Children.Add(j);
        Focusable = true;
        FocusAdorner = null;
    }

    // os abertos, para devolver o foco ao de baixo quando o de cima fecha
    static readonly List<CaixaDialogo> abertas = new();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        abertas.Add(this);
        Dispatcher.UIThread.Post(Focar, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        abertas.Remove(this);
        if (abertas.Count > 0) Dispatcher.UIThread.Post(abertas[^1].Focar, DispatcherPriority.Loaded);
    }

    public void Focar()
    {
        if (J.Foco is { Focusable: true, IsEffectivelyVisible: true } f) f.Focus();
        else Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (e.Key == Key.Enter && J.AoEnter != null) { J.AoEnter(); e.Handled = true; }
        else if (e.Key == Key.Escape && J.AoFechar != null) { J.AoFechar(); e.Handled = true; }
    }
}

// fontes e textos dos dialogos
static class Dlg
{
    internal static readonly FontFamily FArial = new("avares://RetroFoot.App/Assets/Fontes#Liberation Sans");
    internal static readonly FontFamily FMsSans = new("avares://RetroFoot.App/Assets/Fontes#MS Sans Serif");
    internal static readonly FontFamily FTimes = new("avares://RetroFoot.App/Assets/Fontes#Liberation Serif");

    public static readonly Fonte Arial9b = new(FArial, 9, true), Arial9 = new(FArial, 9, false),
        MsSans12 = new(FMsSans, 12, false);

    // clLime, a cor 8454016 do convite, clGreen
    public static readonly IBrush Lima = W95.Cor(0, 255, 0), VerdeClaro = W95.Cor(128, 255, 128),
        Branco = W95.Branco, Aqua = W95.Cor(0, 255, 255);

    public static FormattedText Txt(string s, FontFamily f, double tam, IBrush cor, bool negrito = false, bool italico = false) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(f, italico ? FontStyle.Italic : FontStyle.Normal, negrito ? FontWeight.Bold : FontWeight.Normal), tam * Acessibilidade.Texto, cor);

    // texto do save (cifrado, com o tamanho na frente)
    public static string Texto(byte[] b) => b.Length == 0 ? "" : Encoding.Latin1.GetString(Cifra.TextoEFT(b).Texto);
    public static byte[] Bytes(byte[] b) => b.Length == 0 ? Array.Empty<byte>() : Cifra.TextoEFT(b).Texto;
    public static byte[] Latin1(string s) => Encoding.Latin1.GetBytes(s);

    public static Bitmap? Imagem(string nome) => W95.Imagem("Dialogos/" + nome);

    // TBitBtn de 64x64 com o glifo em cima (Layout = blGlyphTop)
    public static Botao BotaoGrande(string texto, string? glifo, Fonte? f = null, int glifos = 2) => new()
    {
        Texto = texto, Fonte = f ?? Arial9b, GlifoEmCima = true, NumGlifos = glifos,
        Glifo = glifo == null ? null : glifo.StartsWith("Botoes/") ? W95.Imagem(glifo) : Imagem(glifo),
    };

    // TColor do save ($00BBGGRR); as cores do sistema (bit de cima) ficam com o padrao
    public static IBrush CorDelphi(byte[] b, IBrush padrao) =>
        b.Length < 4 || (b[3] & 0x80) != 0 ? padrao : W95.Cor(b[0], b[1], b[2]);

    // o MessageBox com Sim e Nao (MB_YESNO)
    public static void Perguntar(Dialogos d, string titulo, string texto, Action sim, Action? nao = null)
    {
        if (Visual.Moderno) { PerguntarModerno(d, titulo, texto, sim, nao); return; }
        double w = Math.Max(240, Math.Ceiling(W95.Txt(texto, W95.MsSans11, W95.Preto).Width) + 60);
        var j = new Janela(titulo, w, 96, W95.Face);
        j.Por(new Rotulo { Texto = texto, Fonte = W95.MsSans11, Auto = true }, 30, 20, w - 40, 16);
        var bs = j.Por(new Botao { Texto = T("Sim"), Fonte = W95.MsSans11 }, w / 2 - 80, 58, 75, 23);
        var bn = j.Por(new Botao { Texto = T("Não"), Fonte = W95.MsSans11 }, w / 2 + 5, 58, 75, 23);
        CaixaDialogo? c = null;
        bs.Clique += () => { d.Fechar(c!); sim(); };
        bn.Clique += () => { d.Fechar(c!); nao?.Invoke(); };
        j.AoEnter = () => { d.Fechar(c!); sim(); };
        j.AoFechar = () => { d.Fechar(c!); nao?.Invoke(); };
        c = d.Abrir(j);
    }

    // a TInputDlg (10:3e69): titulo, rotulo, o texto inicial e o tamanho maximo; no original
    // a caixa e ecUpperCase (DFM), aqui aceita maiusculas e minusculas (o nome como se escreve)
    public static void PedirTexto(Dialogos d, string titulo, string rotulo, string inicial, int maximo, Action<string> ok)
    {
        if (Visual.Moderno) { PedirTextoModerno(d, titulo, rotulo, inicial, maximo, ok); return; }
        var j = new Janela(titulo, 388, 132, W95.Face);
        j.Por(new Bevel(), 8, 8, 281, 105);
        j.Por(new Rotulo { Texto = rotulo, Fonte = W95.MsSans11 }, 16, 24, 265, 33);
        var bok = j.Por(Botao.Ok(W95.MsSans11b, false), 300, 8, 85, 27);
        var bc = j.Por(Botao.Cancelar(W95.MsSans11b, false), 300, 40, 85, 27);
        var ed = j.Por(new Edit { MaxLen = maximo, Fonte = W95.MsSans11b }, 16, 72, 265, 20);
        ed.Texto = inicial;
        CaixaDialogo? c = null;
        void Ok() { d.Fechar(c!); ok(ed.Texto); }
        void Cancelar() => d.Fechar(c!);
        bok.Clique += Ok;
        bc.Clique += Cancelar;
        j.AoEnter = Ok;
        j.AoFechar = Cancelar;
        j.Foco = ed;
        c = d.Abrir(j);
    }

    // ------------------------------------------------------------ Moderno
    // as perguntas no visual Moderno (ClubeModerno.cs): a janela verde das telas do clube,
    // pequena e centrada nos dois formatos; Sim/OK a direita, como os botoes principais
    static void PerguntarModerno(Dialogos d, string titulo, string texto, Action sim, Action? nao)
    {
        double w = Math.Clamp(W95.Txt(texto, W95.MsSans11, W95.Preto).Width * 1.25 + 80, 380, 600);
        var corpo = ClubeModerno.T(texto, 15, ClubeModerno.Texto);
        var j = new JanelaModerna(titulo, null, null, corpo, w, 180);
        bool respondeu = false;
        void Responder(bool s)
        {
            if (respondeu) return;
            respondeu = true;
            d.Fechar(j);
            if (s) sim(); else nao?.Invoke();
        }
        j.Botao(T("Não"), false, () => Responder(false));
        j.Botao(T("Sim"), true, () => Responder(true));
        j.AoEnter = () => Responder(true);
        j.AoFechar = () => Responder(false);
        d.Abrir(j);
    }

    static void PedirTextoModerno(Dialogos d, string titulo, string rotulo, string inicial, int maximo, Action<string> ok)
    {
        var p = new StackPanel { Spacing = 10 };
        p.Children.Add(ClubeModerno.T(rotulo, 14, ClubeModerno.Fraco));
        var ed = new TextBox
        {
            Text = inicial, MaxLength = maximo, FontSize = 16, FontWeight = FontWeight.SemiBold,
            Foreground = ClubeModerno.Texto, Background = ClubeModerno.Cartao, BorderBrush = TemaVerde.Borda,
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8), CaretBrush = ClubeModerno.Azul,
            SelectionBrush = TemaVerde.Destaque, SelectionForegroundBrush = TemaVerde.SobreDestaque,
        };
        // o Fluent pinta o campo com o azul dele no foco e com o rato em cima: o verde e o amarelo do tema
        ed.Resources["TextControlBackgroundFocused"] = TemaVerde.Cartao;
        ed.Resources["TextControlBackgroundPointerOver"] = TemaVerde.Cartao;
        ed.Resources["TextControlBorderBrushFocused"] = TemaVerde.Destaque;
        ed.Resources["TextControlBorderBrushPointerOver"] = TemaVerde.Ouro;
        ed.Resources["TextControlForegroundFocused"] = TemaVerde.Texto;
        ed.Resources["TextControlForegroundPointerOver"] = TemaVerde.Texto;
        p.Children.Add(ed);
        var j = new JanelaModerna(titulo, null, null, p, 440, 240);
        bool fechou = false;
        void Ok() { if (fechou) return; fechou = true; d.Fechar(j); ok(ed.Text ?? ""); }
        void Cancelar() { if (fechou) return; fechou = true; d.Fechar(j); }
        j.Botao(T("Cancelar"), false, Cancelar);
        j.Botao(T("OK"), true, Ok);
        j.AoEnter = Ok;
        j.AoFechar = Cancelar;
        j.Foco = ed;
        d.Abrir(j);
        Dispatcher.UIThread.Post(() => { ed.Focus(); ed.SelectAll(); }, DispatcherPriority.Loaded);
    }
}

// ==================================================================== a linha formatada
// O texto das listas do original (14:0ad6 / 14:08e0): colunas separadas por TAB, cada uma
// com [codigo] largura texto. Codigos: 2 negrito, 3 italico, 4 e 5 desligam, 6 alinha a
// direita e deixa 10 pontos depois da coluna, 7 so alinha a direita, 0xFC a linha em
// vermelho. 0xFF/0xFE no texto: os simbolos (bolinha/traco); 0xFD: um traco no fundo.
// (RetroFoot) 1 lo hi, antes dos outros codigos da coluna: o logo de uma equipa (Logo) no
// comeco da coluna, com o texto depois dele; sem logo (visual Classico), nada muda.
static class Formatado
{
    // os logos que as linhas citam (1 lo hi = o indice aqui); os bitmaps sao os do cache dos
    // Escudos, entao a lista nao passa do numero de equipas
    static readonly List<Bitmap> _logos = new();
    static readonly Dictionary<Bitmap, int> _indice = new();
    // a largura do logo com o espaco depois dele; cresce e encolhe com o texto (Acessibilidade)
    static double LogoW => Math.Round(18 * Acessibilidade.Texto);

    // o logo da equipa no comeco da proxima coluna (chamar antes do Col dela)
    public static void Logo(List<byte> b, RetroFoot.Motor.Save.Equipa? e)
    {
        if (e == null || Escudos.Escudo(e) is not { } bmp) return;
        if (!_indice.TryGetValue(bmp, out int k))
        {
            if (_logos.Count >= 0xFFFF) return;
            k = _logos.Count;
            _logos.Add(bmp);
            _indice[bmp] = k;
        }
        b.Add(1); b.Add((byte)k); b.Add((byte)(k >> 8));
    }

    // quanto tirar da coluna larga (a mais larga alinhada a esquerda, antes da ultima) para a
    // linha nao chegar a barra de rolagem; quem desenha a lista mede (Excesso) e liga aqui
    // durante o desenho. 0 = as larguras do original.
    [ThreadStatic] public static double Encolher;

    // folga entre o fim do texto e a barra de rolagem
    public const double Folga = 6;

    internal record struct Coluna(bool Negrito, bool Italico, bool Vermelho, bool Direita, bool Mais10, int W, string Txt, Bitmap? Logo = null);

    internal static List<Coluna> Ler(byte[] s, bool negrito)
    {
        var l = new List<Coluna>();
        bool direita = false, mais10 = false, italico = false, vermelho = false;
        int i = 0;
        while (i < s.Length)
        {
            Bitmap? logo = null;
            if (s[i] == 1 && i + 2 < s.Length)
            {
                int k = s[i + 1] | s[i + 2] << 8;
                if (k < _logos.Count) logo = _logos[k];
                i += 3;
                if (i >= s.Length) break;
            }
            switch (s[i])
            {
                case 2: negrito = true; i++; break;
                case 3: italico = true; i++; break;
                case 4: negrito = false; i++; break;
                case 5: italico = false; i++; break;
                case 6: direita = true; mais10 = true; i++; break;
                case 7: direita = true; mais10 = false; i++; break;
                case 0xFC: vermelho = true; i++; break;
            }
            if (i >= s.Length) break;
            int w = s[i++];
            int ini = i;
            while (i < s.Length && s[i] != 9) i++;
            // o € passa pelo 0x80 do Windows-1252 (o Latin1 nao o tem)
            l.Add(new Coluna(negrito, italico, vermelho, direita, mais10, w, Encoding.Latin1.GetString(s, ini, i - ini).Replace('\u0080', '\u20ac'), logo));
            i++;   // o TAB
            direita = false;
        }
        return l;
    }

    // a coluna que encolhe: a mais larga alinhada a esquerda (o nome), fora a ultima
    static int Larga(List<Coluna> l)
    {
        int k = -1;
        for (int j = 0; j < l.Count - 1; j++)
            if (!l[j].Direita && l[j].W >= 60 && (k < 0 || l[j].W > l[k].W)) k = j;
        return k;
    }

    static bool Simbolo(string t) => t.Length > 0 && t[0] is 'ÿ' or 'þ' or 'ý';

    // quantos pontos a linha passa de largura - Folga (0 se cabe)
    public static double Excesso(Fonte f, byte[] s, double largura)
    {
        var l = Ler(s, f.Negrito);
        double x = 2, fim = 0;
        for (int j = 0; j < l.Count; j++)
        {
            var c = l[j];
            if (c.Txt.Length > 0 && !Simbolo(c.Txt))
            {
                double tw = c.Direita ? c.W : Dlg.Txt(c.Txt, f.Familia, f.Tamanho, Brushes.Black, c.Negrito, c.Italico).Width + (c.Logo != null ? LogoW : 0);
                fim = Math.Max(fim, x + (j < l.Count - 1 && !c.Direita ? Math.Min(tw, c.W) : tw));
            }
            x += c.W;
            if (c.Mais10) x += 10;
        }
        return Math.Max(0, Math.Ceiling(fim - (largura - Folga)));
    }

    public static double Excesso(Fonte f, int n, Func<int, byte[]> linha, double largura)
    {
        double e = 0;
        for (int i = 0; i < n; i++) e = Math.Max(e, Excesso(f, linha(i), largura));
        return e;
    }

    public static void Desenhar(DrawingContext dc, Fonte f, IBrush cor, byte[] s, Rect r)
    {
        var l = Ler(s, f.Negrito);
        int k = Encolher > 0 ? Larga(l) : -1;
        double x = r.X + 2;
        for (int j = 0; j < l.Count; j++)
        {
            var c = l[j];
            var txt = c.Txt;
            var cr = c.Vermelho ? W95.Vermelho : cor;
            double w = j == k ? Math.Max(c.W / 2, c.W - Encolher) : c.W;
            // o logo no comeco da coluna e o texto depois dele (na mesma largura)
            double xl = 0;
            if (c.Logo is { } lg && !c.Direita)
            {
                // centrado na linha do texto (que e desenhado no topo de r), nao no meio de r
                double lh = Dlg.Txt("Ág", f.Familia, f.Tamanho, cor).Height;
                double q = Math.Min(Math.Round(15 * Acessibilidade.Texto), r.Height - 2);
                double yl = Math.Max(r.Y, r.Y + (lh - q) / 2);
                using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality }))
                    dc.DrawImage(lg, new Rect(x, yl, q, q));
                xl = LogoW;
            }
            x += xl; w -= xl;
            if (txt.Length > 0 && txt[0] == 'ÿ')
                dc.FillRectangle(cr, new Rect(x + 1, r.Y + 6, 3, 3));
            else if (txt.Length > 0 && txt[0] == 'þ')
                dc.FillRectangle(cr, new Rect(x, r.Y + 7, 6, 1));
            else if (txt.Length > 0 && txt[0] == 'ý')
                dc.FillRectangle(cr, new Rect(r.X, r.Bottom - 4, r.Width, 1));
            else if (txt.Length > 0)
            {
                var t = Dlg.Txt(txt, f.Familia, f.Tamanho, cr, c.Negrito, c.Italico);
                double xx = c.Direita ? x + w - t.WidthIncludingTrailingWhitespace : x;
                if (j == k)
                    using (dc.PushClip(new Rect(x, r.Y, w + (c.Mais10 ? 10 : 0) - 3, r.Height)))
                        dc.DrawText(t, new Point(xx, r.Y));
                else dc.DrawText(t, new Point(xx, r.Y));
            }
            x += w;
            if (c.Mais10) x += 10;
        }
    }

    // uma coluna: [prefixo...] texto TAB
    public static void Col(List<byte> b, string texto, params byte[] prefixo)
    {
        b.AddRange(prefixo);
        // o euro nao existe no Latin1: vai como 0x80 (o do Windows-1252) e volta no Desenhar
        b.AddRange(Encoding.Latin1.GetBytes(texto.Replace('\u20ac', '\u0080')));
        b.Add(9);
    }

    public static void Col(List<byte> b, byte[] texto, params byte[] prefixo)
    {
        b.AddRange(prefixo);
        b.AddRange(texto);
        b.Add(9);
    }
}

// ==================================================================== controles que faltavam

// TCheckBox (CB_Paint do Wine: a caixa de 13x13 e o texto a 13 + largura("0")/2 + 1)
sealed class Marca : Control
{
    public string Texto = "";
    public Fonte Fonte = W95.Arial13;
    public IBrush Cor = W95.Preto;
    bool marcado, habilitado = true;
    public event Action? Clique;

    public bool Marcado { get => marcado; set { marcado = value; InvalidateVisual(); } }
    public bool Habilitado { get => habilitado; set { habilitado = value; InvalidateVisual(); } }

    public override void Render(DrawingContext dc)
    {
        double h = Bounds.Height;
        double y0 = Math.Floor((h - 13) / 2);
        var cx = new Rect(0, y0, 13, 13);
        dc.FillRectangle(habilitado ? W95.Branco : W95.Face, cx);
        W95.Linhas(dc, cx, W95.Sombra, W95.Luz);
        W95.Linhas(dc, cx.Deflate(1), W95.Escura, W95.Luz3D);
        if (marcado)
        {
            var c = habilitado ? W95.Preto : W95.Cinzento;
            // o "v" de 7 pontos de largura, 3 de espessura
            for (int i = 0; i < 7; i++)
            {
                double yy = i < 3 ? y0 + 5 + i : y0 + 7 - (i - 2) + 2;
                dc.FillRectangle(c, new Rect(3 + i, yy, 1, 3));
            }
        }
        double x = 13 + Math.Floor(W95.Txt("0", Fonte, Cor).Width / 2) + 1;
        var t = W95.Txt(Texto, Fonte, habilitado ? Cor : W95.Cinzento);
        if (habilitado) dc.DrawText(t, new Point(x, Math.Floor((h - t.Height) / 2)));
        else W95.TextoCinza(dc, Texto, Fonte, x, Math.Floor((h - t.Height) / 2));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!habilitado || !new Rect(Bounds.Size).Contains(e.GetPosition(this))) return;
        Marcado = !marcado;
        Clique?.Invoke();
    }
}

// TGroupBox / TRadioGroup: a moldura gravada com o titulo por cima; os itens do grupo
// dividem a altura abaixo do titulo
sealed class Grupo : Canvas
{
    public string Titulo = "";
    public Fonte Fonte = W95.Arial13;
    readonly List<Radio> itens = new();
    int indice = -1;
    public event Action? Mudou;
    readonly Pintor fundo;

    public Grupo() { fundo = new Pintor(Desenhar); Children.Add(fundo); }

    public int Indice
    {
        get => indice;
        set
        {
            indice = value;
            for (int i = 0; i < itens.Count; i++) itens[i].Marcado = i == value;
        }
    }

    public void Itens(params string[] textos)
    {
        foreach (var t in textos)
        {
            int k = itens.Count;
            var r = new Radio { Texto = t, Fonte = Fonte };
            r.Clique += () => { Indice = k; Mudou?.Invoke(); };
            itens.Add(r);
            Children.Add(r);
        }
    }

    protected override Size ArrangeOverride(Size tamanho)
    {
        // TCustomRadioGroup.ArrangeButtons: topo = altura da letra + 2, a altura que sobra
        // dividida pelos itens; cada um a 8 da esquerda
        double topo = Math.Ceiling(W95.Txt("Ag", Fonte, W95.Preto).Height) + 2;
        double h = itens.Count == 0 ? 0 : Math.Floor((tamanho.Height - topo - 4) / itens.Count);
        for (int i = 0; i < itens.Count; i++)
            itens[i].Arrange(new Rect(8, topo + i * h, Math.Max(0, tamanho.Width - 12), h));
        fundo.Arrange(new Rect(tamanho));
        return tamanho;
    }

    void Desenhar(DrawingContext dc, Size tam)
    {
        var t = W95.Txt(Titulo, Fonte, W95.Preto);
        double ty = Math.Floor(t.Height / 2);
        var r = new Rect(0, ty, tam.Width, tam.Height - ty);
        W95.Linhas(dc, new Rect(r.X + 1, r.Y + 1, r.Width - 1, r.Height - 1), W95.Luz, W95.Luz);
        W95.Linhas(dc, new Rect(r.X, r.Y, r.Width - 1, r.Height - 1), W95.Sombra, W95.Sombra);
        if (Titulo.Length > 0)
        {
            dc.FillRectangle(W95.Face, new Rect(6, 0, t.Width + 4, t.Height));
            dc.DrawText(t, new Point(8, 0));
        }
    }
}

// TTabbedNotebook do Delphi 1: as abas em cima (TabsPerRow por linha) e as paginas; cada
// pagina e um Canvas com as coordenadas do DFM (TTabPage em (5, topo))
sealed class Notebook : Canvas
{
    readonly List<(string Titulo, Canvas Pagina)> paginas = new();
    public Fonte FonteAbas = W95.Arial13;
    public IBrush CorAbas = W95.Preto;
    public int AbasPorLinha = 3;
    public double TopoPagina = 26;
    int atual;
    public event Action<int>? Mudou;
    readonly Pintor fundo;

    public Notebook() { fundo = new Pintor((dc, _) => Desenhar(dc)); Children.Add(fundo); }

    public int Atual
    {
        get => atual;
        set
        {
            atual = value;
            for (int i = 0; i < paginas.Count; i++) paginas[i].Pagina.IsVisible = i == value;
            fundo.InvalidateVisual();
            Mudou?.Invoke(value);
        }
    }

    public Canvas Pagina(string titulo)
    {
        var p = new Canvas { ClipToBounds = true };
        paginas.Add((titulo, p));
        Children.Add(p);
        p.IsVisible = paginas.Count - 1 == atual;
        return p;
    }

    public C Por<C>(Canvas pagina, C c, double x, double y, double w, double h) where C : Control
    {
        SetLeft(c, x); SetTop(c, y);
        c.Width = w; c.Height = h;
        pagina.Children.Add(c);
        return c;
    }

    double LarguraAba => Math.Floor((Bounds.Width - 4) / Math.Max(1, AbasPorLinha));

    protected override Size ArrangeOverride(Size tamanho)
    {
        foreach (var (_, p) in paginas)
            p.Arrange(new Rect(5, TopoPagina, Math.Max(0, tamanho.Width - 10), Math.Max(0, tamanho.Height - TopoPagina - 5)));
        foreach (var c in Children)
            if (c != fundo && !paginas.Exists(x => x.Pagina == c)) c.Arrange(new Rect(GetLeft(c), GetTop(c), c.Width, c.Height));
        fundo.Arrange(new Rect(tamanho));
        return tamanho;
    }

    void Desenhar(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        dc.FillRectangle(W95.Face, new Rect(0, 0, w, h));
        double ya = TopoPagina - 4;   // o fundo das abas
        // a moldura da pagina
        var pg = new Rect(0, ya, w, h - ya);
        W95.Linhas(dc, pg, W95.Luz, W95.Escura);
        W95.Linhas(dc, pg.Deflate(1), W95.Luz3D, W95.Sombra);
        double lw = LarguraAba;
        for (int i = 0; i < paginas.Count; i++)
        {
            bool sel = i == atual;
            double x = 2 + i * lw;
            var r = sel ? new Rect(x - 2, 0, lw + 3, ya + 2) : new Rect(x, 2, lw - 1, ya - 2);
            dc.FillRectangle(W95.Face, r);
            dc.FillRectangle(W95.Luz, new Rect(r.X, r.Y + 2, 1, r.Height - 2));
            dc.FillRectangle(W95.Luz, new Rect(r.X + 2, r.Y, r.Width - 4, 1));
            dc.FillRectangle(W95.Luz, new Rect(r.X + 1, r.Y + 1, 1, 1));
            dc.FillRectangle(W95.Escura, new Rect(r.Right - 1, r.Y + 2, 1, r.Height - 2));
            dc.FillRectangle(W95.Escura, new Rect(r.Right - 2, r.Y + 1, 1, 1));
            dc.FillRectangle(W95.Sombra, new Rect(r.Right - 2, r.Y + 2, 1, r.Height - 2));
            var t = W95.Txt(paginas[i].Titulo, FonteAbas, CorAbas);
            dc.DrawText(t, new Point(Math.Floor(r.X + (r.Width - t.Width) / 2), Math.Floor(r.Y + (r.Height - t.Height) / 2)));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        if (p.Y >= TopoPagina - 4) return;
        int i = (int)Math.Floor((p.X - 2) / LarguraAba);
        if (i >= 0 && i < paginas.Count && i != atual) Atual = i;
        e.Handled = true;
    }
}

// o fundo desenhado de um painel (o Render dos Panel e selado)
sealed class Pintor(Action<DrawingContext, Size> desenhar) : Control
{
    public override void Render(DrawingContext dc) => desenhar(dc, Bounds.Size);
}

// TListBox com BorderStyle = bsNone (a dos melhores marcadores do fim de temporada): sem
// moldura, no fundo da janela, rola com a roda e com a barra
sealed class ListaSemBorda : Control
{
    public Func<int> Quantos = () => 0;
    public Func<int, string> Texto = _ => "";
    public Fonte Fonte = W95.Arial13;
    public IBrush Cor = W95.Preto;
    public double AlturaItem = 16;
    int topo;
    const double Barra = 16;

    int Visiveis => Math.Max(1, (int)(Bounds.Height / AlturaItem));
    int MaxTopo => Math.Max(0, Quantos() - Visiveis);

    public override void Render(DrawingContext dc)
    {
        topo = Math.Clamp(topo, 0, MaxTopo);
        int n = Quantos();
        bool barra = n > Visiveis;
        var area = new Rect(0, 0, Bounds.Width - (barra ? Barra : 0), Bounds.Height);
        dc.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        using (dc.PushClip(area))
            for (int i = topo; i < n; i++)
            {
                double y = (i - topo) * AlturaItem;
                if (y >= area.Bottom) break;
                var t = W95.Txt(Texto(i), Fonte, Cor);
                dc.DrawText(t, new Point(2, y + Math.Floor((AlturaItem - t.Height) / 2)));
            }
        if (!barra) return;
        double x = Bounds.Width - Barra;
        var cima = new Rect(x, 0, Barra, Barra);
        var baixo = new Rect(x, Bounds.Height - Barra, Barra, Barra);
        var trilho = new Rect(x, Barra, Barra, Math.Max(0, Bounds.Height - 2 * Barra));
        dc.FillRectangle(W95.Trilho, trilho);
        foreach (var (r, paraBaixo) in new[] { (cima, false), (baixo, true) })
        {
            dc.FillRectangle(W95.Face, r);
            W95.Relevo(dc, r, false);
            W95.Seta(dc, r.X + 8, r.Y + 8, paraBaixo, W95.Preto);
        }
        double hc = Math.Max(8, Math.Floor(trilho.Height * Visiveis / Math.Max(1, n)));
        double yc = trilho.Y + (MaxTopo == 0 ? 0 : Math.Floor((trilho.Height - hc) * topo / MaxTopo));
        var cur = new Rect(x, yc, Barra, hc);
        dc.FillRectangle(W95.Face, cur);
        W95.Relevo(dc, cur, false);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        if (Quantos() <= Visiveis || p.X < Bounds.Width - Barra) return;
        topo += p.Y < Bounds.Height / 2 ? -1 : 1;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        topo -= Math.Sign(e.Delta.Y) * 3;
        InvalidateVisual();
        e.Handled = true;
    }
}
