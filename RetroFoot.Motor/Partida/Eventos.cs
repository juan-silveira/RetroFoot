using System;
using System.Collections.Generic;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Partida.Memoria;
using static RetroFoot.Motor.Partida.PreJogo;
using static RetroFoot.Motor.Partida.Sorteio;

namespace RetroFoot.Motor.Partida;

public static class Eventos
{
    // ---------- gol (seg03:46df e rotinas locais) ----------

    // seg03:45b8 (local de 46df): R = t1 + 2 t2 + t3, com
    //   a1 = 1 + D     b1 = 1 + A'          t1 = a1 / (a1 + b1 + 1)
    //   a2 = a1 + M    b2 = b1 + M'         t2 = a2 / (a2 + b2 + 1)
    //   a3 = a2 + A    b3 = b2 + 3G' + D'   t3 = a3 / (a3 + b3 + 1)
    // a, b e a + b + 1 sao palavras de 16 bits sem sinal (estouram); as divisoes em Extended.
    internal static Ext ForcaRelativa(Equipa e, Equipa adv)
    {
        var R = Ext.F32(0);
        ushort a = 1, b = 1;
        for (int k = 1; k <= 3; k++)
        {
            switch (k)
            {
                case 1: a += Setor(e, 1); b += Setor(adv, 3); break;
                case 2: a += Setor(e, 2); b += Setor(adv, 2); break;
                case 3: a += Setor(e, 3); b += (ushort)(3 * Setor(adv, 0) + Setor(adv, 1)); break;
            }
            // a + b + 1 = 65536 da 0 e a divisao por zero para o original; so com forcas
            // acima dos limites de 16 bits (saves editados): aqui t = 0
            ushort den = (ushort)(a + b + 1);
            var t = den == 0 ? Ext.F32(0) : Fpu.Divide(Ext.Int(a), Ext.Int(den));
            R = Fpu.Soma(R, Fpu.Mult(t, Ext.Int(k == 2 ? 2 : 1)));
        }
        return R;
    }

    // 1 + 4 (A + M/2) / (3G' + D' + M'/2 + 1) (seg03:46f1-4718, com as locais 455d e 44f5);
    // 3G' + D' somado em 16 bits
    internal static Ext ArgumentoLn(Equipa e, Equipa adv)
    {
        var num = Fpu.Mult(Fpu.Soma(Ext.Int(Setor(e, 3)), Fpu.Divide(Ext.Int(Setor(e, 2)), Dois)), Quatro);
        var den = Fpu.Soma(Ext.Int((ushort)(3 * Setor(adv, 0) + Setor(adv, 1))), Fpu.Divide(Ext.Int(Setor(adv, 2)), Dois));
        den = Fpu.Soma(den, Um);
        return Fpu.Soma(Fpu.Divide(num, den), Um);
    }

    // Q, o numero de gols "merecidos" pela equipa contra o adversario (seg03:46df ate 4774):
    //   Q = Power(ln(1 + 4 (A + M/2) / (3G' + D' + M'/2 + 1)) R, 1) (moral/2)^(1/4)
    // Se A = M = 0 (sem medios nem avancados em campo: formacao sem avancados e os medios
    // expulsos ou lesionados), o ln da 0 e o Power(0, 1) e o Ln de 0: operacao invalida na
    // FPU (erro de execucao no original). Aqui Power de 0 da 0, entao Q = 0 (so o gol de
    // sorte). Q so depende dos setores e do moral: fica guardado.
    public static Ext ChanceDeGol(Equipa e, Equipa adv)
    {
        ulong s = 0, a = 0, m = 0;
        for (int i = 0; i < 4; i++) { s |= (ulong)Setor(e, i) << (16 * i); a |= (ulong)Setor(adv, i) << (16 * i); }
        for (int i = 0; i < 6; i++) m |= (ulong)e.Moral[i] << (8 * i);
        var k = (s, a, m);
        lock (CacheQ)
            if (CacheQ.TryGetValue(k, out var q0)) return q0;
        var q = CalcularQ(e, adv);
        lock (CacheQ)
        {
            if (CacheQ.Count > 50000) CacheQ.Clear();
            CacheQ[k] = q;
        }
        return q;
    }

