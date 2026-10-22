using System;
using System.Collections.Generic;
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

    // retrato: o jogo online continuado de um retrato (Online/Retrato: a semente e o que o save
    // nao guarda; o host novo depois de uma troca); com ele, s nao e usado (o save vem dele)
    public Sessao(Jogo98 s, Action<Control> mostrar, Opcoes? op = null, string? arquivo = null, RetroFoot.Motor.Online.Retrato? retrato = null)
    {
        host.Children.Add(camadaTela);
        host.Children.Add(camadaDialogo);
        host.Children.Add(relogio);
        host.Children.Add(aviso);
        host.Children.Add(avisosCanto);
        MontarAvisos();
        relogio.PointerPressed += (_, e) => { e.Handled = true; relogioAberto = !relogioAberto; TiqueRelogio(); };
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
                EsconderRelogio();
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
            // (o Enter, o clique ou o Continuar da tela pulam a espera; antes o "continuar" nao
            // fazia nada e so os 3 s contavam)
            FimJornada = jn =>
            {
                using var seguir = new ManualResetEventSlim();
                Dispatcher.UIThread.Invoke(() => jornada?.Fim(jn, () => seguir.Set()));
                if (PausaFimJornadaMs > 0) seguir.Wait(PausaFimJornadaMs);
            },
            // a proposta de patrocinio (Opcoes.Patrocinios): a janela de Sim/Nao do patch
            Patrocinio = p => EsperarDialogo<bool>(fim => new TelaPatrocinio(p, J.Op.Marcas, Moeda.DoSave(S), fim)),
        };
        J = retrato?.Jogo(op ?? Opcoes.Padrao, h) ?? new JogoEmCurso(s, (uint)Environment.TickCount, op ?? Opcoes.Padrao, h);
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

    // ---------------------------------------------------------------- jogo online (host)
    // docs/multiplayer-online.md: o host roda o motor; a preparacao de cada rodada passa pelo
    // HostOnline (todos preparam ao mesmo tempo; a tela da equipa do host abre sem travar o
    // motor) e as perguntas dos times dos outros aparelhos ficam com o padrao do motor (sem
    // janela no host; as perguntas remotas sao a fase 2). Chamar depois de ligar as telas
    // (LigacaoPartida, LigacaoMercado...) e antes de Comecar.
    public RetroFoot.Motor.Online.HostOnline? Online { get; private set; }
    // o jogo num aparelho que nao e o host (sem motor): as acoes que mexem no dinheiro ou noutros
    // times viram comandos para o host
    public JogoRemoto? Remoto { get; init; }

    public void LigarOnline(RetroFoot.Motor.Online.HostOnline host)
    {
        Online = host;
        var h = J.H;
        TaskCompletionSource<bool>? local = null;
        TelaEquipa? telaLocal = null;
        host.PrepararLocal = (e, jogo) =>
        {
            var tcs = local = new TaskCompletionSource<bool>();
            var t = telaLocal = new TelaEquipa(this, e, jogo);
            t.Terminou += sair =>
            {
                tcs.TrySetResult(sair);
                if (!sair) Dispatcher.UIThread.Post(() => { if (camadaTela.Child == t) mostrar(TelaAguardando()); });
            };
            mostrar(t);
            return tcs.Task;
        };
        // o prazo: o relogio; acabou com a tela do host aberta, ela fecha (o time vai com a
        // escalacao que tiver; se nao fecha, a automatica)
        host.PrazoComecou += falta => Dispatcher.UIThread.Post(() => MostrarRelogio(falta));
        host.PrazoAcabou += () => Dispatcher.UIThread.Post(() =>
        {
            FecharDialogo();
            if (local is { Task.IsCompleted: false } l && l.TrySetResult(false) && telaLocal != null && camadaTela.Child == telaLocal)
                mostrar(TelaAguardando());
        });
        h.Preparar = (_, e, jogo) => host.Preparar(e, jogo);
        var sub = h.Substituicao;
        Jornada? jornadaOnline = null;
        h.Substituicao = (jg, lado, saiu, motivo) =>
        {
            if (!host.Remota(jg.Equipa[lado])) { sub?.Invoke(jg, lado, saiu, motivo); return; }
            // o intervalo e o goleiro expulso de um time de outro aparelho: a paralisacao para
            // todos, com as trocas feitas la; a lesao: a troca automatica
            if (motivo is 3 or 4) { host.SubstituicaoRemota(jornadaOnline, jg, lado, motivo); return; }
            if (saiu != null) JogoEmCurso.SubstituicaoForcada(jg.Equipa[lado]!, saiu, 0);
        };
        // (sem janela de leilao no jogo online: um aviso no canto de cada transferencia)
        Mercado.Janela = null;
        host.Transferiu += (t, vendedor) => Dispatcher.UIThread.Post(() => Notificar(global::RetroFoot.App.Online.TextoTransferencia(S, t.W06, t.W0A, vendedor, t.D0C, t.B04)));
        host.ConvidadoMudou += (nome, entrou) => Dispatcher.UIThread.Post(() => Notificar(entrou
            ? Idioma.T("👋 {0} entrou no jogo e assumiu um time da 4ª divisão", nome)
            : Idioma.T("👋 {0} saiu do jogo (o técnico dele foi demitido)", nome)));
        host.PausaMudou += nome => Dispatcher.UIThread.Post(() => MostrarAviso(nome == null ? null : Idioma.T("⏸ {0} está fazendo substituições…", nome)));
        var bat = h.Batedor;
        h.Batedor = (e, titulares, sugerido) => host.Remota(e) ? sugerido : bat != null ? bat(e, titulares, sugerido) : sugerido;
        var aum = h.PedirAumento;
        h.PedirAumento = (e, p, novo, modo) => host.Remota(e) || aum == null || aum(e, p, novo, modo);
        var lance = h.Lance;
        h.Lance = (c, v, p) => host.Remota(c) || lance == null ? (0, false) : lance(c, v, p);
        var conv = h.Convite;
        h.Convite = (t, e) => !host.Remota(J.M.EquipaDoTreinador(t.ID)) && conv != null && conv(t, e);
        var pat = h.Patrocinio;
        h.Patrocinio = p => !host.Remota(PrimeiroHumano()) && pat != null && pat(p);
        host.JogadoresMudaram += l => Dispatcher.UIThread.Post(() => { jogadoresOnline = l; JogadoresOnline?.Invoke(l); });
        // passou o host: a tela do time fecha (o motor sai e a TelaInicio leva para a sala)
        host.HostPassado += _ => Dispatcher.UIThread.Post(() => { FecharDialogo(); EsconderRelogio(); local?.TrySetResult(true); });
        var comeco = h.ComecoJornada;
        h.ComecoJornada = (titulo, jn) =>
        {
            Dispatcher.UIThread.Post(EsconderRelogio);
            // a velocidade da partida das regras da sala; a jornada vai ao vivo para os outros
            J.Op = J.Op with { Velocidade = (short)host.Velocidade };
            jornadaOnline = jn;
            host.TransmitirInicio(titulo, jn);
            comeco?.Invoke(titulo, jn);
        };
        var minuto = h.Minuto;
        h.Minuto = jn => { host.NoMinuto(jn); host.TransmitirMinuto(jn); minuto?.Invoke(jn); };
    }

    // ---------------------------------------------------------------- o relogio do prazo
    // (jogo online) depois do primeiro PRONTO da rodada, o tempo que falta para todos ficarem
    // prontos: por cima de tudo, no alto, em quem espera e em quem ainda mexe no time
    // (compacto por padrao, no canto: so "⏱ 0:55"; um toque mostra a frase inteira e outro esconde)
    readonly Border relogio = new()
    {
        IsVisible = false, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Avalonia.Thickness(0, 4, 6, 0),
        Padding = new Avalonia.Thickness(8, 2), CornerRadius = new Avalonia.CornerRadius(10),
        Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0xD0, 0x10, 0x10, 0x10)),
        BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0xF2, 0xC2, 0x1B)), BorderThickness = new Avalonia.Thickness(1),
        Child = new TextBlock { FontSize = 12, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = Avalonia.Media.Brushes.White },
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
    };
    DispatcherTimer? relogioTimer;
    DateTime relogioFim;
    bool relogioAberto;
    public event Action? RelogioAcabou;

    public void MostrarRelogio(TimeSpan falta)
    {
        relogioFim = DateTime.UtcNow + falta;
        relogioTimer?.Stop();
        relogioTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        relogioTimer.Tick += (_, _) => TiqueRelogio();
        relogioTimer.Start();
        relogio.IsVisible = true;
        TiqueRelogio();
    }

    void TiqueRelogio()
    {
        var r = relogioFim - DateTime.UtcNow;
        if (r <= TimeSpan.Zero) { EsconderRelogio(); RelogioAcabou?.Invoke(); return; }
        string t = $"{(int)r.TotalMinutes}:{r.Seconds:00}";
        ((TextBlock)relogio.Child!).Text = relogioAberto ? Idioma.T("⏱ {0} para todos ficarem prontos", t) : "⏱ " + t;
    }

    // (jogo online) o aviso no alto, no meio: "fulano esta fazendo substituicoes"; null esconde
    readonly Border aviso = new()
    {
        IsVisible = false, IsHitTestVisible = false, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Avalonia.Thickness(0, 40, 0, 0),
        Padding = new Avalonia.Thickness(14, 6), CornerRadius = new Avalonia.CornerRadius(12),
        Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0xE6, 0x10, 0x10, 0x10)),
        BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0xF2, 0xC2, 0x1B)), BorderThickness = new Avalonia.Thickness(1.5),
        Child = new TextBlock { FontSize = 15, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = Avalonia.Media.Brushes.White, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
    };

    // (jogo online) os avisos rapidos (uma transferencia...): somem sozinhos em 6 s, sem parar
    // nada; arrastar para o lado (ou clicar) tira. No celular, no alto, como uma notificacao do
    // sistema; no computador, no canto de baixo
    readonly StackPanel avisosCanto = new() { Spacing = 6 };

    void MontarAvisos()
    {
        if (Celular.Ativo)
        {
            avisosCanto.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            avisosCanto.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            avisosCanto.Margin = new Avalonia.Thickness(8, 8, 8, 0);
        }
        else
        {
            avisosCanto.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            avisosCanto.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
            avisosCanto.Margin = new Avalonia.Thickness(0, 0, 10, 70);
            avisosCanto.MaxWidth = 380;
        }
    }

    public void Notificar(string texto)
    {
        bool cel = Celular.Ativo;
        var b = new Border
        {
            Padding = new Avalonia.Thickness(cel ? 14 : 12, cel ? 10 : 7), CornerRadius = new Avalonia.CornerRadius(cel ? 16 : 10),
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(0xF0, 0x1C, 0x1C, 0x1C)),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0xF2, 0xC2, 0x1B)), BorderThickness = new Avalonia.Thickness(1),
            BoxShadow = Avalonia.Media.BoxShadows.Parse("0 4 14 0 #80000000"),
            Child = new TextBlock { Text = texto, FontSize = cel ? 14 : 13, Foreground = Avalonia.Media.Brushes.White, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            RenderTransform = new Avalonia.Media.TranslateTransform(),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        var tr = (Avalonia.Media.TranslateTransform)b.RenderTransform!;
        Avalonia.Point? inicio = null;
        double arrasto = 0;
        void Tirar() => avisosCanto.Children.Remove(b);
        b.PointerPressed += (_, e) => { inicio = e.GetPosition(host); arrasto = 0; e.Pointer.Capture(b); e.Handled = true; };
        b.PointerMoved += (_, e) =>
        {
            if (inicio is not { } p0) return;
            arrasto = e.GetPosition(host).X - p0.X;
            tr.X = arrasto;
            b.Opacity = Math.Max(0.2, 1 - Math.Abs(arrasto) / 250);
        };
        b.PointerReleased += (_, e) =>
        {
            e.Pointer.Capture(null);
            if (inicio == null) return;
            inicio = null;
            // arrastou para o lado: sai; um toque (sem arrastar): sai tambem; pouco: volta
            if (Math.Abs(arrasto) > 70 || Math.Abs(arrasto) < 4) Tirar();
            else { tr.X = 0; b.Opacity = 1; }
        };
        if (cel) avisosCanto.Children.Insert(0, b); else avisosCanto.Children.Add(b);
        while (avisosCanto.Children.Count > 4) avisosCanto.Children.RemoveAt(cel ? avisosCanto.Children.Count - 1 : 0);
        DispatcherTimer.RunOnce(Tirar, TimeSpan.FromSeconds(6));
    }

    public void MostrarAviso(string? texto)
    {
        aviso.IsVisible = texto != null;
        ((TextBlock)aviso.Child!).Text = texto ?? "";
    }

    public void EsconderRelogio()
    {
        relogioTimer?.Stop();
        relogioTimer = null;
        relogio.IsVisible = false;
    }

    IReadOnlyList<RetroFoot.Motor.Online.JogadorSala> jogadoresOnline = Array.Empty<RetroFoot.Motor.Online.JogadorSala>();

    // "Passar o host...": a lista dos outros tecnicos conectados; o escolhido continua o jogo
    public void EscolherNovoHost()
    {
        if (Online is not { } host) return;
        if (host.ModoAmigo)
        {
            CUi.Folha fa = null!;
            fa = CUi.Pergunta(Idioma.T("Passar o host"), Idioma.T("No jogo com amigos o save é seu: o host não passa para outro técnico."), new[] { (Idioma.T("OK"), true, (Action)(() => FecharDialogo(fa))) });
            AbrirDialogo(fa);
            return;
        }
        var outros = jogadoresOnline.Where(x => x.Equipa != 0 && x.Estado != RetroFoot.Motor.Online.Estados.Saiu && Equipa(x.Equipa) is { } e && host.Remota(e)).ToList();
        CUi.Folha f = null!;
        var bs = new List<(string, bool, Action)>();
        foreach (var j in outros) { var jj = j; bs.Add((jj.Nome, true, () => { FecharDialogo(f); host.PassarHost(jj.Uid); })); }
        bs.Add((Idioma.T("Cancelar"), false, () => FecharDialogo(f)));
        f = CUi.Pergunta(Idioma.T("Passar o host"), outros.Count == 0
            ? Idioma.T("Não há outro técnico conectado para receber o host.")
            : Idioma.T("Quem continua o jogo no aparelho dele? Você continua na sala como os outros (ou pode sair)."), bs);
        AbrirDialogo(f);
    }

    // os jogadores da sala mudaram (quem esta pronto): a tela de espera do host
    public event Action<IReadOnlyList<RetroFoot.Motor.Online.JogadorSala>>? JogadoresOnline;

    // o time que recebe as propostas de patrocinio (Patrocinios.TimeHumano: o 1o humano nas
    // divisoes 1 a 5)
    Equipa? PrimeiroHumano()
    {
        for (int d = 0; d < 5 && d < S.Divisoes.Count; d++)
            foreach (var id in S.Divisoes[d].Equipas)
                if (J.M.Equipa(id) is { } e && RetroFoot.Motor.Mercado.MercadoJogo.HumanoAtivo(J.M.TreinadorDa(e))) return e;
        return null;
    }

    Control TelaAguardando() => new TelaAguardandoOnline(this);

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
        // no celular (em pe) o quadro de 800x600 deixava a janela com menos da metade do
        // tamanho: ela e ampliada pelo tamanho dela mesma, ate a largura da tela (enquanto as
        // janelas nao tem a versao propria do celular)
        if (Celular.Ativo && CUi.FolhaDe(dialogo) is { } folha)
        {
            // a janela feita para o celular: na largura da tela, de baixo (ou a tela toda)
            quadro.Children.Clear();
            dialogo.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            dialogo.VerticalAlignment = folha.Inteira ? Avalonia.Layout.VerticalAlignment.Stretch : Avalonia.Layout.VerticalAlignment.Bottom;
            camada.Children.Add(dialogo);
            camada.SizeChanged += (_, e) => { if (!folha.Inteira) dialogo.MaxHeight = e.NewSize.Height * 0.92; };
        }
        else if (Celular.Ativo)
        {
            quadro.Children.Clear();
            camada.Children.Add(new Viewbox { Stretch = Avalonia.Media.Stretch.Uniform, Margin = new Avalonia.Thickness(8), Child = dialogo });
        }
        else camada.Children.Add(new Viewbox { Stretch = Avalonia.Media.Stretch.Uniform, Child = quadro });
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
