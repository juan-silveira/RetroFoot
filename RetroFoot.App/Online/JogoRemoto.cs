using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Online;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// O jogo num aparelho que nao e o host (docs/multiplayer-online.md): a cada retrato de uma
// rodada nova, a classificacao e a tela da equipa numa copia do jogo (uma Sessao sem motor);
// as acoes que mexem no dinheiro ou noutros times viram comandos (Sessao.Remoto) e o host
// manda um retrato novo; o Jogar manda o Preparo (escalacao, ingresso, observados) e marca
// PRONTO; a espera mostra quem falta.
public sealed class JogoRemoto
{
    readonly Action<Control> _mostrar;
    readonly Action _voltar;
    readonly Sala _sala;
    readonly ClienteOnline _cli;
    string _rodada = "";
    bool _pronto;
    Sessao? _sessao;

    public event Action? Comecou;

    public JogoRemoto(Action<Control> mostrar, Action voltar, Sala sala, ClienteOnline cli)
    {
        _mostrar = mostrar; _voltar = voltar; _sala = sala; _cli = cli;
        cli.NovoRetrato += (r, rodada, versao, instancia) => Dispatcher.UIThread.Post(() => Novo(r, rodada, instancia));
        // o prazo da rodada (depois do primeiro PRONTO): o relogio; acabou sem PRONTO, vai com o
        // que estiver na tela (a escalacao que nao fecha vira a automatica no host)
        // a rodada comecou sem o PRONTO deste aparelho (o prazo acabou): a espera, com o aviso
        sala.B.Observar($"{sala.Raiz}/rodada/comecou", (caminho, v) =>
        {
            if ((string?)v is not { } r || r != _rodada) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (_pronto || _sessao is not { } ss || r != _rodada) return;
                _pronto = true;
                ss.EsconderRelogio();
                ss.FecharDialogo();
                ss.Mostrar(new TelaEsperaRemota(_sala, _rodada, ss, T("O tempo acabou: seu time foi com a escalação que estava (ou a automática). A rodada está sendo jogada.")));
            });
        });
        sala.B.Observar($"{sala.Raiz}/rodada/prazo", (caminho, v) =>
        {
            if (v is not JsonObject o || (string?)o["rodada"] != _rodada || (int?)o["segundos"] is not int seg) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (_sessao is not { } ss) return;
                ss.MostrarRelogio(TimeSpan.FromSeconds(seg));
                ss.RelogioAcabou += () => { if (!_pronto && _time is { } e && _sessao == ss) _ = Pronto(ss, e); };
            });
        });
    }

    Equipa? _time;

    string _instancia = "";

    void Novo(Retrato r, string rodada, string instancia)
    {
        // (o host voltou ou mudou: o Preparo mandado se perdeu, prepara de novo)
        bool nova = rodada != _rodada || instancia != _instancia;
        _instancia = instancia;
        if (!nova && _pronto) return;   // (ja mandou o Preparo desta rodada)
        if (_rodada.Length == 0) Comecou?.Invoke();
        _rodada = rodada;
        _pronto = false;
        var s = Jogo98.Ler(r.Save);
        var cfg = Configuracoes.Ler();
        var op = cfg.OpcoesDoJogo(s, TelaInicio.PastaDoJogo());
        Sessao sessao;
        try { sessao = new Sessao(s, _mostrar, op, null, r) { Remoto = this }; }
        catch (Exception ex) { _mostrar(Mensagem(T("Erro"), ex.Message)); return; }
        _sessao = sessao;
        _ = MostrarTime(sessao, nova);
    }

    async Task MostrarTime(Sessao sessao, bool nova)
    {
        var minha = (await _sala.Jogadores()).FirstOrDefault(x => x.Uid == _sala.Uid);
        if (minha == null || minha.Equipa == 0 || sessao.Equipa(minha.Equipa) is not { } e)
        {
            Dispatcher.UIThread.Post(() => _mostrar(Mensagem(T("Jogo online"), T("Aguardando o host dar um time para você…"))));
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            void Time()
            {
                var jogo = sessao.S.Jogos.FirstOrDefault(x => x.Casa == e.ID || x.Fora == e.ID);
                _time = e;
                // o time nao joga nesta rodada (fora da Taca): so a espera
                if (jogo == null) { sessao.Mostrar(new TelaEsperaRemota(_sala, _rodada, sessao)); return; }
                var t = new TelaEquipa(sessao, e, jogo);
                t.Terminou += sair =>
                {
                    if (sair) { Sair(); return; }
                    _ = Pronto(sessao, e);
                };
                sessao.Mostrar(t);
            }
            // a cada rodada nova, a classificacao primeiro (o resultado da rodada que passou)
            if (nova && sessao.J.M.Jornada > 1)
            {
                var c = new TelaClassificacao(sessao, sessao.J.M.Jornada - 1, false);
                c.Fechou += Time;
                sessao.Mostrar(c);
            }
            else Time();
        });
    }

    async Task Pronto(Sessao sessao, Equipa e)
    {
        var t = sessao.J.M.TreinadorDa(e);
        var preparo = new Preparo(
            e.Jogadores.Select(p => new Escalado(p.ID, Memoria.Escalacao(p))).ToList(),
            Ingressos.Razao(e),
            t != null ? MercadoJogo.Observados(sessao.S, t.ID).ToList() : new(),
            t?.B1A[3] ?? 0);
        _pronto = true;
        Dispatcher.UIThread.Post(() => sessao.Mostrar(new TelaEsperaRemota(_sala, _rodada, sessao)));
        var r = await _cli.Pronto(preparo);
        if (!r.Ok && r.Motivo == HostOnline.Fora) return;   // (a espera ja mostra que a rodada comecou)
        if (!r.Ok)
        {
            _pronto = false;
            Dispatcher.UIThread.Post(() => _mostrar(Mensagem(T("Jogo online"), T("O host não recebeu a escalação: {0}", r.Motivo))));
        }
    }

    // uma acao da tela que vira comando (Sessao.Remoto): a resposta numa mensagem quando o host
    // recusa; aceita, o retrato novo refaz a tela
    public void Executar(Comando c, Action<Resposta>? depois = null)
    {
        _ = Task.Run(async () =>
        {
            var r = await _cli.Executar(c);
            Dispatcher.UIThread.Post(() =>
            {
                depois?.Invoke(r);
                if (!r.Ok && _sessao is { } ss)
                {
                    CUi.Folha f = null!;
                    f = CUi.Pergunta(T("Jogo online"), T("O host recusou: {0}", r.Motivo), new[] { (T("OK"), true, (Action)(() => ss.FecharDialogo(f))) });
                    ss.AbrirDialogo(f);
                }
            });
        });
    }

    // sai do jogo: a vaga e o time ficam (volta em Jogo online > Entrar, com a mesma chave)
    async void Sair()
    {
        _cli.Dispose();
        try { await _sala.Ausentar(); } catch { }
        _voltar();
    }

    Control Mensagem(string titulo, string texto) =>
        Online.Pagina(titulo, null, Sair, CUi.Cartao_(null, CUi.Txt(texto, 15, CUi.Texto, FontWeight.Normal, true)));
}

