using System;
using System.IO;
using System.Linq;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Save;
using Xunit;
using static RetroFoot.Motor.Partida.Memoria;
using Equipa = RetroFoot.Motor.Save.Equipa;
using Jogo = RetroFoot.Motor.Save.Jogo;

namespace RetroFoot.Motor.Testes;

// "ErroPontoFlutuante: Ln de valor nao positivo" depois de clicar Jogar. O Power do jogo
// (seg14:1194) e Exp(expoente x Ln(base)): com base 0 o Ln e operacao invalida (no
// original, "Invalid floating point operation"). Na partida:
//   Q = Power(ln(1 + 4 (A + M/2) / (3G' + D' + M'/2 + 1)) R, 1)
// sem medios e sem avancados em campo (A = M = 0: formacao sem avancados e os medios
// expulsos ou lesionados, os setores sao refeitos a cada saida) da ln(1) = 0 e Power(0, 1).
// O PreJogo.Publico (Power(x, 1,04), x = 0 com moral 0) tem o mesmo Power. E, acima dos
// limites de 16 bits (saves editados), a + b + 1 = 65536 da divisao por zero no R.
// O RetroFoot nao quebra: Power de base <= 0 da 0 (Q = 0, so o gol de sorte) e a divisao
// por zero do R da 0. Nos casos normais nada muda (o oraculo segue igual).
public class ErroPontoFlutuanteTestes
{
    static Equipa ComSetores(ushort g, ushort d, ushort m, ushort a)
    {
        var e = new Equipa { Moral = new byte[] { 0x81, 0, 0, 0, 0, 0 } };
        Poe16(e.B4D, 4, g); Poe16(e.B4D, 6, d); Poe16(e.B4D, 8, m); Poe16(e.B4D, 10, a);
        return e;
    }

    [Fact]
    public void SemMediosNemAvancados()
    {
        var e = ComSetores(15, 60, 0, 0);
        var adv = ComSetores(14, 50, 40, 30);
        Assert.Equal(0, Eventos.ChanceDeGol(e, adv).Cmp(Ext.F32(0)));
        // o adversario continua com o Q de sempre
        Assert.True(Eventos.ChanceDeGol(adv, e).Cmp(Ext.F32(0)) > 0);
    }

    [Fact]
    public void SomaDosSetoresEstoura()
    {
        // a3 = 1 + 0 + 0 + 32766, b3 = 1 + 0 + 0 + 3 x 0 + 32767: a3 + b3 + 1 = 65536 = 0
        var e = ComSetores(0, 0, 0, 32766);
        var adv = ComSetores(0, 32767, 0, 0);
        var R = Eventos.ForcaRelativa(e, adv);
        // t1 = 1/3, t2 = 2 x 1/3, t3 = 0
        Assert.Equal(Fpu.Soma(Fpu.Divide(Ext.F32(1), Ext.F32(3)), Fpu.Mult(Fpu.Divide(Ext.F32(1), Ext.F32(3)), Ext.F32(2))).Bytes(), R.Bytes());
        Assert.True(Eventos.ChanceDeGol(e, adv).Cmp(Ext.F32(0)) > 0);
    }

    [Fact]
    public void PotenciaDeBaseNaoPositiva()
    {
        Assert.Equal(0, Fpu.Potencia(Ext.F32(0), Ext.F32(1)).Cmp(Ext.F32(0)));
        Assert.Equal(0, Fpu.Potencia(Ext.F32(-5), Ext.F32(1)).Cmp(Ext.F32(0)));
        // a base positiva e o Exp(Ln) de sempre
        var dois = Ext.F32(2);
        Assert.Equal(Fpu.Exp(Fpu.Ln(dois).Mul(dois)).Bytes(), Fpu.Potencia(dois, dois).Bytes());
    }

    [Fact]
    public void PublicoComMoralZero()
    {
        var casa = new Equipa { Moral = new byte[6], Estadio = 5 };
        var fora = new Equipa { Moral = new byte[] { 0x81, 0, 0, 0, 0, 0 } };
        for (int i = 0; i < 11; i++)
        {
            casa.Jogadores.Add(new Jogador { Forca = 20 });
            fora.Jogadores.Add(new Jogador { Forca = 20 });
        }
        var jg = new JogoAtual { Dados = new Jogo() };
        jg.Equipa[Lado.Casa] = casa; jg.Equipa[Lado.Visit] = fora;
        PreJogo.Publico(new Aleatorio(1), jg);
        Assert.Equal(0u, jg.Dados.D40);
    }

    // o caso inteiro: a equipa humana entra so com goleiro e defesas (os medios e avancados
    // escalados viram defesas) e outras com moral 0; as jornadas jogam sem erro
    [Fact]
    public void JornadaSemMediosNemAvancados()
    {
        string? arq = null;
        for (var d = AppContext.BaseDirectory; d != null && arq == null; d = Path.GetDirectoryName(d))
        {
            var f = Path.Combine(d, "Oraculo", "j3-entrada.e98");
            if (File.Exists(f)) arq = f;
        }
        Assert.NotNull(arq);
        var s = Jogo98.Ler(File.ReadAllBytes(arq!));
        foreach (var e in s.Equipas.Take(4)) Array.Clear(e.Moral);
        int jornadas = 0, preparadas = 0;
        var j = new JogoEmCurso(s, 1, Opcoes.Padrao, new Humano
        {
            Preparar = (jj, e, _) =>
            {
                Rodada.Escalacao.Automatico(jj.R, e);
                foreach (var p in e.Jogadores)
                    if (p.Posicao > 1) p.Posicao = 1;
                preparadas++;
                return false;
            },
            FimJornada = _ => { if (++jornadas >= 2) throw new OperationCanceledException(); },
        });
        Assert.Throws<OperationCanceledException>(() => j.M.Temporada(null));
        Assert.Equal(2, jornadas);
        Assert.True(preparadas > 0);
    }
}