    static readonly Dictionary<(ulong, ulong, ulong), Ext> CacheQ = new();

    static Ext CalcularQ(Equipa e, Equipa adv)
    {
        var R = ForcaRelativa(e, adv);
        var l = Fpu.Ln(ArgumentoLn(e, adv));
        var p = Fpu.Potencia(Fpu.Mult(l, R), Um);
        var m = Fpu.Raiz(Fpu.Raiz(Fpu.Divide(Fpu.DeReal48(e.Moral), Dois)));
        return Fpu.Mult(p, m);
    }

    // seg03:46df: a equipa marca gol neste minuto? lado so muda os sorteios (90/180 para a
    // casa, 120/270 para o visitante).
    //   gol = Q - S > Random(90 | 120)  ou, senao,  Random(180 | 270) = 0
    //   se gol: gol = minuto > Random(45)
    //   se gol ou minuto = 45: autor = SorteiaJogador(titulares, peso de gol)
    // S = gols + gols anulados da equipa nesta partida (16 bits). No minuto 45 o autor e
    // sorteado mesmo sem gol (gasta um Random); por isso o autor pode vir sem gol.
    public static (bool Gol, Jogador? Autor) Gol(Aleatorio r, Equipa e, int lado, Equipa adv, int minuto)
    {
        var Q = ChanceDeGol(e, adv);
        int n1 = 90, n2 = 180;
        if (lado != Lado.Casa) { n1 = 120; n2 = 270; }
        var sorteio = Ext.Int(r.Random(n1));
        var S = Ext.Int((short)(Golos(e) + Anulados(e)));
        bool gol = Fpu.Subt(Q, S).Cmp(sorteio) > 0;
        if (!gol) gol = r.Random(n2) == 0;
        if (gol) gol = (short)minuto > r.Random(45);
        Jogador? autor = null;
        // 44a1: chama 441a (soma posicao x forca dos titulares, num local que nao e usado)
        // e sorteia o autor
        if (gol || minuto == 45) autor = SorteiaJogador(r, Elenco(e), 1, PesoGol);
        return (gol && autor != null, autor);
    }

    // ---------- expulsao e lesao (seg03:48be, seg03:4940) ----------

    // seg03:48be: sorteia um titular pelo comportamento; se houver e o comportamento for > 0,
    // ele sai, pega Random(4) + 1 jogos de suspensao (+0x27) e soma um vermelho (+0x23)
    public static (bool Ok, Jogador? J) Expulsao(Aleatorio r, Equipa e)
    {
        var j = SorteiaJogador(r, Elenco(e), 1, PesoComportamento);
        if (j == null || (short)j.Comportamento <= 0) return (false, j);
        MudarEscalacao(j, EscFora);
        j.Suspensao = (ushort)(r.Random(4) + 1);
        SomaHistorial(j, HistVermelhos, 1);
        return (true, j);
    }

    // seg03:4940: sorteia um titular pelo atributo Lesao; se houver e Lesao > 0, ele sai,
    // fica Random(Random(20)) + 1 jogos parado (+0x29), soma uma lesao (+0x1F) e perde 10 de
    // forca (minimo 1, conta com sinal)
    public static (bool Ok, Jogador? J) Lesao(Aleatorio r, Equipa e)
    {
        var j = SorteiaJogador(r, Elenco(e), 1, PesoLesao);
        if (j == null || (short)j.Lesao <= 0) return (false, j);
        MudarEscalacao(j, EscFora);
        j.JogosLesao = (ushort)(r.Random(r.Random(20)) + 1);
        SomaHistorial(j, HistLesoes, 1);
        int f = (short)(j.Forca - 10);
        if (f < 1) f = 1;   // seg14:0fed Max(f, 1)
        j.Forca = (ushort)f;
        return (true, j);
    }

