using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Save;
using Mem = RetroFoot.Motor.Partida.Memoria;

namespace RetroFoot.App;

// As janelas do mercado na demo (RETROFOOT_TELA=dialogos, RETROFOOT_DIALOGO=<nome>):
//   mercado-leilao        um leilao da CPU em que o humano licita (o painel do lance)
//   mercado-leilao-fim    um leilao da CPU sem o humano (a espera e o resultado)
//   mercado-vender        a pagina Vender do 1o jogador vendavel (e o leilao depois)
//   mercado-impedimento   Vender num jogador que nao pode ser vendido (o aviso)
//   mercado-salario       a renovacao de contrato
//   mercado-pedido0..3    o pedido de aumento em cada modo
//   mercado-procura       a Lista de Jogadores
//   mercado-plantel       o plantel de outra equipa (com a compra)
//   mercado-observados    os jogadores sob observacao (3 postos se a lista estiver vazia)
//   mercado-observados-cheia  a lista com mais que o limite (o aviso, o Fechar desligado)
static partial class DemoCelular
{
    static partial void Mercado(List<(string Nome, Action Abrir)> l, Dialogos d, Sessao s, Equipa humano, Action<Control> mostrar)
    {
        var m = s.J.M;
        void Msg(string t) => Console.WriteLine("RETROFOOT-DEMO " + t);
        VendavelMercado(humano, out var vendavel);
        void Leilao(bool humanoLicita)
        {
            var cpu = m.S.Equipas.First(x => x != humano && !Mem.Humano(m.TreinadorDa(x)));
            var pj = cpu.Jogadores.FirstOrDefault(x => MercadoJogo.ImpedimentoVenda(cpu, x) == 0) ?? cpu.Jogadores[0];
            // +0x1D do treinador: 0 pergunta sempre; 2 nunca licita
            if (m.TreinadorDa(humano) is { } t) t.B1A[3] = (byte)(humanoLicita ? 0 : 2);
            Task.Run(() =>
            {
                bool r = TelaLeilao.Executar(s, cpu.ID, pj.ID, 0, lance => s.Mercado.Leiloar(cpu.ID, pj.ID, 0, lance));
                m.Reindexar();
                Msg("vendido " + r);
            });
        }
        l.Add(("mercado-leilao", () => Leilao(true)));
        l.Add(("mercado-leilao-fim", () => Leilao(false)));
        l.Add(("mercado-vender", () => TelaLeilao.Vender(s, humano, vendavel, Msg, r => Msg("venda " + r))));
        l.Add(("mercado-impedimento", () =>
        {
            var pj = humano.Jogadores.FirstOrDefault(x => MercadoJogo.ImpedimentoVenda(humano, x) != 0);
            if (pj == null) { pj = humano.Jogadores[0]; pj.B14[2] = 1; }
            TelaLeilao.Vender(s, humano, pj, null, r => Msg("venda " + r));
        }));
        l.Add(("mercado-salario", () =>
            TelaNovoSalario.Abrir(s, humano, humano.Jogadores.FirstOrDefault(MercadoJogo.PodeMudarSalario) ?? humano.Jogadores[0], r => Msg("salario " + r))));
        for (int modo = 0; modo < 4; modo++)
        {
            int md = modo;
            l.Add(("mercado-pedido" + md, () => Task.Run(() =>
            {
                bool r = TelaPedidoSalario.Perguntar(s, humano, vendavel, s.Mercado.SalarioPedido(humano, vendavel), md);
                Msg("pedido " + r);
            })));
        }
        l.Add(("mercado-procura", () => TelaProcura.Abrir(s, humano)));
        l.Add(("mercado-plantel", () =>
        {
            var outra = s.S.Equipas.FirstOrDefault(x => x != humano && s.TreinadorDa(x) is not { Humano: not 0 }) ?? humano;
            TelaPlantel.Abrir(s, outra, humano);
        }));
        l.Add(("mercado-observados", () =>
        {
            if (MercadoJogo.Observados(s.S, humano.Treinador).Count == 0)
                foreach (var p in s.S.Equipas.Where(x => x != humano).SelectMany(x => x.Jogadores).OrderByDescending(p => p.Forca).Take(3))
                    MercadoJogo.ObservarJogador(s.S, humano.Treinador, p.ID);
            TelaObservados.Abrir(s, humano);
        }));
        l.Add(("mercado-observados-cheia", () =>
        {
            // a lista com um a mais que o limite (o Observado do plantel com 15 ja la)
            foreach (var p in s.S.Equipas.Where(x => x != humano).SelectMany(x => x.Jogadores).OrderByDescending(p => p.Forca))
            {
                if (MercadoJogo.Observados(s.S, humano.Treinador).Count > MercadoJogo.LimiteObservados) break;
                if (!MercadoJogo.Observado(s.S, humano.Treinador, p.ID)) MercadoJogo.ObservarJogador(s.S, humano.Treinador, p.ID);
            }
            TelaObservados.Abrir(s, humano);
        }));
    }

    static void VendavelMercado(Equipa e, out Jogador p) =>
        p = e.Jogadores.FirstOrDefault(x => MercadoJogo.ImpedimentoVenda(e, x) == 0) ?? e.Jogadores[0];
}
