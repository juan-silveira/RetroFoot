using System;
using System.Collections.Generic;
using System.Linq;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Partida.Memoria;

namespace RetroFoot.Motor.Partida;

// O sorteio de jogadores, a forca dos setores e a forca media. (Traduzido de
// motor/partida/sorteio.go.)
public static class Sorteio
{
    // modos de peso de seg03:0f2e
    public const int PesoForca = 1, PesoGol = 2, PesoComportamento = 3, PesoLesao = 4;

    // seg03:0f2e: o peso do jogador para o sorteio, em 32 bits com sinal. No modo gol cada
    // multiplicacao guarda so os 16 bits de baixo e o resultado e estendido com sinal (cwd):
    // forca alta pode dar peso negativo
    public static int PesoDoJogador(Jogador j, int modo)
    {
        switch (modo)
        {
            case PesoForca: return (short)j.Forca;
            case PesoGol:
                ushort p = j.Posicao;
                ushort v = (ushort)(p * p);
                v = (ushort)(v * j.Forca);
                v = (ushort)(v * j.Nota);
                return (short)v;
            case PesoComportamento: return (short)j.Comportamento;
            case PesoLesao: return (short)j.Lesao;
        }
        return 0;
    }

    // seg03:0fc8: com filtro 1, so titulares; com qualquer outro, ninguem
    static bool Elegivel(Jogador j, int filtro) => filtro == 1 && Escalacao(j) == EscTitular;

    // seg03:100b: a soma dos pesos e de TODO o elenco, mas so os elegiveis podem ser
    // escolhidos. r = Random(soma) (so os 16 bits de baixo); o 1o elegivel cuja soma
    // acumulada (dos elegiveis) passa de r. Se r cair alem, ninguem (null).
    public static Jogador? SorteiaJogador(Aleatorio r, IList<Jogador> elenco, int filtro, int modo)
    {
        int total = 0;
        foreach (var j in elenco) total += PesoDoJogador(j, modo);
        int sorteio = r.Random((ushort)total);
        int acum = 0;
        Jogador? escolhido = null;
        foreach (var j in elenco)
            if (Elegivel(j, filtro))
            {
                acum += PesoDoJogador(j, modo);
                if (acum > sorteio && escolhido == null) escolhido = j;
            }
        return escolhido;
    }

    // a TList +0x0A da equipa, na ordem dela
    public static List<Jogador> Elenco(Equipa e) => e.Jogadores;

    // seg03:397d: zera +0x51..+0x57 e soma, para cada titular, forca + 2 x estrela +
    // 3 x (pais do jogador = pais da equipa) no setor da posicao. 16 bits; estrela com sinal
    public static void ForcaDosSetores(Equipa e)
    {
        for (int k = 0; k < 4; k++) Poe16(e.B4D, 4 + 2 * k, 0);
        var pe = PaisDaEquipa(e);
        foreach (var j in e.Jogadores)
        {
            if (Escalacao(j) != EscTitular) continue;
            ushort v = (ushort)(PaisDoJogador(j).AsSpan().SequenceEqual(pe) ? 3 : 0);
            v += j.Forca;
            v += (ushort)(2 * (sbyte)j.Estrela);
            int o = 4 + 2 * j.Posicao;
            if (o + 2 <= e.B4D.Length) Poe16(e.B4D, o, (ushort)(Le16(e.B4D, o) + v));
        }
    }

    // seg03:4ec3: Round(soma das forcas de todo o elenco / quantidade); soma de 16 bits
    public static short ForcaMedia(Equipa e)
    {
        short s = 0;
        foreach (var j in e.Jogadores) s += (short)j.Forca;
        return (short)Fpu.Arredondar(Fpu.Divide(Ext.Int(s), Ext.Int((short)e.Jogadores.Count)));
    }

    // seg03:49e2: sorteia um jogador do elenco (Random(quantidade)) e mexe no contador +0x0C:
    // -1 se titular, +1 se nao
    public static void EvolucaoContador(Aleatorio r, Equipa e)
    {
        int i = r.Random(e.Jogadores.Count);
        if (i >= e.Jogadores.Count) return;
        var j = e.Jogadores[i];
        MudarContador(j, (short)(ContadorEvolucao(j) + (Escalacao(j) == EscTitular ? -1 : 1)));
    }
}
