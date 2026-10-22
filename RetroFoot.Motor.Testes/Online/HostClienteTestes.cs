using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

// O jogo online de ponta a ponta no banco da memoria: o host roda o motor; o cliente recebe o
// retrato de cada rodada, prepara o time numa copia (a automatica com o sorteio dele) e marca
// PRONTO; a rodada so anda com os dois prontos e a escalacao do cliente e a que joga.
public class HostClienteTestes
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

    [Fact(Timeout = 120_000)]
    public async Task RodadasComHostECliente()
    {
        var banco = new BancoMemoria();
        var sh = await Sala.Criar(banco, "host", "Juan", "0.2.0");
        var sc = await Sala.Entrar(banco, "cli", sh.Codigo, "João");
        Assert.Equal(2, (await sh.Jogadores()).Count);

        var s = Jogo98.Ler(Entrada());
        var humanos = s.Equipas.Where(e => MercadoJogo.HumanoAtivo(s.Treinadores.FirstOrDefault(t => t.ID == e.Treinador))).ToList();
        var minha = humanos[0];
        // o segundo humano: o treinador de outro time passa a humano
        var dele = humanos.Count > 1 ? humanos[1] : s.Equipas.First(e => e != minha && s.Divisoes[0].Equipas.Contains(e.ID));
        s.Treinadores.First(t => t.ID == dele.Treinador).Humano = 1;
        await sh.DarEquipa("host", minha.ID);
        await sh.DarEquipa("cli", dele.ID);

        // o cliente: a cada rodada nova, a escalacao automatica na copia e PRONTO
        using var cli = new ClienteOnline(sc);
        int rodadasCliente = 0;
        string ultima = "";
        Preparo? ultimoPreparo = null;
        cli.NovoRetrato += (r, rodada, versao) =>
        {
            if (rodada == ultima) return;
            ultima = rodada;
            Interlocked.Increment(ref rodadasCliente);
            _ = Task.Run(async () =>
            {
                var copia = Jogo98.Ler(r.Save);
                var e = copia.Equipas.First(x => x.ID == dele.ID);
                Escalacao.Automatico(new Aleatorio(12345), e);
                var p = new Preparo(e.Jogadores.Select(x => new Escalado(x.ID, Memoria.Escalacao(x))).ToList(), 20000, new(), 0);
                ultimoPreparo = p;
                var resp = await cli.Pronto(p);
                Assert.True(resp.Ok, resp.Motivo);
            });
        };

        int barreiras = 0;
        var jogou = new System.Collections.Generic.List<string>();
        HostOnline? host = null;
        var h = new Humano();
        h.Preparar = (j, e, jogo) =>
        {
            string antes = host!.Rodada;
            bool sair = false;
            host.Preparar(e, jogo);
            if (host.Rodada != antes) { barreiras++; if (barreiras > 4) sair = true; }
            if (!sair && e.ID == dele.ID)
            {
                // a escalacao que joga e a do cliente
                Assert.Equal(ultimoPreparo!.Escalacao.Select(x => (x.Id, x.Estado)), e.Jogadores.Select(x => (x.ID, Memoria.Escalacao(x))));
                Assert.Equal(20000, Ingressos.Razao(e));
                jogou.Add(host.Rodada);
            }
            return sair;
        };
        var jg = new JogoEmCurso(s, 99, Opcoes.Padrao with { Regras = TRegras.Launcher(8), Registrado = true }, h);
        host = new HostOnline(sh, jg);
        var motor = new Thread(() => jg.Ciclo()) { IsBackground = true };
        motor.Start();
        Assert.True(await Task.Run(() => motor.Join(100_000)));
        host.Dispose();
        Assert.Equal(5, barreiras);
        Assert.True(rodadasCliente >= 5);
        Assert.True(jogou.Count >= 2, $"barreiras {barreiras} cliente {rodadasCliente} jogou {string.Join(",", jogou)} humanos {humanos.Count} minha {minha.ID} dele {dele.ID}");
    }

    [Fact]
    public async Task SeteNaoCabem()
    {
        var banco = new BancoMemoria();
        var s = await Sala.Criar(banco, "u0", "Host", "0.2.0");
        for (int i = 1; i < Sala.Vagas; i++) await Sala.Entrar(banco, "u" + i, s.Codigo, "J" + i);
        await Assert.ThrowsAsync<Sala.SalaCheia>(() => Sala.Entrar(banco, "u7", s.Codigo, "Sete"));
        // quem ja esta volta para a mesma vaga
        var de = await Sala.Entrar(banco, "u3", s.Codigo, "J3");
        Assert.Equal(3, (await s.Jogadores()).First(x => x.Uid == "u3").Vaga);
        await Assert.ThrowsAsync<Sala.SalaInexistente>(() => Sala.Entrar(banco, "x", "ZZZZZZ", "X"));
    }
}
