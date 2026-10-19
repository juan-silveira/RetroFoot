using System;
using System.Collections.Generic;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Partida.Memoria;
using static RetroFoot.Motor.Partida.PreJogo;
using static RetroFoot.Motor.Partida.Sorteio;

namespace RetroFoot.Motor.Partida;

// resultados de uma cobranca (seg03:81ed), com o texto da janela de penalti (tabela de
// string[10] em DS:177A, indice = resultado; 0 = "Nao marcado")
public static class ResultadoPenalti
{
    public const int Gol = 1, Defesa = 2, Poste = 3, Barra = 4, AoLado = 5, PorCima = 6;
}

// uma linha da janela de penaltis
public sealed record Cobranca(int Lado, Jogador Batedor, int Resultado);

// o resultado de seg11:0d46
public sealed class Desempate
{
    public bool PassaVisitante;            // o valor devolvido (al != 0: passa o visitante)
    public bool Rapido;                    // decidido pelos 3 sorteios, sem cobrancas
    public short[] Somas = new short[2];
    public short[] Golos = new short[2];
    public List<Cobranca> Cobrancas = new();
}

public static class Penaltis
{
    // seg03:818c: P = Round((posicao + 1) x forca x (Nota/10 + 1) / 2)
    // (posicao + 1) x forca em 16 bits sem sinal, depois lido com sinal; o resto em Extended;
    // Round arredonda empates para o par (4,5 -> 4)
    public static short ValorBatedor(Jogador j)
    {
        short a = (short)(ushort)((j.Posicao + 1) * j.Forca);
        var f = Fpu.Soma(Fpu.Divide(Ext.Int((short)j.Nota), Dez), Um);
        return (short)Fpu.Arredondar(Fpu.Divide(Fpu.Mult(Ext.Int(a), f), Dois));
    }

    // seg14:0ee6: percorre a lista e fica com o item para o qual melhor(item, atual) e
    // verdadeiro; o primeiro entra sempre
    static Jogador? Selecionar(List<Jogador> l, Func<Jogador, Jogador, bool> melhor)
    {
        Jogador? atual = null;
        foreach (var c in l)
            if (atual == null || melhor(c, atual)) atual = c;
        return atual;
    }

    // seg11:1f88: c e melhor se for titular e tiver posicao menor, ou a mesma posicao e mais
    // forca (com sinal)
    static bool MelhorGoleiro(Jogador c, Jogador atual)
    {
        if (c.Posicao < atual.Posicao) return Escalacao(c) == EscTitular;
        if (c.Posicao == atual.Posicao && (short)c.Forca > (short)atual.Forca) return Escalacao(c) == EscTitular;
        return false;
    }

    // seg11:201d: se o atual nao e titular, c ganha; se c nao e titular, perde; entre
    // titulares, ganha quem tiver P maior ou igual (o ultimo de um empate)
    internal static bool MelhorBatedor(Jogador c, Jogador atual)
    {
        if (Escalacao(atual) != EscTitular) return true;
        if (Escalacao(c) != EscTitular) return false;
        return ValorBatedor(c) >= ValorBatedor(atual);
    }

    // seg03:67c1: o "goleiro" para defender um penalti. O 1o jogador da lista entra sempre
    // como candidato, mesmo que nao seja titular, e so e trocado por um titular de posicao
    // menor, ou da mesma posicao com mais forca.
    public static Jogador? Goleiro(Equipa e) => Selecionar(Elenco(e), MelhorGoleiro);

    // seg14:0ee6 com seg11:201d: o titular de maior P (o ultimo, em empate)
    public static Jogador? MelhorBatedor(Equipa e) => Selecionar(Elenco(e), MelhorBatedor);

    // seg03:81ed:
    //   chute = Random(P do batedor); defesa = Random(forca do goleiro)
    //   se Random(4) = 0: chute < 4 ao lado, < 10 por cima, < 12 na barra, < 13 no poste
    //   senao (ou se nenhum desses): gol se chute >= defesa, defesa se nao
    public static int CobrarPenalti(Aleatorio r, Jogador goleiro, Jogador batedor)
    {
        short chute = (short)r.Random((ushort)ValorBatedor(batedor));
        short defesa = (short)r.Random(goleiro.Forca);
        int res = 0;
        if (r.Random(4) == 0)
            res = chute < 4 ? ResultadoPenalti.AoLado
                : chute < 10 ? ResultadoPenalti.PorCima
                : chute < 12 ? ResultadoPenalti.Barra
                : chute < 13 ? ResultadoPenalti.Poste : 0;
        if (res == 0) res = chute < defesa ? ResultadoPenalti.Defesa : ResultadoPenalti.Gol;
        return res;
    }

    internal static List<Jogador> Titulares(Equipa e) => Elenco(e).FindAll(j => Escalacao(j) == EscTitular);

    // seg14:0e0d com seg11:201d: ordena a lista de jogadores DA EQUIPA (para sempre) por um
    // bubble sort: para i de 1 a n-1 e j de n-1 ate i, troca j-1 e j se MelhorBatedor(l[j-1],
    // l[j]) for falso. Titulares primeiro, por P decrescente.
    public static void OrdenarBatedores(Equipa e)
    {
        var l = e.Jogadores;
        int n = l.Count;
        for (int i = 1; i <= n - 1; i++)
            for (int j = n - 1; j >= i; j--)
                if (!MelhorBatedor(l[j - 1], l[j])) (l[j - 1], l[j]) = (l[j], l[j - 1]);
    }

