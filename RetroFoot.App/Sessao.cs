using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Save;
using Equipa = RetroFoot.Motor.Save.Equipa;

namespace RetroFoot.App;

// A ponte entre o motor e as telas. O motor (JogoEmCurso.Ciclo) roda numa thread propria
// e, quando precisa do humano (preparar a equipa, a jornada, a classificacao...), chama um
// gancho que mostra a tela na thread da interface e espera a resposta. Enquanto espera, o
// motor esta parado: as telas podem ler e mexer no save (S) a vontade. Durante a jornada,
// cada minuto e entregue a tela com o motor parado (Invoke sincrono) e so depois o motor
// dorme o tempo do minuto.
public sealed class Sessao
{
    public JogoEmCurso J { get; }
    public Jogo98 S => J.M.S;
    public MercadoJogo Mercado => J.Mc;
    // o arquivo do save (null = jogo novo ainda nao gravado)
    public string? Arquivo { get; set; }
    // milissegundos por minuto de jogo (a velocidade); 0 = sem esperar. Sem valor fixo, o do
    // perfil de tempo das Opcoes (Opcoes.Velocidade), refeito no comeco de cada jornada
    public int MsPorMinuto { get => msFixo ?? msPerfil; set => msFixo = value; }
    int? msFixo;
    int msPerfil = 120;
    // RetroFoot > Sair sem salvar (add-in 0x46): o motor para sem gravar o save
    public bool SemGravar { get; set; }
    // a equipa humana da tela da equipa aberta (a compradora no plantel aberto pelas listas)
    public Equipa? EquipaHumana { get; set; }
    // o ultimo dialogo fechou (a tela de baixo recupera o foco e se redesenha)
    public event Action? SemDialogos;

    readonly Action<Control> mostrar;
    readonly Action<Control> mostrarRaiz;
    // a tela atual fica em camadaTela; os dialogos (janelas modais do original) por cima, em
    // camadaDialogo, num quadro de 800x600 ampliado como as telas
    readonly Grid host = new();
    readonly Border camadaTela = new();
    readonly Grid camadaDialogo = new() { IsVisible = false };
    bool hostMostrado;
    Thread? motor;
    TelaJornada? jornada;

    // na thread do motor: redesenha a jornada com o estado de agora (LigacaoPartida, antes das
    // janelas que o motor abre no meio de um tique)
    internal void AtualizarJornada(Jornada jn) => Dispatcher.UIThread.Invoke(() => jornada?.Atualizar(jn));

    // o tempo que a tela final da jornada fica antes da classificacao (0 nos testes automaticos)
    public static int PausaFimJornadaMs = 3000;

