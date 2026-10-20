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

// As janelas pequenas do mercado no celular: a pagina Vender (PaginaVender), a renovacao de
// contrato (TelaNovoSalario) e o pedido de aumento (TelaPedidoSalario), como folhas de baixo.
// Os valores continuam nos campos das janelas do computador (que validam como o original);
// estas folhas so os escrevem e apertam os mesmos botoes.
static class MercadoFormsCelular
{
    // a pagina Vender: o preco de venda em milhares (MaxLength 6), o Vender e o Cancelar
    public static CUi.Folha Vender(Sessao s, SEquipa e, SJogador? p, Action<string> mudou, Action vender, Action cancelar)
    {
        var m = Moeda.DoSave(s.S);
        if (CUi.Retro) return VenderRetro(s, e, p, mudou, vender, cancelar);
        var corpo = new StackPanel { Spacing = 12 };
        if (p != null)
            corpo.Children.Add(CUi.Cartao_(null, new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    MercadoCelular.Cabecalho(p, null),
                    CUi.Linha_(T("Salário"), FormatoDinheiro.Valor(m, p.Salario)),
                },
            }));
        corpo.Children.Add(CUi.SecaoFolha(T("Preço de venda\n(milhares de {0})", m.Nome).Replace('\n', ' ')));
        int passo = Math.Max(1, 50 * Math.Max(1, m.D43));
        var valor = CUi.Txt("", 14, CUi.Fraco, FontWeight.SemiBold);
        valor.HorizontalAlignment = HorizontalAlignment.Center;
        TextBox caixa = null!;
        (Control campo, caixa) = MercadoCelular.Numero("", 6, d =>
        {
            int v = MercadoCelular.Valor(caixa) ?? 0;
            caixa.Text = Math.Clamp(v + d * passo, 0, 999999).ToString();
        });
        void Escrever()
        {
            var t = caixa.Text ?? "";
            mudou(t);
            valor.Text = MercadoCelular.Valor(caixa) is int v ? "= " + FormatoDinheiro.Valor(m, (long)v * 1000) : "";
        }
        caixa.TextChanged += (_, _) => Escrever();
        caixa.KeyDown += (_, ev) => { if (ev.Key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Return) { ev.Handled = true; vender(); } };
        corpo.Children.Add(campo);
        corpo.Children.Add(valor);
        corpo.Children.Add(CUi.Txt(T("O jogador vai a leilão com este preço base."), 13, CUi.Fraco, FontWeight.Normal, true));
        var rodape = CUi.Botoes(new CUi.Botao(T("Cancelar"), false, cancelar), new CUi.Botao(T("Vender"), true, vender));
        return new CUi.Folha(T("Vender"), corpo, rodape, TM.Texto(e.NomeCompleto), CUi.Escudo(e, 32), fechar: cancelar);
    }

    // a renovacao de contrato: o novo salario no Spin da janela (de 50 a 50000 x a moeda, de
    // 50 em 50), o Propor e o Cancelar
    public static CUi.Folha Salario(Sessao s, SEquipa e, SJogador p, SpinMercado spin, Action propor, Action cancelar)
    {
        var m = Moeda.DoSave(s.S);
        if (CUi.Retro) return SalarioRetro(e, p, spin, propor, cancelar);
        var corpo = new StackPanel { Spacing = 12 };
        corpo.Children.Add(CUi.Cartao_(null, new StackPanel
        {
            Spacing = 10,
            Children =
            {
                MercadoCelular.Cabecalho(p, null),
                CUi.Linha_(T("Salário"), FormatoDinheiro.Valor(m, p.Salario)),
            },
        }));
        corpo.Children.Add(CUi.SecaoFolha(T("Novo salário:").TrimEnd(':')));
        var valor = CUi.Txt("", 14, CUi.Fraco, FontWeight.SemiBold);
        valor.HorizontalAlignment = HorizontalAlignment.Center;
        TextBox caixa = null!;
        bool trocando = false;
        (Control campo, caixa) = MercadoCelular.Numero(spin.Campo.Texto, 6, d =>
        {
            // as setas do TSpinEdit: o valor (preso entre o minimo e o maximo) mais ou menos o passo
            spin.Valor += d * spin.Passo;
            trocando = true;
            caixa.Text = spin.Campo.Texto;
            trocando = false;
            Escrever();
        });
        void Escrever()
        {
            if (!trocando) spin.Campo.Texto = caixa.Text ?? "";
            valor.Text = "= " + FormatoDinheiro.Valor(m, spin.Valor);
        }
        caixa.TextChanged += (_, _) => Escrever();
        caixa.KeyDown += (_, ev) => { if (ev.Key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Return) { ev.Handled = true; propor(); } };
        Escrever();
        corpo.Children.Add(campo);
        corpo.Children.Add(valor);
        corpo.Children.Add(CUi.Txt(FormatoDinheiro.Valor(m, spin.Min) + " – " + FormatoDinheiro.Valor(m, spin.Max), 13, CUi.Fraco, FontWeight.Normal, true));
        var rodape = CUi.Botoes(new CUi.Botao(T("Cancelar"), false, cancelar), new CUi.Botao(T("Propor"), true, propor));
        return new CUi.Folha(T("Renovar contrato"), corpo, rodape, TM.Texto(e.NomeCompleto), CUi.Escudo(e, 32), fechar: cancelar);
    }

    // o pedido de aumento (TaskMoreSalaryDlg): o treinador, a mensagem e Sim / Nao (ou OK)
    public static CUi.Folha Pedido(SEquipa e, string treinador, string msg, IReadOnlyList<(string Texto, bool Principal, Action Acao)> botoes, Action voltar)
    {
        if (CUi.Retro) return PedidoRetro(e, treinador, msg, botoes, voltar);
        var corpo = new StackPanel { Spacing = 10 };
        corpo.Children.Add(CUi.NomeEquipa(e, TM.NomeEquipa(e), 15, true));
        corpo.Children.Add(CUi.Txt(msg.Replace('\r', '\n').Trim('\n'), 16, null, FontWeight.Normal, true));
        var bs = new List<Control>();
        foreach (var (t, pr, a) in botoes) bs.Add(new CUi.Botao(t, pr, a));
        return new CUi.Folha(T("Atualização de Salário"), corpo, CUi.Botoes(bs.ToArray()), treinador, CUi.Escudo(e, 32), fechar: null) { AoVoltar = voltar };
    }

    // ------------------------------------------------------------ o Retro
    // As paginas do computador em pe: o formulario na cor do fundo da equipa, os textos na
    // cor da letra, o titulo grande no meio e os TBitBtn com o glifo embaixo.

    // o titulo da pagina ("Vender", "Renovar contrato"), Arial 19 negrito no meio
    static TextBlock TituloRetro(string t, IBrush letra)
    {
        var x = MercadoRetro.Txt(t, 21, letra, true, true);
        x.HorizontalAlignment = HorizontalAlignment.Center;
        x.TextAlignment = TextAlignment.Center;
        return x;
    }

    // o jogador da pagina (no computador ele esta escolhido na lista da equipa, atras)
    static Control JogadorRetro(SJogador p, IBrush letra)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        if (CUi.Bandeira(TM.PaisDe(p.Pais), 22) is { } b) s.Children.Add(b);
        s.Children.Add(MercadoRetro.Txt(TM.NomeJogador(p), 16, letra, true));
        s.Children.Add(MercadoRetro.Txt(TM.Posicao(p) + " · " + p.Forca, 14, letra));
        return s;
    }

    static CUi.Folha FolhaRetro(SEquipa e, string? sub, Control corpo, Control rodape, Action? fechar)
    {
        var f = new CUi.Folha(TM.Texto(e.NomeCompleto), corpo, rodape, sub, CUi.Bandeira(TM.PaisDe(e.Pais), 30) ?? CUi.Escudo(e, 30), fechar: fechar);
        MercadoRetro.Pintar(f, TM.Cores(e).Fundo);
        return f;
    }

    // a pagina Vender (273x329): "Vender", "Preco de venda (milhares de ...)", o TEdit e os
    // botoes Vender (o visto) e Cancelar (o X)
    static CUi.Folha VenderRetro(Sessao s, SEquipa e, SJogador? p, Action<string> mudou, Action vender, Action cancelar)
    {
        var m = Moeda.DoSave(s.S);
        var (letra, _) = TM.Cores(e);
        var corpo = new StackPanel { Spacing = 14, Margin = new Thickness(0, 6, 0, 6) };
        corpo.Children.Add(TituloRetro(T("Vender"), letra));
        if (p != null) corpo.Children.Add(JogadorRetro(p, letra));
        var rot = MercadoRetro.Txt(T("Preço de venda\n(milhares de {0})", m.Nome), 15, letra, false, true);
        rot.Margin = new Thickness(0, 6, 0, 0);
        corpo.Children.Add(rot);
        var caixa = MercadoRetro.Edit("", 6, 19);
        caixa.HorizontalAlignment = HorizontalAlignment.Stretch;
        Avalonia.Input.TextInput.TextInputOptions.SetContentType(caixa, Avalonia.Input.TextInput.TextInputContentType.Number);
        var valor = MercadoRetro.Txt("", 14, letra, true);
        caixa.TextChanged += (_, _) =>
        {
            mudou(caixa.Text ?? "");
            valor.Text = MercadoCelular.Valor(caixa) is int v ? "= " + FormatoDinheiro.Valor(m, (long)v * 1000) : "";
        };
        caixa.KeyDown += (_, ev) => { if (ev.Key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Return) { ev.Handled = true; vender(); } };
        corpo.Children.Add(caixa);
        corpo.Children.Add(valor);
        var rodape = MercadoRetro.Fila(
            MercadoRetro.Botao(T("Vender"), "Imagens/vender.png", 1, vender, true),
            MercadoRetro.Botao(T("Cancelar"), "Imagens/vender-cancelar.png", 1, cancelar));
        return FolhaRetro(e, null, corpo, rodape, cancelar);
    }

    // a pagina Contrato: "Renovar contrato", "Novo salario:" e o TSpinEdit (as setas viram o
    // menos e o mais), Propor e Cancelar
    static CUi.Folha SalarioRetro(SEquipa e, SJogador p, SpinMercado spin, Action propor, Action cancelar)
    {
        var (letra, _) = TM.Cores(e);
        var corpo = new StackPanel { Spacing = 14, Margin = new Thickness(0, 6, 0, 6) };
        corpo.Children.Add(TituloRetro(T("Renovar contrato"), letra));
        corpo.Children.Add(JogadorRetro(p, letra));
        var rot = MercadoRetro.Txt(T("Novo salário:"), 15, letra);
        rot.Margin = new Thickness(0, 6, 0, 0);
        corpo.Children.Add(rot);
        TextBox caixa = null!;
        bool trocando = false;
        (Control campo, caixa) = MercadoCelular.Numero(spin.Campo.Texto, 6, d =>
        {
            spin.Valor += d * spin.Passo;
            trocando = true;
            caixa.Text = spin.Campo.Texto;
            trocando = false;
        });
        caixa.FontSize = 22;
        caixa.TextChanged += (_, _) => { if (!trocando) spin.Campo.Texto = caixa.Text ?? ""; };
        caixa.KeyDown += (_, ev) => { if (ev.Key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Return) { ev.Handled = true; propor(); } };
        corpo.Children.Add(campo);
        var rodape = MercadoRetro.Fila(
            MercadoRetro.Botao(T("Propor"), "Imagens/salario-propor.png", 1, propor, true),
            MercadoRetro.Botao(T("Cancelar"), "Imagens/salario-cancelar.png", 1, cancelar));
        return FolhaRetro(e, null, corpo, rodape, cancelar);
    }

    // o TaskMoreSalaryDlg (369x260): o treinador (Arial 16 negrito), a mensagem em negrito e
    // Sim (o visto) e Nao (o sinal de proibido), ou so o OK
    static CUi.Folha PedidoRetro(SEquipa e, string treinador, string msg, IReadOnlyList<(string Texto, bool Principal, Action Acao)> botoes, Action voltar)
    {
        var (letra, _) = TM.Cores(e);
        var corpo = new StackPanel { Spacing = 14, Margin = new Thickness(4, 4, 4, 8) };
        corpo.Children.Add(MercadoRetro.Txt(treinador, 18, letra, true));
        corpo.Children.Add(MercadoRetro.Txt(msg.Replace('\r', '\n').Trim('\n'), 15.5, letra, true, true));
        // (a ordem do computador: Sim a esquerda e Nao a direita)
        var bs = new List<Control>();
        for (int i = botoes.Count - 1; i >= 0; i--)
        {
            var (t, pr, a) = botoes[i];
            bs.Add(MercadoRetro.Botao(t, pr ? "Imagens/salario-sim.png" : "Imagens/salario-nao.png", 1, a, pr));
        }
        var f = new CUi.Folha(T("Atualização de Salário"), corpo, MercadoRetro.Fila(bs.ToArray()), TM.NomeEquipa(e), CUi.Bandeira(TM.PaisDe(e.Pais), 30) ?? CUi.Escudo(e, 30), fechar: null) { AoVoltar = voltar };
        MercadoRetro.Pintar(f, TM.Cores(e).Fundo);
        return f;
    }
}
