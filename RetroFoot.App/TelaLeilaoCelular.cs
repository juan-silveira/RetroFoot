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

    readonly string _valorMercado;

    public LeilaoCelular(Sessao s, SEquipa v, SJogador p, int? preco, int salario, bool launcher)
    {
        _v = v; _preco = preco;
        _moeda = Moeda.DoSave(s.S);
        _valorMercado = TextosMercado.TextoValorMercado(s, p);
        if (CUi.Retro)
        {
            // no Retro, a janela amarela do computador (TsellPlayerDlg) em pe
            _folha = new CUi.Folha(T("Venda de jogador por leilão"), FichaRetro(p, v, preco, salario, launcher), new Border(), fechar: null);
            MercadoRetro.Pintar(_folha, W95.Amarelo);
            _folha.Rodape(null);
            Content = _folha;
            return;
        }
        var corpo = new StackPanel { Spacing = 12 };
        corpo.Children.Add(CUi.Cartao_(null, MercadoCelular.Cabecalho(p, v)));
        var linhas = new List<(string, string)>
        {
            (T("Salário pretendido"), FormatoDinheiro.Valor(_moeda, salario)),
            (T("Preço base"), preco is int b ? FormatoDinheiro.Valor(_moeda, b) : "–"),
        };
        corpo.Children.Add(MercadoCelular.Ficha(p, launcher, linhas, TextosMercado.TextoValorMercado(s, p)));
        _folha = new CUi.Folha(T("Venda de jogador por leilão"), corpo, Esperando(), TM.NomeEquipa(v), CUi.Escudo(v, 32), fechar: null);
        Content = _folha;
    }

    // ------------------------------------------------------------ o Retro
    // a ficha da janela do computador (2:3002): a esquerda Time (nas cores do vendedor),
    // Jogador, Posicao, Forca, Salario pretendido e Preco base; a direita a Nacionalidade com
    // a bandeira; embaixo o suspenso / lesionado em vermelho e o Panel1 com o Historico
    Control FichaRetro(SJogador p, SEquipa v, int? preco, int salario, bool launcher)
    {
        var preto = W95.Preto;
        var s = new StackPanel { Spacing = 6 };
        var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var esq = new StackPanel { Spacing = 2 };
        var time = new Grid { ColumnDefinitions = new ColumnDefinitions("62,*"), MinHeight = 30 };
        time.Children.Add(MercadoRetro.Txt(T("Time"), 14, preto));
        var nome = CUi.NomeEquipa(v, TM.NomeEquipa(v), 16, true);
        Grid.SetColumn(nome, 1); time.Children.Add(nome);
        esq.Children.Add(time);
        esq.Children.Add(MercadoRetro.Linha(T("Jogador"), TM.NomeJogador(p), preto, 136, 14.5));
        esq.Children.Add(MercadoRetro.Linha(T("Posição"), TM.Posicao(p), preto, 136, 14.5));
        var f = new Grid { ColumnDefinitions = new ColumnDefinitions("136,*"), MinHeight = 30 };
        f.Children.Add(MercadoRetro.Txt(T("Força"), 14.5, preto));
        MercadoRetro.Col(f, 1, p.Forca.ToString(), preto, true, 18);
        esq.Children.Add(f);
        esq.Children.Add(MercadoRetro.Linha(T("Salário pretendido"), FormatoDinheiro.Valor(_moeda, salario), preto, 136, 14.5));
        esq.Children.Add(MercadoRetro.Linha(T("Preço base"), preco is int b ? FormatoDinheiro.Valor(_moeda, b) : "", preto, 136, 14.5));
        topo.Children.Add(esq);
        string pais = TM.PaisDe(p.Pais);
        var dir = new StackPanel { Spacing = 2, Width = 92, Margin = new Thickness(8, 0, 0, 0) };
        dir.Children.Add(MercadoRetro.Txt(T("Nacionalidade"), 13, preto));
        dir.Children.Add(MercadoRetro.Txt(TM.NomePais(pais), 14, preto, true, true));
        if (CUi.Bandeira(pais, 64) is { } band) { band.Margin = new Thickness(0, 6, 0, 0); band.HorizontalAlignment = HorizontalAlignment.Left; dir.Children.Add(band); }
        Grid.SetColumn(dir, 1); topo.Children.Add(dir);
        s.Children.Add(topo);
        MercadoRetro.Avisos(s, p, W95.Vermelho);
        var ficha = MercadoRetro.Ficha(p, launcher, W95.Amarelo, preto, _valorMercado);
        ficha.Margin = new Thickness(0, 4, 0, 0);
        s.Children.Add(ficha);
        return s;
    }

    // o PanelHumanOffer do computador nas cores da equipa que licita: o treinador, a
    // "Oferta (milhares de...)" com a caixa, o "Dinheiro em caixa: ..." e o OK com o visto
    void PainelRetro(SEquipa c, string treinador, string dinheiro, char senha)
    {
        var (letra, fundo) = TM.Cores(c);
        var p = new StackPanel { Spacing = 6 };
        p.Children.Add(MercadoRetro.Txt(treinador, 16, letra, true));
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,84") };
        var esq = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        esq.Children.Add(MercadoRetro.Txt(T("Oferta (milhares de{0})", _moeda.Sufixo), 14, letra));
        var caixa = MercadoRetro.Edit("", 5, 19);
        if (senha != '\0') caixa.PasswordChar = senha;
        Avalonia.Input.TextInput.TextInputOptions.SetContentType(caixa, Avalonia.Input.TextInput.TextInputContentType.Number);
        caixa.TextChanged += (_, _) => { if (_erro != null) _erro.IsVisible = false; Mudou?.Invoke(caixa.Text ?? ""); };
        caixa.KeyDown += (_, e) => { if (e.Key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Return) { e.Handled = true; Licitou?.Invoke(); } };
        Caixa = caixa;
        esq.Children.Add(caixa);
        g.Children.Add(esq);
        var ok = MercadoRetro.Botao(T("OK"), "Imagens/leilao-ok.png", 1, () => Licitou?.Invoke(), true);
        ok.HorizontalAlignment = HorizontalAlignment.Stretch;
        ok.MinHeight = 80;
        Grid.SetColumn(ok, 2); g.Children.Add(ok);
        p.Children.Add(g);
        p.Children.Add(MercadoRetro.Txt(dinheiro, 14, letra, false, true));
        _erro = MercadoRetro.Txt(T("A oferta é maior que o dinheiro em caixa."), 14, letra, true, true);
        _erro.IsVisible = false;
        p.Children.Add(_erro);
        _folha.Rodape(new Border { Background = fundo, BorderBrush = W95.Preto, BorderThickness = new Thickness(1), Padding = new Thickness(10, 8), Child = p });
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
        if (CUi.Retro) { PainelRetro(c, treinador, dinheiro, senha); return; }
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
        // (no Retro, como no computador: sem o painel, nada embaixo)
        _folha.Rodape(CUi.Retro ? null : Esperando());
    }

    // "Vendido ao X por Y" (os escudos do vendedor e do comprador) ou "Nao houve ofertas"
    public void MostrarResultado(string texto, SEquipa? comprador)
    {
        if (CUi.Retro)
        {
            // textTransferred: o texto em negrito, preto sobre o amarelo
            _folha.Rodape(MercadoRetro.Txt(texto, 17, W95.Preto, true, true));
            return;
        }
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
