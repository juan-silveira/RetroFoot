using Avalonia;
using Avalonia.Media;

namespace RetroFoot.App;

// O tema do visual Moderno: o verde e amarelo da marca e do hub (o quadro verde da capa, os
// cartoes verde-escuros com o fio dourado, os icones amarelos, o Jogar em pilula ouro). E a
// UNICA paleta do Moderno: as paletas de cada tela (ClubeModerno, MM, ConfigEstilo, os M* da
// TelaEquipa, da TelaJornada...) apontam para estes nomes, e um ajuste aqui muda todas.
// O Classico nao usa nada daqui.
//
// Como usar:
//  - Fundo / FundoTela: o fundo da tela inteira (FundoTela e o degrade do quadro, para telas
//    cheias; Fundo e a cor lisa, para onde precisa de cor solida); FundoEscuro e o mais fundo
//    (barras de rolagem, por tras das molduras).
//  - Barra: as barras de cima (menus, titulo); Cartao: os cartoes (mais escuros que o fundo,
//    como os botoes do hub), com Borda (o fio dourado-oliva); CartaoForte: o que fica dentro
//    de um cartao e se destaca (pilulas, campos, botoes secundarios); Menu: os menus abertos.
//  - Linha: separadores e trilhos de barras; Zebra: as linhas pares das tabelas; Hover: o rato
//    em cima; Selecao: a linha escolhida (com um traco Destaque a esquerda); Humano: a linha da
//    equipa do jogador nas tabelas.
//  - Texto (branco), TextoSuave (as descricoes do hub), TextoFraco (cabecalhos, numeros
//    secundarios), Apagado (desabilitado).
//  - Destaque: o amarelo (acoes, abas escolhidas, foco, icones); SobreDestaque: o texto sobre
//    o amarelo; Ouro: o amarelo mais fundo (bordas, hover do amarelo); BotaoPrimario: o degrade
//    da pilula do Jogar.
//  - Estados (texto/icone sobre o verde): Verde (positivo, ao vivo), Vermelho, Laranja, Azul,
//    Roxo, Prata. Preenchimentos fortes (bolinhas, selos): VerdeForte, CinzaForte, VermelhoForte.
// As cores tambem existem como Color em TemaVerde.Cores (para degrades e misturas).
public static class TemaVerde
{
    public static class Cores
    {
        // a marca
        public static readonly Color MarcaVerdeEscuro = Color.Parse("#0D5A2E"), MarcaVerde = Color.Parse("#168C40"),
            MarcaAmarelo = Color.Parse("#F8D734"), MarcaOuro = Color.Parse("#DDA917");

        public static readonly Color Fundo = Color.Parse("#0D5A2E"), FundoClaro = Color.Parse("#11683A"), FundoEscuro = Color.Parse("#073A1D");
        public static readonly Color Barra = Color.Parse("#073A1D"), Cartao = Color.Parse("#094622"), CartaoForte = Color.Parse("#0F5A31");
        public static readonly Color Menu = Color.Parse("#0A4D27"), Borda = Color.Parse("#4E7A2E"), Linha = Color.Parse("#1A6A3A");
        public static readonly Color Zebra = Color.Parse("#0B4E27"), Hover = Color.Parse("#13663C"), Selecao = Color.Parse("#1A7442");
        public static readonly Color Humano = Color.Parse("#3B6B22");
        public static readonly Color Texto = Color.Parse("#FFFFFF"), TextoSuave = Color.Parse("#C8E6D2"), TextoFraco = Color.Parse("#9DC8AB");
        public static readonly Color Apagado = Color.Parse("#5E8C6C");
        public static readonly Color Destaque = Color.Parse("#F8D734"), DestaqueClaro = Color.Parse("#FFE46A"), Ouro = Color.Parse("#DDA917");
        public static readonly Color SobreDestaque = Color.Parse("#0B4A25");
        public static readonly Color Verde = Color.Parse("#6EE89A"), Vermelho = Color.Parse("#FF8A80"), Laranja = Color.Parse("#FFB566");
        public static readonly Color Azul = Color.Parse("#8EC8FF"), Roxo = Color.Parse("#CBA8FF"), Prata = Color.Parse("#D3E2D8");
        public static readonly Color VerdeForte = Color.Parse("#22A355"), CinzaForte = Color.Parse("#5F806B"), VermelhoForte = Color.Parse("#E5483B");
        // o circulo claro por tras dos logos que somem no verde (Escudos.PrecisaDisco)
        public static readonly Color DiscoLogo = Color.Parse("#E6F0E9");
    }

