using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.NovoJogo;
using RetroFoot.Motor.Save;
using NovoOpcoes = RetroFoot.Motor.NovoJogo.Opcoes;
using TRegras = RetroFoot.Motor.Regras.Regras;

using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// A janela principal antes de haver jogo (TmainWindow: verde, maximizada) com os dialogos
// do comeco, nas posicoes dos DFM e no fluxo do original (ui/jogo/abertura.go e
// novojogo.go da versao em Go):
//   TselectGameDlg: jogo novo (nome do arquivo, 8 letras) ou continuar um jogo gravado;
//   jogo novo: TselectCountriesDlg -> TcurrencyDlg -> TgetTeamsDlg (ate 6 humanos, sorteio
//   das equipas) -> Motor.NovoJogo cria o estado -> o 1o save e gravado em JOGOS.
// Depois cria a Sessao com o save e entrega a janela as telas da partida.
public sealed partial class TelaInicio : UserControl
{
    readonly Action<Control> mostrar;
    readonly Canvas tela = new() { Width = 800, Height = 600, Background = W95.Verde, ClipToBounds = true };
    readonly List<Janela> pilha = new();
    readonly string? pastaJogo;    // a pasta de dados (DadosJogo: countries.tsv, teams...) ou a do jogo classico
    readonly string pastaJogos;    // os .e98 (saves/ da pasta de dados)
    Configuracoes cfg = Configuracoes.Ler();

    // o jogo novo de uma sala online (TelaSala): os tecnicos sao os da sala, um time cada
    // Arquivo: o jogo online gravado que o host continua (null: um jogo novo)
    // Retrato: o host passado para este aparelho (ou assumido): o jogo continua do retrato
    public sealed record OnlineNovo(RetroFoot.Motor.Online.Sala Sala, List<RetroFoot.Motor.Online.JogadorSala> Jogadores, string? Arquivo = null,
        RetroFoot.Motor.Online.Retrato? Retrato = null);
    readonly OnlineNovo? online;

