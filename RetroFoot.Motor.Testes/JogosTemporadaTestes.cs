using System.Linq;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Save;
using Xunit;

namespace RetroFoot.Motor.Testes;

// os jogos da temporada do RetroFoot: os 16 bits de cima do contador de jogos da carreira
public class JogosTemporadaTestes
{
    [Fact]
    public void ContaZeraEMedia()
    {
        Memoria.SemJogosDaTemporada = false;
        var p = new Jogador();
        // 300 jogos e 120 gols na carreira; 5 gols na temporada
        System.BitConverter.GetBytes(300u).CopyTo(p.Historial, 0);
        System.BitConverter.GetBytes(120u).CopyTo(p.Historial, 4);
        p.B14[0] = 5;
        for (int i = 0; i < 10; i++) Memoria.SomaJogoNaTemporada(p);
        Assert.Equal(10, Memoria.JogosNaTemporada(p));
        // a carreira nao muda com os jogos da temporada (so os 16 bits de baixo)
        Assert.Equal(300, Memoria.Historial(p, Memoria.HistJogos));
        Assert.Equal(0.4, Memoria.MediaGols(p, false)!.Value, 6);
        Assert.Equal(0.5, Memoria.MediaGols(p, true)!.Value, 6);
        Memoria.ZerarJogosNaTemporada(p);
        Assert.Equal(0, Memoria.JogosNaTemporada(p));
        Assert.Null(Memoria.MediaGols(p, true));
        Assert.Equal(300, Memoria.Historial(p, Memoria.HistJogos));
        // desligado (os testes do jogo classico), nao conta
        Memoria.SemJogosDaTemporada = true;
        Memoria.SomaJogoNaTemporada(p);
        Assert.Equal(0, Memoria.JogosNaTemporada(p));
        Memoria.SemJogosDaTemporada = false;
    }
}

public class ValorDeMercadoTestes
{
    [Fact]
    public void ValorVezes56NasFaixas()
    {
        foreach (var a in Arquivos.Saves().Take(1))
        {
            var s = Jogo98.Ler(System.IO.File.ReadAllBytes(a));
            var m = new RetroFoot.Motor.Mercado.MercadoJogo(s, new RetroFoot.Motor.Base.Aleatorio(0));
            foreach (var p in s.Equipas.SelectMany(e => e.Jogadores).Take(200))
            {
                int v = m.ValorDoJogador(p), vm = m.ValorDeMercado(p);
                double bruto = v * 5.6;
                Assert.InRange(vm, bruto * 0.9 - 1000, bruto + 1);
                int passo = vm >= 2000000 ? 100000 : vm >= 1000000 ? 50000 : vm > 500000 ? 25000 : vm > 100000 ? 10000 : 1000;
                Assert.Equal(0, vm % passo);
            }
        }
    }
}
