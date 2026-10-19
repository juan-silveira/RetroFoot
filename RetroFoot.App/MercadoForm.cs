using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Mercado;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// A base das janelas do mercado (Lista de Jogadores, Plantel, Observados): um formulario
// do Delphi desenhado a mao como a TelaEquipa desenha, nas posicoes do DFM (versao em Go: 
// assets/formularios/*.json). A janela tem a moldura do Wine (borda de 3, titulo de 25) e
// e aberta pela Sessao.AbrirDialogo no centro de um quadro de 800x600 ampliado.
//
// Os componentes (botoes com imagem, caixas de escolha, caixas de marcar, listas com barra
// de rolagem) sao objetos simples, em coordenadas da area cliente; o texto do TEdit e uma
// TextBox de verdade por cima (o teclado do celular aparece).
internal abstract class MercadoForm : Control
{
    // ------------------------------------------------------------ medidas
    protected const double Borda = 3, TituloA = 25;
    protected double CL { get; }
    protected double CA { get; }
    protected static readonly Point Origem = new(Borda, Borda + TituloA);

    protected readonly Sessao S;
    protected string Titulo = "";
    protected Bitmap? LogoTitulo;
    protected IBrush FundoCliente = Face;
    protected readonly List<Comp> Comps = new();
    // a raiz que vai para a Sessao: o desenho e, por cima, as caixas de texto
    readonly Canvas _raiz;
    bool _fechado;
    public event Action? Fechou;

    protected MercadoForm(Sessao s, double clienteL, double clienteA)
    {
        S = s;
        CL = clienteL; CA = clienteA;
        Width = clienteL + 2 * Borda;
        Height = clienteA + TituloA + 2 * Borda;
        ClipToBounds = true;
        Focusable = true;
        _raiz = new Canvas { Width = Width, Height = Height };
        _raiz.Children.Add(this);
    }

    public Control Raiz => _raiz;

    // abre na Sessao (na thread da interface)
    public void Mostrar()
    {
        S.AbrirDialogo(_raiz);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => (_foco ?? (Control)this).Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    public void Fechar()
    {
        if (_fechado) return;
        _fechado = true;
        S.FecharDialogo(_raiz);
        Fechou?.Invoke();
    }

    Control? _foco;

    // ------------------------------------------------------------ fontes e cores
    protected static readonly FontFamily Arial = new("avares://RetroFoot.App/Assets/Fontes#Liberation Sans");
    protected static readonly FontFamily SansSerif = new("avares://RetroFoot.App/Assets/Fontes#MS Sans Serif");
    static readonly IBrush _face = Pincel(245, 245, 245), _branco = Brushes.White, _preto = Brushes.Black;
    protected static readonly IBrush Sombra = Pincel(166, 166, 166), Escura = Pincel(106, 106, 106), Luz3D = Pincel(227, 227, 227);
    static readonly IBrush _cinza = Pincel(128, 128, 128), _destaque = Pincel(50, 150, 250);
    protected static readonly IBrush CorTitulo = Pincel(50, 150, 250);
    static readonly IBrush _amarelo = Pincel(255, 255, 0), _azul = Pincel(0, 0, 255), _vermelho = Pincel(255, 0, 0);

    // no visual Moderno (Visual.cs, ModernoMercado.cs) as mesmas cores no tema verde: a
    // face e o fundo amarelo viram o cartao, o branco dos campos o campo verde, o preto o
    // texto claro, o azul dos avisos o amarelo
    protected static bool Moderno => Visual.Moderno;
    protected static IBrush Face => Moderno ? MM.Cartao : _face;
    protected static IBrush Branco => Moderno ? MM.Campo : _branco;
    protected static IBrush Preto => Moderno ? MM.Texto : _preto;
    protected static IBrush Cinza => Moderno ? MM.Apagado : _cinza;
    protected static IBrush Destaque => Moderno ? MM.Azul : _destaque;
    protected static IBrush Amarelo => Moderno ? MM.Cartao : _amarelo;
    protected static IBrush Azul => Moderno ? MM.Azul : _azul;
    protected static IBrush Vermelho => Moderno ? MM.Vermelho : _vermelho;
    // o texto sobre o Destaque
    protected static IBrush SobreDestaque => Moderno ? MM.SobreAzul : _branco;

    // o fundo do item escolhido de uma lista; devolve a cor do texto dele
    protected static IBrush MarcarEscolhido(DrawingContext dc, Rect r)
    {
        if (Moderno) { MM.Escolhida(dc, r.Deflate(new Thickness(1, 0))); return MM.TextoEscolhido; }
        dc.FillRectangle(_destaque, r);
        return _branco;
    }

    protected static IBrush Pincel(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));

    // ------------------------------------------------------------ dados comuns
    protected static string Texto(byte[] b) => MercadoJogo.Texto(b);
    protected static string NomeJogador(SJogador p) => Encoding.Latin1.GetString(RetroFoot.Motor.Partida.Jornada.NomeNaTela(p));
    protected static string PaisDe(byte[] b) => Texto(b).ToUpperInvariant();

    protected static string Pascal(byte[] b, int max)
    {
        if (b.Length == 0) return "";
        int n = Math.Min(Math.Min((int)b[0], max), b.Length - 1);
        return Encoding.Latin1.GetString(b, 1, n);
    }

    // TColor: $00BBGGRR; as cores do sistema (bit de cima) nao aparecem nas equipas
    protected static IBrush CorDelphi(byte[] b, IBrush padrao) =>
        b.Length < 4 || (b[3] & 0x80) != 0 ? padrao : new SolidColorBrush(Color.FromRgb(b[0], b[1], b[2]));

    protected static (IBrush Letra, IBrush Fundo) Cores(SEquipa? e) =>
        Moderno ? (MM.Texto, MM.Cartao) :
        e == null ? (Preto, Branco) : (CorDelphi(e.CorLetra, Preto), CorDelphi(e.CorFundo, Branco));

    static List<(string Cod, string Nome)>? _paises;
    // a lista de paises (codigo, nome) de Assets/paises.tsv, na ordem do arquivo
    protected static List<(string Cod, string Nome)> Paises()
    {
        if (_paises != null) return _paises;
        _paises = new();
        try
        {
            using var r = new StreamReader(AssetLoader.Open(new Uri("avares://RetroFoot.App/Assets/paises.tsv")));
            for (string? l; (l = r.ReadLine()) != null;)
            {
                var p = l.Split('\t');
                if (p.Length >= 2) _paises.Add((p[0].Trim().ToUpperInvariant(), p[1].Trim()));
            }
        }
        catch { }
        return _paises;
    }

    protected static string NomePais(string cod)
    {
        foreach (var (c, n) in Paises()) if (c == cod) return n;
        return cod;
    }