    static IBrush B(Color c) => new ImmutableSolidColorBrush(c);

    public static readonly IBrush Fundo = B(Cores.Fundo), FundoEscuro = B(Cores.FundoEscuro);
    public static readonly IBrush Barra = B(Cores.Barra), Cartao = B(Cores.Cartao), CartaoForte = B(Cores.CartaoForte);
    public static readonly IBrush Menu = B(Cores.Menu), Borda = B(Cores.Borda), Linha = B(Cores.Linha);
    public static readonly IBrush Zebra = B(Cores.Zebra), Hover = B(Cores.Hover), Selecao = B(Cores.Selecao), Humano = B(Cores.Humano);
    public static readonly IBrush Texto = B(Cores.Texto), TextoSuave = B(Cores.TextoSuave), TextoFraco = B(Cores.TextoFraco), Apagado = B(Cores.Apagado);
    public static readonly IBrush Destaque = B(Cores.Destaque), DestaqueClaro = B(Cores.DestaqueClaro), Ouro = B(Cores.Ouro);
    public static readonly IBrush SobreDestaque = B(Cores.SobreDestaque);
    public static readonly IBrush Verde = B(Cores.Verde), Vermelho = B(Cores.Vermelho), Laranja = B(Cores.Laranja);
    public static readonly IBrush Azul = B(Cores.Azul), Roxo = B(Cores.Roxo), Prata = B(Cores.Prata);
    public static readonly IBrush VerdeForte = B(Cores.VerdeForte), CinzaForte = B(Cores.CinzaForte), VermelhoForte = B(Cores.VermelhoForte);
    public static readonly IBrush DiscoLogo = B(Cores.DiscoLogo);
    // o fundo de um destaque suave (etiquetas amarelas, a aba escolhida)
    public static readonly IBrush DestaqueFundo = new ImmutableSolidColorBrush(Color.FromArgb(0x2E, 0xF8, 0xD7, 0x34));
    public static readonly IBrush Sombra = new ImmutableSolidColorBrush(Color.FromArgb(0x60, 0, 0, 0)), Veu = new ImmutableSolidColorBrush(Color.FromArgb(0x99, 0, 0, 0));

    // o quadro da capa: mais claro no meio de cima, mais escuro nas bordas
    public static readonly IBrush FundoTela = new RadialGradientBrush
    {
        Center = new RelativePoint(0.5, 0.3, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.5, 0.3, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(0.85, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.95, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Cores.FundoClaro, 0), new GradientStop(Cores.Fundo, 0.55), new GradientStop(Color.Parse("#0A4A25"), 1) },
    }.ToImmutable();

    // a pilula do Jogar do hub (de cima para baixo)
    public static readonly IBrush BotaoPrimario = Vertical("#FCE46A", "#F8D734", "#DDA917");
    public static readonly IBrush BotaoPrimarioHover = Vertical("#FFEC8A", "#FFE04A", "#EDB92A");

    static IBrush Vertical(string a, string b, string c) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse(a), 0), new GradientStop(Color.Parse(b), 0.45), new GradientStop(Color.Parse(c), 1) },
    }.ToImmutable();

    // o vidro do hub (TelaHub), para as paginas sobre o FundoTela (inicio, configuracoes, ferramentas):
    // CartaoHub e BarraHub meio transparentes (o quadro aparece por baixo), CirculoIcone o circulo
    // escuro dos icones amarelos, DialogoHub a caixa dos dialogos (quase opaca, com fio Destaque)
    public static readonly IBrush CartaoHub = Alfa(Cores.Cartao, 0.80), BarraHub = Alfa(Cores.Barra, 0.80);
    public static readonly IBrush CirculoIcone = Alfa(Color.Parse("#062A15"), 0.60), DialogoHub = Alfa(Color.Parse("#0A4523"), 0.97);
    // o botao secundario do hub (verde-escuro meio transparente) e o primario desligado
    public static readonly IBrush BotaoSecundario = Alfa(Cores.Barra, 0.55), BotaoDesligado = Alfa(Cores.Barra, 0.70);

    // uma cor do tema com outra opacidade (0..1)
    public static IBrush Alfa(Color c, double a) => new ImmutableSolidColorBrush(Color.FromArgb((byte)System.Math.Round(255 * System.Math.Clamp(a, 0, 1)), c.R, c.G, c.B));
}
