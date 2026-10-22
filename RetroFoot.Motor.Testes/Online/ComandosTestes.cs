using System;
using System.IO;
using System.Linq;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Online;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;
using Xunit;
using TRegras = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.Motor.Testes.Online;

// Os comandos do tecnico remoto executados no host (com o motor parado na preparacao): o
// cliente prepara numa copia (o retrato) e manda o Preparo; o host aplica com as condicoes
// das telas.
public class ComandosTestes
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

    // o host parado na 1a preparacao humana; teste recebe o jogo, a equipa e o executor
    static void NoGancho(Action<JogoEmCurso, Equipa, ExecutorComandos> teste)
    {
        var h = new Humano();
        bool feito = false;
        h.Preparar = (j, e, _) =>
        {
            if (!feito) { feito = true; teste(j, e, new ExecutorComandos(j)); }
            return true;
        };
        var jg = new JogoEmCurso(Jogo98.Ler(Entrada()), 77, Opcoes.Padrao with { Regras = TRegras.Launcher(8), Registrado = true }, h);
        jg.Ciclo();
        Assert.True(feito);
    }

    [Fact]
    public void JsonIdaEVolta()
    {
        Comando[] cs =
        {
            new PedirEmprestimo(), new ConstruirBancada(), new Comprar(3, 44, 120000), new Vender(7, 50000), new Renovar(7, 3000),
            new Preparo(new() { new Escalado(1, 1), new Escalado(2, 2) }, 12500, new() { 9, 10 }, 1),
        };
        foreach (var c in cs)
        {
            var de = Comando.Ler(c.Json());
            Assert.Equal(c.GetType(), de.GetType());
            Assert.Equal(c.Json(), de.Json());
        }
    }

    [Fact]
    public void PreparoDoClienteViraAEscalacaoDoHost()
    {
        NoGancho((j, e, ex) =>
        {
            // o cliente: a copia do retrato, a formacao com o sorteio dele
            var copia = Retrato.Ler(j.Fotografar(e, null).Bytes());
            var s = Jogo98.Ler(copia.Save);
            var ec = s.Equipas.First(x => x.ID == e.ID);
            Escalacao.Automatico(new Aleatorio(999), ec);
            Assert.True(ExecutorComandos.EscalacaoValida(ec));
            var preparo = new Preparo(ec.Jogadores.Select(p => new Escalado(p.ID, Memoria.Escalacao(p))).ToList(), 15000, new() { 4, 5 }, 1);
            var semente = j.R.Semente;
            var r = ex.Executar(e.ID, Comando.Ler(preparo.Json()));
            Assert.Equal(Resposta.Feito, r);
            // a mesma escalacao e a mesma ordem, sem gastar o sorteio do host
            Assert.Equal(ec.Jogadores.Select(p => (p.ID, Memoria.Escalacao(p))), e.Jogadores.Select(p => (p.ID, Memoria.Escalacao(p))));
            Assert.Equal(semente, j.R.Semente);
            Assert.Equal(15000, Ingressos.Razao(e));
            var t = j.M.TreinadorDa(e)!;
            Assert.Equal(new ushort[] { 4, 5 }, MercadoJogo.Observados(j.M.S, t.ID));
            Assert.Equal(1, t.B1A[3]);
        });
    }

    [Fact]
    public void EscalacaoQueNaoFechaViraAutomatica()
    {
        NoGancho((j, e, ex) =>
        {
            // todos titulares: nao fecha
            var r = ex.Executar(e.ID, new Preparo(e.Jogadores.Select(p => new Escalado(p.ID, Memoria.EscTitular)).ToList(), 0, new(), 0));
            Assert.True(r.Ok);
            Assert.Equal("escalacao automatica", r.Motivo);
            Assert.True(ExecutorComandos.EscalacaoValida(e));
        });
    }

    [Fact]
    public void DinheiroComAsCondicoesDasTelas()
    {
        NoGancho((j, e, ex) =>
        {
            uint antes = e.Dinheiro;
            Assert.True(ex.Executar(e.ID, new PedirEmprestimo()).Ok);
            Assert.True(e.Dinheiro > antes);
            Assert.True(ex.Executar(e.ID, new PagarEmprestimo()).Ok);
            Assert.Equal(antes, e.Dinheiro);
            // o banco tem limite
            int pedidos = 0;
            while (ex.Executar(e.ID, new PedirEmprestimo()).Ok && pedidos < 1000) pedidos++;
            Assert.True(pedidos < 1000);
            // sem dinheiro nao constroi
            e.Dinheiro = 0;
            Assert.False(ex.Executar(e.ID, new ConstruirBancada()).Ok);
        });
    }

    [Fact]
    public void CompraComOPrecoVisto()
    {
        NoGancho((j, e, ex) =>
        {
            e.Dinheiro = 50_000_000;
            // um jogador que a CPU vende
            var (v, p, preco) = j.M.S.Equipas.Where(x => x != e)
                .SelectMany(x => x.Jogadores.Select(p => (x, p, j.Mc.PrecoParaComprar(e, x, p))))
                .First(t => t.Item3 > 0);
            Assert.False(ex.Executar(e.ID, new Comprar(v.ID, p.ID, preco + 1000)).Ok);
            Assert.True(ex.Executar(e.ID, new Comprar(v.ID, p.ID, preco)).Ok);
            Assert.Equal(e.ID, p.W36);
            // o mesmo jogador de novo: ja comprado
            Assert.False(ex.Executar(e.ID, new Comprar(v.ID, p.ID, preco)).Ok);
        });
    }

    [Fact]
    public void SoTecnicoHumano()
    {
        NoGancho((j, e, ex) =>
        {
            var cpu = j.M.S.Equipas.First(x => !MercadoJogo.HumanoAtivo(j.M.TreinadorDa(x)));
            Assert.False(ex.Executar(cpu.ID, new PedirEmprestimo()).Ok);
        });
    }
}
