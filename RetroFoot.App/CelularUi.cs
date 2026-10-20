using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace RetroFoot.App;

// O kit das telas do celular (Celular.cs), em duas peles, pelo visual das Configuracoes:
//  - Moderno: o verde e amarelo do hub (TemaVerde), cantos arredondados, Inter;
//  - Retro: o Windows 95 do jogo classico adaptado ao toque: o fundo verde, as janelas
//    cinza com relevo, a barra de titulo azul-marinho, cantos retos, Arial.
// Cada tela do celular e escrita uma vez com estas pecas e sai nas duas peles. Tudo com alvos
// de toque de pelo menos 44 pontos.
public static class CUi
{
    public static bool Retro => !Visual.Moderno;

    // ---------------------------------------------------------------- as cores
    static IBrush H(string s) => new SolidColorBrush(Color.Parse(s));
    static readonly IBrush RFundo = W95.Verde, RBarra = W95.Marinho, RCartao = W95.Face, RTexto = W95.Preto,
        RFraco = H("#404040"), RLinha = H("#808080"), RSelecao = W95.Marinho, RSobreSelecao = W95.Branco,
        RTitulo = W95.Amarelo, RCampo = W95.Branco;

    public static IBrush Fundo => Retro ? RFundo : TemaVerde.FundoTela;
    public static IBrush Barra => Retro ? RBarra : TemaVerde.BarraHub;
    public static IBrush TextoBarra => Retro ? W95.Branco : TemaVerde.Texto;
    public static IBrush FracoBarra => Retro ? H("#C8C8FF") : TemaVerde.TextoFraco;
    public static IBrush Cartao => Retro ? RCartao : TemaVerde.CartaoHub;
    public static IBrush Texto => Retro ? RTexto : TemaVerde.Texto;
    public static IBrush Fraco => Retro ? RFraco : TemaVerde.TextoFraco;
    public static IBrush Linha => Retro ? RLinha : TemaVerde.Borda;
    // o destaque (a linha escolhida, o botao principal) e o texto sobre ele
    public static IBrush Selecao => Retro ? RSelecao : TemaVerde.Selecao;
    public static IBrush SobreSelecao => Retro ? RSobreSelecao : TemaVerde.Texto;
    public static IBrush Destaque => Retro ? W95.Marinho : TemaVerde.Destaque;
    // o titulo das secoes sobre o fundo (o amarelo do classico; no Moderno, o fraco)
    public static IBrush TituloSecao => Retro ? RTitulo : TemaVerde.TextoFraco;
    public static IBrush Erro => Retro ? W95.Vermelho : TemaVerde.Vermelho;
    public static IBrush Bom => Retro ? H("#006400") : TemaVerde.Verde;
    public static FontFamily Fonte => Retro ? W95.Arial13.Familia : ConfigEstilo.Inter;
    public static double Raio => Retro ? 0 : 14;

    // o relevo do Windows 95 (para fora: luz em cima e a esquerda; para dentro: o contrario)
    static readonly BoxShadows Saltado = BoxShadows.Parse("inset 1 1 0 0 #FFFFFF, inset -1 -1 0 0 #6A6A6A");
    static readonly BoxShadows Afundado = BoxShadows.Parse("inset 1 1 0 0 #6A6A6A, inset -1 -1 0 0 #FFFFFF");