    // ---------- substituicao automatica (seg03:67f7) ----------

    public const int SubLesao = 0, SubExpulsao = 1;

    // seg03:67f7, para times do computador, se a equipa ainda tem substituicoes (+0x4D > 0).
    //  - Lesao: entra o 1o reserva da lista "do mesmo tipo" (goleiro por goleiro, linha por linha).
    //  - Expulsao: so se o expulso e goleiro. Entra o 1o goleiro reserva; sai o ultimo titular
    //    da lista, procurando do fim ate o indice 1 (o 0 nunca sai).
    // Na troca: quem sai fica fora, quem entra vira titular e soma um jogo (+0x17), a equipa
    // gasta uma substituicao e a forca dos setores e recalculada.
    // trocou (so a interface): chamado com quem saiu e quem entrou
    public static bool SubstituicaoAutomatica(Equipa e, Jogador saiu, int motivo, Action<Jogador, Jogador>? trocou = null)
    {
        if (Substituicoes(e) <= 0) return false;
        var el = Elenco(e);
        Jogador? entra = null;
        bool ok = false;
        if (motivo == SubLesao)
        {
            foreach (var p in el)
                if (Escalacao(p) == EscReserva && (p.Posicao == 0) == (saiu.Posicao == 0)) { entra = p; ok = true; break; }
        }
        else if (motivo == SubExpulsao)
        {
            if (saiu.Posicao != 0) return false;
            foreach (var p in el)
                if (p.Posicao == 0 && Escalacao(p) == EscReserva) { entra = p; break; }
            if (entra != null)
                for (int i = el.Count - 1; i > 0; i--)
                    if (Escalacao(el[i]) == EscTitular) { saiu = el[i]; ok = true; break; }
        }
        if (!ok) return false;
        MudarEscalacao(saiu, EscFora);
        MudarEscalacao(entra!, EscTitular);
        SomaHistorial(entra!, HistJogos, 1);
        MudarSubstituicoes(e, (short)(Substituicoes(e) - 1));
        ForcaDosSetores(e);
        trocou?.Invoke(saiu, entra!);
        return true;
    }
}

public sealed partial class Jornada
{
    void AvisarLance(JogoAtual jg, Lance l) => Ganchos.Lance?.Invoke(jg, l);

    Lance NovoLance(int lado, int minuto, int tipo, Jogador j)
    {
        var l = Lance.Novo(lado, minuto, tipo, j);
        l.Acrescimo = Acrescimo;
        return l;
    }

    // o lance foi no tique anterior do relogio: minuto - 1 no original; nos acrescimos, o
    // acrescimo anterior do mesmo minuto (o 45'+1' vem depois do 45')
    bool DoMinutoAnterior(Lance l, int minuto) =>
        Acrescimo == 0 ? l.Minuto == minuto - 1 && l.Acrescimo == 0 : l.Minuto == minuto && l.Acrescimo == Acrescimo - 1;

    // a conta de seg03:a76e: o ultimo lance do jogo foi a expulsao de um jogador do lado
    // expulso, no minuto anterior, e nao e o 1o minuto de um tempo (minuto mod 45 > 1):
    // penalti certo se era goleiro, com Random(3) = 0 se era defesa
    bool PenaltiPorExpulsao(Lance? ultimo, int expulso, int minuto)
    {
        if (ultimo == null) return false;
        if (!DoMinutoAnterior(ultimo, minuto) || ultimo.Tipo != TipoLance.Expulsao || ultimo.Lado != expulso || (Acrescimo == 0 && minuto % 45 <= 1))
            return false;
        return ultimo.Posicao switch { 0 => true, 1 => R.Random(3) == 0, _ => false };
    }

