using System;
using System.Collections.Generic;
using Avalonia.Controls;
using RetroFoot.Motor.Save;

using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// As janelas do clube no celular na demo (RETROFOOT_TELA=dialogos): estadio, estadio-cheio (as
// 24 arquibancadas: Construir desligado), estadio-sem-dinheiro, emprestimo, balanco e mensagem
// (Ui.Caixa). O sobre, extras, perguntar e texto da DemoDialogos ja abrem as folhas do celular.
static partial class DemoCelular
{
    static partial void Clube(List<(string Nome, Action Abrir)> l, Dialogos d, Sessao s, Equipa humano, Action<Control> mostrar)
    {
        l.Add(("estadio", () => TelaEstadio.Abrir(s, humano, true)));
        l.Add(("estadio-cheio", () =>
        {
            byte antes = humano.Estadio;
            humano.Estadio = 24;
            TelaEstadio.Abrir(s, humano, true, () => humano.Estadio = antes);
        }));
        l.Add(("estadio-sem-dinheiro", () =>
        {
            uint antes = humano.Dinheiro;
            humano.Dinheiro = 1000;
            TelaEstadio.Abrir(s, humano, false, () => humano.Dinheiro = antes);
        }));
        l.Add(("emprestimo", () => TelaEmprestimo.Abrir(s, humano)));
        l.Add(("balanco", () => TelaBalanco.Abrir(s, humano)));
        l.Add(("mensagem", () =>
        {
            var g = new Grid();
            var c = Ui.Caixa(TelaAcerca.Nome, T("Jogo salvo.") + "\n\nDEMO.E98", () => g.Children.RemoveAt(g.Children.Count - 1));
            // a caixa por cima da pagina da demo (quem usa Ui.Caixa a poe num painel seu)
            g.Children.Add(CUi.Pagina(CUi.BarraTopo("Demo", "Ui.Caixa"), CUi.Txt("…")));
            g.Children.Add(c);
            s.Mostrar(g);
        }));
    }
}