    static readonly Dictionary<string, Bitmap?> _imagens = new();
    protected static Bitmap? Imagem(string caminho)
    {
        if (_imagens.TryGetValue(caminho, out var b)) return b;
        try { b = new Bitmap(AssetLoader.Open(new Uri("avares://RetroFoot.App/Assets/" + caminho))); }
        catch { b = null; }
        _imagens[caminho] = b;
        return b;
    }

    // as bandeiras do app (redondas), no centro do quadro
    protected static void Bandeira(DrawingContext dc, string pais, Rect r)
    {
        var b = Imagem($"Bandeiras/{pais}.png");
        if (b == null) return;
        double lado = Math.Min(r.Width, r.Height);
        dc.DrawImage(b, new Rect(r.X + (r.Width - lado) / 2, r.Y + (r.Height - lado) / 2, lado, lado));
    }

    protected static readonly string[] Comportamentos = { "Fair play", "Cordeirinho", "Cavalheiro", "Caneleiro", "Caceteiro", "Sarrafeiro" };
    protected static readonly string[] Posicoes = { "Goleiro", "Defensor", "Meia", "Atacante" };

    // o dinheiro com o simbolo da moeda do jogo (FormatoDinheiro)
    protected string Dinheiro(long v) => FormatoDinheiro.Valor(S.S, v);

    protected bool Humana(SEquipa? e) => e != null && S.TreinadorDa(e) is { Humano: not 0 };

