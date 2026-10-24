using System;
using Avalonia.Media;
using RetroFoot.Motor.Mercado;
using T = RetroFoot.App.TextosMercado;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;

namespace RetroFoot.App;

// TaskMoreSalaryDlg.Execute (7:330e), 369x260: o formulario na cor do fundo da equipa e os
// textos na cor da letra; o nome do treinador e a mensagem, conforme o modo:
//   0: "<jogador> pede que lhe seja aumentado o salario para <n><moeda>.\r" + "Se nao
//      aceitar o aumento, o jogador sera posto a venda em leilao." + "\r\rAceita o aumento?"
//   1: o mesmo pedido + "Uma vez que este jogador nao pode ser vendido, tem de aceitar o
//      aumento." (so OK)
//   2: "Nem pensar! Exijo <n><moeda>!\r" + "\r\rAceita o aumento?"
//   3: "Nem pensar! Exijo <n><moeda>!\r\r" + "Uma vez que ..." (so OK)
// O valor vai com IntToStr (sem separador) e o sufixo da moeda. Sim e OK = mrYes (true);
// Nao e o fechar da janela = false.
sealed class TelaPedidoSalario : DialogoMercado
{
    static readonly Fonte Arial9b = new(W95.Arial13.Familia, 9, true);

    TelaPedidoSalario(Sessao s, SEquipa e, SJogador p, int novo, int modo, Action<bool> fim)
        : base(Idioma.T("Atualização de Salário"), 369, 260, T.Cores(e).Fundo, logo: Escudos.Escudo(e))
    {
        var (letra, _) = T.Cores(e);
        var m = Moeda.DoSave(s.S);
        string sal = FormatoDinheiro.Valor(m, novo);
        string pedido = Idioma.T("{0} pede que lhe seja aumentado o salário para {1}.", T.NomeJogador(p), sal) + "\n";
        string leilao = Idioma.T("Se não aceitar o aumento, o jogador será posto à venda em leilão.");
        string aceita = "\n\n" + Idioma.T("Aceita o aumento?");
        string obrigado = Idioma.T("Uma vez que este jogador não pode ser vendido, tem de aceitar o aumento.");
        string msg = modo switch
        {
            0 => pedido + leilao + aceita,
            1 => pedido + obrigado,
            2 => Idioma.T("Nem pensar! Exijo {0}!", sal) + "\n" + aceita,
            _ => Idioma.T("Nem pensar! Exijo {0}!", sal) + "\n\n" + obrigado,
        };
        // (no Moderno a janela e do tema, sem o fundo da equipa: a letra da equipa ficava ilegivel
        // ou sem sentido, como o vermelho no verde; o nome no destaque e o texto no do tema)
        Rotulo(T.NomeTreinador(s.J.M.TreinadorDa(e)), 16, 24, 340, 19, W95.Arial16b, Moderno ? MM.TextoEscolhido : letra);
        Por(new TextoMercado { Texto = msg, Fonte = W95.Arial13b, Cor = Moderno ? MM.Texto : letra, Quebra = true }, 20, 56, 329, 113);
        bool soOk = modo % 2 == 1;
        if (soOk)
        {
            var ok = Por(BotaoSim(Idioma.T("OK")), 152, 188, 64, 64);
            ok.Clique += () => fim(true);
            AoEnter = () => fim(true);
        }
        else
        {
            var sim = Por(BotaoSim(Idioma.T("Sim")), 98, 188, 64, 64);
            var nao = Por(new Botao { Texto = Idioma.T("Não"), Fonte = Arial9b, Glifo = W95.Imagem("Imagens/salario-nao.png"), GlifoEmCima = true }, 206, 188, 64, 64);
            sim.Clique += () => fim(true);
            nao.Clique += () => fim(false);
            // o foco comeca no Sim (TabOrder 0): Enter = Sim
            AoEnter = () => fim(true);
        }
        J.AoFechar = () => fim(false);
        if (Celular.Ativo)
        {
            // no celular: a pergunta numa folha (o Voltar e o fechar da janela: Nao)
            var botoes = soOk
                ? new (string, bool, Action)[] { (Idioma.T("OK"), true, () => fim(true)) }
                : new (string, bool, Action)[] { (Idioma.T("Não"), false, () => fim(false)), (Idioma.T("Sim"), true, () => fim(true)) };
            Content = MercadoFormsCelular.Pedido(e, T.NomeTreinador(s.J.M.TreinadorDa(e)), msg, botoes, () => fim(false));
            Width = Height = double.NaN;
        }
    }

    static Botao BotaoSim(string texto) =>
        new() { Texto = texto, Fonte = Arial9b, Glifo = W95.Imagem("Imagens/salario-sim.png"), GlifoEmCima = true };

    // na thread do motor (ou de uma tarefa): abre por cima da tela e espera a resposta.
    // E o gancho Humano.PedirAumento / Interacao.PedirAumento.
    public static bool Perguntar(Sessao s, SEquipa e, SJogador p, int novo, int modo) =>
        s.EsperarDialogo<bool>(fim => new TelaPedidoSalario(s, e, p, novo, modo, fim));
}
