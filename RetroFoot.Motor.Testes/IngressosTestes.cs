using System;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;
using Xunit;
using static RetroFoot.Motor.Rodada.Campos;
using Equipa = RetroFoot.Motor.Save.Equipa;

namespace RetroFoot.Motor.Testes;

// O preco dos ingressos escolhido pelo tecnico (patch do launcher, ingressos.asm; secao 12
// de docs internos do launcher)
public class IngressosTestes
{
    static Ext X(double v) => Ext.F64(v);

    static Equipa Time(ushort id, byte escolha = 0)
    {
        var e = new Equipa { ID = id, Estadio = 200, Treinador = id };
        e.Cabeca[0] = (byte)'E'; e.Cabeca[1] = (byte)'F'; e.Cabeca[2] = (byte)'a';
        e.Cabeca[Ingressos.CabecaByte] = escolha;
        e.Moral = new byte[] { 0x81, 0, 0, 0, 0, 0 };          // 1,0
        for (int i = 0; i < 16; i++)
            e.Jogadores.Add(new Jogador { ID = (ushort)(id * 100 + i), Forca = 40, Posicao = (byte)(i % 4) });
        return e;
    }

    static (Mundo, Equipa, Equipa) Mundo2(byte escolha, uint semente = 1234)
    {
        var s = new Jogo98();
        X(1).Bytes().CopyTo(s.Cabecalho.Real2B46, 0);
        s.Treinadores.Add(new Treinador { ID = 10 });
        s.Treinadores.Add(new Treinador { ID = 20 });
        var casa = Time(10, escolha);
        var fora = Time(20);
        s.Equipas.Add(casa); s.Equipas.Add(fora);
        return (new Mundo(s, semente), casa, fora);
    }

    [Fact]
    public void EscolhaNoCabecalho()
    {
        var e = Time(1);
        Assert.Equal(0, Ingressos.Escolha(e));
        Assert.Equal(100, Ingressos.Percentual(e));
        // o campo do launcher: de 25 em 25 o mais perto, de 50 a 300; o byte = valor/25
        foreach (var (pedido, byteQuero, pctQuero) in new[] { (100, 4, 100), (300, 12, 300), (50, 2, 50),
                     (10, 2, 50), (999, 12, 300), (137, 5, 125), (138, 6, 150) })
        {
            Ingressos.Escolher(e, pedido);
            Assert.Equal(byteQuero, e.Cabeca[4]);
            Assert.Equal(pctQuero, Ingressos.Percentual(e));
        }
        // fora de 2..12 vale como nao escolhido (fator() do ingressos.asm)
        e.Cabeca[4] = 1; Assert.Equal(0, Ingressos.Escolha(e));
        e.Cabeca[4] = 13; Assert.Equal(0, Ingressos.Escolha(e));
    }

    [Fact]
    public void PrecoDaRenda()
    {
        // os casos comprovados no launcher: tabela 2 x 300% = 6, tabela 2 x 150% = 3
        Assert.Equal(6.0, Ingressos.Preco(X(2), Time(1, 12)).Float64());
        Assert.Equal(3.0, Ingressos.Preco(X(2), Time(1, 6)).Float64());
        Assert.Equal(1.0, Ingressos.Preco(X(2), Time(1, 2)).Float64());
        // sem escolha (ou byte invalido) o preco e o mesmo Ext, bit a bit
        var p = X(7.3);
        Assert.Equal(p.Bytes(), Ingressos.Preco(p, Time(1)).Bytes());
        Assert.Equal(p.Bytes(), Ingressos.Preco(p, Time(1, 200)).Bytes());
        Assert.Equal(p.Bytes(), Ingressos.Preco(p, null).Bytes());
        // 4 = 100% escolhido: x 1,0 exato
        Assert.Equal(p.Bytes(), Ingressos.Preco(p, Time(1, 4)).Bytes());
    }

