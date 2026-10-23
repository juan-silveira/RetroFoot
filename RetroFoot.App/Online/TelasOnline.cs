using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RetroFoot.Motor.Online;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// O jogo online (docs/multiplayer-online.md): a entrada (criar ou entrar numa sala com a
// chave), a sala (as 6 vagas, ao vivo) e as esperas. Nas duas peles do kit do celular (CUi),
// tambem no computador (numa coluna no meio da tela).
public static class Online
{
    // (o SocketsHttpHandler do .NET tambem no Android: o do Android segurava o text/event-stream
    // e os avisos ao vivo so chegavam quando a ligacao caia, ~30 s depois. Sem Timeout aqui: a
    // ligacao ao vivo fica aberta; cada pedido do BancoFirebase tem o prazo dele)
    static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(10) }) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    static BancoFirebase? _banco;

    // a identidade anonima do aparelho (guardada junto das Configuracoes) e o banco
    public static BancoFirebase Banco => _banco ??= new BancoFirebase(Http,
        new IdentidadeFirebase(Http, Path.Combine(Path.GetDirectoryName(Configuracoes.Arquivo)!, "online-id.json")));

    // os jogos online ficam fora da pasta dos saves (as ferramentas nao os veem)
    public static string Pasta(string codigo) => Path.Combine(Path.GetDirectoryName(DadosJogo.Saves)!, "online", codigo);

    // os jogos online em que este aparelho e o host, do mais recente
    public static List<string> JogosDoHost()
    {
        try
        {
            var raiz = Path.Combine(Path.GetDirectoryName(DadosJogo.Saves)!, "online");
            if (!Directory.Exists(raiz)) return new();
            return Directory.GetDirectories(raiz).Select(d => Path.Combine(d, "JOGO.e98")).Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTime).ToList();
        }
        catch { return new(); }
    }

    // os saves single player deste aparelho (a pasta dos saves)
    public static List<string> SavesLocais()
    {
        try
        {
            if (!Directory.Exists(DadosJogo.Saves)) return new();
            return Directory.GetFiles(DadosJogo.Saves, "*.e98", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive })
                .OrderByDescending(File.GetLastWriteTime).Take(8).ToList();
        }
        catch { return new(); }
    }

    // as salas em que este aparelho ja entrou (como host ou convidado), para voltar sem digitar a
    // chave: codigo -> nome da sala e a ultima vez (online-salas.json, junto das Configuracoes)
    public sealed record SalaLembrada(string Codigo, string Nome, DateTime Quando);
    static string ArquivoSalas => Path.Combine(Path.GetDirectoryName(Configuracoes.Arquivo)!, "online-salas.json");

    public static List<SalaLembrada> Salas()
    {
        try
        {
            if (!File.Exists(ArquivoSalas)) return new();
            var n = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(ArquivoSalas)) as System.Text.Json.Nodes.JsonArray;
            return (n ?? new()).OfType<System.Text.Json.Nodes.JsonObject>()
                .Select(o => new SalaLembrada((string?)o["codigo"] ?? "", (string?)o["nome"] ?? "", DateTime.TryParse((string?)o["quando"], out var q) ? q : DateTime.MinValue))
                .Where(x => Sala.CodigoValido(x.Codigo)).OrderByDescending(x => x.Quando).ToList();
        }
        catch { return new(); }
    }

    static void GravarSalas(IEnumerable<SalaLembrada> l)
    {
        try
        {
            var a = new System.Text.Json.Nodes.JsonArray(l.Take(20).Select(x => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
            {
                ["codigo"] = x.Codigo, ["nome"] = x.Nome, ["quando"] = x.Quando.ToString("o"),
            }).ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(ArquivoSalas)!);
            File.WriteAllText(ArquivoSalas, a.ToJsonString());
        }
        catch { }
    }

    public static void Lembrar(Sala sala)
    {
        var l = Salas().Where(x => x.Codigo != sala.Codigo).ToList();
        var antes = Salas().FirstOrDefault(x => x.Codigo == sala.Codigo);
        l.Insert(0, new SalaLembrada(sala.Codigo, sala.Nome.Length > 0 ? sala.Nome : antes?.Nome ?? "", DateTime.Now));
        GravarSalas(l);
    }

    public static void Esquecer(string codigo) => GravarSalas(Salas().Where(x => x.Codigo != codigo));

    // o nome da sala do jogo de um host (para a sala nova, se a antiga acabou)
    public static string NomeLembrado(string codigo) => Salas().FirstOrDefault(x => x.Codigo == codigo)?.Nome ?? "";

    // "Tecnicos na sala..." (o menu Tecnico, no jogo online): quem esta no jogo, com o time e se
    // esta conectado; o host pode expulsar (o tecnico sai do jogo e o computador assume o time)
    public static void TecnicosNaSala(Sessao s, Sala sala, RetroFoot.Motor.Online.HostOnline? host)
    {
        var lista = new StackPanel { Spacing = 8 };
        lista.Children.Add(CUi.Txt("…", 13, CUi.Fraco));
        CUi.Folha f = null!;
        f = new CUi.Folha(T("Técnicos na sala"), lista, new CUi.Botao(T("Fechar"), true, () => s.FecharDialogo(f)) { HorizontalAlignment = HorizontalAlignment.Stretch },
            sub: Titulo(sala)) { AoVoltar = () => s.FecharDialogo(f) };
        s.AbrirDialogo(f);
        async void Encher()
        {
            try
            {
                var js = await sala.Jogadores();
                long dif = 0;
                try { dif = await sala.DiferencaDoRelogio(); } catch { }
                var hostUid = (string?)await sala.Host() ?? "";
                Dispatcher.UIThread.Post(() =>
                {
                    lista.Children.Clear();
                    foreach (var j in js.OrderBy(x => x.Vaga))
                    {
                        bool eu = j.Uid == sala.Uid, on = eu || Sala.Conectado(j, dif);
                        string time = j.Equipa != 0 && s.Equipa(j.Equipa) is { } e ? Dlg.Texto(e.NomeCurto) : "";
                        string situacao = j.Estado switch
                        {
                            Estados.Pedindo => T("pedindo para entrar"),
                            Estados.Espectador => T("espectador (recebe um time na próxima preparação)"),
                            Estados.Saiu => T("saiu por agora (de férias)"),
                            Estados.Expulso => T("expulso"),
                            _ => on ? T("conectado") : T("desconectado"),
                        };
                        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 44 };
                        var txt = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
                        txt.Children.Add(CUi.Txt(j.Nome + (eu ? " " + T("(você)") : "") + (j.Uid == hostUid ? " · " + T("host") : ""), 15, CUi.Texto, FontWeight.SemiBold));
                        txt.Children.Add(CUi.Txt((time.Length > 0 ? time + " · " : "") + situacao, 12.5, on ? CUi.Bom : CUi.Fraco));
                        g.Children.Add(txt);
                        if (host != null && !eu && j.Estado != Estados.Expulso)
                        {
                            var jj = j;
                            var b = new CUi.Botao(T("Expulsar"), false, () => { });
                            b.Clique += () =>
                            {
                                CUi.Folha c = null!;
                                c = CUi.PerguntaEmColuna(T("Expulsar"), jj.Equipa != 0
                                    ? T("Tirar {0} do jogo? O aparelho dele sai na hora; o técnico dele sai do jogo e o computador assume o time na próxima preparação.", jj.Nome)
                                    : T("Tirar {0} da sala?", jj.Nome),
                                    new (string, bool, Action)[]
                                    {
                                        (T("Expulsar"), true, () => { s.FecharDialogo(c); s.FecharDialogo(f); _ = host.Expulsar(jj.Uid); }),
                                        (T("Cancelar"), false, () => s.FecharDialogo(c)),
                                    }, () => s.FecharDialogo(c));
                                s.AbrirDialogo(c);
                            };
                            Grid.SetColumn(b, 1);
                            g.Children.Add(b);
                        }
                        lista.Children.Add(g);
                    }
                });
            }
            catch (Exception ex) { Dispatcher.UIThread.Post(() => { lista.Children.Clear(); lista.Children.Add(CUi.Txt(TelaOnline.Curto(ex), 13, CUi.Erro)); }); }
        }
        Encher();
    }

    // "Liga dos Amigos · K7MPQ2"
    public static string Titulo(Sala sala) => sala.Nome.Length > 0 ? $"{sala.Nome} · {sala.Codigo}" : sala.Codigo;

    static string ArquivoNome => Path.Combine(Path.GetDirectoryName(Configuracoes.Arquivo)!, "online-nome.txt");
    public static string UltimoNome
    {
        get { try { return File.Exists(ArquivoNome) ? File.ReadAllText(ArquivoNome).Trim() : ""; } catch { return ""; } }
        set { try { Directory.CreateDirectory(Path.GetDirectoryName(ArquivoNome)!); File.WriteAllText(ArquivoNome, value); } catch { } }
    }

    // o Android liga o servico em primeiro plano enquanto este aparelho e o host (o jogo de
    // todos roda aqui; em segundo plano ele pararia)
    public static Action<bool>? ManterVivo;

    // o sinal de vida do host fora do jogo (na sala e enquanto cria o jogo); null para
    static DispatcherTimer? _sinal;
    public static void Sinal(Sala? sala)
    {
        _sinal?.Stop();
        _sinal = null;
        if (sala == null) return;
        _sinal = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _sinal.Tick += (_, _) => _ = sala.SinalDeVida();
        _sinal.Start();
        _ = sala.SinalDeVida();
    }

    // o aviso de uma transferencia: "Fulano vai para o Palmeiras por $X (leilao)"
    public static string TextoTransferencia(RetroFoot.Motor.Save.Jogo98 s, int jogador, int comprador, int vendedor, long preco, int tipo)
    {
        var v = s.Equipas.FirstOrDefault(e => e.ID == vendedor);
        var p = s.Equipas.SelectMany(e => e.Jogadores).FirstOrDefault(x => x.ID == jogador);
        var c = s.Equipas.FirstOrDefault(e => e.ID == comprador);
        string nome = p == null ? "?" : System.Text.Encoding.Latin1.GetString(RetroFoot.Motor.Partida.Jornada.NomeNaTela(p)).Trim();
        string time = c == null ? "?" : Dlg.Texto(c.NomeCurto);
        string de = v == null ? "" : " (" + Dlg.Texto(v.NomeCurto) + ")";
        return T("🔁 {0}{1} vai para o {2} por {3} ({4})", nome, de, time, FormatoDinheiro.Valor(s, preco), tipo == 1 ? T("compra") : T("leilão"));
    }

    // o resultado do leilao de todos: "🔨 Fulano (Vasco) vai para o Palmeiras por $X" ou sem ofertas
    public static string TextoLeilao(RetroFoot.Motor.Save.Jogo98 s, int jogador, int vendedor, int comprador, long preco)
    {
        if (comprador != 0) return T("Leilão: ") + TextoTransferencia(s, jogador, comprador, vendedor, preco, 0).Replace("🔁 ", "");
        var v = s.Equipas.FirstOrDefault(e => e.ID == vendedor);
        var p = s.Equipas.SelectMany(e => e.Jogadores).FirstOrDefault(x => x.ID == jogador);
        string nome = p == null ? "?" : System.Text.Encoding.Latin1.GetString(RetroFoot.Motor.Partida.Jornada.NomeNaTela(p)).Trim();
        return T("Leilão de {0}{1}: não houve ofertas", nome, v == null ? "" : " (" + Dlg.Texto(v.NomeCurto) + ")");
    }

    public static string Versao => typeof(Online).Assembly.GetName().Version?.ToString(3) ?? "0";

    // a pagina do kit numa coluna no meio (no computador a tela e larga)
    public static Control Pagina(string titulo, string? sub, Action? voltar, Control corpo, Control? rodape = null) =>
        CUi.Pagina(CUi.BarraTopo(titulo, sub, voltar),
            new Border { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Stretch, Child = corpo },
            rodape == null ? null : new Border { MaxWidth = 560, Child = rodape });
}

