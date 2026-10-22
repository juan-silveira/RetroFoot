using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;

namespace RetroFoot.Motor.Online;

// O que um tecnico num aparelho remoto pede ao host (docs/multiplayer-online.md). O cliente
// prepara o time numa copia; o que muda so o proprio time e so o estado final importa vai no
// Preparo (escalacao, ingresso, observados, opcao de leilao), aplicado no gancho Preparar da
// equipa; o que mexe no dinheiro ou noutras equipas vai como comando e o host executa na hora,
// com as mesmas condicoes das telas (o cliente nunca muda o estado oficial).
[JsonPolymorphic(TypeDiscriminatorPropertyName = "tipo")]
[JsonDerivedType(typeof(PedirEmprestimo), "pedirEmprestimo")]
[JsonDerivedType(typeof(PagarEmprestimo), "pagarEmprestimo")]
[JsonDerivedType(typeof(ConstruirBancada), "construirBancada")]
[JsonDerivedType(typeof(Comprar), "comprar")]
[JsonDerivedType(typeof(Vender), "vender")]
[JsonDerivedType(typeof(Renovar), "renovar")]
[JsonDerivedType(typeof(Preparo), "preparo")]
[JsonDerivedType(typeof(Parar), "parar")]
[JsonDerivedType(typeof(Trocar), "trocar")]
[JsonDerivedType(typeof(Retomar), "retomar")]
public abstract record Comando
{
    // (o serializador gerado na compilacao: no Android o ILLink apaga os nomes dos parametros
    // dos construtores e o serializador por reflexao nao le os records)
    public string Json() => JsonSerializer.Serialize(this, JsonComandos.Default.Comando);
    public static Comando Ler(string json) => JsonSerializer.Deserialize(json, JsonComandos.Default.Comando) ?? throw new JsonException("comando vazio");
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Comando))]
internal partial class JsonComandos : JsonSerializerContext;

public sealed record PedirEmprestimo : Comando;
public sealed record PagarEmprestimo : Comando;
public sealed record ConstruirBancada : Comando;
// a compra direta (TPlantelDlg.buyBtnClick) do jogador da equipa vendedora pelo preco que o
// cliente viu: se o preco mudou, nao compra
public sealed record Comprar(ushort Vendedor, ushort Jogador, int Preco) : Comando;
// o leilao de um jogador do time (sellBtnClick) com o preco base
public sealed record Vender(ushort Jogador, int Preco) : Comando;
// a proposta de salario (newSalaryBtnClick)
public sealed record Renovar(ushort Jogador, int Proposta) : Comando;
// o fim da preparacao: a escalacao (id e estado de cada jogador, na ordem da lista), o
// ingresso (Ingressos.Razao), os observados e a opcao de leilao do tecnico (+0x1A[3])
public sealed record Preparo(List<Escalado> Escalacao, int Razao, List<ushort> Observados, byte OpcaoLeilao) : Comando;
public sealed record Escalado(ushort Id, ushort Estado);
// durante a rodada ao vivo (fase 3): parar o jogo para substituir (para todos), uma troca e
// seguir; o host atende no minuto seguinte
public sealed record Parar : Comando;
public sealed record Trocar(ushort Saiu, ushort Entra) : Comando;
public sealed record Retomar : Comando;

public sealed record Resposta(bool Ok, string Motivo = "")
{
    public static readonly Resposta Feito = new(true);
    public static Resposta Recusado(string motivo) => new(false, motivo);
}

// Executa os comandos de uma equipa no jogo do host (com o motor parado na preparacao)
public sealed class ExecutorComandos(JogoEmCurso j)
{
    MercadoJogo Mc => j.Mc;
    Jogo98 S => j.M.S;

    // a pergunta de aumento e os lances do dono da equipa (o aparelho dele); null = o padrao
    public Interacao? InteracaoDe(ushort equipa) => Interacoes.GetValueOrDefault(equipa);
    public readonly Dictionary<ushort, Interacao> Interacoes = new();

