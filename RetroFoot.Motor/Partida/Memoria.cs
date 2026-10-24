using System;
using System.Buffers.Binary;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Save;

namespace RetroFoot.Motor.Partida;

// Campos dos objetos do jogo (TPlayer, TTeam) que o motor da partida usa e que no save
// estao em bytes crus. O motor le e grava direto nos bytes do save, para que o estado depois
// da partida seja o mesmo que o original gravaria. (Traduzido de motor/partida/memoria.go.)
public static class Memoria
{
    // escalacao do jogador (+0x10)
    public const ushort EscFora = 0, EscTitular = 1, EscReserva = 2;

    internal static ushort Le16(byte[] b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));
    internal static void Poe16(byte[] b, int o, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), v);
    internal static uint Le32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    internal static void Poe32(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);

    // ---------------------------------------------------------------- jogador
    public static ushort Escalacao(Jogador j) => Le16(j.B0C, 4);
    public static void MudarEscalacao(Jogador j, ushort v) => Poe16(j.B0C, 4, v);

    // +0x0C: -1 por minuto em campo, +1 fora (seg03:49e2); usado depois da rodada
    public static short ContadorEvolucao(Jogador j) => (short)Le16(j.B0C, 0);
    internal static void MudarContador(Jogador j, short v) => Poe16(j.B0C, 0, (ushort)v);

    // +0x0E: gols nesta partida
    public static short GolosNaJornada(Jogador j) => (short)Le16(j.B0C, 2);

    // +0x14: gols da carreira (16 bits)
    public static short Golos14(Jogador j) => (short)Le16(j.B14, 0);

    // historial (+0x17, 4 contadores de 32 bits)
    public const int HistJogos = 0, HistGolos = 1, HistLesoes = 2, HistVermelhos = 3;

    // (os jogos: so os 16 bits de baixo; os de cima sao os jogos da temporada, do RetroFoot)
    public static int Historial(Jogador j, int qual) => qual == HistJogos ? Le16(j.Historial, 0) : (int)Le32(j.Historial, 4 * qual);

    // (RetroFoot, fora do original) os jogos da temporada: os 16 bits de cima do contador de jogos
    // (+0x19), que o jogo classico deixa a zero, como os amarelos nas lesoes e expulsoes. Zerados
    // com os gols da temporada; somam com os da carreira, so dos times que jogam na rodada.
    // Desligado (nesta thread), o save fica igual ao do jogo classico (os testes do oraculo).
    [ThreadStatic] public static bool SemJogosDaTemporada;
    public static int JogosNaTemporada(Jogador j) => Le16(j.Historial, 2);
    public static void SomaJogoNaTemporada(Jogador j)
    {
        if (!SemJogosDaTemporada) Poe16(j.Historial, 2, (ushort)(Le16(j.Historial, 2) + 1));
    }
    public static void ZerarJogosNaTemporada(Jogador j) => Poe16(j.Historial, 2, 0);

    // a media de gols (gols / jogos), da carreira ou da temporada; null sem jogos
    public static double? MediaGols(Jogador j, bool temporada)
    {
        int jogos = temporada ? JogosNaTemporada(j) : Historial(j, HistJogos);
        int gols = temporada ? Golos14(j) : Historial(j, HistGolos);
        return jogos > 0 ? (double)gols / jogos : null;
    }
    internal static void SomaHistorial(Jogador j, int qual, int d) => Poe32(j.Historial, 4 * qual, Le32(j.Historial, 4 * qual) + (uint)d);

    internal static void SomaGolosJogador(Jogador j, short d)
    {
        Poe16(j.B0C, 2, (ushort)(Le16(j.B0C, 2) + d));   // +0x0E
        Poe16(j.B14, 0, (ushort)(Le16(j.B14, 0) + d));   // +0x14
        SomaHistorial(j, HistGolos, d);                   // +0x1B
    }

    // texto decifrado do save (cifra dos .EFT); vazio se nao houver
    internal static byte[] Texto(byte[] b) => b.Length == 0 ? Array.Empty<byte>() : Cifra.TextoEFT(b).Texto;

    internal static byte[] PaisDoJogador(Jogador j) => Texto(j.Pais);

    // ---------------------------------------------------------------- equipa
    // B4D: +0x4D substituicoes que ainda pode fazer, +0x4F gols nesta partida, +0x51..+0x57
    // forca de cada setor (G, D, M, A), +0x59 gols anulados. Zerados antes de cada jogo.
    public static short Substituicoes(Equipa e) => (short)Le16(e.B4D, 0);
    public static void MudarSubstituicoes(Equipa e, short v) => Poe16(e.B4D, 0, (ushort)v);
    public static short Golos(Equipa e) => (short)Le16(e.B4D, 2);
    internal static void SomaGolos(Equipa e, short d) => Poe16(e.B4D, 2, (ushort)(Le16(e.B4D, 2) + d));
    public static ushort Setor(Equipa e, int setor) => Le16(e.B4D, 4 + 2 * setor);
    public static short Anulados(Equipa e) => (short)Le16(e.B4D, 12);

    // seg03:3e9c (42e1): +0x4D..+0x5A da equipa a zero antes de cada jogo
    public static void ZerarPartida(Equipa e) => Array.Clear(e.B4D);

    internal static byte[] PaisDaEquipa(Equipa e) => Texto(e.Pais);

    // ---------------------------------------------------------------- treinador
    // seg03:8c67: humano quando +0x07 != 0 e +0x06 = 0
    public static bool Humano(Treinador? t) => t != null && t.Humano != 0 && t.B06 == 0;
}
