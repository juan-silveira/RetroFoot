using System;
using System.Collections.Generic;
using System.Linq;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Rodada.X87;
using TRegras = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.Motor.Temporada;

// o que o fim de temporada fez (para a interface mostrar)
public sealed class Resultado
{
    public ushort Campeao, VencedorTaca;
    public List<ushort>[] Subiram = Enumerable.Range(0, 5).Select(_ => new List<ushort>()).ToArray();
    public List<ushort>[] Desceram = Enumerable.Range(0, 5).Select(_ => new List<ushort>()).ToArray();
}

// Comeco e fim da temporada (subida e descida, campeoes, premios, o que se zera) e as
// contas da Taca, como o jogo classico (98.003) faz (seg05 e as rotinas de seg03 que ele chama).
// (Traduzido de motor/temporada.)
public sealed class Epoca
{
    public MercadoJogo Mc;
    public Jogo98 S => Mc.S;
    public Aleatorio G => Mc.G;
    public TRegras Regras { get => Mc.Regras; set => Mc.Regras = value; }

    // 3:4f40 (troca de treinador): o fim de temporada chama-a com (equipa, 0x400) quando o
    // humano desce para o Distrital (5:037d)
    public Action<Equipa, uint>? Chicotada;
    // as janelas que pedem decisao do humano e a lista das chicotadas (DS:2716)
    public Interacao? Interacao;
    // DS:2B3A: 2 = sempre, 1 = so com humano na Taca, outro = nunca (ver OrdenarTacaPorNome)
    public int MostrarSorteio;
    // seg05:0AA8, logo antes dos Premios (o "call far premios" que o patch dos patrocinios do
    // launcher troca pela rotina que paga os contratos)
    public Action? AntesDosPremios;

    public Epoca(Jogo98 s, Aleatorio g)
    {
        Mc = new MercadoJogo(s, g);
        Chicotada = (e, flags) => Mc.ChicotadaPsicologica(e, flags, Interacao);
    }

    // divisoes 0 a 3 = 1a a 4a, 4 = Distrital (a ordem de DS:26FA)
    public const int Distrital = 4;

    const int B16Ordem = 0x22 - 0x16, B16Taca = 0x30 - 0x16, B16Balanco = 0x31 - 0x16;

    // 5:0002 (quando a temporada comeca do zero, DS:271E = 0):
    //  1. a lista da Taca (DS:26F2) recebe todas as equipas, cada uma com +0x30 = 0;
    //  2. para cada divisao 0..4 e cada equipa, 3:38a9 com "tem calendario" = d < 4;
    //  3. cada jogador fica com +0x14 (golos na epoca) e +0x16 (salario revisto) zerados;
    //  4. DS:271E = 0, DS:271C = 1, DS:271A = 0. Nao usa Random.
    public void NovaTemporada()
    {
        S.OutrasEquipas.Clear();
        foreach (var e in S.Equipas)
        {
            S.OutrasEquipas.Add(e.ID);
            e.B16[B16Taca] = 0;
        }
        for (int d = 0; d < S.Divisoes.Count && d <= Distrital; d++)
        {
            var lista = S.Divisoes[d].Equipas;
            for (int idx = 0; idx < lista.Count; idx++)
                if (MercadoJogo.Equipa(S, lista[idx]) is { } e) NovaTemporadaEquipa(Regras, e, d < Distrital, idx, lista);
        }
        foreach (var e in S.Equipas)
            foreach (var p in e.Jogadores) Array.Clear(p.B14);
        S.Cabecalho.G271E = 0;
        S.Cabecalho.G271C = 1;
        S.Cabecalho.G271A = 0;
    }

    // 3:38a9: +0xA5 <- +0x89; +0x89 <- +0x31 (28 bytes); +0x16..+0x4C <- zeros; com
    // calendario (1a a 4a), a linha idx de DS:17C8 em +0x22 e o calendario de 3:1d20; no
    // Distrital so esvazia o calendario
    public static void NovaTemporadaEquipa(TRegras rg, Equipa e, bool comCalendario, int idx, List<ushort> divisao)
    {
        Array.Copy(e.B89, e.BA5, 28);
        Array.Copy(e.B16, B16Balanco, e.B89, 0, 28);
        Array.Clear(e.B16);
        if (comCalendario)
        {
            var (l, ok) = rg.LugaresIniciais(idx);
            if (ok) Array.Copy(l, 0, e.B16, B16Ordem, 14);
            e.Calendario = Calendario(rg, idx, divisao);
        }
        else e.Calendario = new();
    }