    public TelaInicio(Action<Control> mostrar, OnlineNovo online) : this(mostrar, false)
    {
        this.online = online;
        if (online.Retrato is { } r)
        {
            // o host veio para este aparelho: o jogo do retrato, gravado na pasta da sala
            var arq = online.Arquivo ?? Path.Combine(Online.Pasta(online.Sala.Codigo), "JOGO.e98");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(arq)!);
                File.WriteAllBytes(arq, r.Save);
                Comecar(Jogo98.Ler(r.Save), arq, false, r);
            }
            catch (Exception ex) { Mensagem(T("Erro"), ex.Message, () => mostrar(new TelaHub(mostrar))); }
        }
        else if (online.Arquivo != null) ContinuarOnline(online.Arquivo);
        else NovoJogo(Path.Combine(Online.Pasta(online.Sala.Codigo), "JOGO.e98"));
    }

    // o jogo online gravado: quem esta na sala sem time recebe o do tecnico com o nome dele
    async void ContinuarOnline(string arquivo)
    {
        var o = online!;
        Jogo98 s;
        try
        {
            s = Jogo98.Ler(File.ReadAllBytes(arquivo));
            var usados = o.Jogadores.Where(x => x.Equipa != 0).Select(x => x.Equipa).ToHashSet();
            foreach (var j in o.Jogadores.Where(x => x.Equipa == 0))
            {
                var t = s.Treinadores.FirstOrDefault(t => t.Humano != 0 && TextoSave(t.Nome).Trim().Equals(j.Nome.Trim(), StringComparison.OrdinalIgnoreCase)
                    && s.Equipas.Any(e => e.Treinador == t.ID && !usados.Contains(e.ID)));
                if (t != null && s.Equipas.FirstOrDefault(e => e.Treinador == t.ID) is { } e)
                {
                    await o.Sala.DarEquipa(j.Uid, e.ID);
                    usados.Add(e.ID);
                }
            }
        }
        catch (Exception ex) { Mensagem(T("Erro"), ex.Message, () => mostrar(new TelaHub(mostrar))); return; }
        Comecar(s, arquivo, false);
    }

    public TelaInicio(Action<Control> mostrar) : this(mostrar, true) { }

    TelaInicio(Action<Control> mostrar, bool escolher)
    {
        this.mostrar = mostrar;
        Focusable = true;
        pastaJogo = AcharPastaJogo();
        pastaJogos = Environment.GetEnvironmentVariable("RETROFOOT_JOGOS") is { Length: > 0 } j ? j : DadosJogo.Saves;
        // Moderno: a tela escura (TelaInicioModerna.cs); Original: o verde do original, mais
        // largo em 16:9 (as janelas no centro)
        // no celular: as paginas em pe do kit (TelaInicioCelular.cs), nos dois visuais
        if (Celular.Ativo) MontarCelular();
        else if (Visual.Moderno) MontarModerno();
        else
        {
            Background = W95.Verde;
            Content = new Viewbox { Stretch = Stretch.Uniform, Child = tela };
            Largura();
            Action mudou = () => Dispatcher.UIThread.Post(Largura);
            AttachedToVisualTree += (_, _) => { Formato.Mudou -= mudou; Formato.Mudou += mudou; };
            DetachedFromVisualTree += (_, _) => Formato.Mudou -= mudou;
        }
        if (escolher) EscolherJogo();
    }

    // Ao abrir o jogo: o Sobre primeiro (como a TaboutDlg do original) e, ao fechar, o inicio
    public static Control ComSobre(Action<Control> mostrar)
    {
        var quadro = new Grid { Width = 800, Height = 600 };
        // a capa do jogo no fundo (a do formato); o Sobre embaixo, para a bola aparecer
        IBrush fundo = Visual.Moderno ? ClubeModerno.Fundo : W95.Verde;
        try
        {
            var capa = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(
                new Uri($"avares://RetroFoot.App/Assets/Marca/{(Formato.Largo ? "capa-16x9" : "capa-4x3")}.jpg")));
            fundo = new ImageBrush(capa) { Stretch = Stretch.UniformToFill };
        }
        catch { /* sem a capa: a cor de antes */ }
        var raiz = new Border
        {
            Background = fundo,
            Child = new Viewbox { Stretch = Stretch.Uniform, Child = quadro },
        };
        var d = new Dialogos(c =>
        {
            c.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            c.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
            c.Margin = new Thickness(0, 0, 0, 24);
            quadro.Children.Add(c);
            Dispatcher.UIThread.Post(() => c.Focus());
        }, c => quadro.Children.Remove(c));
        bool foi = false;
        // depois do Sobre, o hub (TelaHub: Jogar, ferramentas, Configuracoes...)
        void Seguir() { if (foi) return; foi = true; mostrar(new TelaHub(mostrar)); }
        raiz.AttachedToVisualTree += (_, _) => { if (quadro.Children.Count == 0 && !foi) TelaAcerca.Abrir(d, Seguir); };
        return raiz;
    }

    // 4:3: o quadro de 800x600 do original; 16:9: 1067x600, o verde ate as bordas
    void Largura()
    {
        tela.Width = Formato.Largo ? 1067 : 800;
        foreach (var j in pilha)
        {
            Canvas.SetLeft(j, Math.Floor((tela.Width - j.Width) / 2));
            Canvas.SetTop(j, Math.Floor((tela.Height - j.Height) / 2));
        }
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => { if (Visual.Moderno || Celular.Ativo) FocarModerno(); else if (pilha.Count > 0) pilha[^1].Focar(); else Focus(); });
    }

    // ---------------------------------------------------------------- pastas
    // RETROFOOT_JOGO (uma pasta do jogo classico ou de dados, para testes) ou a pasta de dados do
    // RetroFoot (DadosJogo: preparada aqui, com os arquivos soltos convertidos); se ela nao
    // tiver os dados, a do launcher instalado ou (desenvolvimento) game/ subindo do executavel
    internal static string? PastaDoJogo() => AcharPastaJogo();

    static string? AcharPastaJogo()
    {
        var c = new List<string>();
        if (Environment.GetEnvironmentVariable("RETROFOOT_JOGO") is { Length: > 0 } v) c.Add(v);
        else c.Add(DadosJogo.Preparar());
        if (Motor.Arquivos.PastaClassica.Instalada() is string inst) c.Add(inst);
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            c.Add(Path.Combine(d.FullName, "game"));
            c.AddRange(Motor.Arquivos.PastaClassica.Em(d.FullName));
        }
        return c.FirstOrDefault(p => Achar(p, "countries.tsv") is string t && File.Exists(t)
            || Achar(p, "COUNTRY.TXE") is string a && File.Exists(a));
    }

    // um arquivo sem diferenciar maiusculas (como no Windows)
    static string? Achar(string pasta, string nome)
    {
        if (!Directory.Exists(pasta)) return null;
        foreach (var e in Directory.EnumerateFileSystemEntries(pasta))
            if (string.Equals(Path.GetFileName(e), nome, StringComparison.OrdinalIgnoreCase)) return e;
        return null;
    }

    // ---------------------------------------------------------------- janelas
    void Abrir(Janela j)
    {
        Canvas.SetLeft(j, Math.Floor((tela.Width - j.Width) / 2));    // poScreenCenter
        Canvas.SetTop(j, Math.Floor((tela.Height - j.Height) / 2));
        foreach (var p in pilha) p.Ativa = false;
        pilha.Add(j);
        tela.Children.Add(j);
        Dispatcher.UIThread.Post(j.Focar);
    }

    void Fechar(Janela j)
    {
        pilha.Remove(j);
        tela.Children.Remove(j);
        if (pilha.Count > 0) { pilha[^1].Ativa = true; pilha[^1].Focar(); }
        else Focus();
    }

    // Enter = o botao padrao, Esc = o Cancelar da janela de cima
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || pilha.Count == 0) return;
        var j = pilha[^1];
        if (e.Key == Key.Enter && j.AoEnter != null) { j.AoEnter(); e.Handled = true; }
        else if (e.Key == Key.Escape && j.AoFechar != null) { j.AoFechar(); e.Handled = true; }
    }

    // TInputDlg sem a caixa de texto (como o Mensagem do Go)
    void Mensagem(string titulo, string texto, Action? depois = null)
    {
        if (Celular.Ativo) { MensagemCelular(titulo, texto, depois); return; }
        if (Visual.Moderno) { MensagemModerna(titulo, texto, depois); return; }
        var j = new Janela(titulo, 388, 132, W95.Face);
        j.Por(new Bevel(), 8, 8, 281, 105);
        j.Por(new Rotulo { Texto = texto, Fonte = W95.MsSans11, Cor = W95.Preto }, 16, 24, 265, 82);
        var ok = j.Por(Botao.Ok(W95.MsSans11b, false), 300, 8, 85, 27);
        void Fim() { Fechar(j); depois?.Invoke(); }
        ok.Clique += Fim;
        j.AoEnter = Fim; j.AoFechar = Fim;
        Abrir(j);
    }

    // Sim/Nao no visual da tela (o Sim chama sim)
    void Confirmar(string titulo, string texto, Action sim)
    {
        if (Celular.Ativo) { ConfirmarCelular(titulo, texto, sim); return; }
        if (Visual.Moderno) { ConfirmarModerno(titulo, texto, sim); return; }
        var j = new Janela(titulo, 388, 132, W95.Face);
        j.Por(new Bevel(), 8, 8, 281, 105);
        j.Por(new Rotulo { Texto = texto, Fonte = W95.MsSans11, Cor = W95.Preto }, 16, 24, 265, 82);
        var bs = j.Por(new Botao { Texto = T("Sim"), Fonte = W95.MsSans11b, Glifo = W95.Imagem("Botoes/BBYES.png"), NumGlifos = 2 }, 300, 8, 85, 27);
        var bn = j.Por(new Botao { Texto = T("Não"), Fonte = W95.MsSans11b, Glifo = W95.Imagem("Botoes/BBNO.png"), NumGlifos = 2 }, 300, 40, 85, 27);
        bs.Clique += () => { Fechar(j); sim(); };
        bn.Clique += () => Fechar(j);
        j.AoEnter = () => { Fechar(j); sim(); };
        j.AoFechar = () => Fechar(j);
        Abrir(j);
    }

    // (acrescimo) excluir um jogo gravado: pergunta e manda o .e98 para saves/excluidos (da para
    // recuperar copiando de volta); depois a tela recomeca com a lista nova
    void ExcluirJogo(string nome, string caminho)
    {
        Confirmar(T("Excluir jogo"), T("Excluir o jogo gravado \"{0}\"? O arquivo vai para a pasta excluidos, dentro da pasta dos jogos.", nome), () =>
        {
            try
            {
                var dir = Path.Combine(Path.GetDirectoryName(caminho)!, "excluidos");
                Directory.CreateDirectory(dir);
                var dest = Path.Combine(dir, Path.GetFileName(caminho));
                if (File.Exists(dest)) dest = Path.Combine(dir, Path.GetFileNameWithoutExtension(caminho) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + Path.GetExtension(caminho));
                File.Move(caminho, dest);
                mostrar(new TelaInicio(mostrar));
            }
            catch (Exception ex) { Mensagem(T("Erro"), ex.Message); }
        });
    }

    // (acrescimo, como o "Importar save" do launcher) traz um jogo gravado (.e98) de fora para a
    // pasta dos jogos: so o que o RetroFoot le como save; o nome vira um nome de 8 letras; com o
    // nome de um jogo que ja existe, pergunta antes e guarda o atual como .bak. No celular e o
    // unico caminho (a pasta dos jogos e privada do app)
    async void ImportarJogo()
    {
        var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (sp is not { CanOpen: true }) { Mensagem(T("Importar jogo"), T("Copie o arquivo .e98 para a pasta dos jogos:\n{0}", pastaJogos)); return; }
        IReadOnlyList<IStorageFile> arqs;
        try
        {
            arqs = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = T("Importar jogo (.e98)"),
                AllowMultiple = false,
                // (o Android filtra pelos tipos MIME e nao conhece o .e98: todos os arquivos)
                FileTypeFilter = new[]
                {
                    new FilePickerFileType(T("Jogo gravado (.e98)")) { Patterns = new[] { "*.e98", "*.E98" }, MimeTypes = new[] { "application/octet-stream", "*/*" } },
                    FilePickerFileTypes.All,
                },
            });
        }
        catch (Exception ex) { Mensagem(T("Erro"), ex.Message); return; }
        if (arqs.Count == 0) return;
        byte[] dados;
        try
        {
            await using var st = await arqs[0].OpenReadAsync();
            using var ms = new MemoryStream();
            await st.CopyToAsync(ms);
            dados = ms.ToArray();
            if (Jogo98.Ler(dados).Equipas.Count == 0) throw new InvalidDataException();
        }
        catch { Mensagem(T("Importar jogo"), T("\"{0}\" não é um jogo gravado que o RetroFoot consiga ler.", arqs[0].Name)); return; }
        var nome = NomeDeArquivo(Path.GetFileNameWithoutExtension(arqs[0].Name));
        var atual = Achar(pastaJogos, nome + ".e98");
        void Gravar()
        {
            try
            {
                Directory.CreateDirectory(pastaJogos);
                if (atual != null) File.Copy(atual, Path.ChangeExtension(atual, ".bak"), true);
                File.WriteAllBytes(atual ?? Path.Combine(pastaJogos, nome + ".e98"), dados);
                Mensagem(T("Importar jogo"), T("Jogo \"{0}\" importado.", nome), () => mostrar(new TelaInicio(mostrar)));
            }
            catch (Exception ex) { Mensagem(T("Erro"), ex.Message); }
        }
        if (atual != null) Confirmar(T("Importar jogo"), T("Já existe um jogo \"{0}\". Substituir? O atual fica guardado como {0}.bak.", nome), Gravar);
        else Gravar();
    }

    // (acrescimo, como o "Exportar save" do launcher) uma copia do .e98 onde o jogador quiser
    // (para o launcher, outro aparelho ou backup)
    async void ExportarJogo(string nome, string caminho)
    {
        byte[] dados;
        try { dados = File.ReadAllBytes(caminho); }
        catch (Exception ex) { Mensagem(T("Erro"), ex.Message); return; }
        var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (sp is not { CanSave: true }) { Mensagem(T("Exportar jogo"), T("O jogo gravado está em:\n{0}", caminho)); return; }
        try
        {
            var f = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                // (o Android poe a extensao padrao no nome sugerido: la, sem o .e98)
                Title = T("Exportar jogo"), SuggestedFileName = OperatingSystem.IsAndroid() ? nome : nome + ".e98", DefaultExtension = "e98",
                FileTypeChoices = new[] { new FilePickerFileType(T("Jogo gravado (.e98)")) { Patterns = new[] { "*.e98" }, MimeTypes = new[] { "application/octet-stream" } } },
            });
            if (f == null) return;
            await using (var st = await f.OpenWriteAsync())
            {
                if (st.CanSeek) st.SetLength(0);   // (o do Android nao deixa; o arquivo e novo)
                await st.WriteAsync(dados);
            }
            Mensagem(T("Exportar jogo"), T("Jogo \"{0}\" exportado.", nome));
        }
        catch (Exception ex) { Mensagem(T("Erro"), ex.Message); }
    }

    // um nome de arquivo do DOS (NomeValido) a partir de outro: sem acentos, em maiusculas, so
    // letras, algarismos, _ e -, ate 8; vazio: IMPORT
    static string NomeDeArquivo(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.Normalize(NormalizationForm.FormD).ToUpperInvariant())
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-') { sb.Append(c); if (sb.Length == 8) break; }
        return sb.Length > 0 ? sb.ToString() : "IMPORT";
    }

    // o Cancelar/Esc do comeco volta ao hub (antes fechava o programa)
    void SairDoPrograma() => mostrar(new TelaHub(mostrar));

    // ---------------------------------------------------------------- TselectGameDlg
    // um treinador humano do jogo gravado: o nome, a equipa e a divisao dela ("" fora delas)
    sealed record HumanoGravado(string Treinador, string Equipa, string Divisao)
    {
        public override string ToString() => Divisao.Length > 0 ? $"{Treinador} — {Equipa} ({Divisao})" : $"{Treinador} — {Equipa}";
    }

    // Descricao: o texto do cabecalho (humanos e data); Humanos, Ano e Equipa (o logo) lidos do
    // save. Save ilegivel: so o nome (Humanos vazio, Ano 0).
    sealed record JogoGravado(string Nome, string Caminho, string Descricao, IReadOnlyList<HumanoGravado> Humanos, int Ano,
        RetroFoot.Motor.Save.Equipa? Equipa)
    {
        // as linhas do cabecalho depois dos nomes: a data e a hora
        public string Quando
        {
            get
            {
                var d = Descricao.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return d.Length > 1 ? string.Join(" · ", d.Skip(1)) : "";
            }
        }
    }

    // um save e lido uma vez por tamanho e data de gravacao (a lista volta sem reler)
    static readonly Dictionary<string, (DateTime Data, long Tamanho, JogoGravado Jogo)> cacheJogos = new();

    List<JogoGravado> ListarJogos()
    {
        var r = new List<JogoGravado>();
        if (!Directory.Exists(pastaJogos)) return r;
        foreach (var p in Directory.EnumerateFiles(pastaJogos))
        {
            if (!string.Equals(Path.GetExtension(p), ".e98", StringComparison.OrdinalIgnoreCase)) continue;
            // Run (6:209b): so os arquivos com FileSize > 0
            var fi = new FileInfo(p);
            if (fi.Length == 0) continue;
            lock (cacheJogos)
                if (cacheJogos.TryGetValue(p, out var c) && c.Data == fi.LastWriteTimeUtc && c.Tamanho == fi.Length) { r.Add(c.Jogo); continue; }
            var g = LerJogo(p);
            lock (cacheJogos) cacheJogos[p] = (fi.LastWriteTimeUtc, fi.Length, g);
            r.Add(g);
        }
        r.Sort((a, b) => string.CompareOrdinal(a.Nome, b.Nome));   // Sorted = True
        return r;
    }

    static JogoGravado LerJogo(string p)
    {
        var nome = Path.GetFileNameWithoutExtension(p).ToUpperInvariant();
        Jogo98 s;
        try { s = Jogo98.Ler(File.ReadAllBytes(p)); }
        catch { return new JogoGravado(nome, p, "", Array.Empty<HumanoGravado>(), 0, null); }
        string desc = "";
        try { desc = Descricao(s); } catch { }
        var hs = new List<HumanoGravado>();
        RetroFoot.Motor.Save.Equipa? primeira = null;
        try
        {
            foreach (var t in s.Treinadores.Where(t => t.Humano != 0))
            {
                var e = s.Equipas.FirstOrDefault(q => q.Treinador == t.ID);
                if (e == null) continue;
                primeira ??= e;
                var dv = s.Divisoes.FirstOrDefault(d => d.Equipas.Contains(e.ID));
                hs.Add(new HumanoGravado(TextoSave(t.Nome), TextoSave(e.NomeCompleto), dv == null ? "" : Pascal(dv.Nome)));
            }
        }
        catch { hs.Clear(); }
        return new JogoGravado(nome, p, desc, hs, s.Cabecalho.Ano, primeira);
    }

    static string Pascal(byte[] b) => b.Length == 0 ? "" : Encoding.Latin1.GetString(b, 1, Math.Min(b[0], b.Length - 1));

    // o texto do cabecalho (humanos e data), cifrado duas vezes
    static string Descricao(Jogo98 s)
    {
        var t = s.Cabecalho.Texto;
        if (t.Length == 0) return "";
        var um = Cifra.TextoEFT(t).Texto;
        var dois = Cifra.TextoEFT(new[] { (byte)um.Length }.Concat(um).ToArray()).Texto;
        return Encoding.Latin1.GetString(dois).Replace("\r", "\n");
    }

    // ate 8 letras e algarismos (nome de arquivo do DOS)
    static bool NomeValido(string s) =>
        s.Length is > 0 and <= 8 && s.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-');

    void EscolherJogo()
    {
        if (Celular.Ativo) { EscolherJogoCelular(); return; }
        if (Visual.Moderno) { EscolherJogoModerno(); return; }
        var jogos = ListarJogos();
        bool novo = jogos.Count == 0;
        // (acrescimo) a janela cresce 34 pontos para o Importar e o Exportar, embaixo da lista
        var j = new Janela(TelaAcerca.Nome, 469, 377, W95.Verde);
        j.Por(new Bevel(), 8, 8, 361, 325);
        j.Por(new Rotulo { Texto = T("Nome do arquivo (8 letras):"), Fonte = W95.Arial11b, Cor = W95.Amarelo, Auto = true }, 68, 76, 160, 14);
        j.Por(new Rotulo { Texto = T("Jogos salvos:"), Fonte = W95.Arial11b, Cor = W95.Amarelo, Auto = true }, 68, 164, 100, 14);
        var aviso = j.Por(new Rotulo { Texto = T("Nome do arquivo inválido"), Fonte = W95.Arial11b, Cor = W95.Vermelho, Fundo = W95.Branco, Alin = TextAlignment.Center, IsVisible = false }, 32, 100, 313, 14);
        var desc = j.Por(new Rotulo { Fonte = W95.Arial11, Cor = W95.Amarelo }, 24, 184, 141, 120);
        var quando = j.Por(new Rotulo { Fonte = new Fonte(W95.Arial11.Familia, 9, false), Cor = W95.Amarelo }, 24, 308, 141, 22);
        var ok = j.Por(Botao.Ok(W95.Arial11b, true), 392, 12, 64, 64);
        var cancelar = j.Por(Botao.Cancelar(W95.Arial11b, true), 392, 88, 64, 64);
        // (acrescimo) as Configuracoes do launcher, abaixo do Cancelar
        var configurar = j.Por(new Botao { Texto = T("Configurar"), Fonte = W95.Arial11b, Glifo = W95.Imagem("Botoes/CONFIGURAR.png"), NumGlifos = 2, GlifoEmCima = true }, 392, 164, 64, 64);
        var excluir = j.Por(new Botao { Texto = T("Excluir"), Fonte = W95.Arial11b, Glifo = W95.Imagem("Botoes/EXCLUIR.png"), NumGlifos = 2, GlifoEmCima = true }, 392, 240, 64, 64);
        var importar = j.Por(new Botao { Texto = T("Importar"), Fonte = W95.MsSans11b }, 172, 341, 84, 27);
        var exportar = j.Por(new Botao { Texto = T("Exportar"), Fonte = W95.MsSans11b }, 261, 341, 84, 27);
        var rNovo = j.Por(new Radio { Texto = T("Quero começar um jogo do início"), Fonte = W95.Arial16b }, 24, 36, 329, 17);
        var nome = j.Por(new Edit { MaxLen = 8, Maiusculas = true, Fonte = W95.Arial13b, Cor = W95.Azul }, 236, 72, 109, 21);
        var rCont = j.Por(new Radio { Texto = T("Quero continuar um jogo"), Fonte = W95.Arial16b }, 24, 132, 257, 17);
        var lista = j.Por(new Lista { AlturaItem = 16 }, 172, 160, 173, 165);
        j.Por(new Painel(), 8, 120, 361, 5);
        lista.Quantos = () => jogos.Count;
        lista.Desenhar = Lista.Textos(i => jogos[i].Nome, W95.Arial13b, W95.Azul);
        if (!novo) lista.Sel = 0;

        void Atualizar()
        {
            rNovo.Marcado = novo; rCont.Marcado = !novo;
            // 6:25d6: o radio escolhido fica com a letra amarela, o outro azul-marinho
            rNovo.Cor = novo ? W95.Amarelo : W95.Marinho;
            rCont.Cor = novo ? W95.Marinho : W95.Amarelo;
            nome.Habilitado = novo;
            lista.Habilitado = !novo;
            bool okHab;
            string? av = null;
            if (novo)
            {
                okHab = NomeValido(nome.Texto);
                if (nome.Texto.Length > 0 && !okHab) av = T("Nome do arquivo inválido");
                // (acrescimo) nao apaga um jogo gravado com o mesmo nome
                else if (okHab && Achar(pastaJogos, nome.Texto + ".e98") != null) { av = T("Já existe um jogo com esse nome"); okHab = false; }
            }
            else okHab = lista.Sel >= 0 && lista.Sel < jogos.Count;
            aviso.IsVisible = av != null;
            aviso.Texto = av ?? "";
            ok.Habilitado = okHab;
            excluir.Habilitado = exportar.Habilitado = !novo && lista.Sel >= 0 && lista.Sel < jogos.Count;
            var g = !novo && lista.Sel >= 0 && lista.Sel < jogos.Count ? jogos[lista.Sel] : null;
            // os humanos com a equipa e a divisao, a temporada; a data embaixo, menor
            desc.Texto = g == null ? "" : g.Humanos.Count == 0 ? g.Descricao
                : string.Join("\n", g.Humanos.Select(h => h.Treinador + "\n" + h.Equipa + (h.Divisao.Length > 0 ? ", " + h.Divisao : "")))
                  + "\n\n" + T("Temporada {0}", g.Ano);
            quando.Texto = g == null || g.Humanos.Count == 0 ? "" : g.Quando;
            j.InvalidateTudo();
        }

        void Ok()
        {
            if (!ok.Habilitado) return;
            if (novo)
            {
                var arq = Path.Combine(pastaJogos, nome.Texto + ".e98");
                Fechar(j);
                NovoJogo(arq);
                return;
            }
            var g = jogos[lista.Sel];
            Jogo98 s;
            try { s = Jogo98.Ler(File.ReadAllBytes(g.Caminho)); }
            catch (Exception ex) { Mensagem(T("Erro"), ex.Message); return; }
            Fechar(j);
            Comecar(s, g.Caminho, false);
        }

        rNovo.Clique += () => { novo = true; Atualizar(); nome.Focus(); };
        nome.Clicado += () => { if (!novo) { novo = true; Atualizar(); nome.Focus(); } };
        nome.Mudou += Atualizar;
        rCont.Clique += () =>
        {
            novo = false;
            if (lista.Sel < 0 && jogos.Count > 0) lista.Sel = 0;
            Atualizar();
            lista.Focus();
        };
        lista.Clique += _ => Atualizar();
        lista.Duplo += _ => Ok();
        ok.Clique += Ok;
        cancelar.Clique += SairDoPrograma;
        configurar.Clique += AbrirConfiguracoes;
        excluir.Clique += () => { if (excluir.Habilitado) ExcluirJogo(jogos[lista.Sel].Nome, jogos[lista.Sel].Caminho); };
        importar.Clique += ImportarJogo;
        exportar.Clique += () => { if (exportar.Habilitado) ExportarJogo(jogos[lista.Sel].Nome, jogos[lista.Sel].Caminho); };
        j.AoEnter = Ok;
        j.AoFechar = SairDoPrograma;
        j.Foco = novo ? nome : lista;
        Atualizar();
        Abrir(j);
    }

    // a tela das Configuracoes no lugar desta; ao fechar, volta com a escolha do jogo aberta
    void AbrirConfiguracoes()
    {
        var t = new TelaConfiguracoes(cfg);
        t.Fechou += salvou =>
        {
            // salvou: uma tela nova (o visual ou o formato podem ter mudado)
            if (salvou) mostrar(new TelaInicio(mostrar));
            else mostrar(this);
        };
        mostrar(t);
    }

    // ---------------------------------------------------------------- jogo novo
    // a semente do Randomize do Delphi 1 (a hora do DOS)
    static uint Randomize(DateTime t) =>
        (uint)((t.Second << 8 | t.Millisecond / 10) << 16 | t.Hour << 8 | t.Minute);

    async void NovoJogo(string arquivo)
    {
        if (pastaJogo == null)
        {
            Mensagem(T("Erro"), T("Não achei os dados do jogo (countries.tsv, teams...) em {0}.", DadosJogo.Raiz), EscolherJogo);
            return;
        }
        Dados d;
        SelecaoPaises sel;
        try
        {
            // os arquivos do jogo classico que o jogador deixou na pasta entram agora
            (d, sel) = await Task.Run(() =>
            {
                if (RetroFoot.Motor.Arquivos.PastaDados.EhPastaNova(pastaJogo)) RetroFoot.Motor.Arquivos.PastaDados.ConverterSoltos(pastaJogo);
                var x = Dados.Ler(pastaJogo);
                return (x, x.ContarEquipas());
            });
            // a lista Bosman das Configuracoes (Original ou Liberado), como o launcher
            // deixaria o BOSMAN.TXE antes de o jogo abrir
            var (bosman, _) = cfg.ListasEstrangeiros(pastaJogo);
            if (bosman != null) d.Bosman = d.Paises.Where(p => bosman.Contains(p.Codigo)).ToList();
        }
        catch (Exception ex)
        {
            Mensagem(T("Erro"), T("{0} (pasta do jogo: {1})", ex.Message, pastaJogo), EscolherJogo);
            return;
        }
        SelecionarPaises(d, sel, arquivo);
    }

    // TselectCountriesDlg
    void SelecionarPaises(Dados d, SelecaoPaises sel, string arquivo)
    {
        // o minimo do N das Configuracoes (4N + as que sobem do Distrital)
        sel.Minimo = cfg.RegrasDoJogoNovo.MinimoEquipas;
        if (Celular.Ativo) { SelecionarPaisesCelular(d, sel, arquivo); return; }
        if (Visual.Moderno) { SelecionarPaisesModerno(d, sel, arquivo); return; }
        var j = new Janela(T("Seleção de Países"), 632, 453, W95.Verde);
        j.Por(new Bevel(), 8, 8, 369, 421);
        j.Por(new Rotulo { Texto = T("Times selecionados"), Fonte = W95.Arial13, Cor = W95.Amarelo, Auto = true }, 392, 152, 150, 16);
        var tSelEq = j.Por(new Rotulo { Fonte = W95.Arial16b, Cor = W95.Amarelo, Alin = TextAlignment.Right }, 544, 152, 81, 16);
        j.Por(new Rotulo { Texto = T("Times existentes"), Fonte = W95.Arial13, Cor = W95.Branco, Auto = true }, 392, 128, 150, 16);
        var tExEq = j.Por(new Rotulo { Fonte = W95.Arial16b, Cor = W95.Branco, Alin = TextAlignment.Right }, 543, 128, 81, 16);
        var lSel = j.Por(new Rotulo { Fonte = W95.Arial13, Cor = W95.Branco }, 392, 264, 233, 65);
        var lAviso = j.Por(new Rotulo { Fonte = W95.Arial13b, Cor = W95.Amarelo }, 391, 336, 233, 81);
        j.Por(new Rotulo { Texto = T("Países selecionados"), Fonte = W95.Arial13, Cor = W95.Branco, Auto = true }, 392, 224, 150, 16);
        j.Por(new Rotulo { Texto = T("Países existentes"), Fonte = W95.Arial13, Cor = W95.Branco, Auto = true }, 392, 200, 150, 16);
        var tExPa = j.Por(new Rotulo { Fonte = W95.Arial16b, Cor = W95.Branco, Alin = TextAlignment.Right }, 543, 200, 81, 16);
        var tSelPa = j.Por(new Rotulo { Fonte = W95.Arial16b, Cor = W95.Branco, Alin = TextAlignment.Right }, 544, 224, 81, 16);
        var ok = j.Por(Botao.Ok(W95.Arial13, true), 484, 16, 64, 64);
        var cancelar = j.Por(Botao.Cancelar(W95.Arial13, true), 556, 16, 64, 64);
        var lista = j.Por(new Lista { AlturaItem = 31, SemSelecao = true }, 16, 16, 353, 405);
        var todas = j.Por(new Botao { Texto = T("Todas"), Fonte = W95.Arial13, Glifo = W95.Imagem("Botoes/TODAS.png"), NumGlifos = 2, GlifoEmCima = true }, 392, 16, 64, 64);
        lista.Quantos = () => sel.Lista.Count;
        lista.Desenhar = (dc, i, r, _) => DesenharPais(dc, r, sel.Lista[i]);

        void Atualizar()
        {
            tExEq.Texto = sel.Total.ToString();
            tSelEq.Texto = sel.Escolhidas.ToString();
            tExPa.Texto = sel.Lista.Count.ToString();
            tSelPa.Texto = sel.Escolhidos.ToString();
            lSel.Texto = T("Selecione os países com que quer jogar, de modo a totalizar pelo menos {0} times.", sel.MinimoDeEquipas);
            lAviso.Texto = sel.Escolhidas > SelecaoPaises.MaximoEquipas
                ? T("O jogo só suporta {0} times. Alguns dos times dos países selecionados não farão parte deste jogo.", SelecaoPaises.MaximoEquipas)
                : "";
            ok.Habilitado = sel.Escolhidas >= sel.MinimoDeEquipas;
            // "Todas" vira "Nenhum" quando ja estao todos escolhidos (acrescimo do Go)
            todas.Texto = sel.TodosEscolhidos ? T("Nenhum") : T("Todas");
            j.InvalidateTudo();
        }

        void Ok()
        {
            if (sel.Escolhidas < sel.MinimoDeEquipas) return;
            try { sel.Confirmar(new Aleatorio(Randomize(DateTime.Now))); }
            catch (Exception ex) { Mensagem(T("Erro"), ex.Message); return; }
            Fechar(j);
            EscolherMoeda(d, arquivo);
        }

        void Cancelar() { Fechar(j); EscolherJogo(); }

        lista.Clique += i => { sel.Alternar(sel.Lista[i]); Atualizar(); };
        todas.Clique += () => { if (sel.TodosEscolhidos) sel.Nenhum(); else sel.Todos(); Atualizar(); };
        ok.Clique += Ok;
        cancelar.Clique += Cancelar;
        j.AoEnter = Ok;
        j.AoFechar = Cancelar;
        j.Foco = lista;
        Atualizar();
        Abrir(j);
    }

    readonly Dictionary<string, Bitmap?> bandeiras = new();

    // flags/<codigo>.BMP (ou .png) da pasta de dados (FLAGS na do jogo classico)
    Bitmap? Bandeira(string codigo)
    {
        if (bandeiras.TryGetValue(codigo, out var b)) return b;
        try
        {
            var flags = pastaJogo == null ? null : Achar(pastaJogo, "FLAGS");
            var arq = flags == null ? null : Achar(flags, codigo + ".BMP") ?? Achar(flags, codigo + ".png");
            b = arq == null ? null : new Bitmap(arq);
        }
        catch { b = null; }
        return bandeiras[codigo] = b;
    }

    // 12:0b04 (DrawCountry): Arial 16 preto; escolhido: fundo amarelo e negrito; a bandeira
    // em (esquerda, topo+1) no tamanho do BMP; o nome (ate 20 letras) em esquerda+44,
    // topo+5; as vagas alinhadas a direita em esquerda+254 e " times"/" time" a seguir
    void DesenharPais(DrawingContext dc, Rect r, Pais p)
    {
        var f = p.Selecionado ? W95.Arial16b : W95.Arial16;
        dc.FillRectangle(p.Selecionado ? W95.Amarelo : W95.Branco, r);
        if (Bandeira(p.Codigo) is Bitmap bm)
            dc.DrawImage(bm, new Rect(r.X, r.Y + 1, bm.PixelSize.Width, bm.PixelSize.Height));
        var nome = Encoding.Latin1.GetString(p.Nome);
        if (nome.Length > 20) nome = nome[..20];
        W95.Escreve(dc, nome, f, W95.Preto, r.X + 44, r.Y + 5);
        var n = W95.Txt(p.Vagas.ToString(), f, W95.Preto);
        dc.DrawText(n, new Point(r.X + 254 - n.Width, r.Y + 5));
        W95.Escreve(dc, p.Vagas > 1 ? T(" times") : T(" time"), f, W95.Preto, r.X + 254, r.Y + 5);
    }

    // TcurrencyDlg
    void EscolherMoeda(Dados d, string arquivo)
    {
        if (Celular.Ativo) { EscolherMoedaCelular(d, arquivo); return; }
        if (Visual.Moderno) { EscolherMoedaModerno(d, arquivo); return; }
        var j = new Janela(T("Dinheiro"), 388, 166, W95.Cor(255, 255, 128));   // Color = $80FFFF
        j.Por(new Bevel(), 8, 8, 285, 145);
        j.Por(new Rotulo { Texto = T("Com que moeda vai querer jogar?"), Fonte = W95.Arial13, Cor = W95.Preto, Auto = true }, 20, 40, 250, 16);
        var ok = j.Por(Botao.Ok(W95.Arial13, true), 312, 16, 64, 64);
        var combo = j.Por(new Combo { Fonte = W95.Arial19, AlturaItem = 22 }, 20, 92, 261, 30);
        foreach (int i in Tabelas.MoedasDoJogoNovo) combo.Itens.Add(Tabelas.NomeMoeda(i));
        combo.Indice = 0;
        void Ok() { Fechar(j); EscolherTreinadores(d, Tabelas.MoedasDoJogoNovo[Math.Max(0, combo.Indice)], arquivo); }
        ok.Clique += Ok;
        j.AoEnter = () => { if (combo.Aberto) combo.Fechar(); else Ok(); };
        j.Foco = combo;
        Abrir(j);
    }

    // TgetTeamsDlg: os nomes dos humanos; "Sortear times" cria o jogo e mostra a equipa de
    // cada um; o botao vira "Continuar" e comeca
    void EscolherTreinadores(Dados d, int moeda, string arquivo)
    {
        if (online != null) { CriarOnline(d, moeda, arquivo); return; }
        if (Celular.Ativo) { EscolherTreinadoresCelular(d, moeda, arquivo); return; }
        if (Visual.Moderno) { EscolherTreinadoresModerno(d, moeda, arquivo); return; }
        var j = new Janela(T("Jogadores"), 411, 295, W95.Face);
        j.Por(new Bevel(), 8, 8, 393, 201);
        j.Por(new Rotulo { Texto = T("Nome"), Fonte = W95.MsSans11b, Cor = W95.Preto }, 88, 24, 39, 13);
        j.Por(new Rotulo { Texto = T("Time"), Fonte = W95.MsSans11b, Cor = W95.Preto }, 200, 24, 39, 13);
        var equipas = new Rotulo[6];
        var nomes = new Edit[6];
        for (int i = 0; i < 6; i++)
        {
            j.Por(new Rotulo { Texto = T("Jogador {0}", i + 1), Fonte = W95.MsSans11, Cor = W95.Preto }, 16, 48 + 24 * i, 57, 13);
            equipas[i] = j.Por(new Rotulo { Fonte = W95.MsSans11b, Cor = W95.Preto }, 208, 48 + 24 * i, 180, 17);
        }
        var botao = j.Por(new Botao { Texto = T("Sortear times"), Fonte = W95.MsSans11b }, 149, 224, 113, 64);
        for (int i = 0; i < 6; i++)
            nomes[i] = j.Por(new Edit { MaxLen = 20, Fonte = W95.MsSans11b, Cor = W95.Preto }, 88, 48 + 24 * i, 105, 17);

        Jogo98? criado = null;
        void Clique()
        {
            if (criado != null)
            {
                Fechar(j);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
                    File.WriteAllBytes(arquivo, criado.Gravar());
                }
                catch (Exception ex) { Mensagem(T("Erro"), T("Não consegui gravar o jogo: {0}", ex.Message)); }
                Comecar(criado, arquivo, true);
                return;
            }
            var humanos = nomes.Select(n => n.Texto.Trim()).ToList();
            if (humanos.All(h => h.Length == 0)) return;
            Jogo98 s;
            try { s = CriarJogo(d, moeda, humanos, cfg.RegrasDoJogoNovo); }
            catch (Exception ex) { Mensagem(T("Erro"), ex.Message); return; }
            // a equipa de cada humano, na ordem dos campos
            var hs = s.Treinadores.Where(t => t.Humano != 0).ToList();
            int k = 0;
            for (int i = 0; i < 6; i++)
            {
                if (humanos[i].Length == 0 || k >= hs.Count) continue;
                var e = s.Equipas.FirstOrDefault(q => q.Treinador == hs[k].ID);
                k++;
                if (e == null) continue;
                equipas[i].Texto = TextoSave(e.NomeCurto);
                equipas[i].Fundo = W95.CorTColor(e.CorFundo);
                equipas[i].Cor = W95.CorTColor(e.CorLetra);
            }
            foreach (var n in nomes) n.Habilitado = false;
            botao.Texto = T("Continuar");
            criado = s;
            j.InvalidateTudo();
            j.Focar();
        }
        botao.Clique += Clique;
        j.AoEnter = Clique;
        j.Foco = nomes[0];
        Abrir(j);
    }

    // o jogo novo online: um tecnico humano por jogador da sala (na ordem das vagas), sem a
    // janela dos nomes; cada um recebe o time sorteado para ele
    async void CriarOnline(Dados d, int moeda, string arquivo)
    {
        var o = online!;
        var nomes = o.Jogadores.OrderBy(x => x.Vaga).Select(x => x.Nome).ToList();
        Jogo98 s;
        try
        {
            s = CriarJogo(d, moeda, nomes, cfg.RegrasDoJogoNovo);
            var hs = s.Treinadores.Where(t => t.Humano != 0).ToList();
            var js = o.Jogadores.OrderBy(x => x.Vaga).ToList();
            for (int k = 0; k < js.Count && k < hs.Count; k++)
                if (s.Equipas.FirstOrDefault(q => q.Treinador == hs[k].ID) is { } e) await o.Sala.DarEquipa(js[k].Uid, e.ID);
            Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
            File.WriteAllBytes(arquivo, s.Gravar());
        }
        catch (Exception ex) { Mensagem(T("Erro"), ex.Message, () => mostrar(new TelaHub(mostrar))); return; }
        Comecar(s, arquivo, true);
    }

    // Motor.NovoJogo: o estado do inicio da 1a jornada, o save e a leitura dele (como
    // ui/jogo/novojogo.go). As regras vem das Configuracoes (padrao = jogo original: 8
    // equipas por divisao, regras do 98.003).
    static Jogo98 CriarJogo(Dados d, int moeda, List<string> humanos, TRegras regras)
    {
        var agora = DateTime.Now;
        var g = new Aleatorio(Randomize(agora));
        var op = new NovoOpcoes
        {
            Moeda = moeda,
            Ano = agora.Year,
            Humanos = humanos.Select(ParaLatin1).ToList(),
            Regras = regras,
        };
        // os arbitros da FIFA (e, com um so pais, so os dele); o REFEREE.TXE fica igual. Da
        // pasta de dados (referees.tsv) a lista ja vem junta: so o filtro do pais
        var original = d.Arbitro;
        d.Arbitro = d.ArbitrosProntos ? ListaArbitros.SoDoPaisEscolhido(original, d.Paises)
            : ListaArbitros.ParaJogoNovo(original, ArbitrosFifa(), d.Paises);
        try
        {
            var e = Criacao.Criar(d, op, g);
            e.IniciarTemporada();
            e.PrepararJornada(1, g);
            return Jogo98.Ler(e.Save(Exportar.DataHora(agora)).Gravar());
        }
        finally { d.Arbitro = original; }
    }

    // Assets/arbitros.tsv (pais, nome); sem ele, so os do original
    static List<(string Pais, string Nome)> ArbitrosFifa()
    {
        try
        {
            using var r = new StreamReader(AssetLoader.Open(new Uri("avares://RetroFoot.App/Assets/arbitros.tsv")));
            var linhas = new List<string>();
            for (string? l; (l = r.ReadLine()) != null;) linhas.Add(l);
            return ListaArbitros.LerTsv(linhas);
        }
        catch { return new(); }
    }

    static string ParaLatin1(string s) => new(s.Where(c => c < 256).ToArray());

    static string TextoSave(byte[] b) => b.Length == 0 ? "" : Encoding.Latin1.GetString(Cifra.TextoEFT(b).Texto);

    // "Ana", "Ana e Rui", "Ana, Rui e Zé": o texto do cabecalho quando o jogo e gravado
    static string NomesHumanos(Jogo98 s)
    {
        var n = s.Treinadores.Where(t => t.Humano != 0).Select(t => TextoSave(t.Nome)).ToList();
        return n.Count switch
        {
            0 => "",
            1 => n[0],
            _ => string.Join(", ", n.Take(n.Count - 1)) + " e " + n[^1],
        };
    }

    // ---------------------------------------------------------------- a sessao
    void Comecar(Jogo98 s, string arquivo, bool novo, RetroFoot.Motor.Online.Retrato? retrato = null)
    {
        // N do save; versao, amarelos, patrocinios e estrangeiros das Configuracoes
        // (um save com N != 8 abre sempre como Modificada)
        var op = cfg.OpcoesDoJogo(s, pastaJogo);
        // os patrocinadores: SPONSORS\MARCAS.DAT da pasta do jogo (a opcao vem das
        // Configuracoes; RETROFOOT_PATROC=1 liga para testes)
        op = op with
        {
            Marcas = RetroFoot.Motor.Patrocinio.Marcas.Ler(pastaJogo),
            Patrocinios = op.Patrocinios || Environment.GetEnvironmentVariable("RETROFOOT_PATROC") == "1",
        };
        // as Opcoes (velocidade, sorteio, substituicoes no intervalo...) do arquivo das opcoes
        if (OpcoesDoJogo.Ler() is { } oj) op = oj.Em(op);
        Sessao sessao;
        try
        {
            sessao = new Sessao(s, mostrar, op, arquivo, retrato);
        }
        catch (Exception ex) { Mensagem(T("Erro"), ex.Message, EscolherJogo); return; }
        sessao.J.Treinador = NomesHumanos(s);
        // o save do jogo novo para antes da preparacao das equipas da 1a jornada (3:13e1):
        // os jogos ja existem, mas ninguem foi preparado. Retomar < 0 prepara todas sem
        // criar os jogos de novo (como IniciarMotor do Go).
        if (novo) sessao.J.M.Retomar = -1;
        RetroFoot.Motor.Online.HostOnline? hostOnline = null;
        if (online != null)
        {
            var host = hostOnline = new RetroFoot.Motor.Online.HostOnline(online.Sala, sessao.J, a => Dispatcher.UIThread.Invoke(a));
            sessao.LigarOnline(host);
            Online.Sinal(null);   // (o HostOnline manda o sinal de vida daqui em diante)
            Online.ManterVivo?.Invoke(true);
            sessao.Saiu += () => { host.Dispose(); Online.ManterVivo?.Invoke(false); };
            sessao.Erro += _ => { host.Dispose(); Online.ManterVivo?.Invoke(false); };
        }
        sessao.Saiu += () =>
        {
            if (online == null) { mostrar(new TelaInicio(mostrar)); return; }
            // passou o host: o jogo deste aparelho deixa de ser o oficial (fica de lado) e ele
            // continua na sala como os outros
            if (hostOnline?.Passou == true)
            {
                try { File.Move(arquivo, arquivo + ".passado", true); } catch { }
                mostrar(new TelaSala(mostrar, () => mostrar(new TelaHub(mostrar)), online.Sala, false));
                return;
            }
            // o host saiu do jogo: quem esta na sala espera ele continuar
            _ = online.Sala.Pausar();
            mostrar(new TelaHub(mostrar));
        };
        // o motor parou com erro (na thread dele): volta ao inicio com o aviso, sem travar;
        // o detalhe vai para a saida de erro
        sessao.Erro += ex =>
        {
            Console.Error.WriteLine("motor: " + ex);
            var t = new TelaInicio(mostrar);
            mostrar(t);
            t.Mensagem(T("Erro"), T("O jogo parou por um erro inesperado. Carregue de novo o último jogo salvo para continuar.")
                + "\n\n" + ex.GetType().Name + ": " + ex.Message);
        };
        sessao.Comecar();
    }
}