// a entrada: o nome do tecnico, criar uma sala ou entrar numa com a chave
public sealed class TelaOnline : UserControl
{
    readonly Action<Control> _mostrar;
    readonly Action _voltar;

    public TelaOnline(Action<Control> mostrar, Action voltar)
    {
        _mostrar = mostrar; _voltar = voltar;
        FontFamily = CUi.Fonte;
        var nome = CUi.Caixa(Online.UltimoNome, 30, T("Seu nome de técnico"));
        var codigo = CUi.Caixa("", 6, T("CHAVE"));
        codigo.TextChanged += (_, _) =>
        {
            var t = codigo.Text ?? "";
            var u = t.ToUpperInvariant();
            if (u != t) { int c = codigo.CaretIndex; codigo.Text = u; codigo.CaretIndex = c; }
        };
        var aviso = CUi.Txt("", 13.5, CUi.Erro, FontWeight.Normal, true);
        aviso.IsVisible = false;
        CUi.Botao criar = null!, entrar = null!;
        void Ocupado(bool o) { criar.Habilitado = entrar.Habilitado = !o; }
        void Erro(string s) { aviso.Text = s; aviso.IsVisible = true; Ocupado(false); }
        var nomeSala = CUi.Caixa("", 40, T("Nome da sala (opcional)"));
        criar = new CUi.Botao(T("Criar sala"), true, async () =>
        {
            Online.UltimoNome = nome.Text ?? "";
            Ocupado(true); aviso.IsVisible = false;
            try
            {
                var b = Online.Banco;
                await b.Ler("salas/AAAAAA/info");   // (cria a identidade)
                var sala = await Sala.Criar(b, b.Uid, nome.Text ?? "", Online.Versao, nomeSala.Text);
                Online.Lembrar(sala);
                _mostrar(new TelaSala(_mostrar, _voltar, sala, true));
            }
            catch (Exception ex) { Erro(T("Não consegui criar a sala: {0}", Curto(ex))); }
        });
        async Task EntrarNa(string chave)
        {
            Online.UltimoNome = nome.Text ?? "";
            Ocupado(true); aviso.IsVisible = false;
            try
            {
                var b = Online.Banco;
                await b.Ler("salas/AAAAAA/info");
                var sala = await Sala.Entrar(b, b.Uid, chave, nome.Text ?? "");
                // (a sala do jogar com um amigo e de uma vez so: nao fica na lista)
                if (await sala.Modo() == Sala.ModoAmigo) Online.Esquecer(sala.Codigo);
                else Online.Lembrar(sala);
                _mostrar(new TelaSala(_mostrar, _voltar, sala, false));
            }
            catch (Sala.SalaCheia) { Erro(T("A sala está cheia (6 técnicos).")); }
            catch (Sala.SalaInexistente) { Online.Esquecer(chave.Trim().ToUpperInvariant()); Erro(T("Não existe sala com essa chave.")); }
            catch (Exception ex) { Erro(T("Não consegui entrar: {0}", Curto(ex))); }
        }
        entrar = new CUi.Botao(T("Entrar"), true, () => _ = EntrarNa(codigo.Text ?? ""));
        criar.HorizontalAlignment = entrar.HorizontalAlignment = HorizontalAlignment.Stretch;

        // as suas salas: os jogos online deste aparelho como host (data/online/<CHAVE>/JOGO.e98) e
        // as salas em que ele entrou como convidado; cada uma com o nome, a chave, o ano e o seu
        // time (o resumo que o host grava a cada rodada), para voltar sem digitar a chave
        var meus = new StackPanel { Spacing = 10 };
        var doHost = Online.JogosDoHost().ToDictionary(arq => Path.GetFileName(Path.GetDirectoryName(arq))!, arq => arq);
        var lembradas = Online.Salas();
        var codigos = doHost.Keys.Concat(lembradas.Select(x => x.Codigo)).Distinct()
            .OrderByDescending(c => doHost.TryGetValue(c, out var f) ? File.GetLastWriteTime(f) : lembradas.FirstOrDefault(x => x.Codigo == c)?.Quando ?? DateTime.MinValue)
            .ToList();
        foreach (var codigoJogo in codigos)
        {
            string? arq = doHost.GetValueOrDefault(codigoJogo);
            string nomeLembrado = lembradas.FirstOrDefault(x => x.Codigo == codigoJogo)?.Nome ?? "";
            var titulo = CUi.Txt(nomeLembrado.Length > 0 ? $"{nomeLembrado} · {codigoJogo}" : codigoJogo, 15, CUi.Texto, FontWeight.SemiBold);
            var detalhe = CUi.Txt("…", 13, CUi.Fraco, FontWeight.Normal, true);
            var papel = CUi.Etiqueta(arq != null ? T("host") : T("convidado"));
            var cab = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            cab.Children.Add(titulo);
            Grid.SetColumn(papel, 1);
            cab.Children.Add(papel);
            CUi.Botao b;
            if (arq != null)
                b = new CUi.Botao(T("Continuar"), true, async () =>
                {
                    Online.UltimoNome = nome.Text ?? "";
                    Ocupado(true); aviso.IsVisible = false;
                    try
                    {
                        var bn = Online.Banco;
                        await bn.Ler("salas/AAAAAA/info");
                        // a mesma sala, se ainda for dele; se a sala continua com outro host (ele passou o
                        // host ou caiu e outro assumiu), entra como os outros; senao uma sala nova (com a
                        // mesma chave e o mesmo nome, se der: os outros a acham na lista deles)
                        if (await Sala.Reabrir(bn, bn.Uid, codigoJogo) is { } minha) { Online.Lembrar(minha); _mostrar(new TelaSala(_mostrar, _voltar, minha, true, arq)); return; }
                        if (await bn.Ler($"salas/{codigoJogo}/info") != null)
                        {
                            try { File.Move(arq, arq + ".passado", true); } catch { }
                            var outra = await Sala.Entrar(bn, bn.Uid, codigoJogo, nome.Text ?? "");
                            Online.Lembrar(outra);
                            _mostrar(new TelaSala(_mostrar, _voltar, outra, false));
                            return;
                        }
                        var nova = await Sala.Criar(bn, bn.Uid, nome.Text ?? "", Online.Versao, nomeLembrado, default, codigoJogo);
                        Online.Lembrar(nova);
                        _mostrar(new TelaSala(_mostrar, _voltar, nova, true, arq));
                    }
                    catch (Exception ex) { Erro(T("Não consegui abrir a sala: {0}", Curto(ex))); }
                });
            else b = new CUi.Botao(T("Entrar"), true, () => _ = EntrarNa(codigoJogo));
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            // excluir a sala da lista (com a confirmacao no lugar dos botoes): o host apaga o jogo
            // online gravado e a sala (some para todos); o convidado sai dela de vez (o tecnico
            // dele sai do jogo e o computador assume o time)
            var botoes = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto") };
            var excluir = new CUi.Botao(T("Excluir"), false, () => { });
            Grid.SetColumn(excluir, 2);
            botoes.Children.Add(b);
            botoes.Children.Add(excluir);
            var confirmar = new StackPanel { Spacing = 6, IsVisible = false };
            var item = new StackPanel { Spacing = 4, Children = { cab, detalhe, botoes, confirmar } };
            excluir.Clique += () =>
            {
                confirmar.Children.Clear();
                confirmar.Children.Add(CUi.Txt(arq != null
                    ? T("Apagar este jogo online? A sala fecha para todos os técnicos e o jogo gravado neste aparelho é apagado.")
                    : T("Sair desta sala de vez? Seu técnico sai do jogo e o computador assume o time."), 13, CUi.Texto, FontWeight.Normal, true));
                var nao = new CUi.Botao(T("Cancelar"), false, () => { confirmar.IsVisible = false; botoes.IsVisible = true; });
                var sim = new CUi.Botao(arq != null ? T("Apagar") : T("Sair de vez"), true, async () =>
                {
                    Ocupado(true); aviso.IsVisible = false;
                    try
                    {
                        var bn = Online.Banco;
                        await bn.Ler("salas/AAAAAA/info");
                        if (arq != null)
                        {
                            // (a sala so se ainda for dele; o jogo gravado sempre)
                            if (await Sala.Reabrir(bn, bn.Uid, codigoJogo) is { } minha) await minha.Apagar();
                            try { Directory.Delete(Path.GetDirectoryName(arq)!, true); } catch { }
                        }
                        else if (await bn.Ler($"salas/{codigoJogo}/info") is { } info)
                        {
                            // (com o jogo comecado, desiste: o host tira o tecnico; antes, so sai da sala)
                            var sala = await Sala.Entrar(bn, bn.Uid, codigoJogo, nome.Text ?? "");
                            if ((string?)info["fase"] is Fases.Jogando or Fases.Pausado or Fases.Continuando) await sala.Desistir();
                            else await sala.Sair();
                        }
                        Online.Esquecer(codigoJogo);
                        meus.Children.Remove(item);
                        Ocupado(false);
                    }
                    catch (Exception ex) { Erro(T("Não consegui excluir: {0}", Curto(ex))); }
                });
                nao.HorizontalAlignment = sim.HorizontalAlignment = HorizontalAlignment.Stretch;
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*") };
                Grid.SetColumn(sim, 2);
                g.Children.Add(nao);
                g.Children.Add(sim);
                confirmar.Children.Add(g);
                botoes.IsVisible = false;
                confirmar.IsVisible = true;
            };
            meus.Children.Add(item);
            // o resumo, do banco
            _ = Task.Run(async () =>
            {
                try
                {
                    var bn = Online.Banco;
                    var r = await Sala.LerResumo(bn, bn.Uid, codigoJogo);
                    Dispatcher.UIThread.Post(() =>
                    {
                        // (a sala do jogar com um amigo, de uma vez so, gravada antes: sai da lista)
                        if (r != null && arq == null && r.Modo == Sala.ModoAmigo) r = null;
                        if (r == null)
                        {
                            // a sala acabou: o host continua o jogo dele (abre uma sala nova); o convidado
                            // nao tem mais o que abrir
                            if (arq != null) { detalhe.Text = T("A sala foi fechada. Continuar abre a sala de novo (com esta chave, se estiver livre)."); return; }
                            Online.Esquecer(codigoJogo);
                            meus.Children.Remove(item);
                            return;
                        }
                        if (r.Nome.Length > 0) titulo.Text = $"{r.Nome} · {codigoJogo}";
                        var partes = new List<string>();
                        if (r.Ano > 0) partes.Add(r.Rodada.Length > 0 ? $"{r.Ano} · {r.Rodada}" : r.Ano.ToString());
                        if (r.Time.Length > 0) partes.Add(r.Divisao.Length > 0 ? $"{r.Time} ({r.Divisao})" : r.Time);
                        detalhe.Text = partes.Count > 0 ? string.Join(" · ", partes) : T("O jogo ainda não começou.");
                    });
                }
                catch (Exception ex) { Dispatcher.UIThread.Post(() => detalhe.Text = Curto(ex)); }
            });
        }
        var corpo = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                CUi.Cartao_(T("Técnico"), new StackPanel { Spacing = 8, Children = { CUi.Txt(T("O nome que os outros veem na sala."), 13, CUi.Fraco, FontWeight.Normal, true), nome } }),
                CUi.Cartao_(T("Entrar numa sala"), new StackPanel { Spacing = 8, Children = { CUi.Txt(T("A chave de 6 letras que o host passou."), 13, CUi.Fraco, FontWeight.Normal, true), codigo, entrar } }),
                CUi.Cartao_(T("Criar uma sala"), new StackPanel { Spacing = 8, Children = { CUi.Txt(T("Você será o host: o jogo roda no seu aparelho. Até 6 técnicos, um por aparelho."), 13, CUi.Fraco, FontWeight.Normal, true), nomeSala, criar } }),
                aviso,
            },
        };
        if (meus.Children.Count > 0) corpo.Children.Insert(1, CUi.Cartao_(T("Suas salas"), meus));
        // jogar um save seu com um amigo (o amigo assume um time da 4a divisao; depois o save
        // volta a ser so seu)
        var amigo = new StackPanel { Spacing = 8 };
        amigo.Children.Add(CUi.Txt(T("Abra um save seu como sala, para jogar uma vez: cada amigo que entrar assume um time da 4ª divisão (se cair, o técnico dele fica de férias até ele voltar). Quando você fechar o jogo, os amigos são demitidos e saem do save, e o computador assume os times deles."), 13, CUi.Fraco, FontWeight.Normal, true));
        foreach (var arq in Online.SavesLocais())
        {
            var nomeSave = Path.GetFileNameWithoutExtension(arq);
            // o que e cada save: o ano, os tecnicos humanos (time e divisao) e quando foi gravado
            var tituloSave = CUi.Txt(nomeSave.ToUpperInvariant(), 15, CUi.Texto, FontWeight.SemiBold);
            var detalheSave = CUi.Txt("…", 13, CUi.Fraco, FontWeight.Normal, true);
            _ = Task.Run(() =>
            {
                try
                {
                    var g = TelaInicio.LerJogo(arq);
                    var partes = new List<string>();
                    if (g.Ano > 0) partes.Add(g.Ano.ToString());
                    partes.AddRange(g.Humanos.Select(x => x.ToString()));
                    if (g.Quando.Length > 0) partes.Add(T("gravado {0}", g.Quando));
                    Dispatcher.UIThread.Post(() => detalheSave.Text = partes.Count > 0 ? string.Join(" · ", partes) : T("save ilegível"));
                }
                catch { Dispatcher.UIThread.Post(() => detalheSave.Text = ""); }
            });
            var item = new StackPanel { Spacing = 4, Children = { tituloSave, detalheSave } };
            amigo.Children.Add(item);
            item.Children.Add(new CUi.Botao(T("Jogar com um amigo"), false, async () =>
            {
                Online.UltimoNome = nome.Text ?? "";
                Ocupado(true); aviso.IsVisible = false;
                try
                {
                    var bn = Online.Banco;
                    await bn.Ler("salas/AAAAAA/info");
                    var sala = await Sala.Criar(bn, bn.Uid, nome.Text ?? "", Online.Versao,
                        string.IsNullOrWhiteSpace(nomeSala.Text) ? T("{0} com amigos", nomeSave) : nomeSala.Text);
                    await sala.DefinirModo(Sala.ModoAmigo);
                    _mostrar(new TelaSala(_mostrar, _voltar, sala, true, arq, modoAmigo: true));
                }
                catch (Exception ex) { Erro(T("Não consegui criar a sala: {0}", Curto(ex))); }
            }) { HorizontalAlignment = HorizontalAlignment.Stretch });
        }
        if (amigo.Children.Count > 1) corpo.Children.Add(CUi.Cartao_(T("Jogar com um amigo"), amigo));
        Content = Online.Pagina(T("Jogo online"), T("Sem login: só a chave da sala"), _voltar, corpo);
    }

    internal static string Curto(Exception ex) => ex is HttpRequestException ? T("sem conexão com a internet") : ex.Message.Split('\n')[0];
}

