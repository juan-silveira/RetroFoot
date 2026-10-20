using System;
using System.Collections.Generic;
using Avalonia.Controls;
using RetroFoot.Motor.Save;

namespace RetroFoot.App;

// As entradas da demo das janelas (RETROFOOT_TELA=dialogos, DemoDialogos.cs) para as janelas
// do celular, por grupo: cada grupo poe as suas no proprio arquivo (DemoCelular<Grupo>.cs),
// com o nome que RETROFOOT_DIALOGO abre logo ao comecar.
//   l: a lista de (nome, abrir); d: os dialogos; s: a sessao de teste (sem motor);
//   humano: a equipa do humano (escalada); mostrar: abre um controle como janela
static partial class DemoCelular
{
    public static void Entradas(List<(string Nome, Action Abrir)> l, Dialogos d, Sessao s, Equipa humano, Action<Control> mostrar)
    {
        Partida(l, d, s, humano, mostrar);
        Listas(l, d, s, humano, mostrar);
        Mercado(l, d, s, humano, mostrar);
        Clube(l, d, s, humano, mostrar);
        Configuracoes(l, d, s, humano, mostrar);
    }

    static partial void Partida(List<(string Nome, Action Abrir)> l, Dialogos d, Sessao s, Equipa humano, Action<Control> mostrar);
    static partial void Listas(List<(string Nome, Action Abrir)> l, Dialogos d, Sessao s, Equipa humano, Action<Control> mostrar);
    static partial void Mercado(List<(string Nome, Action Abrir)> l, Dialogos d, Sessao s, Equipa humano, Action<Control> mostrar);
    static partial void Clube(List<(string Nome, Action Abrir)> l, Dialogos d, Sessao s, Equipa humano, Action<Control> mostrar);
    static partial void Configuracoes(List<(string Nome, Action Abrir)> l, Dialogos d, Sessao s, Equipa humano, Action<Control> mostrar);
}