// ==================================================================== os controles
// Um Windows 3.1/95 pelo Wine do launcher (tema "wine" da versao em Go: borda 3, titulo
// 26 em (50,150,250) com letra preta, face 245, relevo 166/158 por fora e 255/166 por dentro).

sealed record Fonte(FontFamily Familia, double Tamanho, bool Negrito);

static class W95
{
    static readonly FontFamily MsSans = new("avares://RetroFoot.App/Assets/Fontes#MS Sans Serif");
    static readonly FontFamily Arial = new("avares://RetroFoot.App/Assets/Fontes#Liberation Sans");
    public static readonly Fonte MsSans11 = new(MsSans, 11, false), MsSans11b = new(MsSans, 11, true),
        Titulo = new(MsSans, 13, false),
        Arial11 = new(Arial, 11, false), Arial11b = new(Arial, 11, true), Arial13 = new(Arial, 13, false),
        Arial13b = new(Arial, 13, true), Arial16 = new(Arial, 16, false), Arial16b = new(Arial, 16, true),
        Arial19 = new(Arial, 19, false);

    public static IBrush Cor(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));
    static IBrush Cinza(byte v) => Cor(v, v, v);

    public static readonly IBrush Verde = Cor(0, 128, 0), Amarelo = Cor(255, 255, 0), Marinho = Cor(0, 0, 128),
        Azul = Cor(0, 0, 255), Vermelho = Cor(255, 0, 0), Branco = Cinza(255), Preto = Cinza(0),
        Face = Cinza(0xF5), Luz = Cinza(0xFF), Sombra = Cinza(0xA6), Escura = Cinza(0x6A), Luz3D = Cinza(0xE3),
        Moldura = Cinza(0x9E), Cinzento = Cinza(0x80), TituloCor = Cor(50, 150, 250), Destaque = Cor(48, 150, 250),
        Trilho = Cinza(0xE8);

    // TColor do Delphi (0x00BBGGRR, 4 bytes little-endian)
    public static IBrush CorTColor(byte[] c) => c.Length < 3 ? Branco : Cor(c[0], c[1], c[2]);

    public static FormattedText Txt(string s, Fonte f, IBrush cor) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(f.Familia, FontStyle.Normal, f.Negrito ? FontWeight.Bold : FontWeight.Normal), f.Tamanho * Acessibilidade.Texto, cor);

    public static void Escreve(DrawingContext dc, string s, Fonte f, IBrush cor, double x, double y) =>
        dc.DrawText(Txt(s, f, cor), new Point(x, y));

    // texto desabilitado: branco em +1,+1 e cinza por cima
    public static void TextoCinza(DrawingContext dc, string s, Fonte f, double x, double y)
    {
        Escreve(dc, s, f, Luz, x + 1, y + 1);
        Escreve(dc, s, f, Cinzento, x, y);
    }

    public static void Linhas(DrawingContext dc, Rect r, IBrush cimaEsq, IBrush baixoDir)
    {
        dc.FillRectangle(cimaEsq, new Rect(r.X, r.Y, r.Width, 1));
        dc.FillRectangle(cimaEsq, new Rect(r.X, r.Y, 1, r.Height));
        dc.FillRectangle(baixoDir, new Rect(r.X, r.Bottom - 1, r.Width, 1));
        dc.FillRectangle(baixoDir, new Rect(r.Right - 1, r.Y, 1, r.Height));
    }

    // relevo de botao; devolve o interior
    public static Rect Relevo(DrawingContext dc, Rect r, bool afundado)
    {
        if (afundado)
        {
            Linhas(dc, r, Escura, Escura);
            Linhas(dc, r.Deflate(1), Sombra, Face);
        }
        else
        {
            Linhas(dc, r, Sombra, Moldura);
            Linhas(dc, r.Deflate(1), Luz, Sombra);
        }
        return r.Deflate(2);
    }

    // a caixa de TEdit/TListBox/TComboBox com o Ctl3D do Delphi 1 no Wine: anel
    // sombra/realce e por dentro o WS_BORDER (158); devolve o interior
    public static Rect Caixa(DrawingContext dc, Rect r, IBrush fundo)
    {
        dc.FillRectangle(fundo, r);
        Linhas(dc, r, Sombra, Luz);
        Linhas(dc, r.Deflate(1), Moldura, Moldura);
        return r.Deflate(2);
    }

    public static void Seta(DrawingContext dc, double cx, double cy, bool paraBaixo, IBrush cor)
    {
        for (int i = 0; i < 4; i++)
        {
            double y = paraBaixo ? cy - 2 + i : cy + 1 - i;
            dc.FillRectangle(cor, new Rect(cx - 3 + i, y, 7 - 2 * i, 1));
        }
    }

    static readonly Dictionary<string, Bitmap?> imagens = new();

    public static Bitmap? Imagem(string caminho)
    {
        if (imagens.TryGetValue(caminho, out var b)) return b;
        try { b = new Bitmap(AssetLoader.Open(new Uri("avares://RetroFoot.App/Assets/" + caminho))); }
        catch { b = null; }
        return imagens[caminho] = b;
    }
}