    // 3:1d20: as entradas do campeonato da equipa na posicao idx (Regras.Adversario): +0x04 =
    // jornada, +0x08 = adversario, +0x0A = +0x0C = -1, +0x0E = 1 se fora, +0x10 = -1
    public static List<EntradaCalendario> Calendario(TRegras rg, int idx, List<ushort> divisao)
    {
        var r = new List<EntradaCalendario>();
        for (int j = 1; j <= rg.Jornadas; j++)
        {
            var (adv, fora, ok) = rg.Adversario(idx, j);
            if (!ok) return r;
            r.Add(new EntradaCalendario
            {
                W04 = (ushort)j, W0A = 0xFFFF, W0C = 0xFFFF, D10 = 0xFFFFFFFF,
                W08 = adv < divisao.Count ? divisao[adv] : (ushort)0, W0E = (ushort)(fora ? 1 : 0),
            });
        }
        return r;
    }

    // 5:0148 (ao criar o jogo novo): o treinador de cada equipa ganha o evento 5 (entrou)
    public void EventosIniciais()
    {
        uint ano = (uint)S.Cabecalho.Ano;
        foreach (var e in S.Equipas)
            if (MercadoJogo.Treinador(S, e.Treinador) is { } c)
                MercadoJogo.AcrescentarLimitado(c.Eventos, new EventoTreinador { B04 = 5, D05 = ano, W09 = e.ID }, MercadoJogo.LimiteEventosTreinador);
    }

    // tipos de evento gravados no fim da temporada
    public const byte EvEquipaCampeao = 1, EvEquipaTaca = 2, EvEquipaDobradinha = 3, EvEquipaVice = 4, EvEquipaTerceiro = 5,
        EvTreinadorCampeao = 3, EvTreinadorTaca = 4, EvTreinadorEntrou = 5, EvTreinadorDobradinha = 8,
        EvTreinadorVice = 9, EvTreinadorTerceiro = 10;

    public const int LimiteCampeoes = 20;   // DS:270E (14:06a2 com 0x14)

    // 5:038e (depois da ultima jornada, se o jogo nao foi abortado):
    //  1. campeao = 1a da lista da 1a divisao; vencedor da Taca = a unica de DS:26F2;
    //  2. moral: campeao += 1,0; vencedor da Taca += 0,5;
    //  3. equipa: +0xC1 e +0xC3 += 1; treinador: +0x16 e +0x18 += 1;
    //  4. registro de campeoes (3:ca1a, <= 20);
    //  5. eventos (dobradinha se for a mesma equipa; 2o e 3o lugares);
    //  6. subida e descida (com os Random do Distrital);
    //  7. 5:025a: chicotadas mostradas e esvaziadas;
    //  8. DS:271E = 0, DS:271A = 0, ano += 1;
    //  9. 5:2bf6 (TEndOfSeasonDlg): premios.
    public Resultado FimDeTemporada()
    {
        var r = new Resultado();
        uint ano = (uint)S.Cabecalho.Ano;
        var camp = MercadoJogo.Equipa(S, S.Divisoes[0].Equipas[0])!;
        var taca = MercadoJogo.Equipa(S, S.OutrasEquipas[0])!;
        r.Campeao = camp.ID; r.VencedorTaca = taca.ID;

        SomaMoral(camp, 1.0);
        SomaMoral(taca, 0.5);
        camp.DC1 = camp.DC1 & 0xFFFF0000 | (ushort)((ushort)camp.DC1 + 1);
        taca.DC1 = taca.DC1 & 0x0000FFFF | (uint)(ushort)((ushort)(taca.DC1 >> 16) + 1) << 16;
        var tc = MercadoJogo.Treinador(S, camp.Treinador)!;
        var tt = MercadoJogo.Treinador(S, taca.Treinador)!;
        tc.D16 = tc.D16 & 0xFFFF0000 | (ushort)((ushort)tc.D16 + 1);
        tt.D16 = tt.D16 & 0x0000FFFF | (uint)(ushort)((ushort)(tt.D16 >> 16) + 1) << 16;

        MercadoJogo.AcrescentarLimitado(S.Campeoes, new Campeao { W = new[] { (ushort)ano, camp.ID, tc.ID, taca.ID, tt.ID } }, LimiteCampeoes);

        if (camp != taca)
        {
            EventoEquipa(camp, EvEquipaCampeao, ano, tc.ID);
            EventoEquipa(taca, EvEquipaTaca, ano, tt.ID);
            EventoTreinador(tc, EvTreinadorCampeao, ano, camp.ID);
            EventoTreinador(tt, EvTreinadorTaca, ano, taca.ID);
        }
        else
        {
            EventoEquipa(camp, EvEquipaDobradinha, ano, tc.ID);
            EventoTreinador(tc, EvTreinadorDobradinha, ano, camp.ID);
        }
        foreach (var (pos, ev, evt) in new[] { (1, EvEquipaVice, EvTreinadorVice), (2, EvEquipaTerceiro, EvTreinadorTerceiro) })
        {
            var e = MercadoJogo.Equipa(S, S.Divisoes[0].Equipas[pos])!;
            var c = MercadoJogo.Treinador(S, e.Treinador)!;
            EventoEquipa(e, ev, ano, c.ID);
            EventoTreinador(c, evt, ano, e.ID);
        }

        SubidaEDescida(r);
        Interacao?.Chicotadas.Clear();   // 5:025a

        S.Cabecalho.G271E = 0;
        S.Cabecalho.G271A = 0;
        S.Cabecalho.Ano++;

        AntesDosPremios?.Invoke();
        Premios(camp, taca);
        return r;
    }

