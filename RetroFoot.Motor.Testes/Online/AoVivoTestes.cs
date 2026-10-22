using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Online;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;
using Xunit;
using TRegras = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.Motor.Testes.Online;

// A rodada ao vivo: o que o cliente monta com o inicio e os tiques (passando pelo JSON) e a
// mesma jornada do host: o minuto, os golos, os lances e as substituicoes de cada jogo.
public class AoVivoTestes
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

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    public void ClienteVeAMesmaJornada(uint semente)
    {
        var emissor = new AoVivo.Emissor();
        AoVivo.Receptor? cli = null;
        int rodadas = 0, tiques = 0, conferidos = 0;
        long bytes = 0; int maior = 0;
        JogoEmCurso j = null!;
        void Conferir(Jornada jn)
        {
            Assert.Equal(jn.Minuto, cli!.Jornada.Minuto);
            Assert.Equal(jn.NoIntervalo, cli.Jornada.NoIntervalo);
            Assert.Equal(jn.Jogos.Count, cli.Jornada.Jogos.Count);
            for (int i = 0; i < jn.Jogos.Count; i++)
            {
                var a = jn.Jogos[i]; var b = cli.Jornada.Jogos[i];
                Assert.Equal((a.Dados.Casa, a.Dados.Fora), (b.Dados.Casa, b.Dados.Fora));
                for (int l = 0; l < 2; l++)
                    if (a.Equipa[l] is { } ea) Assert.Equal(Memoria.Golos(ea), Memoria.Golos(b.Equipa[l]!));
                Assert.Equal(a.Lances.Select(x => (x.Lado, x.Minuto, x.Tipo, x.JogadorID)), b.Lances.Select(x => (x.Lado, x.Minuto, x.Tipo, x.JogadorID)));
                Assert.Equal(a.Extras.Select(x => (x.Tipo, x.Saiu?.ID, x.Entrou?.ID)), b.Extras.Select(x => (x.Tipo, x.Saiu?.ID, x.Entrou?.ID)));
                for (int l = 0; l < 2; l++)
                    if (a.Equipa[l] is { } ea)
                        Assert.Equal(ea.Jogadores.Select(p => Memoria.Escalacao(p)), b.Equipa[l]!.Jogadores.Select(p => Memoria.Escalacao(p)));
            }
            conferidos++;
        }
        var h = new Humano
        {
            Preparar = (jg, e, _) => { Escalacao.Automatico(jg.R, e); return rodadas >= 3; },
            ComecoJornada = (titulo, jn) =>
            {
                rodadas++;
                var ini = (JsonObject)JsonNode.Parse(emissor.Inicio(j, titulo, jn).ToJsonString())!;
                cli = new AoVivo.Receptor(ini);
            },
            Minuto = jn =>
            {
                tiques++;
                var txt = emissor.Tique(jn).ToJsonString();
                bytes += txt.Length; maior = Math.Max(maior, txt.Length);
                var t = (JsonObject)JsonNode.Parse(txt)!;
                Assert.True(cli!.Aplicar(t));
                Conferir(jn);
            },
            // a substituicao do intervalo e das lesoes: a primeira possivel (muda escalacoes)
            Substituicao = (jg, lado, saiu, motivo) => { if (motivo != 4 && saiu != null) JogoEmCurso.SubstituicaoForcada(jg.Equipa[lado]!, saiu, 0); },
        };
        j = new JogoEmCurso(Jogo98.Ler(Entrada()), semente, Opcoes.Padrao with { Regras = TRegras.Launcher(8) }, h);
        j.Ciclo();
        Assert.True(rodadas >= 3);
        Assert.True(tiques > 150);
        Assert.Equal(tiques, conferidos);
        Console.WriteLine($"AOVIVO tiques {tiques} media {bytes / tiques} B maior {maior} B");
    }
}
