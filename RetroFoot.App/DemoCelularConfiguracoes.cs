using System;
using System.Collections.Generic;
using Avalonia.Controls;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Save;

namespace RetroFoot.App;

// As entradas do grupo Configuracoes e Opcoes na demo das janelas do celular (DemoCelular.cs):
//   config-jogo    as Configuracoes com o jogo de teste em curso (RetroFoot > Configuracoes...)
//   opcoes-vazio   as Opcoes sem jogo (sem o exemplo da equipa e sem as listas do Controle)
static partial class DemoCelular
{
    static partial void Configuracoes(List<(string Nome, Action Abrir)> l, Dialogos d, Sessao s, Equipa humano, Action<Control> mostrar)
    {
        l.Add(("config-jogo", () =>
        {
            // (Salvar grava o config.json de verdade, como a RETROFOOT_TELA=configuracoes)
            var t = new TelaConfiguracoes(emCurso: s);
            t.Fechou += salvou => { Console.WriteLine("RETROFOOT-CONFIGURACOES " + (salvou ? "salvou" : "cancelou")); d.Fechar(t); };
            mostrar(t);
        }));
        l.Add(("opcoes-vazio", () => TelaOpcoes.Abrir(d, OpcoesDoJogo.De(Opcoes.Padrao), null, null, _ => { })));
    }
}