    public Sessao(Jogo98 s, Action<Control> mostrar, Opcoes? op = null, string? arquivo = null)
    {
        host.Children.Add(camadaTela);
        host.Children.Add(camadaDialogo);
        mostrarRaiz = mostrar;
        this.mostrar = c =>
        {
            MostrarHost();
            camadaTela.Child = c;
        };
        Arquivo = arquivo;
        var h = new Humano
        {
            Preparar = (_, e, jogo) =>
            {
                GravarAutomatico(e, jogo);
                return Esperar<bool>(fim =>
                {
                    var t = new TelaEquipa(this, e, jogo);
                    t.Terminou += sair => fim(sair);
                    return t;
                });
            },
            Substituicao = (jg, lado, saiu, motivo) =>
            {
                // primeira versao: na lesao e no goleiro expulso entra o 1o reserva possivel;
                // no intervalo, sem trocas
                if (motivo != 4 && saiu != null) JogoEmCurso.SubstituicaoForcada(jg.Equipa[lado]!, saiu, 0);
            },
            ComecoJornada = (titulo, jn) => Dispatcher.UIThread.Invoke(() =>
            {
                msPerfil = OpcoesDoJogo.Intervalo(J.Op.Velocidade, jn);
                jornada = new TelaJornada(this, titulo, jn);
                this.mostrar(jornada);
            }),
            Minuto = jn =>
            {
                Dispatcher.UIThread.Invoke(() => jornada?.Minuto(jn));
                int ms = MsPorMinuto;
                if (ms > 0) Thread.Sleep(ms);
            },
            // o original fecha a jornada sozinho quando o relogio passa de Fim + 5 (seg10:08DB);
            // aqui a tela final da jornada fica 3 s (PausaFimJornadaMs) e so entao vem a classificacao
            FimJornada = jn =>
            {
                Dispatcher.UIThread.Invoke(() => jornada?.Fim(jn, () => { }));
                if (PausaFimJornadaMs > 0) Thread.Sleep(PausaFimJornadaMs);
            },
            // a proposta de patrocinio (Opcoes.Patrocinios): a janela de Sim/Nao do patch
            Patrocinio = p => EsperarDialogo<bool>(fim => new TelaPatrocinio(p, J.Op.Marcas, Moeda.DoSave(S), fim)),
        };
        J = new JogoEmCurso(s, (uint)Environment.TickCount, op ?? Opcoes.Padrao, h);
        // o banco e as substituicoes das regras deste jogo (5 e 3 = como no classico)
        RetroFoot.Motor.Rodada.Escalacao.MaxReservas = J.M.Regras.MaxReservas;
        RetroFoot.Motor.Rodada.Escalacao.MaxSubstituicoes = J.M.Regras.MaxSubstituicoes;
        // sem registo: DS:29DE = 0x64 (Registo.Nivel), todos os add-ins ligados
        LigacaoPartida.SubstituicoesDuranteJogo = Registo.AddIn(0x12);
        LigacaoPartida.VerEscalacaoDosOutros = Registo.AddIn(0x14);
        // RETROFOOT_PATROC_TESTE=1: o PATROC_TESTE do patch (propostas em toda rodada)
        J.Pat.Teste = Environment.GetEnvironmentVariable("RETROFOOT_PATROC_TESTE") == "1";
        // a classificacao depois de cada jornada do campeonato, com os jogos ja fechados
        J.M.G.Classificacao = (m, _) => Esperar<bool>(fim =>
        {
            var c = new TelaClassificacao(this);
            c.Fechou += () => fim(true);
            return c;
        });
        // as janelas do mercado (leilao, pedido de aumento) e da partida (substituicao,
        // penalti, disputa de penaltis)
        // a janela das chicotadas (seg05:025a) depois de cada jornada, da Taca e no fim da
        // temporada
        J.M.G.Chicotadas = m =>
        {
            var l = m.Chicotadas.ToList();
            EsperarNaInterface(fim => TelaListas.Chicotadas(this, this, l, fim));
        };
        // o save retomado no meio de uma jornada nao e gravado de novo nela (DS:2720)
        if (J.M.Retomar != 0) retomado = (J.M.Ano, J.M.Jornada);
        LigacaoMercado.Ligar(this);
        LigacaoPartida.Ligar(this);
        Campeonato.Ligar(this);
        // os planteis abertos das listas (duplo clique no calendario e nas transferencias,
        // com a compra directa) e do convite; os botoes do plantel que abrem outras janelas
        TelaListas.AbrirPlantel = (eq, id) => TelaPlantel.Abrir(this, eq, EquipaHumana, id);
        TelaTreinadores.AbrirPlantel = eq => TelaPlantel.Abrir(this, eq, null);
        TelaClassificacao.AbrirEquipa = (eq, jornada, fechou) => TelaPlantel.Abrir(this, eq, jornada ? null : EquipaHumana, 0, fechou);
        TelaPlantel.Calendario = (ss, eq, pronto) => TelaListas.Calendario(ss, eq, pronto);
        TelaPlantel.Balanco = Registo.AddIn(0x11) ? (ss, eq, pronto) => TelaBalanco.Abrir(ss, eq, pronto) : null;
        TelaPlantel.Emprestimo = Registo.AddIn(0x13) ? (ss, eq, pronto) => TelaEmprestimo.Abrir(ss, eq, pronto) : null;
    }