    // seg03:a6eb: penalti e gol de um jogo neste minuto.
    //  1. penalti para a casa: forcado por expulsao do visitante no minuto anterior (a76e),
    //     ou Random(6000) < V(casa); o Random(6000) e sorteado sempre;
    //  2. penalti para o visitante: forcado por expulsao da casa, ou Random(7000) <
    //     V(visitante) (sorteado sempre); so e cobrado se a casa nao fez gol de penalti;
    //  3. se nenhum penalti foi "marcado", gol da casa (46df); se nao, gol do visitante.
    // Um penalti sorteado e perdido nao impede o gol normal da casa no mesmo minuto; um
    // forcado perdido impede. No maximo um gol por jogo por minuto. O original tambem exige
    // que um local nao iniciado (bp-0x16) seja != nil para olhar o ultimo lance; na pratica
    // ele tem restos de chamadas anteriores, e aqui e considerado sempre != nil.
    public bool MinutoGol(JogoAtual jg, int minuto)
    {
        if (!jg.TemEquipas) return false;
        var casa = jg.Equipa[Lado.Casa]!; var fora = jg.Equipa[Lado.Visit]!;
        var ultimo = jg.Lances.Count > 0 ? jg.Lances[^1] : null;
        int tipo = TipoLance.Gol;
        bool golCasa = false, golFora = false;
        Jogador? autor = null;
        int lado = Lado.Casa;

        bool penCasa = PenaltiPorExpulsao(ultimo, Lado.Visit, minuto);
        if (jg.Influencia(Lado.Casa) > R.Random(6000) || penCasa)
        {
            var (ok, b) = Penalti(casa, fora, jg.Treinador[Lado.Casa], jg.Treinador[Lado.Visit]);
            autor = b;
            if (ok) { golCasa = true; tipo = TipoLance.Penalti; penCasa = true; }
            else ExtraPenalti(jg, Lado.Casa, minuto, b);
        }
        bool penFora = PenaltiPorExpulsao(ultimo, Lado.Casa, minuto);
        if ((jg.Influencia(Lado.Visit) > R.Random(7000) || penFora) && !golCasa)
        {
            var (ok, b) = Penalti(fora, casa, jg.Treinador[Lado.Visit], jg.Treinador[Lado.Casa]);
            autor = b;
            if (ok) { golFora = true; tipo = TipoLance.Penalti; penFora = true; }
            else ExtraPenalti(jg, Lado.Visit, minuto, b);
        }
        // nos acrescimos, o minuto do sorteio do golo e o corrido (45 + n, 90 + n): o golo sempre
        // vale (minuto > Random(45)) e o autor so e sorteado sem golo no 45 de verdade
        int mg = minuto + Acrescimo;
        if (!penCasa && !penFora) (golCasa, autor) = Eventos.Gol(R, casa, Lado.Casa, fora, mg);
        if (!penCasa && !penFora && !golCasa) (golFora, autor) = Eventos.Gol(R, fora, Lado.Visit, casa, mg);
        if (!golCasa && !golFora) return false;
        if (golCasa) { SomaGolos(casa, 1); lado = Lado.Casa; }
        if (golFora) { SomaGolos(fora, 1); lado = Lado.Visit; }
        SomaGolosJogador(autor!, 1);
        var l = NovoLance(lado, minuto, tipo, autor!);
        jg.Lances.Add(l);
        AvisarLance(jg, l);
        return true;
    }

    // seg03:aa11: um unico r = Random(1000) por jogo e minuto. Se r < 10 e r > V(casa),
    // expulsao na casa; senao, se r < 10 e r > V(visitante), no visitante.
    public bool MinutoExpulsao(JogoAtual jg, int minuto)
    {
        if (!jg.TemEquipas) return false;
        if (Amarelos) return MinutoCartao(jg, minuto);   // launcher: Cartoes.cs
        short r = (short)R.Random(1000);
        bool ok = false; Jogador? j = null;
        int lado = Lado.Casa;
        if (r < 10 && r > jg.Influencia(Lado.Casa)) (ok, j) = Eventos.Expulsao(R, jg.Equipa[Lado.Casa]!);
        else if (r < 10 && r > jg.Influencia(Lado.Visit)) { (ok, j) = Eventos.Expulsao(R, jg.Equipa[Lado.Visit]!); lado = Lado.Visit; }
        if (!ok) return false;
        DepoisDeSair(jg, lado, j!, minuto, TipoLance.Expulsao, false);
        return true;
    }

