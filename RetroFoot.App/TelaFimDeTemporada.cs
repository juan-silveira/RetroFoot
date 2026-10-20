using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Save;
using RetroFoot.Motor.Temporada;
using Mem = RetroFoot.Motor.Partida.Memoria;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// TEndOfSeasonDlg (5:2bf6, "Fim de Temporada"): o vencedor do campeonato e o da Taca (nas
// cores da equipa, com o numero de titulos dela e do treinador), o melhor marcador (todos os
// jogadores com o maximo de golos da epoca) e os premios por extenso. O TimerCount (1 s)
// fecha a janela sozinha no 11o tique (5:3027). Os premios ja foram pagos pelo motor
// (Epoca.Premios) antes da janela.
//
// Ligar: o motor chama o fim de temporada pelo Ciclo(aoFim), na thread do motor. Na Sessao:
//     J.Ciclo(TelaFimDeTemporada.AoFim(this));
public static class TelaFimDeTemporada
{
    // (acrescimo, desligado = como o original) depois da janela, a lista das equipas que
    // subiram e desceram de divisao
    public static bool MostrarSubidasEDescidas;

    public static Action<Resultado> AoFim(Sessao s) => r => Mostrar(s, r);

    // na thread do motor: abre e espera fechar
    public static void Mostrar(Sessao s, Resultado r)
    {
        s.EsperarDialogo<bool>(fim => Janela(s, r, () => fim(true)));
        if (MostrarSubidasEDescidas) s.EsperarDialogo<bool>(fim => SubidasEDescidas(s, r, () => fim(true)));
    }

    internal static Control Janela(Sessao s, Resultado r, Action fechar)
    {
        // o celular: a folha da tela toda (FimDeTemporadaCelular.cs)
        if (Celular.Ativo) return FimDeTemporadaCelular.Janela(s, r, fechar);
        if (Visual.Moderno) return JanelaModerna(s, r, fechar);
        var camp = s.Equipa(r.Campeao);
        var taca = s.Equipa(r.VencedorTaca);
        var moeda = Moeda.DoSave(s.S);
        var b = W95.MsSans11b;
        var j = new Janela(T("Fim de Temporada"), 632, 349, Dlg.Lima);
        j.Por(new Bevel(), 8, 8, 601, 89);
        j.Por(new Bevel(), 8, 104, 601, 89);
        j.Por(new Rotulo { Texto = T("Vencedor do campeonato"), Fonte = b, Auto = true }, 16, 16, 146, 13);
        j.Por(new Rotulo { Texto = T("Vencedor da copa"), Fonte = b, Auto = true }, 16, 112, 102, 13);
        j.Por(new Rotulo { Texto = T("Treinador"), Fonte = b, Auto = true }, 16, 72, 55, 13);
        j.Por(new Rotulo { Texto = T("Treinador"), Fonte = b, Auto = true }, 16, 168, 55, 13);
        j.Por(new Rotulo { Texto = T("Prêmios"), Fonte = b, Auto = true }, 312, 208, 45, 13);
        j.Por(new Rotulo { Texto = T("Vencedor do campeonato"), Fonte = W95.MsSans11, Auto = true }, 312, 224, 123, 13);
        j.Por(new Rotulo { Texto = T("Vencedor da copa"), Fonte = W95.MsSans11, Auto = true }, 312, 240, 85, 13);
        j.Por(new Rotulo { Texto = T("Artilheiro"), Fonte = W95.MsSans11 }, 312, 256, 113, 13);

        // as linhas de cada vencedor: nome (TLabel nas cores da equipa, 19:0fdf e 21:1ed5),
        // "Nª vitória no campeonato" / "Nª copa" (+0xC1 / +0xC3 da equipa) e o treinador com
        // "(Nª vitória no campeonato)" / "(Nª copa)" (+0x16 / +0x18 do treinador)
        void Vencedor(Equipa? e, double y, bool copa)
        {
            if (e == null) return;
            var (letra, fundo) = (Dlg.CorDelphi(e.CorLetra, W95.Preto), Dlg.CorDelphi(e.CorFundo, W95.Branco));
            // com logo: o nome fica 72 pontos mais curto e o logo (64x64) entra no fim dele
            var logo = Escudos.Controle(e, 64);
            j.Por(new RotuloEstilo
            {
                Texto = Dlg.Texto(e.NomeCompleto), Familia = Dlg.FTimes, Tamanho = 19, Negrito = true, Italico = true,
                Cor = letra, Fundo = fundo, Centro = true,
            }, 16, y, logo == null ? 401 : 329, 22);
            if (logo != null) j.Por(logo, 353, y - 16, 64, 64);
            short vezes = copa ? (short)(e.DC1 >> 16) : (short)e.DC1;
            j.Por(new Rotulo { Texto = T(copa ? "{0}ª copa" : "{0}ª vitória no campeonato", vezes), Fonte = W95.Arial13 }, 432, y, 169, 17);
            var t = s.TreinadorDa(e);
            if (t != null)
            {
                short n = copa ? (short)(t.D16 >> 16) : (short)t.D16;
                j.Por(new Rotulo { Texto = Dlg.Texto(t.Nome) + " (" + T(copa ? "{0}ª copa" : "{0}ª vitória no campeonato", n) + ")", Fonte = W95.Arial11, Auto = true }, 88, y + 32, 400, 14);
            }
        }
        Vencedor(camp, 40, false);
        Vencedor(taca, 136, true);

        j.Por(new Rotulo { Texto = FormatoDinheiro.Valor(moeda, moeda.PremioCampeao), Fonte = W95.Arial11 }, 448, 224, 161, 14);
        j.Por(new Rotulo { Texto = FormatoDinheiro.Valor(moeda, moeda.PremioTaca), Fonte = W95.Arial11 }, 448, 240, 161, 14);
        j.Por(new Rotulo { Texto = FormatoDinheiro.Valor(moeda, moeda.PremioMarcador), Fonte = W95.Arial11 }, 448, 256, 161, 14);

        // o melhor marcador: o maximo de +0x14 (com sinal, comecando em 0) e, na ordem da
        // lista DS:26FE, cada jogador com esse numero: "NOME  (EQUIPA)"
        int max = 0;
        var todos = new List<(Jogador P, Equipa E)>();
        s.J.M.TodosJogadores((e, p) => { todos.Add((p, e)); max = Math.Max(max, Mem.Golos14(p)); });
        var marc = todos.Where(x => Mem.Golos14(x.P) == max)
            .Select(x => System.Text.Encoding.Latin1.GetString(RetroFoot.Motor.Partida.Jornada.NomeNaTela(x.P)) + "  (" + Dlg.Texto(x.E.NomeCurto) + ")")
            .ToList();
        j.Por(new Rotulo { Texto = T("Artilheiro ({0} gols)", max), Fonte = b }, 16, 208, 201, 13);
        j.Por(new ListaSemBorda { Quantos = () => marc.Count, Texto = i => marc[i], Fonte = W95.Arial13 }, 16, 232, 265, 89);

        var ok = j.Por(Dlg.BotaoGrande(T("Fechar"), "Botoes/BBOK.png"), 540, 280, 64, 64);
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
        ok.Clique += Fechar;
        j.AoEnter = Fechar;
        j.AoFechar = Fechar;
        return new CaixaDialogo(j);
    }

