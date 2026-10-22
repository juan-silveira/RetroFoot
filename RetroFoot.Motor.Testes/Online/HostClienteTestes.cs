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
        cli.NovoRetrato += (r, rodada, versao, inst) =>
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

    // quem sai do jogo (Ausentar) nao trava a rodada (o treinador fica de ferias) e volta com a
    // mesma chave para o mesmo time
    [Fact(Timeout = 120_000)]
    public async Task QuemSaiFicaDeFeriasEVoltaParaOTime()
    {
        var banco = new BancoMemoria();
        var sh = await Sala.Criar(banco, "host", "Juan", "0.2.0");
        var sc = await Sala.Entrar(banco, "cli", sh.Codigo, "João");
        var s = Jogo98.Ler(Entrada());
        var minha = s.Equipas.First(e => MercadoJogo.HumanoAtivo(s.Treinadores.FirstOrDefault(t => t.ID == e.Treinador)));
        var dele = s.Equipas.First(e => e != minha && s.Divisoes.Take(4).Any(d => d.Equipas.Contains(e.ID)));
        s.Treinadores.First(t => t.ID == dele.Treinador).Humano = 1;
        await sh.DarEquipa("host", minha.ID);
        await sh.DarEquipa("cli", dele.ID);
        await sc.Ausentar();
        int barreiras = 0;
        HostOnline? host = null;
        var h = new Humano();
        h.Preparar = (j, e, jogo) =>
        {
            string antes = host!.Rodada;
            host.Preparar(e, jogo);
            if (host.Rodada != antes) barreiras++;
            return barreiras > 2;
        };
        var jg = new JogoEmCurso(s, 5, Opcoes.Padrao with { Regras = TRegras.Launcher(8) }, h);
        host = new HostOnline(sh, jg);
        // ausente: as rodadas andam sem ele
        var motor = new Thread(() => jg.Ciclo()) { IsBackground = true };
        motor.Start();
        Assert.True(await Task.Run(() => motor.Join(60_000)));
        Assert.Equal(3, barreiras);
        // o treinador dele de ferias (o computador cuida do time)
        Assert.Equal(1, jg.M.TreinadorDa(jg.M.Equipa(dele.ID))!.B06);
        host.Dispose();
        // volta: a mesma vaga e o mesmo time, preparando
        await Sala.Entrar(banco, "cli", sh.Codigo, "João");
        var eu = (await sh.Jogadores()).First(x => x.Uid == "cli");
        Assert.Equal(dele.ID, eu.Equipa);
        Assert.Equal(1, eu.Vaga);
        Assert.Equal(Estados.Preparando, eu.Estado);
    }

    // quem some sem sair (o sinal de vida parado pelo prazo) tambem fica de ferias
    [Fact(Timeout = 120_000)]
    public async Task QuemSomeFicaDeFerias()
    {
        var prazo = HostOnline.PrazoAusente;
        HostOnline.PrazoAusente = TimeSpan.FromSeconds(2);
        try
        {
            var banco = new BancoMemoria();
            var sh = await Sala.Criar(banco, "host", "Juan", "0.2.0");
            await Sala.Entrar(banco, "cli", sh.Codigo, "João");
            var s = Jogo98.Ler(Entrada());
            var minha = s.Equipas.First(e => MercadoJogo.HumanoAtivo(s.Treinadores.FirstOrDefault(t => t.ID == e.Treinador)));
            var dele = s.Equipas.First(e => e != minha && s.Divisoes.Take(4).Any(d => d.Equipas.Contains(e.ID)));
            s.Treinadores.First(t => t.ID == dele.Treinador).Humano = 1;
            await sh.DarEquipa("host", minha.ID);
            await sh.DarEquipa("cli", dele.ID);
            int barreiras = 0;
            HostOnline? host = null;
            var h = new Humano();
            h.Preparar = (j, e, jogo) =>
            {
                string antes = host!.Rodada;
                host.Preparar(e, jogo);
                if (host.Rodada != antes) barreiras++;
                return barreiras > 1;
            };
            var jg = new JogoEmCurso(s, 5, Opcoes.Padrao with { Regras = TRegras.Launcher(8) }, h);
            host = new HostOnline(sh, jg);
            var motor = new Thread(() => jg.Ciclo()) { IsBackground = true };
            motor.Start();
            Assert.True(await Task.Run(() => motor.Join(60_000)));
            host.Dispose();
            Assert.Equal(1, jg.M.TreinadorDa(jg.M.Equipa(dele.ID))!.B06);
        }
        finally { HostOnline.PrazoAusente = prazo; }
    }

    // o prazo depois do primeiro PRONTO: quem nao ficou pronto vai no automatico e a rodada anda
    [Fact(Timeout = 120_000)]
    public async Task PrazoAcabaEARodadaAnda()
    {
        HostOnline.PrazoFixo = TimeSpan.FromSeconds(2);
        try
        {
            var banco = new BancoMemoria();
            var sh = await Sala.Criar(banco, "host", "Juan", "0.2.0");
            var sc = await Sala.Entrar(banco, "cli", sh.Codigo, "João");
            var s = Jogo98.Ler(Entrada());
            var minha = s.Equipas.First(e => MercadoJogo.HumanoAtivo(s.Treinadores.FirstOrDefault(t => t.ID == e.Treinador)));
            var dele = s.Equipas.First(e => e != minha && s.Divisoes.Take(4).Any(d => d.Equipas.Contains(e.ID)));
            s.Treinadores.First(t => t.ID == dele.Treinador).Humano = 1;
            await sh.DarEquipa("host", minha.ID);
            await sh.DarEquipa("cli", dele.ID);
            // o cliente esta conectado (sinal de vida) mas nunca marca PRONTO
            using var cli = new ClienteOnline(sc);
            var prazos = new System.Collections.Generic.List<int>();
            using var obs = banco.Observar($"{sh.Raiz}/rodada/prazo", (_, v) => { if (v?["segundos"] is { } x) lock (prazos) prazos.Add((int)x); });
            int barreiras = 0;
            HostOnline? host = null;
            var h = new Humano();
            h.Preparar = (j, e, jogo) =>
            {
                // (as 2 primeiras rodadas do campeonato; na Taca o host nao joga e ninguem fica
                // pronto: sem o primeiro PRONTO o prazo nao comeca)
                string id = $"{j.M.Ano}-{j.M.Jornada}-{(jogo?.Taca == TipoJogo.Taca ? "T" : "L")}";
                if (id != host!.Rodada && barreiras == 2) return true;
                string antes = host.Rodada;
                host.Preparar(e, jogo);
                if (host.Rodada != antes) barreiras++;
                return false;
            };
            var jg = new JogoEmCurso(s, 5, Opcoes.Padrao with { Regras = TRegras.Launcher(8) }, h);
            host = new HostOnline(sh, jg);
            var motor = new Thread(() => jg.Ciclo()) { IsBackground = true };
            motor.Start();
            Assert.True(await Task.Run(() => motor.Join(60_000)));
            host.Dispose();
            Assert.Equal(2, barreiras);
            // nao ficou de ferias (estava conectado): so foi no automatico
            Assert.Equal(0, jg.M.TreinadorDa(jg.M.Equipa(dele.ID))!.B06);
            lock (prazos) Assert.Contains(2, prazos);
        }
        finally { HostOnline.PrazoFixo = null; }
    }

    // o host passa o host na preparacao: o outro aparelho continua do retrato e as rodadas andam
    [Fact(Timeout = 120_000)]
    public async Task PassarOHost()
    {
        var banco = new BancoMemoria();
        var sh = await Sala.Criar(banco, "host", "Juan", "0.2.0");
        var sc = await Sala.Entrar(banco, "cli", sh.Codigo, "João");
        var s = Jogo98.Ler(Entrada());
        var minha = s.Equipas.First(e => MercadoJogo.HumanoAtivo(s.Treinadores.FirstOrDefault(t => t.ID == e.Treinador)));
        var dele = s.Equipas.First(e => e != minha && s.Divisoes.Take(4).Any(d => d.Equipas.Contains(e.ID)));
        s.Treinadores.First(t => t.ID == dele.Treinador).Humano = 1;
        await sh.DarEquipa("host", minha.ID);
        await sh.DarEquipa("cli", dele.ID);
        // o host passa na 1a barreira
        HostOnline? h1 = null;
        var hum1 = new Humano { Preparar = (j, e, jogo) => h1!.Preparar(e, jogo) };
        var j1 = new JogoEmCurso(s, 5, Opcoes.Padrao with { Regras = TRegras.Launcher(8) }, hum1);
        h1 = new HostOnline(sh, j1);
        h1.PassarHost("cli");
        var m1 = new Thread(() => j1.Ciclo()) { IsBackground = true };
        m1.Start();
        Assert.True(await Task.Run(() => m1.Join(30_000)));
        h1.Dispose();
        Assert.True(h1.Passou);
        Assert.Equal("cli", (string?)await banco.Ler($"{sh.Raiz}/host"));
        // (o antigo host sai do jogo: fica de ferias no host novo)
        await sh.Ausentar();
        // o aparelho do cliente vira o host: o retrato publicado e um motor novo
        var no = (System.Text.Json.Nodes.JsonObject)(await banco.Ler($"{sh.Raiz}/retrato"))!;
        var r = Retrato.Ler(Convert.FromBase64String((string)no["dados"]!));
        int barreiras = 0;
        HostOnline? h2 = null;
        var hum2 = new Humano
        {
            Preparar = (j, e, jogo) =>
            {
                string antes = h2!.Rodada;
                bool sair = h2.Preparar(e, jogo);
                if (h2.Rodada != antes) barreiras++;
                return sair || barreiras > 2;
            },
        };
        var j2 = r.Jogo(Opcoes.Padrao with { Regras = TRegras.Launcher(8) }, hum2);
        h2 = new HostOnline(sc, j2);
        var m2 = new Thread(() => j2.Ciclo()) { IsBackground = true };
        m2.Start();
        Assert.True(await Task.Run(() => m2.Join(60_000)));
        h2.Dispose();
        Assert.Equal(3, barreiras);
        // a mesma rodada em que o host passou continua no host novo
        Assert.True(r.Save.Length > 0);
    }

    // a paralisacao: o cliente para o jogo durante a rodada, troca um jogador e retoma; a troca
    // vale no host (a escalacao e o extra da substituicao) e o jogo segue
    [Fact(Timeout = 120_000)]
    public async Task ParalisacaoComTroca()
    {
        HostOnline.PrazoFixo = TimeSpan.FromSeconds(1);
        try
        {
            var banco = new BancoMemoria();
            var sh = await Sala.Criar(banco, "host", "Juan", "0.2.0");
            var sc = await Sala.Entrar(banco, "cli", sh.Codigo, "João");
            var s = Jogo98.Ler(Entrada());
            var minha = s.Equipas.First(e => MercadoJogo.HumanoAtivo(s.Treinadores.FirstOrDefault(t => t.ID == e.Treinador)));
            var dele = s.Equipas.First(e => e != minha && s.Divisoes.Take(4).Any(d => d.Equipas.Contains(e.ID)));
            s.Treinadores.First(t => t.ID == dele.Treinador).Humano = 1;
            await sh.DarEquipa("host", minha.ID);
            await sh.DarEquipa("cli", dele.ID);
            using var cli = new ClienteOnline(sc);
            // o cliente fica pronto em toda rodada (com a automatica da copia)
            cli.NovoRetrato += (r, rodada, versao, inst) => _ = Task.Run(async () =>
            {
                var c = Jogo98.Ler(r.Save);
                var e = c.Equipas.First(x => x.ID == dele.ID);
                Escalacao.Automatico(new Aleatorio(3), e);
                await cli.Pronto(new Preparo(e.Jogadores.Select(x => new Escalado(x.ID, Memoria.Escalacao(x))).ToList(), 10000, new(), 0));
            });
            HostOnline? host = null;
            int rodadas = 0;
            bool pediu = false;
            Resposta? troca = null;
            JogoAtual? jogoDele = null;
            var h = new Humano();
            h.Preparar = (j, e, jogo) => { bool sair = host!.Preparar(e, jogo); return sair || rodadas >= 1; };
            h.ComecoJornada = (t, jn) => { rodadas++; host!.TransmitirInicio(t, jn); jogoDele = jn.Jogos.First(x => x.Dados.Casa == dele.ID || x.Dados.Fora == dele.ID); };
            h.Minuto = jn =>
            {
                if (jn.Minuto == 30 && !pediu)
                {
                    pediu = true;
                    // o cliente: parar, trocar o 1o titular de linha pelo 1o reserva de linha e retomar
                    var eq = jogoDele!.Equipa[jogoDele.Dados.Casa == dele.ID ? 0 : 1]!;
                    var sai = eq.Jogadores.First(p => p.Posicao != 0 && Memoria.Escalacao(p) == Memoria.EscTitular);
                    var entra = eq.Jogadores.First(p => p.Posicao != 0 && Memoria.Escalacao(p) == Memoria.EscReserva);
                    _ = Task.Run(async () =>
                    {
                        await cli.Executar(new Parar());
                        troca = await cli.Executar(new Trocar(sai.ID, entra.ID));
                        await cli.Executar(new Retomar());
                    });
                    // o motor espera o pedido chegar (o cliente e de outro "aparelho")
                    var ate = DateTime.UtcNow.AddSeconds(5);
                    while (DateTime.UtcNow < ate && troca == null) { host!.NoMinuto(jn); Thread.Sleep(50); }
                }
                host!.NoMinuto(jn);
                host.TransmitirMinuto(jn);
            };
            var jg = new JogoEmCurso(s, 9, Opcoes.Padrao with { Regras = TRegras.Launcher(8) }, h);
            host = new HostOnline(sh, jg);
            var motor = new Thread(() => jg.Ciclo()) { IsBackground = true };
            motor.Start();
            Assert.True(await Task.Run(() => motor.Join(90_000)));
            host.Dispose();
            Assert.NotNull(troca);
            Assert.True(troca!.Ok, troca.Motivo);
            Assert.Contains(jogoDele!.Extras, x => x.Tipo == TipoExtra.Substituicao && x.Minuto == 30);
        }
        finally { HostOnline.PrazoFixo = null; }
    }

    // a venda de um tecnico (o leilao no host, sem janela) vira o aviso de transferencia para
    // todos (rodada/noticia), com quem vendeu e quem comprou
    [Fact(Timeout = 120_000)]
    public async Task VendaViraAviso()
    {
        var banco = new BancoMemoria();
        var sh = await Sala.Criar(banco, "host", "Juan", "0.2.0");
        var sc = await Sala.Entrar(banco, "cli", sh.Codigo, "João");
        var s = Jogo98.Ler(Entrada());
        var minha = s.Equipas.First(e => MercadoJogo.HumanoAtivo(s.Treinadores.FirstOrDefault(t => t.ID == e.Treinador)));
        var dele = s.Equipas.First(e => e != minha && s.Divisoes.Take(4).Any(d => d.Equipas.Contains(e.ID)));
        s.Treinadores.First(t => t.ID == dele.Treinador).Humano = 1;
        await sh.DarEquipa("host", minha.ID);
        await sh.DarEquipa("cli", dele.ID);
        using var cli = new ClienteOnline(sc);
        var avisos = new System.Collections.Generic.List<System.Text.Json.Nodes.JsonObject>();
        using var obs = banco.Observar($"{sh.Raiz}/rodada/noticia", (_, v) => { if (v is System.Text.Json.Nodes.JsonObject o) lock (avisos) avisos.Add(o); });
        Resposta? venda = null;
        ushort vendido = 0;
        cli.NovoRetrato += (r, rodada, versao, inst) => _ = Task.Run(async () =>
        {
            if (venda != null) return;
            var c = Jogo98.Ler(r.Save);
            var e = c.Equipas.First(x => x.ID == dele.ID);
            // o melhor jogador de linha, por quase nada: a CPU compra
            var p = e.Jogadores.Where(x => x.Posicao != 0).OrderByDescending(x => x.Forca).First();
            vendido = p.ID;
            venda = await cli.Executar(new Vender(p.ID, 0));
            Escalacao.Automatico(new Aleatorio(3), e);
            await cli.Pronto(new Preparo(e.Jogadores.Where(x => x.ID != p.ID).Select(x => new Escalado(x.ID, Memoria.Escalacao(x))).ToList(), 10000, new(), 0));
        });
        HostOnline? host = null;
        var h = new Humano { Preparar = (j, e, jogo) => host!.Preparar(e, jogo) || venda != null };
        var jg = new JogoEmCurso(s, 11, Opcoes.Padrao with { Regras = TRegras.Launcher(8) }, h);
        host = new HostOnline(sh, jg);
        var motor = new Thread(() => jg.Ciclo()) { IsBackground = true };
        motor.Start();
        Assert.True(await Task.Run(() => motor.Join(60_000)));
        host.Dispose();
        await Task.Delay(500);
        Assert.NotNull(venda);
        Assert.Equal("vendido", venda!.Motivo);
        lock (avisos) Assert.Contains(avisos, a => (int?)a["jogador"] == vendido && (int?)a["vendedor"] == dele.ID);
    }
}
