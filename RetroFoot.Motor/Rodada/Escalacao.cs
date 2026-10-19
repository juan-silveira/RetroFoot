using RetroFoot.Motor.Base;
using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Rodada.Campos;

namespace RetroFoot.Motor.Rodada;

// uma entrada do menu "Seleccionar" da TprepareGameDlg: vagas passadas a SelectPlayers e
// os numeros que seg04:29ab confere com 4b3e
public sealed record Formacao(string Nome, short G, short D, short M, short A, short CG, short CD, short CM, short CA);

// Escalacao automatica e formacoes (jogo classico, 98.003):
//   seg14:0f68 Embaralhar; seg14:0e0d bolha; seg11:1958 forca + golos decrescente;
//   seg11:1826 posicao, depois nome; seg03:0e4c / 4b3e formacao valida / permitida;
//   seg03:4b73 escalacao; seg03:4388 escalacao da CPU; seg04:29ab menu F1-F12;
//   seg04:2e12 SelectPlayers; seg04:3452 Melhores; seg04:34e6 Automatico.
// A ordem de Equipa.Jogadores e a da TList da equipa (+0x0A): o embaralhamento e a
// ordenacao mudam essa ordem, e o save grava nessa ordem.
// (Traduzido de motor/rodada/escalacao.go.)
public static class Escalacao
{
    public const short EscFora = 0, EscTitular = 1, EscReserva = 2;

    // seg11:1958: forca (+0x0A) + golos (+0x14) em 16 bits; true se a >= b
    static bool PorForcaGolos(Jogador a, Jogador b) =>
        (short)((short)a.Forca + JgW(a, JgGolos14)) >= (short)((short)b.Forca + JgW(b, JgGolos14));

    public static void OrdenarPorForcaGolos(Equipa e) => Listas.Ordenar(e.Jogadores, PorForcaGolos);
    public static void OrdenarPorPosicaoNome(Equipa e) => Partida.Jornada.OrdenarPorPosicaoNome(e);

    static bool Disponivel(Jogador j) => j.JogosLesao == 0 && j.Suspensao == 0;

    // seg03:0e4c: cada disponivel tira 1 da vaga da posicao dele; true se nenhuma vaga
    // ficou > 0
    public static bool FormacaoValida(System.Collections.Generic.IEnumerable<Jogador> jogadores, short g, short d, short m, short a)
    {
        foreach (var j in jogadores)
        {
            if (!Disponivel(j)) continue;
            switch (j.Posicao) { case 0: g--; break; case 1: d--; break; case 2: m--; break; case 3: a--; break; }
        }
        return !(g > 0 || d > 0 || m > 0 || a > 0);
    }

    public static bool FormacaoPermitida(Equipa e, short g, short d, short m, short a) => FormacaoValida(e.Jogadores, g, d, m, a);

    // F1-F12, na ordem do menu (MenuSelectXXXClick, seg04:2f05 ... 30ff; conferencia
    // seg04:29ab). A do 6-4-0 confere 1/6/4/1 no original (seg04:2b6d).
    public static readonly Formacao[] Formacoes =
    {
        new("3-3-4", 1, 3, 3, 4, 1, 3, 3, 4), new("3-4-3", 1, 3, 4, 3, 1, 3, 4, 3),
        new("4-2-4", 1, 4, 2, 4, 1, 4, 2, 4), new("4-3-3", 1, 4, 3, 3, 1, 4, 3, 3),
        new("4-4-2", 1, 4, 4, 2, 1, 4, 4, 2), new("4-5-1", 1, 4, 5, 1, 1, 4, 5, 1),
        new("5-2-3", 1, 5, 2, 3, 1, 5, 2, 3), new("5-3-2", 1, 5, 3, 2, 1, 5, 3, 2),
        new("5-4-1", 1, 5, 4, 1, 1, 5, 4, 1), new("5-5-0", 1, 5, 5, 0, 1, 5, 5, 0),
        new("6-3-1", 1, 6, 3, 1, 1, 6, 3, 1), new("6-4-0", 1, 6, 4, 0, 1, 6, 4, 1),
    };

    // o 5-0-5 do launcher (tools/patch_505.py): sempre habilitado
    public static readonly Formacao Tatica505 = new("5-0-5", 1, 5, 0, 5, 1, 5, 0, 5);

    public static bool[] FormacoesHabilitadas(Equipa e)
    {
        var h = new bool[12];
        for (int i = 0; i < 12; i++) { var f = Formacoes[i]; h[i] = FormacaoPermitida(e, f.CG, f.CD, f.CM, f.CA); }
        return h;
    }

    // seg03:4b73 (devolve sempre true; nao usa o Random). g, d, m, a: vagas de titular;
    // banco liga a montagem do banco.
    //  1. +0x4D (reservas) = 0; vagas = (g, d, m, a); livres = 0; vagasBanco = 0; linha = 0.
    //  2. cada disponivel: fora -> livres++; titular -> vagas--, linha++ se nao e goleiro;
    //     reserva -> vagasBanco--, reservas++.
    //  3. cada posicao: livres > vagas -> vagasBanco = 1.
    //  4. ordena por forca+golos decrescente.
    //  5. na ordem: vagas > 0 -> o disponivel e fora vira titular se goleiro ou linha < 10;
    //     senao, com banco, o disponivel e fora com vagasBanco > 0 vira reserva se reservas
    //     < 4 (< 5 para goleiro).
    //  6. com banco: enquanto reservas < 5, cada jogador de linha disponivel e fora vira reserva.
    //  7. +0x4D = Min(reservas, 3).
    // o banco e as substituicoes do jogo em curso (Regras.MaxReservas/MaxSubstituicoes; quem
    // abre o jogo acerta). 5 e 3 sao os do classico e deixam o motor igual ao original
    public static int MaxReservas = 5, MaxSubstituicoes = 3;

