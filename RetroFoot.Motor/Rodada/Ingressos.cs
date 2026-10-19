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
public static class Ingressos
{
    public const int CabecaByte = 4;
    public const int Minimo = 50, Maximo = 300, Passo = 25;

    // a escolha guardada (2 a 12), ou 0 se nao ha
    public static byte Escolha(Equipa? e)
    {
        if (e == null) return 0;
        byte b = e.Cabeca[CabecaByte];
        return b is >= 2 and <= 12 ? b : (byte)0;
    }

    // o % mostrado no campo (100 sem escolha)
    public static int Percentual(Equipa e)
    {
        byte k = Escolha(e);
        return k == 0 ? 100 : k * 25;
    }

    // o campo do launcher (abas.asm): o valor arredondado para 25, de 50 a 300; o byte
    // guarda valor/25 (100% vira 4, nao 0)
    public static void Escolher(Equipa e, int percentual)
    {
        int p = System.Math.Clamp((percentual + Passo / 2) / Passo * Passo, Minimo, Maximo);
        e.Cabeca[CabecaByte] = (byte)(p / Passo);
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
        byte k = Escolha(mandante);
        return k == 0 ? preco : preco.Mul(Ext.Int(k).Mul(Quarto));
    }

    // publico: fmul dword [procura + 4 (escolha - 2)] antes do Round
    public static Ext Publico(Ext publico, Equipa? mandante)
    {
        byte k = Escolha(mandante);
        return k == 0 ? publico : publico.Mul(Procura[k - 2]);
    }
}