// a sala: a chave, as 6 vagas ao vivo e, no host, o Comecar; no cliente, a espera pelo jogo
public sealed class TelaSala : UserControl
{
    // arquivo: o jogo online gravado que o host continua (null: um jogo novo)
    // modoAmigo: o save single player do host (arquivo) com amigos
    public TelaSala(Action<Control> mostrar, Action voltar, Sala sala, bool host, string? arquivo = null, bool modoAmigo = false)
    {
        FontFamily = CUi.Fonte;
        var lista = new StackPanel { Spacing = 6 };
        List<JogadorSala> jogadores = new();
        IDisposable? obs = null;
        ClienteOnline? cliente = null;
        IDisposable? obsInfo = null;
        var comecar = new CUi.Botao(arquivo == null ? T("Começar jogo novo") : T("Continuar o jogo"), true, () =>
        {
            obs?.Dispose();
            obsInfo?.Dispose();
            _ = sala.MarcarFase(arquivo == null ? Fases.Criando : Fases.Continuando);
            mostrar(new TelaInicio(mostrar, new TelaInicio.OnlineNovo(sala, jogadores.ToList(), arquivo, null, modoAmigo)));
        }) { HorizontalAlignment = HorizontalAlignment.Stretch };
        string hostUid = host ? sala.Uid : "";
        long? difRelogio = null;
        _ = Task.Run(async () => { try { var d = await sala.DiferencaDoRelogio(); Dispatcher.UIThread.Post(() => { difRelogio = d; Desenhar(); }); } catch { } });
        // (o estado conectado muda com o tempo, sem novidade no banco: refaz de 5 em 5 s)
        var refazer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        refazer.Tick += (_, _) => Desenhar();
        refazer.Start();
        DetachedFromVisualTree += (_, _) => refazer.Stop();
        void Desenhar()
        {
            lista.Children.Clear();
            for (int v = 0; v < Sala.Vagas; v++)
            {
                var j = jogadores.FirstOrDefault(x => x.Vaga == v);
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,Auto"), MinHeight = 36 };
                g.Children.Add(CUi.Txt((v + 1) + ".", 14, CUi.Fraco));
                var n = CUi.Txt(j == null ? T("vaga livre") : j.Nome + (j.Uid == sala.Uid ? " " + T("(você)") : ""), 15, j == null ? CUi.Fraco : CUi.Texto, j?.Uid == sala.Uid ? FontWeight.Bold : FontWeight.Normal);
                Control nomeEStatus = n;
                if (j != null)
                {
                    // conectado (o sinal de vida recente) ou nao; este aparelho sempre esta
                    bool on = j.Uid == sala.Uid || (difRelogio is long d && Sala.Conectado(j, d));
                    var st = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                    st.Children.Add(new Avalonia.Controls.Shapes.Ellipse { Width = 10, Height = 10, Fill = on ? CUi.Bom : Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
                    st.Children.Add(CUi.Txt(difRelogio == null && j.Uid != sala.Uid ? "…" : on ? T("conectado") : T("desconectado"), 12, on ? CUi.Bom : CUi.Fraco));
                    nomeEStatus = new StackPanel { Spacing = 1, Children = { n, st } };
                }
                Grid.SetColumn(nomeEStatus, 1);
                g.Children.Add(nomeEStatus);
                if (j != null && j.Uid == hostUid)
                {
                    var et = CUi.Etiqueta(T("host"));
                    Grid.SetColumn(et, 2);
                    g.Children.Add(et);
                }
                lista.Children.Add(g);
            }
            comecar.Habilitado = jogadores.Count >= 1;
        }
        obs = sala.B.Observar($"{sala.Raiz}/jogadores", (_, _) => _ = Task.Run(async () =>
        {
            try
            {
                var l = await sala.Jogadores();
                Dispatcher.UIThread.Post(() => { jogadores = l; Desenhar(); });
            }
            catch { }
        }));
        Desenhar();
        var obsHost = sala.B.Observar($"{sala.Raiz}/host", (c, v) => Dispatcher.UIThread.Post(() => { hostUid = (string?)v ?? ""; Desenhar(); }));
        DetachedFromVisualTree += (_, _) => obsHost.Dispose();
        var chave = new TextBlock
        {
            Text = sala.Codigo, FontSize = 40, FontWeight = FontWeight.Bold, Foreground = CUi.Destaque, FontFamily = new FontFamily("monospace"),
            HorizontalAlignment = HorizontalAlignment.Center, LetterSpacing = 6,
        };
        // copiar a chave (para colar no WhatsApp, Telegram...)
        CUi.Botao copiar = null!;
        copiar = new CUi.Botao(T("Copiar chave"), false, async () =>
        {
            try
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
                {
                    await cb.SetTextAsync(sala.Codigo);
                    copiar.Texto = T("Chave copiada!");
                }
            }
            catch { }
        }) { HorizontalAlignment = HorizontalAlignment.Center };
        var aviso = CUi.Txt(host ? T("Passe esta chave aos amigos. Eles entram em Jogo online > Entrar.") : T("Aguardando o host começar o jogo…"), 13, CUi.Fraco, FontWeight.Normal, true);
        // (cliente) o host comecou: o jogo sendo criado e os times sorteados
        var criandoTxt = CUi.Txt("", 14, CUi.Texto, FontWeight.SemiBold, true);
        var criando = new StackPanel
        {
            Spacing = 8, IsVisible = false,
            Children = { criandoTxt, new ProgressBar { IsIndeterminate = true, Height = 6 } },
        };
        // as regras da sala: o host escolhe; os outros veem
        var prazoTxt = CUi.Txt("", 13, CUi.Fraco, FontWeight.Normal, true);
        Control regras;
        if (host)
        {
            var (esc, marcar) = CUi.Escolha(new[] { "1 min", "2 min", "3 min", "4 min", "5 min" }, Sala.PrazoPadrao - 1, i => _ = sala.DefinirPrazo(i + 1));
            _ = Task.Run(async () => { try { var p = await sala.Prazo(); Dispatcher.UIThread.Post(() => marcar(p - 1)); } catch { } });
            // a velocidade da partida (os perfis de tempo do jogo: 1 lento a 6 ultrassonico)
            var nomesVel = OpcoesDoJogo.Perfis.Select(x => T(x.Nome.Trim())).ToArray();
            var (escVel, marcarVel) = CUi.Escolha(nomesVel, Sala.VelocidadePadrao - 1, i => _ = sala.DefinirVelocidade(i + 1));
            _ = Task.Run(async () => { try { var v = await sala.Velocidade(); Dispatcher.UIThread.Post(() => marcarVel(v - 1)); } catch { } });
            regras = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    CUi.Txt(T("Tempo para todos ficarem prontos depois do primeiro PRONTO da rodada. Acabou, quem não estiver pronto vai com a escalação automática."), 13, CUi.Fraco, FontWeight.Normal, true), esc,
                    CUi.Txt(T("Velocidade da partida (para todos)."), 13, CUi.Fraco, FontWeight.Normal, true), escVel,
                },
            };
        }
        else regras = prazoTxt;
        obsInfo = sala.B.Observar($"{sala.Raiz}/info", (c, v) => _ = Task.Run(async () =>
        {
            try
            {
                var info = await sala.B.Ler($"{sala.Raiz}/info");
                int prazo = Math.Clamp((int?)info?["prazo"] ?? Sala.PrazoPadrao, 1, 5);
                int vel = Math.Clamp((int?)info?["velocidade"] ?? Sala.VelocidadePadrao, 1, OpcoesDoJogo.Perfis.Length);
                string? fase = (string?)info?["fase"];
                Dispatcher.UIThread.Post(() =>
                {
                    prazoTxt.Text = T("Tempo para ficar pronto depois do primeiro PRONTO: {0} min.", prazo) + "\n"
                        + T("Velocidade da partida: {0}.", T(OpcoesDoJogo.Perfis[vel - 1].Nome.Trim()));
                    if (host) return;
                    criandoTxt.Text = fase switch
                    {
                        Fases.Criando => T("O host está criando o jogo e sorteando os times…"),
                        Fases.Continuando => T("O host está abrindo o jogo…"),
                        _ => "",
                    };
                    criando.IsVisible = criandoTxt.Text.Length > 0;
                    aviso.Text = fase == Fases.Pausado ? T("Aguardando o host continuar o jogo…") : T("Aguardando o host começar o jogo…");
                    aviso.IsVisible = !criando.IsVisible;
                });
            }
            catch { }
        }));
        var corpo = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                CUi.Cartao_(T("Chave da sala"), new StackPanel { Spacing = 8, Children = { chave, copiar, aviso, criando } }),
                CUi.Cartao_(T("Técnicos"), lista),
                CUi.Cartao_(T("Regras da sala"), regras),
            },
        };
        async void Sair()
        {
            obs?.Dispose();
            obsInfo?.Dispose();
            cliente?.Dispose();
            // (o host de um jogo gravado so sai: a sala fica para continuar depois)
            if (host) Online.Sinal(null);
            // (o host de um jogo gravado so sai: a sala fica para continuar depois; a do modo amigo
            // e de uma sessao so)
            try { if (host && (arquivo == null || modoAmigo)) await sala.Apagar(); else if (host) await sala.Pausar(); else await sala.Sair(); } catch { }
            voltar();
        }
        // (host) o sinal de vida desde a sala, antes do jogo (e enquanto ele cria o jogo): os
        // outros nao acham que ele caiu; o HostOnline continua dali
        if (host) Online.Sinal(sala);
        if (!host)
        {
            // o jogo comecou quando chega o primeiro retrato
            cliente = new ClienteOnline(sala);
            var jogo = new JogoRemoto(mostrar, voltar, sala, cliente);
            jogo.Comecou += () => { obs?.Dispose(); obsInfo?.Dispose(); };
        }
        Content = Online.Pagina(sala.Nome.Length > 0 ? sala.Nome : T("Sala {0}", sala.Codigo),
            (host ? T("Você é o host") + " · " : "") + T("chave {0}", sala.Codigo), Sair, corpo, host ? comecar : null);
    }
}

