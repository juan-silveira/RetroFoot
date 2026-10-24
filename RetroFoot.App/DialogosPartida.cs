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
using static RetroFoot.App.Idioma;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Save;
using Mem = RetroFoot.Motor.Partida.Memoria;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;

namespace RetroFoot.App;

// O que os dialogos da partida (substituicao, lesao, penalti, penaltis, sorteio) tem em comum:
// fontes, cores das equipas, nomes, os textos em pt-BR (os do IDIOMAS/pt-BR.txt do launcher) e
// os controles do Delphi que o kit W95 da TelaInicio nao tem (TCheckBox, TGroupBox, a lista
// sem borda dos lances e um TLabel com italico).
static class P98
{
    static readonly FontFamily MsSans = new("avares://RetroFoot.App/Assets/Fontes#MS Sans Serif");
    static readonly FontFamily Arial = new("avares://RetroFoot.App/Assets/Fontes#Liberation Sans");
    static readonly FontFamily Times = new("avares://RetroFoot.App/Assets/Fontes#Liberation Serif");

    public static readonly Fonte MsSans11 = new(MsSans, 11, false), MsSans11b = new(MsSans, 11, true),
        Arial9 = new(Arial, 9, false), Arial9b = new(Arial, 9, true), Arial11 = new(Arial, 11, false),
        Arial11b = new(Arial, 11, true), Arial16 = new(Arial, 16, false), Arial16b = new(Arial, 16, true),
        Arial48b = new(Arial, 48, true);
    public static FontFamily FamTimes => Times;
    public static FontFamily FamArial => Arial;

    // TColor $00BBGGRR; as cores do sistema (byte de cima com o bit 7) nao aparecem nas equipas
    public static IBrush Cor(byte[] b, IBrush padrao) =>
        b.Length < 4 || (b[3] & 0x80) != 0 ? padrao : new ImmutableSolidColorBrush(Color.FromRgb(b[0], b[1], b[2]));

    public static IBrush Letra(SEquipa? e) => e == null ? W95.Preto : Cor(e.CorLetra, W95.Preto);
    public static IBrush Fundo(SEquipa? e) => e == null ? W95.Branco : Cor(e.CorFundo, W95.Branco);

    public static string Texto(byte[] b) => b.Length == 0 ? "" : Encoding.Latin1.GetString(Cifra.TextoEFT(b).Texto);

    // o nome curto (TTeamAttr +0x2D, string[20])
    public static string NomeEquipa(SEquipa? e)
    {
        if (e == null) return "";
        var s = Texto(e.NomeCurto);
        return s.Length > 20 ? s[..20] : s;
    }

    // seg12:13fe: o nome com o "*" da estrela
    public static string NomeJogador(SJogador p) => Encoding.Latin1.GetString(Jornada.NomeNaTela(p));

    // seg12:1462: so o nome (string[20]), sem a estrela
    public static string NomeSimples(SJogador p)
    {
        var s = Texto(p.NomeCifrado);
        return s.Length > 20 ? s[..20] : s;
    }

    // DS:18A8 (string[20] por posicao), no idioma escolhido
    static readonly string[] PosicoesOriginal = { "Guarda-redes", "Defesa", "Médio", "Avançado" };
    public static string Posicao(int p) => T(PosicoesOriginal[Math.Clamp(p, 0, 3)]);

    // DS:177A (string[10] por resultado de ResultadoPenalti; 0 = "Nao marcad"), no idioma escolhido
    static readonly string[] Resultados = { "Não marcad", "GOLO", "Defendeu", "Ao poste", "À barra", "Ao lado", "Por cima" };

    public static string Resultado(int r) => r >= 0 && r < Resultados.Length ? T(Resultados[r]) : "";

    static Dictionary<string, string>? _paises;
    public static string NomePais(string cod)
    {
        if (_paises == null)
        {
            _paises = new();
            try
            {
                using var r = new StreamReader(AssetLoader.Open(new Uri("avares://RetroFoot.App/Assets/paises.tsv")));
                for (string? l; (l = r.ReadLine()) != null;)
                {
                    var p = l.Split('\t');
                    if (p.Length >= 2) _paises[p[0].Trim().ToUpperInvariant()] = p[1].Trim();
                }
            }
            catch { }
        }
        return _paises.TryGetValue(cod.ToUpperInvariant(), out var n) ? n : cod;
    }

