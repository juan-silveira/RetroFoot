using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Partida;
using Xunit;
using TRegras = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.Motor.Testes;

// Os acrescimos (Jornada.Acrescimos, Regras.Acrescimos; Partida/Acrescimos.cs)
public partial class PartidaTestes
{
    // a impressao digital das jornadas dos saves (save gravado e lances, com e sem amarelos),
    // tirada do motor de antes dos acrescimos
    const string ImpressaoOriginal = "438CA9D5AAD671A74A03C1E8558403B212FAF8CCD5EF3A274BF8F091C252BD99";

    string Impressao(bool acrescimos)
    {
        using var h = SHA256.Create();
        int i = 0;
        foreach (var s in Saves())
        {
            var b = s.Gravar();
            foreach (bool am in new[] { false, true })
            {
                var l = new List<string>();
                var r = JogarSave(b, (uint)(3000 + i), am, true, l, acrescimos: acrescimos);
                h.TransformBlock(r.save, 0, r.save.Length, null, 0);
                var t = Encoding.ASCII.GetBytes(string.Join("\n", l));
                h.TransformBlock(t, 0, t.Length, null, 0);
            }
            i++;
        }
        h.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(h.Hash!);
    }

    // desligados: o mesmo motor de antes, save e lances iguais; ligados: outro jogo
    [Fact]
    public void SemAcrescimosIgualAoOriginal()
    {
        if (Saves().Count == 0) return;
        Assert.Equal(ImpressaoOriginal, Impressao(false));
        Assert.NotEqual(ImpressaoOriginal, Impressao(true));
        Assert.False(TRegras.Original.Acrescimos);
        Assert.False(TRegras.Launcher(8).Acrescimos);
    }

    // ligados: 1 a 5 no 1o tempo, 1 a 10 no 2o, 1 a 3 na prorrogacao; lances nos acrescimos
    // com Minuto 45/90/120 e o n dentro do sorteado; a mesma semente da o mesmo jogo
    [Fact]
    public void AcrescimosLigados()
    {
        int i = 0, jogos = 0, l45 = 0, l90 = 0, l120 = 0, golos = 0, prorrog = 0;
        var vistos = new HashSet<(int, int)>();
        foreach (var s in Saves())
        {
            var b = s.Gravar();
            for (uint k = 0; k < 3; k++)
            {
                var fin = new List<JogoAtual>();
                var l1 = new List<string>(); var l2 = new List<string>();
                var r1 = JogarSave(b, (uint)(5000 + 10 * i + k), k == 1, true, l1, acrescimos: true, finalizados: fin);
                var r2 = JogarSave(b, (uint)(5000 + 10 * i + k), k == 1, true, l2, acrescimos: true);
                Assert.Equal(r1.save, r2.save);
                Assert.Equal(l1, l2);
                foreach (var jg in fin)
                {
                    jogos++;
                    Assert.InRange(jg.Acrescimos[0], 1, 5);
                    Assert.InRange(jg.Acrescimos[1], 1, 10);
                    vistos.Add((0, jg.Acrescimos[0])); vistos.Add((1, jg.Acrescimos[1]));
                    if (jg.Dados.B50 == 1) { prorrog++; Assert.InRange(jg.Acrescimos[2], 1, 3); }
                    else Assert.Equal(0, jg.Acrescimos[2]);
                    for (int n = 1; n < jg.Lances.Count; n++)
                    {
                        var a = jg.Lances[n - 1]; var c = jg.Lances[n];
                        Assert.True(c.Minuto > a.Minuto || (c.Minuto == a.Minuto && c.Acrescimo >= a.Acrescimo));
                    }
                    foreach (var l in jg.Lances.Where(l => l.Acrescimo > 0))
                    {
                        Assert.Contains(l.Minuto, new[] { 45, 90, 120 });
                        int t = l.Minuto == 45 ? 0 : l.Minuto == 90 ? 1 : 2;
                        Assert.InRange(l.Acrescimo, 1, jg.Acrescimos[t]);
                        if (l.Minuto == 45) l45++; else if (l.Minuto == 90) l90++; else l120++;
                        if (l.Tipo is TipoLance.Gol or TipoLance.Penalti) golos++;
                    }
                    foreach (var x in jg.Extras.Where(x => x.Intervalo)) Assert.Equal(0, x.Acrescimo);
                }
            }
            i++;
        }
        saida.WriteLine($"{jogos} jogos ({prorrog} com prorrogacao): lances nos acrescimos 45+ {l45}, 90+ {l90}, 120+ {l120}; {golos} golos");
        if (jogos == 0) return;
        Assert.True(l45 > 0 && l90 > 0 && golos > 0);
        // todos os valores aparecem
        for (int n = 1; n <= 5; n++) Assert.Contains((0, n), vistos);
        for (int n = 1; n <= 10; n++) Assert.Contains((1, n), vistos);
    }

    // o relogio: o 45 fica parado enquanto anda o acrescimo; o intervalo so depois dele
    [Fact]
    public void RelogioDosAcrescimos()
    {
        var s = Saves().FirstOrDefault();
        if (s == null) return;
        var jn = new Jornada { R = new Aleatorio(77), Acrescimos = true };
        foreach (var g in s.Jogos)
        {
            var jg = new JogoAtual { Dados = g };
            jg.Equipa[0] = s.Equipas.Find(e => e.ID == g.Casa);
            jg.Equipa[1] = s.Equipas.Find(e => e.ID == g.Fora);
            foreach (var e in jg.Equipa) if (e != null) PrepararSimples(e);
            jn.Jogos.Add(jg);
        }
        bool intervaloVisto = false;
        int minutoNoIntervalo = 0, acrNoIntervalo = 0, ticks = 0, max45 = 0;
        jn.Ganchos.Substituicao = (_, _, _, _) => { };
        jn.Iniciar();
        var seq = new List<(int, int)>();
        while (jn.MinutoPassado())
        {
            ticks++;
            seq.Add((jn.Minuto, jn.Acrescimo));
            if (jn.NoIntervalo && !intervaloVisto)
            {
                intervaloVisto = true; minutoNoIntervalo = jn.Minuto; acrNoIntervalo = jn.Acrescimo;
                max45 = jn.Jogos.Max(j => j.Acrescimos[0]);
            }
        }
        Assert.True(intervaloVisto);
        Assert.Equal(45, minutoNoIntervalo);
        Assert.Equal(max45, acrNoIntervalo);
        Assert.Contains((45, 1), seq);
        Assert.Contains((46, 0), seq);
        Assert.True(seq.IndexOf((46, 0)) == seq.IndexOf((45, max45)) + 1);
        Assert.Contains((90, jn.Jogos.Max(j => j.Acrescimos[1])), seq);
        // de -4 a 95 (o original fecha depois de Fim + 5) e mais os acrescimos dos dois tempos
        Assert.Equal(100 + max45 + jn.Jogos.Max(j => j.Acrescimos[1]), ticks);
    }
}