    // seg03:ac72: r = Random(500); 0 lesao na casa, 1 no visitante
    public bool MinutoLesao(JogoAtual jg, int minuto)
    {
        if (!jg.TemEquipas) return false;
        int r = R.Random(500);
        bool ok = false; Jogador? j = null;
        int lado = Lado.Casa;
        if (r == 0) (ok, j) = Eventos.Lesao(R, jg.Equipa[Lado.Casa]!);
        else if (r == 1) { (ok, j) = Eventos.Lesao(R, jg.Equipa[Lado.Visit]!); lado = Lado.Visit; }
        if (!ok) return false;
        DepoisDeSair(jg, lado, j!, minuto, TipoLance.Lesao, false);
        return true;
    }

    // o fim comum de aa11 e ac72: lance; time humano: janela de substituicao (motivo 2 lesao,
    // 3 goleiro expulso); computador: SubstituicaoAutomatica; e a forca dos setores dos dois
    void DepoisDeSair(JogoAtual jg, int lado, Jogador j, int minuto, int tipo, bool segundoAmarelo)
    {
        var l = NovoLance(lado, minuto, tipo, j);
        l.SegundoAmarelo = segundoAmarelo;
        jg.Lances.Add(l);
        AvisarLance(jg, l);
        // (aqui o original toca um aviso sonoro se algum dos times e humano)
        if (jg.HumanoNoLado(lado) && (tipo == TipoLance.Lesao || j.Posicao == 0))
            JanelaSubstituicao(jg, lado, j, tipo == TipoLance.Expulsao ? 3 : 2);
        if (!jg.HumanoNoLado(lado))
            Eventos.SubstituicaoAutomatica(jg.Equipa[lado]!, j, tipo == TipoLance.Expulsao ? Eventos.SubExpulsao : Eventos.SubLesao,
                (sai, entra) => ExtraSubstituicao(jg, lado, minuto, sai, entra));
        ForcaDosSetores(jg.Equipa[Lado.Casa]!);
        ForcaDosSetores(jg.Equipa[Lado.Visit]!);
    }

    // seg03:aea7 (nao e chamado nos minutos 46 e 91). Se o jogo tem lances, r = Random(200);
    // se r < 10, r > V(casa) (a influencia sobre a casa, seja quem for que marcou) e o ultimo
    // lance e um gol normal do minuto anterior, o gol e anulado: a equipa perde o gol (+0x4F)
    // e ganha um anulado (+0x59), o autor perde o gol e o lance vira tipo 6
    public bool GolAnulado(JogoAtual jg, int minuto)
    {
        if (jg.Lances.Count == 0) return false;
        short r = (short)R.Random(200);
        if (r >= 10 || !(r > jg.Influencia(Lado.Casa))) return false;
        var l = jg.Lances[^1];
        if (!DoMinutoAnterior(l, minuto) || l.Tipo != TipoLance.Gol) return false;
        var e = jg.Equipa[l.Lado]!;
        Poe16(e.B4D, 12, (ushort)(Le16(e.B4D, 12) + 1));   // +0x59
        SomaGolos(e, -1);
        // o original procura o jogador pelo identificador na lista global (d510)
        var p = JogadorPorID(e, l.JogadorID);
        if (p != null) SomaGolosJogador(p, -1);
        l.Tipo = TipoLance.GolAnulado;
        AvisarLance(jg, l);
        return true;
    }

    // seg03:afff: em jogos com tipo != 2, EvolucaoContador da casa e depois do visitante
    public void MinutoEvolucao(JogoAtual jg)
    {
        if (jg.Dados.Taca == TipoJogo.SemEquipas || !jg.TemEquipas) return;
        EvolucaoContador(R, jg.Equipa[Lado.Casa]!);
        EvolucaoContador(R, jg.Equipa[Lado.Visit]!);
    }
}
