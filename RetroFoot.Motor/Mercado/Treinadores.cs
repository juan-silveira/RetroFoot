using System;
using System.Collections.Generic;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Mercado.Constantes;
using static RetroFoot.Motor.Rodada.X87;
using Chicotada = RetroFoot.Motor.Rodada.Chicotada;

namespace RetroFoot.Motor.Mercado;

// Treinadores: chicotada psicologica, candidatos, convite ao humano, ranking, gestao.
// Campos do treinador: +0x06 de ferias (o computador dirige a equipa do humano), +0x07
// humano, +0x0C jogos na equipa atual, +0x0E pontos (vitoria em casa 2, fora 3, empate em
// casa 1, fora 2; passar na Taca 3), +0x12 eventos, +0x16 campeonatos, +0x18 tacas, +0x1A
// perfil (5 bytes: [0..2] o computador gere os salarios dos nacionais / Bosman / outros;
// [3] compra em leilao: 0 a todos, 1 nao aos mais fracos, 2 nao compra; [4] esconder
// ofertas; treinador novo 00 00 00 01 00). (Traduzido de motor/mercado/treinadores.go.)
public sealed partial class MercadoJogo
{
    // tipos de evento do treinador (TCoachEvent, textos em 3:b626)
    public const byte EventoContratado = 1, EventoDespedido = 2, EventoCampeao = 3, EventoTaca = 4,
        EventoIngresso = 5, EventoDespromovido = 6, EventoPromovido = 7, EventoDobradinha = 8,
        EventoSegundo = 9, EventoTerceiro = 10;

    public const uint FlagOutrasEquipas = 0x100, FlagDesempregados = 0x200, FlagSemHumanos = 0x400;

    internal static byte[] MoralUm => new byte[] { 0x81, 0, 0, 0, 0, 0 };   // Real48 1,0 (3:5162)

    // 3:8c67: humano (+0x07) e nao esta de ferias (+0x06 = 0)
    public static bool HumanoAtivo(Treinador? t) => t != null && t.Humano != 0 && t.B06 == 0;

    void Evento(Treinador t, byte tipo, ushort equipa) =>
        AcrescentarLimitado(t.Eventos, new EventoTreinador { B04 = tipo, D05 = (uint)S.Cabecalho.Ano, W09 = equipa }, LimiteEventosTreinador);

    // 3:576f: o nome da divisao da equipa e o da divisao de indice 4 (o Distrital)
    public bool NoDistrital(Equipa e)
    {
        var (d, _) = Divisao(S, e.ID);
        if (d < 0 || S.Divisoes.Count <= 4) return false;
        var a = S.Divisoes[d].Nome; var b = S.Divisoes[4].Nome;
        return a.AsSpan(0, 1 + a[0]).SequenceEqual(b.AsSpan(0, 1 + b[0]));
    }

    // 3:8b5d: >= 10 jogos e moral <= 0,65; ou humano no Distrital
    public bool DeveDespedir(Treinador t, Equipa e)
    {
        if ((short)t.W0C >= 10 && Ext.DeReal48(e.Moral).Cmp(Ext10(0x3ffe, 0xa666666666666666)) <= 0) return true;
        return t.Humano != 0 && NoDistrital(e);
    }

    // 3:5661: a outra equipa tem moral >= 1,25 e a que despede e de 11 a 19 mais forte
    static bool BoaEquipaParaTrocar(Equipa outra, Equipa e)
    {
        if (Ext.DeReal48(outra.Moral).Cmp(X(1.25)) < 0) return false;
        int d = (short)(ForcaMedia(e) - ForcaMedia(outra));
        return d > 10 && d < 20;
    }

    // 3:8bcc: um humano ativo que ja treina uma equipa e convidado (TinviteCoachDlg)
    public bool AceitaConvite(Treinador t, Equipa e, Interacao? it)
    {
        if (!HumanoAtivo(t)) return true;
        if (EquipaDoTreinador(S, t.ID) == null) return true;
        return it?.Convite?.Invoke(t, e) ?? true;
    }

    static short Titulos(Treinador t) => (short)t.D16;
    static short Tacas(Treinador t) => (short)(t.D16 >> 16);

    // 11:1d59: mais campeonatos; mais tacas; pontos >=
    public static bool AntesNoRanking(Treinador a, Treinador b)
    {
        short d = (short)(Titulos(a) - Titulos(b));
        if (d != 0) return d > 0;
        d = (short)(Tacas(a) - Tacas(b));
        if (d != 0) return d > 0;
        return (int)a.D0E >= (int)b.D0E;
    }

    // 11:1e3b: humanos primeiro; dentro do grupo, pontos >=
    static bool AntesDesempregado(Treinador a, Treinador b)
    {
        if (a.Humano != 0 && b.Humano == 0) return true;
        if (a.Humano == 0 && b.Humano != 0) return false;
        return (int)a.D0E >= (int)b.D0E;
    }

    // 4:3ec1 (coachRankingClick): ordena a lista GLOBAL de treinadores (vai para o save)
    public static void OrdenarRanking(Jogo98 s) => Rodada.Listas.Ordenar(s.Treinadores, AntesNoRanking);

