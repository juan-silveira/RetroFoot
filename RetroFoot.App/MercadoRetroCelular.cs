using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;
using TM = RetroFoot.App.TextosMercado;
using Mem = RetroFoot.Motor.Partida.Memoria;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// As pecas do Retro das janelas do mercado no celular, copiadas das janelas do computador
// (TelaLeilao, MercadoForm, TelaPlantel...): os TBitBtn com o glifo em cima, a lista afundada
// numa cor qualquer (a da equipa), o TComboBox, a caixa de marcar, a moldura de grupo na cor
// do formulario e o fundo da folha na cor da janela (amarelo, a cor da equipa). So o Retro;
// o Moderno continua com as pecas do kit.
static class MercadoRetro
{
    static IBrush H(string s) => new ImmutableSolidColorBrush(Color.Parse(s));
    // a selecao das listas do computador (MarcarEscolhido): o azul do titulo e o texto branco
    public static readonly IBrush Selecao = W95.Destaque;
    static readonly BoxShadows Afundado = BoxShadows.Parse("inset 1 1 0 0 #808080, inset 2 2 0 0 #000000, inset -1 -1 0 0 #FFFFFF, inset -2 -2 0 0 #DFDFDF");
    static readonly BoxShadows BotaoBaixo = BoxShadows.Parse("inset 1 1 0 0 #000000, inset -1 -1 0 0 #FFFFFF, inset 2 2 0 0 #808080, inset -2 -2 0 0 #DFDFDF");
    static readonly BoxShadows BotaoSolto = BoxShadows.Parse("inset 1 1 0 0 #FFFFFF, inset -1 -1 0 0 #000000, inset 2 2 0 0 #DFDFDF, inset -2 -2 0 0 #808080");

    // ------------------------------------------------------------ texto
    // o texto das janelas do computador: Arial (negrito nos valores), na cor dada
    public static TextBlock Txt(string s, double tam, IBrush cor, bool negrito = false, bool quebra = false)
    {
        var t = CUi.Txt(s, tam, cor, negrito ? FontWeight.Bold : FontWeight.Normal, quebra);
        // (a cor e a da janela: o realce das listas do kit nao a troca)
        t.Tag = "fixo";
        return t;
    }

    // ------------------------------------------------------------ a folha
    // o fundo da folha (o corpo e o rodape) na cor da janela do computador: o amarelo do
    // leilao e da Lista de Jogadores, a cor da equipa da pagina Vender, o cinza dos observados
    public static void Pintar(CUi.Folha f, IBrush fundo)
    {
        DockPanel? d = null;
        if (f.Content is Border b) { b.Background = fundo; d = b.Child as DockPanel; }
        else d = f.Content as DockPanel;
        if (f.Inteira) f.Background = fundo;
        if (d == null) return;
        foreach (var c in d.Children)
            if (c is Border r && DockPanel.GetDock(r) == Dock.Bottom)
            {
                r.Background = fundo;
                r.BorderBrush = H("#808080");
            }
    }

    // ------------------------------------------------------------ os botoes com glifo
    static readonly Dictionary<(string, int), (IImage?, IImage?)> _glifos = new();

    // o glifo do TBitBtn (Assets/<caminho>): com 2 quadros, o 1o ligado e o 2o desligado
    public static (IImage? Ligado, IImage? Desligado) Glifo(string caminho, int quadros)
    {
        if (_glifos.TryGetValue((caminho, quadros), out var g)) return g;
        var b = W95.Imagem(caminho);
        if (b == null) g = (null, null);
        else if (quadros <= 1) g = (b, null);
        else
        {
            int w = b.PixelSize.Width / quadros, h = b.PixelSize.Height;
            g = (new CroppedBitmap(b, new PixelRect(0, 0, w, h)), new CroppedBitmap(b, new PixelRect(w, 0, w, h)));
        }
        return _glifos[(caminho, quadros)] = g;
    }