    public static FormattedText Txt(string s, FontFamily f, double tam, IBrush cor, bool negrito, bool italico = false) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(f, italico ? FontStyle.Italic : FontStyle.Normal, negrito ? FontWeight.Bold : FontWeight.Normal), tam * Acessibilidade.Texto, cor);

    public static FormattedText Txt(string s, Fonte f, IBrush cor) => W95.Txt(s, f, cor);

    // TPlayer.Formatar com 0x43 (seg03:76a3) desenhado por 14:0ad6: a posicao (15 pontos), o
    // nome em negrito (120) e a forca a direita em 20, tudo a 2 pontos da borda; o escolhido
    // com as cores trocadas (fundo na cor da letra)
    public static void LinhaJogador(DrawingContext dc, SJogador p, Rect r, Fonte f, IBrush letra, IBrush fundo, bool escolhido)
    {
        var cor = letra;
        if (escolhido)
        {
            dc.FillRectangle(letra, r);
            cor = fundo;
        }
        double x = r.X + 2;
        var fb = f with { Negrito = true };
        double y = r.Y + Math.Floor((r.Height - W95.Txt("Ag", f, cor).Height) / 2);
        W95.Escreve(dc, Idioma.Posicao(Math.Min((int)p.Posicao, 3)), f, cor, x, y);
        x += 15;
        using (dc.PushClip(new Rect(x, r.Y, 120, r.Height))) W95.Escreve(dc, NomeJogador(p), fb, cor, x, y);
        x += 120;
        var tf = W95.Txt(p.Forca.ToString(), fb, cor);
        dc.DrawText(tf, new Point(x + 20 - tf.Width, y));
    }

    // a lista de jogadores de um dialogo, nas cores da equipa (Color = fundo, Font.Color = letra)
    public static Lista ListaJogadores(List<SJogador> l, SEquipa e, Fonte f, double alturaItem)
    {
        var letra = Letra(e); var fundo = Fundo(e);
        var lista = new Lista { AlturaItem = alturaItem, Fundo = fundo };
        lista.Quantos = () => l.Count;
        lista.Desenhar = (dc, i, r, esc) => { if (i < l.Count) LinhaJogador(dc, l[i], r, f, letra, fundo, esc); };
        return lista;
    }

    // a bolha de 14:0e0d com 11:1826 numa lista: G, D, M, A e o nome mostrado
    public static void OrdenarPorPosicaoNome(List<SJogador> l)
    {
        int n = l.Count;
        for (int i = 1; i <= n - 1; i++)
            for (int j = n - 1; j >= i; j--)
                if (!PorPosicaoNome(l[j - 1], l[j])) (l[j - 1], l[j]) = (l[j], l[j - 1]);
    }

    static bool PorPosicaoNome(SJogador a, SJogador b)
    {
        if (a.Posicao != b.Posicao) return a.Posicao < b.Posicao;
        return Jornada.NomeNaTela(a).AsSpan().SequenceCompareTo(Jornada.NomeNaTela(b)) < 0;
    }

    // +0x17 do jogador (historial, jogos: 32 bits)
    public static void SomaJogo(SJogador p)
    {
        uint v = BitConverter.ToUInt32(p.Historial, 0) + 1;
        BitConverter.GetBytes(v).CopyTo(p.Historial, 0);
        RetroFoot.Motor.Partida.Memoria.SomaJogoNaTemporada(p);
    }

    public static bool Humano(JogoAtual jg, int lado) => Mem.Humano(jg.Treinador[lado]);
}

// TLabel com italico e alinhamento; Fundo = null e transparente
sealed class Etiqueta : Control
{
    public string Texto = "";
    public FontFamily Familia = P98.FamArial;
    public double Tamanho = 11;
    public bool Negrito, Italico;
    public IBrush Cor = W95.Preto;
    public IBrush? Fundo;
    public TextAlignment Alin = TextAlignment.Left;
    public bool Quebra = true;

    public Etiqueta() { IsHitTestVisible = false; }

    public void Mudar(string s) { Texto = s; InvalidateVisual(); }

    public override void Render(DrawingContext dc)
    {
        var r = new Rect(Bounds.Size);
        if (Fundo != null) dc.FillRectangle(Fundo, r);
        if (Texto.Length == 0) return;
        var t = P98.Txt(Texto.Replace('\r', '\n'), Familia, Tamanho, Cor, Negrito, Italico);
        t.MaxTextWidth = r.Width;
        t.TextAlignment = Alin;
        if (!Quebra) t.MaxLineCount = 1;
        using (dc.PushClip(r)) dc.DrawText(t, new Point(0, 0));
    }
}

// TCheckBox (CB_Paint do Wine: a caixa de 13x13 e o texto a 13 + largura("0")/2 + 1)
sealed class Caixinha : Control
{
    public string Texto = "";
    public Fonte Fonte = P98.MsSans11;
    public IBrush Cor = W95.Preto;
    bool marcada;
    public event Action? Mudou;

    public bool Marcada { get => marcada; set { marcada = value; InvalidateVisual(); } }

    public Caixinha() { Cursor = new Cursor(StandardCursorType.Arrow); }

    public override void Render(DrawingContext dc)
    {
        double h = Bounds.Height;
        double y0 = Math.Floor((h - 13) / 2);
        var c = new Rect(0, y0, 13, 13);
        dc.FillRectangle(W95.Branco, c);
        W95.Linhas(dc, c, W95.Sombra, W95.Luz);
        W95.Linhas(dc, c.Deflate(1), W95.Escura, W95.Luz3D);
        if (marcada)
        {
            // o visto do Wine: 7 colunas, 3 pontos de altura
            double x = c.X + 3, y = c.Y + 5;
            for (int i = 0; i < 7; i++)
            {
                double dy = i < 3 ? i : 4 - i;
                dc.FillRectangle(W95.Preto, new Rect(x + i, y + dy, 1, 3));
            }
        }
        var t = W95.Txt(Texto, Fonte, Cor);
        double tx = 13 + Math.Floor(W95.Txt("0", Fonte, Cor).Width / 2) + 1;
        dc.DrawText(t, new Point(tx, Math.Floor((h - t.Height) / 2)));
    }