    // 3:5343(equipa, flags): 0x400 tambem no Distrital ou na ultima jornada; candidatos na
    // ordem da lista global (desempregados com 0x200; de outras equipas com 0x100, se
    // BoaEquipaParaTrocar, resultado que substitui o anterior; sem humanos com 0x400 e
    // quem treina no Distrital); ordena; o 1o que aceita
    public Treinador? Candidato(Equipa e, uint flags, Interacao? it)
    {
        if (NoDistrital(e)) flags |= FlagSemHumanos;
        if (Jornada == Regras.Jornadas) flags |= FlagSemHumanos;   // seg03:53f3 (14 no original)
        var l = new List<Treinador>();
        foreach (var t in S.Treinadores)
        {
            var outra = EquipaDoTreinador(S, t.ID);
            bool ok = (flags & FlagDesempregados) != 0 && outra == null;
            // o original compara com o endereco do parametro na pilha (nunca iguais)
            if ((flags & FlagOutrasEquipas) != 0 && outra != null) ok = BoaEquipaParaTrocar(outra, e);
            if ((flags & FlagSemHumanos) != 0) ok = ok && t.Humano == 0;
            if (outra != null && NoDistrital(outra)) ok = ok && t.Humano == 0;
            if (ok) l.Add(t);
        }
        Rodada.Listas.Ordenar(l, (flags & FlagDesempregados) != 0 ? AntesDesempregado : AntesNoRanking);
        foreach (var t in l)
            if (AceitaConvite(t, e, it)) return t;
        return null;
    }

    // 3:4f40(equipa, flags): depois de um jogo perdido quando DeveDespedir (flags 0) e no fim
    // da temporada para o humano rebaixado ao Distrital (0x400). flags |= 0x100 (e 0x200 com
    // forca media < 15); novo = Candidato; se DS:2B34 manda, tres linhas na lista; a equipa
    // fica com o novo (0 jogos, moral 1,0); a outra, com o antigo; eventos; antigo humano e
    // novo nao: SalariosSemHumano. Devolve sempre false.
    public bool ChicotadaPsicologica(Equipa e, uint flags, Interacao? it)
    {
        var antigo = Treinador(S, e.Treinador);
        flags |= FlagOutrasEquipas;
        if (ForcaMedia(e) < 15) flags |= FlagDesempregados;
        var novo = Candidato(e, flags, it);
        if (novo != null)
        {
            antigo = Treinador(S, e.Treinador);
            var outra = EquipaDoTreinador(S, novo.ID);
            if (it != null)
            {
                bool humano = (antigo != null && antigo.Humano != 0) || novo.Humano != 0;
                if (it.MostrarChicotadas == 2 || (humano && it.MostrarChicotadas == 1))
                {
                    it.Chicotadas.Add(new Chicotada(antigo?.ID ?? 0, e.ID, 1));
                    it.Chicotadas.Add(new Chicotada(novo.ID, outra?.ID ?? 0, 0));
                    it.Chicotadas.Add(new Chicotada(0, 0, 0));
                }
            }
            e.Treinador = novo.ID;
            novo.W0C = 0;
            e.Moral = MoralUm;
            if (outra != null)
            {
                outra.Treinador = antigo?.ID ?? 0;
                outra.Moral = MoralUm;
                if (antigo != null)
                {
                    antigo.W0C = 0;
                    Evento(antigo, EventoDespromovido, outra.ID);
                }
                Evento(novo, EventoPromovido, e.ID);
            }
            else
            {
                if (antigo != null) Evento(antigo, EventoDespedido, 0);
                Evento(novo, EventoContratado, e.ID);
            }
        }
        if (antigo != null && novo != null && antigo.Humano != 0 && novo.Humano == 0) SalariosSemHumano(e);
        return false;
    }

    // 3:9c86/3:9d21
    public static void ContarJogo(Treinador t)
    {
        if ((short)t.W0C < 0x7fff) t.W0C++;
    }

    // TcoachManagerDlg.addBtnClick (11:09bc) com 3:827c: id = DS:2722 (e ++), humano, sem
    // ferias, perfil 00 00 00 01 00, no fim da lista. nome ja cifrado como no save.
    public Treinador NovoTreinador(byte[] nome)
    {
        ushort id = S.Cabecalho.G2722;
        S.Cabecalho.G2722++;
        var t = new Treinador { ID = id, Nome = (byte[])nome.Clone(), Humano = 1, B1A = new byte[] { 0, 0, 0, 1, 0 } };
        S.Treinadores.Add(t);
        return t;
    }

    // 11:06e9 (removeBtnClick): a equipa do treinador passa para o 1o sem equipa e nao
    // humano, com SalariosSemHumano e o evento "Ingresso no"; o original nao para no fim
    // da lista se nao houver nenhum (aqui fica sem treinador). Sai da lista global.
    public void RemoverTreinador(ushort id)
    {
        if (EquipaDoTreinador(S, id) is { } e)
        {
            var sub = S.Treinadores.Find(t => EquipaDoTreinador(S, t.ID) == null && t.Humano == 0);
            if (sub == null) e.Treinador = 0;
            else
            {
                e.Treinador = sub.ID;
                SalariosSemHumano(e);
                Evento(sub, EventoIngresso, e.ID);
            }
        }
        int i = S.Treinadores.FindIndex(t => t.ID == id);
        if (i >= 0) S.Treinadores.RemoveAt(i);
    }

    // 11:091c: +0x06
    public static void Ferias(Treinador t, bool ferias) => t.B06 = (byte)(ferias ? 1 : 0);
}