    public Resposta Executar(ushort equipa, Comando c)
    {
        var e = j.M.Equipa(equipa);
        if (e == null) return Resposta.Recusado("equipa");
        if (!MercadoJogo.HumanoAtivo(j.M.TreinadorDa(e))) return Resposta.Recusado("sem tecnico humano");
        switch (c)
        {
            case PedirEmprestimo:
                if (!j.Op.Registrado) return Resposta.Recusado("emprestimo");
                if (!Mc.AbrirBanco(e).PodePedir(e)) return Resposta.Recusado("limite do banco");
                Mc.PedirEmprestimo(e);
                return Resposta.Feito;
            case PagarEmprestimo:
                if (!Mc.PodePagar(e)) return Resposta.Recusado("sem divida ou sem dinheiro");
                Mc.PagarEmprestimo(e);
                return Resposta.Feito;
            case ConstruirBancada:
                if (!Mc.PodeConstruir(e)) return Resposta.Recusado("sem dinheiro ou estadio no limite");
                Mc.ConstruirBancada(e);
                return Resposta.Feito;
            case Comprar k:
            {
                if (!j.Op.Registrado) return Resposta.Recusado("compra");
                var v = j.M.Equipa(k.Vendedor);
                int idx = v?.Jogadores.FindIndex(p => p.ID == k.Jogador) ?? -1;
                if (v == null || idx < 0) return Resposta.Recusado("o jogador ja saiu");
                // (um jogador ja comprado fica na lista do vendedor com +0x36 = comprador)
                if (v.Jogadores[idx].W36 != 0) return Resposta.Recusado("o jogador ja foi comprado");
                int preco = Mc.PrecoParaComprar(e, v, v.Jogadores[idx]);
                if (preco <= 0) return Resposta.Recusado("nao pode comprar");
                if (preco != k.Preco) return Resposta.Recusado("o preco mudou");
                Mc.Comprar(e, v, idx, preco, true);
                return Resposta.Feito;
            }
            case Vender k:
            {
                var p = e.Jogadores.Find(x => x.ID == k.Jogador);
                if (p == null) return Resposta.Recusado("o jogador nao e do time");
                if (MercadoJogo.ImpedimentoVenda(e, p) != 0) return Resposta.Recusado("nao pode vender");
                if (k.Preco < 0) return Resposta.Recusado("preco");
                // humano nao compra de humano (PodeComprar): os lances sao so da CPU
                bool vendido = Mc.Leiloar(e.ID, p.ID, k.Preco, null);
                p.B0C[6] = 1;   // 4:463d: so volta a tentar no proximo jogo
                j.M.Reindexar();
                return new Resposta(true, vendido ? "vendido" : "nao vendido");
            }
            case Renovar k:
            {
                var p = e.Jogadores.Find(x => x.ID == k.Jogador);
                if (p == null || !MercadoJogo.PodeMudarSalario(p)) return Resposta.Recusado("nao pode renovar");
                if (k.Proposta <= 0) return Resposta.Recusado("proposta");
                bool ficou = Mc.ProporSalario(e, p, k.Proposta, InteracaoDe(e.ID));
                j.M.Reindexar();
                if (ficou) return new Resposta(true, "aceito");
                bool vendido = MercadoJogo.EquipaDoJogador(S, p.ID).E != e;
                return new Resposta(true, vendido ? "vendido" : "nao vendido");
            }
            case Preparo k:
                return Preparar(e, k);
        }
        return Resposta.Recusado("comando");
    }