    public static bool Escalar(Equipa e, short g, short d, short m, short a, bool banco, bool p0A)
    {
        var vagas = new short[] { g, d, m, a };
        var livres = new short[4];
        var vagasBanco = new short[4];
        short linha = 0;
        SetEqW(e, EqSubst, 0);
        foreach (var j in e.Jogadores)
        {
            if (!Disponivel(j)) continue;
            int pos = j.Posicao;
            switch (JgW(j, JgEscalacao))
            {
                case EscFora: livres[pos]++; break;
                case EscTitular: vagas[pos]--; if (pos != 0) linha++; break;
                case EscReserva: vagasBanco[pos]--; AddW(e, EqSubst, 1); break;
            }
        }
        for (int p = 0; p <= 3; p++)
            if (livres[p] > vagas[p]) vagasBanco[p] = 1;

        OrdenarPorForcaGolos(e);

        foreach (var j in e.Jogadores)
        {
            int pos = j.Posicao;
            if (vagas[pos] > 0)
            {
                if (Disponivel(j) && JgW(j, JgEscalacao) == EscFora && (pos == 0 || linha < 10))
                {
                    SetJgW(j, JgEscalacao, EscTitular);
                    vagas[pos]--;
                    if (pos != 0) linha++;
                }
            }
            else if (banco && Disponivel(j) && JgW(j, JgEscalacao) == EscFora && vagasBanco[pos] > 0)
            {
                short lim = (short)(pos == 0 ? MaxReservas : MaxReservas - 1);
                if (lim > EqW(e, EqSubst))
                {
                    SetJgW(j, JgEscalacao, EscReserva);
                    vagasBanco[pos]--;
                    AddW(e, EqSubst, 1);
                }
            }
        }
        if (banco)
            foreach (var j in e.Jogadores)
                if (EqW(e, EqSubst) < MaxReservas && Disponivel(j) && JgW(j, JgEscalacao) == EscFora && j.Posicao != 0)
                {
                    SetJgW(j, JgEscalacao, EscReserva);
                    AddW(e, EqSubst, 1);
                }
        if (EqW(e, EqSubst) > MaxSubstituicoes) SetEqW(e, EqSubst, (short)MaxSubstituicoes);
        return true;
    }

    // seg03:4388: Embaralhar; Escalar(1, 3, 3, 2, sem banco); Escalar(1, 10, 10, 10, com
    // banco). Nao zera a escalacao: quem ja estiver escalado conta nas vagas.
    public static void EscalacaoCPU(Aleatorio r, Equipa e)
    {
        Listas.Embaralhar(r, e.Jogadores);
        Escalar(e, 1, 3, 3, 2, false, true);
        Escalar(e, 1, 10, 10, 10, true, false);
    }

    // o laco do comeco de SelectPlayers (seg04:2e40) e dos botoes Melhores/Automatico
    public static void LimparEscalacao(Equipa e)
    {
        foreach (var j in e.Jogadores) SetJgW(j, JgEscalacao, EscFora);
    }

    // seg04:2e12 sem a interface: zera, embaralha, Escalar e ordena por posicao e nome
    public static void SelecionarJogadores(Aleatorio r, Equipa e, short g, short d, short m, short a, bool banco, bool p0A)
    {
        LimparEscalacao(e);
        Listas.Embaralhar(r, e.Jogadores);
        Escalar(e, g, d, m, a, banco, p0A);
        OrdenarPorPosicaoNome(e);
    }

    // MenuSelectXXXClick: a formacao F(n), n de 1 a 12
    public static void EscalarFormacao(Aleatorio r, Equipa e, int n)
    {
        var f = Formacoes[n - 1];
        SelecionarJogadores(r, e, f.G, f.D, f.M, f.A, true, true);
    }

    public static void EscalarTatica505(Aleatorio r, Equipa e) =>
        SelecionarJogadores(r, e, Tatica505.G, Tatica505.D, Tatica505.M, Tatica505.A, true, true);

    // seg04:3452
    public static void Melhores(Aleatorio r, Equipa e)
    {
        LimparEscalacao(e);
        SelecionarJogadores(r, e, 1, 10, 10, 10, true, true);
    }

    // seg04:34e6: zera; Embaralhar; v[i] = Max(0, Min(byte[+0x83+i] - 1, 3)) para i = 1..3;
    // SelecionarJogadores(0, v1, v2, v3, sem banco); SelecionarJogadores(1, 10, 10, 10, com
    // banco). A 2a chamada zera tudo de novo: a 1a so gasta Random.
    public static void Automatico(Aleatorio r, Equipa e)
    {
        LimparEscalacao(e);
        Listas.Embaralhar(r, e.Jogadores);
        var v = new short[4];
        for (int i = 1; i <= 3; i++) v[i] = (short)System.Math.Clamp(EqB(e, 0x83 + i) - 1, 0, 3);
        SelecionarJogadores(r, e, 0, v[1], v[2], v[3], false, true);
        SelecionarJogadores(r, e, 1, 10, 10, 10, true, false);
    }
}
