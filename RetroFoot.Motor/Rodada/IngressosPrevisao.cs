using System;
using System.Collections.Generic;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Save;

namespace RetroFoot.Motor.Rodada;

// A previsao do publico e da renda do proximo jogo em casa para um preco de ingresso (so para
// mostrar ao tecnico: nada aqui muda o jogo). O publico de um jogo e decidido quando a
// jornada e criada (Mundo.Publico, seg03:b0a1), antes da tela da equipa:
//  - o jogo desta jornada, se for em casa, ja tem o publico (Jogo.D40), refeito a cada mudanca
//    do preco (Mundo.RecalcularPublico): a previsao e esse publico exato;
//  - nos seguintes, a conta e a mesma do jogo com o sorteio (RandomReal, de 0 a 1) nas duas
//    pontas: o menor e o maior publico possiveis com as forcas e as morais de agora
//    (3 F casa + F fora + 3 r) raiz(W casa) raiz(W fora) 200, elevado a 1,04, x 1,3 na Taca,
//    x a procura do preco (Ingressos), limitado ao estadio.
public static class IngressosPrevisao
{
    public sealed record Previsao(
        ushort Adversario,       // a equipa de fora
        bool Taca,
        bool Definido,           // o jogo desta jornada: o publico exato (com o preco escolhido)
        int Minimo, int Maximo,  // o publico (iguais quando definido)
        int Capacidade,          // o estadio do mandante
        long Preco)              // o preco do ingresso com a escolha (a moeda do save)
    {
        public long RendaMinima => Minimo * Preco;
        public long RendaMaxima => Maximo * Preco;
    }

    // a procura de cada escolha (ingressos.asm), por percentual de 50 a 300
    static readonly float[] Procura = { 2.4623f, 1.4535f, 1.0f, 0.7482f, 0.5903f, 0.4831f, 0.4061f, 0.3485f, 0.3039f, 0.2685f, 0.2397f };

    static double FatorProcura(int percentual)
    {
        int k = Math.Clamp(percentual / Ingressos.Passo, 2, 12);
        return Procura[k - 2];
    }

    // o preco de tabela x a escolha (como Ingressos.Preco, mas para um percentual qualquer)
    public static long PrecoCom(Ext precoTabela, int percentual) =>
        precoTabela.Mul(Ext.Int(Math.Clamp(percentual / Ingressos.Passo, 2, 12)).Mul(Ext.F32(0.25f))).Round();

    // o proximo jogo em casa de e (o desta jornada, se for em casa); null sem nenhum. jogoAtual:
    // o jogo de e nesta jornada (o preco de tabela vem dele); equipa: o id -> Equipa
    public static Previsao? ProximoEmCasa(Equipa e, Jogo? jogoAtual, int percentual, Func<ushort, Equipa?> equipa)
    {
        if (jogoAtual == null) return null;
        var tabela = Ext.Bytes(jogoAtual.B36.AsSpan(0, 10));
        long preco = PrecoCom(tabela, percentual);
        int cap = PreJogo.CapacidadeDoEstadio(e);
        if (jogoAtual.Casa == e.ID && jogoAtual.Taca != TipoJogo.SemEquipas)
        {
            int p = (int)jogoAtual.D40;
            return new Previsao(jogoAtual.Fora, jogoAtual.Taca == TipoJogo.Taca, true, p, p, cap, preco);
        }
        // a primeira entrada ainda nao jogada e a desta jornada (fora): a seguinte em casa
        bool pulouAtual = false;
        foreach (var c in e.Calendario)
        {
            if ((short)c.W0A >= 0) continue;
            if (!pulouAtual) { pulouAtual = true; continue; }
            if (c.W0E != 0) continue;
            var fora = equipa(c.W08);
            if (fora == null) continue;
            bool taca = c.B07 == 1;
            int Conta(double r)
            {
                double b = 3 * Mundo.MediaForca(e) + Mundo.MediaForca(fora) + 3 * r;
                double v = b * Math.Sqrt(Ext.DeReal48(e.Moral).Float64()) * Math.Sqrt(Ext.DeReal48(fora.Moral).Float64()) * 200;
                v = Math.Pow(Math.Max(v, 0), 1.04) * (taca ? 1.3 : 1) * FatorProcura(percentual);
                return Math.Min((int)Math.Round(v), cap);
            }
            return new Previsao(fora.ID, taca, false, Conta(0), Conta(1), cap, preco);
        }
        return null;
    }
}