// o host depois de marcar PRONTO: quem ainda esta preparando
public sealed class TelaAguardandoOnline : UserControl
{
    public TelaAguardandoOnline(Sessao s, string? aviso = null)
    {
        FontFamily = CUi.Fonte;
        var lista = new StackPanel { Spacing = 6 };
        void Desenhar(IReadOnlyList<JogadorSala> l)
        {
            lista.Children.Clear();
            foreach (var j in l.Where(x => x.Equipa != 0 && (s.Online?.Joga(x.Equipa) ?? true)))
                lista.Children.Add(TelaEsperaOnline.Linha(j, s.Online?.Rodada ?? "", s.Equipa(j.Equipa)));
        }
        Action<IReadOnlyList<JogadorSala>> mudou = l => Desenhar(l);
        s.JogadoresOnline += mudou;
        DetachedFromVisualTree += (_, _) => s.JogadoresOnline -= mudou;
        Control corpo = CUi.Cartao_(null, lista);
        if (aviso != null) corpo = new StackPanel { Spacing = 12, Children = { CUi.Cartao_(null, CUi.Txt(aviso, 14, CUi.Texto, FontWeight.Normal, true)), corpo } };
        Content = Online.Pagina(T("Aguardando os técnicos"),
            (s.Online?.Sala is { } sala ? Online.Titulo(sala) + " · " : "") + T("A rodada começa quando todos estiverem prontos"), null, corpo);
    }
}

