namespace RetroFoot.App;

// Liga as janelas do mercado aos ganchos do motor (RetroFoot.Motor.Ciclo.Humano):
//   Lance        -> TelaLeilao.Lance: o painel do lance humano (TsellPlayerDlg)
//   PedirAumento -> TelaPedidoSalario.Perguntar: o TaskMoreSalaryDlg (modos 0..3)
//   Mercado.Janela -> TelaLeilao.JanelaDoMotor: a janela de todo leilao (TsellPlayerDlg)
// Os ganchos sao lidos na hora de cada chamada (JogoEmCurso.Interacao()), mas o certo e
// ligar antes de Sessao.Comecar().
//
// Da tela da equipa (thread da interface, motor parado) chamam-se direto:
//   TelaLeilao.Vender(s, e, p, mensagem, fim)   Jogador > Vender
//   TelaNovoSalario.Abrir(s, e, p, fim)         Renovar contrato...
static class LigacaoMercado
{
    public static void Ligar(Sessao s)
    {
        s.J.H.Lance = TelaLeilao.Lance(s);
        s.J.H.PedirAumento = (e, p, novo, modo) => TelaPedidoSalario.Perguntar(s, e, p, novo, modo);
        // a janela do leilao em todo leilao do motor (MercadoJogo.Janela)
        s.Mercado.Janela = (l, rodar) => TelaLeilao.JanelaDoMotor(s, l, rodar);
    }
}
