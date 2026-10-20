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
        if (CUi.Retro) return JanelaRetro(s, r, fechar);
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
        if (CUi.Retro) return SubidasRetro(s, r, fechar);
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

    // Retro: a TPrintListDlg do computador: a divisao em negrito e, embaixo dela, cada equipa
    // com "subiu" / "desceu"
    static Control SubidasRetro(Sessao s, Resultado r, Action fechar)
    {
        var linhas = new List<Control>();
        for (int d = 0; d <= 4; d++)
        {
            if (r.Subiram[d].Count == 0 && r.Desceram[d].Count == 0) continue;
            linhas.Add(ListasCelular.LinhaR(ListasCelular.R(d < 4 ? T("{0}ª Divisão", d + 1) : T("Distrital"), true)));
            foreach (var (ids, txt) in new[] { (r.Subiram[d], T("subiu")), (r.Desceram[d], T("desceu")) })
                foreach (var id in ids)
                    linhas.Add(ListasCelular.LinhaR(ListasCelular.Grade("26,*,90", null,
                        ListasCelular.R(s.Equipa(id) is { } e ? Dlg.Texto(e.NomeCurto) : ""), ListasCelular.R(txt))));
        }
        bool fechado = false;
        void Fechar() { if (fechado) return; fechado = true; fechar(); }
        return ListasCelular.FolhaR(T("Subidas e descidas"), ListasCelular.CaixaR(linhas), Fechar);
    }

    // Retro: a TEndOfSeasonDlg do computador na tela toda: o fundo lima, uma moldura por
    // vencedor (o rotulo em negrito, o nome completo nas cores da equipa em Times negrito e
    // italico no meio, o numero de titulos e o treinador), o artilheiro (cada empatado com a
    // equipa entre parenteses), os premios e o Fechar com o visto verde
    static Control JanelaRetro(Sessao s, Resultado r, Action fechar)
    {
        var moeda = Moeda.DoSave(s.S);
        TextBlock X(string t, bool negrito = false, double tam = 14.5)
        {
            var x = ListasCelular.R(t, negrito, null, tam);
            x.TextWrapping = TextWrapping.Wrap;
            return x;
        }

        Control Vencedor(Equipa? e, bool copa)
        {
            var p = new StackPanel { Spacing = 8 };
            p.Children.Add(X(copa ? T("Vencedor da copa") : T("Vencedor do campeonato"), true, 14));
            if (e != null)
            {
                var faixa = new Border
                {
                    Background = Dlg.CorDelphi(e.CorFundo, W95.Branco), Padding = new Thickness(6, 2), MinHeight = 32,
                    Child = new TextBlock
                    {
                        Text = Dlg.Texto(e.NomeCompleto), FontFamily = Dlg.FTimes, FontSize = 21 * Acessibilidade.Texto, FontWeight = FontWeight.Bold,
                        FontStyle = FontStyle.Italic, Foreground = Dlg.CorDelphi(e.CorLetra, W95.Preto), TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
                    },
                };
                // com logo, ele fica no fim da faixa (como no computador)
                if (Escudos.Controle(e, 48) is { } logo) p.Children.Add(ListasCelular.Grade("*,Auto", faixa, ListasCelular.Margem(logo, 10)));
                else p.Children.Add(faixa);
                short vezes = copa ? (short)(e.DC1 >> 16) : (short)e.DC1;
                p.Children.Add(X(T(copa ? "{0}ª copa" : "{0}ª vitória no campeonato", vezes), false, 15.5));
                if (s.TreinadorDa(e) is { } t)
                {
                    short n = copa ? (short)(t.D16 >> 16) : (short)t.D16;
                    var nt = X(Dlg.Texto(t.Nome) + " (" + T(copa ? "{0}ª copa" : "{0}ª vitória no campeonato", n) + ")", false, 13.5);
                    nt.Margin = new Thickness(10, 0, 0, 0);
                    p.Children.Add(ListasCelular.Grade("Auto,*", X(T("Treinador"), true, 14), nt));
                }
            }
            return CUi.GrupoRetro(null, p);
        }

        var (max, marc) = TelaFimDeTemporada.Marcadores(s);
        var art = new StackPanel { Spacing = 2 };
        art.Children.Add(X(T("Artilheiro ({0} gols)", max), true, 14));
        var nomes = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (nome, e) in marc) nomes.Children.Add(X(nome + "  (" + Dlg.Texto(e.NomeCurto) + ")", false, 15));
        // (a ListBox do computador: rola dentro, os premios ficam a vista)
        art.Children.Add(new ScrollViewer { Content = nomes, MaxHeight = 132, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });

        var pr = new StackPanel { Spacing = 2 };
        pr.Children.Add(X(T("Prêmios"), true, 14));
        foreach (var (rot, v) in new[] { (T("Vencedor do campeonato"), moeda.PremioCampeao), (T("Vencedor da copa"), moeda.PremioTaca), (T("Artilheiro"), moeda.PremioMarcador) })
            pr.Children.Add(ListasCelular.Grade("*,Auto", X(rot, false, 14), ListasCelular.RD(FormatoDinheiro.Valor(moeda, v), false, null, 14)));

        var corpo = new StackPanel { Spacing = 10 };
        corpo.Children.Add(Vencedor(s.Equipa(r.Campeao), false));
        corpo.Children.Add(Vencedor(s.Equipa(r.VencedorTaca), true));
        art.Margin = new Thickness(8, 6, 8, 0);
        pr.Margin = new Thickness(8, 6, 8, 0);
        corpo.Children.Add(art);
        corpo.Children.Add(pr);

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
        timer.Tick += (_, _) => { if (++tiques > 10) Fechar(); };
        timer.Start();
        var f = new CUi.Folha(T("Fim de Temporada"), corpo, ListasCelular.BotaoOk(T("Fechar"), Fechar), inteira: true, fechar: Fechar);
        f.Background = Dlg.Lima;
        f.DetachedFromVisualTree += (_, _) => timer.Stop();
        return f;
    }
}