// uma janela de dialogo (bsDialog): moldura, titulo e a area cliente com as coordenadas do DFM
sealed class Janela : Canvas
{
    public readonly Canvas Cliente;
    readonly string titulo;
    bool ativa = true;
    public bool Ativa { get => ativa; set { ativa = value; InvalidateTudo(); } }
    public Action? AoEnter, AoFechar;
    public Control? Foco;
    const double B = 3, T = 26;

    public Janela(string titulo, double cw, double ch, IBrush fundo)
    {
        this.titulo = titulo;
        Width = cw + 2 * B;
        Height = ch + T + 2 * B;
        Children.Add(new MolduraJanela(this) { Width = Width, Height = Height });
        Cliente = new Canvas { Width = cw, Height = ch, Background = fundo, ClipToBounds = true };
        SetLeft(Cliente, B);
        SetTop(Cliente, B + T);
        Children.Add(Cliente);
    }

    public C Por<C>(C c, double x, double y, double w, double h) where C : Control
    {
        SetLeft(c, x); SetTop(c, y);
        c.Width = w; c.Height = h;
        Cliente.Children.Add(c);
        return c;
    }

    public void Focar()
    {
        if (Foco != null && Foco.Focusable) Foco.Focus();
        else this.FindAncestorOfType<TelaInicio>()?.Focus();
    }

