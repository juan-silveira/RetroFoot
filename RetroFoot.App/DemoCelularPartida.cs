using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Rodada;
using Mem = RetroFoot.Motor.Partida.Memoria;
using RetroFoot.Motor.Save;
using SJogador = RetroFoot.Motor.Save.Jogador;

namespace RetroFoot.App;

// As janelas da partida no celular na demo (RETROFOOT_TELA=dialogos RETROFOOT_DIALOGO=nome):
// partida-intervalo, partida-intervalo-amarelos, partida-goleiro, partida-emjogo (os detalhes
// do jogo do humano), partida-detalhes (os do adversario), partida-penalti, partida-penalti-cpu,
// partida-penaltis, partida-sorteio, partida-patrocinio, partida-patrocinio-pontual. O jogo e o
// da demo da partida (LigacaoPartida.JogoDemo: o humano e o adversario escalados, uns lances).
static partial class DemoCelular
{
    static partial void Partida(List<(string Nome, Action Abrir)> l, Dialogos d, Sessao s, Equipa humano, Action<Control> mostrar)
    {
        void Abrir(Control c) => mostrar(c);
        void Fecha(Control c) => s.FecharDialogo(c);
        Control Ligar(Control c)
        {
            switch (c)
            {
                case TelaSubstituicao t: t.Fechou += () => { Console.WriteLine("RETROFOOT-DEMO fechou"); Fecha(t); }; break;
                case TelaPenalti t: t.Terminou += b => { Console.WriteLine("RETROFOOT-DEMO penalti " + P98.NomeJogador(b)); Fecha(t); }; break;
                case TelaPenaltis t: t.Fechou += () => Fecha(t); break;
                case TelaSorteio t: t.Fechou += () => Fecha(t); break;
                case TelaDetalhesModerna t: t.Fechou += () => Fecha(t); break;
            }
            return c;
        }
        TelaSubstituicao Intervalo(bool amarelos)
        {
            var (jg, lado) = LigacaoPartida.JogoDemo(s);
            Jornada.OrdenarPorPosicaoNome(jg.Equipa[lado]!);
            return new TelaSubstituicao(jg, lado, 4, s.J.Op, amarelos);
        }
        l.Add(("partida-intervalo", () => Abrir(Ligar(Intervalo(s.J.M.Regras.CartoesAmarelos)))));
        l.Add(("partida-intervalo-amarelos", () => Abrir(Ligar(Intervalo(true)))));
        l.Add(("partida-goleiro", () => Abrir(Ligar(LigacaoPartida.Dialogo(s, "goleiro")))));
        l.Add(("partida-emjogo", () =>
        {
            // o clique no jogo do humano durante a jornada (motivo 1): no Moderno a linha do
            // tempo com a Escalacao; no Retro a janela de substituicao
            var (jg, lado) = LigacaoPartida.JogoDemo(s);
            Jornada.OrdenarPorPosicaoNome(jg.Equipa[lado]!);
            Control? c = null;
            c = Visual.Moderno
                ? LigacaoPartida.DetalhesModernos(s, jg, lado, 60, 0, false, () => Fecha(c!))
                : Ligar(new TelaSubstituicao(jg, lado, 1, s.J.Op, s.J.M.Regras.CartoesAmarelos,
                    LigacaoPartida.SubstituicoesDuranteJogo, LigacaoPartida.VerEscalacaoDosOutros));
            Abrir(c);
        }));
        l.Add(("partida-detalhes", () =>
        {
            Control? c = null;
            c = LigacaoPartida.Dialogo(s, "detalhes");
            if (c is TelaSubstituicao) Ligar(c);
            else if (c is TelaDetalhesModerna t) t.Fechou += () => Fecha(t);
            Abrir(c);
        }));
        l.Add(("partida-penalti", () => Abrir(Ligar(LigacaoPartida.Dialogo(s, "penalti")))));
        l.Add(("partida-penalti-cpu", () =>
        {
            // o humano defende: o batedor do computador e o resultado ja sorteado
            var (jg, lado) = LigacaoPartida.JogoDemo(s);
            var cpu = jg.Equipa[1 - lado]!;
            var bat = cpu.Jogadores.Where(p => Mem.Escalacao(p) == Mem.EscTitular).OrderByDescending(p => p.Forca).First();
            Abrir(Ligar(new TelaPenalti(cpu, "", new List<SJogador>(), bat, _ => ResultadoPenalti.Defesa)));
        }));
        l.Add(("partida-penaltis", () => Abrir(Ligar(LigacaoPartida.Dialogo(s, "penaltis")))));
        l.Add(("partida-sorteio", () => Abrir(Ligar(LigacaoPartida.Dialogo(s, "sorteio")))));
        void Patrocinio(byte tipo)
        {
            Control? c = null;
            var p = new RetroFoot.Motor.Patrocinio.Proposta { Valor = 850000, Quantidade = (byte)(tipo == RetroFoot.Motor.Patrocinio.Proposta.Pontual ? 3 : 2), Tipo = tipo, Multa = 300000 };
            p.NomeBruto[0] = 9; System.Text.Encoding.Latin1.GetBytes("SUPERBOCK").CopyTo(p.NomeBruto, 1);
            c = new TelaPatrocinio(p, s.J.Op.Marcas, RetroFoot.Motor.Mercado.Moeda.DoSave(s.S), v => { Console.WriteLine("RETROFOOT-DEMO patrocinio " + v); Fecha(c!); });
            Abrir(c);
        }
        l.Add(("partida-patrocinio", () => Patrocinio(RetroFoot.Motor.Patrocinio.Proposta.Master)));
        l.Add(("partida-patrocinio-pontual", () => Patrocinio(RetroFoot.Motor.Patrocinio.Proposta.Pontual)));
    }
}
