using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.NovoJogo;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Patrocinio;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;
using static RetroFoot.Motor.Rodada.Campos;
using CicloOpcoes = RetroFoot.Motor.Ciclo.Opcoes;
using NovoOpcoes = RetroFoot.Motor.NovoJogo.Opcoes;
using TRegras = RetroFoot.Motor.Regras.Regras;
using Equipa = RetroFoot.Motor.Save.Equipa;
using Jogo = RetroFoot.Motor.Save.Jogo;

namespace RetroFoot.Motor.Testes;

// O jogador automatico (soak): cria um jogo novo como a TelaInicio, liga os mesmos ganchos
// que a Sessao liga (Humano, Ganchos da rodada, janela do leilao, fim de temporada) com
// respostas simples (escalacao automatica, vende/compra/emprestimo/estadio/salario de vez
// em quando, aceita ou recusa propostas ao acaso) e joga varias temporadas inteiras. O motor
// roda numa thread propria com um vigia: sem nenhum gancho por Travou segundos = travou.
// De vez em quando o humano sai (grava) e o jogo e recarregado do save, como a TelaInicio.
// Verifica invariantes depois de cada jornada do campeonato e de cada fim de temporada.
public sealed class Soak
{
    public sealed record Config(string Nome, TRegras Regras, int Humanos = 1, bool Patrocinios = false,
        bool Registrado = false, bool DivisoesDiferentes = false, int Temporadas = 3, int Semente = 1,
        double Recarregar = 0.04);

    public readonly List<string> Problemas = new();
    public readonly List<string> Log = new();
    public int Temporadas, Jornadas, Recargas, Leiloes, Compras, Vendas, Emprestimos, Propostas;
    public int Travou = 60;

    readonly Config cfg;
    readonly string raiz;
    readonly Random rnd;
    JogoEmCurso J = null!;
    CicloOpcoes op = null!;
    volatile string onde = "";
    long pulso;
    int lancesSeguidos;
    bool terminar;
    int humanosIniciais;
    string ultimo = "";

    public Soak(Config c, string raizJogo)
    {
        cfg = c; raiz = raizJogo; rnd = new Random(c.Semente);
    }

    void Problema(string s)
    {
        string t = $"[{cfg.Nome}] T{Temporadas} J{J?.M.Jornada}: {s}";
        lock (Problemas) if (Problemas.Count < 200) Problemas.Add(t);
    }

    bool achouPoucos;
    void Pulso(string o)
    {
        if (!achouPoucos && J != null)
            foreach (var e in J.M.S.Equipas)
                if (e.Jogadores.Count < 11) { achouPoucos = true; Log.Add($"DIAG: {e.ID} com {e.Jogadores.Count} ao chegar em {o}; antes: {onde}; ultimo: {ultimo}"); }
        onde = o; Interlocked.Increment(ref pulso);
    }

    // ---------------------------------------------------------------- o jogo novo
    Jogo98 JogoNovo()
    {
        var d = Dados.Ler(raiz);
        var sel = d.ContarEquipas();
        sel.Minimo = cfg.Regras.MinimoEquipas;
        sel.Todos();
        sel.Confirmar(new Aleatorio((uint)cfg.Semente * 7919u));
        var g = new Aleatorio((uint)cfg.Semente * 104729u + 1);
        var nomes = Enumerable.Range(1, cfg.Humanos).Select(i => "Bot" + i).ToList();
        var e = Criacao.Criar(d, new NovoOpcoes { Moeda = NovoJogo.Tabelas.MoedaReais, Ano = 2026, Humanos = nomes, Regras = cfg.Regras }, g);
        e.IniciarTemporada();
        e.PrepararJornada(1, g);
        var s = Jogo98.Ler(e.Save(Exportar.DataHora(new DateTime(2026, 1, 1))).Gravar());
        if (cfg.DivisoesDiferentes && cfg.Humanos >= 2)
        {
            // o 2o humano passa para a 1a divisao (troca de treinador com a 1a equipa dela)
            var hs = s.Treinadores.Where(t => t.Humano != 0).ToList();
            var eh = s.Equipas.First(q => q.Treinador == hs[1].ID);
            var ec = s.Equipas.First(q => q.ID == s.Divisoes[0].Equipas[0]);
            (eh.Treinador, ec.Treinador) = (ec.Treinador, eh.Treinador);
            s = Jogo98.Ler(s.Gravar());
        }
        return s;
    }