    public void InvalidateTudo()
    {
        foreach (var c in Children) c.InvalidateVisual();
        foreach (var c in Cliente.Children) c.InvalidateVisual();
    }

    Rect Fechar => new(Width - B - 2 - 16, B + 2, 16, T - 5);

    // a moldura, o titulo e o botao fechar (o Render do Panel e selado)
    sealed class MolduraJanela(Janela j) : Control
    {
        public override void Render(DrawingContext dc) => j.Desenhar(dc);
    }

    void Desenhar(DrawingContext dc)
    {
        var r = new Rect(0, 0, Width, Height);
        dc.FillRectangle(W95.Face, r);
        W95.Linhas(dc, r, W95.Luz3D, W95.Escura);
        W95.Linhas(dc, r.Deflate(1), W95.Luz, W95.Sombra);
        dc.FillRectangle(Ativa ? W95.TituloCor : W95.Cinzento, new Rect(B, B, Width - 2 * B, T - 1));
        var t = W95.Txt(titulo, W95.Titulo, Ativa ? W95.Preto : W95.Cor(0xC8, 0xC8, 0xC8));
        dc.DrawText(t, new Point(B + 2, B + Math.Floor((T - 1 - t.Height) / 2)));
        // o botao fechar
        var f = Fechar;
        dc.FillRectangle(W95.Face, f);
        W95.Linhas(dc, f, W95.Luz3D, W95.Escura);
        W95.Linhas(dc, f.Deflate(1), W95.Luz, W95.Sombra);
        double cx = f.X + f.Width / 2 - 3, cy = f.Y + f.Height / 2 - 3;
        for (int i = 0; i < 6; i++)
        {
            dc.FillRectangle(W95.Preto, new Rect(cx + i, cy + i, 2, 1));
            dc.FillRectangle(W95.Preto, new Rect(cx + 5 - i, cy + i, 2, 1));
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (Ativa && Fechar.Contains(e.GetPosition(this))) AoFechar?.Invoke();
    }
}

// TBevel com Shape = bsFrame
sealed class Bevel : Control
{
    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        W95.Linhas(dc, new Rect(1, 1, w - 1, h - 1), W95.Luz, W95.Luz);
        W95.Linhas(dc, new Rect(0, 0, w - 1, h - 1), W95.Sombra, W95.Sombra);
    }
}

