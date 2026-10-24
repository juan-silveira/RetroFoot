using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RetroFoot.Motor.Mercado;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// A TloanDlg (7:3826, Execute): o limite fica fixo ao abrir (Max(divida, LimiteEmprestimo),
// MercadoJogo.AbrirBanco); os dois botoes mostram so o passo (DS:2D7F por extenso; o + e o -
// sao os glifos); Atualizar (7:38e6) liga Pedir se divida < limite e Pagar se divida > 0 e
// dinheiro > passo. Cada clique pede ou paga um passo (7:3a0e, 7:3a38).
//
// No original abre por Equipa > Pedir empréstimo (4:3de6), pelo clique no juro (4:3e45) e no
// rotulo (4:3e27) da aba Financas e pelo Empréstimo do Estadio, sempre so com o add-in 0x13
// (registado); quem chama decide.
public static class TelaEmprestimo
{
    // abre a janela por cima da tela atual (thread da interface, motor parado); fechou: ao
    // fechar. A cada pedido/pagamento dispara TelaDinheiro.Mudou.
    public static void Abrir(Sessao s, SEquipa e, Action? fechou = null) => Abrir(s, e, fechou, null, null);

    internal static void Abrir(Sessao s, SEquipa e, Action? fechou, Action<Control>? abrir, Action<Control>? fechar)
    {
        if (Celular.Ativo) { ClubeCelular.Emprestimo(s, e, fechou, abrir, fechar); return; }
        if (Visual.Moderno) { AbrirModerno(s, e, fechou, abrir, fechar); return; }
        var mc = s.Mercado;
        var banco = mc.AbrirBanco(e);
        string passo = FormatoDinheiro.Valor(s.S, mc.PassoEmprestimo);
        var j = new Janela(T("Empréstimos"), 413, 394, W95.Cor(255, 255, 128));   // Color = $80FFFF
        var f11 = TelaDinheiro.Arial11;
        j.Por(new Rotulo { Texto = T("Os juros a pagar pelo empréstimo são de 5% do capital em dívida por cada jogo realizado"), Fonte = f11 }, 8, 280, 337, 33);
        j.Por(new Rotulo { Texto = T("O montante em dívida poderá ser reduzido sempre que o desejar, ou aumentado, se tal lhe for permitido."), Fonte = f11 }, 8, 320, 380, 33);
        j.Por(new Rotulo { Texto = T("O montante máximo a emprestar está condicionado pela confiança que o banco tem em si, neste momento."), Fonte = f11 }, 8, 352, 380, 33);
        j.Por(new Rotulo { Texto = T("Capital em dívida"), Fonte = TelaDinheiro.MsSans11b, Auto = true }, 8, 16, 100, 13);
        var tDivida = j.Por(new Rotulo { Fonte = TelaDinheiro.MsSans16b }, 144, 16, 225, 49);
        j.Por(new Rotulo { Texto = T("Juro a pagar"), Fonte = TelaDinheiro.MsSans11b, Auto = true }, 8, 72, 72, 13);
        var tJuro = j.Por(new Rotulo { Fonte = TelaDinheiro.MsSans16b }, 144, 72, 225, 20);
        j.Por(new Rotulo { Texto = T("Dinheiro em caixa"), Fonte = TelaDinheiro.MsSans11b, Auto = true }, 184, 208, 102, 13);
        var tDinheiro = j.Por(new Rotulo { Fonte = f11 }, 184, 224, 209, 33);
        // Panel1: 1 ponto de altura, a linha que separa as explicacoes
        j.Por(new Rotulo { Fundo = W95.Sombra }, 0, 264, 409, 1);
        var fechar0 = j.Por(BotaoMargem.Ok(T("Fechar"), TelaDinheiro.Arial9b, 10), 308, 124, 64, 64);
        var pedir = j.Por(new BotaoMargem { Texto = passo, Fonte = TelaDinheiro.Arial9b, Glifo = W95.Imagem("Botoes/PEDIR.png"), NumGlifos = 2, Margem = 10 }, 8, 124, 80, 64);
        var pagar = j.Por(new BotaoMargem { Texto = passo, Fonte = TelaDinheiro.Arial9b, Glifo = W95.Imagem("Botoes/PAGAR.png"), NumGlifos = 2, Margem = 10 }, 96, 124, 80, 64);

        void Atualizar()
        {
            tDivida.Texto = FormatoDinheiro.Valor(s.S, (int)e.D6D);
            tJuro.Texto = FormatoDinheiro.Valor(s.S, MercadoJogo.Juros((int)e.D6D));
            tDinheiro.Texto = FormatoDinheiro.Valor(s.S, (int)e.Dinheiro);
            pedir.Habilitado = banco.PodePedir(e);
            pagar.Habilitado = mc.PodePagar(e);
            j.InvalidateTudo();
        }
        pedir.Clique += () =>
        {
            if (!banco.PodePedir(e)) return;
            if (s.Remoto is { } rmP) { rmP.Executar(new RetroFoot.Motor.Online.PedirEmprestimo()); return; }
            mc.PedirEmprestimo(e);
            Atualizar();
            TelaDinheiro.Avisar(e);
        };
        pagar.Clique += () =>
        {
            if (!mc.PodePagar(e)) return;
            if (s.Remoto is { } rmG) { rmG.Executar(new RetroFoot.Motor.Online.PagarEmprestimo()); return; }
            mc.PagarEmprestimo(e);
            Atualizar();
            TelaDinheiro.Avisar(e);
        };
        Atualizar();
        var d = TelaDinheiro.Abrir(s, j, abrir, fechar);
        if (fechou != null) d.Fechou += fechou;
        fechar0.Clique += d.Fechar;
        j.AoEnter = d.Fechar;
    }

