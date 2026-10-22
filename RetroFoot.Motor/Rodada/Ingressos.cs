using RetroFoot.Motor.Base;
using RetroFoot.Motor.Save;

namespace RetroFoot.Motor.Rodada;

// Preco dos ingressos escolhido pelo tecnico (patch do launcher: tools/divisoes/
// ingressos.py e ingressos.asm; docs internos do launcher, secao 12).
//
// O TTeam do launcher ganha um byte em +0xCB, que vai para o save no byte 4 do cabecalho
// "EFa" de 50 bytes da equipa (Equipa.Cabeca[4]; o jogo original o grava como zero):
// 0 = nao escolhido (100%), 2 a 12 = a escolha em quartos (50% a 300%, de 25 em 25).
// Fora de 2..12 vale como nao escolhido. Nos jogos em que o mandante tem escolha:
//  - renda: o preco do jogo (+0x36) x escolha/4, nas 3 leituras da bilheteira
//    (seg03:93C4 no campeonato, 98A6/98DB na Taca);
//  - publico: x (escolha/4)^-1,3 (tabela de floats) antes do Round de seg03:B1EC, isto e,
//    depois do x1,3 da Taca e antes do limite do estadio.
// Com o byte em zero (o jogo original, ou o tecnico que nao mexe) nada muda.
//
// RetroFoot: o preco muda de $1 em $1 (nao mais de 25% em 25%). A razao do preco sobre o de
// tabela fica em decimos de milesimo (10000 = 100%, de 5000 a 30000) num ushort nos bytes 26
// e 27 do cabecalho (o launcher e o jogo original os gravam zerados); o byte 4 leva o quarto
// mais proximo (para quem le so ele). Com a razao gravada:
//  - renda: o preco de tabela x razao, arredondado (o preco mostrado e o cobrado);
//  - publico: x razao^-1,3 (nos quartos, os floats da tabela do launcher).
public static class Ingressos
{
    public const int CabecaByte = 4, RazaoByte = 26;
    public const int Minimo = 50, Maximo = 300, Passo = 25;
    public const int Cem = 10000, RazaoMinima = Minimo * 100, RazaoMaxima = Maximo * 100;

    // a razao fina gravada (5000 a 30000), ou 0 se nao ha
    static int Fina(Equipa e)
    {
        int v = e.Cabeca[RazaoByte] | e.Cabeca[RazaoByte + 1] << 8;
        return v is >= RazaoMinima and <= RazaoMaxima ? v : 0;
    }

    // a razao do preco sobre o de tabela, em decimos de milesimo (10000 sem escolha)
    public static int Razao(Equipa e)
    {
        int f = Fina(e);
        if (f != 0) return f;
        byte k = Escolha(e);
        return k == 0 ? Cem : k * 2500;
    }

    // o preco do ingresso de um jogo com a razao (arredondado: o que se mostra e se cobra)
    public static long PrecoCom(Ext precoTabela, int razao) =>
        precoTabela.Mul(Ext.Int(razao)).Div(Ext.Int(Cem)).Round();

    // o fator da procura com a razao: a tabela do launcher nos quartos, razao^-1,3 no meio
    public static Ext FatorProcura(int razao)
    {
        razao = System.Math.Clamp(razao, RazaoMinima, RazaoMaxima);
        if (razao % 2500 == 0) return Procura[razao / 2500 - 2];
        return Ext.F64(System.Math.Pow(razao / (double)Cem, -1.3));
    }

    // o -/+ da tela da equipa: o preco de $1 em $1 (passos), de metade ate o triplo do de tabela
    public static void MudarPreco(Equipa e, Ext precoTabela, int passos)
    {
        double tab = precoTabela.Float64();
        if (tab <= 0) return;
        long atual = PrecoCom(precoTabela, Razao(e));
        long min = (long)System.Math.Ceiling(tab * Minimo / 100), max = (long)System.Math.Floor(tab * Maximo / 100);
        long novo = System.Math.Clamp(atual + passos, min, System.Math.Max(min, max));
        int r = System.Math.Clamp((int)System.Math.Round(novo * Cem / tab), RazaoMinima, RazaoMaxima);
        // o arredondamento da razao nao pode voltar ao mesmo preco
        while (PrecoCom(precoTabela, r) < novo && r < RazaoMaxima) r++;
        while (PrecoCom(precoTabela, r) > novo && r > RazaoMinima) r--;
        GravarRazao(e, r);
    }

    internal static void GravarRazao(Equipa e, int r)
    {
        e.Cabeca[RazaoByte] = (byte)r; e.Cabeca[RazaoByte + 1] = (byte)(r >> 8);
        e.Cabeca[CabecaByte] = (byte)System.Math.Clamp((r + 1250) / 2500, 2, 12);
    }

    // a escolha guardada (2 a 12), ou 0 se nao ha
    public static byte Escolha(Equipa? e)
    {
        if (e == null) return 0;
        byte b = e.Cabeca[CabecaByte];
        return b is >= 2 and <= 12 ? b : (byte)0;
    }

    // o % do preco de tabela (100 sem escolha)
    public static int Percentual(Equipa e) => (Razao(e) + 50) / 100;

    // o campo do launcher (abas.asm): o valor arredondado para 25, de 50 a 300; o byte
    // guarda valor/25 (100% vira 4, nao 0)
    public static void Escolher(Equipa e, int percentual)
    {
        int p = System.Math.Clamp((percentual + Passo / 2) / Passo * Passo, Minimo, Maximo);
        e.Cabeca[CabecaByte] = (byte)(p / Passo);
        e.Cabeca[RazaoByte] = e.Cabeca[RazaoByte + 1] = 0;
    }

    // "quarto: dd 0.25" e "procura: dd 2.4623, ..." (ingressos.asm)
    static readonly Ext Quarto = Ext.F32(0.25f);
    static readonly Ext[] Procura =
    {
        Ext.F32(2.4623f), Ext.F32(1.4535f), Ext.F32(1.0f), Ext.F32(0.7482f), Ext.F32(0.5903f),
        Ext.F32(0.4831f), Ext.F32(0.4061f), Ext.F32(0.3485f), Ext.F32(0.3039f), Ext.F32(0.2685f),
        Ext.F32(0.2397f),
    };

    // preco: fld tword [jogo+0x36]; fild word escolha; fmul dword 0,25; fmulp
    public static Ext Preco(Ext preco, Equipa? mandante)
    {
        if (mandante != null && Fina(mandante) is var f and not 0) return Ext.Int(PrecoCom(preco, f));
        byte k = Escolha(mandante);
        return k == 0 ? preco : preco.Mul(Ext.Int(k).Mul(Quarto));
    }

    // publico: fmul dword [procura + 4 (escolha - 2)] antes do Round
    public static Ext Publico(Ext publico, Equipa? mandante)
    {
        if (mandante != null && Fina(mandante) is var f and not 0) return publico.Mul(FatorProcura(f));
        byte k = Escolha(mandante);
        return k == 0 ? publico : publico.Mul(Procura[k - 2]);
    }
}
