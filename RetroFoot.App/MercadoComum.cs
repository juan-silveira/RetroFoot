using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;
using Mem = RetroFoot.Motor.Partida.Memoria;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// O que as janelas do mercado (leilao, pedido de aumento, venda e renovacao de contrato)
// tem em comum: os textos do jogo (portugues de Portugal com a traducao do launcher,
// game/IDIOMAS/pt-BR.txt), as cores das equipas, o dinheiro por extenso e o TEdit/TSpinEdit
// desenhados como os do Wine (W95, TelaInicio.cs).
static class TextosMercado
{
    // DS:1528 + 71 x motivo (3:5a6a), com a traducao do launcher
    public static readonly string[] Motivos =
    {
        "",
        "Não pode vender mais jogadores",
        "Este jogador já está vendido",
        "Este jogador só pode ser vendido na próxima temporada",
        "Só pode tentar vender este jogador novamente no próximo jogo",
        "Não pode vender mais goleiros",
        "Não pode vender mais jogadores de campo",
    };

    // DS:18a8 + 21 x posicao (Guarda-redes, Defesa, Medio, Avancado no original)
    public static readonly string[] Posicoes = { "Goleiro", "Defensor", "Meia", "Atacante" };
    // DS:171a + 12 x comportamento
    public static readonly string[] Comportamentos = { "Fair play", "Cordeirinho", "Cavalheiro", "Caneleiro", "Caceteiro", "Sarrafeiro" };

    public static string Texto(byte[] b) => b.Length == 0 ? "" : Encoding.Latin1.GetString(Cifra.TextoEFT(b).Texto);
    public static string NomeJogador(SJogador p) => Encoding.Latin1.GetString(RetroFoot.Motor.Partida.Jornada.NomeNaTela(p));
    public static string NomeEquipa(SEquipa? e) => e == null ? "" : Texto(e.NomeCurto);
    public static string PaisDe(byte[] b) => Texto(b).ToUpperInvariant();
    public static string NomeTreinador(Treinador? t) => t == null ? "" : Texto(t.Nome);
    public static string Comportamento(SJogador p) =>
        p.Comportamento < Comportamentos.Length ? T(Comportamentos[p.Comportamento]) : p.Comportamento.ToString();
    public static string Posicao(SJogador p) => p.Posicao < Posicoes.Length ? T(Posicoes[p.Posicao]) : "";

    // TColor $00BBGGRR; as cores do sistema (bit de cima) nao aparecem nas equipas
    public static IBrush CorDelphi(byte[] b, IBrush padrao) =>
        b.Length < 4 || (b[3] & 0x80) != 0 ? padrao : new ImmutableSolidColorBrush(Color.FromRgb(b[0], b[1], b[2]));

    // CorLetra (+0x42) e CorFundo (+0x46) da equipa
    public static (IBrush Letra, IBrush Fundo) Cores(SEquipa? e) =>
        e == null ? (W95.Preto, W95.Branco) : (CorDelphi(e.CorLetra, W95.Preto), CorDelphi(e.CorFundo, W95.Branco));

    static Dictionary<string, string>? paises;
    public static string NomePais(string cod)
    {
        if (paises == null)
        {
            paises = new();
            try
            {
                using var r = new StreamReader(AssetLoader.Open(new Uri("avares://RetroFoot.App/Assets/paises.tsv")));
                for (string? l; (l = r.ReadLine()) != null;)
                {
                    var p = l.Split('\t');
                    if (p.Length >= 2) paises[p[0].Trim().ToUpperInvariant()] = p[1].Trim();
                }
            }
            catch { }
        }
        return paises.TryGetValue(cod, out var n) ? n : cod;
    }

    // as bandeiras do app (redondas), centradas no quadro do TImage
    public static void Bandeira(DrawingContext dc, string pais, Rect r)
    {
        var b = W95.Imagem($"Bandeiras/{pais}.png");
        if (b == null) return;
        double lado = Math.Min(r.Width, r.Height);
        dc.DrawImage(b, new Rect(r.X + (r.Width - lado) / 2, r.Y + (r.Height - lado) / 2, lado, lado));
    }

