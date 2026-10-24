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
    public Sala Sala => _sala;

    public JogoRemoto(Action<Control> mostrar, Action voltar, Sala sala, ClienteOnline cli)
    {
        _mostrar = mostrar; _voltar = voltar; _sala = sala; _cli = cli;
        cli.NovoRetrato += (r, rodada, versao, instancia) => Dispatcher.UIThread.Post(() => Novo(r, rodada, instancia));
        // a rodada ao vivo (fase 3): o inicio (o jogo naquele momento) e um tique por minuto; a
        // mesma tela da jornada, numa Sessao sem motor sobre a copia
        sala.B.Observar($"{sala.Raiz}/rodada/aoVivo/inicio", (caminho, v) =>
        {
            if (caminho != "/" || v is not JsonObject ini) return;
            Dispatcher.UIThread.Post(() => ComecarAoVivo(ini));
        });
        sala.B.Observar($"{sala.Raiz}/rodada/aoVivo/tique", (caminho, v) =>
        {
            if (caminho != "/" || v is not JsonObject t) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (_aoVivo is not { } av || _jornada == null) return;
                if (av.Aplicar(t)) _jornada.Minuto(av.Jornada);
            });
        });
        // a disputa de penaltis da Taca que o host mostrou: a mesma janela aqui
        sala.B.Observar($"{sala.Raiz}/rodada/aoVivo/penaltis", (caminho, v) =>
        {
            if (caminho != "/" || v is not JsonObject o || (int?)o["n"] is not int n) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (n <= _penaltisVistos || _aoVivo is not { } av || _sessaoAoVivo is not { } sv) return;
                _penaltisVistos = n;
                if (av.Penaltis(o) is not var (jg, d)) return;
                Control? t = null;
                var tp = new TelaPenaltis(jg, d);
                tp.Fechou += () => { if (t != null) sv.FecharDialogo(t); };
                t = tp;
                sv.AbrirDialogo(t);
            });
        });
        // a paralisacao (fase 3): quem parou (este aparelho: a janela das trocas; os outros: o
        // aviso), ou o fim dela
        sala.B.Observar($"{sala.Raiz}/rodada/aoVivo/pausa", (caminho, v) =>
        {
            if (caminho != "/") return;
            Dispatcher.UIThread.Post(() => Pausa(v as JsonObject));
        });
        // as transferencias (leiloes, compras): o aviso no canto, sem parar nada
        sala.B.Observar($"{sala.Raiz}/rodada/noticia", (caminho, v) =>
        {
            if (caminho != "/" || v is not JsonObject o || (int?)o["n"] is not int n) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (n <= _noticia) return;
                _noticia = n;
                var s = _jornada != null ? _sessaoAoVivo : _sessao;
                if (s == null) return;
                s.Notificar(Online.TextoTransferencia(s.S, (int?)o["jogador"] ?? 0, (int?)o["comprador"] ?? 0, (int?)o["vendedor"] ?? 0, (long?)o["preco"] ?? 0, (int?)o["tipo"] ?? 0));
            });
        });
        // (a sala do jogar com um amigo nao fica na lista; se o host a guardou como jogo online, a
        // bifurcacao, ela vira sala normal e entra na lista)
        // o sorteio dos times, ao mesmo tempo que o do host (antes do primeiro jogo chegar)
        sala.B.Observar($"{sala.Raiz}/rodada/sorteio", (caminho, v) =>
        {
            if (caminho != "/" || v is not JsonObject o) return;
            Dispatcher.UIThread.Post(() => SorteioDoHost(o));
        });
        // o meu lugar na sala: pedindo para entrar (a espera do host), espectador, expulso ou fora
        // (recusado, ou expulso ja tirado)
        sala.B.Observar($"{sala.Raiz}/jogadores/{sala.Uid}", (caminho, v) =>
        {
            if (caminho != "/" && caminho != "/estado" && caminho != "/equipa") return;
            Dispatcher.UIThread.Post(async () =>
            {
                JsonNode? eu = caminho == "/" ? v : await Ler($"{sala.Raiz}/jogadores/{sala.Uid}");
                string estado = (string?)eu?["estado"] ?? "";
                if (eu == null && _estado != "")
                    Fora(_estado == Estados.Pedindo ? T("O host não permitiu sua entrada no jogo.") : T("O host tirou você do jogo."));
                else if (estado == Estados.Expulso) Fora(T("O host tirou você do jogo. Seu técnico sai e o computador assume o time."));
                else if (estado == Estados.Pedindo && _sessao == null)
                    _mostrar(Mensagem(T("Jogo online"), T("O jogo já começou: o host precisa permitir sua entrada. Aguarde…")));
                if (eu != null) _estado = estado;
                // (o destaque so do meu time, na jornada e na classificacao: o time da sala)
                if ((int?)eu?["equipa"] is int eq && eq != 0) TelaJornada.SoTime = (ushort)eq;
            });
        });
        // a sala acabou (o host encerrou o jogo com o amigo, ou apagou a sala): o aviso e o inicio
        sala.B.Observar($"{sala.Raiz}/info", (caminho, v) =>
        {
            if (caminho != "/" || v != null) return;
            Dispatcher.UIThread.Post(Encerrada);
        });
        sala.B.Observar($"{sala.Raiz}/info/modo", (caminho, v) =>
        {
            if (caminho != "/") return;
            // (a sala apagada tambem tira o modo: so o modo trocado, com a sala ainda la, e a copia)
            if (v == null) return;
            string modo = (string?)v ?? "";
            if (modo == Sala.ModoAmigo) _modoAmigo = true;
            else if (_modoAmigo) { _modoAmigo = false; Online.Lembrar(sala); }
        });
        // o resultado do leilao de todos: o aviso no canto
        sala.B.Observar($"{sala.Raiz}/rodada/leilao", (caminho, v) =>
        {
            if (caminho != "/" || v is not JsonObject o || (int?)o["n"] is not int n) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (n <= _leilaoVisto) return;
                _leilaoVisto = n;
                var s = _jornada != null ? _sessaoAoVivo : _sessao;
                if (s == null) return;
                int jogador = (int?)o["jogador"] ?? 0, comprador = (int?)o["comprador"] ?? 0;
                long preco = (long?)o["preco"] ?? 0;
                // quem licitou: o resultado na janela do leilao (2 s); os outros, o aviso no canto
                if (_leilaoTodos is { } dt && dt.Jogador == jogador)
                {
                    _leilaoTodos = null;
                    var c = comprador == 0 ? null : s.Equipa((ushort)comprador);
                    var txt = TelaLeilao.DeTodos.Texto(s, c, preco);
                    _ = Task.Run(() => dt.Resultado(txt, c));
                    return;
                }
                s.Notificar(Online.TextoLeilao(s.S, jogador, (int?)o["vendedor"] ?? 0, comprador, preco));
            });
        });
        // (fase 2) as perguntas do motor para o meu time: a janela de sempre, aqui; a resposta
        // vai para o host (sem resposta no prazo, ele usa o padrao e a janela fecha)
        sala.B.Observar($"{sala.Raiz}/perguntas/{sala.Uid}", (caminho, v) =>
        {
            if (caminho != "/") return;
            Dispatcher.UIThread.Post(() => Pergunta(v as JsonObject));
        });
        // o host parou o jogo: a espera ate ele continuar
        cli.Pausado += () => Dispatcher.UIThread.Post(() =>
        {
            _rodada = ""; _instancia = ""; _pronto = false;
            _sessao?.EsconderRelogio();
            _sessao = null;
            _mostrar(Mensagem(T("Jogo online"), T("O host parou o jogo. Quando ele continuar, a rodada volta para você aqui.")));
        });
        // o sinal de vida do host (de 10 em 10 s): parado por 35 s, o aviso de que o host saiu ou
        // caiu (o jogo continua quando ele voltar); voltou, o aviso some
        sala.B.Observar($"{sala.Raiz}/hostVivo", (caminho, v) => _sinalHost = DateTime.UtcNow);
        // o host passado para este aparelho (ou assumido): este aparelho vira o host
        sala.B.Observar($"{sala.Raiz}/host", (caminho, v) =>
        {
            _hostUid = (string?)v ?? "";
            if (_hostUid == sala.Uid) Dispatcher.UIThread.Post(() => _ = TornarHost());
        });
        _vigia = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _vigia.Tick += (_, _) => VigiarHost();
        _vigia.Start();
        // o prazo da rodada (depois do primeiro PRONTO): o relogio; acabou sem PRONTO, vai com o
        // que estiver na tela (a escalacao que nao fecha vira a automatica no host)
        // a rodada comecou sem o PRONTO deste aparelho (o prazo acabou): a espera, com o aviso
        sala.B.Observar($"{sala.Raiz}/rodada/comecou", (caminho, v) =>
        {
            if ((string?)v is not { } r || r != _rodada) return;
            Dispatcher.UIThread.Post(() =>
            {
                // (a jornada ao vivo ja esta na tela: nada a fazer)
                if (_sessao is not { } ss || r != _rodada || _jornada != null) return;
                ss.EsconderRelogio();
                ss.FecharDialogo();
                // (a partida ao vivo nos outros aparelhos e a fase 3: por enquanto o resultado
                // chega no fim da rodada, com a classificacao)
                string aviso = _pronto
                    ? T("A rodada está sendo jogada no aparelho do host. Quando acabar, a classificação e a próxima rodada aparecem aqui.")
                    : T("O tempo acabou: seu time foi com a escalação que estava (ou a automática). A rodada está sendo jogada no aparelho do host.");
                _pronto = true;
                ss.Mostrar(new TelaEsperaRemota(_sala, _rodada, ss, aviso));
            });
        });
        sala.B.Observar($"{sala.Raiz}/rodada/prazo", (caminho, v) =>
        {
            if (caminho != "/" || v is not JsonObject o) return;
            _prazo = o;
            _ = MostrarPrazo();
        });
    }

    // o prazo da rodada (rodada/prazo): o relogio com o tempo que falta (pela hora do servidor);
    // tambem quando o time chega depois de o prazo comecar
    JsonObject? _prazo;
    long? _difRelogio;
    async Task MostrarPrazo()
    {
        if (_prazo is not { } o || (string?)o["rodada"] != _rodada || (int?)o["segundos"] is not int seg) return;
        double falta = seg;
        if ((long?)o["inicio"] is long ini)
        {
            try { _difRelogio ??= await _sala.DiferencaDoRelogio(); } catch { }
            long agoraServidor = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (_difRelogio ?? 0);
            falta = seg - (agoraServidor - ini) / 1000.0;
        }
        if (falta <= 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_sessao is not { } ss || _pronto) return;
            ss.MostrarRelogio(TimeSpan.FromSeconds(falta));
            ss.RelogioAcabou += () => { if (!_pronto && _time is { } e && _sessao == ss) _ = Pronto(ss, e); };
        });
    }

    Equipa? _time;
    AoVivo.Receptor? _aoVivo;
    TelaJornada? _jornada;

    string? _perguntaAberta;
    Sessao? _sessaoPergunta;   // a copia de uma pergunta antes da primeira rodada
    Sessao? _perguntaEm;
    string? _respondida;
    TelaLeilao.DeTodos? _leilaoTodos;   // a janela do leilao de todos, ate o resultado

    void Pergunta(JsonObject? q)
    {
        if (q == null)
        {
            // (o leilao de todos acabou: o resultado vem em seguida, na janela; se nao vier, ela
            // fecha sozinha)
            if (_leilaoTodos is { } dt)
            {
                _perguntaAberta = null;
                _ = Task.Run(async () => { await Task.Delay(10_000); if (_leilaoTodos == dt) { _leilaoTodos = null; dt.Fechar(); } });
                return;
            }
            // (o prazo acabou com a janela aberta: fecha)
            if (_perguntaAberta != null && _perguntaAberta != _respondida) _perguntaEm?.FecharDialogo();
            _perguntaAberta = null;
            return;
        }
        string id = (string?)q["id"] ?? "";
        if (id == _perguntaAberta) return;
        var s = _jornada != null ? _sessaoAoVivo : _sessao;
        Control? antes = null;
        bool copia = false;
        if (s == null)
        {
            // antes da primeira rodada (os patrocinios do comeco do jogo): a janela numa copia do
            // jogo que veio com a pergunta; respondida, volta a tela que estava (a sala, o sorteio)
            if ((string?)q["save"] is not { } sv) return;
            try
            {
                var js = Jogo98.Ler(AoVivo.Descomprimir(sv));
                antes = (App.Raiz as Border)?.Child;
                s = new Sessao(js, _mostrar, Configuracoes.Ler().OpcoesDoJogo(js, TelaInicio.PastaDoJogo())) { Remoto = this };
                s.Mostrar(Mensagem(T("Jogo online"), T("O jogo está começando…")));
                copia = true;
                _sessaoPergunta = s;
            }
            catch (Exception ex) { Console.Error.WriteLine("pergunta: " + ex.Message); return; }
        }
        _perguntaAberta = id;
        _perguntaEm = s;
        string tipo = (string?)q["tipo"] ?? "";
        // (o leilao para todos os tecnicos: com o prazo do host; respondido, a espera pelos outros)
        bool todos = (bool?)q["todos"] == true;
        _ = Task.Run(() =>
        {
            JsonObject? r = null;
            if (todos) Sessao.PrazoDialogo = TimeSpan.FromSeconds(Math.Max(1, ((int?)q["segundos"] ?? 45) - 1));
            try { r = Responder(s, tipo, q, dt => _leilaoTodos = dt); }
            catch (Exception ex) { Console.Error.WriteLine("pergunta: " + ex.Message); }
            finally { Sessao.PrazoDialogo = null; }
            if (todos) r ??= new JsonObject { ["milhares"] = 0, ["ok"] = false };
            if (r != null && _perguntaAberta == id)
                try { _sala.B.Gravar($"{_sala.Raiz}/respostas/{_sala.Uid}/{id}", r).GetAwaiter().GetResult(); _respondida = id; } catch { }
            Dispatcher.UIThread.Post(() =>
            {
                if (todos && _perguntaAberta == id)
                {
                    // (a espera pelos outros sobre a janela do leilao, ate o resultado)
                    _leilaoTodos?.Aguardar();
                    return;
                }
                if (_perguntaAberta == id) _perguntaAberta = null;
                if (copia && _sessaoPergunta == s) _sessaoPergunta = null;
                // (a rodada ainda nao chegou: a tela de antes)
                if (copia && _sessao == null && _jornada == null && _perguntaAberta == null)
                    _mostrar(antes ?? Mensagem(T("Jogo online"), T("O jogo está começando…")));
            });
        });
    }

    // a janela de cada pergunta (na thread de uma tarefa: as janelas esperam com EsperarDialogo)
    static JsonObject? Responder(Sessao s, string tipo, JsonObject q, Action<TelaLeilao.DeTodos>? abriu = null)
    {
        Equipa? Eq(string k) => s.Equipa((ushort)((int?)q[k] ?? 0));
        Jogador? Jg(Equipa? e, string k) => e?.Jogadores.FirstOrDefault(x => x.ID == (int?)q[k]) ?? s.S.Equipas.SelectMany(x => x.Jogadores).FirstOrDefault(x => x.ID == (int?)q[k]);
        switch (tipo)
        {
            case "aumento":
            {
                var e = Eq("equipa"); var p = Jg(e, "jogador");
                if (e == null || p == null) return null;
                return new JsonObject { ["ok"] = TelaPedidoSalario.Perguntar(s, e, p, (int?)q["novo"] ?? 0, (int?)q["modo"] ?? 0) };
            }
            case "lance":
            {
                var c = Eq("comprador"); var v = Eq("vendedor"); var p = Jg(v, "jogador");
                if (c == null || v == null || p == null) return null;
                if ((bool?)q["todos"] == true)
                {
                    // o leilao de todos: a janela de sempre, que fica aberta ate o resultado
                    var dt = TelaLeilao.DeTodos.Abrir(s, v, p, (int?)q["preco"] ?? 0);
                    abriu?.Invoke(dt);
                    var (m, k) = dt.Lance(c);
                    return new JsonObject { ["milhares"] = m, ["ok"] = k };
                }
                var (mil, ok) = TelaLeilao.Lance(s)(c, v, p);
                return new JsonObject { ["milhares"] = mil, ["ok"] = ok };
            }
            case "convite":
            {
                var t = s.S.Treinadores.FirstOrDefault(x => x.ID == (int?)q["treinador"]); var e = Eq("equipa");
                if (t == null || e == null) return null;
                return new JsonObject { ["ok"] = TelaTreinadores.Convite(s, t, e) };
            }
            case "patrocinio":
            {
                var p = new RetroFoot.Motor.Patrocinio.Proposta
                {
                    NomeBruto = Convert.FromBase64String((string?)q["nome"] ?? ""), Quantidade = (byte)((int?)q["quantidade"] ?? 0),
                    Valor = (int?)q["valor"] ?? 0, Tipo = (byte)((int?)q["tipoP"] ?? 0), Marca = (byte)((int?)q["marca"] ?? 0), Multa = (int?)q["multa"] ?? 0,
                    Registro = (string?)q["registro"] is { } reg ? new RetroFoot.Motor.Patrocinio.Marca(Convert.FromBase64String(reg)) : null,
                };
                bool ok = s.EsperarDialogo<bool>(fim => new TelaPatrocinio(p, s.J.Op.Marcas, Moeda.DoSave(s.S), fim));
                return new JsonObject { ["ok"] = ok };
            }
            case "batedor":
            {
                var e = Eq("equipa");
                if (e == null || q["titulares"] is not JsonArray ts) return null;
                var tit = ts.Select(x => e.Jogadores.FirstOrDefault(p => p.ID == (int?)x)).OfType<Jogador>().ToList();
                var sug = e.Jogadores.FirstOrDefault(p => p.ID == (int?)q["sugerido"]);
                var b = LigacaoPartida.BatedorOnline(s, e, tit, sug);
                return new JsonObject { ["jogador"] = (int)(b?.ID ?? 0) };
            }
            case "lesao":
            {
                var e = Eq("equipa"); var saiu = Jg(e, "saiu");
                if (e == null || saiu == null) return null;
                var reservas = e.Jogadores.Where(p => Memoria.Escalacao(p) == Memoria.EscReserva).Select(p => p.ID).ToHashSet();
                s.EsperarDialogo<bool>(fim => { var t = new TelaSubstituicaoForcada(e, saiu); t.Fechou += () => fim(true); return t; });
                var entrou = e.Jogadores.FirstOrDefault(p => reservas.Contains(p.ID) && Memoria.Escalacao(p) == Memoria.EscTitular);
                return entrou == null ? null : new JsonObject { ["entra"] = (int)entrou.ID };
            }
        }
        return null;
    }

    // o toque no proprio jogo da jornada (LigacaoPartida.PedirDetalhes): parar o jogo de todos
    public void PedirPausa(JogoAtual jg, int lado)
    {
        if (_time is not { } meu || jg.Equipa[lado]?.ID != meu.ID || _pausaMinha) return;
        Executar(new Parar());
    }

    bool _pausaMinha;
    bool _viuSorteio;
    bool _anteriorTaca;   // a rodada que passou foi da Taca (sem a classificacao depois dela)
    volatile bool _modoAmigo;   // a sala do jogar com um amigo (o save do host, a meio da temporada)
    int _noticia, _leilaoVisto, _penaltisVistos;

    void Pausa(JsonObject? p)
    {
        var s = _jornada != null ? _sessaoAoVivo : _sessao;
        if (p == null) { s?.MostrarAviso(null); s?.EsconderRelogio(); return; }
        string uid = (string?)p["uid"] ?? "";
        // o intervalo de todos (30 s): a janela das trocas para quem tem time no jogo; o relogio
        if ((bool?)p["coletivo"] == true)
        {
            // (o jogo de outro time aberto para ver: fecha, vem a janela das trocas)
            _jornada?.PararDeEspiar();
            int seg = (int?)p["segundos"] ?? 30;
            s?.MostrarRelogio(TimeSpan.FromSeconds(seg));
            var meus = (p["equipas"] as JsonArray)?.Select(x => (int?)x ?? 0).ToHashSet() ?? new();
            if (_time is { } t && meus.Contains(t.ID)) AbrirTrocas(t.ID, 4, TimeSpan.FromSeconds(seg));
            else s?.MostrarAviso(T("⏸ Intervalo: os técnicos fazem as substituições"));
            return;
        }
        // a pausa simples do host (o botao de pausa): so o aviso
        if ((int?)p["motivo"] == 0) { s?.MostrarAviso(T("⏸ {0} pausou a partida", (string?)p["nome"] ?? "")); return; }
        if (uid != _sala.Uid)
        {
            s?.MostrarAviso(T("⏸ {0} está fazendo substituições…", (string?)p["nome"] ?? ""));
            return;
        }
        // as trocas deste aparelho: a janela sobre a copia ao vivo; as trocas feitas nela vao
        // para o host (ele confere e transmite); fechar e Retomar
        AbrirTrocas((int?)p["equipa"] ?? 0, (int?)p["motivo"] ?? 1, null);
    }

    void AbrirTrocas(int eq, int motivo, TimeSpan? prazo)
    {
        if (_aoVivo is not { } av || _sessaoAoVivo is not { } sv || _pausaMinha) return;
        var jg = av.Jornada.Jogos.FirstOrDefault(x => x.Dados.Casa == eq || x.Dados.Fora == eq);
        if (jg == null) return;
        int lado = jg.Dados.Casa == eq ? Lado.Casa : Lado.Visit;
        var e = jg.Equipa[lado]!;
        _pausaMinha = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var antes = e.Jogadores.Where(x => Memoria.Escalacao(x) == Memoria.EscTitular).Select(x => x.ID).ToList();
                Sessao.PrazoDialogo = prazo;
                try { LigacaoPartida.JanelaOnline(sv, jg, lado, motivo, av.Jornada); }
                finally { Sessao.PrazoDialogo = null; }
                var depois = e.Jogadores.Where(x => Memoria.Escalacao(x) == Memoria.EscTitular).Select(x => x.ID).ToList();
                var sairam = antes.Where(id => !depois.Contains(id)).ToList();
                foreach (var entra in depois.Where(id => !antes.Contains(id)))
                {
                    var pe = e.Jogadores.First(x => x.ID == entra);
                    int k = sairam.FindIndex(id => (e.Jogadores.First(x => x.ID == id).Posicao == 0) == (pe.Posicao == 0));
                    if (sairam.Count == 0) break;
                    if (k < 0) k = 0;
                    await _cli.Executar(new Trocar(sairam[k], entra));
                    sairam.RemoveAt(k);
                }
                await _cli.Executar(new Retomar());
            }
            catch (Exception ex) { Console.Error.WriteLine("pausa: " + ex.Message); }
            finally { _pausaMinha = false; }
        });
    }

    Sessao? _sessaoAoVivo;

    void ComecarAoVivo(JsonObject ini)
    {
        // (quem ainda pede para entrar nao assiste)
        if (_estado == Estados.Pedindo) return;
        try
        {
            var av = new AoVivo.Receptor(ini);
            var s = new Sessao(av.S, _mostrar, Configuracoes.Ler().OpcoesDoJogo(av.S, TelaInicio.PastaDoJogo())) { Remoto = this };
            _aoVivo = av;
            _sessao?.EsconderRelogio();
            var titulo = System.Text.Encoding.Latin1.GetBytes(Mundo.TituloNaTela(System.Text.Encoding.Latin1.GetString(av.Titulo)));
            TelaJornada.SoTime = _time?.ID;
            _jornada = new TelaJornada(s, titulo, av.Jornada);
            _sessaoAoVivo = s;
            _pronto = true;
            s.Mostrar(_jornada);
        }
        catch (Exception ex) { Console.Error.WriteLine("ao vivo: " + ex.Message); }
    }
    DateTime _sinalHost = DateTime.UtcNow;
    readonly DispatcherTimer _vigia;
    Action? _avisoHost;   // fecha o aviso de host desconectado

    string _hostUid = "";
    bool _virouHost;

    // este aparelho continua o jogo como host: o ultimo retrato (o da preparacao em que o host
    // passou ou sumiu) num motor aqui
    async Task TornarHost()
    {
        if (_virouHost) return;
        _virouHost = true;
        _vigia.Stop();
        try
        {
            var no = await _sala.B.Ler($"{_sala.Raiz}/retrato");
            var r = Retrato.Ler(Convert.FromBase64String((string?)no?["dados"] ?? ""));
            var js = await _sala.Jogadores();
            _cli.Dispose();
            Dispatcher.UIThread.Post(() =>
            {
                _avisoHost?.Invoke(); _avisoHost = null;
                _mostrar(new TelaInicio(_mostrar, new TelaInicio.OnlineNovo(_sala, js, null, r)));
            });
        }
        catch (Exception ex)
        {
            _virouHost = false;
            Dispatcher.UIThread.Post(() => _mostrar(Mensagem(T("Jogo online"), T("Não consegui assumir o host: {0}", ex.Message))));
        }
    }

    // o host sumiu: depois de 60 s (mais 20 s por quem vem antes na ordem das vagas, para nao
    // tentarem todos juntos) este aparelho tenta assumir; as regras e a escrita condicional
    // deixam so um
    DateTime _tentouAssumir;
    async Task TalvezAssumir(TimeSpan parado)
    {
        if (_virouHost || _rodada.Length == 0 || DateTime.UtcNow - _tentouAssumir < TimeSpan.FromSeconds(15)) return;
        // (no modo amigo o jogo e o save do host: ninguem assume)
        if (await _sala.Modo() == Sala.ModoAmigo) return;
        try
        {
            var js = await _sala.Jogadores();
            var fila = js.Where(x => x.Uid != _hostUid && x.Estado != Estados.Saiu && x.Equipa != 0).OrderBy(x => x.Vaga).ToList();
            int pos = fila.FindIndex(x => x.Uid == _sala.Uid);
            if (pos < 0 || parado < TimeSpan.FromSeconds(65 + 20 * pos)) return;
            _tentouAssumir = DateTime.UtcNow;
            if (await _sala.Assumir()) Dispatcher.UIThread.Post(() => _ = TornarHost());
        }
        catch { }
    }

    void VigiarHost()
    {
        var parado = DateTime.UtcNow - _sinalHost;
        // (no jogar com um amigo, o host que some por 90 s fechou o jogo: o amigo e desconectado)
        if (_modoAmigo && _rodada.Length > 0 && parado > TimeSpan.FromSeconds(90))
        {
            if (_avisoHost != null) { _avisoHost(); _avisoHost = null; }
            Encerrada();
            return;
        }
        if (parado > TimeSpan.FromSeconds(60)) _ = TalvezAssumir(parado);
        bool sumiu = parado > TimeSpan.FromSeconds(35);
        if (sumiu && _avisoHost == null && _sessao is { } ss)
        {
            // (no jogar com um amigo o jogo e o save do host: ninguem assume; sem ele, nao ha jogo)
            ss.EsconderRelogio();
            _avisoHost = Online.Janela(ss, T("Host desconectado"), null, Online.Texto(_modoAmigo
                ? T("O host perdeu a conexão. O jogo é o save dele: quando ele voltar, o jogo continua daqui. Você pode esperar ou sair.")
                : T("O host saiu do jogo ou perdeu a conexão. Se ele não voltar em cerca de 1 minuto, outro técnico da sala assume o host e o jogo continua. Você pode esperar aqui ou sair (sua vaga e seu time ficam guardados).")),
                new (string, bool, Action)[] { (T("Sair do jogo"), false, Sair) }, () => { });
        }
        else if (!sumiu && _avisoHost != null)
        {
            _avisoHost();
            _avisoHost = null;
        }
    }

    string _instancia = "";

    void Novo(Retrato r, string rodada, string instancia)
    {
        _aoVivo = null; _jornada = null;
        // (o host voltou ou mudou: o Preparo mandado se perdeu, prepara de novo)
        bool nova = rodada != _rodada || instancia != _instancia;
        _instancia = instancia;
        // (o retrato novo da mesma rodada vem de um comando: so refaz a tela de quem mandou; os
        // outros seguem mexendo no time sem a tela recarregar no meio)
        // (sem time ainda, o espectador: o jogo novo da mesma rodada traz o time dele)
        if (!nova && !_esperandoRetrato && _sessao != null && _time != null) return;
        _esperandoRetrato = false;
        if (!nova && _pronto) return;   // (ja mandou o Preparo desta rodada)
        if (_rodada.Length == 0) Comecou?.Invoke();
        if (nova) _anteriorTaca = _rodada.EndsWith("-T");
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
        var js = await _sala.Jogadores();
        var minha = js.FirstOrDefault(x => x.Uid == _sala.Uid);
        if (minha == null || minha.Equipa == 0 || sessao.Equipa(minha.Equipa) is not { } e)
        {
            Dispatcher.UIThread.Post(() => _mostrar(Mensagem(T("Jogo online"), minha?.Estado == Estados.Espectador
                ? T("Você entrou como espectador: assiste às rodadas e recebe um time na próxima preparação dos times.")
                : minha?.Estado == Estados.Pedindo ? T("O jogo já começou: o host precisa permitir sua entrada. Aguarde…")
                : T("Aguardando o host dar um time para você…"))));
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            // (o destaque so do meu time, na classificacao e na jornada)
            TelaJornada.SoTime = e.ID;
            // (o sorteio do host ainda na tela: ele passa para dentro da tela do jogo, para as
            // perguntas que chegarem abrirem por cima dele; o time depois do Continuar)
            if (_noSorteio)
            {
                _depoisDoSorteio = () => _ = MostrarTime(sessao, false);
                if (_telaSorteio is { } ts) { (ts.Parent as Border)?.SetValue(Border.ChildProperty, null); sessao.Mostrar(ts); }
                return;
            }
            void Time()
            {
                var jogo = sessao.S.Jogos.FirstOrDefault(x => x.Casa == e.ID || x.Fora == e.ID);
                _time = e;
                // (com o time na tela, o sorteio do comeco ja passou: nao volta nas rodadas seguintes)
                _viuSorteio = true;
                // o time nao joga nesta rodada (fora da Taca): so a espera
                if (jogo == null) { sessao.Mostrar(new TelaEsperaRemota(_sala, _rodada, sessao)); return; }
                var t = new TelaEquipa(sessao, e, jogo);
                t.Terminou += sair =>
                {
                    if (sair) { Sair(() => _ = MostrarTime(sessao, false)); return; }
                    _ = Pronto(sessao, e);
                };
                sessao.Mostrar(t);
                // (o prazo ja corria antes de o time chegar: o relogio com o que falta)
                _ = MostrarPrazo();
            }
            // o jogo novo (a 1a rodada, o primeiro jogo que chega): o sorteio dos times
            // (no jogar com um amigo, o save do host esta a meio da temporada: o sorteio no 1o jogo)
            if (nova && !_viuSorteio && (sessao.J.M.Jornada == 1 || _modoAmigo))
            {
                _viuSorteio = true;
                var tecnicos = js.Where(x => x.Equipa != 0).OrderBy(x => x.Vaga).Select(x => (x.Nome, x.Equipa)).ToList();
                sessao.Mostrar(new TelaSorteioTimes(sessao.S, tecnicos, Time));
            }
            // a cada rodada nova do campeonato, a classificacao primeiro (o resultado da rodada que
            // passou); na Taca e logo depois dela, direto ao time
            else if (nova && sessao.J.M.Jornada > 1 && !_rodada.EndsWith("-T") && !_anteriorTaca)
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
    bool _esperandoRetrato;

    public void Executar(Comando c, Action<Resposta>? depois = null)
    {
        if (c is not (Parar or Trocar or Retomar)) _esperandoRetrato = true;
        _ = Task.Run(async () =>
        {
            var r = await _cli.Executar(c);
            Dispatcher.UIThread.Post(() =>
            {
                depois?.Invoke(r);
                if (!r.Ok && _sessao is { } ss)
                {
                    Action fechar = () => { };
                    fechar = Online.Janela(ss, T("Jogo online"), null, Online.Texto(T("O host recusou: {0}", r.Motivo)), new (string, bool, Action)[] { (T("OK"), true, () => fechar()) });
                }
            });
        });
    }

    // sai do jogo: a vaga e o time ficam (volta em Jogo online > Entrar, com a mesma chave)
    // sair do jogo: por agora (o tecnico fica de ferias, o computador cuida do time, e ele volta
    // com a mesma chave, pela lista Suas salas) ou de vez (o tecnico sai do jogo e o computador
    // assume o time). aoCancelar: a tela de antes (a da equipa fecha ao pedir para sair)
    void Sair() => Sair(null);

    void Sair(Action? aoCancelar)
    {
        var s = _jornada != null ? _sessaoAoVivo : _sessao;
        if (s == null) { SairPorAgora(); return; }
        Action fechar = () => { };
        fechar = Online.Janela(s, T("Sair do jogo"), null,
            Online.Texto(T("Você volta depois? Saindo por agora, seu técnico fica de férias (o computador cuida do time) e você continua de onde parou, com a mesma chave. Desistindo, seu técnico sai do jogo e o computador assume o time de vez.")),
            new (string, bool, Action)[]
            {
                (T("Cancelar"), false, () => { fechar(); aoCancelar?.Invoke(); }),
                (T("Desistir de vez"), false, () => { fechar(); ConfirmarDesistir(s, aoCancelar); }),
                (T("Sair por agora"), true, () => { fechar(); SairPorAgora(); }),
            }, () => { fechar(); aoCancelar?.Invoke(); }, emColuna: true);
    }

    void ConfirmarDesistir(Sessao s, Action? aoCancelar)
    {
        Action fechar = () => { };
        fechar = Online.Janela(s, T("Desistir do jogo"), null,
            Online.Texto(T("Seu técnico sai do jogo e o computador assume o time. Não dá para voltar com este técnico. Desistir mesmo?")),
            new (string, bool, Action)[]
            {
                (T("Não"), false, () => { fechar(); aoCancelar?.Invoke(); }),
                (T("Desistir"), true, () => { fechar(); Desistir(); }),
            }, () => { fechar(); aoCancelar?.Invoke(); });
    }

    // o sorteio que o host mandou: a tela dele; o jogo que chegar enquanto ela esta aberta espera
    // o Continuar
    Action? _depoisDoSorteio;
    bool _noSorteio;
    Control? _telaSorteio;
    int _sorteioVisto;
    void SorteioDoHost(JsonObject o)
    {
        // (o sorteio de quem entrou com o jogo comecado: por cima da tela de agora)
        if ((int?)o["n"] is int n && n > 0)
        {
            if (n <= _sorteioVisto || o["times"] is not JsonArray ts2) return;
            _sorteioVisto = n;
            _viuSorteio = true;   // (o do comeco do jogo nao vem mais: este aparelho ja tem o seu)
            var s = _jornada != null ? _sessaoAoVivo : _sessao;
            if (s == null) return;
            var l = ts2.OfType<JsonObject>().Select(t => ((string?)t["nome"] ?? "", (ushort)((int?)t["equipa"] ?? 0))).ToList();
            try
            {
                // (o time novo ainda nao esta no jogo deste aparelho: o jogo que veio com o sorteio).
                // Depois, se este aparelho ainda esta sem time, o time (que chegou no jogo novo)
                var js = (string?)o["save"] is { } sv2 ? Jogo98.Ler(AoVivo.Descomprimir(sv2)) : s.S;
                TelaSorteioTimes.EmJanela(s, js, l, () => { if (_time == null && _sessao is { } ss && _jornada == null) _ = MostrarTime(ss, false); });
            }
            catch (Exception ex) { Console.Error.WriteLine("sorteio: " + ex.Message); }
            return;
        }
        if (_viuSorteio || _sessao != null || (string?)o["save"] is not { } sv || o["times"] is not JsonArray ts) return;
        try
        {
            var js = Jogo98.Ler(AoVivo.Descomprimir(sv));
            var tecnicos = ts.OfType<JsonObject>().Select(t => ((string?)t["nome"] ?? "", (ushort)((int?)t["equipa"] ?? 0))).ToList();
            _viuSorteio = true;
            _noSorteio = true;
            var antes = (App.Raiz as Border)?.Child;
            _telaSorteio = new TelaSorteioTimes(js, tecnicos, () =>
            {
                _noSorteio = false;
                _telaSorteio = null;
                if (_depoisDoSorteio is { } d) { _depoisDoSorteio = null; d(); }
                else if (antes != null && _sessao == null) _mostrar(antes);
            });
            _mostrar(_telaSorteio);
        }
        catch (Exception ex) { Console.Error.WriteLine("sorteio: " + ex.Message); }
    }

    string _estado = "";
    async Task<JsonNode?> Ler(string caminho) { try { return await _sala.B.Ler(caminho); } catch { return null; } }

    // fora da sala (recusado ou expulso): o aviso e o inicio
    void Fora(string texto) => Encerrada(texto);

    bool _encerrada;
    void Encerrada() => Encerrada(T("O host encerrou o jogo."));

    void Encerrada(string texto)
    {
        if (_encerrada) return;
        _encerrada = true;
        _vigia.Stop();
        _cli.Dispose();
        Online.Esquecer(_sala.Codigo);
        _sessao?.EsconderRelogio();
        _sessao = null; _sessaoAoVivo = null; _jornada = null;
        _mostrar(Online.Pagina(T("Jogo encerrado"), Online.Titulo(_sala), _voltar,
            CUi.Cartao_(null, new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    CUi.Txt(texto, 15, CUi.Texto, FontWeight.Normal, true),
                    new CUi.Botao(T("Voltar ao início"), true, _voltar) { HorizontalAlignment = HorizontalAlignment.Stretch },
                },
            })));
    }

    async void SairPorAgora()
    {
        _vigia.Stop();
        _cli.Dispose();
        try { await _sala.Ausentar(); } catch { }
        _voltar();
    }

    async void Desistir()
    {
        _vigia.Stop();
        _cli.Dispose();
        try { await _sala.Desistir(); } catch { }
        Online.Esquecer(_sala.Codigo);
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
            Online.Titulo(sala) + (aviso != null ? "" : " · " + T("A rodada começa quando todos estiverem prontos")), null, corpo);
    }
}