    // o melhor marcador: o maximo de +0x14 (com sinal, comecando em 0) e cada jogador com ele
    internal static (int Max, List<(string Nome, Equipa E)> Lista) Marcadores(Sessao s)
    {
        int max = 0;
        var todos = new List<(Jogador P, Equipa E)>();
        s.J.M.TodosJogadores((e, p) => { todos.Add((p, e)); max = Math.Max(max, Mem.Golos14(p)); });
        return (max, todos.Where(x => Mem.Golos14(x.P) == max)
            .Select(x => (System.Text.Encoding.Latin1.GetString(RetroFoot.Motor.Partida.Jornada.NomeNaTela(x.P)), x.E)).ToList());
    }

    // ------------------------------------------------------------ Moderno
    // O fim de temporada no visual Moderno: um cartao por vencedor (logo, nome, o numero de
    // titulos da equipa e do treinador, o premio), com a faixa dourada (campeonato) ou
    // prateada (copa); o artilheiro com os jogadores empatados no maximo; a contagem do
    // relogio do original (fecha no 11o tique) no rodape. 4:3: os cartoes um embaixo do
    // outro; 16:9: campeao e copa lado a lado.
    static Control JanelaModerna(Sessao s, Resultado r, Action fechar)
    {
        var moeda = Moeda.DoSave(s.S);
        string Ext(long v) => FormatoDinheiro.Valor(moeda, v);
        bool largo = Formato.Largo;

        Control Vencedor(Equipa? e, bool copa, long premio)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            var corpo = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            corpo.Children.Add(ClubeModerno.Rotulo(copa ? T("Vencedor da copa") : T("Campeão")));
            if (e != null)
            {
                var esc = new EscudoClube(e, 60) { Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
                g.Children.Add(esc);
                corpo.Children.Add(new TextBlock
                {
                    Text = Dlg.Texto(e.NomeCompleto), FontSize = 20, FontWeight = FontWeight.Bold, Foreground = ClubeModerno.Texto,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                short vezes = copa ? (short)(e.DC1 >> 16) : (short)e.DC1;
                corpo.Children.Add(ClubeModerno.T(T(copa ? "{0}ª copa" : "{0}ª vitória no campeonato", vezes), 13, copa ? ClubeModerno.Prata : ClubeModerno.Ouro, FontWeight.SemiBold));
                if (s.TreinadorDa(e) is { } t)
                {
                    short n = copa ? (short)(t.D16 >> 16) : (short)t.D16;
                    corpo.Children.Add(ClubeModerno.T(T("Treinador: ") + Dlg.Texto(t.Nome) + " (" + T(copa ? "{0}ª copa" : "{0}ª vitória no campeonato", n) + ")", 12.5, ClubeModerno.Fraco));
                }
            }
            else corpo.Children.Add(ClubeModerno.T("—", 20, ClubeModerno.Fraco));
            corpo.Children.Add(ClubeModerno.T(T("Prêmio: ") + Ext(premio), 12.5, ClubeModerno.Verde));
            Grid.SetColumn(corpo, 1);
            g.Children.Add(corpo);
            var faixa = new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = copa ? ClubeModerno.Prata : ClubeModerno.Ouro, Margin = new Thickness(0, 0, 14, 0) };
            var d = new DockPanel();
            DockPanel.SetDock(faixa, Dock.Left);
            d.Children.Add(faixa);
            d.Children.Add(g);
            return ClubeModerno.Secao(d);
        }

        var (max, marc) = Marcadores(s);
        var lista = new StackPanel { Spacing = 6 };
        foreach (var (nome, e) in marc)
        {
            var l = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            l.Children.Add(new EscudoClube(e, 22) { VerticalAlignment = VerticalAlignment.Center });
            l.Children.Add(new TextBlock { Text = nome, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = ClubeModerno.Texto, VerticalAlignment = VerticalAlignment.Center });
            l.Children.Add(new TextBlock { Text = Dlg.Texto(e.NomeCurto), FontSize = 13, Foreground = ClubeModerno.Fraco, VerticalAlignment = VerticalAlignment.Center });
            lista.Children.Add(l);
        }
        var art = new DockPanel();
        var cabArt = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var premioArt = ClubeModerno.T(T("Prêmio: ") + Ext(moeda.PremioMarcador), 12.5, ClubeModerno.Verde);
        DockPanel.SetDock(premioArt, Dock.Right);
        cabArt.Children.Add(premioArt);
        cabArt.Children.Add(ClubeModerno.Rotulo(T("Artilheiro") + " · " + (max == 1 ? T("1 gol") : T("{0} gols", max))));
        DockPanel.SetDock(cabArt, Dock.Top);
        art.Children.Add(cabArt);
        art.Children.Add(new ScrollViewer { Content = lista, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });

        var camp = Vencedor(s.Equipa(r.Campeao), false, moeda.PremioCampeao);
        var taca = Vencedor(s.Equipa(r.VencedorTaca), true, moeda.PremioTaca);
        var cartoes = new Grid();
        if (largo)
        {
            cartoes.ColumnDefinitions = new ColumnDefinitions("*,12,*");
            cartoes.RowDefinitions = new RowDefinitions("Auto,12,*");
            Grid.SetColumn(taca, 2);
        }
        else cartoes.RowDefinitions = new RowDefinitions("Auto,12,Auto,12,*");
        cartoes.Children.Add(camp);
        cartoes.Children.Add(taca);
        var cartaoArt = ClubeModerno.Secao(art);
        cartoes.Children.Add(cartaoArt);
        if (largo) { Grid.SetRow(cartaoArt, 2); Grid.SetColumnSpan(cartaoArt, 3); }
        else { Grid.SetRow(taca, 2); Grid.SetRow(cartaoArt, 4); }

        var icone = Escudos.Controle(Escudos.Liga(s.S, 0), 44);
        // a altura cabe o artilheiro inteiro: antes (500/430 fixos) a linha dele saia cortada
        // embaixo; cada empatado a mais (ate 4 a vista; dai rola) acrescenta uma linha
        double extra = 28 * (Math.Clamp(marc.Count, 1, 4) - 1);
        var j = new JanelaModerna(T("Fim de Temporada"), T("Temporada {0}", s.S.Cabecalho.Ano), icone, cartoes, largo ? 780 : 600,
            Math.Min(580, (largo ? 430 : 520) + extra));
        var conta = ClubeModerno.T("", 12, ClubeModerno.Fraco);
        j.NotaRodape(conta);
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
        j.DetachedFromVisualTree += (_, _) => timer.Stop();
        j.Botao(T("Continuar"), true, Fechar);
        j.AoEnter = Fechar;
        j.AoFechar = Fechar;
        return j;
    }

    // (acrescimo, Moderno) as subidas e descidas: um cartao por divisao com as equipas (logo,
    // nome) e a seta verde (subiu) ou vermelha (desceu). 16:9: os cartoes em duas colunas.
    static Control SubidasModerna(Sessao s, Resultado r, Action fechar)
    {
        bool largo = Formato.Largo;
        var cartoes = new List<Control>();
        for (int d = 0; d <= 4; d++)
        {
            if (r.Subiram[d].Count == 0 && r.Desceram[d].Count == 0) continue;
            var p = new StackPanel { Spacing = 6 };
            p.Children.Add(ClubeModerno.T(d < 4 ? T("{0}ª Divisão", d + 1) : T("Distrital"), 15, ClubeModerno.Texto, FontWeight.Bold));
            foreach (var (ids, subiu) in new[] { (r.Subiram[d], true), (r.Desceram[d], false) })
                foreach (var id in ids)
                {
                    var e = s.Equipa(id);
                    var l = new DockPanel();
                    var seta = ClubeModerno.T(subiu ? T("▲ subiu") : T("▼ desceu"), 12.5, subiu ? ClubeModerno.Verde : ClubeModerno.Vermelho, FontWeight.SemiBold);
                    seta.VerticalAlignment = VerticalAlignment.Center;
                    DockPanel.SetDock(seta, Dock.Right);
                    l.Children.Add(seta);
                    var esc = new EscudoClube(e, 24) { Margin = new Thickness(0, 0, 10, 0) };
                    DockPanel.SetDock(esc, Dock.Left);
                    l.Children.Add(esc);
                    l.Children.Add(new TextBlock { Text = e != null ? Dlg.Texto(e.NomeCurto) : "", FontSize = 14, Foreground = ClubeModerno.Texto, VerticalAlignment = VerticalAlignment.Center });
                    p.Children.Add(l);
                }
            cartoes.Add(ClubeModerno.Secao(p));
        }
        Panel grade = largo ? new Avalonia.Controls.Primitives.UniformGrid { Columns = 2 } : new StackPanel { Spacing = 12 };
        foreach (var c in cartoes)
        {
            if (largo) c.Margin = new Thickness(0, 0, 12, 12);
            grade.Children.Add(c);
        }
        if (cartoes.Count == 0) grade.Children.Add(ClubeModerno.T(T("Nenhuma equipa mudou de divisão."), 14, ClubeModerno.Fraco));
        var rolar = new ScrollViewer { Content = grade, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var j = new JanelaModerna(T("Subidas e descidas"), T("As equipas que mudam de divisão"), null, rolar, largo ? 760 : 520, largo ? 480 : 540);
        bool fechado = false;
        void Fechar() { if (fechado) return; fechado = true; fechar(); }
        j.Botao(T("OK"), true, Fechar);
        j.AoEnter = Fechar;
        j.AoFechar = Fechar;
        return j;
    }

    // (acrescimo) as subidas e descidas da temporada que acabou, na TPrintListDlg
    internal static Control SubidasEDescidas(Sessao s, Resultado r, Action fechar)
    {
        if (Celular.Ativo) return FimDeTemporadaCelular.SubidasEDescidas(s, r, fechar);
        if (Visual.Moderno) return SubidasModerna(s, r, fechar);
        var linhas = new List<byte[]>();
        string Div(int d) => d < 4 ? T("{0}ª Divisão", d + 1) : T("Distrital");
        for (int d = 0; d <= 4; d++)
        {
            if (r.Subiram[d].Count == 0 && r.Desceram[d].Count == 0) continue;
            var t = new List<byte> { 2 };
            Formatado.Col(t, Div(d), 0xB4);
            linhas.Add(t.ToArray());
            foreach (var (ids, txt) in new[] { (r.Subiram[d], T("subiu")), (r.Desceram[d], T("desceu")) })
                foreach (var id in ids)
                {
                    var l = new List<byte>();
                    Formatado.Col(l, " ", 0x14);
                    Formatado.Col(l, s.Equipa(id) is { } e ? Dlg.Bytes(e.NomeCurto) : Dlg.Latin1(" "), (byte)'x');
                    Formatado.Col(l, txt, 0x50);
                    linhas.Add(l.ToArray());
                }
        }
        CaixaDialogo? c = null;
        var d2 = new Dialogos(x => c = x as CaixaDialogo, _ => fechar());
        TelaListas.Lista(d2, T("Subidas e descidas"), linhas.Count, i => linhas[i], 0);
        return c!;
    }
}