    // ---------------------------------------------------------------- texto
    public static TextBlock Txt(string s, double tam = 15, IBrush? cor = null, FontWeight peso = FontWeight.Normal, bool quebra = false) => new()
    {
        Text = s, FontFamily = Fonte, FontSize = tam * Acessibilidade.Texto, FontWeight = peso, Foreground = cor ?? Texto,
        TextWrapping = quebra ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = quebra ? TextTrimming.None : TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // o titulo de uma secao sobre o fundo ("GOLEIROS · 4")
    public static TextBlock TituloSecao_(string s) => Txt(s.ToUpperInvariant(), 12.5, TituloSecao, FontWeight.Bold);

    // ---------------------------------------------------------------- pecas
    // a pagina: o cabecalho em cima, o corpo (que rola) e o rodape fixo embaixo
    public static Control Pagina(Control cabecalho, Control corpo, Control? rodape = null, bool rolar = true)
    {
        var d = new DockPanel { Background = Fundo };
        DockPanel.SetDock(cabecalho, Dock.Top);
        d.Children.Add(cabecalho);
        if (rodape != null)
        {
            var r = new Border
            {
                Background = Retro ? RCartao : TemaVerde.BarraHub, BorderBrush = Linha, BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(14, 10), Child = rodape, BoxShadow = Retro ? Saltado : default,
            };
            DockPanel.SetDock(r, Dock.Bottom);
            d.Children.Add(r);
        }
        var miolo = new Border { Padding = new Thickness(12, 12, 12, 16), Child = corpo };
        d.Children.Add(rolar
            ? new ScrollViewer { Content = miolo, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }
            : miolo);
        return d;
    }

    // o cabecalho: a bola (ou o escudo), o titulo, o subtitulo e as acoes embaixo
    public static Control Cabecalho(string titulo, string? sub = null, Control? acoes = null, Control? icone = null)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,10,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        var ic = icone ?? Logotipo.ImagemBola(38);
        ic.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(ic);
        var t = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(Txt(titulo, 19, TextoBarra, FontWeight.Bold));
        if (!string.IsNullOrEmpty(sub)) t.Children.Add(Txt(sub, 12.5, FracoBarra));
        Grid.SetColumn(t, 2);
        g.Children.Add(t);
        if (acoes != null)
        {
            Grid.SetRow(acoes, 1); Grid.SetColumnSpan(acoes, 3);
            acoes.Margin = new Thickness(0, 10, 0, 0);
            g.Children.Add(acoes);
        }
        return new Border
        {
            Background = Barra, Padding = new Thickness(14, 12), Child = g,
            BorderBrush = Retro ? W95.Preto : TemaVerde.Borda, BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    // um cartao (no Retro, uma janela: a barra de titulo azul-marinho e o corpo cinza com relevo)
    public static Border Cartao_(string? titulo, Control conteudo, Control? direita = null)
    {
        var corpo = new StackPanel { Spacing = 10 };
        if (titulo != null && !Retro)
        {
            var cab = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            cab.Children.Add(Txt(titulo, 16, Texto, FontWeight.SemiBold));
            if (direita != null) { Grid.SetColumn(direita, 1); cab.Children.Add(direita); }
            corpo.Children.Add(cab);
        }
        corpo.Children.Add(conteudo);
        if (!Retro)
            return new Border
            {
                Background = Cartao, BorderBrush = Linha, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(Raio),
                Padding = new Thickness(14, 12), Child = corpo, BoxShadow = BoxShadows.Parse("0 4 12 0 #40000000"),
            };
        var janela = new StackPanel();
        if (titulo != null)
        {
            var barra = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            barra.Children.Add(Txt(titulo, 14, W95.Branco, FontWeight.Bold));
            if (direita != null) { Grid.SetColumn(direita, 1); barra.Children.Add(direita); }
            janela.Children.Add(new Border { Background = RBarra, Padding = new Thickness(8, 4), Child = barra });
        }
        janela.Children.Add(new Border { Padding = new Thickness(10), Child = corpo });
        return new Border
        {
            Background = RCartao, BorderBrush = W95.Preto, BorderThickness = new Thickness(1), Padding = new Thickness(2),
            BoxShadow = Saltado, Child = janela,
        };
    }

    // uma etiqueta pequena ("Temporada 2026")
    public static Border Etiqueta(string s, IBrush? cor = null, IBrush? fundo = null) => new()
    {
        Background = fundo ?? (Retro ? W95.Amarelo : TemaVerde.DestaqueFundo),
        CornerRadius = new CornerRadius(Retro ? 0 : 9), Padding = new Thickness(7, 2),
        BorderBrush = Retro ? W95.Preto : null, BorderThickness = new Thickness(Retro ? 1 : 0),
        Child = Txt(s, 11.5, cor ?? (Retro ? W95.Preto : TemaVerde.Destaque), FontWeight.SemiBold),
        VerticalAlignment = VerticalAlignment.Center,
    };

    // um botao (principal: o amarelo do Moderno; no Retro, o botao padrao com a borda preta)
    public sealed class Botao : Clicavel
    {
        readonly TextBlock _t;
        public Botao(string texto, bool principal, Action clique)
        {
            _t = Txt(texto, 15, null, FontWeight.Bold);
            _t.HorizontalAlignment = HorizontalAlignment.Center;
            Child = _t;
            MinHeight = 44;
            Padding = new Thickness(18, 8);
            HorizontalAlignment = HorizontalAlignment.Left;
            if (Retro)
            {
                Background = RCartao;
                BorderBrush = W95.Preto;
                BorderThickness = new Thickness(principal ? 2 : 1);
                BoxShadow = Saltado; Solto = Saltado; Premido = Afundado;
                _t.Foreground = RTexto;
            }
            else
            {
                CornerRadius = new CornerRadius(22);
                Background = principal ? TemaVerde.BotaoPrimario : TemaVerde.BotaoSecundario;
                Hover = principal ? TemaVerde.BotaoPrimarioHover : TemaVerde.Hover;
                BorderBrush = principal ? TemaVerde.Ouro : TemaVerde.Borda;
                BorderThickness = new Thickness(1.5);
                _t.Foreground = principal ? TemaVerde.SobreDestaque : TemaVerde.Texto;
            }
            Clique += () => { if (Habilitado) clique(); };
        }

        public string Texto { get => _t.Text ?? ""; set => _t.Text = value; }
        public bool Habilitado { get => IsEnabled; set => IsEnabled = value; }
    }

    // uma linha que se toca (jogos gravados, paises, jogadores...): Marcar(true) a destaca
    public sealed class Item : Clicavel
    {
        public Item(Control conteudo, Action toque)
        {
            Child = conteudo;
            MinHeight = 44;
            Padding = new Thickness(10, 8);
            CorFoco = Retro ? W95.Preto : TemaVerde.Destaque;
            CornerRadius = new CornerRadius(Retro ? 0 : 12);
            BorderThickness = new Thickness(1);
            Clique += toque;
            Marcar(false);
        }

        public void Marcar(bool sim)
        {
            Background = sim ? Selecao : Retro ? RCampo : TemaVerde.BotaoSecundario;
            BorderBrush = sim ? (Retro ? W95.Marinho : TemaVerde.Destaque) : Linha;
            // o texto sobre a selecao do Retro fica branco (o realce do Windows 95)
            if (Retro) Pintar(Child, sim ? RSobreSelecao : RTexto);
        }

        static void Pintar(Control? c, IBrush cor)
        {
            switch (c)
            {
                case TextBlock t when t.Tag is not "fixo": t.Foreground = cor; break;
                case Panel p: foreach (var x in p.Children) Pintar(x, cor); break;
                case Decorator d: Pintar(d.Child, cor); break;
            }
        }
    }

    // a caixa de texto (no Retro, branca e afundada)
    public static TextBox Caixa(string texto = "", int max = 0, string? dica = null)
    {
        var t = new TextBox
        {
            Text = texto, MaxLength = max, PlaceholderText = dica, FontFamily = Fonte, FontSize = 17, MinHeight = 44,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        if (Retro)
        {
            t.Background = RCampo; t.Foreground = RTexto; t.BorderBrush = W95.Escura; t.CornerRadius = new CornerRadius(0);
            t.BorderThickness = new Thickness(2);
        }
        return t;
    }

    // uma linha de botoes que divide a largura
    public static Grid Botoes(params Control[] bs)
    {
        var g = new Grid { ColumnSpacing = 10 };
        for (int i = 0; i < bs.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            bs[i].HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(bs[i], i);
            g.Children.Add(bs[i]);
        }
        return g;
    }

    // ---------------------------------------------------------------- caixa por cima
    // uma mensagem ou pergunta no meio da tela (o veu escurece o resto)
    public static Control Caixa_(string titulo, Control conteudo, params Botao[] botoes)
    {
        var p = new StackPanel { Spacing = 14, Children = { conteudo } };
        if (botoes.Length > 0) p.Children.Add(Botoes(botoes));
        var c = Cartao_(titulo, p);
        c.Margin = new Thickness(18);
        c.VerticalAlignment = VerticalAlignment.Center;
        c.MaxWidth = 460;
        if (!Retro)
        {
            // no Moderno o titulo vem dentro do cartao (Cartao_ ja pos)
        }
        return new Panel { Background = TemaVerde.Veu, Children = { c } };
    }
}