// TPanel com BevelOuter = bvRaised (sem o texto: o Panel1 de 5 px nao o mostra)
sealed class Painel : Control
{
    public override void Render(DrawingContext dc) =>
        W95.Linhas(dc, new Rect(Bounds.Size), W95.Luz, W95.Sombra);
}

// TLabel: sempre com quebra nas palavras; o que passa da altura fica de fora
sealed class Rotulo : Control
{
    public string Texto = "";
    public Fonte Fonte = W95.Arial13;
    public IBrush Cor = W95.Preto;
    public IBrush? Fundo;
    public TextAlignment Alin = TextAlignment.Left;
    public bool Auto;   // AutoSize: uma linha, sem cortar

    public Rotulo() { IsHitTestVisible = false; }

    public override void Render(DrawingContext dc)
    {
        var r = new Rect(Bounds.Size);
        if (Fundo != null) dc.FillRectangle(Fundo, r);
        if (Texto.Length == 0) return;
        var t = W95.Txt(Texto, Fonte, Cor);
        if (!Auto)
        {
            t.MaxTextWidth = r.Width;
            t.TextAlignment = Alin;
        }
        if (Auto) { dc.DrawText(t, new Point(0, 0)); return; }
        using (dc.PushClip(r)) dc.DrawText(t, new Point(0, 0));
    }
}