    // 5:0987-0a81: d0[N-2] <-> d1[0] (2x), d1[N-4] <-> d2[0] (2x), d2[N-4] <-> d3[0] (2x);
    // Distrital: r1 = Random(n), r2 = Random(n) ate r2 != r1; d3[N-4] <-> d4[r1] e d3[N-4]
    // <-> d4[r2 - (r1 < r2 ? 1 : 0)], com a verificacao do humano. Com N >= 16, SubidaK.
    public void SubidaEDescida(Resultado r)
    {
        int n = Regras.N;
        int k = Regras.SobemDescem;
        if (k != 2) { SubidaK(r, n, k); return; }
        for (int i = 0; i < 2; i++) Trocar(r, 0, n - 2, 1, 0, false);
        for (int i = 0; i < 2; i++) Trocar(r, 1, n - 4, 2, 0, false);
        for (int i = 0; i < 2; i++) Trocar(r, 2, n - 4, 3, 0, false);
        int nd = S.Divisoes[Distrital].Equipas.Count;
        if (nd < 2) throw new InvalidOperationException("temporada: Distrital com menos de 2 equipas (o original nao sai do laco)");
        int r1 = G.Random(nd);
        int r2 = G.Random(nd);
        while (r2 == r1) r2 = G.Random(nd);
        Trocar(r, 3, n - 4, Distrital, r1, true);
        Trocar(r, 3, n - 4, Distrital, r1 < r2 ? r2 - 1 : r2, true);
    }

    // tools/divisoes/subida.asm do launcher (N >= 16): k trocas por nivel (a que desce e a
    // k-esima a contar do fim: N - k na 1a divisao, N - 2k nas outras); do Distrital sobem k
    // por sorteio entre os que ja estavam la: Random(n - i)
    void SubidaK(Resultado r, int n, int k)
    {
        for (int nivel = 0; nivel < 3; nivel++)
        {
            int ia = nivel == 0 ? n - k : n - 2 * k;
            for (int c = 0; c < k; c++) Trocar(r, nivel, ia, nivel + 1, 0, false);
        }
        for (int c = 0; c < k; c++)
        {
            int ib = G.Random(S.Divisoes[Distrital].Equipas.Count - c);
            Trocar(r, 3, n - 2 * k, Distrital, ib, true);
        }
    }

    // 5:02a6: A = divA[ia], B = divB[ib]; divA.Remove(A); divA.Add(B); divB.Remove(B);
    // divB.Add(A). Com verificar e treinador de A humano: Chicotada(A, 0x400).
    void Trocar(Resultado r, int da, int ia, int db, int ib, bool verificar)
    {
        var la = S.Divisoes[da].Equipas; var lb = S.Divisoes[db].Equipas;
        ushort a = la[ia], b = lb[ib];
        la.Remove(a); la.Add(b);
        lb.Remove(b); lb.Add(a);
        r.Desceram[da].Add(a);
        r.Subiram[db].Add(b);
        if (verificar && MercadoJogo.Equipa(S, a) is { } e && MercadoJogo.Treinador(S, e.Treinador) is { Humano: not 0 })
            Chicotada?.Invoke(e, 0x400);
    }

