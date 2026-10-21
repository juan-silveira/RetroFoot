using System;
using System.IO;
using System.Linq;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;
using Xunit;
using Equipa = RetroFoot.Motor.Save.Equipa;

namespace RetroFoot.Motor.Testes;

// O convite a um treinador humano empregado (a "promocao" do jogo classico): um time que
// demite o treinador depois de um jogo (Mundo.Chicotada, seg03:5343) pode chamar o treinador de
// outra equipa com moral >= 1,25 se for de 11 a 19 pontos de forca media mais forte; o humano
// responde na janela do convite (seg03:8bcc -> TinviteCoachDlg). O Mundo pergunta pelo gancho
// AceitaConvite, que o JogoEmCurso liga a Humano.Convite (antes ficava sem ligar e o humano
// nunca era chamado).
public class ConviteTestes
{
    static readonly byte[] Moral15 = { 0x81, 0, 0, 0, 0, 0x40 };   // Real48 1,5
    static readonly byte[] Moral10 = { 0x81, 0, 0, 0, 0, 0 };      // Real48 1,0

    // o save, o treinador humano, o time dele (moral alta) e um time da CPU de 11 a 19 pontos
    // mais forte (ajustando a forca dos jogadores dele), os outros times com moral 1,0 (assim
    // so o humano passa na regra da moral)
    static (JogoEmCurso J, Treinador Humano, Equipa Dele, Equipa Forte) Cenario(Func<Treinador, Equipa, bool> resposta, Action<Treinador, Equipa> perguntado)
    {
        string? arq = null;
        for (var d = AppContext.BaseDirectory; d != null && arq == null; d = Path.GetDirectoryName(d))
        {
            var f = Path.Combine(d, "Oraculo", "j3-entrada.e98");
            if (File.Exists(f)) arq = f;
        }
        Assert.NotNull(arq);
        var s = Jogo98.Ler(File.ReadAllBytes(arq!));
        var j = new JogoEmCurso(s, 1, Opcoes.Padrao, new Humano { Convite = (t, e) => { perguntado(t, e); return resposta(t, e); } });
        var m = j.M;
        var humano = s.Treinadores.First(t => t.Humano != 0 && t.B06 == 0);
        var dele = s.Equipas.First(e => e.Treinador == humano.ID);
        Assert.False(m.NoDistrital(dele));
        foreach (var e in s.Equipas) e.Moral = (byte[])Moral10.Clone();
        dele.Moral = (byte[])Moral15.Clone();
        var forte = s.Divisoes.Take(4).SelectMany(d => d.Equipas).Select(id => s.Equipas.First(e => e.ID == id))
            .First(e => e != dele && s.Treinadores.First(t => t.ID == e.Treinador).Humano == 0);
        // a forca media do time forte 15 acima da do humano (e pelo menos 15: sem desempregados)
        for (int volta = 0; volta < 40; volta++)
        {
            int dif = Mundo.MediaForca(forte) - Mundo.MediaForca(dele);
            if (dif is > 10 and < 20 && Mundo.MediaForca(forte) >= 15) break;
            int passo = dif < 15 ? 1 : -1;
            foreach (var p in forte.Jogadores) p.Forca = (ushort)Math.Max(1, p.Forca + passo);
        }
        int final = Mundo.MediaForca(forte) - Mundo.MediaForca(dele);
        Assert.InRange(final, 11, 19);
        return (j, humano, dele, forte);
    }

    [Fact]
    public void HumanoComMoralAltaEChamadoPeloTimeMaisForte()
    {
        Treinador? quem = null; Equipa? onde = null;
        var (j, humano, dele, forte) = Cenario((_, _) => true, (t, e) => { quem ??= t; onde ??= e; });
        j.M.Chicotada(forte, 0);
        Assert.Same(humano, quem);
        Assert.Same(forte, onde);
        // aceitou: treina o time forte; o antigo treinador do forte vai para o time dele
        Assert.Equal(humano.ID, forte.Treinador);
        Assert.NotEqual(humano.ID, dele.Treinador);
    }

    [Fact]
    public void HumanoQueRecusaFicaNoTime()
    {
        int perguntas = 0;
        var (j, humano, dele, forte) = Cenario((_, _) => false, (_, _) => perguntas++);
        j.M.Chicotada(forte, 0);
        Assert.Equal(1, perguntas);
        Assert.Equal(humano.ID, dele.Treinador);
        Assert.NotEqual(humano.ID, forte.Treinador);
    }
}
