using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Layout;
using Avalonia.Media;
using RetroFoot.Motor.Mercado;
using Mem = RetroFoot.Motor.Partida.Memoria;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;
using TM = RetroFoot.App.TextosMercado;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// As pecas comuns das janelas do mercado no celular (leilao, vender, salario, plantel, lista
// de jogadores, observados), por cima do kit (CelularUi.cs): a ficha do jogador, o campo de
// numero com o menos e o mais, os selos e a folha que abre por cima da outra. As regras ficam
// nas janelas do computador; isto so desenha.
static class MercadoCelular
{
    // ------------------------------------------------------------ folhas por cima
    // abre uma folha por cima do que estiver aberto; devolve o que a fecha
    public static Action Abrir(Sessao s, CUi.Folha f)
    {
        bool fechada = false;
        void Fechar() { if (fechada) return; fechada = true; s.FecharDialogo(f); }
        f.AoVoltar ??= Fechar;
        s.AbrirDialogo(f);
        return Fechar;
    }

    // ------------------------------------------------------------ numeros
    // o campo de numero: o menos, a caixa (o teclado numerico do telefone) e o mais
    public static (Control Campo, TextBox Caixa) Numero(string texto, int max, Action<int> passo, char senha = '\0')
    {
        var caixa = CUi.Caixa(texto, max);
        caixa.FontSize = 20;
        caixa.FontWeight = FontWeight.Bold;
        caixa.HorizontalContentAlignment = HorizontalAlignment.Center;
        caixa.MinHeight = 48;
        if (senha != '\0') caixa.PasswordChar = senha;
        TextInputOptions.SetContentType(caixa, TextInputContentType.Number);
        var menos = new CUi.Botao("−", false, () => passo(-1)) { Padding = new Thickness(0), MinHeight = 48, MinWidth = 56 };
        var mais = new CUi.Botao("+", false, () => passo(1)) { Padding = new Thickness(0), MinHeight = 48, MinWidth = 56 };
        foreach (var b in new[] { menos, mais }) if (b.Child is TextBlock tb) tb.FontSize = 22 * Acessibilidade.Texto;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("56,10,*,10,56") };
        menos.HorizontalAlignment = mais.HorizontalAlignment = HorizontalAlignment.Stretch;
        g.Children.Add(menos);
        Grid.SetColumn(caixa, 2); g.Children.Add(caixa);
        Grid.SetColumn(mais, 4); g.Children.Add(mais);
        return (g, caixa);
    }

    // o numero escrito na caixa (o Val do jogo: so digitos); null = vazio ou lixo
    public static int? Valor(TextBox t) => TM.Val(t.Text ?? "", out int v) ? v : null;

    // ------------------------------------------------------------ jogadores
    static IBrush Hex(string s) => new ImmutableSolidColorBrush(Color.Parse(s));

    // um selo pequeno ao lado do nome (suspenso, lesionado, o preco...)
    public static Control Selo(string t, IBrush fundo, IBrush letra) => new Border
    {
        Background = fundo, CornerRadius = new CornerRadius(CUi.Retro ? 0 : 5), Padding = new Thickness(4, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = t, FontFamily = CUi.Fonte, FontSize = 11.5 * Acessibilidade.Texto, FontWeight = FontWeight.Bold, Foreground = letra,
            Tag = "fixo", VerticalAlignment = VerticalAlignment.Center,
        },
    };

    // os selos de suspensao e lesao (as colunas S e L das listas do computador)
    public static List<Control> Selos(SJogador j)
    {
        var l = new List<Control>();
        if (j.Suspensao > 0) l.Add(Selo("⊘" + j.Suspensao, Hex("#FFE4E1"), Hex("#C62828")));
        if (j.JogosLesao > 0) l.Add(Selo("✚" + j.JogosLesao, Brushes.White, Hex("#D32F2F")));
        return l;
    }

    // a coluna da direita das listas (antes da forca): o preco em cima e a equipa embaixo
    public static Control Extra(string? preco, string? embaixo)
    {
        var s = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 130 };
        if (!string.IsNullOrEmpty(preco))
        {
            var t = CUi.Txt(preco, 13, null, FontWeight.SemiBold);
            t.HorizontalAlignment = HorizontalAlignment.Right;
            s.Children.Add(t);
        }
        if (!string.IsNullOrEmpty(embaixo))
        {
            var t = CUi.Txt(embaixo, 11.5, CUi.Fraco);
            t.HorizontalAlignment = HorizontalAlignment.Right;
            s.Children.Add(t);
        }
        return s;
    }

    // a linha do jogador do kit (CUi.LinhaJogador) com a coluna extra: o nome nao passa por
    // cima dela (o nome longo termina em "...")
    public static Control Jogador(SJogador j, IEnumerable<Control>? selos, Control? extra)
    {
        var g = CUi.LinhaJogador(j, selos, extra);
        if (extra != null && g is Grid gr)
            foreach (var c in gr.Children)
                if (Grid.GetColumn(c) == 1 && c is StackPanel meio)
                {
                    meio.ClipToBounds = true;
                    foreach (var t in meio.Children)
                        if (t is TextBlock nome) { nome.MaxWidth = 150; break; }
                }
        return g;
    }

    // a forca em destaque (a ficha)
    public static Control Forca(SJogador p, double tam = 24)
    {
        var t = CUi.Txt(p.Forca.ToString(), tam, CUi.Retro ? W95.Branco : TemaVerde.SobreDestaque, FontWeight.Bold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        var s = new StackPanel { Spacing = 0, Children = { t } };
        var r = CUi.Txt(T("Força").ToUpperInvariant(), 9.5, CUi.Retro ? W95.Branco : TemaVerde.SobreDestaque, FontWeight.Bold);
        r.HorizontalAlignment = HorizontalAlignment.Center;
        s.Children.Add(r);
        return new Border
        {
            Background = CUi.Retro ? W95.Marinho : TemaVerde.Destaque, CornerRadius = new CornerRadius(CUi.Retro ? 0 : 12),
            BorderBrush = CUi.Retro ? W95.Preto : null, BorderThickness = new Thickness(CUi.Retro ? 1 : 0),
            Padding = new Thickness(10, 4), MinWidth = 64, VerticalAlignment = VerticalAlignment.Center, Child = s,
        };
    }

    // o cabecalho da ficha: a bandeira, o nome, a posicao (e o pais) e a forca a direita
    public static Control Cabecalho(SJogador p, SEquipa? equipa, string? nomeEquipa = null)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        string pais = TM.PaisDe(p.Pais);
        if (W95.Imagem($"Bandeiras/{pais}.png") is { } b)
            g.Children.Add(new Image { Source = b, Width = 44, Height = 44, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        var meio = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        meio.Children.Add(CUi.Txt(TM.NomeJogador(p), 20, null, FontWeight.Bold));
        meio.Children.Add(CUi.Txt(TM.Posicao(p) + " · " + TM.NomePais(pais), 13.5, CUi.Fraco, FontWeight.Normal, true));
        if (equipa != null) meio.Children.Add(CUi.NomeEquipa(equipa, nomeEquipa ?? TM.NomeEquipa(equipa), 13.5, true));
        Grid.SetColumn(meio, 1); g.Children.Add(meio);
        var f = Forca(p);
        f.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(f, 2); g.Children.Add(f);
        return g;
    }

    // a ficha (2:3002, 10:1fa7): o comportamento, a nota e o risco (launcher), os gols da
    // temporada, o historico, o suspenso e o lesionado; antes, as linhas que quem chama der
    public static Control Ficha(SJogador p, bool launcher, IEnumerable<(string Rotulo, string Valor)>? antes = null)
    {
        var s = new StackPanel { Spacing = 10 };
        if (p.Suspensao > 0 || p.JogosLesao > 0)
        {
            var avisos = new StackPanel { Spacing = 2 };
            if (p.Suspensao > 0)
                avisos.Children.Add(CUi.Txt(p.Suspensao == 1 ? T("Suspenso por 1 jogo") : T("Suspenso por {0} jogos", p.Suspensao), 14, CUi.Erro, FontWeight.Bold));
            if (p.JogosLesao > 0)
                avisos.Children.Add(CUi.Txt(p.JogosLesao == 1 ? T("Lesionado por 1 jogo") : T("Lesionado por {0} jogos", p.JogosLesao), 14, CUi.Erro, FontWeight.Bold));
            s.Children.Add(avisos);
        }
        var dados = new List<Control>();
        if (antes != null) foreach (var (r, v) in antes) dados.Add(Linha(r, v));
        dados.Add(Linha(T("Comportamento"), TM.Comportamento(p)));
        if (launcher)
        {
            dados.Add(Linha(T("Nota"), p.Nota.ToString()));
            dados.Add(Linha(T("Risco de lesão"), p.Lesao.ToString()));
        }
        dados.Add(Linha(T("Gols na Temporada"), Mem.Golos14(p).ToString()));
        s.Children.Add(CUi.Grupo(dados));
        s.Children.Add(CUi.SecaoFolha(T("Histórico")));
        var hist = new List<Control>
        {
            Linha(T("Jogos"), TM.Hist(p, Mem.HistJogos).ToString()),
            Linha(T("Gols"), TM.Hist(p, Mem.HistGolos).ToString()),
        };
        if (launcher) hist.Add(Linha(T("Cartões amarelos"), TM.Amarelos(p).ToString()));
        hist.Add(Linha(T("Cartões vermelhos"), TM.Hist(p, Mem.HistVermelhos).ToString()));
        hist.Add(Linha(T("Lesões"), TM.Hist(p, Mem.HistLesoes).ToString()));
        s.Children.Add(CUi.Grupo(hist));
        return s;
    }

    // uma linha "rotulo ... valor" dentro de um Grupo (com a margem das linhas)
    public static Control Linha(string rotulo, string valor, IBrush? cor = null) =>
        new Border { Padding = new Thickness(12, 7), Child = CUi.Linha_(rotulo, valor, cor) };

    // a barra fina do moral (o TBiGauge: verde acima de 1,25, vermelha abaixo de 0,65)
    public static Control Moral(SEquipa e)
    {
        double m = RetroFoot.Motor.Base.Ext.DeReal48(e.Moral).Float64();
        double f = Math.Clamp(Math.Round(m * 10, MidpointRounding.ToEven) / 20, 0, 1);
        IBrush c = m > 1.25 ? (CUi.Retro ? Hex("#00C000") : TemaVerde.Verde) : m < 0.65 ? CUi.Erro : (CUi.Retro ? W95.Amarelo : TemaVerde.Destaque);
        return new ProgressBar
        {
            Minimum = 0, Maximum = 1, Value = f, Height = 8, MinHeight = 8, MinWidth = 0,
            Foreground = c, Background = CUi.Retro ? W95.Branco : TemaVerde.Alfa(Colors.White, 0.15),
            CornerRadius = new CornerRadius(CUi.Retro ? 0 : 4), VerticalAlignment = VerticalAlignment.Center,
        };
    }

    // o botao na largura toda
    public static CUi.Botao Largo(string texto, bool principal, Action clique)
    {
        var b = new CUi.Botao(texto, principal, clique) { HorizontalAlignment = HorizontalAlignment.Stretch };
        return b;
    }

    // o fundo de um painel nas cores de uma equipa (o lance humano)
    public static (IBrush Letra, IBrush Fundo) Cores(SEquipa e) => TM.Cores(e);
}
