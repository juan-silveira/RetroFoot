using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    static BancoFirebase? _banco;

    // a identidade anonima do aparelho (guardada junto das Configuracoes) e o banco
    public static BancoFirebase Banco => _banco ??= new BancoFirebase(Http,
        new IdentidadeFirebase(Http, Path.Combine(Path.GetDirectoryName(Configuracoes.Arquivo)!, "online-id.json")));

    // os jogos online ficam fora da pasta dos saves (as ferramentas nao os veem)
    public static string Pasta(string codigo) => Path.Combine(Path.GetDirectoryName(DadosJogo.Saves)!, "online", codigo);

    static string ArquivoNome => Path.Combine(Path.GetDirectoryName(Configuracoes.Arquivo)!, "online-nome.txt");
    public static string UltimoNome
    {
        get { try { return File.Exists(ArquivoNome) ? File.ReadAllText(ArquivoNome).Trim() : ""; } catch { return ""; } }
        set { try { Directory.CreateDirectory(Path.GetDirectoryName(ArquivoNome)!); File.WriteAllText(ArquivoNome, value); } catch { } }
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
        criar = new CUi.Botao(T("Criar sala"), true, async () =>
        {
            Online.UltimoNome = nome.Text ?? "";
            Ocupado(true); aviso.IsVisible = false;
            try
            {
                var b = Online.Banco;
                await b.Ler("salas/AAAAAA/info");   // (cria a identidade)
                var sala = await Sala.Criar(b, b.Uid, nome.Text ?? "", Online.Versao);
                _mostrar(new TelaSala(_mostrar, _voltar, sala, true));
            }
            catch (Exception ex) { Erro(T("Não consegui criar a sala: {0}", Curto(ex))); }
        });
        entrar = new CUi.Botao(T("Entrar"), true, async () =>
        {
            Online.UltimoNome = nome.Text ?? "";
            Ocupado(true); aviso.IsVisible = false;
            try
            {
                var b = Online.Banco;
                await b.Ler("salas/AAAAAA/info");
                var sala = await Sala.Entrar(b, b.Uid, codigo.Text ?? "", nome.Text ?? "");
                _mostrar(new TelaSala(_mostrar, _voltar, sala, false));
            }
            catch (Sala.SalaCheia) { Erro(T("A sala está cheia (6 técnicos).")); }
            catch (Sala.SalaInexistente) { Erro(T("Não existe sala com essa chave.")); }
            catch (Exception ex) { Erro(T("Não consegui entrar: {0}", Curto(ex))); }
        });
        criar.HorizontalAlignment = entrar.HorizontalAlignment = HorizontalAlignment.Stretch;
        var corpo = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                CUi.Cartao_(T("Técnico"), new StackPanel { Spacing = 8, Children = { CUi.Txt(T("O nome que os outros veem na sala."), 13, CUi.Fraco, FontWeight.Normal, true), nome } }),
                CUi.Cartao_(T("Entrar numa sala"), new StackPanel { Spacing = 8, Children = { CUi.Txt(T("A chave de 6 letras que o host passou."), 13, CUi.Fraco, FontWeight.Normal, true), codigo, entrar } }),
                CUi.Cartao_(T("Criar uma sala"), new StackPanel { Spacing = 8, Children = { CUi.Txt(T("Você será o host: o jogo roda no seu aparelho. Até 6 técnicos, um por aparelho."), 13, CUi.Fraco, FontWeight.Normal, true), criar } }),
                aviso,
            },
        };
        Content = Online.Pagina(T("Jogo online"), T("Sem login: só a chave da sala"), _voltar, corpo);
    }

    internal static string Curto(Exception ex) => ex is HttpRequestException ? T("sem conexão com a internet") : ex.Message.Split('\n')[0];
}

// a sala: a chave, as 6 vagas ao vivo e, no host, o Comecar; no cliente, a espera pelo jogo
public sealed class TelaSala : UserControl
{
    public TelaSala(Action<Control> mostrar, Action voltar, Sala sala, bool host)
    {
        FontFamily = CUi.Fonte;
        var lista = new StackPanel { Spacing = 6 };
        List<JogadorSala> jogadores = new();
        IDisposable? obs = null;
        ClienteOnline? cliente = null;
        var comecar = new CUi.Botao(T("Começar jogo novo"), true, () =>
        {
            obs?.Dispose();
            mostrar(new TelaInicio(mostrar, new TelaInicio.OnlineNovo(sala, jogadores.ToList())));
        }) { HorizontalAlignment = HorizontalAlignment.Stretch };
        void Desenhar()
        {
            lista.Children.Clear();
            for (int v = 0; v < Sala.Vagas; v++)
            {
                var j = jogadores.FirstOrDefault(x => x.Vaga == v);
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("28,*,Auto"), MinHeight = 36 };
                g.Children.Add(CUi.Txt((v + 1) + ".", 14, CUi.Fraco));
                var n = CUi.Txt(j == null ? T("vaga livre") : j.Nome + (j.Uid == sala.Uid ? " " + T("(você)") : ""), 15, j == null ? CUi.Fraco : CUi.Texto, j?.Uid == sala.Uid ? FontWeight.Bold : FontWeight.Normal);
                Grid.SetColumn(n, 1);
                g.Children.Add(n);
                if (j != null && v == 0)
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
        var chave = new TextBlock
        {
            Text = sala.Codigo, FontSize = 40, FontWeight = FontWeight.Bold, Foreground = CUi.Destaque, FontFamily = new FontFamily("monospace"),
            HorizontalAlignment = HorizontalAlignment.Center, LetterSpacing = 6,
        };
        var corpo = new StackPanel
        {
            Spacing = 14,
            Children =
            {
                CUi.Cartao_(T("Chave da sala"), new StackPanel { Spacing = 6, Children = { chave, CUi.Txt(host ? T("Passe esta chave aos amigos. Eles entram em Jogo online > Entrar.") : T("Aguardando o host começar o jogo…"), 13, CUi.Fraco, FontWeight.Normal, true) } }),
                CUi.Cartao_(T("Técnicos"), lista),
            },
        };
        async void Sair()
        {
            obs?.Dispose();
            cliente?.Dispose();
            try { if (host) await sala.Apagar(); else await sala.Sair(); } catch { }
            voltar();
        }
        if (!host)
        {
            // o jogo comecou quando chega o primeiro retrato
            cliente = new ClienteOnline(sala);
            var jogo = new JogoRemoto(mostrar, voltar, sala, cliente);
            jogo.Comecou += () => obs?.Dispose();
        }
        Content = Online.Pagina(T("Sala {0}", sala.Codigo), host ? T("Você é o host") : null, Sair, corpo, host ? comecar : null);
    }
}

// o host depois de marcar PRONTO: quem ainda esta preparando
public sealed class TelaAguardandoOnline : UserControl
{
    public TelaAguardandoOnline(Sessao s)
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
        Content = Online.Pagina(T("Aguardando os técnicos"), T("A rodada começa quando todos estiverem prontos"), null, CUi.Cartao_(null, lista));
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
        var et = CUi.Etiqueta(pronto ? T("PRONTO") : T("AGUARDANDO…"), pronto ? CUi.Bom : CUi.Fraco);
        Grid.SetColumn(et, 1);
        g.Children.Add(et);
        return g;
    }
}