    // ------------------------------------------------------------ texto
    protected static FormattedText Txt(string s, double tam, IBrush cor, bool negrito = false, FontFamily? fonte = null,
        bool italico = false, double largura = 0)
    {
        if (Moderno) { fonte = MM.Inter; tam = Math.Round(tam * MM.Escala, 1); }
        var t = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(fonte ?? Arial, italico ? FontStyle.Italic : FontStyle.Normal, negrito ? (Moderno ? FontWeight.SemiBold : FontWeight.Bold) : FontWeight.Normal),
            tam * Acessibilidade.Texto, cor);
        if (largura > 0) t.MaxTextWidth = largura;
        return t;
    }

    protected static double Largura(string s, double tam, bool negrito = false, FontFamily? fonte = null) =>
        Txt(s, tam, Preto, negrito, fonte).WidthIncludingTrailingWhitespace;

    protected static void Escreve(DrawingContext dc, string s, double x, double y, double tam, IBrush cor,
        bool negrito = false, FontFamily? fonte = null, bool italico = false, double largura = 0) =>
        dc.DrawText(Txt(s, tam, cor, negrito, fonte, italico, largura), new Point(x, y));

    protected static void Direita(DrawingContext dc, string s, double xFim, double y, double tam, IBrush cor,
        bool negrito = false, FontFamily? fonte = null)
    {
        var t = Txt(s, tam, cor, negrito, fonte);
        dc.DrawText(t, new Point(xFim - t.WidthIncludingTrailingWhitespace, y));
    }

    // um TLabel: o texto quebra nas palavras dentro da largura e o que passa da altura some
    protected static void Rotulo(DrawingContext dc, string s, Rect r, double tam, IBrush cor, bool negrito = false,
        FontFamily? fonte = null, IBrush? fundo = null, bool centro = false, bool direita = false)
    {
        if (fundo != null) dc.FillRectangle(fundo, r);
        using (dc.PushClip(r))
        {
            var t = Txt(s, tam, cor, negrito, fonte, largura: r.Width);
            if (centro) t.TextAlignment = TextAlignment.Center;
            else if (direita) t.TextAlignment = TextAlignment.Right;
            dc.DrawText(t, r.TopLeft);
        }
    }

    // ------------------------------------------------------------ molduras
    protected static void Moldura(DrawingContext dc, Rect r, IBrush claro, IBrush escuro)
    {
        dc.FillRectangle(claro, new Rect(r.X, r.Y, r.Width, 1));
        dc.FillRectangle(claro, new Rect(r.X, r.Y, 1, r.Height));
        dc.FillRectangle(escuro, new Rect(r.X, r.Bottom - 1, r.Width, 1));
        dc.FillRectangle(escuro, new Rect(r.Right - 1, r.Y, 1, r.Height));
    }

    protected static void Afundada(DrawingContext dc, Rect r)
    {
        if (Moderno) { MM.Cartao_(dc, r, MM.Campo, 6, MM.Borda); return; }
        Moldura(dc, r, Sombra, Branco);
        Moldura(dc, r.Deflate(1), Escura, Luz3D);
    }

    protected static void Saltada(DrawingContext dc, Rect r)
    {
        if (Moderno) { MM.Cartao_(dc, r, null, 6, MM.Borda); return; }
        Moldura(dc, r, Luz3D, Escura);
        Moldura(dc, r.Deflate(1), Branco, Sombra);
    }

    // TBevel bsFrame
    protected static void Bevel(DrawingContext dc, Rect r)
    {
        // (no Moderno so o fio: o que fica dentro, como a lista, ja tem o fundo dele)
        if (Moderno) { MM.Cartao_(dc, r, null, 10, MM.Linha); return; }
        Moldura(dc, new Rect(r.X + 1, r.Y + 1, r.Width - 1, r.Height - 1), Branco, Branco);
        Moldura(dc, new Rect(r.X, r.Y, r.Width - 1, r.Height - 1), Sombra, Sombra);
    }

    // TGroupBox: a moldura gravada e o titulo por cima
    protected static void Grupo(DrawingContext dc, Rect r, string titulo, double tam, IBrush cor, IBrush fundo)
    {
        if (Moderno)
        {
            MM.Cartao_(dc, new Rect(r.X, r.Y + 6, r.Width, r.Height - 6), MM.Barra, 8, MM.Linha);
            var tt = Txt(titulo.ToUpperInvariant(), tam - 1, MM.Fraco, true);
            dc.FillRectangle(MM.Barra, new Rect(r.X + 8, r.Y, tt.WidthIncludingTrailingWhitespace + 6, 13));
            dc.DrawText(tt, new Point(r.X + 11, r.Y));
            return;
        }
        double y = 8;
        Moldura(dc, new Rect(r.X, r.Y + y, r.Width - 1, r.Height - y - 1), Sombra, Sombra);
        Moldura(dc, new Rect(r.X + 1, r.Y + y + 1, r.Width - 1, r.Height - y - 1), Branco, Branco);
        double tw = Largura(titulo, tam);
        dc.FillRectangle(fundo, new Rect(r.X + 8, r.Y, tw + 2, 16));
        Escreve(dc, titulo, r.X + 9, r.Y, tam, cor);
    }

    // a setinha da barra de rolagem e da caixa de escolha
    protected static void Seta(DrawingContext dc, double cx, double cy, bool cima, IBrush cor)
    {
        for (int k = 0; k < 4; k++)
            dc.FillRectangle(cor, new Rect(cx - 3 + k, cima ? cy + 1 - k : cy - 2 + k, 7 - 2 * k, 1));
    }

    // TBiGauge do moral (moralNoMedidor, 4:1d5a): Round(moral x 10) de 0 a 20; a barra verde
    // acima de 1,25, vermelha abaixo de 0,65, amarela entre os dois
    protected static void MedidorMoral(DrawingContext dc, Rect r, SEquipa e, double margem)
    {
        double m = Ext.DeReal48(e.Moral).Float64();
        if (Moderno)
        {
            // a barra do moral arredondada, nas cores da jornada
            var tr = new Rect(r.X, r.Y + r.Height / 2 - 4, r.Width, 8);
            MM.Cartao_(dc, tr, MM.Linha, 4);
            double f = Math.Clamp(Math.Round(m * 10, MidpointRounding.ToEven) / 20, 0, 1);
            IBrush c = m > 1.25 ? MM.Vivo : m < 0.65 ? MM.Vermelho : MM.Amarelo;
            if (f > 0) MM.Cartao_(dc, new Rect(tr.X, tr.Y, Math.Max(8, tr.Width * f), 8), c, 4);
            return;
        }
        IBrush cor = m > 1.25 ? Pincel(0, 255, 0) : m < 0.65 ? Pincel(255, 0, 0) : Pincel(255, 255, 0);
        dc.FillRectangle(Branco, r);
        Moldura(dc, r, Sombra, Sombra);
        var dentro = r.Deflate(1);
        Moldura(dc, dentro, Sombra, Branco);
        Moldura(dc, dentro.Deflate(1), Sombra, Branco);
        var area = dentro.Deflate(2 + margem);
        double w = area.Width * Math.Clamp(Math.Round(m * 10, MidpointRounding.ToEven) / 20, 0, 1);
        if (w > 0) dc.FillRectangle(cor, new Rect(area.X, area.Y, w, area.Height));
    }

    // ------------------------------------------------------------ linha de jogador
    // TPlayer.Formatar (seg03:76a3) desenhado por 14:0ad6: cada coluna tem uma largura; o
    // codigo 6 alinha a direita e, dali em diante, cada coluna deixa 10 pontos depois.
    // Um numero alinhado a direita mais largo que a coluna (uma forca de 4 ou 5 algarismos)
    // cresce para a esquerda e o texto da coluna de antes (o nome) e cortado antes dele: a
    // coluna alinhada a esquerda so e desenhada quando se sabe a seguinte (ou no Dispose).
    protected sealed class Colunas : IDisposable
    {
        readonly DrawingContext _dc;
        readonly double _y, _alt;
        readonly IBrush _cor;
        double _x;
        bool _mais10;
        public bool Negrito;
        public double X => _x;
        // a coluna alinhada a esquerda ainda por desenhar
        (string S, double X, double W, bool Negrito)? _pendente;

        public Colunas(DrawingContext dc, Rect r, IBrush cor, bool negrito)
        {
            _dc = dc; _x = r.X + 2; _y = r.Y; _alt = r.Height; _cor = cor; Negrito = negrito;
        }

        // direita = codigo 6 (alinha a direita e liga os 10 pontos)
        public void Col(double w, string s, bool direita = false)
        {
            // o numero comeca em inicio (2 pontos de folga antes dele); o que passa da coluna
            double inicio = direita && s.Length > 0 ? _x + w - Largura(s, 11, Negrito) - 2 : double.MaxValue;
            double invade = Math.Max(0, _x - inicio);
            Desenhar(inicio);
            if (direita) _mais10 = true;
            if (s.Length > 0)
            {
                if (direita)
                    using (_dc.PushClip(new Rect(_x - invade, _y, Math.Max(w, 1) + invade, _alt)))
                        Direita(_dc, s, _x + w, _y, 11, _cor, Negrito);
                else _pendente = (s, _x, w, Negrito);
            }
            _x += w;
            if (_mais10) _x += 10;
        }

        // desenha a coluna pendente, cortada em limite (o comeco do numero que vem a seguir)
        void Desenhar(double limite)
        {
            if (_pendente is not { } p) return;
            _pendente = null;
            double larg = Math.Min(Math.Max(p.W, 1) + 10, limite - p.X);
            if (larg <= 0) return;
            using (_dc.PushClip(new Rect(p.X, _y, larg, _alt)))
                Escreve(_dc, p.S, p.X, _y, 11, _cor, p.Negrito);
        }

        public void Dispose() => Desenhar(double.MaxValue);
    }

    static readonly string[] ComportamentosCurtos = { "FP", "Cor", "Cav", "Can", "Cac", "Sar" };
    protected static string ComportamentoCurto(SJogador p) =>
        p.Comportamento < ComportamentosCurtos.Length ? Abrev("comportamento:" + ComportamentosCurtos[p.Comportamento], ComportamentosCurtos[p.Comportamento]) : "";

    // a traducao de uma abreviatura (chave com prefixo); sem ela, a do original
    protected static string Abrev(string chave, string original) { var t = T(chave); return t == chave ? original : t; }

    protected static string Nacional(SJogador p) => p.B35 switch
    {
        0 => "",
        1 => "* " + PaisDe(p.Pais),
        _ => PaisDe(p.Pais),
    };

    // ============================================================ componentes
    internal abstract class Comp
    {
        public Rect R;
        public Func<bool>? Visivel, Habilitado;
        public bool EstaVisivel => Visivel?.Invoke() ?? true;
        public bool EstaHabilitado => Habilitado?.Invoke() ?? true;
        public abstract void Desenhar(DrawingContext dc, MercadoForm f);
    }

    // TBitBtn / TSpeedButton com a imagem em cima (blGlyphTop, Margin 10, Spacing -1) ou so
    // a imagem (os botoes pequenos de 25x25). Glifos: quantas imagens tem a tira (2 = a
    // segunda e a desligada).
    internal sealed class Botao : Comp
    {
        public string Texto = "";
        public string? Glifo;
        public int Glifos = 2;
        public bool Negrito, Padrao, Cancelar;
        public Func<bool>? Baixo;         // TSpeedButton.Down
        public Action? Clique;
        public bool Premido;

        public override void Desenhar(DrawingContext dc, MercadoForm f)
        {
            if (Moderno)
            {
                bool h = EstaHabilitado, baixo = Baixo?.Invoke() ?? false;
                bool sobre = f._sobreBotao == this;
                IBrush fundo = !h ? MM.Barra : Premido ? MM.BotaoBaixo : baixo ? MM.AzulFundo : sobre ? MM.BotaoSobre : MM.Botao;
                IBrush borda = !h ? MM.Linha : baixo ? MM.Azul : Padrao ? MM.Azul : MM.Borda;
                // um respiro entre os botoes encostados do DFM
                var rb = R.Deflate(R.Height >= 48 ? 2 : 1);
                // o padrao (Enter): a pilula ouro do hub, texto e icone verde-escuros
                IBrush? cor = null;
                if (Padrao && h && !baixo) { fundo = Premido ? MM.PrimarioBaixo : sobre ? MM.PrimarioSobre : MM.Primario; borda = MM.PrimarioBorda; cor = MM.SobreAzul; }
                if (Texto == "" && Glifo != null && Glifo.Contains(".clear"))
                {
                    // os botoes de limpar um criterio: o "x" (IconesModernos) discreto
                    MM.Cartao_(dc, rb, fundo, 6, borda);
                    var c = rb.Center;
                    IconesModernos.Desenhar(dc, IconesModernos.Cancelar, new Rect(c.X - 7, c.Y - 7, 14, 14), h, MM.Fraco);
                    return;
                }
                MM.Desenhar(dc, rb, fundo, borda, Texto, Glifo != null ? Imagem(Glifo) : null, Glifos, h, 10.5, Negrito || cor != null, IconesModernos.DoArquivo(Glifo), cor);
                return;
            }
            var r = R;
            dc.FillRectangle(Face, r);
            if (Padrao)
            {
                Moldura(dc, r, Preto, Preto);
                r = r.Deflate(1);
            }
            bool desce = Premido || (Baixo?.Invoke() ?? false);
            if (desce)
            {
                Moldura(dc, r, Escura, Branco);
                Moldura(dc, r.Deflate(1), Sombra, Luz3D);
                if (Baixo?.Invoke() == true && !Premido)
                    // o Down do TSpeedButton: o fundo pontilhado claro
                    dc.FillRectangle(Pincel(250, 250, 250), r.Deflate(2));
            }
            else Saltada(dc, r);
            var dentro = r.Deflate(2);
            bool hab = EstaHabilitado;
            var img = Glifo != null ? Imagem(Glifo) : null;
            double gw = 0, gh = 0;
            if (img != null) { gw = img.Size.Width / Glifos; gh = img.Size.Height; }
            double d = desce ? 1 : 0;
            if (Texto == "")
            {
                if (img != null) DesenharGlifo(dc, img, new Point(dentro.X + Math.Floor((dentro.Width - gw + 1) / 2) + d, dentro.Y + Math.Floor((dentro.Height - gh + 1) / 2) + d), gw, gh, hab);
                return;
            }
            var t = Txt(Texto, 9, hab ? Preto : Cinza, Negrito);
            double th = Math.Round(t.Height), tw = t.WidthIncludingTrailingWhitespace;
            double margem = 10, esp = Math.Floor((dentro.Height - (margem + gh) - th) / 2);
            double gy = dentro.Y + margem + d;
            if (img != null) DesenharGlifo(dc, img, new Point(dentro.X + Math.Floor((dentro.Width - gw + 1) / 2) + d, gy), gw, gh, hab);
            double ty = gy + gh + esp, tx = dentro.X + Math.Floor((dentro.Width - tw + 1) / 2) + d;
            if (!hab) dc.DrawText(Txt(Texto, 9, Branco, Negrito), new Point(tx + 1, ty + 1));
            dc.DrawText(t, new Point(tx, ty));
        }

        void DesenharGlifo(DrawingContext dc, Bitmap img, Point p, double gw, double gh, bool hab)
        {
            if (Glifos >= 2)
            {
                var src = new Rect(hab ? 0 : gw, 0, gw, gh);
                dc.DrawImage(img, src, new Rect(p.X, p.Y, gw, gh));
            }
            else
                using (dc.PushOpacity(hab ? 1 : 0.35))
                    dc.DrawImage(img, new Rect(p.X, p.Y, gw, gh));
        }
    }

    // TComboBox csDropDownList (ou csOwnerDrawFixed com DesenharItem)
    internal sealed class Combo : Comp
    {
        public Func<string> Texto = () => "";
        public Func<IReadOnlyList<string>> Itens = () => Array.Empty<string>();
        public Func<int> Indice = () => -1;
        public Action<int>? Escolher;
        public double ItemA = 16, Tam = 13;
        public bool Negrito, Plano;
        public FontFamily? Fonte;
        public Action<DrawingContext, int, Rect, IBrush>? DesenharItem;
        public double Topo;                // a rolagem da lista aberta, em itens

        public override void Desenhar(DrawingContext dc, MercadoForm f)
        {
            if (Moderno)
            {
                MM.Cartao_(dc, R, MM.Campo, 6, f._aberto == this ? MM.Azul : MM.Borda);
                double cx = R.Right - 12, cy = R.Y + R.Height / 2;
                var pen = new Pen(MM.Fraco, 1.6, lineCap: PenLineCap.Round);
                dc.DrawLine(pen, new Point(cx - 4, cy - 2), new Point(cx, cy + 2));
                dc.DrawLine(pen, new Point(cx, cy + 2), new Point(cx + 4, cy - 2));
                var a = new Rect(R.X + 4, R.Y + 1, R.Width - 26, R.Height - 2);
                using (dc.PushClip(a))
                {
                    int i = Indice();
                    if (DesenharItem != null && i >= 0) DesenharItem(dc, i, a, MM.Texto);
                    else
                    {
                        var t = Txt(Texto(), Tam, MM.Texto, Negrito, Fonte);
                        dc.DrawText(t, new Point(a.X + 2, a.Y + Math.Floor((a.Height - t.Height) / 2)));
                    }
                }
                return;
            }
            dc.FillRectangle(Branco, R);
            Rect dentro;
            if (Plano) { Moldura(dc, R, Preto, Preto); dentro = R.Deflate(1); }
            else { Afundada(dc, R); dentro = R.Deflate(2); }
            var b = new Rect(dentro.Right - 16, dentro.Y, 16, dentro.Height);
            dc.FillRectangle(Face, b);
            Saltada(dc, b);
            Seta(dc, b.X + 7, b.Y + Math.Floor(b.Height / 2), false, Preto);
            var area = new Rect(dentro.X, dentro.Y, dentro.Width - 16, dentro.Height);
            using (dc.PushClip(area))
            {
                int i = Indice();
                if (DesenharItem != null && i >= 0) DesenharItem(dc, i, area, Preto);
                else
                {
                    var t = Txt(Texto(), Tam, Preto, Negrito, Fonte);
                    dc.DrawText(t, new Point(area.X + 2, area.Y + Math.Floor((area.Height - t.Height) / 2)));
                }
            }
        }

        public const int Linhas = 8;
        public int Visiveis => Math.Min(Linhas, Itens().Count);
        public Rect Lista => new(R.X, R.Bottom, R.Width, Visiveis * ItemA + 2);
    }

    // TCheckBox (a caixa de 13x13; o texto e um TLabel a parte)
    internal sealed class Marcar : Comp
    {
        public Func<bool> Marcado = () => false;
        public Action? Clique;

        public override void Desenhar(DrawingContext dc, MercadoForm f)
        {
            var c = new Rect(R.X, R.Y + Math.Floor((R.Height - 13) / 2), 13, 13);
            if (Moderno)
            {
                var q = c.Inflate(1);
                bool m = Marcado();
                MM.Cartao_(dc, q, m ? MM.Azul : MM.Campo, 4, m ? MM.Azul : MM.Fraco, 1.4);
                if (m)
                {
                    var pen = new Pen(MM.SobreAzul, 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                    dc.DrawLine(pen, new Point(q.X + 3.5, q.Y + 7.5), new Point(q.X + 6.2, q.Y + 10.2));
                    dc.DrawLine(pen, new Point(q.X + 6.2, q.Y + 10.2), new Point(q.X + 11, q.Y + 4.5));
                }
                return;
            }
            dc.FillRectangle(Branco, c);
            Afundada(dc, c);
            if (!Marcado()) return;
            int[] alt = { 2, 3, 4, 3, 2, 1, 0 };
            for (int i = 0; i < alt.Length; i++)
                dc.FillRectangle(Preto, new Rect(c.X + 3 + i, c.Y + 3 + alt[i], 1, 3));
        }
    }

    // TListBox lbOwnerDrawFixed, com a barra de rolagem vertical (sempre, como os EXEs do
    // launcher pedem WS_VSCROLL)
    internal sealed class Lista : Comp
    {
        public Func<int> N = () => 0;
        public Action<DrawingContext, int, Rect, bool>? Item;
        public Func<IBrush> Fundo = () => Branco;
        public double ItemA = 16;
        public int Sel = -1;
        public double Rolagem;
        public Action<int>? AoClicar, AoDuplo;

        public Rect Itens => new(R.X + 2, R.Y + 2, R.Width - 4 - 16, R.Height - 4);

        // metade da diferenca entre a altura do texto a 100% e a do tamanho escolhido
        static double AjusteVertical()
        {
            if (Acessibilidade.Percentual == 100) return 0;
            var f = Moderno ? MM.Inter : Arial;
            var t = new FormattedText("Ág", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(f), 13, Preto);
            return Math.Round(t.Height * (1 - Acessibilidade.Texto) / 2);
        }
        public Rect Barra => new(R.Right - 2 - 16, R.Y + 2, 16, R.Height - 4);
        double Total => N() * ItemA;

        public void Rolar(double v) => Rolagem = Math.Clamp(v, 0, Math.Max(0, Total - Itens.Height));

        public void MostrarEscolhido()
        {
            if (Sel < 0) return;
            double y = Sel * ItemA;
            if (y < Rolagem) Rolar(y);
            else if (y + ItemA > Rolagem + Itens.Height) Rolar(y + ItemA - Itens.Height);
        }

        public int ItemEm(Point p)
        {
            if (!Itens.Contains(p)) return -1;
            int i = (int)Math.Floor((p.Y - Itens.Y + Rolagem) / ItemA);
            return i >= 0 && i < N() ? i : -1;
        }

        public (Rect Cursor, bool Rola) CursorBarra()
        {
            double total = Total, vis = Itens.Height, trilho = Barra.Height - 32;
            if (total <= vis) return (default, false);
            double h = Math.Max(12, trilho * vis / total);
            double py = Barra.Y + 16 + (trilho - h) * (Rolagem / (total - vis));
            return (new Rect(Barra.X, py, 16, h), true);
        }

        public override void Desenhar(DrawingContext dc, MercadoForm f)
        {
            var it = Itens;
            if (Moderno) MM.Cartao_(dc, R, MM.Poco, 8, MM.Linha);
            else
            {
                Afundada(dc, R);
                dc.FillRectangle(Fundo(), it);
            }
            Rolar(Rolagem);
            using (dc.PushClip(it))
            {
                int n = N();
                int i0 = (int)Math.Floor(Rolagem / ItemA);
                for (int i = Math.Max(0, i0); i < n; i++)
                {
                    double y = it.Y + i * ItemA - Rolagem;
                    if (y >= it.Bottom) break;
                    // as linhas alternadas do Moderno (a do escolhido, a lista pinta)
                    if (Moderno && i % 2 == 1 && i != Sel) dc.FillRectangle(MM.Zebra, new Rect(it.X, y, it.Width, ItemA));
                    // com o texto menor ou maior (Acessibilidade), a linha desce ou sobe metade da
                    // diferenca de altura: o texto fica no meio da faixa como a 100%
                    using (dc.PushTransform(Matrix.CreateTranslation(0, AjusteVertical())))
                        Item?.Invoke(dc, i, new Rect(it.X, y, it.Width, ItemA), i == Sel);
                }
            }
            BarraVertical(dc, Barra, CursorBarra());
        }
    }

    protected static void BarraVertical(DrawingContext dc, Rect barra, (Rect Cursor, bool Rola) cur)
    {
        if (Moderno) { MM.BarraRolagem(dc, barra, cur.Cursor, cur.Rola); return; }
        dc.FillRectangle(Luz3D, barra);
        foreach (var (by, cima) in new[] { (barra.Y, true), (barra.Bottom - 16, false) })
        {
            var b = new Rect(barra.X, by, 16, 16);
            dc.FillRectangle(Face, b);
            Saltada(dc, b);
            Seta(dc, b.X + 7, b.Y + (cima ? 7 : 8), cima, cur.Rola ? Preto : Sombra);
        }
        if (cur.Rola)
        {
            dc.FillRectangle(Face, cur.Cursor);
            Saltada(dc, cur.Cursor);
        }
    }

    // ------------------------------------------------------------ caixa de texto (TEdit)
    // Uma TextBox do Avalonia sem enfeites dentro da moldura afundada desenhada; as letras
    // ficam maiusculas (CharCase = ecUpperCase) e o Enter aperta o botao padrao.
    protected TextBox CaixaTexto(Rect r, int max, Action<string> mudou)
    {
        var tb = new TextBox
        {
            Width = r.Width - 4, Height = r.Height - 4, MinHeight = 0, MinWidth = 0,
            Padding = new Thickness(1, 0, 1, 0), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0),
            Background = Branco, Foreground = Preto, FontFamily = Moderno ? MM.Inter : Arial, FontSize = Moderno ? 12 : 13, MaxLength = max,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            SelectionBrush = Destaque, SelectionForegroundBrush = SobreDestaque, CaretBrush = Preto,
        };
        foreach (var estado in new[] { "", ":focus", ":pointerover" })
        {
            Selector sel = estado == "" ? Selectors.OfType(null, typeof(TextBox)) : Selectors.OfType(null, typeof(TextBox)).Class(estado);
            tb.Styles.Add(new Style(_ => sel.Template().OfType(typeof(Border)).Name("PART_BorderElement"))
            {
                Setters =
                {
                    new Setter(Border.BackgroundProperty, Branco),
                    new Setter(Border.BorderThicknessProperty, new Thickness(0)),
                    new Setter(Border.MinHeightProperty, 0.0),
                },
            });
        }
        bool trocando = false;
        tb.TextChanged += (_, _) =>
        {
            if (trocando) return;
            var t = tb.Text ?? "";
            var m = t.ToUpperInvariant();
            if (m != t)
            {
                trocando = true;
                int c = tb.CaretIndex;
                tb.Text = m;
                tb.CaretIndex = c;
                trocando = false;
            }
            mudou(m);
        };
        tb.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Return) { ApertarPadrao(); e.Handled = true; }
            else if (e.Key == Key.Escape) { ApertarCancelar(); e.Handled = true; }
        };
        Canvas.SetLeft(tb, Origem.X + r.X + 2);
        Canvas.SetTop(tb, Origem.Y + r.Y + 2);
        _raiz.Children.Add(tb);
        _foco ??= tb;
        return tb;
    }

    // ============================================================ desenho
    // o que fica por baixo dos componentes (rotulos, quadros) e por cima
    protected virtual void DesenharFundo(DrawingContext dc) { }
    protected virtual void DesenharFrente(DrawingContext dc) { }

    Combo? _aberto;
    int _sobre = -1;
    Botao? _sobreBotao;

    public override void Render(DrawingContext dc)
    {
        if (Moderno) { RenderModerno(dc); return; }
        var jan = new Rect(0, 0, Width, Height);
        dc.FillRectangle(Face, jan);
        Moldura(dc, jan, Luz3D, Escura);
        Moldura(dc, jan.Deflate(1), Branco, Sombra);
        var tit = new Rect(Borda, Borda, Width - 2 * Borda, TituloA);
        dc.FillRectangle(CorTitulo, tit);
        using (dc.PushClip(tit))
        {
            // LogoTitulo (o logo de uma equipa, se a janela tiver): antes do titulo
            double xt = Borda + 2;
            if (LogoTitulo != null)
            {
                using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                    dc.DrawImage(LogoTitulo, new Rect(Borda + 2, Borda + 2, TituloA - 4, TituloA - 4));
                xt += TituloA;
            }
            Escreve(dc, Titulo, xt, Borda + 4, 13, Preto);
        }
        using (dc.PushTransform(Matrix.CreateTranslation(Origem.X, Origem.Y)))
        using (dc.PushClip(new Rect(0, 0, CL, CA)))
        {
            dc.FillRectangle(FundoCliente, new Rect(0, 0, CL, CA));
            DesenharFundo(dc);
            foreach (var c in Comps)
                if (c.EstaVisivel) c.Desenhar(dc, this);
            DesenharFrente(dc);
            if (_aberto != null) DesenharAberto(dc, _aberto);
        }
    }

    // a janela no Moderno: o cartao arredondado com a barra do titulo (MM.Moldura) e a
    // area cliente no mesmo lugar
    void RenderModerno(DrawingContext dc)
    {
        MM.DesenharMoldura(dc, new Size(Width, Height), Titulo, LogoTitulo, false);
        using (dc.PushTransform(Matrix.CreateTranslation(Origem.X, Origem.Y)))
        using (dc.PushClip(new Rect(0, 0, CL, CA)))
        {
            DesenharFundo(dc);
            foreach (var c in Comps)
                if (c.EstaVisivel) c.Desenhar(dc, this);
            DesenharFrente(dc);
            if (_aberto != null) DesenharAbertoModerno(dc, _aberto);
        }
    }

    void DesenharAbertoModerno(DrawingContext dc, Combo c)
    {
        var l = c.Lista;
        var itens = c.Itens();
        // a area dos itens no mesmo lugar do original (o clique conta de Lista.Y + 1)
        MM.Cartao_(dc, l.Inflate(new Thickness(0, 2)), MM.Barra, 8, MM.Borda);
        bool rola = itens.Count > Combo.Linhas;
        var area = new Rect(l.X + 2, l.Y + 1, l.Width - 4 - (rola ? 14 : 0), l.Height - 2);
        using (dc.PushClip(area))
            for (int k = 0; k < c.Visiveis; k++)
            {
                int i = (int)c.Topo + k;
                if (i >= itens.Count) break;
                var r = new Rect(area.X, area.Y + k * c.ItemA, area.Width, c.ItemA);
                bool sel = i == _sobre || (_sobre < 0 && i == c.Indice());
                if (sel) MM.Escolhida(dc, r);
                var cor = sel ? MM.TextoEscolhido : MM.Texto;
                if (c.DesenharItem != null) c.DesenharItem(dc, i, r, cor);
                else
                {
                    var t = Txt(itens[i], c.Tam, cor, c.Negrito, c.Fonte);
                    dc.DrawText(t, new Point(r.X + 4, r.Y + Math.Floor((r.Height - t.Height) / 2)));
                }
            }
        if (BarraAberta(c) is { } b) MM.BarraRolagem(dc, b.Barra, b.Cursor, true);
    }

    void DesenharAberto(DrawingContext dc, Combo c)
    {
        var l = c.Lista;
        var itens = c.Itens();
        dc.FillRectangle(Branco, l);
        Moldura(dc, l, Preto, Preto);
        bool rola = itens.Count > Combo.Linhas;
        var area = new Rect(l.X + 1, l.Y + 1, l.Width - 2 - (rola ? 16 : 0), l.Height - 2);
        using (dc.PushClip(area))
            for (int k = 0; k < c.Visiveis; k++)
            {
                int i = (int)c.Topo + k;
                if (i >= itens.Count) break;
                var r = new Rect(area.X, area.Y + k * c.ItemA, area.Width, c.ItemA);
                bool sel = i == _sobre || (_sobre < 0 && i == c.Indice());
                if (sel) dc.FillRectangle(Destaque, r);
                var cor = sel ? Branco : Preto;
                if (c.DesenharItem != null) c.DesenharItem(dc, i, r, cor);
                else
                {
                    var t = Txt(itens[i], c.Tam, cor, c.Negrito, c.Fonte);
                    dc.DrawText(t, new Point(r.X + 2, r.Y + Math.Floor((r.Height - t.Height) / 2)));
                }
            }
        if (BarraAberta(c) is { } b) BarraVertical(dc, b.Barra, (b.Cursor, true));
    }

    // a barra de rolagem da lista aberta, o cursor dela e o curso livre do cursor (os dois
    // visuais); null quando todos os itens cabem
    (Rect Barra, Rect Cursor, double Livre)? BarraAberta(Combo c)
    {
        int n = c.Itens().Count;
        if (n <= Combo.Linhas) return null;
        var l = c.Lista;
        double max = n - Combo.Linhas;
        if (Moderno)
        {
            var bm = new Rect(l.Right - 14, l.Y + 1, 12, l.Height - 2);
            double hm = Math.Max(16, bm.Height * Combo.Linhas / n);
            return (bm, new Rect(bm.X, bm.Y + (bm.Height - hm) * (c.Topo / max), 12, hm), bm.Height - hm);
        }
        var b = new Rect(l.Right - 1 - 16, l.Y + 1, 16, l.Height - 2);
        double trilho = b.Height - 32, h = Math.Max(12, trilho * Combo.Linhas / n);
        return (b, new Rect(b.X, b.Y + 16 + (trilho - h) * (c.Topo / max), 16, h), trilho - h);
    }

    // o item i da lista aberta a vista (a rolagem anda o minimo)
    static void MostrarNaLista(Combo c, int i)
    {
        int n = c.Itens().Count;
        if (i < c.Topo) c.Topo = i;
        else if (i >= c.Topo + Combo.Linhas) c.Topo = i - Combo.Linhas + 1;
        c.Topo = Math.Clamp(c.Topo, 0, Math.Max(0, n - Combo.Linhas));
    }

    void EscolherAberto(int i)
    {
        var c = _aberto;
        _aberto = null;
        if (c != null && i >= 0 && i < c.Itens().Count) c.Escolher?.Invoke(i);
        InvalidateVisual();
    }

    // ============================================================ mouse e toque
    Botao? _premido;
    Lista? _arrasteLista;
    Point _arrasteIni;
    double _arrasteRol;
    bool _arrasteBarra, _moveu;
    // a lista aberta: o cursor da barra arrastado (e a rolagem do comeco); _abrindo, o botao
    // ainda premido desde o clique que abriu (soltar sobre um item escolhe, como no Windows)
    bool _arrasteCombo, _abrindo;
    double _arrasteTopo;
    // a busca pelo que se digita com a lista aberta ("40" vai ao 40)
    string _digitado = "";
    DateTime _digitadoEm;

    Point Cliente(Point p) => new(p.X - Origem.X, p.Y - Origem.Y);

    Comp? CompEm(Point pc)
    {
        for (int i = Comps.Count - 1; i >= 0; i--)
            if (Comps[i].EstaVisivel && Comps[i].R.Contains(pc)) return Comps[i];
        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var pc = Cliente(e.GetPosition(this));
        e.Handled = true;
        if (_aberto != null)
        {
            CliqueAberto(_aberto, pc, e.Pointer);
            InvalidateVisual();
            return;
        }
        if (_foco == null || !(_foco.IsFocused)) Focus();
        var c = CompEm(pc);
        switch (c)
        {
            case Botao b when b.EstaHabilitado:
                _premido = b; b.Premido = true;
                e.Pointer.Capture(this);
                break;
            case Combo cb when cb.EstaHabilitado:
                _aberto = cb; _sobre = -1; _digitado = "";
                int n = cb.Itens().Count;
                cb.Topo = Math.Clamp(cb.Indice() - Combo.Linhas / 2, 0, Math.Max(0, n - Combo.Linhas));
                // o teclado vem para a janela (as setas e o que se digita vao para a lista,
                // nao para a caixa do nome)
                Focus();
                _abrindo = true; _arrasteIni = pc;
                e.Pointer.Capture(this);
                break;
            case Marcar m when m.EstaHabilitado:
                m.Clique?.Invoke();
                break;
            case Lista l when l.EstaHabilitado:
                if (l.Barra.Contains(pc))
                {
                    var (cur, rola) = l.CursorBarra();
                    if (!rola) break;
                    if (pc.Y < l.Barra.Y + 16) l.Rolar(l.Rolagem - l.ItemA);
                    else if (pc.Y >= l.Barra.Bottom - 16) l.Rolar(l.Rolagem + l.ItemA);
                    else if (cur.Contains(pc))
                    {
                        _arrasteLista = l; _arrasteIni = pc; _arrasteRol = l.Rolagem; _arrasteBarra = true;
                        e.Pointer.Capture(this);
                    }
                    else l.Rolar(l.Rolagem + (pc.Y < cur.Y ? -1 : 1) * (l.Itens.Height - l.ItemA));
                    break;
                }
                int i = l.ItemEm(pc);
                if (i >= 0)
                {
                    bool duplo = e.ClickCount >= 2 && i == l.Sel;
                    l.Sel = i;
                    l.AoClicar?.Invoke(i);
                    if (duplo) l.AoDuplo?.Invoke(i);
                }
                _arrasteLista = l; _arrasteIni = pc; _arrasteRol = l.Rolagem; _arrasteBarra = false; _moveu = false;
                e.Pointer.Capture(this);
                break;
        }
        InvalidateVisual();
    }

    void CliqueAberto(Combo c, Point pc, IPointer ponteiro)
    {
        var l = c.Lista;
        int n = c.Itens().Count;
        bool rola = n > Combo.Linhas;
        if (l.Contains(pc))
        {
            if (rola && pc.X >= l.Right - 17)
            {
                if (BarraAberta(c) is { } b && b.Cursor.Contains(pc))
                {
                    _arrasteCombo = true; _arrasteIni = pc; _arrasteTopo = c.Topo;
                    ponteiro.Capture(this);
                }
                else if (pc.Y < l.Y + 17) c.Topo = Math.Max(0, c.Topo - 1);
                else if (pc.Y >= l.Bottom - 17) c.Topo = Math.Min(n - Combo.Linhas, c.Topo + 1);
                else c.Topo = Math.Clamp(c.Topo + (pc.Y < l.Y + l.Height / 2 ? -Combo.Linhas : Combo.Linhas), 0, n - Combo.Linhas);
                return;
            }
            int i = (int)c.Topo + (int)Math.Floor((pc.Y - l.Y - 1) / c.ItemA);
            _aberto = null;
            if (i >= 0 && i < n) c.Escolher?.Invoke(i);
            return;
        }
        _aberto = null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pc = Cliente(e.GetPosition(this));
        if (_aberto != null)
        {
            if (_arrasteCombo)
            {
                if (BarraAberta(_aberto) is { } b && b.Livre > 0)
                {
                    int n = _aberto.Itens().Count;
                    _aberto.Topo = Math.Clamp(Math.Round(_arrasteTopo + (pc.Y - _arrasteIni.Y) * (n - Combo.Linhas) / b.Livre), 0, n - Combo.Linhas);
                    InvalidateVisual();
                }
                return;
            }
            var l = _aberto.Lista;
            int s = l.Contains(pc) && pc.X < l.Right - 17 ? (int)_aberto.Topo + (int)Math.Floor((pc.Y - l.Y - 1) / _aberto.ItemA) : -1;
            if (s != _sobre) { _sobre = s; InvalidateVisual(); }
            return;
        }
        if (Moderno && _premido == null && _arrasteLista == null)
        {
            var b = CompEm(pc) as Botao;
            if (b != null && !b.EstaHabilitado) b = null;
            if (b != _sobreBotao) { _sobreBotao = b; InvalidateVisual(); }
        }
        if (_premido != null)
        {
            bool dentro = _premido.R.Contains(pc);
            if (dentro != _premido.Premido) { _premido.Premido = dentro; InvalidateVisual(); }
            return;
        }
        if (_arrasteLista is not { } lt) return;
        if (_arrasteBarra)
        {
            var (cur, _) = lt.CursorBarra();
            double livre = lt.Barra.Height - 32 - cur.Height;
            double total = lt.N() * lt.ItemA - lt.Itens.Height;
            if (livre > 0) lt.Rolar(_arrasteRol + (pc.Y - _arrasteIni.Y) * total / livre);
            InvalidateVisual();
            return;
        }
        if (Math.Abs(pc.Y - _arrasteIni.Y) > 6) _moveu = true;
        if (_moveu) { lt.Rolar(_arrasteRol - (pc.Y - _arrasteIni.Y)); InvalidateVisual(); }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_sobreBotao != null) { _sobreBotao = null; InvalidateVisual(); }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        _arrasteLista = null;
        _arrasteCombo = false;
        if (_abrindo)
        {
            // o botao premido na caixa e solto sobre um item da lista: escolhe
            _abrindo = false;
            var pc = Cliente(e.GetPosition(this));
            if (_aberto != null && _sobre >= 0 && _aberto.Lista.Contains(pc)) EscolherAberto(_sobre);
            return;
        }
        if (_premido is { } b)
        {
            _premido = null;
            bool dentro = b.Premido;
            b.Premido = false;
            if (dentro && b.EstaHabilitado && b.EstaVisivel) b.Clique?.Invoke();
            InvalidateVisual();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var pc = Cliente(e.GetPosition(this));
        if (_aberto is { } c)
        {
            int n = c.Itens().Count;
            c.Topo = Math.Clamp(c.Topo - Math.Sign(e.Delta.Y) * 3, 0, Math.Max(0, n - Combo.Linhas));
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (CompEm(pc) is Lista l)
        {
            l.Rolar(l.Rolagem - e.Delta.Y * l.ItemA * 3);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    // ============================================================ teclado
    // a lista que as setas movem
    protected Lista? ListaPrincipal;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        switch (e.Key)
        {
            case Key.Escape:
                if (_aberto != null) { _aberto = null; InvalidateVisual(); }
                else ApertarCancelar();
                e.Handled = true;
                break;
            case Key.Enter or Key.Return:
                if (_aberto != null) EscolherAberto(_sobre);
                else ApertarPadrao();
                e.Handled = true;
                break;
            case Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End when _aberto is { } c:
                {
                    int n = c.Itens().Count;
                    if (n == 0) break;
                    int i = _sobre >= 0 ? _sobre : c.Indice();
                    i = e.Key switch
                    {
                        Key.Up => i - 1,
                        Key.Down => i < 0 ? 0 : i + 1,
                        Key.PageUp => i - Combo.Linhas,
                        Key.PageDown => i + Combo.Linhas,
                        Key.Home => 0,
                        _ => n - 1,
                    };
                    _sobre = Math.Clamp(i, 0, n - 1);
                    MostrarNaLista(c, _sobre);
                    InvalidateVisual();
                    e.Handled = true;
                    break;
                }
            case Key.Up or Key.Down when ListaPrincipal is { } l && l.EstaVisivel && _aberto == null:
                {
                    int n = l.N();
                    if (n == 0) break;
                    int i = Math.Clamp(l.Sel + (e.Key == Key.Up ? -1 : 1), 0, n - 1);
                    if (l.Sel < 0) i = 0;
                    l.Sel = i;
                    l.MostrarEscolhido();
                    l.AoClicar?.Invoke(i);
                    InvalidateVisual();
                    e.Handled = true;
                    break;
                }
        }
    }

    // com a lista aberta, o que se digita leva ao primeiro item que comeca com o texto
    // (digitado em menos de 1 s junta: "4" e "0" vao ao 40)
    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (_aberto is not { } c || string.IsNullOrEmpty(e.Text)) return;
        var agora = DateTime.UtcNow;
        _digitado = (agora - _digitadoEm).TotalSeconds < 1 ? _digitado + e.Text : e.Text;
        _digitadoEm = agora;
        var itens = c.Itens();
        int Achar(string t)
        {
            for (int i = 0; i < itens.Count; i++)
                if (itens[i].Trim().StartsWith(t, StringComparison.CurrentCultureIgnoreCase)) return i;
            return -1;
        }
        int k = Achar(_digitado);
        if (k < 0 && _digitado.Length > 1) { _digitado = e.Text; k = Achar(_digitado); }
        if (k >= 0) { _sobre = k; MostrarNaLista(c, k); InvalidateVisual(); }
        e.Handled = true;
    }

    void ApertarPadrao()
    {
        foreach (var c in Comps)
            if (c is Botao { Padrao: true } b && b.EstaVisivel && b.EstaHabilitado) { b.Clique?.Invoke(); InvalidateVisual(); return; }
    }

    void ApertarCancelar()
    {
        foreach (var c in Comps)
            if (c is Botao { Cancelar: true } b && b.EstaVisivel && b.EstaHabilitado) { b.Clique?.Invoke(); InvalidateVisual(); return; }
    }

    // ============================================================ demo
    // um jogo de teste (o save do oraculo j3) com o motor a correr ate a tela da equipa; com
    // ela na tela (motor parado), abrir(sessao, equipa humana) abre o dialogo
    internal static Control Demo(Action<Sessao, SEquipa> abrir)
    {
        var raiz = new Border { Background = new SolidColorBrush(Color.FromRgb(0, 128, 0)) };
        var arq = AcharSaveDemo();
        if (arq == null)
            return new TextBlock { Text = "Demo: não achei RetroFoot.Motor.Testes/Oraculo/j3-entrada.e98 (ou RETROFOOT_SAVE)", Foreground = Brushes.White };
        var s = RetroFoot.Motor.Save.Jogo98.Ler(File.ReadAllBytes(arq));
        bool launcher = Environment.GetEnvironmentVariable("RETROFOOT_LAUNCHER") == "1";
        var op = RetroFoot.Motor.Ciclo.Opcoes.Padrao with
        {
            Regras = launcher ? RetroFoot.Motor.Regras.Regras.Launcher(8) : RetroFoot.Motor.Regras.Regras.Original,
        };
        var tmp = Path.Combine(Path.GetTempPath(), "retrofoot-demo-mercado.e98");
        Sessao? sessao = null;
        bool aberto = false;
        sessao = new Sessao(s, c =>
        {
            raiz.Child = c;
            if (aberto) return;
            aberto = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var m = sessao!.J.M;
                var e = m.S.Equipas.FirstOrDefault(x => RetroFoot.Motor.Partida.Memoria.Humano(m.TreinadorDa(x)));
                if (e != null) abrir(sessao, e);
            }, Avalonia.Threading.DispatcherPriority.Background);
        }, op, tmp);
        sessao.Erro += ex => raiz.Child = new TextBlock { Text = "Erro no motor: " + ex, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
        sessao.Comecar();
        return raiz;
    }

    static string? AcharSaveDemo()
    {
        var env = Environment.GetEnvironmentVariable("RETROFOOT_SAVE");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        foreach (var ini in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var d = new DirectoryInfo(ini); d != null; d = d.Parent)
            {
                var f = Path.Combine(d.FullName, "RetroFoot.Motor.Testes", "Oraculo", "j3-entrada.e98");
                if (File.Exists(f)) return f;
            }
        return null;
    }
}
