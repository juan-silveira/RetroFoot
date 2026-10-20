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
    // o mesmo dentro de uma folha ou cartao (no Retro, o fundo e cinza: azul-marinho)
    public static TextBlock SecaoFolha(string s) => Txt(s.ToUpperInvariant(), 12.5, Retro ? W95.Marinho : TemaVerde.TextoFraco, FontWeight.Bold);

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

    // ---------------------------------------------------------------- mais pecas
    // uma linha "rotulo ........ valor" (fichas, financas, detalhes)
    public static Control Linha_(string rotulo, string valor, IBrush? cor = null, bool negrito = true)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 30 };
        g.Children.Add(Txt(rotulo, 14, Fraco));
        var v = Txt(valor, 15, cor, negrito ? FontWeight.SemiBold : FontWeight.Normal);
        v.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(v, 1);
        g.Children.Add(v);
        return g;
    }

    // um grupo de linhas num cartao, com um fio entre elas (o elenco, as listas)
    public static Border Grupo(IEnumerable<Control> linhas)
    {
        var p = new StackPanel();
        bool primeira = true;
        foreach (var l in linhas)
        {
            p.Children.Add(new Border
            {
                BorderBrush = Linha, BorderThickness = new Thickness(0, primeira ? 0 : 0.5, 0, 0), Child = l,
            });
            primeira = false;
        }
        return new Border
        {
            Background = Retro ? W95.Branco : TemaVerde.CartaoHub, CornerRadius = new CornerRadius(Retro ? 0 : 14),
            BorderBrush = Retro ? W95.Escura : Linha, BorderThickness = new Thickness(1), ClipToBounds = true, Child = p,
        };
    }

    // uma linha de lista que se toca (sem fundo proprio: vai dentro de um Grupo)
    public static Clicavel Toque(Control conteudo, Action? toque, double altura = 48)
    {
        var c = new Clicavel
        {
            Background = Brushes.Transparent, Child = conteudo, MinHeight = altura, Padding = new Thickness(12, 6), CorFoco = null,
            Hover = Retro ? null : TemaVerde.Hover,
        };
        if (toque != null) c.Clique += toque; else c.Cursor = Cursor.Default;
        return c;
    }

    // a barra de cima das telas inteiras: voltar, o titulo (e o subtitulo) e acoes a direita
    public static Control BarraTopo(string titulo, string? sub = null, Action? voltar = null, Control? direita = null)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), MinHeight = 44 };
        if (voltar != null)
        {
            var v = new Clicavel
            {
                Width = 44, Height = 44, Background = Brushes.Transparent, CorFoco = null, Margin = new Thickness(-8, 0, 4, 0),
                Child = new TextBlock { Text = "←", FontSize = 24, Foreground = TextoBarra, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            v.Clique += voltar;
            g.Children.Add(v);
        }
        var t = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(Txt(titulo, 18, TextoBarra, FontWeight.Bold));
        if (!string.IsNullOrEmpty(sub)) t.Children.Add(Txt(sub, 12.5, FracoBarra));
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        if (direita != null) { direita.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(direita, 2); g.Children.Add(direita); }
        return new Border
        {
            Background = Barra, Padding = new Thickness(14, 8), Child = g,
            BorderBrush = Retro ? W95.Preto : TemaVerde.Borda, BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    // as abas de uma tela (Configuracoes, Opcoes, detalhes do jogo): uma fila que rola de lado;
    // trocar(i) e chamado na escolha. Devolve a fila e a funcao que marca a aba i
    public static (Control Fila, Action<int> Marcar) Abas(IReadOnlyList<string> nomes, Action<int> trocar, int inicial = 0)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12, 10, 12, 6) };
        var bs = new List<Border>();
        void Marcar(int k)
        {
            for (int i = 0; i < bs.Count; i++)
            {
                bool m = i == k;
                var b = bs[i];
                var t = (TextBlock)b.Child!;
                if (Retro)
                {
                    b.Background = m ? W95.Marinho : RCartao;
                    t.Foreground = m ? W95.Branco : RTexto;
                    b.BoxShadow = m ? Afundado : Saltado;
                }
                else
                {
                    b.Background = m ? TemaVerde.BotaoPrimario : TemaVerde.BotaoSecundario;
                    b.BorderBrush = m ? TemaVerde.Ouro : TemaVerde.Borda;
                    t.Foreground = m ? TemaVerde.SobreDestaque : TemaVerde.Texto;
                }
            }
        }
        for (int i = 0; i < nomes.Count; i++)
        {
            int k = i;
            var c = new Clicavel
            {
                Child = Txt(nomes[i], 14, null, FontWeight.Bold), MinHeight = 38, Padding = new Thickness(16, 6),
                CornerRadius = new CornerRadius(Retro ? 0 : 19), BorderThickness = new Thickness(Retro ? 1 : 1.5),
                BorderBrush = Retro ? W95.Preto : TemaVerde.Borda, CorFoco = null,
            };
            c.Clique += () => { Marcar(k); trocar(k); };
            bs.Add(c);
            s.Children.Add(c);
        }
        Marcar(inicial);
        var rolar = new ScrollViewer
        {
            Content = s, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        return (rolar, Marcar);
    }

    // um interruptor com o texto (e a explicacao embaixo): liga e desliga ao tocar na linha
    public static Control Interruptor(string texto, string? explicacao, bool ligado, Action<bool> mudou, bool habilitado = true)
    {
        var sw = new ToggleSwitch { IsChecked = ligado, OnContent = "", OffContent = "", IsEnabled = habilitado, VerticalAlignment = VerticalAlignment.Center };
        if (Retro) sw = new ToggleSwitch { IsChecked = ligado, OnContent = "", OffContent = "", IsEnabled = habilitado, VerticalAlignment = VerticalAlignment.Center };
        sw.IsCheckedChanged += (_, _) => mudou(sw.IsChecked == true);
        var t = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(Txt(texto, 15, null, FontWeight.Medium, true));
        if (!string.IsNullOrEmpty(explicacao)) t.Children.Add(Txt(explicacao, 12.5, Fraco, FontWeight.Normal, true));
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 48, Opacity = habilitado ? 1 : 0.5 };
        g.Children.Add(t);
        Grid.SetColumn(sw, 1);
        g.Children.Add(sw);
        var c = Toque(g, habilitado ? () => sw.IsChecked = sw.IsChecked != true : null);
        return c;
    }

    // a escolha de uma opcao entre varias, em pilulas que quebram a linha (formacoes, faixas,
    // numero de equipas); escolher(i) na escolha. Devolve o painel e o que marca a opcao i
    public static (Control Painel, Action<int> Marcar) Escolha(IReadOnlyList<string> nomes, int atual, Action<int> escolher, double larguraMin = 0, Func<int, bool>? habilitada = null)
    {
        var w = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        var bs = new List<Clicavel>();
        void Marcar(int k)
        {
            for (int i = 0; i < bs.Count; i++)
            {
                bool m = i == k;
                var t = (TextBlock)bs[i].Child!;
                if (Retro)
                {
                    bs[i].Background = m ? W95.Marinho : RCartao;
                    t.Foreground = m ? W95.Branco : RTexto;
                    bs[i].BoxShadow = m ? Afundado : Saltado;
                }
                else
                {
                    bs[i].Background = m ? TemaVerde.Selecao : TemaVerde.BotaoSecundario;
                    bs[i].BorderBrush = m ? TemaVerde.Destaque : TemaVerde.Borda;
                    t.Foreground = TemaVerde.Texto;
                }
            }
        }
        for (int i = 0; i < nomes.Count; i++)
        {
            int k = i;
            var t = Txt(nomes[i], 14.5, null, FontWeight.SemiBold);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            var c = new Clicavel
            {
                Child = t, MinHeight = 42, MinWidth = larguraMin, Padding = new Thickness(14, 6),
                CornerRadius = new CornerRadius(Retro ? 0 : 12), BorderThickness = new Thickness(Retro ? 1 : 1.5),
                BorderBrush = Retro ? W95.Preto : TemaVerde.Borda, CorFoco = null,
                IsEnabled = habilitada?.Invoke(i) ?? true,
            };
            c.Clique += () => { Marcar(k); escolher(k); };
            bs.Add(c);
            w.Children.Add(c);
        }
        Marcar(atual);
        return (w, Marcar);
    }

    // um deslizador com o rotulo e o valor escrito a direita (volume, tamanho do texto)
    public static Control Deslizador(string rotulo, double min, double max, double valor, Func<double, string> texto, Action<double> mudou, double passo = 1)
    {
        var v = Txt(texto(valor), 14, null, FontWeight.SemiBold);
        var sl = new Slider { Minimum = min, Maximum = max, Value = valor, SmallChange = passo, LargeChange = passo, TickFrequency = passo, IsSnapToTickEnabled = true };
        sl.ValueChanged += (_, e) => { v.Text = texto(e.NewValue); mudou(e.NewValue); };
        var cab = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        cab.Children.Add(Txt(rotulo, 15, null, FontWeight.Medium));
        Grid.SetColumn(v, 1);
        cab.Children.Add(v);
        return new StackPanel { Spacing = 2, Children = { cab, sl } };
    }

    // o escudo da equipa (Moderno) ou o circulo nas cores dela (Retro, ou sem logo)
    public static Control Escudo(RetroFoot.Motor.Save.Equipa? e, double tam)
    {
        if (e != null && !Retro && Escudos.Controle(e, tam) is { } c) return c;
        IBrush C(byte[]? b, IBrush padrao) => b == null || b.Length < 4 || (b[3] & 0x80) != 0 ? padrao : new SolidColorBrush(Color.FromRgb(b[0], b[1], b[2]));
        var fundo = C(e?.CorFundo, Brushes.White);
        var letra = C(e?.CorLetra, Brushes.Black);
        double t = tam * 0.85;
        return new Border
        {
            Width = t, Height = t, CornerRadius = new CornerRadius(t / 2), Background = fundo, BorderBrush = letra,
            BorderThickness = new Thickness(Math.Max(2, t / 14)), VerticalAlignment = VerticalAlignment.Center,
        };
    }

    // a caixa com o nome da equipa nas cores dela (o Retro, como nas listas do classico)
    public static Control NomeEquipa(RetroFoot.Motor.Save.Equipa e, string nome, double tam = 14, bool negrito = false)
    {
        IBrush C(byte[]? b, IBrush padrao) => b == null || b.Length < 4 || (b[3] & 0x80) != 0 ? padrao : new SolidColorBrush(Color.FromRgb(b[0], b[1], b[2]));
        if (Retro)
            return new Border
            {
                Background = C(e.CorFundo, Brushes.White), Padding = new Thickness(6, 2), VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = nome, FontFamily = Fonte, FontSize = tam * Acessibilidade.Texto, FontWeight = negrito ? FontWeight.Bold : FontWeight.Normal, Foreground = C(e.CorLetra, Brushes.Black), Tag = "fixo", TextTrimming = TextTrimming.CharacterEllipsis },
            };
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        s.Children.Add(Escudo(e, tam + 8));
        s.Children.Add(Txt(nome, tam, null, negrito ? FontWeight.Bold : FontWeight.Medium));
        return s;
    }

    // ---------------------------------------------------------------- a folha (janelas)
    // As janelas do jogo no celular (substituicao, leilao, estadio, mensagens...): sobem de
    // baixo, na largura da tela (Inteira: a tela toda, com a barra de cima). Sessao.AbrirDialogo
    // reconhece a Folha (ou a janela cujo Content e uma Folha) e a mostra sem a ampliar.
    // AoVoltar: o que o Voltar do Android faz (null: nada, a janela tem de ser respondida).
    public sealed class Folha : UserControl
    {
        public Action? AoVoltar { get; set; }
        public bool Inteira { get; }
        readonly ContentControl _corpo = new();
        readonly ContentControl _rodape = new();

        public Folha(string titulo, Control corpo, Control? rodape = null, string? sub = null, Control? icone = null, bool inteira = false, bool rolar = true, Action? fechar = null)
        {
            Inteira = inteira;
            FontFamily = Fonte;
            AoVoltar = fechar;
            _corpo.Content = rolar
                ? new ScrollViewer { Content = new Border { Padding = new Thickness(14, 10, 14, 14), Child = corpo }, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }
                : new Border { Padding = new Thickness(inteira ? 0 : 14, inteira ? 0 : 10, inteira ? 0 : 14, inteira ? 0 : 14), Child = corpo };
            var d = new DockPanel();
            Control cab;
            if (inteira) cab = BarraTopo(titulo, sub, fechar, null);
            else
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
                if (icone != null) { icone.Margin = new Thickness(0, 0, 10, 0); icone.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(icone); }
                var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                t.Children.Add(Txt(titulo, Retro ? 15 : 18, Retro ? W95.Branco : Texto, FontWeight.Bold));
                if (!string.IsNullOrEmpty(sub)) t.Children.Add(Txt(sub, 12.5, Retro ? H("#C8C8FF") : Fraco, FontWeight.Normal, true));
                Grid.SetColumn(t, 1);
                g.Children.Add(t);
                if (fechar != null)
                {
                    var x = new Botao("✕", false, fechar) { Padding = new Thickness(12, 2), MinHeight = Retro ? 30 : 38 };
                    Grid.SetColumn(x, 2);
                    g.Children.Add(x);
                }
                cab = Retro
                    ? new Border { Background = RBarra, Padding = new Thickness(10, 6), Child = g }
                    : new StackPanel
                    {
                        Children =
                        {
                            new Border { Width = 40, Height = 4, CornerRadius = new CornerRadius(2), Background = TemaVerde.Borda, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 6) },
                            new Border { Padding = new Thickness(16, 2, 16, 4), Child = g },
                        },
                    };
            }
            DockPanel.SetDock(cab, Dock.Top);
            d.Children.Add(cab);
            if (rodape != null)
            {
                _rodape.Content = rodape;
                var r = new Border
                {
                    Padding = new Thickness(14, 10, 14, 14), Child = _rodape,
                    BorderBrush = Linha, BorderThickness = new Thickness(0, 1, 0, 0),
                    Background = Retro ? RCartao : (inteira ? TemaVerde.BarraHub : null),
                };
                DockPanel.SetDock(r, Dock.Bottom);
                d.Children.Add(r);
            }
            d.Children.Add(_corpo);
            if (inteira)
            {
                Background = Fundo;
                Content = d;
            }
            else
            {
                Content = new Border
                {
                    Background = Retro ? RCartao : TemaVerde.FundoTela, Child = d,
                    CornerRadius = new CornerRadius(Retro ? 0 : 22, Retro ? 0 : 22, 0, 0),
                    BorderBrush = Retro ? W95.Preto : TemaVerde.Borda, BorderThickness = new Thickness(Retro ? 2 : 1, Retro ? 2 : 1, Retro ? 2 : 1, 0),
                    BoxShadow = Retro ? Saltado : BoxShadows.Parse("0 -6 18 0 #50000000"),
                };
                VerticalAlignment = VerticalAlignment.Bottom;
            }
            AttachedToVisualTree += (_, _) => Abertas.Add(this);
            DetachedFromVisualTree += (_, _) => Abertas.Remove(this);
        }

        // troca o corpo (uma janela que muda de passo: o leilao, os penaltis)
        public void Rodape(Control? c) => _rodape.Content = c;

        // as folhas abertas (a de cima recebe o Voltar)
        internal static readonly List<Folha> Abertas = new();

        // o Voltar do Android (App liga): a folha de cima responde; sem AoVoltar, fica
        public static bool Voltar()
        {
            if (Abertas.Count == 0) return false;
            Abertas[^1].AoVoltar?.Invoke();
            return true;
        }
    }

    // a folha dentro de um controle (o dialogo cujo Content e a Folha)
    public static Folha? FolhaDe(Control? c)
    {
        for (int i = 0; i < 4 && c != null; i++, c = (c as ContentControl)?.Content as Control)
            if (c is Folha f) return f;
        return null;
    }

    // uma pergunta ou aviso como folha (Sim/Nao, OK): para as janelas que so perguntam
    public static Folha Pergunta(string titulo, string texto, IReadOnlyList<(string Texto, bool Principal, Action Acao)> botoes, Action? voltar = null)
    {
        var bs = new List<Control>();
        foreach (var (t, pr, a) in botoes) bs.Add(new Botao(t, pr, a));
        return new Folha(titulo, Txt(texto, 15.5, null, FontWeight.Normal, true), Botoes(bs.ToArray()), fechar: null) { AoVoltar = voltar };
    }

    // ---------------------------------------------------------------- jogadores
    static readonly string[] SiglasComp = { "FP", "COR", "CAV", "CAN", "CAC", "SAR" };
    public static string SiglaComp(RetroFoot.Motor.Save.Jogador j) => j.Comportamento < SiglasComp.Length ? SiglasComp[j.Comportamento] : "";

    // a bandeira do pais (16 pontos) ou nada
    public static Control? Bandeira(string pais, double tam = 16) =>
        W95.Imagem($"Bandeiras/{pais}.png") is { } b ? new Image { Source = b, Width = tam, Height = tam, VerticalAlignment = VerticalAlignment.Center } : null;

    // a linha de um jogador como no elenco: a posicao, a bandeira, o nome (e o que vier ao lado
    // dele: selos), o comportamento e a forca; extra = colunas a mais antes da forca (o
    // preco, a equipa...). esmaecido = nao pode ser escolhido (o fora, o lesionado)
    public static Control LinhaJogador(RetroFoot.Motor.Save.Jogador j, IEnumerable<Control>? selos = null, Control? extra = null, bool esmaecido = false, bool comp = true)
    {
        var pos = Txt(Idioma.Posicao(Math.Min((int)j.Posicao, 3)), 12.5, Retro ? W95.Marinho : TemaVerde.Destaque, FontWeight.Bold);
        pos.Width = 20;
        var meio = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (Bandeira(TextosMercado.PaisDe(j.Pais)) is { } bb) meio.Children.Add(bb);
        meio.Children.Add(Txt(TextosMercado.NomeJogador(j), 15, esmaecido ? Fraco : null, FontWeight.Medium));
        if (selos != null) foreach (var x in selos) meio.Children.Add(x);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,40"), MinHeight = 34 };
        g.Children.Add(pos);
        Grid.SetColumn(meio, 1); g.Children.Add(meio);
        if (extra != null) { extra.VerticalAlignment = VerticalAlignment.Center; extra.Margin = new Thickness(6, 0); Grid.SetColumn(extra, 2); g.Children.Add(extra); }
        if (comp)
        {
            var c = Txt(SiglaComp(j), 11.5, Fraco, FontWeight.Bold);
            c.Width = 36; c.TextAlignment = TextAlignment.Center;
            Grid.SetColumn(c, 3); g.Children.Add(c);
        }
        var f = Txt(j.Forca.ToString(), 16, Retro ? W95.Marinho : TemaVerde.Destaque, FontWeight.Bold);
        f.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(f, 4); g.Children.Add(f);
        return g;
    }

    // uma lista de escolha unica (os reservas da substituicao, os jogadores da venda...): cada
    // linha se toca e fica marcada; escolheu(i) na escolha
    public static (Control Lista, Action<int> Marcar) ListaEscolha(IReadOnlyList<Control> linhas, Action<int> escolheu, Func<int, bool>? habilitada = null)
    {
        var itens = new List<Clicavel>();
        void Marcar(int k)
        {
            for (int i = 0; i < itens.Count; i++)
            {
                bool m = i == k;
                itens[i].Background = m ? (Retro ? W95.Marinho : TemaVerde.Selecao) : Brushes.Transparent;
                if (Retro) PintarTexto(itens[i].Child, m ? W95.Branco : null);
            }
        }
        var ls = new List<Control>();
        for (int i = 0; i < linhas.Count; i++)
        {
            int k = i;
            var c = Toque(linhas[i], () => { Marcar(k); escolheu(k); });
            if (habilitada != null && !habilitada(i)) c.Opacity = 0.45;
            itens.Add(c);
            ls.Add(c);
        }
        return (Grupo(ls), Marcar);
    }

    // o texto de uma linha no realce do Retro (branco) e de volta (null: a cor de antes)
    static void PintarTexto(Control? c, IBrush? cor)
    {
        switch (c)
        {
            case TextBlock t when t.Tag is not "fixo":
                if (cor != null) { t.Tag ??= t.Foreground; t.Foreground = cor; }
                else if (t.Tag is IBrush antes) t.Foreground = antes;
                break;
            case Panel p: foreach (var x in p.Children) PintarTexto(x, cor); break;
            case Decorator d: PintarTexto(d.Child, cor); break;
        }
    }
}
