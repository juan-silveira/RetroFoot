using System;
using System.Collections.Generic;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Partida.Memoria;
using static RetroFoot.Motor.Partida.Sorteio;

namespace RetroFoot.Motor.Partida;

// O motor da partida do jogo classico (98.003), minuto a minuto, descompilado do executavel:
// relogio da jornada (TjourneyDlg, seg10), os eventos de cada minuto (seg03), penaltis
// (seg08, seg03:81ed), desempate da Taca (seg11:0d46), publico e arbitro (seg03:8db2). As
// chamadas ao Random sao feitas na mesma ordem e com os mesmos argumentos do original.
// (Traduzido de motor/partida.)

public static class Lado { public const int Casa = 0, Visit = 1; }

public static class TipoJogo { public const int Campeonato = 0, Taca = 1, SemEquipas = 2; }

// tipos de lance (+0x08 do TGameEvent)
public static class TipoLance
{
    public const int Gol = 1, Penalti = 2, Amarelo = 3, Expulsao = 4, Lesao = 5, GolAnulado = 6;
}

// o TGameEvent (seg03:c1bf): +0x04 lado, +0x06 minuto, +0x08 tipo, +0x0A id do jogador,
// +0x0C posicao dele na hora do lance. SegundoAmarelo: o vermelho veio do 2o amarelo (o byte
// +0x0D do launcher, desenhado com o amarelo atras).
public sealed class Lance
{
    public int Lado, Minuto, Tipo;
    public ushort JogadorID;
    public byte Posicao;
    public Jogador Jogador = null!;
    public bool SegundoAmarelo;
    // com os acrescimos (Jornada.Acrescimos): o n de "45'+n'"; Minuto fica no 45, 90 ou 120
    public int Acrescimo;

    internal static Lance Novo(int lado, int minuto, int tipo, Jogador j) =>
        new() { Lado = lado, Minuto = minuto, Tipo = tipo, JogadorID = j.ID, Posicao = j.Posicao, Jogador = j };
}

// o TGame em memoria: +0x04 titulo (Dados.B04), +0x2D tipo (Dados.Taca), +0x2E e +0x32
// equipas, +0x36 preco do bilhete (Dados.B36), +0x40 publico (Dados.D40), +0x44 lances,
// +0x48 arbitro, +0x4C e +0x4E influencia do arbitro, +0x50 prorrogacao (Dados.B50)
public sealed class JogoAtual
{
    public Jogo Dados = new();
    public Equipa?[] Equipa = new Equipa?[2];
    public Treinador?[] Treinador = new Treinador?[2];
    public Arbitro? Arbitro;
    public List<Lance> Lances = new();
    // substituicoes e penaltis sem gol desta sessao (Extras.cs): fora do save
    public List<LanceExtra> Extras = new();
    // os acrescimos sorteados deste jogo (Jornada.Acrescimos): 1o tempo, 2o tempo, prorrogacao
    public int[] Acrescimos = new int[3];

    internal bool TemEquipas => Equipa[Lado.Casa] != null && Equipa[Lado.Visit] != null;

    // +0x4C (casa) ou +0x4E (visitante) como inteiro de 16 bits com sinal
    public short Influencia(int lado) => lado == Lado.Casa ? (short)Dados.W4C : (short)Dados.W4E;

    internal bool HumanoNoLado(int lado) => Humano(Treinador[lado]);

    // seg03:b03b: as duas equipas existem e tem os mesmos gols
    public bool Empatado => TemEquipas && Golos(Equipa[Lado.Casa]!) == Golos(Equipa[Lado.Visit]!);
}

// as partes de interface que o motor chama (todas podem ser null)
public sealed class Ganchos
{
    // seg03:a5ad: a janela de substituicao de um time humano (motivo 2 lesao, 3 expulsao do
    // goleiro, 4 intervalo). Quem implementa faz a troca (escalacao, +0x4D).
    public Action<JogoAtual, int, Jogador?, int>? Substituicao;
    // a escolha do batedor na janela de penalti (seg08:328b) quando quem cobra e humano
    public Func<Equipa, List<Jogador>, Jogador?, Jogador?>? Batedor;
    // chamado a cada lance novo ou mudado (o redesenho seg03:9ecd)
    public Action<JogoAtual, Lance>? Lance;
    // depois de cada cobranca de penalti (seg08:39b6) com humano num dos lados, a janela de
    // penalti (seg08:328b) mostra: atacante, defensor, batedor e o resultado (ResultadoPenalti)
    public Action<Equipa, Equipa, Jogador, int>? Cobranca;
    // cada substituicao ou penalti sem gol anotado em JogoAtual.Extras (Extras.cs)
    public Action<JogoAtual, LanceExtra>? Extra;
}