    // 5:2bf6 (TEndOfSeasonDlg), tudo com Movimento tipo 3 (Premios): campeao DS:2D8F; Taca
    // DS:2D93 (sem inflacao); melhor marcador: o maximo de +0x14 entre todos os jogadores,
    // comecando de 0; a equipa de CADA jogador com esse numero recebe DS:2D97 (se ninguem
    // marcou, cada equipa recebe uma vez por jogador)
    public void Premios(Equipa camp, Equipa taca)
    {
        var m = Moeda.DoSave(S);
        MercadoJogo.Movimento(camp, m.PremioCampeao, 3);
        MercadoJogo.Movimento(taca, m.PremioTaca, 3);
        short max = 0;
        foreach (var e in S.Equipas)
            foreach (var p in e.Jogadores) max = Math.Max(max, GolosNaEpoca(p));
        foreach (var e in S.Equipas.ToList())
            foreach (var p in e.Jogadores.ToList())
                if (GolosNaEpoca(p) == max) MercadoJogo.Movimento(MercadoJogo.EquipaDoJogador(S, p.ID).E!, m.PremioMarcador, 3);
    }

    static short GolosNaEpoca(Jogador p) => (short)(p.B14[0] | p.B14[1] << 8);

    // moral (Real48) -> Extended, fadd dword v, -> Real48
    static void SomaMoral(Equipa e, double v) => Ext.DeReal48(e.Moral).Add(X(v)).Real48().CopyTo(e.Moral, 0);

    static void EventoEquipa(Equipa e, byte tipo, uint ano, ushort treinador) =>
        MercadoJogo.AcrescentarLimitado(e.Eventos, new EventoEquipa { B04 = tipo, D05 = ano, W09 = treinador }, MercadoJogo.LimiteEventosEquipa);

    static void EventoTreinador(Treinador c, byte tipo, uint ano, ushort equipa) =>
        MercadoJogo.AcrescentarLimitado(c.Eventos, new EventoTreinador { B04 = tipo, D05 = ano, W09 = equipa }, MercadoJogo.LimiteEventosTreinador);

    // ---------- a Taca ----------

    // 5:0ee3-0f34: ha fase na jornada j se o numero TOTAL de equipas e maior que
    // Round(Power(2; (R - j) div 2)) (so a palavra de baixo, com sinal)
    public static bool TacaNaJornada(TRegras rg, int totalEquipas, int jornada)
    {
        short k = (short)((rg.Jornadas - jornada) / 2);
        short lim = (short)Arred(Partida.Fpu.PotenciaExata(X(2), Xi(k)));
        return (short)totalEquipas > lim;
    }

    // 5:0f6a-0fa6: P e a menor potencia de 2 com n/2 <= P; jogam-se n - P jogos
    public static (int P, int Jogos) TamanhoDaFase(int n)
    {
        int p = 1;
        while (Xi((short)n).Div(X(2)).Cmp(Xi((short)p)) > 0) p = (short)(p << 1);
        return (p, n - p);
    }

    public static string NomeDaFase(int p) => p switch
    {
        1 => "FINAL",
        2 or 3 => "Meias-finais",
        >= 4 and <= 7 => "Quartos de final",
        >= 8 and <= 15 => "Oitavos de final",
        _ => $"{p} avos de final",
    };

    // 3:0b1e: quantas equipas de DS:26F2 tem treinador humano ativo
    public int HumanosNaTaca() =>
        S.OutrasEquipas.Count(id => MercadoJogo.Equipa(S, id) is { } e && MercadoJogo.HumanoAtivo(MercadoJogo.Treinador(S, e.Treinador)));

    // o que TsorteioDlg (5:3205) faz em DS:26F2 antes de mostrar o sorteio: a bolha com
    // 11:1be4 (troca se o nome curto da 1a for maior, comparacao de strings do Pascal)
    public static void OrdenarTacaPorNome(Jogo98 s)
    {
        byte[] Nome(ushort id) => MercadoJogo.Equipa(s, id) is { } e && e.NomeCurto.Length > 0 ? Cifra.TextoEFT(e.NomeCurto).Texto : Array.Empty<byte>();
        Rodada.Listas.Ordenar(s.OutrasEquipas, (a, b) => !(Nome(a).AsSpan().SequenceCompareTo(Nome(b)) > 0));
    }
}