    // ------------------------------------------------------------ Moderno
    // O emprestimo no visual Moderno: a divida em destaque com a barra do limite do banco
    // (fixo ao abrir, como no original), o juro por jogo e o dinheiro em caixa; os botoes
    // Pagar e Pedir com o passo; as explicacoes do original em letra pequena. No 16:9 os
    // numeros ficam a esquerda e as explicacoes a direita.
    static void AbrirModerno(Sessao s, SEquipa e, Action? fechou, Action<Control>? abrir, Action<Control>? fechar)
    {
        var mc = s.Mercado;
        var banco = mc.AbrirBanco(e);
        string passo = FormatoDinheiro.Valor(s.S, mc.PassoEmprestimo);
        var tDivida = ClubeModerno.T("", 26, ClubeModerno.Texto, FontWeight.Bold);
        var tLimite = ClubeModerno.T("", 12, ClubeModerno.Fraco);
        var barra = new ContentControl();
        var tJuro = ClubeModerno.T("", 16, ClubeModerno.Vermelho, FontWeight.SemiBold);
        var tDinheiro = ClubeModerno.T("", 16, ClubeModerno.Texto, FontWeight.SemiBold);

        var divida = new StackPanel { Spacing = 6 };
        divida.Children.Add(ClubeModerno.Rotulo(T("Capital em dívida")));
        divida.Children.Add(tDivida);
        divida.Children.Add(barra);
        divida.Children.Add(tLimite);
        StackPanel Valor(string rotulo, TextBlock t)
        {
            var p = new StackPanel { Spacing = 4 };
            p.Children.Add(ClubeModerno.Rotulo(rotulo));
            p.Children.Add(t);
            return p;
        }
        var numeros = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        var cj = ClubeModerno.Secao(Valor(T("Juro por jogo"), tJuro));
        var cd = ClubeModerno.Secao(Valor(T("Dinheiro em caixa"), tDinheiro));
        Grid.SetColumn(cd, 2);
        numeros.Children.Add(cj);
        numeros.Children.Add(cd);

        var pagar = new BotaoModerno(T("−  Pagar {0}", passo));
        var pedir = new BotaoModerno(T("+  Pedir {0}", passo), true);
        var acoes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        acoes.Children.Add(pagar);
        acoes.Children.Add(pedir);

        var notas = new StackPanel { Spacing = 8 };
        foreach (var n in new[]
        {
            T("Os juros a pagar pelo empréstimo são de 5% do capital em dívida por cada jogo realizado."),
            T("O montante em dívida poderá ser reduzido sempre que o desejar, ou aumentado, se tal lhe for permitido."),
            T("O montante máximo a emprestar está condicionado pela confiança que o banco tem em si, neste momento."),
        })
            notas.Children.Add(ClubeModerno.T(n, 12, ClubeModerno.Fraco));

        var esquerda = new StackPanel { Spacing = 14 };
        esquerda.Children.Add(ClubeModerno.Secao(divida, 16));
        esquerda.Children.Add(numeros);
        esquerda.Children.Add(acoes);
        Control corpo;
        double largura, altura;
        if (Formato.Largo)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,20,230") };
            g.Children.Add(esquerda);
            var sn = ClubeModerno.Secao(notas, 16);
            sn.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(sn, 2);
            g.Children.Add(sn);
            corpo = g;
            largura = 760; altura = 400;
        }
        else
        {
            notas.Margin = new Thickness(2, 4, 2, 0);
            esquerda.Children.Add(notas);
            corpo = esquerda;
            largura = 520; altura = 500;
        }

        var j = new JanelaModerna(T("Empréstimos"), TelaDinheiro.Texto(e.NomeCompleto), new EscudoClube(e, 44), corpo, largura, altura);
        void Atualizar()
        {
            int d = (int)e.D6D;
            tDivida.Text = FormatoDinheiro.Valor(s.S, d);
            tLimite.Text = T("Limite do banco: ") + FormatoDinheiro.Valor(s.S, banco.Limite);
            barra.Content = ClubeModerno.Progresso(banco.Limite > 0 ? (double)d / banco.Limite : 0, d >= banco.Limite ? ClubeModerno.Vermelho : ClubeModerno.Ouro);
            tJuro.Text = FormatoDinheiro.Valor(s.S, MercadoJogo.Juros(d));
            tDinheiro.Text = FormatoDinheiro.Valor(s.S, (int)e.Dinheiro);
            tDinheiro.Foreground = (int)e.Dinheiro < 0 ? ClubeModerno.Vermelho : ClubeModerno.Texto;
            pedir.Habilitado = banco.PodePedir(e);
            pagar.Habilitado = mc.PodePagar(e);
        }
        pedir.Clique += () =>
        {
            if (!banco.PodePedir(e)) return;
            if (s.Remoto is { } rmP) { rmP.Executar(new RetroFoot.Motor.Online.PedirEmprestimo()); return; }
            mc.PedirEmprestimo(e);
            Atualizar();
            TelaDinheiro.Avisar(e);
        };
        pagar.Clique += () =>
        {
            if (!mc.PodePagar(e)) return;
            if (s.Remoto is { } rmG) { rmG.Executar(new RetroFoot.Motor.Online.PagarEmprestimo()); return; }
            mc.PagarEmprestimo(e);
            Atualizar();
            TelaDinheiro.Avisar(e);
        };
        Atualizar();
        j.Botao(T("Fechar"), false, j.Fechar);
        j.AoEnter = j.Fechar;
        if (fechou != null) j.Fechou += fechou;
        j.Abrir(abrir ?? (c => s.AbrirDialogo(c)), fechar ?? (c => s.FecharDialogo(c)));
    }
}