    // o original reage no OnMouseUp (seg02:2881, 28ab)
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!new Rect(Bounds.Size).Contains(e.GetPosition(this))) return;
        marcada = !marcada;
        InvalidateVisual();
        Mudou?.Invoke();
        e.Handled = true;
    }
}

// TGroupBox com Ctl3D: o quadro gravado a meia altura da letra e o titulo por cima, a 8 pontos
sealed class GrupoPartida : Control
{
    public string Texto = "";
    public Fonte Fonte = P98.MsSans11;
    public IBrush Cor = W95.Preto;
    public IBrush Fundo = W95.Face;

    public GrupoPartida() { IsHitTestVisible = false; }

    public void Mudar(string s) { Texto = s; InvalidateVisual(); }

    public override void Render(DrawingContext dc)
    {
        var r = new Rect(Bounds.Size);
        dc.FillRectangle(Fundo, r);
        var t = W95.Txt(Texto, Fonte, Cor);
        double meio = Math.Floor(t.Height / 2);
        var q = new Rect(0, meio, r.Width, r.Height - meio);
        W95.Linhas(dc, new Rect(q.X, q.Y, q.Width - 1, q.Height - 1), W95.Sombra, W95.Sombra);
        W95.Linhas(dc, new Rect(q.X + 1, q.Y + 1, q.Width - 1, q.Height - 1), W95.Luz, W95.Luz);
        if (Texto.Length == 0) return;
        dc.FillRectangle(Fundo, new Rect(6, 0, t.Width + 4, t.Height));
        dc.DrawText(t, new Point(8, 0));
    }
}

// TListBox de textos sem borda (BorderStyle = bsNone, ParentColor), com a barra de rolagem
// quando nao cabe: a lista de lances da janela de substituicao
sealed class ListaTexto : Control
{
    public readonly List<string> Itens = new();
    public Fonte Fonte = P98.MsSans11;
    public IBrush Cor = W95.Preto;
    public IBrush Fundo = W95.Face;
    public double AlturaItem = 13;
    int topo;
    const double Barra = 16;

    int Visiveis => Math.Max(1, (int)(Bounds.Height / AlturaItem));
    int MaxTopo => Math.Max(0, Itens.Count - Visiveis);

    public void Mudou() { topo = Math.Clamp(topo, 0, MaxTopo); InvalidateVisual(); }

    public override void Render(DrawingContext dc)
    {
        var r = new Rect(Bounds.Size);
        dc.FillRectangle(Fundo, r);
        topo = Math.Clamp(topo, 0, MaxTopo);
        bool barra = Itens.Count > Visiveis;
        var area = barra ? new Rect(0, 0, r.Width - Barra, r.Height) : r;
        using (dc.PushClip(area))
            for (int i = topo; i < Itens.Count; i++)
            {
                double y = (i - topo) * AlturaItem;
                if (y >= area.Bottom) break;
                W95.Escreve(dc, Itens[i], Fonte, Cor, 2, y);
            }
        if (!barra) return;
        var cima = new Rect(r.Right - Barra, 0, Barra, Barra);
        var baixo = new Rect(r.Right - Barra, r.Bottom - Barra, Barra, Barra);
        var trilho = new Rect(r.Right - Barra, Barra, Barra, Math.Max(0, r.Height - 2 * Barra));
        dc.FillRectangle(W95.Trilho, trilho);
        foreach (var (b, paraBaixo) in new[] { (cima, false), (baixo, true) })
        {
            dc.FillRectangle(W95.Face, b);
            W95.Relevo(dc, b, false);
            W95.Seta(dc, b.X + 8, b.Y + 8, paraBaixo, W95.Preto);
        }
        double h = Math.Max(8, Math.Floor(trilho.Height * Visiveis / Math.Max(1, Itens.Count)));
        double cy = trilho.Y + (MaxTopo == 0 ? 0 : Math.Floor((trilho.Height - h) * topo / MaxTopo));
        var cur = new Rect(trilho.X, cy, Barra, h);
        dc.FillRectangle(W95.Face, cur);
        W95.Relevo(dc, cur, false);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        if (Itens.Count <= Visiveis || p.X < Bounds.Width - Barra) return;
        topo = Math.Clamp(p.Y < Bounds.Height / 2 ? topo - 1 : topo + 1, 0, MaxTopo);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        topo = Math.Clamp(topo - Math.Sign(e.Delta.Y) * 3, 0, MaxTopo);
        InvalidateVisual();
        e.Handled = true;
    }
}
