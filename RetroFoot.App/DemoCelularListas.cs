using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using RetroFoot.Motor.Save;
using RetroFoot.Motor.Temporada;
using Chicotada = RetroFoot.Motor.Rodada.Chicotada;

namespace RetroFoot.App;

// As entradas do grupo Listas na demo das janelas (DemoCelular.cs): as listas que a demo do
// computador nao tinha (titulos, historico, contratacoes, chicotadas, subidas, a lista generica)
// e a classificacao (no lugar da tela, como depois da jornada e pelo menu). As que ja existiam
// (calendario, artilheiros, sempre, vencedores, transferencias, historia, gestao, ranking,
// perfil, fim, convite) abrem a versao do celular sozinhas.
static partial class DemoCelular
{
    static partial void Listas(List<(string Nome, Action Abrir)> l, Dialogos d, Sessao s, Equipa humano, Action<Control> mostrar)
    {
        l.Add(("titulos", () => TelaTitulos.Abrir(d, s)));
        l.Add(("historico", () => TelaListas.HistoricoEquipa(d, s, humano)));
        l.Add(("contratacoes", () =>
        {
            // (o save de teste nao tem compras a chegar: duas de mentira, so para ver)
            var outros = s.S.Equipas.Where(e => e != humano).Take(2).ToList();
            var ps = outros.Select(e => e.Jogadores[0]).ToList();
            var antes = ps.Select(p => p.W36).ToList();
            foreach (var p in ps) p.W36 = humano.ID;
            TelaListas.Contratacoes(d, s, humano, () => { for (int i = 0; i < ps.Count; i++) ps[i].W36 = antes[i]; });
        }));
        l.Add(("chicotadas", () =>
        {
            var eqs = s.S.Equipas.Take(3).ToList();
            var ts = eqs.Select(e => s.TreinadorDa(e)).ToList();
            var ch = new List<Chicotada>
            {
                new(ts[0]?.ID ?? 0, eqs[0].ID, 1),
                new(ts[1]?.ID ?? 0, 0, 0),
                new(ts[2]?.ID ?? 0, eqs[2].ID, 0),
            };
            TelaListas.Chicotadas(d, s, ch, () => Console.WriteLine("RETROFOOT-DEMO chicotadas fechou"));
        }));
        l.Add(("subidas", () =>
        {
            var r = new Resultado();
            r.Subiram[0].Add(s.S.Divisoes[1].Equipas[0]);
            r.Subiram[0].Add(s.S.Divisoes[1].Equipas[1]);
            r.Desceram[0].Add(s.S.Divisoes[0].Equipas[^1]);
            r.Desceram[1].Add(s.S.Divisoes[1].Equipas[^1]);
            Control? c = null;
            c = TelaFimDeTemporada.SubidasEDescidas(s, r, () => d.Fechar(c!));
            mostrar(c);
        }));
        l.Add(("arbitros", () => TelaOpcoes.Arbitros(d, s.S)));
        // a classificacao no lugar da tela: pelo menu (sem relogio) e depois da jornada (10 s)
        void Classificacao(bool relogio)
        {
            var c = new TelaClassificacao(s, relogio ? Math.Max((int)s.J.M.Jornada, 1) : 0, relogio);
            c.Fechou += () => Console.WriteLine("RETROFOOT-DEMO classificacao fechou");
            s.Mostrar(c);
        }
        l.Add(("classificacao", () => Classificacao(false)));
        l.Add(("classificacao-relogio", () => Classificacao(true)));
    }
}