    // seg11:0d46, no fim de um jogo da Taca empatado (depois da prorrogacao), por seg03:9a15.
    // jornada e DS:2B40; jornadas, o numero de jornadas da temporada (14 no original;
    // seg11:0da2 no launcher); verPenaltis e a opcao viewComputerPenalties (DS:2B33).
    //
    // Modo rapido (opcao desligada, nenhum humano e jornada < jornadas): cada equipa soma 3
    // sorteios de Random(forca media do elenco), a casa primeiro; "passa o visitante" se a
    // soma da casa for MAIOR OU IGUAL a do visitante (passa quem tirou a soma MENOR; assim
    // esta no codigo).
    //
    // Senao, cobrancas: as listas das duas equipas sao reordenadas (OrdenarBatedores,
    // permanente) e batem alternadas, a casa primeiro, cada lado percorrendo a sua lista em
    // ciclo e pulando quem nao e titular, contra Goleiro(adversario). Para quando, depois de
    // uma cobranca: o visitante ja bateu mais de 5 e o placar esta diferente; ou uma equipa
    // esta a frente por mais do que as cobrancas que faltam a outra (das 5), com quem acabou
    // de bater tendo no maximo 5. Passa o visitante se fizer pelo menos tantos gols quanto a casa.
    //
    // O original marca ainda o calendario das duas equipas (seg11:0cf2: +0x06 = 2 para quem
    // passa, 3 para quem cai); isso fica para quem chama.
    public static Desempate DesempateTaca(Aleatorio r, JogoAtual jg, bool verPenaltis, int jornada, int jornadas)
    {
        var d = new Desempate();
        var eqs = new[] { jg.Equipa[Lado.Casa]!, jg.Equipa[Lado.Visit]! };
        if (!verPenaltis && !jg.HumanoNoLado(Lado.Casa) && !jg.HumanoNoLado(Lado.Visit) && jornada < jornadas)
        {
            d.Rapido = true;
            for (int lado = 0; lado < 2; lado++)
            {
                int f = ForcaMedia(eqs[lado]);
                short a = (short)r.Random(f), b = (short)r.Random(f), c = (short)r.Random(f);
                d.Somas[lado] = (short)(c + b + a);
            }
            d.PassaVisitante = d.Somas[Lado.Casa] >= d.Somas[Lado.Visit];
            return d;
        }
        OrdenarBatedores(eqs[0]);
        OrdenarBatedores(eqs[1]);
        if (Titulares(eqs[0]).Count == 0 || Titulares(eqs[1]).Count == 0)
            throw new InvalidOperationException("partida: penaltis sem titulares (o original fica em laco infinito)");
        var cobradas = new short[2];
        var prox = new int[2];
        for (int lado = Lado.Casa; ; lado = 1 - lado)
        {
            var e = eqs[lado];
            int n = e.Jogadores.Count;
            while (Escalacao(e.Jogadores[prox[lado]]) != EscTitular)
                if (++prox[lado] >= n) prox[lado] = 0;
            var b = e.Jogadores[prox[lado]];
            if (++prox[lado] >= n) prox[lado] = 0;
            int res = CobrarPenalti(r, Goleiro(eqs[1 - lado])!, b);
            if (res == ResultadoPenalti.Gol) d.Golos[lado]++;
            cobradas[lado]++;
            d.Cobrancas.Add(new Cobranca(lado, b, res));

            short gc = d.Golos[Lado.Casa], gf = d.Golos[Lado.Visit];
            bool fim = (lado != Lado.Casa && cobradas[lado] > 5 && gc != gf)
                || (gc - gf > 5 - cobradas[Lado.Visit] && cobradas[lado] <= 5)
                || (gf - gc > 5 - cobradas[Lado.Casa] && cobradas[lado] <= 5);
            if (fim) break;
        }
        d.PassaVisitante = d.Golos[Lado.Visit] >= d.Golos[Lado.Casa];
        return d;
    }
}

public sealed partial class Jornada
{
    // seg08:39b6: um penalti para atacante contra defensor; devolve se foi gol e o batedor.
    // Com humano em qualquer dos lados o original abre a janela de penalti (seg08:328b), que
    // faz a mesma conta; se o humano e quem cobra, ele escolhe o batedor (gancho Batedor).
    public (bool Gol, Jogador? Batedor) Penalti(Equipa atacante, Equipa defensor, Treinador? tAtac, Treinador? tDef)
    {
        var batedor = Penaltis.MelhorBatedor(atacante);
        if (Humano(tAtac) && Ganchos.Batedor != null)
        {
            var b = Ganchos.Batedor(atacante, Penaltis.Titulares(atacante), batedor);
            if (b != null) batedor = b;
        }
        var gk = Penaltis.Goleiro(defensor);
        ultimoGoleiro = gk; ultimoResultado = 0;
        if (batedor == null || gk == null) return (false, batedor);   // lista vazia: no original, acesso a nil
        int res = Penaltis.CobrarPenalti(R, gk, batedor);
        ultimoResultado = res;
        if (Humano(tAtac) || Humano(tDef)) Ganchos.Cobranca?.Invoke(atacante, defensor, batedor, res);
        return (res == ResultadoPenalti.Gol, batedor);
    }
}