    // comeca (ou continua) o jogo na thread do motor
    public void Comecar()
    {
        motor = new Thread(() =>
        {
            try
            {
                J.Ciclo(Campeonato.AoFim(this));
                if (J.Gravado != null && Arquivo != null && !SemGravar) File.WriteAllBytes(Arquivo, J.Gravado);
                Dispatcher.UIThread.Post(() => Saiu?.Invoke());
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => Erro?.Invoke(ex));
            }
        }) { IsBackground = true, Name = "motor" };
        motor.Start();
    }

    // o jogo acabou (o humano saiu e o save foi gravado) / o motor parou com erro
    public event Action? Saiu;
    public event Action<Exception>? Erro;

    // grava o save no arquivo atual (so com o motor parado num gancho: na tela da equipa)
    public void Gravar(string? arquivo = null)
    {
        Arquivo = arquivo ?? Arquivo ?? throw new InvalidOperationException("sem arquivo");
        File.WriteAllBytes(Arquivo, J.Gravar());
    }

    // TprepareGameDlg.FormShow (4:21dd): a gravacao automatica (Opcoes > Gravar, DS:2B3C):
    // 1 todas as jornadas, 2 de 3 em 3, 3 so na 1a; nunca na jornada em que o jogo foi
    // retomado (DS:2720). Grava como o Sair: DS:271E = a equipa e DS:271A = o jogo e da Taca.
    (int Ano, int Jornada)? retomado;
    void GravarAutomatico(Equipa e, Jogo? jogo)
    {
        var m = J.M;
        int q = J.Op.GravarQuando, jn = m.Jornada;
        if (!(q == 1 || (q == 2 && jn % 3 == 0) || jn == 1)) return;
        if (retomado == (m.Ano, jn) || Arquivo == null) return;
        short antes = m.Retomar;
        bool jogada = m.JornadaJogada;
        try
        {
            m.Retomar = (short)e.ID;
            m.JornadaJogada = jogo != null && jogo.Taca != 0;
            Gravar();
        }
        catch (Exception ex) { Console.Error.WriteLine("gravar: " + ex.Message); }
        finally { m.Retomar = antes; m.JornadaJogada = jogada; }
    }

    // na thread do MOTOR: abrir() roda na thread da interface e chama o "fim" quando acabar
    public void EsperarNaInterface(Action<Action> abrir)
    {
        var tcs = new TaskCompletionSource<bool>();
        Dispatcher.UIThread.Post(() => abrir(() => tcs.TrySetResult(true)));
        tcs.Task.GetAwaiter().GetResult();
    }

    // ---------------------------------------------------------------- dialogos
    // Abre um dialogo por cima da tela atual (na thread da interface). O controle e posto no
    // centro de um quadro de 800x600 (poScreenCenter do original) ampliado como as telas,
    // com o fundo escurecido. Varios podem ficar empilhados.
    public void AbrirDialogo(Control dialogo)
    {
        // (um dialogo pode vir antes da 1a tela: as propostas de patrocinio do jogo novo)
        MostrarHost();
        var quadro = new Grid { Width = 800, Height = 600 };
        dialogo.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        dialogo.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        quadro.Children.Add(dialogo);
        var camada = new Grid { Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0x55, 0, 0, 0)) };
        camada.Children.Add(new Viewbox { Stretch = Avalonia.Media.Stretch.Uniform, Child = quadro });
        camada.Tag = dialogo;
        // visivel antes de entrar: o Focus() que o dialogo faz ao entrar na arvore so pega
        // num controle visivel
        camadaDialogo.IsVisible = true;
        camadaDialogo.Children.Add(camada);
        // o dialogo que ja pos o foco num controle seu ao entrar na arvore (a lista da
        // substituicao forcada, o campo do preco...) fica com ele: tirar o foco de la deixava
        // as setas sem efeito e o OK desligado, sem saida pelo teclado
        var foco = TopLevel.GetTopLevel(dialogo)?.FocusManager?.GetFocusedElement() as Avalonia.Visual;
        if (foco == null || !Avalonia.VisualTree.VisualExtensions.IsVisualAncestorOf(dialogo, foco)) dialogo.Focus();
    }

    void MostrarHost()
    {
        if (hostMostrado) return;
        hostMostrado = true;
        if (camadaTela.Child == null) camadaTela.Child = new Border { Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0, 128, 0)) };
        mostrarRaiz(host);
    }

    // fecha o dialogo (o de cima, se for null)
    public void FecharDialogo(Control? dialogo = null)
    {
        for (int i = camadaDialogo.Children.Count - 1; i >= 0; i--)
            if (dialogo == null || camadaDialogo.Children[i].Tag == dialogo)
            {
                camadaDialogo.Children.RemoveAt(i);
                break;
            }
        camadaDialogo.IsVisible = camadaDialogo.Children.Count > 0;
        // (depois do que o fechar abrir em seguida: um dialogo que abre outro ao fechar)
        if (camadaDialogo.Children.Count == 0)
            Dispatcher.UIThread.Post(() => { if (camadaDialogo.Children.Count == 0) SemDialogos?.Invoke(); });
    }

    // na thread do MOTOR (dentro de um gancho): abre o dialogo que criar() devolve, espera ele
    // chamar o "fim" com a resposta, fecha e devolve a resposta
    public T EsperarDialogo<T>(Func<Action<T>, Control> criar)
    {
        NaoNaInterface();
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Control? d = null;
        Dispatcher.UIThread.Post(() =>
        {
            // um erro ao montar o dialogo vai para o motor (Erro): sem isto o motor esperava
            // para sempre uma resposta de uma janela que nunca abriu (o jogo congelava)
            try
            {
                d = criar(v => { Dispatcher.UIThread.Post(() => FecharDialogo(d)); tcs.TrySetResult(v); });
                AbrirDialogo(d);
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        return tcs.Task.GetAwaiter().GetResult();
    }
    // troca a tela mostrada (ex.: as Configuracoes por cima da tela da equipa e a volta)
    public void Mostrar(Control tela) => mostrar(tela);

    public Equipa? Equipa(ushort id) => J.M.Equipa(id);
    public Treinador? TreinadorDa(Equipa e) => J.M.TreinadorDa(e);

    // na thread do motor: mostra a tela que criar() devolve e espera ela chamar o "fim"
    T Esperar<T>(Func<Action<T>, Control> criar)
    {
        NaoNaInterface();
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Control? tela = null;
        Dispatcher.UIThread.Post(() =>
        {
            // (como em EsperarDialogo: o erro ao montar a tela vai para o motor)
            try
            {
                tela = criar(v =>
                {
                    // a tela acabou: volta a janela principal verde (TmainWindow), que e o que o
                    // original mostra por tras dos dialogos do computador (leiloes, propostas...)
                    // ate a proxima tela
                    Dispatcher.UIThread.Post(() => { if (camadaTela.Child == tela) mostrar(FundoVerde()); });
                    tcs.TrySetResult(v);
                });
                mostrar(tela);
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        return tcs.Task.GetAwaiter().GetResult();
    }

    // esperar na thread da interface pela propria interface trava o jogo: melhor um erro
    static void NaoNaInterface()
    {
        if (Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("Sessao: espera de uma janela na thread da interface (travaria)");
    }

    static Control FundoVerde() => new Border { Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0, 128, 0)) };
}
