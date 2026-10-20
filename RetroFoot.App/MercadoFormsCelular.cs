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
        var corpo = new StackPanel { Spacing = 10 };
        corpo.Children.Add(CUi.NomeEquipa(e, TM.NomeEquipa(e), 15, true));
        corpo.Children.Add(CUi.Txt(msg.Replace('\r', '\n').Trim('\n'), 16, null, FontWeight.Normal, true));
        var bs = new List<Control>();
        foreach (var (t, pr, a) in botoes) bs.Add(new CUi.Botao(t, pr, a));
        return new CUi.Folha(T("Atualização de Salário"), corpo, CUi.Botoes(bs.ToArray()), treinador, CUi.Escudo(e, 32), fechar: null) { AoVoltar = voltar };
    }
}
