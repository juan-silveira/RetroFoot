using System;
using System.Buffers.Binary;
using RetroFoot.Motor.Save;

namespace RetroFoot.Motor.Rodada;

// Os campos dos objetos do jogo que o save guarda em blocos de bytes crus (Equipa.B16,
// B4D...; Jogador.B0C, B14, Historial), lidos e gravados pelo deslocamento em memoria
// ("+0xNN"), para o codigo seguir o original deslocamento a deslocamento.
// (Traduzido de motor/rodada/campos.go.)
public static class Campos
{
    public const int EqVitorias = 0x16, EqEmpates = 0x18, EqDerrotas = 0x1A, EqGolosPro = 0x1C,
        EqGolosContra = 0x1E, EqPontos = 0x20, EqEliminadoTaca = 0x30, EqEstadoJogo = 0x4D,
        EqSubst = 0x4D, EqGolosJogo = 0x4F, EqMoral = 0x5B;

    public const int JgContadorV = 0x0C, JgEscalacao = 0x10, JgSalarioRevisto = 0x16, JgGolos14 = 0x14,
        JgJogos = 0x17, JgGolos = 0x1B, JgLesoes = 0x1F, JgVermelhos = 0x23,
        // cartoes amarelos do launcher (cartoes.asm), nos 2 bytes de cima das lesoes e expulsoes
        JgAmarelosCarreira = 0x21, JgAmarelosAcumulados = 0x25, JgMarcaAmarelo = 0x26;

    internal static (byte[] B, int O) MemEquipa(Equipa e, int off) => off switch
    {
        >= 0x16 and < 0x4D => (e.B16, off - 0x16),
        >= 0x4D and < 0x5B => (e.B4D, off - 0x4D),
        >= 0x5B and < 0x61 => (e.Moral, off - 0x5B),
        >= 0x79 and < 0x83 => (e.B79, off - 0x79),
        >= 0x83 and < 0x89 => (e.B83, off - 0x83),
        >= 0x89 and < 0xA5 => (e.B89, off - 0x89),
        >= 0xA5 and < 0xC1 => (e.BA5, off - 0xA5),
        _ => throw new ArgumentException($"campo +0x{off:X} da equipa nao esta num bloco cru"),
    };

    internal static (byte[] B, int O) MemJogador(Jogador j, int off) => off switch
    {
        >= 0x0C and < 0x14 => (j.B0C, off - 0x0C),
        >= 0x14 and < 0x17 => (j.B14, off - 0x14),
        >= 0x17 and < 0x27 => (j.Historial, off - 0x17),
        _ => throw new ArgumentException($"campo +0x{off:X} do jogador nao esta num bloco cru"),
    };

    public static short EqW(Equipa e, int off) { var (b, o) = MemEquipa(e, off); return BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(o)); }
    public static void SetEqW(Equipa e, int off, short v) { var (b, o) = MemEquipa(e, off); BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(o), v); }
    public static void AddW(Equipa e, int off, short v) => SetEqW(e, off, (short)(EqW(e, off) + v));
    public static byte EqB(Equipa e, int off) { var (b, o) = MemEquipa(e, off); return b[o]; }
    public static void SetEqB(Equipa e, int off, byte v) { var (b, o) = MemEquipa(e, off); b[o] = v; }
    public static int EqD(Equipa e, int off) { var (b, o) = MemEquipa(e, off); return BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o)); }
    public static void SetEqD(Equipa e, int off, int v) { var (b, o) = MemEquipa(e, off); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o), v); }

    public static short JgW(Jogador j, int off) { var (b, o) = MemJogador(j, off); return BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(o)); }
    public static void SetJgW(Jogador j, int off, short v) { var (b, o) = MemJogador(j, off); BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(o), v); }
    public static byte JgB(Jogador j, int off) { var (b, o) = MemJogador(j, off); return b[o]; }
    public static void SetJgB(Jogador j, int off, byte v) { var (b, o) = MemJogador(j, off); b[o] = v; }
    public static int JgD(Jogador j, int off) { var (b, o) = MemJogador(j, off); return BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o)); }
    public static void SetJgD(Jogador j, int off, int v) { var (b, o) = MemJogador(j, off); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o), v); }
}