public static class PreJogo
{
    internal static readonly Ext C104 = Fpu.Bits(0x851EB851EB851EB8, 0x3FFF);   // 1,04 (seg03:b1ac)
    internal static readonly Ext C03 = Fpu.Bits(0x999999999999999A, 0x3FFD);    // 0,3 (seg03:b093)
    internal static readonly Ext Um = Ext.F32(1);
    internal static readonly Ext Dois = Ext.F32(2), Tres = Ext.F32(3), Quatro = Ext.F32(4), Dez = Ext.F32(10);
    internal static readonly byte[] R48_09 = { 0x80, 0x66, 0x66, 0x66, 0x66, 0x66 };   // Real 0,9 (seg03:c0f4)

    // a parte de TGame.Create (seg03:8db2) que e do motor, na ordem do original: +0x50 = 0,
    // publico (seg03:b0a1, um RandomReal), lances vazios, arbitro (seg03:1a66, sem sorteio)
    // e a influencia dele (seg03:c95c; a casa recebe +2)
    public static void PrepararJogo(Aleatorio r, JogoAtual jg, List<Arbitro> arbitros)
    {
        jg.Dados.B50 = 0;
        jg.Dados.W4C = 0; jg.Dados.W4E = 0;
        Publico(r, jg);
        jg.Lances = new List<Lance>();
        jg.Arbitro = null;
        jg.Dados.Arbitro = 0;
        if (!jg.TemEquipas) return;
        var a = EscolherArbitro(arbitros, jg.Equipa[Lado.Casa]!, jg.Equipa[Lado.Visit]!);
        if (a == null) return;
        jg.Arbitro = a;
        jg.Dados.Arbitro = a.ID;
        jg.Dados.W4C = (ushort)(InfluenciaDoArbitro(a, jg.Equipa[Lado.Casa]!) + 2);
        jg.Dados.W4E = (ushort)InfluenciaDoArbitro(a, jg.Equipa[Lado.Visit]!);
    }

    // seg03:b0a1. Com F = forca media do elenco inteiro e W = moral:
    //   base = 3 F(casa) + F(visitante) (16 bits)
    //   x = (base + 3 RandomReal) raiz(W casa) raiz(W visitante) 200
    //   publico = Round(Power(Real48(x), 1,04) (1 + 0,3 [Taca])), limitado ao estadio
    public static void Publico(Aleatorio r, JogoAtual jg)
    {
        if (!jg.TemEquipas || jg.Dados.Taca == TipoJogo.SemEquipas) { jg.Dados.D40 = 0; return; }
        var casa = jg.Equipa[Lado.Casa]!; var fora = jg.Equipa[Lado.Visit]!;
        short ff = ForcaMedia(fora);
        short fc = ForcaMedia(casa);
        var @base = Ext.Int((short)(3 * fc + ff));
        var x = Fpu.Soma(Fpu.Mult(Ext.F64(r.Real()), Tres), @base);
        x = Fpu.Mult(Fpu.Raiz(Fpu.DeReal48(casa.Moral)), x);
        x = Fpu.Mult(Fpu.Raiz(Fpu.DeReal48(fora.Moral)), x);
        x = Fpu.Mult(x, Ext.F32(200));
        var x48 = Fpu.ParaReal48(x);
        var p = Fpu.Potencia(Fpu.DeReal48(x48), C104);
        long taca = jg.Dados.Taca == TipoJogo.Taca ? 1 : 0;
        var f = Fpu.Soma(Fpu.Mult(Ext.Int(taca), C03), Um);
        jg.Dados.D40 = (uint)LotacaoDoEstadio(casa, Fpu.Arredondar(Fpu.Mult(p, f)));
    }