// a espera do cliente depois do Jogar: quem ja esta pronto (ao vivo)
sealed class TelaEsperaRemota : UserControl
{
    public TelaEsperaRemota(Sala sala, string rodada, Sessao sessao, string? aviso = null)
    {
        FontFamily = CUi.Fonte;
        var lista = new StackPanel { Spacing = 6 };
        var obs = sala.B.Observar($"{sala.Raiz}/jogadores", (_, _) => _ = Task.Run(async () =>
        {
            try
            {
                var l = await sala.Jogadores();
                Dispatcher.UIThread.Post(() =>
                {
                    lista.Children.Clear();
                    var jogam = sessao.S.Jogos.SelectMany(x => new[] { x.Casa, x.Fora }).ToHashSet();
                    foreach (var j in l.Where(x => x.Equipa != 0 && jogam.Contains(x.Equipa))) lista.Children.Add(TelaEsperaOnline.Linha(j, rodada, sessao.Equipa(j.Equipa)));
                });
            }
            catch { }
        }));
        DetachedFromVisualTree += (_, _) => obs.Dispose();
        Control corpo = CUi.Cartao_(null, lista);
        if (aviso != null)
            corpo = new StackPanel { Spacing = 12, Children = { CUi.Cartao_(null, CUi.Txt(aviso, 14, CUi.Texto, FontWeight.Normal, true)), corpo } };
        Content = Online.Pagina(aviso != null ? T("Rodada em andamento") : T("Aguardando os técnicos"),
            aviso != null ? null : T("A rodada começa quando todos estiverem prontos"), null, corpo);
    }
}