// TBitBtn / TButton (TButtonGlyph.CalcButtonLayout do Delphi: margem -1, espaco 4)
sealed class Botao : Control
{
    public string Texto = "";
    public Fonte Fonte = W95.Arial13;
    public Bitmap? Glifo;
    public int NumGlifos = 1;
    public bool GlifoEmCima;
    bool habilitado = true;
    bool apertado, preso;
    public event Action? Clique;

    public bool Habilitado { get => habilitado; set { habilitado = value; InvalidateVisual(); } }

    public static Botao Ok(Fonte f, bool emCima) =>
        new() { Texto = T("OK"), Fonte = f, Glifo = W95.Imagem("Botoes/BBOK.png"), NumGlifos = 2, GlifoEmCima = emCima };

    public static Botao Cancelar(Fonte f, bool emCima) =>
        new() { Texto = T("Cancelar"), Fonte = f, Glifo = W95.Imagem("Botoes/BBCANCEL.png"), NumGlifos = 2, GlifoEmCima = emCima };

    public override void Render(DrawingContext dc)
    {
        var tudo = new Rect(Bounds.Size);
        dc.FillRectangle(W95.Face, tudo);
        bool baixo = apertado && habilitado;
        var r = W95.Relevo(dc, tudo, baixo);
        var t = W95.Txt(Texto, Fonte, W95.Preto);
        double gw = 0, gh = 0;
        if (Glifo != null) { gw = Glifo.PixelSize.Width / (double)NumGlifos; gh = Glifo.PixelSize.Height; }
        double tw = Texto.Length > 0 ? t.Width : 0, th = Texto.Length > 0 ? t.Height : 0;
        double esp = Glifo != null && Texto.Length > 0 ? 4 : 0;
        double gx, gy, tx, ty;
        if (GlifoEmCima)
        {
            double margem = Math.Floor((r.Height - (gh + esp + th) + 1) / 2);
            gx = Math.Floor((r.Width - gw + 1) / 2); gy = margem;
            tx = Math.Floor((r.Width - tw + 1) / 2); ty = gy + gh + esp;
        }
        else
        {
            double margem = Math.Floor((r.Width - (gw + esp + tw) + 1) / 2);
            gx = margem; gy = Math.Floor((r.Height - gh + 1) / 2);
            tx = gx + gw + esp; ty = Math.Floor((r.Height - th + 1) / 2);
        }
        double d = baixo ? 1 : 0;
        if (Glifo != null)
        {
            int q = !habilitado && NumGlifos > 1 ? 1 : 0;
            dc.DrawImage(Glifo, new Rect(q * gw, 0, gw, gh), new Rect(r.X + gx + d, r.Y + gy + d, gw, gh));
        }
        if (Texto.Length == 0) return;
        if (habilitado) dc.DrawText(t, new Point(r.X + tx + d, r.Y + ty + d));
        else W95.TextoCinza(dc, Texto, Fonte, r.X + tx, r.Y + ty);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        preso = apertado = true;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!preso) return;
        bool dentro = new Rect(Bounds.Size).Contains(e.GetPosition(this));
        if (dentro != apertado) { apertado = dentro; InvalidateVisual(); }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!preso) return;
        bool clicou = apertado && habilitado;
        preso = apertado = false;
        e.Pointer.Capture(null);
        InvalidateVisual();
        if (clicou) Clique?.Invoke();
    }
}

// TRadioButton (CB_Paint do Wine: o circulo de 13x13 e o texto a 13 + largura("0")/2 + 1)
sealed class Radio : Control
{
    public string Texto = "";
    public Fonte Fonte = W95.Arial13;
    public IBrush Cor = W95.Preto;
    bool marcado;
    public event Action? Clique;

    public bool Marcado { get => marcado; set { marcado = value; InvalidateVisual(); } }

    public override void Render(DrawingContext dc)
    {
        double h = Bounds.Height;
        double y0 = Math.Floor((h - 13) / 2);
        var c = new Point(6.5, y0 + 6.5);
        dc.DrawEllipse(W95.Branco, new Pen(W95.Sombra, 1), c, 6, 6);
        dc.DrawEllipse(null, new Pen(W95.Escura, 1), c, 5, 5);
        if (marcado) dc.DrawEllipse(W95.Preto, null, c, 2.5, 2.5);
        var t = W95.Txt(Texto, Fonte, Cor);
        double x = 13 + Math.Floor(W95.Txt("0", Fonte, Cor).Width / 2) + 1;
        dc.DrawText(t, new Point(x, Math.Floor((h - t.Height) / 2)));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (new Rect(Bounds.Size).Contains(e.GetPosition(this))) Clique?.Invoke();
    }
}

// TEdit: o texto a 1 ponto da borda (como no Wine), cursor, apagar e setas
sealed class Edit : Control
{
    string texto = "";
    int cursor;
    public int MaxLen = 255;
    public bool Maiusculas;
    public Fonte Fonte = W95.Arial13;
    public IBrush Cor = W95.Preto;
    bool habilitado = true;
    public event Action? Mudou, Clicado;

    public Edit()
    {
        Focusable = true;
        FocusAdorner = null;
        Cursor = new Cursor(StandardCursorType.Ibeam);
    }

    public string Texto
    {
        get => texto;
        set { texto = value; cursor = texto.Length; InvalidateVisual(); Mudou?.Invoke(); }
    }

    public bool Habilitado
    {
        get => habilitado;
        set { habilitado = value; Focusable = value; InvalidateVisual(); }
    }

    public override void Render(DrawingContext dc)
    {
        var dentro = W95.Caixa(dc, new Rect(Bounds.Size), W95.Branco);
        using (dc.PushClip(dentro))
        {
            // a 1 ponto da borda; com a letra mais alta que a caixa, centrada
            double x = dentro.X + 1;
            double y = dentro.Y + Math.Min(1, Math.Floor((dentro.Height - W95.Txt("Ag", Fonte, Cor).Height) / 2));
            if (habilitado) W95.Escreve(dc, texto, Fonte, Cor, x, y);
            else W95.Escreve(dc, texto, Fonte, W95.Cinzento, x, y);
            if (IsFocused && habilitado)
            {
                double cx = x + W95.Txt(texto[..cursor], Fonte, Cor).Width;
                dc.FillRectangle(W95.Preto, new Rect(Math.Round(cx), dentro.Y + 1, 1, dentro.Height - 2));
            }
        }
    }

    protected override void OnGotFocus(FocusChangedEventArgs e) { base.OnGotFocus(e); InvalidateVisual(); }
    protected override void OnLostFocus(FocusChangedEventArgs e) { base.OnLostFocus(e); InvalidateVisual(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Clicado?.Invoke();
        if (!habilitado) return;
        Focus();
        double x = e.GetPosition(this).X - 3;
        cursor = texto.Length;
        for (int i = 0; i <= texto.Length; i++)
            if (W95.Txt(texto[..i], Fonte, Cor).Width >= x) { cursor = i; break; }
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!habilitado || string.IsNullOrEmpty(e.Text)) return;
        var s = new StringBuilder();
        foreach (var c0 in e.Text)
        {
            var c = Maiusculas ? char.ToUpperInvariant(c0) : c0;
            if (c < 32 || c > 255) continue;   // so Latin-1, como o jogo
            if (texto.Length + s.Length >= MaxLen) break;
            s.Append(c);
        }
        if (s.Length == 0) return;
        texto = texto.Insert(cursor, s.ToString());
        cursor += s.Length;
        e.Handled = true;
        InvalidateVisual();
        Mudou?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!habilitado) return;
        bool mudou = false;
        switch (e.Key)
        {
            case Key.Back when cursor > 0: texto = texto.Remove(--cursor, 1); mudou = true; break;
            case Key.Delete when cursor < texto.Length: texto = texto.Remove(cursor, 1); mudou = true; break;
            case Key.Left: cursor = Math.Max(0, cursor - 1); break;
            case Key.Right: cursor = Math.Min(texto.Length, cursor + 1); break;
            case Key.Home: cursor = 0; break;
            case Key.End: cursor = texto.Length; break;
            case Key.Back or Key.Delete: break;
            default: return;
        }
        e.Handled = true;
        InvalidateVisual();
        if (mudou) Mudou?.Invoke();
    }
}