    [Fact]
    public void ProcuraCaiComOPreco()
    {
        // (escolha/4)^-1,3, como floats da tabela
        for (byte k = 2; k <= 12; k++)
        {
            double f = Ingressos.Publico(X(1), Time(1, k)).Float64();
            Assert.True(Math.Abs(f - Math.Pow(k / 4.0, -1.3)) < 2e-4, $"k={k}: {f}");
            Assert.Equal((double)(float)f, f);
        }
        Assert.Equal(1.0, Ingressos.Publico(X(1), Time(1, 4)).Float64());
        Assert.Equal(X(1234.5).Bytes(), Ingressos.Publico(X(1234.5), Time(1)).Bytes());
    }

    [Fact]
    public void PublicoDoJogo()
    {
        int Pub(byte escolha, byte taca = 0)
        {
            var (m, casa, fora) = Mundo2(escolha);
            var j = new Jogo { Taca = taca, Casa = casa.ID, Fora = fora.ID };
            m.Publico(j);
            return (int)j.D40;
        }
        foreach (byte taca in new byte[] { 0, 1 })
        {
            int base0 = Pub(0, taca);
            Assert.True(base0 > 0);
            Assert.Equal(base0, Pub(4, taca));                     // 100%: igual
            for (byte k = 2; k <= 12; k++)
            {
                // base0 ja esta arredondado: erro de 0,5 x fator, mais o arredondamento novo
                double f = Ingressos.Publico(X(1), Time(1, k)).Float64();
                double quero = base0 * f;
                Assert.True(Math.Abs(Pub(k, taca) - quero) <= 0.5 * f + 0.5 + 1e-9, $"k={k} taca={taca}: {Pub(k, taca)} x {quero}");
            }
        }
        // o limite do estadio vem depois: com 50% a procura passa da capacidade
        var (m2, c2, f2) = Mundo2(2);
        c2.Estadio = 1;
        var j2 = new Jogo { Taca = 0, Casa = c2.ID, Fora = f2.ID };
        m2.Publico(j2);
        Assert.Equal(5000u, j2.D40);
    }

    [Fact]
    public void BilheteiraNoFimDoJogo()
    {
        foreach (var (escolha, quero) in new[] { (0, 12345 * 7), (12, 12345 * 21), (6, (int)Math.Round(12345 * 10.5, MidpointRounding.ToEven)) })
        {
            // campeonato: tudo para o mandante
            var (m, casa, fora) = Mundo2((byte)escolha);
            var j = new Jogo { Taca = 0, Casa = casa.ID, Fora = fora.ID, D40 = 12345 };
            X(7).Bytes().CopyTo(j.B36, 0);
            m.FimDeJogo(j);
            Assert.Equal(quero, Mundo.Saldo(casa, Mundo.BalBilheteira));
            Assert.Equal(0, Mundo.Saldo(fora, Mundo.BalBilheteira));
            // o preco guardado no jogo nao muda (a tela mostra o de tabela)
            Assert.Equal(7.0, Ext.Bytes(j.B36).Float64());
        }
        // a escolha do visitante nao conta
        {
            var (m, casa, fora) = Mundo2(0);
            fora.Cabeca[4] = 12;
            var j = new Jogo { Taca = 0, Casa = casa.ID, Fora = fora.ID, D40 = 1000 };
            X(7).Bytes().CopyTo(j.B36, 0);
            m.FimDeJogo(j);
            Assert.Equal(7000, Mundo.Saldo(casa, Mundo.BalBilheteira));
        }
    }

    [Fact]
    public void GuardadoNoSave()
    {
        string? arq = null;
        for (var d = AppContext.BaseDirectory; d != null && arq == null; d = System.IO.Path.GetDirectoryName(d))
        {
            var f = System.IO.Path.Combine(d, "Oraculo", "j3-entrada.e98");
            if (System.IO.File.Exists(f)) arq = f;
        }
        Assert.NotNull(arq);
        var s = Jogo98.Ler(System.IO.File.ReadAllBytes(arq!));
        Assert.All(s.Equipas, e => Assert.Equal(0, e.Cabeca[4]));
        Ingressos.Escolher(s.Equipas[3], 225);
        var lido = Jogo98.Ler(s.Gravar());
        Assert.Equal(9, lido.Equipas[3].Cabeca[4]);
        Assert.Equal(225, Ingressos.Percentual(lido.Equipas[3]));
        Assert.Equal(0, lido.Equipas[2].Cabeca[4]);
    }
}
