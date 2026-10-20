using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RetroFoot.Motor.Mercado;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;
using TM = RetroFoot.App.TextosMercado;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// O leilao no celular (TelaLeilao.cs continua a mandar: o ciclo, os tempos, o OK que so aceita
// o lance que cabe no dinheiro). Uma folha que nao se fecha a mao (como o FormCloseQuery do
// original): a ficha do jogador em cima e, embaixo, o estado: a espera das ofertas, o painel do
// lance humano (nas cores da equipa que licita, a caixa do lance com o menos e o mais, o
// Desistir e o Licitar) e o resultado, que fecha sozinho.
sealed class LeilaoCelular : UserControl
{
    readonly CUi.Folha _folha;
    readonly SEquipa _v;
    readonly int? _preco;
    readonly Moeda _moeda;
    // o texto do lance mudou (a TelaLeilao copia para a caixa dela) e o OK / o Desistir
    public event Action<string>? Mudou;
    public event Action? Licitou;
    public TextBox? Caixa { get; private set; }
    TextBlock? _erro;

    public LeilaoCelular(Sessao s, SEquipa v, SJogador p, int? preco, int salario, bool launcher)
    {
        _v = v; _preco = preco;
        _moeda = Moeda.DoSave(s.S);
        var corpo = new StackPanel { Spacing = 12 };
        corpo.Children.Add(CUi.Cartao_(null, MercadoCelular.Cabecalho(p, v)));
        var linhas = new List<(string, string)>
        {
            (T("Salário pretendido"), FormatoDinheiro.Valor(_moeda, salario)),
            (T("Preço base"), preco is int b ? FormatoDinheiro.Valor(_moeda, b) : "–"),
        };
        corpo.Children.Add(MercadoCelular.Ficha(p, launcher, linhas));
        _folha = new CUi.Folha(T("Venda de jogador por leilão"), corpo, Esperando(), TM.NomeEquipa(v), CUi.Escudo(v, 32), fechar: null);
        Content = _folha;
    }

    // ------------------------------------------------------------ os estados do rodape
    // a espera: as equipas do computador licitam (a barra corre ate o resultado ou o lance)
    static Control Esperando()
    {
        var s = new StackPanel { Spacing = 8 };
        s.Children.Add(CUi.Txt(T("A aguardar ofertas…"), 15, null, FontWeight.SemiBold));
        s.Children.Add(new ProgressBar
        {
            IsIndeterminate = true, Height = 6, MinHeight = 6, MinWidth = 0,
            Foreground = CUi.Retro ? W95.Marinho : TemaVerde.Destaque, CornerRadius = new CornerRadius(CUi.Retro ? 0 : 3),
        });
        return s;
    }

    // o lance humano (Licitante, 2:3787): o painel nas cores da equipa que licita
    public void MostrarPainel(SEquipa c, string treinador, string dinheiro, char senha)
    {
        var (letra, fundo) = TM.Cores(c);
        IBrush texto = CUi.Retro ? letra : CUi.Texto, fraco = CUi.Retro ? letra : CUi.Fraco;
        var p = new StackPanel { Spacing = 10 };
        var cab = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var esc = CUi.Escudo(c, 40);
        esc.Margin = new Thickness(0, 0, 10, 0);
        cab.Children.Add(esc);
        var nomes = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        nomes.Children.Add(CUi.Txt(treinador, 16, texto, FontWeight.Bold));
        nomes.Children.Add(CUi.Txt(dinheiro, 13.5, fraco, FontWeight.Medium));
        Grid.SetColumn(nomes, 1);
        cab.Children.Add(nomes);
        p.Children.Add(cab);
        p.Children.Add(CUi.Txt(T("Oferta (milhares de{0})", _moeda.Sufixo), 13.5, fraco, FontWeight.SemiBold));
        // o passo do menos e do mais (em milhares): 50 x a moeda, a menor faixa de preco
        int passo = Math.Max(1, 50 * Math.Max(1, _moeda.D43));
        TextBox caixa = null!;
        (Control campo, caixa) = MercadoCelular.Numero("", 5, d =>
        {
            int v = MercadoCelular.Valor(caixa) ?? -1;
            if (v < 0) v = d > 0 ? Math.Max(passo, (_preco ?? 0) / 1000) : 0;
            else v = Math.Max(0, v + d * passo);
            caixa.Text = Math.Min(v, 99999).ToString();
        }, senha);
        caixa.TextChanged += (_, _) => { if (_erro != null) _erro.IsVisible = false; Mudou?.Invoke(caixa.Text ?? ""); };
        caixa.KeyDown += (_, e) => { if (e.Key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Return) { e.Handled = true; Licitou?.Invoke(); } };
        Caixa = caixa;
        p.Children.Add(campo);
        _erro = CUi.Txt(T("A oferta é maior que o dinheiro em caixa."), 13.5, CUi.Retro ? W95.Vermelho : TemaVerde.Vermelho, FontWeight.SemiBold, true);
        _erro.IsVisible = false;
        p.Children.Add(_erro);
        // Desistir: o lance vazio (o OK com a caixa vazia: nao licita)
        var desistir = new CUi.Botao(T("Desistir"), false, () => { caixa.Text = ""; Mudou?.Invoke(""); Licitou?.Invoke(); });
        var ok = new CUi.Botao(T("Licitar"), true, () => Licitou?.Invoke());
        p.Children.Add(CUi.Botoes(desistir, ok));
        Control painel = CUi.Retro
            ? new Border { Background = fundo, BorderBrush = W95.Preto, BorderThickness = new Thickness(1), Padding = new Thickness(10), Child = p }
            : new Border { Background = TemaVerde.CartaoHub, BorderBrush = TemaVerde.Destaque, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Child = p };
        _folha.Rodape(painel);
    }

    // o lance nao coube no dinheiro: o painel fica (como o original), com o aviso
    public void Excedeu()
    {
        if (_erro != null) _erro.IsVisible = true;
    }

    public void EsconderPainel()
    {
        Caixa = null; _erro = null;
        _folha.Rodape(Esperando());
    }

    // "Vendido ao X por Y" (os escudos do vendedor e do comprador) ou "Nao houve ofertas"
    public void MostrarResultado(string texto, SEquipa? comprador)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*") };
        g.Children.Add(CUi.Escudo(_v, 40));
        int col = 1;
        if (comprador != null)
        {
            var seta = CUi.Txt("→", 22, CUi.Retro ? W95.Marinho : TemaVerde.Verde, FontWeight.Bold);
            seta.Margin = new Thickness(8, 0);
            Grid.SetColumn(seta, col++); g.Children.Add(seta);
            var e = CUi.Escudo(comprador, 40);
            Grid.SetColumn(e, col++); g.Children.Add(e);
        }
        var t = CUi.Txt(texto, 16, comprador != null ? CUi.Bom : CUi.Texto, FontWeight.Bold, true);
        t.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(t, 3); g.Children.Add(t);
        _folha.Rodape(g);
    }
}