    // o Preparo no gancho da equipa: a escalacao na ordem enviada (quem nao veio, no fim, fora;
    // quem ja nao e do time, ignorado), o ingresso, os observados e a opcao de leilao. Uma
    // escalacao que nao fecha (lesionado escalado, goleiros, reservas) vira a automatica
    Resposta Preparar(Equipa e, Preparo k)
    {
        var porId = e.Jogadores.ToDictionary(p => p.ID);
        var nova = new List<Jogador>();
        foreach (var x in k.Escalacao)
            if (porId.Remove(x.Id, out var p))
            {
                nova.Add(p);
                Memoria.MudarEscalacao(p, x.Estado is Memoria.EscTitular or Memoria.EscReserva ? x.Estado : Memoria.EscFora);
            }
        foreach (var p in e.Jogadores)
            if (porId.ContainsKey(p.ID)) { Memoria.MudarEscalacao(p, Memoria.EscFora); nova.Add(p); }
        e.Jogadores.Clear();
        e.Jogadores.AddRange(nova);
        bool automatica = !EscalacaoValida(e);
        if (automatica) Escalacao.Automatico(j.R, e);
        else Memoria.MudarSubstituicoes(e, (short)Math.Min(e.Jogadores.Count(p => Memoria.Escalacao(p) == Memoria.EscReserva), Escalacao.MaxSubstituicoes));

        if (k.Razao is >= Ingressos.RazaoMinima and <= Ingressos.RazaoMaxima) Ingressos.GravarRazao(e, k.Razao);
        var t = j.M.TreinadorDa(e);
        if (t != null)
        {
            MercadoJogo.LimparObservados(S, t.ID);
            foreach (var id in k.Observados.Take(MercadoJogo.LimiteObservados)) MercadoJogo.ObservarJogador(S, t.ID, id);
            if (k.OpcaoLeilao <= 2) t.B1A[3] = k.OpcaoLeilao;
        }
        return automatica ? new Resposta(true, "escalacao automatica") : Resposta.Feito;
    }

    // uma troca durante o jogo (TelaSubstituicao.Trocar, seg02:2a4f): o titular sai, o reserva
    // entra (+1 jogo), a equipa gasta uma substituicao e a forca dos setores e refeita; goleiro so
    // por goleiro, a nao ser com o goleiro expulso (motivo 3)
    public static Resposta Substituir(Equipa e, ushort saiu, ushort entra, int motivo = 1)
    {
        var s = e.Jogadores.Find(p => p.ID == saiu);
        var n = e.Jogadores.Find(p => p.ID == entra);
        if (s == null || n == null) return Resposta.Recusado("jogador");
        if (Memoria.Substituicoes(e) <= 0) return Resposta.Recusado("sem substituicoes");
        if (Memoria.Escalacao(s) != Memoria.EscTitular || Memoria.Escalacao(n) != Memoria.EscReserva) return Resposta.Recusado("troca invalida");
        if (!((s.Posicao == 0) == (n.Posicao == 0) || (motivo == 3 && n.Posicao == 0))) return Resposta.Recusado("goleiro so por goleiro");
        Memoria.MudarEscalacao(s, Memoria.EscFora);
        Memoria.MudarEscalacao(n, Memoria.EscTitular);
        Memoria.SomaHistorial(n, Memoria.HistJogos, 1);
        Memoria.MudarSubstituicoes(e, (short)(Memoria.Substituicoes(e) - 1));
        Sorteio.ForcaDosSetores(e);
        return Resposta.Feito;
    }

    // a mesma conferencia do Jogar da tela da equipa (TelaEquipa.ConferirEscalacao)
    public static bool EscalacaoValida(Equipa e)
    {
        int dispG = 0, dispL = 0, titG = 0, titL = 0, res = 0;
        foreach (var p in e.Jogadores)
        {
            bool disp = p.JogosLesao == 0 && p.Suspensao == 0;
            if (disp) { if (p.Posicao == 0) dispG++; else dispL++; }
            switch (Memoria.Escalacao(p))
            {
                case Memoria.EscTitular:
                    if (!disp) return false;
                    if (p.Posicao == 0) titG++; else titL++;
                    break;
                case Memoria.EscReserva:
                    if (!disp) return false;
                    res++;
                    break;
            }
        }
        int precisaG = dispG == 0 ? 0 : 1;
        return titG == precisaG && titL == Math.Min(dispL, 10) && res <= Escalacao.MaxReservas;
    }
}