public static class TelaEsperaOnline
{
    // um tecnico: o time e PRONTO ou AGUARDANDO
    public static Control Linha(JogadorSala j, string rodada, RetroFoot.Motor.Save.Equipa? e)
    {
        bool pronto = j.Estado == Estados.Pronto && j.Rodada == rodada;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 40 };
        var nome = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        nome.Children.Add(CUi.Txt(j.Nome, 15, CUi.Texto, FontWeight.SemiBold));
        if (e != null) nome.Children.Add(CUi.Txt(Dlg.Texto(e.NomeCurto), 12.5, CUi.Fraco));
        g.Children.Add(nome);
        var et = CUi.Etiqueta(pronto ? T("PRONTO") : j.Estado == Estados.Saiu ? T("SAIU (automático)") : T("AGUARDANDO…"), pronto ? CUi.Bom : CUi.Fraco);
        Grid.SetColumn(et, 1);
        g.Children.Add(et);
        return g;
    }
}

// o sorteio dos times aos tecnicos da sala (o jogo novo online): cada um com o time que caiu
// para ele, um de cada vez; depois o Continuar
public sealed class TelaSorteioTimes : UserControl
{
    public TelaSorteioTimes(RetroFoot.Motor.Save.Jogo98 s, IReadOnlyList<(string Nome, ushort Equipa)> tecnicos, Action continuar)
    {
        FontFamily = CUi.Fonte;
        var lista = new StackPanel { Spacing = 10 };
        var botao = new CUi.Botao(T("Continuar"), true, continuar) { HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
        string Divisao(ushort id)
        {
            for (int d = 0; d < s.Divisoes.Count; d++)
                if (s.Divisoes[d].Equipas.Contains(id)) return d < 4 ? T("{0}ª Divisão", d + 1) : T("Distrital");
            return "";
        }
        int k = 0;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        t.Tick += (_, _) =>
        {
            if (k >= tecnicos.Count) { t.Stop(); botao.IsEnabled = true; return; }
            var (nome, id) = tecnicos[k++];
            var e = s.Equipas.FirstOrDefault(x => x.ID == id);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 48 };
            var esq = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            esq.Children.Add(CUi.Txt(nome, 16, CUi.Texto, FontWeight.Bold));
            if (e != null) esq.Children.Add(CUi.Txt(Divisao(e.ID) + " · " + TextosMercado.PaisDe(e.Pais), 12.5, CUi.Fraco));
            g.Children.Add(esq);
            if (e != null)
            {
                var n = CUi.NomeEquipa(e, Dlg.Texto(e.NomeCurto), 16, true);
                n.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(n, 1);
                g.Children.Add(n);
            }
            lista.Children.Add(CUi.Cartao_(null, g));
            Som.Tocar(Som.Efeito.Sorteio);
        };
        t.Start();
        DetachedFromVisualTree += (_, _) => t.Stop();
        Content = Online.Pagina(T("Sorteio dos times"), T("O time de cada técnico da sala"), null, lista, botao);
    }
}
