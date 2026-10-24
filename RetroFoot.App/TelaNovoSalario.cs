using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using RetroFoot.Motor.Mercado;
using T = RetroFoot.App.TextosMercado;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;

namespace RetroFoot.App;

// o fim da renovacao de contrato: Aceito = o salario novo ficou (+0x16 = 1); Vendido e
// NaoVendido = o jogador recusou o "Exijo" e foi a leilao
enum ResultadoSalario { Cancelado, Aceito, Vendido, NaoVendido }

// A pagina Contrato da TprepareGameDlg ("Renovar contrato", 273x329, nas cores da equipa)
// numa janela com o nome da equipa. ChangeSalaryBtnClick (4:3f87): so se o salario ainda
// nao foi revisto (+0x16 = 0); o TSpinEdit comeca no salario atual, passo 50 x moeda, de
// 50 x moeda a 50000 x moeda. Propor (newSalaryBtnClick, 4:470c -> MercadoJogo.
// ProporSalario): acima de 0,8 x o salario que o jogador pede, aceita; senao ele exige
// (TaskMoreSalaryDlg modo 2, ou 3 se nao pode ser vendido) e, recusado, vai a leilao com
// preco base 0 (a janela do leilao aparece).
sealed class TelaNovoSalario : DialogoMercado
{
    public event Action<int>? Propor;
    public event Action? Cancelou;
    internal readonly SpinMercado Spin;

    TelaNovoSalario(Sessao s, SEquipa e, SJogador p)
        : base(T.Texto(e.NomeCompleto), 273, 329, T.Cores(e).Fundo, logo: Escudos.Escudo(e))
    {
        var (letra, _) = T.Cores(e);
        int moeda = s.Mercado.Moeda;
        Por(new TextoMercado { Texto = Idioma.T("Renovar contrato"), Fonte = new Fonte(W95.Arial13.Familia, 19, true), Cor = letra, Centro = true }, 0, 16, 273, 33);
        Rotulo(Idioma.T("Novo salário:"), 32, 104, 100, 16, W95.Arial13, letra);
        Spin = Por(new SpinMercado(105, 34, new Fonte(W95.Arial13.Familia, 21, true))
        {
            Min = 50 * moeda, Max = 50000 * moeda, Passo = 50 * moeda,
        }, 136, 96, 105, 34);
        Spin.Valor = (int)p.Salario;
        var propor = Por(BotaoGlifo(Idioma.T("Propor"), "salario-propor", W95.Arial13), 56, 200, 64, 64);
        var cancelar = Por(BotaoGlifo(Idioma.T("Cancelar"), "salario-cancelar", W95.Arial13), 152, 200, 64, 64);
        propor.Clique += ClicarPropor;
        cancelar.Clique += () => Cancelou?.Invoke();
        J.AoFechar = () => Cancelou?.Invoke();
        AoEnter = ClicarPropor;
        AoEsc = () => Cancelou?.Invoke();
        Foco = Spin.Campo;
        if (Celular.Ativo)
        {
            // no celular: a folha (MercadoFormsCelular.cs); o valor continua no Spin daqui
            // (o minimo, o maximo e o passo dele)
            Content = MercadoFormsCelular.Salario(s, e, p, Spin, ClicarPropor, () => Cancelou?.Invoke());
            Width = Height = double.NaN;
            Foco = null;
        }
    }

    internal void ClicarPropor() => Propor?.Invoke(Spin.Valor);

    // "Renovar contrato..." da aba Jogador. Na thread da interface, com o motor parado na
    // tela da equipa. fim: o que aconteceu (para a tela refazer a lista e a escalacao).
    public static void Abrir(Sessao s, SEquipa e, SJogador p, Action<ResultadoSalario>? fim = null)
    {
        if (!MercadoJogo.PodeMudarSalario(p)) return;
        var t = new TelaNovoSalario(s, e, p);
        t.Cancelou += () => { s.FecharDialogo(t); fim?.Invoke(ResultadoSalario.Cancelado); };
        t.Propor += v =>
        {
            s.FecharDialogo(t);
            if (s.Remoto is { } rm)
            {
                rm.Executar(new RetroFoot.Motor.Online.Renovar(p.ID, v), r =>
                    fim?.Invoke(r.Motivo switch { "vendido" => ResultadoSalario.Vendido, "nao vendido" => ResultadoSalario.NaoVendido, "aceito" => ResultadoSalario.Aceito, _ => ResultadoSalario.Cancelado }));
                return;
            }
            Task.Run(() =>
            {
                var r = ResultadoSalario.Aceito;
                try { r = Propor_(s, e, p, v); }
                catch (Exception ex) { Console.Error.WriteLine("salario: " + ex); }
                Dispatcher.UIThread.Post(() => fim?.Invoke(r));
            });
        };
        s.AbrirDialogo(t);
    }

    // numa tarefa (o TaskMoreSalaryDlg espera a resposta): ProporSalario com as janelas
    static ResultadoSalario Propor_(Sessao s, SEquipa e, SJogador p, int valor)
    {
        int antes = TelaLeilao.Mostrados;
        var it = new Interacao
        {
            PedirAumento = (e2, p2, novo, modo) => TelaPedidoSalario.Perguntar(s, e2, p2, novo, modo),
            Lance = TelaLeilao.Lance(s),
        };
        bool ficou = s.Mercado.ProporSalario(e, p, valor, it);
        s.J.M.Reindexar();
        if (ficou) return ResultadoSalario.Aceito;
        var (dono, _) = MercadoJogo.EquipaDoJogador(s.S, p.ID);
        bool vendido = dono != e;
        // o motor leiloou la dentro sem avisar a interface: a janela do leilao agora (se um
        // gancho do motor ja nao a mostrou)
        if (TelaLeilao.Mostrados == antes) TelaLeilao.MostrarDepoisDoMotor(s, e, p, vendido);
        return vendido ? ResultadoSalario.Vendido : ResultadoSalario.NaoVendido;
    }
}