// TListBox desenhada pelo dono (ou de textos), com a barra de rolagem do Wine
sealed class Lista : Control
{
    public Func<int> Quantos = () => 0;
    public Action<DrawingContext, int, Rect, bool>? Desenhar;
    public double AlturaItem = 16;
    public bool SemSelecao;
    public IBrush Fundo = W95.Branco;
    int sel = -1, topo;
    bool habilitado = true;
    double? arrastoY;
    int arrastoTopo;
    public event Action<int>? Clique, Duplo;
    const double Barra = 16;

    public Lista() { Focusable = true; FocusAdorner = null; }

    public int Sel { get => sel; set { sel = value; Mostrar(value); InvalidateVisual(); } }
    public bool Habilitado { get => habilitado; set { habilitado = value; InvalidateVisual(); } }

    // a lista de textos: o escolhido em branco sobre o realce
    public static Action<DrawingContext, int, Rect, bool> Textos(Func<int, string> texto, Fonte f, IBrush cor) =>
        (dc, i, r, escolhido) =>
        {
            if (escolhido) dc.FillRectangle(W95.Destaque, r);
            var t = W95.Txt(texto(i), f, escolhido ? W95.Branco : cor);
            dc.DrawText(t, new Point(r.X + 2, r.Y + Math.Floor((r.Height - t.Height) / 2)));
        };

    Rect Dentro => new Rect(Bounds.Size).Deflate(2);
    int Visiveis => Math.Max(1, (int)(Dentro.Height / AlturaItem));
    bool ComBarra => Quantos() > Visiveis;
    int MaxTopo => Math.Max(0, Quantos() - Visiveis);

    void Rolar(int t)
    {
        topo = Math.Clamp(t, 0, MaxTopo);
        InvalidateVisual();
    }

    void Mostrar(int i)
    {
        if (i < 0) return;
        if (i < topo) topo = i;
        else if (i >= topo + Visiveis) topo = i - Visiveis + 1;
        topo = Math.Clamp(topo, 0, MaxTopo);
    }

    (Rect Cima, Rect Baixo, Rect Trilho, Rect Cursor) Partes()
    {
        var d = Dentro;
        var cima = new Rect(d.Right - Barra, d.Y, Barra, Barra);
        var baixo = new Rect(d.Right - Barra, d.Bottom - Barra, Barra, Barra);
        var trilho = new Rect(d.Right - Barra, d.Y + Barra, Barra, Math.Max(0, d.Height - 2 * Barra));
        int n = Quantos(), v = Visiveis;
        double h = Math.Max(8, Math.Floor(trilho.Height * v / Math.Max(1, n)));
        double y = trilho.Y + (MaxTopo == 0 ? 0 : Math.Floor((trilho.Height - h) * topo / MaxTopo));
        return (cima, baixo, trilho, new Rect(trilho.X, y, Barra, h));
    }

    public override void Render(DrawingContext dc)
    {
        var d = W95.Caixa(dc, new Rect(Bounds.Size), Fundo);
        topo = Math.Clamp(topo, 0, MaxTopo);
        bool barra = ComBarra;
        var area = barra ? new Rect(d.X, d.Y, d.Width - Barra, d.Height) : d;
        using (dc.PushClip(area))
        {
            int n = Quantos();
            for (int i = topo; i < n; i++)
            {
                double y = area.Y + (i - topo) * AlturaItem;
                if (y >= area.Bottom) break;
                Desenhar?.Invoke(dc, i, new Rect(area.X, y, area.Width, AlturaItem), !SemSelecao && i == sel);
            }
        }
        if (!barra) return;
        var (cima, baixo, trilho, cur) = Partes();
        dc.FillRectangle(W95.Trilho, trilho);
        foreach (var (r, paraBaixo) in new[] { (cima, false), (baixo, true) })
        {
            dc.FillRectangle(W95.Face, r);
            W95.Relevo(dc, r, false);
            W95.Seta(dc, r.X + 8, r.Y + 8, paraBaixo, W95.Preto);
        }
        dc.FillRectangle(W95.Face, cur);
        W95.Relevo(dc, cur, false);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        if (ComBarra && p.X >= Dentro.Right - Barra)
        {
            var (cima, baixo, _, cur) = Partes();
            if (cima.Contains(p)) Rolar(topo - 1);
            else if (baixo.Contains(p)) Rolar(topo + 1);
            else if (cur.Contains(p)) { arrastoY = p.Y; arrastoTopo = topo; e.Pointer.Capture(this); }
            else Rolar(p.Y < cur.Y ? topo - Visiveis : topo + Visiveis);
            e.Handled = true;
            return;
        }
        if (!habilitado) return;
        Focus();
        int i = topo + (int)Math.Floor((p.Y - Dentro.Y) / AlturaItem);
        if (i < 0 || i >= Quantos()) return;
        sel = i;
        InvalidateVisual();
        e.Handled = true;
        Clique?.Invoke(i);
        if (e.ClickCount == 2) Duplo?.Invoke(i);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (arrastoY is not double y0) return;
        var (_, _, trilho, cur) = Partes();
        double livre = trilho.Height - cur.Height;
        if (livre <= 0) return;
        Rolar(arrastoTopo + (int)Math.Round((e.GetPosition(this).Y - y0) * MaxTopo / livre));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (arrastoY == null) return;
        arrastoY = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Rolar(topo - Math.Sign(e.Delta.Y) * 3);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!habilitado || SemSelecao) return;
        int n = Quantos();
        int novo = e.Key switch
        {
            Key.Up => Math.Max(0, sel - 1),
            Key.Down => Math.Min(n - 1, sel + 1),
            Key.Home => 0,
            Key.End => n - 1,
            _ => -2,
        };
        if (novo == -2 || n == 0) return;
        Sel = novo;
        e.Handled = true;
        Clique?.Invoke(novo);
    }
}

// TComboBox com Style = csDropDownList: a lista abre por baixo, na janela
sealed class Combo : Control
{
    public readonly List<string> Itens = new();
    public Fonte Fonte = W95.Arial13;
    public double AlturaItem = 16;
    int indice = -1;
    Lista? aberta;
    public event Action? Mudou;

    public Combo() { Focusable = true; FocusAdorner = null; }

    public int Indice { get => indice; set { indice = value; InvalidateVisual(); Mudou?.Invoke(); } }
    public bool Aberto => aberta != null;

    public override void Render(DrawingContext dc)
    {
        var d = W95.Caixa(dc, new Rect(Bounds.Size), W95.Branco);
        var b = new Rect(d.Right - 16, d.Y, 16, d.Height);
        dc.FillRectangle(W95.Face, b);
        W95.Relevo(dc, b, false);
        W95.Seta(dc, b.X + 8, b.Y + Math.Floor(b.Height / 2), true, W95.Escura);
        var txt = new Rect(d.X, d.Y, d.Width - 16, d.Height);
        if (IsFocused && !Aberto) dc.FillRectangle(W95.Destaque, txt.Deflate(1));
        if (indice >= 0 && indice < Itens.Count)
        {
            var t = W95.Txt(Itens[indice], Fonte, IsFocused && !Aberto ? W95.Branco : W95.Preto);
            using (dc.PushClip(txt))
                dc.DrawText(t, new Point(d.X + 2, d.Y + Math.Floor((d.Height - t.Height) / 2)));
        }
    }

    protected override void OnGotFocus(FocusChangedEventArgs e) { base.OnGotFocus(e); InvalidateVisual(); }
    protected override void OnLostFocus(FocusChangedEventArgs e) { base.OnLostFocus(e); InvalidateVisual(); }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (aberta?.Parent is Canvas c) c.Children.Remove(aberta);
        aberta = null;
    }

    public void Fechar()
    {
        if (aberta?.Parent is Canvas c) c.Children.Remove(aberta);
        aberta = null;
        InvalidateVisual();
        Focus();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true;
        if (aberta != null) { Fechar(); return; }
        // a lista sai da janela, como no Windows: vai para a tela de baixo de todas
        if (this.FindAncestorOfType<Janela>()?.Parent is not Canvas c) return;
        if (this.TranslatePoint(new Point(0, Bounds.Height), c) is not Point pos) return;
        Focus();
        var l = new Lista { AlturaItem = AlturaItem, Width = Bounds.Width, Height = Itens.Count * AlturaItem + 4, ZIndex = 100 };
        l.Quantos = () => Itens.Count;
        l.Desenhar = Lista.Textos(i => Itens[i], Fonte, W95.Preto);
        l.Sel = indice;
        l.Clique += i => { Indice = i; Fechar(); };
        Canvas.SetLeft(l, pos.X);
        Canvas.SetTop(l, pos.Y);
        c.Children.Add(l);
        aberta = l;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Itens.Count == 0) return;
        switch (e.Key)
        {
            case Key.Up: Indice = Math.Max(0, indice - 1); break;
            case Key.Down: Indice = Math.Min(Itens.Count - 1, indice + 1); break;
            default: return;
        }
        if (aberta != null) aberta.Sel = indice;
        e.Handled = true;
    }
}