    // o historial de 4 bytes; os 2 de cima das lesoes e das expulsoes guardam os amarelos do
    // launcher (cartoes.asm): so os 2 de baixo
    public static int Hist(SJogador p, int qual)
    {
        int v = Mem.Historial(p, qual);
        return qual == Mem.HistVermelhos || qual == Mem.HistLesoes ? v & 0xFFFF : v;
    }

    public static int Amarelos(SJogador p) => Campos.JgB(p, Campos.JgAmarelosAcumulados);

    // Val do Turbo Pascal: espacos a esquerda, sinal e digitos ate o fim; "" ou lixo = erro
    public static bool Val(string s, out int v)
    {
        s = s.TrimStart(' ');
        return int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v);
    }
}

// TEdit (e o texto do TSpinEdit): Latin-1, MaxLength, CharCase, PasswordChar
sealed class CampoMercado : Control
{
    string texto = "";
    int cursor;
    public int MaxLen = 255;
    public bool Maiusculas;
    public char Senha;            // PasswordChar ('X' nas ofertas escondidas, +0x1E do treinador)
    public Fonte Fonte = W95.Arial13b;
    public IBrush Cor = W95.Preto;
    public bool SemCaixa;          // dentro do TSpinEdit: so o texto
    public event Action? Mudou;

    public CampoMercado()
    {
        Focusable = true;
        FocusAdorner = null;
        Cursor = new Cursor(StandardCursorType.Ibeam);
    }

    public string Texto
    {
        get => texto;
        set { texto = value; cursor = texto.Length; InvalidateVisual(); }
    }

    string Visivel => Senha != 0 ? new string(Senha, texto.Length) : texto;

    public override void Render(DrawingContext dc)
    {
        bool m = Visual.Moderno;
        var f = m ? MM.Fonte(Fonte) : Fonte;
        var cor = m ? MM.Texto : Cor;
        Rect dentro;
        if (m)
        {
            // o campo moderno: arredondado, a borda azul com o foco
            if (!SemCaixa) MM.Cartao_(dc, new Rect(Bounds.Size), MM.Campo, 6, IsFocused ? MM.Azul : MM.Borda);
            dentro = SemCaixa ? new Rect(Bounds.Size) : new Rect(Bounds.Size).Deflate(new Thickness(6, 2));
        }
        else
        {
            dentro = SemCaixa ? new Rect(Bounds.Size) : W95.Caixa(dc, new Rect(Bounds.Size), W95.Branco);
            if (SemCaixa) dc.FillRectangle(W95.Branco, dentro);
        }
        using (dc.PushClip(dentro))
        {
            var v = Visivel;
            double x = dentro.X + 1;
            double y = m ? dentro.Y + Math.Floor((dentro.Height - W95.Txt("Ag", f, cor).Height) / 2)
                : dentro.Y + Math.Min(1, Math.Floor((dentro.Height - W95.Txt("Ag", f, cor).Height) / 2));
            W95.Escreve(dc, v, f, cor, x, y);
            if (IsFocused)
            {
                double cx = x + W95.Txt(v[..cursor], f, cor).WidthIncludingTrailingWhitespace;
                dc.FillRectangle(m ? MM.Azul : W95.Preto, new Rect(Math.Round(cx), dentro.Y + (m ? 4 : 1), 1, dentro.Height - (m ? 8 : 2)));
            }
        }
    }

