using System;
using System.Numerics;
using RetroFoot.Motor.Save;

namespace RetroFoot.Motor.Regras;

// As opcoes que mudam as regras do jogo: o numero de equipas por divisao, o limite de
// jogadores por equipa, os cartoes amarelos e a tatica 5-0-5. O padrao (Original) e o
// jogo classico (98.003) puro; as outras combinacoes sao as dos executaveis do launcher
// (ELIF08.EXE ... ELIF20.EXE, tools/divisoes/patch_divisoes.py), aqui como codigo normal.
// (Traduzido de motor/regras.)
public readonly partial record struct Regras(int EquipasPorDivisao, int LimiteJogadores, bool CartoesAmarelos, bool Tatica505)
{
    // desempate da classificacao com vitorias (desempate.asm do launcher): pontos,
    // vitorias, saldo e golos marcados; o original nao olha as vitorias
    public bool DesempateVitorias { get; init; }

    // os acrescimos (Jornada.Acrescimos): 1 a 5 minutos no 1o tempo, 1 a 10 no 2o e 1 a 3 no
    // fim da prorrogacao. Mudam a sequencia do Random: so na versao Modificada
    public bool Acrescimos { get; init; }

    // a opcao "Sobem e descem por divisao" (so na versao Modificada): 0 = automatico (2, ou 4
    // com N >= 16, como o launcher); 1 a 4 = esse K, limitado por MaximoSobemDescem. Fica
    // gravada no save (MarcaSobemDescem): DoSave a le de la, nao das Configuracoes
    public int SobemDescemEscolhido { get; init; }

    // banco e substituicoes (so na versao Atual; 0 = como no classico: ate 5 reservas e 3
    // substituicoes). Com uma escolha, a prorrogacao da Taca da mais uma substituicao
    public int ReservasEscolhidas { get; init; }
    public int SubstituicoesEscolhidas { get; init; }
    public int MaxReservas => ReservasEscolhidas > 0 ? ReservasEscolhidas : 5;
    public int MaxSubstituicoes => SubstituicoesEscolhidas > 0 ? SubstituicoesEscolhidas : 3;
    public bool SubstituicaoNaProrrogacao => SubstituicoesEscolhidas > 0;

    // o jogo classico (98.003)
    public static readonly Regras Original = new(8, 24, false, false);

    // o ELIFnn.EXE do launcher: limite de 30 jogadores, cartoes amarelos e o 5-0-5
    public static Regras Launcher(int n) => new(n, 30, true, true);

    // base com o N do save: o numero de equipas da 1a divisao
    // base com o N e o K do save: o numero de equipas da 1a divisao e a marca do K escolhido
    // (sem marca = automatico, qualquer que seja o da base)
    public static Regras DoSave(Jogo98 s, Regras @base)
    {
        var r = @base with { SobemDescemEscolhido = SobemDescemDoSave(s) };
        return s.Divisoes.Count > 0 && s.Divisoes[0].Equipas.Count > 0 ? r with { EquipasPorDivisao = s.Divisoes[0].Equipas.Count } : r;
    }

    public int N => EquipasPorDivisao <= 0 ? 8 : EquipasPorDivisao;

    // R = 2(N - 1): 14 no original (seg05:11dc e os outros testes de ultima jornada)
    public short Jornadas => (short)(2 * (N - 1));

    // o maximo nas compras e nos leiloes (seg03:635a): 24 no original
    public short Limite => (short)(LimiteJogadores <= 0 ? 24 : LimiteJogadores);

    // a partir de quantos o computador com 5 estrangeiros vende (seg03:3b51)
    public short LimiteVendaCPU => (short)(Limite - 3);

    // K, as que sobem e descem: automatico = 2, ou 4 com N >= 16 (tools/divisoes/subida.asm);
    // o escolhido, ate MaximoSobemDescem
    public int SobemDescem => SobemDescemEscolhido > 0 ? Math.Min(SobemDescemEscolhido, MaximoSobemDescem(N)) : SobemDescemAutomatico(N);

    public static int SobemDescemAutomatico(int n) => n >= 16 ? 4 : 2;

    // o maior K com N equipas: 2K < N (na 2a a 4a fica pelo menos uma equipa que nem sobe nem
    // desce) e no maximo 4. Com 8 equipas, 3; com 10 ou mais, 4
    public static int MaximoSobemDescem(int n) => Math.Clamp((n - 1) / 2, 1, 4);

    // A marca do K no save: o ultimo byte (o 21o) do nome do Distrital, um string[20] do
    // Pascal. "Distrital" usa 10 bytes e o resto nunca e lido (o original grava os 21 bytes
    // como estao na memoria: o 11o e um "o" que sobra do "5a Divisao"). 0x40 + K; outro valor
    // (0 nos saves do original e nos do automatico) = automatico.
    const int IndiceMarca = 20;
    const byte BaseMarca = 0x40;

    public static int SobemDescemDoSave(Jogo98 s)
    {
        if (s.Divisoes.Count <= 4) return 0;
        var nome = s.Divisoes[4].Nome;
        if (nome.Length <= IndiceMarca || nome[0] >= IndiceMarca) return 0;
        int k = nome[IndiceMarca] - BaseMarca;
        return k is >= 1 and <= 4 ? k : 0;
    }

    // grava o K escolhido (0 = automatico: o byte volta a 0); false se o nome do Distrital
    // ocupa o byte da marca
    public static bool MarcarSobemDescem(Jogo98 s, int k)
    {
        if (s.Divisoes.Count <= 4) return false;
        var nome = s.Divisoes[4].Nome;
        if (nome.Length <= IndiceMarca || nome[0] >= IndiceMarca) return false;
        nome[IndiceMarca] = k is >= 1 and <= 4 ? (byte)(BaseMarca + k) : (byte)0;
        return true;
    }

    // minimo de equipas para um jogo novo (seg06:1818): 4N + K
    public int MinimoEquipas => 4 * N + SobemDescem;

    // alvo da evolucao de forca na posicao pos de uma divisao com D (+0x04):
    // D - 2 pos no original (seg03:6fed); D - 14 pos / (N - 1) (idiv) com N equipas
    public short Escada(short d, short pos) => (short)(d - (short)(14 * pos / (short)(N - 1)));

    // o Extended de seg05:0c2f, o passo da inflacao a cada jornada: 0,01 no original;
    // 0,01 x 14 / R com R jornadas, arredondado ao mais proximo (empates ao par)
    public byte[] PassoInflacao() => Extendido(14, 100 * Jornadas);

    // o real de 10 bytes (x87) da fracao positiva num/den
    static byte[] Extendido(BigInteger num, BigInteger den)
    {
        int e = 0;
        while (num >= 2 * den) { den *= 2; e++; }
        while (num < den) { num *= 2; e--; }
        num <<= 63;
        var q = BigInteger.DivRem(num, den, out var resto);
        var d2 = resto * 2;
        int c = d2.CompareTo(den);
        if (c > 0 || (c == 0 && !q.IsEven)) q += 1;
        if (q.GetBitLength() > 64) { q >>= 1; e++; }
        var b = new byte[10];
        ulong m = (ulong)q;
        for (int i = 0; i < 8; i++) b[i] = (byte)(m >> (8 * i));
        ushort ex = (ushort)(e + 16383);
        b[8] = (byte)ex; b[9] = (byte)(ex >> 8);
        return b;
    }
}