    // ---------------------------------------------------------------- o laco
    public void Jogar()
    {
        var (bosman, plop) = Dados.ListasDoJogo(raiz);
        op = CicloOpcoes.Padrao with
        {
            Regras = cfg.Regras, Bosman = bosman, PLOP = plop,
            Registrado = cfg.Registrado, AddIn13 = cfg.Registrado,
            Patrocinios = cfg.Patrocinios, Marcas = Marcas.Ler(raiz),
            VerPenaltisCPU = true, VerSorteio = 1, VerExpulsoes = true, VerLesoes = true,
        };
        var s = JogoNovo();
        humanosIniciais = s.Treinadores.Count(t => t.Humano != 0);
        bool novo = true;
        uint semente = (uint)cfg.Semente;
        while (true)
        {
            J = new JogoEmCurso(s, semente++, op, Humano()) { Agora = () => new DateTime(2026, 1, 1) };
            if (novo) J.M.Retomar = -1;
            novo = false;
            Ligar();
            Verificar(false, "inicio");
            if (!Rodar()) return;
            if (J.Gravado == null) { Problema("o ciclo acabou sem ninguem sair"); return; }
            // o save gravado le e grava igual
            var lido = Jogo98.Ler(J.Gravado);
            var de_novo = lido.Gravar();
            if (!de_novo.AsSpan().SequenceEqual(J.Gravado)) Problema("save: ler e gravar de novo muda os bytes");
            if (terminar) return;
            Recargas++;
            s = lido;
        }
    }

    // roda o ciclo numa thread com o vigia; false = travou ou erro
    bool Rodar()
    {
        Exception? erro = null;
        var t = new Thread(() =>
        {
            try { J.Ciclo(AoFim); }
            catch (Exception ex) { erro = ex; }
        }) { IsBackground = true, Name = "motor-soak" };
        t.Start();
        long visto = -1;
        var desde = DateTime.UtcNow;
        while (!t.Join(500))
        {
            long p = Interlocked.Read(ref pulso);
            if (p != visto) { visto = p; desde = DateTime.UtcNow; continue; }
            if ((DateTime.UtcNow - desde).TotalSeconds > Travou)
            {
                Problema($"TRAVOU ha {Travou}s; ultimo gancho: {onde}");
                return false;
            }
        }
        if (erro != null) { Problema($"EXCECAO em {onde}: {erro}"); return false; }
        return true;
    }

    void AoFim(Temporada.Resultado r)
    {
        Pulso("fim de temporada");
        Temporadas++;
        Log.Add($"[{cfg.Nome}] fim da temporada {Temporadas} ({J.M.Ano}): campeao {r.Campeao}, taca {r.VencedorTaca}");
        Verificar(false, "fim de temporada");
        // o K do jogo (o do save, Regras.SobemDescemDoSave) e o do jogo novo; K de cada lado
        int k = cfg.Regras.SobemDescem;
        if (J.M.Regras.SobemDescem != k) Problema($"K = {J.M.Regras.SobemDescem}, quer {k}");
        for (int d = 0; d < 4; d++)
            if (r.Desceram[d].Count != k || r.Subiram[d + 1].Count != k) Problema($"divisao {d + 1}: {r.Desceram[d].Count} desceram, {r.Subiram[d + 1].Count} subiram da de baixo (K = {k})");
        if (Temporadas >= cfg.Temporadas) terminar = true;
    }

    void Ligar()
    {
        var m = J.M;
        m.G.Classificacao = (mm, _) =>
        {
            Pulso("classificacao");
            Jornadas++;
            Verificar(true, "classificacao");
        };
        m.G.Sorteio = _ => Pulso("sorteio");
        J.Mc.Janela = (l, rodar) => { Pulso("leilao"); Leiloes++; return rodar(); };
    }

