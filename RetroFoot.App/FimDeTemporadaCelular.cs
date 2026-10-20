using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Save;
using RetroFoot.Motor.Temporada;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// O fim de temporada no celular (Celular.cs), nas folhas do kit (CelularUi.cs): a TEndOfSeasonDlg
// na tela toda, com um cartao por vencedor (o escudo, o nome completo, o numero de titulos da
// equipa e do treinador e o premio) e o do artilheiro (todos os empatados no maximo de golos); o
// relogio do original fecha no 11o tique (5:3027), com a contagem embaixo. E as subidas e
// descidas (o acrescimo) numa folha por divisao. Os dados sao os de TelaFimDeTemporada.
static class FimDeTemporadaCelular
{
    internal static Control Janela(Sessao s, Resultado r, Action fechar)
    {
        var moeda = Moeda.DoSave(s.S);
        string Ext(long v) => FormatoDinheiro.Valor(moeda, v);

        Control Vencedor(Equipa? e, bool copa, long premio)
        {
            var p = new StackPanel { Spacing = 4 };
            if (e != null)
            {
                var nome = CUi.Retro
                    ? CUi.NomeEquipa(e, Dlg.Texto(e.NomeCompleto), 17, true)
                    : CUi.Txt(Dlg.Texto(e.NomeCompleto), 18, null, FontWeight.Bold, true);
                var esc = CUi.Escudo(e, 56);
                esc.Margin = new Thickness(0, 0, 12, 0);
                short vezes = copa ? (short)(e.DC1 >> 16) : (short)e.DC1;
                var info = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(nome);
                info.Children.Add(CUi.Txt(T(copa ? "{0}ª copa" : "{0}ª vitória no campeonato", vezes), 14, ListasCelular.Forte, FontWeight.SemiBold));
                p.Children.Add(ListasCelular.Grade("Auto,*", esc, info));
                if (s.TreinadorDa(e) is { } t)
                {
                    short n = copa ? (short)(t.D16 >> 16) : (short)t.D16;
                    // (o nome e a conta numa linha que quebra: no Linha_ passava da largura)
                    var tr = new StackPanel { Spacing = 1, Margin = new Thickness(0, 4, 0, 0) };
                    tr.Children.Add(CUi.Txt(T("Treinador"), 12.5, CUi.Fraco));
                    tr.Children.Add(CUi.Txt(Dlg.Texto(t.Nome) + " (" + T(copa ? "{0}ª copa" : "{0}ª vitória no campeonato", n) + ")", 14.5, null, FontWeight.SemiBold, true));
                    p.Children.Add(tr);
                }
            }
            else p.Children.Add(CUi.Txt("—", 18, CUi.Fraco));
            p.Children.Add(CUi.Linha_(T("Prêmio: ").TrimEnd().TrimEnd(':'), Ext(premio), CUi.Bom));
            return CUi.Cartao_(copa ? T("Vencedor da copa") : T("Vencedor do campeonato"), p);
        }

        var (max, marc) = TelaFimDeTemporada.Marcadores(s);
        var art = new StackPanel { Spacing = 6 };
        foreach (var (nome, e) in marc)
        {
            var eq = ListasCelular.Eq(e, 12.5);
            eq.MaxWidth = 140;
            eq.Margin = new Thickness(8, 0, 0, 0);
            art.Children.Add(ListasCelular.Grade("*,Auto", CUi.Txt(nome, 15, null, FontWeight.SemiBold), eq));
        }
        art.Children.Add(CUi.Linha_(T("Prêmio: ").TrimEnd().TrimEnd(':'), Ext(moeda.PremioMarcador), CUi.Bom));

        var corpo = new StackPanel { Spacing = 12 };
        corpo.Children.Add(Vencedor(s.Equipa(r.Campeao), false, moeda.PremioCampeao));
        corpo.Children.Add(Vencedor(s.Equipa(r.VencedorTaca), true, moeda.PremioTaca));
        corpo.Children.Add(CUi.Cartao_(T("Artilheiro ({0} gols)", max), art));

        var conta = CUi.Txt("", 12.5, CUi.Fraco);
        conta.HorizontalAlignment = HorizontalAlignment.Center;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        int tiques = 0;
        bool fechado = false;
        void Fechar()
        {
            if (fechado) return;
            fechado = true;
            timer.Stop();
            fechar();
        }
        void Contar() => conta.Text = T("Fecha sozinha em {0} s", 11 - tiques);
        timer.Tick += (_, _) => { if (++tiques > 10) Fechar(); else Contar(); };
        Contar();
        timer.Start();
        var ok = new CUi.Botao(T("Continuar"), true, Fechar) { HorizontalAlignment = HorizontalAlignment.Stretch };
        var rodape = new StackPanel { Spacing = 6, Children = { conta, ok } };
        var f = new CUi.Folha(T("Fim de Temporada"), corpo, rodape, T("Temporada {0}", s.S.Cabecalho.Ano), inteira: true, fechar: Fechar);
        f.DetachedFromVisualTree += (_, _) => timer.Stop();
        return f;
    }

    // as subidas e descidas da temporada que acabou: uma secao por divisao, cada equipa com a
    // seta verde (subiu) ou vermelha (desceu)
    internal static Control SubidasEDescidas(Sessao s, Resultado r, Action fechar)
    {
        var corpo = new StackPanel { Spacing = 8 };
        for (int d = 0; d <= 4; d++)
        {
            if (r.Subiram[d].Count == 0 && r.Desceram[d].Count == 0) continue;
            var sec = CUi.SecaoFolha(d < 4 ? T("{0}ª Divisão", d + 1) : T("Distrital"));
            if (corpo.Children.Count > 0) sec.Margin = new Thickness(0, 8, 0, 0);
            corpo.Children.Add(sec);
            var linhas = new List<Control>();
            foreach (var (ids, subiu) in new[] { (r.Subiram[d], true), (r.Desceram[d], false) })
                foreach (var id in ids)
                {
                    var seta = CUi.Txt(subiu ? T("▲ subiu") : T("▼ desceu"), 13.5, subiu ? CUi.Bom : CUi.Erro, FontWeight.Bold);
                    seta.Margin = new Thickness(8, 0, 0, 0);
                    linhas.Add(CUi.Toque(ListasCelular.Grade("*,Auto", ListasCelular.Eq(s.Equipa(id), 14.5), seta), null, 44));
                }
            corpo.Children.Add(CUi.Grupo(linhas));
        }
        if (corpo.Children.Count == 0) corpo.Children.Add(CUi.Txt(T("Nenhuma equipa mudou de divisão."), 14.5, CUi.Fraco, FontWeight.Normal, true));
        bool fechado = false;
        void Fechar() { if (fechado) return; fechado = true; fechar(); }
        var ok = new CUi.Botao(T("OK"), true, Fechar) { HorizontalAlignment = HorizontalAlignment.Stretch };
        return new CUi.Folha(T("Subidas e descidas"), corpo, ok, T("As equipas que mudam de divisão"), fechar: Fechar);
    }
}