    protected override void OnGotFocus(FocusChangedEventArgs e) { base.OnGotFocus(e); InvalidateVisual(); }
    protected override void OnLostFocus(FocusChangedEventArgs e) { base.OnLostFocus(e); InvalidateVisual(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        double x = e.GetPosition(this).X - 3;
        var v = Visivel;
        cursor = v.Length;
        for (int i = 0; i <= v.Length; i++)
            if (W95.Txt(v[..i], Visual.Moderno ? MM.Fonte(Fonte) : Fonte, Cor).WidthIncludingTrailingWhitespace >= x) { cursor = i; break; }
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (string.IsNullOrEmpty(e.Text)) return;
        var s = new StringBuilder();
        foreach (var c0 in e.Text)
        {
            var c = Maiusculas ? char.ToUpperInvariant(c0) : c0;
            if (c < 32 || c > 255) continue;
            if (texto.Length + s.Length >= MaxLen) break;
            s.Append(c);
        }
        if (s.Length == 0) return;
        texto = texto.Insert(cursor, s.ToString());
        cursor += s.Length;
        e.Handled = true;
        InvalidateVisual();
        Mudou?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        bool mudou = false;
        switch (e.Key)
        {
            case Key.Back when cursor > 0: texto = texto.Remove(--cursor, 1); mudou = true; break;
            case Key.Delete when cursor < texto.Length: texto = texto.Remove(cursor, 1); mudou = true; break;
            case Key.Left: cursor = Math.Max(0, cursor - 1); break;
            case Key.Right: cursor = Math.Min(texto.Length, cursor + 1); break;
            case Key.Home: cursor = 0; break;
            case Key.End: cursor = texto.Length; break;
            case Key.Back or Key.Delete: break;
            default: return;
        }
        e.Handled = true;
        InvalidateVisual();
        if (mudou) Mudou?.Invoke();
    }
}

// TSpinEdit (Samples do Delphi 1): o texto e os dois botoes do TSpinButton a direita.
// Value: StrToInt(Text), ou MinValue se nao for numero, preso entre Min e Max.
sealed class SpinMercado : Canvas
{
    public readonly CampoMercado Campo;
    public int Min, Max, Passo = 1;
    int? apertado;            // 0 cima, 1 baixo
    const double LB = 15;     // largura do TSpinButton

    public SpinMercado(double w, double h, Fonte f)
    {
        Width = w; Height = h;
        Children.Add(new Fundo(this) { Width = w, Height = h });
        Campo = new CampoMercado { Fonte = f, SemCaixa = true, MaxLen = 6, Width = w - 4 - LB, Height = h - 4 };
        SetLeft(Campo, 2); SetTop(Campo, 2);
        Children.Add(Campo);
        if (Visual.Moderno)
        {
            Campo.Width = w - 12 - LB; Campo.Height = h - 6;
            SetLeft(Campo, 8); SetTop(Campo, 3);
            Campo.GotFocus += (_, _) => Children[0].InvalidateVisual();
            Campo.LostFocus += (_, _) => Children[0].InvalidateVisual();
        }
    }

    public int Valor
    {
        get
        {
            int v = int.TryParse(Campo.Texto.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x) ? x : Min;
            if (Max != Min) v = Math.Clamp(v, Min, Max);
            return v;
        }
        set
        {
            if (Max != Min) value = Math.Clamp(value, Min, Max);
            Campo.Texto = value.ToString(CultureInfo.InvariantCulture);
        }
    }

    Rect Botao(int i)
    {
        double x = Width - 2 - LB, h = (Height - 4) / 2;
        return new Rect(x, 2 + i * h, LB, h);
    }

    // o Render do Panel e selado: a caixa e os botoes num filho por baixo do texto
    sealed class Fundo(SpinMercado s) : Control
    {
        public override void Render(DrawingContext dc) => s.Desenhar(dc);
    }

    void Desenhar(DrawingContext dc)
    {
        if (Visual.Moderno)
        {
            // o campo arredondado e as duas setas em cima e embaixo, sem relevo
            MM.Cartao_(dc, new Rect(0, 0, Width, Height), MM.Campo, 6, Campo.IsFocused ? MM.Azul : MM.Borda);
            for (int i = 0; i < 2; i++)
            {
                var r = Botao(i);
                if (apertado == i) MM.Cartao_(dc, r.Deflate(1), MM.BotaoBaixo, 4);
                double cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                double s = i == 0 ? -1 : 1;
                var pen = new Pen(MM.Fraco, 1.6, lineCap: PenLineCap.Round);
                dc.DrawLine(pen, new Point(cx - 4, cy - 2 * s), new Point(cx, cy + 2 * s));
                dc.DrawLine(pen, new Point(cx, cy + 2 * s), new Point(cx + 4, cy - 2 * s));
            }
            return;
        }
        W95.Caixa(dc, new Rect(0, 0, Width, Height), W95.Branco);
        for (int i = 0; i < 2; i++)
        {
            var r = Botao(i);
            dc.FillRectangle(W95.Face, r);
            var d = W95.Relevo(dc, r, apertado == i);
            double o = apertado == i ? 1 : 0;
            W95.Seta(dc, Math.Floor(d.X + d.Width / 2) + o, Math.Floor(d.Y + d.Height / 2) + o, i == 1, W95.Preto);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        for (int i = 0; i < 2; i++)
            if (Botao(i).Contains(p))
            {
                apertado = i;
                Valor += i == 0 ? Passo : -Passo;
                Campo.Focus();
                e.Handled = true;
                Children[0].InvalidateVisual();
                return;
            }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (apertado == null) return;
        apertado = null;
        Children[0].InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (e.Key == Key.Up) { Valor += Passo; e.Handled = true; }
        else if (e.Key == Key.Down) { Valor -= Passo; e.Handled = true; }
    }
}

// um rotulo desenhado com as fontes do W95 (TLabel)
sealed class TextoMercado : Control
{
    public string Texto = "";
    public Fonte Fonte = W95.Arial13;
    public IBrush Cor = W95.Preto;
    public bool Quebra;          // WordWrap
    public bool Centro;          // taCenter

