using System;
using System.Buffers.Binary;
using RetroFoot.Motor.Save;

namespace RetroFoot.Motor.Patrocinio;

// Os campos do patrocinio no TTeam (+0xCC a +0xDF), que o patch do launcher (ingressos.asm)
// leva ao save nos bytes 4 a 24 do cabecalho "EFa" de cada equipa: o byte 4 e +0xCB (o preco
// dos ingressos), o 5 e +0xCC... O Jogo98 ja le e grava esse cabecalho inteiro (Equipa.Cabeca),
// entao os saves continuam identicos byte a byte.
//   +0xCC byte  marca do patrocinador master (1..N; 0 = nenhum)
//   +0xCD byte  epocas que faltam do contrato (contando a atual)
//   +0xCE dword valor por epoca (reais, com a inflacao da assinatura)
//   +0xD2 byte  marca do contrato do humano que acabou (renovacao na epoca seguinte)
//   +0xD8 dword patrocinio pontual: valor por jogo; +0xDC byte a marca; +0xDD byte os jogos
//               que faltam
public static class Contrato
{
    const int Base = 0xCB - 4;   // TTeam +0xCB = Cabeca[4]

    public const int MMarca = 0xCC, MAnos = 0xCD, MValor = 0xCE, UMarca = 0xD2,
        PValor = 0xD8, PMarca = 0xDC, PJogos = 0xDD;

    static int I(int campo) => campo - Base;

    public static byte B(this Equipa e, int campo) => e.Cabeca[I(campo)];
    public static void SetB(this Equipa e, int campo, byte v) => e.Cabeca[I(campo)] = v;
    public static int D(this Equipa e, int campo) => BinaryPrimitives.ReadInt32LittleEndian(e.Cabeca.AsSpan(I(campo)));
    public static void SetD(this Equipa e, int campo, int v) => BinaryPrimitives.WriteInt32LittleEndian(e.Cabeca.AsSpan(I(campo)), v);

    public static byte MarcaMaster(this Equipa e) => e.B(MMarca);
    public static byte EpocasMaster(this Equipa e) => e.B(MAnos);
    public static int ValorMaster(this Equipa e) => e.D(MValor);
    public static byte MarcaRenovacao(this Equipa e) => e.B(UMarca);
    public static byte MarcaPontual(this Equipa e) => e.B(PMarca);
    public static byte JogosPontual(this Equipa e) => e.B(PJogos);
    public static int ValorPontual(this Equipa e) => e.D(PValor);
}
