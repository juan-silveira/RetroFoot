using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Save;
using Chicotada = RetroFoot.Motor.Rodada.Chicotada;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// As listas dos menus Campeonato e Tecnico no celular (Celular.cs), nas folhas do kit
// (CelularUi.cs): a tela toda, com a seta de voltar, e uma linha por item, feita para o
// celular (o calendario com o adversario e o resultado, os artilheiros com a bandeira, as
// transferencias em duas linhas...). Os dados e as regras sao os de TelaListas (o computador);
// estas so desenham. As listas sem versao propria (Generica) vem das mesmas linhas formatadas
// (Formatado), em letra legivel e com a coluna larga a encolher.
static class ListasCelular
{
    // ---------------------------------------------------------------- a folha
    // abre a lista: titulo, as linhas (rolam) e o voltar (que chama fechou); fim = rola ate o
    // ultimo item (OpUltimo); as listas abrem na gaveta (a folha de baixo, com o X), como os
    // menus da tela da equipa; inteira = a tela toda; ok = o botao OK embaixo (as chicotadas)
    internal static (CUi.Folha Folha, Action Fechar, Action Tirar) Abrir(Dialogos d, string titulo, string? sub, Control corpo,
        Action? fechou, bool fim = false, bool inteira = false, Control? icone = null, bool ok = false)
    {
        CUi.Folha? f = null;
        bool fechado = false;
        // Tirar: so fecha (o toque que abre o plantel, como o duplo clique do computador)
        void Tirar() { if (fechado) return; fechado = true; d.Fechar(f!); }
        void Fechar() { if (fechado) return; Tirar(); fechou?.Invoke(); }
        // (no Retro, todas tem o OK com o visto verde embaixo, como a TPrintListDlg)
        Control? rodape = ok || CUi.Retro ? BotaoOk(T("OK"), Fechar) : null;
        f = new CUi.Folha(titulo, corpo, rodape, sub, icone, inteira: inteira, fechar: Fechar);
        if (fim)
            f.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (corpo.FindAncestorOfType<ScrollViewer>() is { } sv) sv.ScrollToEnd();
            }, DispatcherPriority.Background);
        d.Abrir(f);
        return (f, Fechar, Tirar);
    }

    // o botao de fechar embaixo: no Retro o TBitBtn do computador (o visto verde, no meio, na
    // largura dele); no Moderno, a largura toda
    internal static Control BotaoOk(string texto, Action clique)
    {
        var b = new CUi.Botao(texto, true, clique) { HorizontalAlignment = HorizontalAlignment.Stretch };
        if (!CUi.Retro) return b;
        b.ComIcone("Imagens/ok.png", "Imagens/ok-desab.png");
        b.HorizontalAlignment = HorizontalAlignment.Center;
        b.MinWidth = 150;
        return b;
    }

    // ---------------------------------------------------------------- Retro
    // A TPrintListDlg do computador no celular: a moldura (Bevel) com a lista branca afundada
    // dentro, as linhas no texto preto da lista (Arial), as colunas do original na ordem dele, e
    // o item escolhido no azul do realce (OpUltimo: o ultimo). Sem fio entre as linhas.

    // um texto da lista (preto; negrito como os codigos do original)
    internal static TextBlock R(string s, bool negrito = false, IBrush? cor = null, double tam = 14.5) =>
        CUi.Txt(s, tam, cor ?? W95.Preto, negrito ? FontWeight.Bold : FontWeight.Normal);

    // o mesmo a direita (os numeros)
    internal static TextBlock RD(string s, bool negrito = false, IBrush? cor = null, double tam = 14.5)
    {
        var t = R(s, negrito, cor, tam);
        t.HorizontalAlignment = HorizontalAlignment.Right;
        t.TextAlignment = TextAlignment.Right;
        return t;
    }

    // uma linha da lista: o fundo dela (o calendario) ou o realce (escolhida, com o texto
    // branco); com toque, a altura do dedo
    internal static Clicavel LinhaR(Control c, Action? toque = null, IBrush? fundo = null, bool sel = false)
    {
        var b = new Clicavel
        {
            Background = sel ? W95.Destaque : fundo ?? Brushes.Transparent, Child = c, CorFoco = null,
            MinHeight = toque != null ? 40 : 28, Padding = new Thickness(6, 2),
        };
        if (sel) Branco(c);
        if (toque != null) b.Clique += toque; else b.Cursor = Avalonia.Input.Cursor.Default;
        return b;
    }

    // o texto da linha escolhida fica branco (o realce do Windows 95)
    internal static void Branco(Control? c)
    {
        switch (c)
        {
            case TextBlock t when t.Tag is not "fixo": t.Foreground = W95.Branco; break;
            case Panel p: foreach (var x in p.Children) Branco(x); break;
            case Decorator d: Branco(d.Child); break;
        }
    }

    // a moldura com a lista dentro (as linhas ja feitas); vazia, a caixa branca sem nada
    internal static Control CaixaR(IReadOnlyList<Control> linhas)
    {
        Control lista = linhas.Count == 0
            ? CUi.Grupo(new Control[] { new Border { MinHeight = 28 } })
            : CUi.Grupo(linhas);
        lista.VerticalAlignment = VerticalAlignment.Top;
        var g = CUi.GrupoRetro(null, lista);
        g.VerticalAlignment = VerticalAlignment.Top;
        return g;
    }

    // n linhas da lista: linha(i), toque(i) e a escolhida (sel; -1 nenhuma)
    internal static Control ListaR(int n, Func<int, Control> linha, Func<int, Action?>? toque = null, int sel = -1, Func<int, IBrush?>? fundo = null)
    {
        var ls = new List<Control>(n);
        for (int i = 0; i < n; i++) ls.Add(LinhaR(linha(i), toque?.Invoke(i), fundo?.Invoke(i), i == sel));
        return CaixaR(ls);
    }

    // a folha da lista no Retro (as que devolvem a janela: as subidas e descidas)
    internal static CUi.Folha FolhaR(string titulo, Control corpo, Action fechar) =>
        new(titulo, corpo, BotaoOk(T("OK"), fechar), fechar: fechar);

    // as linhas num grupo (com o fio entre elas); toque(i) != null: a linha se toca
    internal static Control Grupo(int n, Func<int, Control> linha, Func<int, Action?>? toque = null, double altura = 44)
    {
        if (n == 0) return new TextBlock { Text = T("Nada a mostrar."), FontFamily = CUi.Fonte, FontSize = 14 * Acessibilidade.Texto, Foreground = CUi.Fraco, VerticalAlignment = VerticalAlignment.Top };
        var ls = new List<Control>(n);
        for (int i = 0; i < n; i++)
        {
            var t = CUi.Toque(linha(i), toque?.Invoke(i), altura);
            t.Padding = new Thickness(10, 4);
            ls.Add(t);
        }
        var g = CUi.Grupo(ls);
        g.VerticalAlignment = VerticalAlignment.Top;
        return g;
    }

    // ---------------------------------------------------------------- pecas
    // uma grade com as colunas dadas e os controles nelas (null = coluna vazia)
    internal static Grid Grade(string colunas, params Control?[] cs)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(colunas), VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < cs.Length; i++)
        {
            if (cs[i] is not { } c) continue;
            c.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(c, i);
            g.Children.Add(c);
        }
        return g;
    }

    // um numero (ou texto curto) numa coluna, a direita
    internal static TextBlock Num(string s, double tam = 14, IBrush? cor = null, bool negrito = false)
    {
        var t = CUi.Txt(s, tam, cor, negrito ? FontWeight.Bold : FontWeight.Normal);
        t.HorizontalAlignment = HorizontalAlignment.Right;
        t.TextAlignment = TextAlignment.Right;
        return t;
    }

    // a equipa: no Retro a caixa nas cores dela (como as listas do classico); no Moderno o
    // logo (ou o circulo nas cores) e o nome, que encolhe com reticencias
    internal static Control Eq(Equipa? e, double tam = 14, bool negrito = false)
    {
        if (e == null) return CUi.Txt("—", tam, CUi.Fraco);
        string nome = TelaDinheiro.Texto(e.NomeCurto);
        if (CUi.Retro) return CUi.NomeEquipa(e, nome, tam, negrito);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), VerticalAlignment = VerticalAlignment.Center };
        var esc = CUi.Escudo(e, tam + 8);
        esc.Margin = new Thickness(0, 0, 7, 0);
        g.Children.Add(esc);
        var t = CUi.Txt(nome, tam, null, negrito ? FontWeight.Bold : FontWeight.Medium);
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        return g;
    }

    // a cor do numero que se destaca (a forca, os golos, o ano)
    internal static IBrush Forte => CUi.Retro ? W95.Marinho : TemaVerde.Destaque;

    // o cabecalho das colunas, por cima do grupo (sobre o fundo verde)
    internal static Control Cabecalho(string colunas, params string?[] nomes)
    {
        var cs = nomes.Select((n, i) =>
        {
            if (n == null) return (Control?)null;
            var t = CUi.Txt(n, 11.5, CUi.TituloSecao, FontWeight.Bold);
            if (i > 0 && i == nomes.Length - 1 || (i > 1 && colunas.Split(',')[i] != "*")) { t.HorizontalAlignment = HorizontalAlignment.Right; t.TextAlignment = TextAlignment.Right; }
            return t;
        }).ToArray();
        var g = Grade(colunas, cs);
        g.Margin = new Thickness(11, 0, 11, 4);
        return g;
    }

    static StackPanel Pilha(params Control[] cs)
    {
        var p = new StackPanel { Spacing = 8 };
        foreach (var c in cs) p.Children.Add(c);
        return p;
    }

    // ---------------------------------------------------------------- generica
    // TelaListas.Lista no celular: as linhas formatadas (Formatado) em letra de 14,5, cada coluna
    // no tamanho do texto; a larga (o nome: a mais larga alinhada a esquerda, fora a ultima)
    // fica com o que sobra e encolhe com reticencias. Os mesmos codigos: negrito, italico,
    // vermelho, a direita, o logo.
    internal static void Generica(Dialogos d, string titulo, int n, Func<int, byte[]> linha, int opcoes, Action<int>? duplo, Action? fechou)
    {
        bool numerada = (opcoes & TelaListas.OpNumerada) != 0;
        bool toca = (opcoes & TelaListas.OpDuplo) != 0 && duplo != null;
        Action Tirar = () => { };
        bool ultimo = (opcoes & TelaListas.OpUltimo) != 0;
        var corpo = CUi.Retro
            ? ListaR(n, i => LinhaFormatada(Formatado.Ler(numerada ? TelaListas.Numerada(i, linha(i)) : linha(i), false)),
                toca ? i => () => { Tirar(); duplo!(i); } : null, ultimo ? n - 1 : -1)
            : Grupo(n, i => LinhaFormatada(Formatado.Ler(numerada ? TelaListas.Numerada(i, linha(i)) : linha(i), false)),
            toca ? i => () => { Tirar(); duplo!(i); } : null, 40);
        (_, _, Tirar) = Abrir(d, titulo, null, corpo, fechou, (opcoes & TelaListas.OpUltimo) != 0);
    }

    static Control LinhaFormatada(List<Formatado.Coluna> l)
    {
        int k = -1;
        for (int j = 0; j < l.Count - 1; j++)
            if (!l[j].Direita && l[j].W >= 60 && (k < 0 || l[j].W > l[k].W)) k = j;
        if (k < 0) k = l.Count - 1;
        var g = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        for (int j = 0; j < l.Count; j++)
        {
            var c = l[j];
            g.ColumnDefinitions.Add(new ColumnDefinition(j == k ? GridLength.Star : GridLength.Auto));
            string txt = c.Txt;
            if (txt.Length > 0 && txt[0] == 'ÿ') txt = "•";
            else if (txt.Length > 0 && txt[0] == 'þ') txt = "–";
            else if (txt.Length > 0 && txt[0] == 'ý') txt = "";
            txt = txt.Trim();
            if (txt.Length == 0 && c.Logo == null) continue;
            var t = CUi.Txt(txt, CUi.Retro ? 14 : 14.5, c.Vermelho ? CUi.Erro : null, c.Negrito ? FontWeight.Bold : FontWeight.Normal);
            if (c.Italico) t.FontStyle = FontStyle.Italic;
            Control cel = t;
            if (c.Direita) { t.HorizontalAlignment = HorizontalAlignment.Right; t.TextAlignment = TextAlignment.Right; }
            if (c.Logo is { } lg)
            {
                var p = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                p.Children.Add(new Image { Source = lg, Width = 20, Height = 20, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                Grid.SetColumn(t, 1);
                p.Children.Add(t);
                cel = p;
            }
            cel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(cel, j);
            g.Children.Add(cel);
        }
        return g;
    }

    // ---------------------------------------------------------------- Campeonato
    // o calendario: a jornada, o local (C/F/N: cheio em casa; vermelho na Copa, como as cores
    // do original), o adversario, o resultado (verde ganhou, vermelho perdeu) e embaixo dele o
    // prolongamento / os penaltis e o publico. toque(i): o plantel do adversario (compra directa).
    internal static void Calendario(Dialogos d, Sessao s, List<EntradaCalendario> cal, Action<int>? toque, Action? fechou, string sub)
    {
        Action Tirar = () => { };
        var corpo = CUi.Retro
            ? ListaR(cal.Count, i => LinhaCalendarioRetro(s, cal[i]), toque == null ? null : i => () => { Tirar(); toque(i); },
                fundo: i => CorCalendario(cal[i]).Fundo)
            : Grupo(cal.Count, i => LinhaCalendario(s, cal[i]), toque == null ? null : i => () => { Tirar(); toque(i); }, 46);
        (_, _, Tirar) = Abrir(d, T("Calendário"), sub, corpo, fechou);
    }

    // as cores do original (3:254c fundo, 3:2597 letra): em casa o fundo azul (vermelho na
    // Copa) e a letra branca; fora (e neutro) o fundo branco e a letra azul (ou vermelha)
    static (IBrush Letra, IBrush Fundo) CorCalendario(EntradaCalendario c)
    {
        var cor = c.B07 == 1 ? W95.Vermelho : W95.Azul;
        return c.W0E == 0 ? (W95.Branco, cor) : (cor, W95.Branco);
    }

    // Retro: as colunas do TCalendarEntry.Formatar (a jornada a direita, o adversario, C/F/N,
    // golos : golos, "a. p." / "v. pen." / "d. pen." e o publico), a linha toda nas cores do jogo
    static Control LinhaCalendarioRetro(Sessao s, EntradaCalendario c)
    {
        var (letra, _) = CorCalendario(c);
        var adv = s.Equipa(c.W08);
        TextBlock X(string t, bool dir = false) { var x = dir ? RD(t, false, letra) : R(t, false, letra); x.Tag = "fixo"; return x; }
        var nome = X(adv != null ? Dlg.Texto(adv.NomeCurto) : "");
        nome.Margin = new Thickness(8, 0, 4, 0);
        var local = X(TelaListas.Local("CFN"[Math.Min((int)c.W0E, 2)]));
        Control? gp = null, dp = null, gc = null, ex = null, pub = null;
        if ((short)c.W0A >= 0)
        {
            gp = X(((short)c.W0A).ToString(), true);
            var x = X(":"); x.HorizontalAlignment = HorizontalAlignment.Center; dp = x;
            gc = X(((short)c.W0C).ToString(), true);
            string e = (c.B06 switch { 1 => T("  a. p."), 2 => T("  v. pen."), 3 => T("  d. pen."), _ => "" }).Trim();
            if (e.Length > 0) { var t = X(e); t.FontSize = 12 * Acessibilidade.Texto; t.Margin = new Thickness(6, 0, 0, 0); ex = t; }
            pub = X(((int)c.D10).ToString(), true);
        }
        return Grade("24,*,22,22,12,22,Auto,62", X((short)c.W04 > 0 ? c.W04.ToString() : "", true), nome, local, gp, dp, gc, ex, pub);
    }

    static Control LinhaCalendario(Sessao s, EntradaCalendario c)
    {
        var adv = s.Equipa(c.W08);
        bool taca = c.B07 == 1;
        int local = Math.Min((int)c.W0E, 2);
        IBrush cor = local == 2 ? CUi.Fraco : taca ? (CUi.Retro ? W95.Vermelho : TemaVerde.Vermelho) : (CUi.Retro ? W95.Azul : TemaVerde.Azul);
        var jn = Num((short)c.W04 > 0 ? c.W04.ToString() : "", 13, CUi.Fraco, true);
        var letra = CUi.Txt(TelaListas.Local("CFN"[local]), 12.5, local == 0 ? (CUi.Retro ? W95.Branco : TemaVerde.FundoTela) : cor, FontWeight.Bold);
        letra.HorizontalAlignment = HorizontalAlignment.Center;
        letra.Tag = "fixo";
        var marca = new Border
        {
            Width = 26, Height = 24, CornerRadius = new CornerRadius(CUi.Retro ? 0 : 7), Background = local == 0 ? cor : Brushes.Transparent,
            BorderBrush = cor, BorderThickness = new Thickness(1.5), Child = letra, Margin = new Thickness(8, 0, 10, 0),
        };
        var nome = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var eq = Eq(adv, 14.5);
        Control meio = eq;
        if (taca)
        {
            var gm = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto") };
            gm.Children.Add(eq);
            var et = CUi.Etiqueta(T("Copa"), CUi.Retro ? W95.Branco : TemaVerde.FundoTela, cor);
            et.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(et, 1);
            gm.Children.Add(et);
            meio = gm;
            eq.MaxWidth = 170;
        }
        Control? res = null;
        if ((short)c.W0A >= 0)
        {
            short gp = (short)c.W0A, gc = (short)c.W0C;
            var placar = Num(gp + " : " + gc, 16, gp > gc ? CUi.Bom : gp < gc ? CUi.Erro : CUi.Texto, true);
            string extra = (c.B06 switch { 1 => T("  a. p."), 2 => T("  v. pen."), 3 => T("  d. pen."), _ => "" }).Trim();
            string pub = ((int)c.D10).ToString();
            var baixo = Num(extra.Length > 0 ? extra + " · " + pub : pub, 11.5, CUi.Fraco);
            res = new StackPanel { Spacing = 0, Children = { placar, baixo } };
        }
        return Grade("22,Auto,*,Auto", jn, marca, meio, res);
    }

    // os artilheiros (TelaListas.MelhoresMarcadores): o lugar, a bandeira, o nome, a equipa e
    // os golos
    internal static void Marcadores(Dialogos d, string titulo, List<(Jogador P, Equipa E, int Golos)> ms)
    {
        if (CUi.Retro)
        {
            // as colunas do computador, numeradas: o nome (negrito), os golos e a equipa
            Abrir(d, titulo, null, ListaR(ms.Count, i =>
            {
                var (p, e, g) = ms[i];
                var nome = R(TextosMercado.NomeJogador(p), true);
                nome.Margin = new Thickness(8, 0, 0, 0);
                var eq = R(Dlg.Texto(e.NomeCurto));
                eq.Margin = new Thickness(12, 0, 0, 0);
                eq.MaxWidth = 140;
                return Grade("24,*,30,Auto", RD((i + 1).ToString()), nome, RD(g.ToString(), true), eq);
            }), null);
            return;
        }
        var corpo = Pilha(Cabecalho("26,*,Auto,40", "#", T("Jogador"), T("Time"), T("Gols")),
            Grupo(ms.Count, i =>
            {
                var (p, e, g) = ms[i];
                var nome = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                if (CUi.Bandeira(TextosMercado.PaisDe(p.Pais)) is { } b) { b.Margin = new Thickness(0, 0, 6, 0); nome.Children.Add(b); }
                var n = CUi.Txt(TextosMercado.NomeJogador(p), 14.5, null, FontWeight.Medium);
                Grid.SetColumn(n, 1);
                nome.Children.Add(n);
                var eq = Eq(e, 12.5);
                eq.MaxWidth = 130;
                eq.Margin = new Thickness(8, 0, 0, 0);
                return Grade("26,*,Auto,40", Num((i + 1).ToString(), 13, CUi.Fraco, true), Margem(nome, 8), eq, Num(g.ToString(), 16, Forte, true));
            }));
        Abrir(d, titulo, null, corpo, null);
    }

    internal static Control Margem(Control c, double esq) { c.Margin = new Thickness(esq, 0, 0, 0); return c; }

    // os ultimos vencedores: o ano e, embaixo um do outro, o campeao (a taca dourada) e o
    // vencedor da Copa (a prateada), cada um com o treinador embaixo (em negrito o humano)
    internal static void UltimosVencedores(Dialogos d, Sessao s, IReadOnlyList<ushort[]> cs)
    {
        if (CUi.Retro)
        {
            // TLastWinners.Formatar em duas linhas: o ano, o campeao e o treinador; embaixo o
            // vencedor da Copa e o treinador dele (negrito o humano)
            Control Par(string ano, ushort eq, ushort tr)
            {
                var t = s.J.M.Treinador(tr);
                var e = R(s.Equipa(eq) is { } x ? Dlg.Texto(x.NomeCurto) : "");
                e.Margin = new Thickness(0, 0, 8, 0);
                return Grade("44,*,*", R(ano), e, R(t != null ? Dlg.Texto(t.Nome) : "", t != null && t.Humano != 0));
            }
            // (o titulo do computador nao cabe na barra: o curto e o resto embaixo)
            Abrir(d, T("Últimos campeões"), T("Campeões do campeonato e da copa"), ListaR(cs.Count, i =>
            {
                var w = cs[i];
                return new StackPanel { Margin = new Thickness(0, 2), Children = { Par(w[0].ToString(), w[1], w[2]), Par("", w[3], w[4]) } };
            }), null, fim: true);
            return;
        }
        Control Sub(bool copa, ushort eq, ushort tr)
        {
            var t = s.J.M.Treinador(tr);
            var nt = CUi.Txt(t != null ? Dlg.Texto(t.Nome) : "", 12.5, CUi.Fraco, t != null && t.Humano != 0 ? FontWeight.Bold : FontWeight.Normal);
            var p = new StackPanel { Spacing = 1, Children = { Eq(s.Equipa(eq), 14), nt } };
            var ic = Taca(20, copa);
            ic.Margin = new Thickness(0, 2, 8, 0);
            ic.VerticalAlignment = VerticalAlignment.Top;
            var g = Grade("Auto,*", ic, p);
            ic.VerticalAlignment = VerticalAlignment.Top;
            return g;
        }
        var corpo = Pilha(Legenda(), Grupo(cs.Count, i =>
        {
            var w = cs[i];
            var ano = CUi.Txt(w[0].ToString(), 16, Forte, FontWeight.Bold);
            ano.VerticalAlignment = VerticalAlignment.Top;
            var p = new StackPanel { Spacing = 8, Margin = new Thickness(0, 4), Children = { Sub(false, w[1], w[2]), Sub(true, w[3], w[4]) } };
            var g = Grade("54,*", ano, p);
            ano.VerticalAlignment = VerticalAlignment.Top;
            ano.Margin = new Thickness(0, 6, 0, 0);
            return g;
        }, null, 60));
        Abrir(d, T("Últimos campeões"), T("Campeões do campeonato e da copa"), corpo, null, fim: true);
    }

    // as duas tacas (campeonato e copa) e o que querem dizer, sobre o fundo
    static Control Legenda()
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(4, 0, 0, 0) };
        foreach (var (copa, nome) in new[] { (false, T("Campeonato")), (true, T("Copa")) })
        {
            p.Children.Add(Taca(16, copa, true));
            var t = CUi.Txt(nome, 12.5, CUi.TituloSecao, FontWeight.Bold);
            t.Margin = new Thickness(0, 0, 10, 0);
            p.Children.Add(t);
        }
        return p;
    }

    // as ultimas transferencias, em duas linhas: a posicao, a bandeira, o nome (vermelho na
    // compra directa) e a forca na hora; embaixo a equipa compradora e o preco
    internal static void UltimasTransferencias(Dialogos d, Sessao s, List<Transferencia> tr, Action<int>? toque)
    {
        Action Tirar = () => { };
        if (CUi.Retro)
        {
            // TLastTransfer.Formatar em duas linhas: a posicao, o nome, o pais e a forca na hora;
            // embaixo o preco e a equipa compradora; a compra directa em vermelho; o ultimo
            // escolhido
            var lista = ListaR(tr.Count, i =>
            {
                var t = tr[i];
                var (eq, k) = s.J.M.EquipaDoJogador(t.W06);
                IBrush? cor = t.B04 != 0 ? W95.Vermelho : null;
                Control cima;
                if (eq != null)
                {
                    var p = eq.Jogadores[k];
                    cima = Grade("20,*,40,28", R(Idioma.Posicao(Math.Min((int)p.Posicao, 3)), false, cor), R(TextosMercado.NomeJogador(p), false, cor),
                        R(Dlg.Texto(p.Pais), false, cor), RD(((short)t.W08).ToString(), false, cor));
                }
                else cima = R(" ");
                var c = R(s.Equipa(t.W0A) is { } x ? Dlg.Texto(x.NomeCurto) : "", false, cor);
                c.Margin = new Thickness(12, 0, 0, 0);
                var baixo = Grade("20,112,*", null, RD(FormatoDinheiro.Valor(s.S, (int)t.D0C), false, cor), c);
                return new StackPanel { Margin = new Thickness(0, 2), Children = { cima, baixo } };
            }, toque == null ? null : i => () => { Tirar(); toque(i); }, tr.Count - 1);
            (_, _, Tirar) = Abrir(d, T("Últimas transferências"), null, lista, null, fim: true);
            return;
        }
        var corpo = Grupo(tr.Count, i =>
        {
            var t = tr[i];
            var (eq, k) = s.J.M.EquipaDoJogador(t.W06);
            IBrush? cor = t.B04 != 0 ? CUi.Erro : null;
            Control cima;
            if (eq != null)
            {
                var p = eq.Jogadores[k];
                var pos = CUi.Txt(Idioma.Posicao(Math.Min((int)p.Posicao, 3)), 12.5, Forte, FontWeight.Bold);
                var nome = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                if (CUi.Bandeira(TextosMercado.PaisDe(p.Pais)) is { } b) { b.Margin = new Thickness(0, 0, 6, 0); nome.Children.Add(b); }
                var n = CUi.Txt(TextosMercado.NomeJogador(p), 15, cor, FontWeight.SemiBold);
                Grid.SetColumn(n, 1);
                nome.Children.Add(n);
                cima = Grade("22,*,Auto", pos, nome, Num(((short)t.W08).ToString(), 16, Forte, true));
            }
            else cima = CUi.Txt("—", 15, CUi.Fraco);
            var seta = CUi.Txt("→", 13, CUi.Fraco);
            var preco = Num(FormatoDinheiro.Valor(s.S, (int)t.D0C), 14, cor, true);
            preco.Margin = new Thickness(8, 0, 0, 0);
            var baixo = Grade("22,*,Auto", seta, Eq(s.Equipa(t.W0A), 13), preco);
            return new StackPanel { Spacing = 4, Children = { cima, baixo } };
        }, toque == null ? null : i => () => { Tirar(); toque(i); }, 60);
        (_, _, Tirar) = Abrir(d, T("Últimas transferências"), null, corpo, null, fim: true);
    }

    // ---------------------------------------------------------------- Time e Jogador
    // o historico da equipa: o ano, o titulo e o treinador (em negrito o humano)
    internal static void HistoricoEquipa(Dialogos d, Sessao s, Equipa e, List<EventoEquipa> ev, Action? fechou)
    {
        if (CUi.Retro)
        {
            // TTeamEvent.Formatar: o ano, o titulo e o treinador (em negrito se humano); o ultimo
            // escolhido
            Abrir(d, Dlg.Texto(e.NomeCompleto), null, ListaR(ev.Count, i =>
            {
                var v = ev[i];
                var t = s.J.M.Treinador(v.W09);
                bool h = t != null && t.Humano != 0;
                string tit = v.B04 switch { 1 => T("Campeonato"), 2 => T("Copa"), 3 => T("DOBRADINHA"), 4 => T("2º lugar"), 5 => T("3º lugar"), _ => "" };
                return Grade("48,Auto,*", R(((int)v.D05).ToString()), Margem(R(tit, h), 0), Margem(R(t != null ? Dlg.Texto(t.Nome) : "", h), 12));
            }, sel: ev.Count - 1), fechou, fim: true);
            return;
        }
        var corpo = Grupo(ev.Count, i =>
        {
            var v = ev[i];
            var t = s.J.M.Treinador(v.W09);
            bool humano = t != null && t.Humano != 0;
            string tit = v.B04 switch { 1 => T("Campeonato"), 2 => T("Copa"), 3 => T("DOBRADINHA"), 4 => T("2º lugar"), 5 => T("3º lugar"), _ => "" };
            var nt = CUi.Txt(t != null ? Dlg.Texto(t.Nome) : "", 13, humano ? null : CUi.Fraco, humano ? FontWeight.Bold : FontWeight.Normal);
            nt.MaxWidth = 170;
            nt.Margin = new Thickness(8, 0, 0, 0);
            return Grade("54,*,Auto", CUi.Txt(((int)v.D05).ToString(), 15, Forte, FontWeight.Bold),
                CUi.Txt(tit, 14.5, null, v.B04 is 1 or 2 or 3 ? FontWeight.Bold : FontWeight.Medium), nt);
        });
        Abrir(d, Dlg.Texto(e.NomeCompleto), null, corpo, fechou, fim: true);
    }

    // os jogadores adquiridos (as compras que ainda nao chegaram): a linha do elenco com a
    // equipa de onde vem
    internal static void Contratacoes(Dialogos d, List<(Jogador P, Equipa E)> ps, Action? fechou)
    {
        if (CUi.Retro)
        {
            // TPlayer.Formatar com as colunas 0x4063: a posicao, o nome (negrito), a equipa, a
            // forca e o pais
            Abrir(d, T("Jogadores adquiridos"), null, ListaR(ps.Count, i =>
            {
                var (p, e) = ps[i];
                var eq = R(Dlg.Texto(e.NomeCurto));
                eq.MaxWidth = 130;
                eq.Margin = new Thickness(8, 0, 0, 0);
                return Grade("20,*,Auto,30,40", R(Idioma.Posicao(Math.Min((int)p.Posicao, 3))), R(TextosMercado.NomeJogador(p), true), eq,
                    RD(p.Forca.ToString()), Margem(R(Dlg.Texto(p.Pais)), 8));
            }), fechou);
            return;
        }
        var corpo = Grupo(ps.Count, i =>
        {
            var eq = Eq(ps[i].E, 12.5);
            eq.MaxWidth = 120;
            return CUi.LinhaJogador(ps[i].P, extra: eq, comp: false);
        });
        Abrir(d, T("Jogadores adquiridos"), null, corpo, fechou);
    }

    // ---------------------------------------------------------------- Tecnico
    // a historia do treinador: o ano e o texto do evento, com a equipa (os titulos em negrito)
    internal static void HistoriaTreinador(Dialogos d, Sessao s, Treinador t, List<EventoTreinador> ev)
    {
        if (CUi.Retro)
        {
            // TCoachEvent.Formatar: o ano e o texto do evento com o nome da equipa (os titulos
            // em negrito); o ultimo escolhido
            Abrir(d, Dlg.Texto(t.Nome), null, ListaR(ev.Count, i =>
            {
                var v = ev[i];
                string eq = s.Equipa(v.W09) is { } e ? Dlg.Texto(e.NomeCurto) : "";
                string txt = v.B04 switch
                {
                    1 => T("Contratado pelo ") + eq, 2 => T("Despedido"), 3 => T("VENCEDOR DO CAMPEONATO"), 4 => T("VENCEDOR DA COPA"),
                    5 => T("Entrada no ") + eq, 6 => T("Rebaixado para o ") + eq, 7 => T("Promovido para o ") + eq, 8 => T("DOBRADINHA"),
                    9 => T("2º lugar"), 10 => T("3º lugar"), _ => "",
                };
                var x = R(txt, v.B04 is 3 or 4 or 8);
                x.TextWrapping = TextWrapping.Wrap;
                return Grade("48,*", R(((int)v.D05).ToString()), x);
            }, sel: ev.Count - 1), null, fim: true);
            return;
        }
        var corpo = Grupo(ev.Count, i =>
        {
            var v = ev[i];
            var e = s.Equipa(v.W09);
            bool titulo = v.B04 is 3 or 4 or 8;
            string txt = v.B04 switch
            {
                1 => T("Contratado pelo "), 2 => T("Despedido"), 3 => T("VENCEDOR DO CAMPEONATO"), 4 => T("VENCEDOR DA COPA"),
                5 => T("Entrada no "), 6 => T("Rebaixado para o "), 7 => T("Promovido para o "), 8 => T("DOBRADINHA"),
                9 => T("2º lugar"), 10 => T("3º lugar"), _ => "",
            };
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            var tx = CUi.Txt(txt.TrimEnd(), 14, v.B04 == 6 ? CUi.Erro : v.B04 == 7 ? CUi.Bom : null, titulo ? FontWeight.Bold : FontWeight.Normal);
            g.Children.Add(tx);
            if (v.B04 is 1 or 5 or 6 or 7)
            {
                var eq = Eq(e, 13.5);
                eq.Margin = new Thickness(6, 0, 0, 0);
                Grid.SetColumn(eq, 1);
                g.Children.Add(eq);
            }
            return Grade("54,*", CUi.Txt(((int)v.D05).ToString(), 15, Forte, FontWeight.Bold), g);
        });
        Abrir(d, Dlg.Texto(t.Nome), null, corpo, null, fim: true);
    }

    // as chicotadas depois da jornada: a folha de baixo com o OK; o escudo da equipa e o texto
    // da TLash (em negrito o humano)
    internal static void Chicotadas(Dialogos d, Sessao s, IReadOnlyList<Chicotada> l, Action? fechou)
    {
        var linhas = new List<(Equipa? E, string Texto, bool Humano)>();
        foreach (var c in l)
        {
            var t = s.J.M.Treinador(c.Treinador);
            var e = s.Equipa(c.Equipa);
            string nome = t != null ? Dlg.Texto(t.Nome) : "", eq = e != null ? TelaDinheiro.Texto(e.NomeCurto) : "";
            string txt;
            if (c.Tipo != 0) txt = nome + T(" foi demitido do ") + eq;
            else if (t == null) continue;
            else if (e == null) txt = T("Para o seu lugar foi contratado ") + nome;
            else txt = T("Troca com ") + nome + " (" + eq + ")";
            linhas.Add((e, txt, t != null && t.Humano != 0));
        }
        if (CUi.Retro)
        {
            // a TLash do computador: uma coluna so, negrito o humano
            Abrir(d, T("Demissões de Técnicos"), null, ListaR(linhas.Count, i =>
            {
                var x = R(linhas[i].Texto, linhas[i].Humano);
                x.TextWrapping = TextWrapping.Wrap;
                return x;
            }), fechou, ok: true);
            return;
        }
        var corpo = Grupo(linhas.Count, i =>
        {
            var (e, txt, h) = linhas[i];
            var esc = CUi.Escudo(e, 28);
            esc.Margin = new Thickness(0, 0, 10, 0);
            return Grade("Auto,*", e != null ? esc : null, CUi.Txt(txt, 14.5, null, h ? FontWeight.Bold : FontWeight.Normal, true));
        });
        Abrir(d, T("Demissões de Técnicos"), null, corpo, fechou, ok: true);
    }

    // o ranking dos treinadores (ja ordenado): o lugar, o nome (negrito o humano) e a equipa
    // embaixo, os titulos (campeonatos + copas e a soma) e, com o add-in, os pontos
    internal static void RankingTreinadores(Dialogos d, Sessao s, List<Treinador> tr, bool pontos)
    {
        if (CUi.Retro)
        {
            // TCoach.Formatar (0x2B, e os pontos com o add-in) em duas linhas: o numero, o nome
            // (negrito o humano), "campeonatos + copas = soma" e os pontos; embaixo a equipa
            string cr = pontos ? "28,*,Auto,40" : "28,*,Auto";
            Abrir(d, T("Ranking de Treinadores"), null, ListaR(tr.Count, i =>
            {
                var t = tr[i];
                short c = (short)t.D16, ta = (short)(t.D16 >> 16);
                var nome = Margem(R(Dlg.Texto(t.Nome), t.Humano != 0), 8);
                var tit = Margem(RD(c + " + " + ta + " = " + (c + ta)), 8);
                var cima = pontos ? Grade(cr, RD((i + 1).ToString()), nome, tit, RD(((int)t.D0E).ToString()))
                    : Grade(cr, RD((i + 1).ToString()), nome, tit);
                var eq = Margem(R(s.J.M.EquipaDoTreinador(t.ID) is { } e ? Dlg.Texto(e.NomeCurto) : ""), 8);
                return new StackPanel { Margin = new Thickness(0, 2), Children = { cima, Grade("28,*", null, eq) } };
            }), null);
            return;
        }
        string cols = pontos ? "34,*,Auto,52" : "34,*,Auto";
        var cab = pontos ? Cabecalho(cols, "#", T("Treinador"), T("Títulos"), T("Pontos")) : Cabecalho(cols, "#", T("Treinador"), T("Títulos"));
        var corpo = Pilha(cab, Grupo(tr.Count, i =>
        {
            var t = tr[i];
            var nome = CUi.Txt(Dlg.Texto(t.Nome), 15, null, t.Humano != 0 ? FontWeight.Bold : FontWeight.Medium);
            var eq = Eq(s.J.M.EquipaDoTreinador(t.ID), 12.5);
            var meio = new StackPanel { Spacing = 3, Children = { nome, eq }, Margin = new Thickness(4, 0, 0, 0) };
            short c = (short)t.D16, ta = (short)(t.D16 >> 16);
            var tit = new StackPanel { Spacing = 0, Margin = new Thickness(8, 0, 0, 0), Children = { Num((c + ta).ToString(), 16, Forte, true), Num(c + " + " + ta, 11.5, CUi.Fraco) } };
            return pontos
                ? Grade(cols, Num((i + 1).ToString(), 13, CUi.Fraco, true), meio, tit, Num(((int)t.D0E).ToString(), 15, null, true))
                : Grade(cols, Num((i + 1).ToString(), 13, CUi.Fraco, true), meio, tit);
        }, null, 56));
        Abrir(d, T("Ranking de Treinadores"), null, corpo, null);
    }

    // ---------------------------------------------------------------- Titulos
    // os titulos: as abas dos ultimos campeoes (por ano), do total (o que o classico mostra:
    // campeonato, copa e a soma) e do ranking de cada competicao. O Retro abre no total (a lista
    // do classico); o Moderno nos ultimos campeoes (como no computador).
    internal static void Titulos(Dialogos d, List<TelaTitulos.Ano> anos)
    {
        if (CUi.Retro)
        {
            // o classico: a lista so com as duas contagens (equipa, Camp., Copa e Total), do
            // maior para o menor, como no computador
            var ids = anos.SelectMany(a => new[] { a.Campeao, a.Taca }).Where(e => e != null).Select(e => e!).DistinctBy(e => e.ID).ToList();
            var ls = ids.Select(e => (E: e, C: anos.Count(a => a.Campeao?.ID == e.ID), T: anos.Count(a => a.Taca?.ID == e.ID)))
                .OrderByDescending(x => x.C + x.T).ThenByDescending(x => x.C).ToList();
            Abrir(d, T("Títulos: campeonato e copa"), null, ListaR(ls.Count, i => Grade("*,Auto,Auto,Auto",
                R(Dlg.Texto(ls[i].E.NomeCurto)), Margem(R(T("Camp.") + " " + ls[i].C), 10), Margem(R(T("Copa") + " " + ls[i].T), 12),
                Margem(R(T("Total") + " " + (ls[i].C + ls[i].T)), 12))), null);
            return;
        }
        var area = new ContentControl();
        var abas = new[] { T("Últimos campeões"), T("Total"), T("Campeonato"), T("Copa") };
        Control Pagina(int k)
        {
            if (anos.Count == 0) return CUi.Txt(T("Ainda não há campeões: os títulos aparecem no fim da primeira temporada."), 14, CUi.Retro ? W95.Branco : CUi.Fraco, FontWeight.Normal, true);
            switch (k)
            {
                case 0:
                    return Pilha(Cabecalho("54,*,*", T("Ano"), T("Campeonato"), T("Copa")), Grupo(anos.Count, i =>
                    {
                        var a = anos[i];
                        var c1 = Eq(a.Campeao, 13.5); c1.Margin = new Thickness(0, 0, 6, 0);
                        return Grade("54,*,*", CUi.Txt(a.Valor.ToString(), 15, Forte, FontWeight.Bold), c1, Eq(a.Taca, 13.5));
                    }));
                case 1:
                {
                    var ids = anos.SelectMany(a => new[] { a.Campeao, a.Taca }).Where(e => e != null).Select(e => e!).DistinctBy(e => e.ID).ToList();
                    var ls = ids.Select(e => (E: e, C: anos.Count(a => a.Campeao?.ID == e.ID), T: anos.Count(a => a.Taca?.ID == e.ID)))
                        .OrderByDescending(x => x.C + x.T).ThenByDescending(x => x.C).ToList();
                    const string cols = "*,52,52,52";
                    return Pilha(Cabecalho(cols, T("Time"), T("Camp."), T("Copa"), T("Total")), Grupo(ls.Count, i =>
                        Grade(cols, Eq(ls[i].E, 14), Num(ls[i].C.ToString(), 15), Num(ls[i].T.ToString(), 15), Num((ls[i].C + ls[i].T).ToString(), 16, Forte, true))));
                }
                default:
                {
                    var r = TelaTitulos.Ranking(anos, k == 2 ? a => a.Campeao : a => a.Taca);
                    return Grupo(r.Count, i =>
                    {
                        var tacas = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, Margin = new Thickness(8, 0) };
                        for (int x = 0; x < Math.Min(r[i].N, 5); x++) tacas.Children.Add(Taca(18, k == 3));
                        return Grade("*,Auto,36", Eq(r[i].E, 14), tacas, Num(r[i].N.ToString(), 17, Forte, true));
                    });
                }
            }
        }
        int inicial = CUi.Retro ? 1 : 0;
        var (fila, _) = CUi.Abas(abas, k => area.Content = Pagina(k), inicial);
        fila.Margin = new Thickness(-14, -6, -14, 0);
        area.Content = Pagina(inicial);
        var corpo = new StackPanel { Spacing = 8, Children = { fila, area } };
        Abrir(d, T("Títulos"), anos.Count == 0 ? null : T("Campeões do campeonato e da copa"), corpo, null);
    }

    // a taca (o "trophy" do Lucide, ISC), na cor do destaque
    // (copa: a prateada; sobreFundo: no Retro, sobre o verde, a dourada fica amarela)
    internal static Control Taca(double tam, bool copa = false, bool sobreFundo = false) => new Path
    {
        Data = Geometry.Parse("M6 9H4.5a2.5 2.5 0 0 1 0-5H6M18 9h1.5a2.5 2.5 0 0 0 0-5H18M4 22h16M10 14.66V17c0 .55-.47.98-.97 1.21C7.85 18.75 7 20.24 7 22M14 14.66V17c0 .55.47.98.97 1.21C16.15 18.75 17 20.24 17 22M18 2H6v7a6 6 0 0 0 12 0V2Z"),
        Stroke = copa ? (CUi.Retro ? (sobreFundo ? W95.Branco : W95.Cinzento) : TemaVerde.Prata)
            : CUi.Retro ? (sobreFundo ? W95.Amarelo : W95.Marinho) : TemaVerde.Ouro,
        StrokeThickness = 1.8, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
        Width = tam, Height = tam, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
    };
}