    public TextoMercado() { IsHitTestVisible = false; ClipToBounds = true; }

    public override void Render(DrawingContext dc)
    {
        if (Texto.Length == 0) return;
        var t = Visual.Moderno ? W95.Txt(Texto, MM.Fonte(Fonte), MM.Cor(Cor)) : W95.Txt(Texto, Fonte, Cor);
        if (Quebra || Centro)
        {
            t.MaxTextWidth = Math.Max(1, Bounds.Width);
            t.TextAlignment = Centro ? TextAlignment.Center : TextAlignment.Left;
        }
        dc.DrawText(t, new Point(0, 0));
    }
}

// a base dos dialogos do mercado: a Janela do W95 (moldura e titulo do Wine), o foco, Enter
// e Esc. Fica no centro do quadro de 800x600 da Sessao (AbrirDialogo/EsperarDialogo).
abstract class DialogoMercado : UserControl
{
    protected readonly Janela J;
    protected Action? AoEnter, AoEsc;
    protected Control? Foco;

    protected readonly bool Moderno = Visual.Moderno;

    protected DialogoMercado(string titulo, double cw, double ch, IBrush fundo, bool fechar = true, Avalonia.Media.Imaging.Bitmap? logo = null)
    {
        J = new Janela(titulo, cw, ch, fundo);
        if (Moderno) MM.Vestir(J, titulo, fechar, logo);
        else if (!fechar)
        {
            // BorderIcons = []: sem o botao de fechar no titulo
            var tampa = new Tampa { Width = 20, Height = 25 };
            Canvas.SetLeft(tampa, J.Width - 3 - 20);
            Canvas.SetTop(tampa, 3);
            J.Children.Add(tampa);
        }
        Content = J;
        Width = J.Width;
        Height = J.Height;
        Focusable = true;
        FocusAdorner = null;
    }

    sealed class Tampa : Control
    {
        public override void Render(DrawingContext dc) => dc.FillRectangle(W95.TituloCor, new Rect(Bounds.Size));
    }

    protected C Por<C>(C c, double x, double y, double w, double h) where C : Control
    {
        J.Por(c, x, y, w, h);
        if (Moderno && c is Botao b) MM.Cobrir(b, J.Cliente);
        return c;
    }

    protected TextoMercado Rotulo(string s, double x, double y, double w, double h, Fonte f, IBrush cor) =>
        Por(new TextoMercado { Texto = s, Fonte = f, Cor = cor }, x, y, w, h);

    protected static Botao BotaoGlifo(string texto, string imagem, Fonte f)
    {
        // so o glifo normal (estes botoes nunca ficam desabilitados)
        return new Botao { Texto = texto, Fonte = f, Glifo = W95.Imagem($"Imagens/{imagem}.png"), NumGlifos = 1, GlifoEmCima = true };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Avalonia.Threading.Dispatcher.UIThread.Post(Focar, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    protected void Focar()
    {
        if (Foco != null && Foco.IsVisible && Foco.Focusable) Foco.Focus();
        else Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (e.Key is Key.Enter or Key.Return && AoEnter != null) { AoEnter(); e.Handled = true; }
        else if (e.Key == Key.Escape && AoEsc != null) { AoEsc(); e.Handled = true; }
    }

    // um clique fora dos controles devolve o foco ao campo (como o modal do Windows)
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.Handled) Focar();
    }
}
