using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Mem = RetroFoot.Motor.Partida.Memoria;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// As janelas da partida no celular (Celular.cs), nas folhas do kit (CelularUi.cs): as mesmas
// regras das janelas do computador (TelaSubstituicaoForcada...), que continuam a decidir; estas
// so mostram e recolhem o toque.

// ---------------------------------------------------------------- a lesao
sealed class SubstituicaoForcadaCelular : UserControl
{
    public event Action? Fechou;
    bool _fechou;

    public SubstituicaoForcadaCelular(SEquipa e, SJogador saiu)
    {
        var res = e.Jogadores.Where(p => Mem.Escalacao(p) == Mem.EscReserva).ToList();
        int goleiros = res.Count(p => p.Posicao == 0);
        bool algum = res.Exists(p => TelaSubstituicaoForcada.Elegivel(saiu, p, goleiros));
        int sel = -1;
        var ok = new CUi.Botao(T("OK"), true, () =>
        {
            if (_fechou) return;
            _fechou = true;
            TelaSubstituicaoForcada.Confirmar(e, saiu, sel >= 0 && TelaSubstituicaoForcada.Elegivel(saiu, res[sel], goleiros) ? res[sel] : null);
            Fechou?.Invoke();
        }) { Habilitado = !algum };
        var corpo = new StackPanel { Spacing = 12 };
        corpo.Children.Add(CUi.Txt(T("{0} ({1}) lesionou-se e tem de ser substituído.\rEscolha o jogador a entrar.",
            P98.NomeJogador(saiu), P98.Posicao(saiu.Posicao)).Replace('\r', '\n'), 15, null, FontWeight.Normal, true));
        if (res.Count == 0) corpo.Children.Add(CUi.Txt(T("Sem reservas."), 14, CUi.Fraco));
        else
        {
            corpo.Children.Add(CUi.SecaoFolha(T("Reservas")));
            var (lista, _) = CUi.ListaEscolha(res.Select(p => CUi.LinhaJogador(p)).ToList(), i =>
            {
                sel = i;
                ok.Habilitado = TelaSubstituicaoForcada.Elegivel(saiu, res[i], goleiros) || !algum;
            }, i => TelaSubstituicaoForcada.Elegivel(saiu, res[i], goleiros));
            corpo.Children.Add(lista);
        }
        ok.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        Content = new CUi.Folha(T("Lesão") + " · " + P98.NomeEquipa(e), corpo, ok, icone: CUi.Escudo(e, 32));
    }
}