    // ---------------------------------------------------------------- as decisoes
    Humano Humano() => new()
    {
        Preparar = Preparar,
        Batedor = (_, titulares, sugerido) => { Pulso("batedor"); return titulares.Count > 0 ? titulares[rnd.Next(titulares.Count)] : sugerido; },
        Substituicao = (jg, lado, saiu, motivo) =>
        {
            Pulso("substituicao " + motivo);
            if (motivo == 4 || motivo == 1 || saiu == null) return;
            JogoEmCurso.SubstituicaoForcada(jg.Equipa[lado]!, saiu, 0);
        },
        Lance = (c, v, p) =>
        {
            Pulso("lance");
            if (++lancesSeguidos > 1000) throw new InvalidOperationException("lance humano pedido mais de 1000 vezes seguidas");
            if (rnd.Next(3) == 0) return (0, false);
            int max = Math.Max(0, (int)c.Dinheiro / 1000);
            return (rnd.Next(max + 1), true);
        },
        PedirAumento = (e, p, novo, modo) => { Pulso("pedido de aumento"); return modo % 2 == 1 || rnd.Next(2) == 0; },   // modos 1 e 3: so OK
        Convite = (t, e) => { Pulso("convite"); return rnd.Next(4) == 0; },
        Minuto = _ => { Pulso("minuto"); lancesSeguidos = 0; },
        Desempate = (_, _) => Pulso("desempate"),
        Lance2 = (_, _) => { },
        ComecoJornada = (_, _) => Pulso("comeco da jornada"),
        FimJornada = _ => Pulso("fim da jornada"),
        Patrocinio = p => { Pulso("patrocinio"); Propostas++; return rnd.Next(2) == 0; },
        Penalti = (_, _, _, _) => Pulso("penalti"),
    };

    bool Preparar(JogoEmCurso j, Equipa e, Jogo? jogo)
    {
        Pulso("preparar");
        lancesSeguidos = 0;
        if (terminar) return true;
        if (rnd.NextDouble() < cfg.Recarregar) return true;
        var mc = j.Mc;
        Escalacao.Automatico(j.R, e);
        // Jogador > Vender (TelaLeilao.Vender)
        if (rnd.Next(8) == 0 && e.Jogadores.Count > 16)
        {
            var p = e.Jogadores[rnd.Next(e.Jogadores.Count)];
            if (MercadoJogo.ImpedimentoVenda(e, p) == 0)
            {
                int preco = mc.ValorDoJogador(p) / 1000 * 1000;
                Vendas++;
                Pulso("venda");
                ultimo = $"venda de {p.ID} por {e.ID} ({e.Jogadores.Count})";
                mc.Leiloar(e.ID, p.ID, preco, j.H.Lance);
                p.B0C[6] = 1;
                j.M.Reindexar();
                Escalacao.Automatico(j.R, e);
            }
        }
        // compra direta (TelaPlantel.Comprar)
        if (j.Op.Registrado && rnd.Next(6) == 0)
        {
            var v = j.M.S.Equipas[rnd.Next(j.M.S.Equipas.Count)];
            if (v != e && v.Jogadores.Count > 0)
            {
                int k = rnd.Next(v.Jogadores.Count);
                int preco = mc.PrecoParaComprar(e, v, v.Jogadores[k]);
                if (preco > 0) { ultimo = $"compra de {v.Jogadores[k].ID} de {v.ID} ({v.Jogadores.Count})"; mc.Comprar(e, v, k, preco, true); Compras++; }
            }
        }
        // emprestimo (TelaEmprestimo) e estadio (TelaEstadio)
        int x = rnd.Next(12);
        if (x == 0 && mc.AbrirBanco(e).PodePedir(e)) { mc.PedirEmprestimo(e); Emprestimos++; }
        else if (x == 1 && mc.PodePagar(e)) mc.PagarEmprestimo(e);
        else if (x == 2 && mc.PodeConstruir(e)) mc.ConstruirBancada(e);
        // renovar contrato (TelaNovoSalario)
        if (rnd.Next(10) == 0 && e.Jogadores.Count > 0)
        {
            var p = e.Jogadores[rnd.Next(e.Jogadores.Count)];
            if (MercadoJogo.PodeMudarSalario(p))
            {
                int valor = Math.Clamp((int)(p.Salario * (0.7 + rnd.NextDouble())), 50 * mc.Moeda, 50000 * mc.Moeda);
                var it = new Interacao { PedirAumento = j.H.PedirAumento, Lance = j.H.Lance, Convite = j.H.Convite };
                ultimo = $"salario de {p.ID} em {e.ID} ({e.Jogadores.Count})";
                mc.ProporSalario(e, p, valor, it);
                j.M.Reindexar();
                Escalacao.Automatico(j.R, e);
            }
        }
        Verificar(false, "preparar");
        return false;
    }