    // o TBitBtn do computador ao toque: o glifo em cima (o dobro, sem suavizar) e o texto
    // embaixo, no botao cinza do kit; desligado, o 2o quadro do glifo (ou ele apagado)
    public static CUi.Botao Botao(string texto, string? glifo, int quadros, Action clique, bool padrao = false, double tamGlifo = 32)
    {
        var b = new CUi.Botao(texto, padrao, clique) { Padding = new Thickness(6, 6), MinHeight = 64 };
        if (b.Child is not TextBlock t) return b;
        t.FontSize = 13.5 * Acessibilidade.Texto;
        t.TextAlignment = TextAlignment.Center;
        if (glifo == null) return b;
        var (lig, des) = Glifo(glifo, quadros);
        if (lig == null) return b;
        var img = new Image { Source = lig, Width = tamGlifo, Height = tamGlifo, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.None);
        b.Child = null;
        b.Child = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { img, t } };
        void Estado()
        {
            bool h = b.IsEffectivelyEnabled;
            img.Source = h ? lig : des ?? lig;
            img.Opacity = h || des != null ? 1 : 0.35;
        }
        b.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Input.InputElement.IsEffectivelyEnabledProperty) Estado(); };
        return b;
    }

    // o botao pequeno so com o glifo (os X de limpar da Lista de Jogadores, o pais do time)
    public static CUi.Botao Pequeno(string glifo, int quadros, Action clique)
    {
        var b = new CUi.Botao("", false, clique) { Padding = new Thickness(4), MinHeight = 40, MinWidth = 44, Width = 44 };
        var (lig, des) = Glifo(glifo, quadros);
        if (lig == null) return b;
        var img = new Image { Source = lig, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.None);
        b.Child = img;
        b.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Input.InputElement.IsEffectivelyEnabledProperty) img.Source = b.IsEffectivelyEnabled ? lig : des ?? lig;
        };
        return b;
    }

    // o TSpeedButton premido (o Observado do plantel com o jogador ja observado)
    public static void Baixo(CUi.Botao b, bool baixo)
    {
        b.Solto = baixo ? BotaoBaixo : BotaoSolto;
        b.BoxShadow = baixo ? BotaoBaixo : BotaoSolto;
    }

    // uma fila de botoes com glifo na largura toda
    public static Grid Fila(params Control[] bs)
    {
        // (quatro ou mais na largura do telefone: o texto um pouco menor, para caber inteiro)
        if (bs.Length >= 4)
            foreach (var b in bs)
                if (b is CUi.Botao cb && cb.Child is StackPanel sp)
                    foreach (var x in sp.Children)
                        if (x is TextBlock t) t.FontSize = 12 * Acessibilidade.Texto;
        var g = CUi.Botoes(bs);
        g.ColumnSpacing = 6;
        return g;
    }

    // ------------------------------------------------------------ listas e caixas
    // a TListBox do computador: a caixa afundada (o fio duplo) no fundo dado
    public static Border Lista(Control conteudo, IBrush fundo) => new()
    {
        Background = fundo, Padding = new Thickness(2), BoxShadow = Afundado, ClipToBounds = true, Child = conteudo,
    };

    // o TComboBox (csDropDownList): a caixa branca afundada com o texto e o botao da seta;
    // o toque abre a escolha
    public static Clicavel Combo(Control conteudo, Action abrir, double altura = 40)
    {
        var seta = new Border
        {
            Width = 26, Background = H("#C0C0C0"), BoxShadow = BotaoSolto, Margin = new Thickness(0, 2, 2, 2),
            Child = new TextBlock { Text = "▼", FontSize = 10, Foreground = W95.Preto, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        conteudo.Margin = new Thickness(6, 0, 4, 0);
        conteudo.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(conteudo);
        Grid.SetColumn(seta, 1); g.Children.Add(seta);
        var c = new Clicavel { Background = W95.Branco, BoxShadow = Afundado, MinHeight = altura, Child = g, CorFoco = null };
        c.Clique += abrir;
        return c;
    }

    // a TCheckBox: o quadrado branco afundado com o visto e o texto; o toque na linha troca
    public static Clicavel Marcar(string texto, bool marcado, IBrush cor, Action trocar)
    {
        var q = new Border
        {
            Width = 22, Height = 22, Background = W95.Branco, BoxShadow = Afundado, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = marcado ? "✔" : "", FontSize = 15, FontWeight = FontWeight.Bold, Foreground = W95.Preto,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,10,*"), MinHeight = 44 };
        g.Children.Add(q);
        var t = Txt(texto, 14, cor, false, true);
        Grid.SetColumn(t, 2); g.Children.Add(t);
        var c = new Clicavel { Background = Brushes.Transparent, Child = g, CorFoco = null };
        c.Clique += trocar;
        return c;
    }

    // a TEdit branca afundada (a caixa de texto do kit com o fio duplo do Windows 95)
    public static TextBox Edit(string texto, int max, double tam = 17, bool negrito = true)
    {
        var t = CUi.Caixa(texto, max);
        t.FontSize = tam;
        t.FontWeight = negrito ? FontWeight.Bold : FontWeight.Normal;
        t.MinHeight = 44;
        return t;
    }

    // a moldura de grupo (TGroupBox) na cor do formulario: o fio gravado e o titulo por cima
    public static Control Moldura(string titulo, Control conteudo, IBrush fundo, IBrush cor)
    {
        var moldura = new Border
        {
            Margin = new Thickness(0, 9, 0, 0), Padding = new Thickness(10, 14, 10, 8),
            BorderBrush = H("#A6A6A6"), BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("inset 1 1 0 0 #FFFFFF"), Child = conteudo,
        };
        var rot = new Border
        {
            Background = fundo, Padding = new Thickness(3, 0), Margin = new Thickness(8, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Child = Txt(titulo, 13, cor),
        };
        return new Panel { Children = { moldura, rot } };
    }

    // uma linha "rotulo  valor" das fichas do computador (o rotulo normal, o valor em negrito);
    // largura = a coluna do rotulo (0: o valor a direita)
    public static Control Linha(string rotulo, string valor, IBrush cor, double largura = 0, double tam = 14, bool negrito = true)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(largura > 0 ? $"{largura},*" : "*,Auto"), MinHeight = 26 };
        g.Children.Add(Txt(rotulo, tam, cor));
        var v = Txt(valor, tam, cor, negrito);
        if (largura <= 0) v.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(v, 1); g.Children.Add(v);
        return g;
    }

    // o medidor do moral (MedidorMoral do computador): verde acima de 1,25, vermelho abaixo
    // de 0,65, amarelo no meio
    public static Control Moral(SEquipa e, double altura = 16)
    {
        double m = RetroFoot.Motor.Base.Ext.DeReal48(e.Moral).Float64();
        IBrush cor = m > 1.25 ? W95.Cor(0, 255, 0) : m < 0.65 ? W95.Vermelho : W95.Amarelo;
        double f = Math.Clamp(Math.Round(m * 10, MidpointRounding.ToEven) / 20, 0, 1);
        return new Border
        {
            Background = W95.Branco, BoxShadow = Afundado, Padding = new Thickness(4), Height = altura + 8,
            Child = new ProgressBar
            {
                Minimum = 0, Maximum = 1, Value = f, MinHeight = altura, Height = altura, MinWidth = 0,
                Foreground = cor, Background = W95.Branco, CornerRadius = new CornerRadius(0),
            },
        };
    }

    // ------------------------------------------------------------ a ficha do jogador
    // Panel1 da janela do leilao e a ficha do plantel: o Comportamento (a Nota e o Risco no
    // launcher), os gols da temporada e o Historico na moldura de grupo, na cor da janela
    public static Control Ficha(SJogador p, bool launcher, IBrush fundo, IBrush cor)
    {
        var s = new StackPanel { Spacing = 0 };
        s.Children.Add(Linha(T("Comportamento"), TM.Comportamento(p), cor, 170, 14, false));
        if (launcher)
        {
            s.Children.Add(Linha(T("Nota"), p.Nota.ToString(), cor, 170, 14, false));
            s.Children.Add(Linha(T("Risco de lesão"), p.Lesao.ToString(), cor, 170, 14, false));
        }
        s.Children.Add(Linha(T("Gols na Temporada"), Mem.Golos14(p).ToString(), cor, 170, 14, false));
        var h = new StackPanel { Spacing = 0 };
        h.Children.Add(Linha(T("Jogos"), TM.Hist(p, Mem.HistJogos).ToString(), cor, 0, 14, false));
        h.Children.Add(Linha(T("Gols"), TM.Hist(p, Mem.HistGolos).ToString(), cor, 0, 14, false));
        if (launcher) h.Children.Add(Linha(T("Cartões amarelos"), TM.Amarelos(p).ToString(), cor, 0, 14, false));
        h.Children.Add(Linha(T("Cartões vermelhos"), TM.Hist(p, Mem.HistVermelhos).ToString(), cor, 0, 14, false));
        h.Children.Add(Linha(T("Lesões"), TM.Hist(p, Mem.HistLesoes).ToString(), cor, 0, 14, false));
        var m = Moldura(T("Histórico"), h, fundo, cor);
        m.Margin = new Thickness(0, 4, 0, 0);
        s.Children.Add(m);
        return s;
    }

    // "Suspenso por n jogos" / "Lesionado por n jogos" (o vermelho do leilao, o preto do plantel)
    public static void Avisos(Panel s, SJogador p, IBrush cor)
    {
        if (p.Suspensao > 0)
            s.Children.Add(Txt(p.Suspensao == 1 ? T("Suspenso por 1 jogo") : T("Suspenso por {0} jogos", p.Suspensao), 14, cor, true, true));
        if (p.JogosLesao > 0)
            s.Children.Add(Txt(p.JogosLesao == 1 ? T("Lesionado por 1 jogo") : T("Lesionado por {0} jogos", p.JogosLesao), 14, cor, true, true));
    }

    // ------------------------------------------------------------ as linhas das listas
    // uma linha de lista que se toca, com o realce do computador (azul, o texto branco)
    public static Clicavel LinhaLista(Control conteudo, bool escolhida, Action toque, double altura = 34)
    {
        var c = new Clicavel
        {
            Background = escolhida ? Selecao : Brushes.Transparent, Child = conteudo, MinHeight = altura,
            Padding = new Thickness(4, 2), CorFoco = null,
        };
        c.Clique += toque;
        return c;
    }

    // uma celula de coluna (Colunas do computador): o texto cortado, a esquerda ou a direita
    public static TextBlock Col(Grid g, int col, string s, IBrush cor, bool negrito, double tam = 14, bool direita = false, int linha = 0)
    {
        var t = Txt(s, tam, cor, negrito);
        if (direita) { t.HorizontalAlignment = HorizontalAlignment.Right; t.TextAlignment = TextAlignment.Right; }
        Grid.SetColumn(t, col);
        Grid.SetRow(t, linha);
        g.Children.Add(t);
        return t;
    }

    // uma linha de jogador das listas do computador em duas linhas (a tela e estreita para as
    // colunas todas numa so): em cima a posicao, o nome em negrito e as colunas a (forca,
    // S, L, preco...); embaixo, sob o nome, a equipa e as colunas b (pais, comportamento...)
    public static Control Jogador2(IBrush cor, string pos, string nome, (string T, double W, bool D)[] a, string? equipa, (string T, double W, bool D)[] b, bool nomeNegrito = true)
    {
        var s = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        Grid Fila((string T, double W, bool D)[] cols, string p0, string t1, double tam, bool negrito)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition(20, GridUnitType.Pixel));
            g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            foreach (var c in cols) g.ColumnDefinitions.Add(new ColumnDefinition(c.W, GridUnitType.Pixel));
            Col(g, 0, p0, cor, false, tam);
            Col(g, 1, t1, cor, negrito, tam);
            for (int i = 0; i < cols.Length; i++) Col(g, i + 2, cols[i].T, cor, false, tam, cols[i].D).Margin = new Thickness(4, 0, 0, 0);
            return g;
        }
        s.Children.Add(Fila(a, pos, nome, 14.5, nomeNegrito));
        if (equipa != null || b.Length > 0) s.Children.Add(Fila(b, "", equipa ?? "", 12.5, false));
        return s;
    }
}
