using System;
using System.IO;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Online;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;
using Xunit;
using TRegras = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.Motor.Testes.Online;

// O retrato do jogo online (docs/multiplayer-online.md): continuar do retrato tirado na
// preparacao de um humano da o mesmo jogo que o host continuando sem parar (a base da troca de
// host). As preparacoes escalam no automatico; a n-esima sai e grava: os dois saves iguais.
// (Com os patrocinios: a jornada retomada nao repete as propostas, Mundo.PularAntesDaJornada.)
public class RetratoTestes
{
    static byte[] Entrada()
    {
        for (var d = AppContext.BaseDirectory; d != null; d = Path.GetDirectoryName(d))
        {
            var f = Path.Combine(d, "Oraculo", "j3-entrada.e98");
            if (File.Exists(f)) return File.ReadAllBytes(f);
        }
        throw new FileNotFoundException("j3-entrada.e98");
    }

    static Opcoes Op(TRegras r) => Opcoes.Padrao with { Regras = r, Patrocinios = true };

    // joga ate a preparacao numero fim (contando de 1) e grava ali; em tirar, guarda o retrato
    static byte[] Jogar(JogoEmCurso? j, Func<Humano, JogoEmCurso> criar, int inicio, int fim, int tirar, Action<Retrato>? retrato)
    {
        int n = inicio;
        var h = new Humano();
        h.Preparar = (jg, e, jogo) =>
        {
            n++;
            if (n == tirar) retrato?.Invoke(jg.Fotografar(e, jogo));
            if (n >= fim) return true;
            Escalacao.Automatico(jg.R, e);
            return false;
        };
        j = criar(h);
        j.Agora = () => new DateTime(2026, 10, 22, 12, 0, 0);
        j.Ciclo();
        Assert.NotNull(j.Gravado);
        return j.Gravado!;
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(3, 40)]
    [InlineData(17, 60)]
    [InlineData(31, 90)]
    [InlineData(45, 100)]
    [InlineData(70, 120)]
    public void ContinuarDoRetratoEIgualAoHost(int tirar, int fim)
    {
        foreach (var regras in new[] { TRegras.Original, TRegras.Launcher(8) })
        {
            Retrato? r = null;
            var host = Jogar(null, h => new JogoEmCurso(Jogo98.Ler(Entrada()), 4242, Op(regras), h), 0, fim, tirar, x => r = x);
            Assert.NotNull(r);
            // pela rede: comprimido e lido de volta
            var lido = Retrato.Ler(r!.Bytes());
            var outro = Jogar(null, h => lido.Jogo(Op(regras), h), tirar - 1, fim, -1, null);
            Assert.Equal(host, outro);
        }
    }
}