    // ---------------------------------------------------------------- invariantes
    // cada transferencia nova (leilao ou compra): o preco pago e o valor de mercado do jogador
    // (MercadoJogo.ValorDeMercado), para ver se o valor bate com o que os times pagam
    public readonly List<(int Valor, int Preco, int Tipo, int Forca)> Precos = new();
    readonly HashSet<Transferencia> _transfVistas = new(ReferenceEqualityComparer.Instance);

    void AnotarPrecos(Jogo98 s)
    {
        foreach (var t in s.Transferencias)
        {
            if (!_transfVistas.Add(t) || t.D0C == 0) continue;
            var p = s.Equipas.SelectMany(e => e.Jogadores).FirstOrDefault(x => x.ID == t.W06);
            if (p == null) continue;
            Precos.Add((J.Mc.ValorDeMercado(p), (int)t.D0C, t.B04, p.Forca));
        }
    }

    void Verificar(bool depoisDaJornada, string quando)
    {
        var m = J.M;
        var s = m.S;
        AnotarPrecos(s);
        int n = m.Regras.N;
        string Q(string t) => $"{quando}: {t}";
        if (s.Divisoes.Count != 5) { Problema(Q($"{s.Divisoes.Count} divisoes")); return; }
        var vistas = new HashSet<ushort>();
        for (int d = 0; d < 5; d++)
            foreach (var id in s.Divisoes[d].Equipas)
            {
                if (!vistas.Add(id)) Problema(Q($"equipa {id} em duas divisoes"));
                if (m.Equipa(id) == null) Problema(Q($"equipa {id} da divisao {d + 1} nao existe"));
            }
        for (int d = 0; d < 4; d++)
            if (s.Divisoes[d].Equipas.Count != n) Problema(Q($"divisao {d + 1} com {s.Divisoes[d].Equipas.Count} equipas (N = {n})"));
        foreach (var e in s.Equipas)
            if (!vistas.Contains(e.ID)) Problema(Q($"equipa {e.ID} fora das divisoes"));
        var treinadores = new HashSet<ushort>();
        var jogadores = new HashSet<ushort>();
        foreach (var e in s.Equipas)
        {
            if (m.TreinadorDa(e) == null) Problema(Q($"equipa {e.ID} sem treinador ({e.Treinador})"));
            else if (!treinadores.Add(e.Treinador)) Problema(Q($"treinador {e.Treinador} em duas equipas"));
            int c = e.Jogadores.Count;
            if (c < 11 || c > m.Regras.Limite + 8) Problema(Q($"equipa {e.ID} com {c} jogadores (B83[5]={e.B83[5]}, humano={m.TreinadorDa(e)?.Humano}, divisao {m.DivisaoDa(e) + 1}, ultimo: {ultimo})"));
            foreach (var p in e.Jogadores)
            {
                if (!jogadores.Add(p.ID)) Problema(Q($"jogador {p.ID} em duas equipas"));
                if (p.Posicao > 3) Problema(Q($"jogador {p.ID} com posicao {p.Posicao}"));
            }
        }
        int humanos = s.Treinadores.Count(t => t.Humano != 0);
        if (humanos != humanosIniciais) Problema(Q($"{humanos} treinadores humanos (eram {humanosIniciais})"));
        // (um humano despedido fica sem equipa ate aceitar um convite, como no original)
        if (!depoisDaJornada) return;
        for (int d = 0; d < 4; d++)
        {
            int gp = 0, gc = 0, v = 0, der = 0;
            foreach (var id in s.Divisoes[d].Equipas)
            {
                var e = m.Equipa(id);
                if (e == null) continue;
                int vv = EqW(e, EqVitorias), ee = EqW(e, EqEmpates), dd = EqW(e, EqDerrotas);
                if (vv + ee + dd != m.Jornada) Problema(Q($"equipa {id} (div {d + 1}) com {vv + ee + dd} jogos na jornada {m.Jornada}"));
                if (EqW(e, EqPontos) != 3 * vv + ee) Problema(Q($"equipa {id} com {EqW(e, EqPontos)} pontos, V{vv} E{ee}"));
                gp += EqW(e, EqGolosPro); gc += EqW(e, EqGolosContra); v += vv; der += dd;
            }
            if (gp != gc) Problema(Q($"divisao {d + 1}: golos pro {gp} != contra {gc}"));
            if (v != der) Problema(Q($"divisao {d + 1}: vitorias {v} != derrotas {der}"));
        }
    }
}