    // seg03:bf35: 5000 x N (32 bits)
    public static int CapacidadeDoEstadio(Equipa e) => e.Estadio * 5000;

    // seg03:c0c0: o publico limitado a capacidade; atualiza a ocupacao media do estadio
    public static int LotacaoDoEstadio(Equipa e, int publico)
    {
        int cap = CapacidadeDoEstadio(e);
        int v = Math.Min(publico, cap);
        var occ = Fpu.ParaReal48(Fpu.Divide(Ext.Int(v), Ext.Int((long)cap + 1)));
        e.EstadioReal = MediaExponencial(e.EstadioReal, R48_09, occ);
        return v;
    }

    // seg14:10d1: x := Real48(x a + (1 - a) b), em Real de 6 bytes (tambem o moral, a = 0,85)
    public static byte[] MediaExponencial(byte[] x, byte[] a, byte[] b)
    {
        var ea = Fpu.DeReal48(a);
        var t1 = Fpu.Mult(Fpu.DeReal48(x), ea);
        var t2 = Fpu.Subt(Um, ea);
        return Fpu.ParaReal48(Fpu.Soma(Fpu.Mult(Fpu.DeReal48(b), t2), t1));
    }

    static byte[] PaisDoArbitro(Arbitro a) => Texto(a.Pais);
    static byte[] Tres3(byte[] p) => p.Length > 3 ? p[..3] : p;

    // seg03:1a66: o 1o livre (+0x06 != 0) do pais das duas equipas; senao, o 1o livre de
    // um pais diferente das duas. O escolhido fica ocupado ate o fim do jogo (seg03:9d41).
    public static Arbitro? EscolherArbitro(List<Arbitro> arbitros, Equipa casa, Equipa fora)
    {
        var pc = Tres3(PaisDaEquipa(casa)); var pf = Tres3(PaisDaEquipa(fora));
        for (int passo = 0; passo < 2; passo++)
            foreach (var a in arbitros)
            {
                if (a.B06 == 0) continue;
                var pa = PaisDoArbitro(a);
                bool ok = passo == 0
                    ? pa.AsSpan().SequenceEqual(pc) && pa.AsSpan().SequenceEqual(pf)
                    : !pa.AsSpan().SequenceEqual(pc) && !pa.AsSpan().SequenceEqual(pf);
                if (ok) { a.B06 = 0; return a; }
            }
        return null;
    }

    // os bytes do TTeamAttr de +0x2D em diante, como na memoria: nome curto (string[20]),
    // cores (+0x42, +0x46), pais (string[3], +0x4A) e nivel (+0x4E)
    static byte[] AtributosDaEquipa(Equipa e)
    {
        var b = new byte[64];
        var n = Texto(e.NomeCurto);
        if (n.Length > 20) n = n[..20];
        b[0] = (byte)n.Length;
        n.CopyTo(b, 1);
        Array.Copy(e.CorLetra, 0, b, 0x42 - 0x2D, 4);
        Array.Copy(e.CorFundo, 0, b, 0x46 - 0x2D, 4);
        var p = Tres3(PaisDaEquipa(e));
        b[0x4A - 0x2D] = (byte)p.Length;
        p.CopyTo(b, 0x4B - 0x2D);
        b[0x4E - 0x2D] = e.Nivel;
        return b;
    }

    // seg03:c95c: soma, do fim para o comeco, letra do arbitro - letra do nome curto da
    // equipa, ate o tamanho do mais longo (16 bits); |soma mod 11|, +3 do mesmo pais
    public static short InfluenciaDoArbitro(Arbitro a, Equipa e)
    {
        var nome = Texto(a.Nome);
        if (nome.Length > 40) nome = nome[..40];
        var ar = new byte[64];
        ar[0] = (byte)nome.Length;
        nome.CopyTo(ar, 1);
        var eq = AtributosDaEquipa(e);
        int n = Math.Max(ar[0], eq[0]);
        short s = 0;
        for (int i = n; i >= 1; i--) { s += ar[i]; s -= eq[i]; }
        short v = (short)(s % 11);
        if (v < 0) v = (short)-v;
        if (Tres3(PaisDoArbitro(a)).AsSpan().SequenceEqual(Tres3(PaisDaEquipa(e)))) v += 3;
        return v;
    }
}
