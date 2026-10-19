using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Save;
using CicloOpcoes = RetroFoot.Motor.Ciclo.Opcoes;
using TRegras = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.App;

// As Configuracoes do jogo novo (as do launcher, src-cross/SettingsWindow.cs e
// LauncherConfig.cs), gravadas em JSON em $XDG_CONFIG_HOME/retrofoot/config.json
// (~/.config/... no Linux; a pasta de dados do aplicativo nos outros sistemas). Padrao = o
// jogo original: toda adicao desligada, 8 equipas. Sem registo: o jogo e sempre o da copia
// registada (todos os add-ins); a chave "registrado" dos arquivos antigos e ignorada.
//
// Na versao Original as adicoes (equipas por divisao, amarelos, patrocinios, acrescimos) nao existem: o
// que fica guardado nelas e a escolha da Modificada, que volta quando ela e escolhida de
// novo (como o launcher). Um save com N != 8 abre sempre como Modificada.
public sealed class Configuracoes
{
    public static readonly int[] OpcoesEquipas = { 8, 10, 12, 14, 16, 18, 20 };
    public const string IdiomaPT = "pt-PT", IdiomaBR = "pt-BR", IdiomaEN = "en", IdiomaES = "es";
    public static readonly string[] Idiomas = { IdiomaPT, IdiomaBR, IdiomaEN, IdiomaES };
    public const string Formato43 = "4:3", Formato169 = "16:9";
    // o visual Classico era gravado como "original" (antes do RetroFoot): os dois sao lidos
    public const string VisualClassico = "classico", VisualOriginal = "original", VisualModerno = "moderno";

    public bool Modificada { get; set; }
    public int EquipasPorDivisao { get; set; } = 8;
    public bool CartoesAmarelos { get; set; }
    public bool Patrocinios { get; set; }
    // os acrescimos no fim de cada tempo (Regras.Acrescimos)
    public bool Acrescimos { get; set; }
    // "Sobem e descem por divisao" (Regras.SobemDescemEscolhido): 0 = automatico, 1 a 4
    public int SobemDescem { get; set; }
    // (versao Atual) 5 substituicoes como a regra da FIFA (senao 3) e banco com 9 reservas
    // (senao 5); nas duas, a prorrogacao da Taca da mais uma substituicao
    public bool CincoSubstituicoes { get; set; } = true;
    public bool NoveReservas { get; set; } = true;
    // os lances que aparecem ao lado do placar na jornada (bits de TelaJornada.Mostra*;
    // padrao: todos, como no classico)
    public int LancesNaJornada { get; set; } = 0x1F;
    public string Idioma { get; set; } = IdiomaPT;
    // Liberado: todos os paises do COUNTRY.TXE na lista Bosman (sem limite de estrangeiros)
    public bool EstrangeirosLiberados { get; set; }
    // o formato da janela: 4:3 (padrao, o visual do original) ou 16:9 (telas largas)
    public string FormatoTela { get; set; } = Formato43;
    public bool Largo => FormatoTela == Formato169;
    // o visual das telas: classico (padrao, o aspecto do jogo original) ou moderno (Visual.cs)
    public string VisualTela { get; set; } = VisualClassico;
    public bool Moderno => VisualTela == VisualModerno;

    // o N do proximo jogo novo
    public int N => Modificada ? EquipasPorDivisao : 8;

    // minimo de equipas no jogo novo: 4N + as que sobem do Distrital (seg06:1818), com o K
    // destas Configuracoes
    public int MinimoDeEquipas(int n) => RegrasPara(n).MinimoEquipas;

    // as regras de um jogo com n equipas por divisao: Original so com n = 8 e a versao
    // Original escolhida (e sem forcar a Modificada); senao as do ELIFnn.EXE do launcher com
    // os amarelos, os acrescimos e o K da opcao
    public TRegras RegrasPara(int n, bool forcarModificada = false) =>
        !Modificada && n == 8 && !forcarModificada ? TRegras.Original
            : TRegras.Launcher(n) with { CartoesAmarelos = CartoesAmarelos, DesempateVitorias = true, Acrescimos = Acrescimos, SemEstouro = true, SobemDescemEscolhido = SobemDescem,
                SubstituicoesEscolhidas = CincoSubstituicoes ? 5 : 3, ReservasEscolhidas = NoveReservas ? 9 : 5 };

    // o K que vale no proximo jogo novo (o automatico ou o escolhido, ate o maximo do N)
    public int SobemDescemPara(int n) => RegrasPara(n).SobemDescem;

    // as regras do proximo jogo novo
    public TRegras RegrasDoJogoNovo => RegrasPara(N);

    // as opcoes da Sessao para um save: N do save, o resto daqui
    public CicloOpcoes OpcoesDoJogo(Jogo98 s, string? pastaJogo)
    {
        // um save com N != 8 ou com o K marcado (Regras.SobemDescemDoSave) abre sempre como
        // Modificada; o N e o K vem do save
        var doSave = TRegras.DoSave(s, TRegras.Original);
        bool mod = Modificada || doSave.N != 8 || doSave.SobemDescemEscolhido > 0;
        var regras = TRegras.DoSave(s, RegrasPara(doSave.N, mod));
        var (bosman, plop) = ListasEstrangeiros(pastaJogo);
        return CicloOpcoes.Padrao with
        {
            Regras = regras, Bosman = bosman, PLOP = plop,
            Registrado = true, AddIn13 = true,
            Patrocinios = mod && Patrocinios,
        };
    }

    // ---------------------------------------------------------------- estrangeiros
    // Original: as listas CTRGROUP da pasta do jogo (BOSMAN.TXE e PLOP.TXE); se o launcher
    // deixou o BOSMAN.TXE liberado (todos os paises), a Lei Bosman do 98.003.
    // Liberado: todos os paises do COUNTRY.TXE na lista Bosman.
    public (List<string>? Bosman, List<string>? Plop) ListasEstrangeiros(string? pastaJogo)
    {
        if (pastaJogo == null) return (null, null);
        List<string> bosman, plop, paises;
        try
        {
            (bosman, plop) = Motor.NovoJogo.Dados.ListasDoJogo(pastaJogo);
            paises = CodigosDosPaises(pastaJogo);
        }
        catch { return (null, null); }
        if (EstrangeirosLiberados) bosman = paises.Union(bosman).ToList();
        else if (paises.Count > 0 && paises.All(bosman.Contains)) bosman = Motor.Rodada.Mundo.BosmanOriginal.ToList();
        return (bosman, plop);
    }

    // os codigos de COUNTRY.TXE ("Country" e depois "CCC Nome"), com POR se faltar; na pasta
    // de dados do RetroFoot, os de countries.tsv
    static List<string> CodigosDosPaises(string pastaJogo)
    {
        if (Motor.Arquivos.PastaDados.EhPastaNova(pastaJogo)) return Motor.Arquivos.PastaDados.CodigosDosPaises(pastaJogo);
        var arq = Directory.EnumerateFiles(pastaJogo)
            .FirstOrDefault(f => string.Equals(Path.GetFileName(f), "COUNTRY.TXE", StringComparison.OrdinalIgnoreCase));
        var r = new List<string>();
        if (arq == null) return r;
        var l = Eft.LerTxe(arq);
        if (l.Count == 0 || Encoding.Latin1.GetString(l[0]) != "Country") return r;
        foreach (var t in l.Skip(1))
            if (t.Length >= 3) r.Add(Encoding.Latin1.GetString(t, 0, 3));
        if (!r.Contains("POR")) r.Add("POR");
        return r;
    }

    // ---------------------------------------------------------------- arquivo
    // As pastas do RetroFoot: $XDG_CONFIG_HOME/retrofoot (config.json, opcoes.json) e
    // $XDG_DATA_HOME/retrofoot (os JOGOS do jogar.sh)
    public const string Pasta = "retrofoot";

    public static string RaizConfig
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var raiz = !string.IsNullOrEmpty(xdg) ? xdg : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(raiz))
                raiz = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return raiz;
        }
    }

    public static string RaizDados
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var raiz = !string.IsNullOrEmpty(xdg) ? xdg : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(raiz))
                raiz = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return raiz;
        }
    }

    public static string Arquivo
    {
        get
        {
            // modo portatil (DadosJogo.cs): a config fica dentro da pasta de dados
            if (DadosJogo.Portatil) return Path.Combine(DadosJogo.PastaPortatil, "config.json");
            return Path.Combine(RaizConfig, Pasta, "config.json");
        }
    }

    public Configuracoes Copia() => (Configuracoes)MemberwiseClone();

    // leitura tolerante: arquivo ausente, JSON quebrado, campo faltando ou de outro tipo e
    // valor fora das opcoes ficam no padrao
    public static Configuracoes Ler(string? arquivo = null)
    {
        var c = new Configuracoes();
        try
        {
            arquivo ??= Arquivo;
            if (!File.Exists(arquivo)) return c;
            if (JsonNode.Parse(File.ReadAllText(arquivo), documentOptions: new JsonDocumentOptions
                { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) is not JsonObject o) return c;
            c.Modificada = Texto(o, "versao") is string v ? v == "modificada" : c.Modificada;
            if (Inteiro(o, "equipasPorDivisao") is int n && Array.IndexOf(OpcoesEquipas, n) >= 0) c.EquipasPorDivisao = n;
            c.CartoesAmarelos = Logico(o, "cartoesAmarelos") ?? c.CartoesAmarelos;
            c.Patrocinios = Logico(o, "patrocinios") ?? c.Patrocinios;
            c.Acrescimos = Logico(o, "acrescimos") ?? c.Acrescimos;
            c.CincoSubstituicoes = Logico(o, "cincoSubstituicoes") ?? c.CincoSubstituicoes;
            c.NoveReservas = Logico(o, "noveReservas") ?? c.NoveReservas;
            if (Inteiro(o, "lancesNaJornada") is int lj && lj is >= 0 and <= 0x1F) c.LancesNaJornada = lj;
            if (Inteiro(o, "sobemDescem") is int k && k is >= 0 and <= 4) c.SobemDescem = k;
            if (Texto(o, "idioma") is IdiomaPT or IdiomaBR or IdiomaEN or IdiomaES) c.Idioma = Texto(o, "idioma")!;
            c.EstrangeirosLiberados = Texto(o, "estrangeiros") is string e ? e == "liberado" : c.EstrangeirosLiberados;
            if (Texto(o, "formatoTela") is Formato43 or Formato169) c.FormatoTela = Texto(o, "formatoTela")!;
            if (Texto(o, "visual") is VisualClassico or VisualOriginal) c.VisualTela = VisualClassico;
            else if (Texto(o, "visual") is VisualModerno) c.VisualTela = VisualModerno;
        }
        catch { }
        return c;
    }

    public void Gravar(string? arquivo = null)
    {
        arquivo ??= Arquivo;
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        var o = new JsonObject
        {
            ["versao"] = Modificada ? "modificada" : "original",
            ["equipasPorDivisao"] = EquipasPorDivisao,
            ["cartoesAmarelos"] = CartoesAmarelos,
            ["patrocinios"] = Patrocinios,
            ["acrescimos"] = Acrescimos,
            ["cincoSubstituicoes"] = CincoSubstituicoes,
            ["noveReservas"] = NoveReservas,
            ["lancesNaJornada"] = LancesNaJornada,
            ["sobemDescem"] = SobemDescem,
            ["idioma"] = Idioma,
            ["estrangeiros"] = EstrangeirosLiberados ? "liberado" : "original",
            ["formatoTela"] = FormatoTela,
            ["visual"] = VisualTela,
        };
        var tmp = arquivo + ".tmp";
        File.WriteAllText(tmp, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, arquivo, true);
    }

    static JsonValue? Valor(JsonObject o, string k) =>
        o.FirstOrDefault(p => string.Equals(p.Key, k, StringComparison.OrdinalIgnoreCase)).Value as JsonValue;

    static string? Texto(JsonObject o, string k) =>
        Valor(o, k) is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim().ToLowerInvariant() switch
        {
            "pt-pt" or "pt" => IdiomaPT, "pt-br" => IdiomaBR, "en" or "en-us" or "en-gb" => IdiomaEN, "es" or "es-es" => IdiomaES, var x => x,
        } : null;

    static int? Inteiro(JsonObject o, string k)
    {
        var v = Valor(o, k);
        if (v == null) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d) && d == Math.Floor(d)) return (int)d;
        if (v.TryGetValue<string>(out var s) && int.TryParse(s, out i)) return i;
        return null;
    }

    static bool? Logico(JsonObject o, string k)
    {
        var v = Valor(o, k);
        if (v == null) return null;
        if (v.TryGetValue<bool>(out var b)) return b;
        if (v.TryGetValue<int>(out var i)) return i != 0;
        if (v.TryGetValue<string>(out var s))
            return s.Trim().ToLowerInvariant() switch { "1" or "true" or "sim" or "ligado" => true, "0" or "false" or "nao" or "não" or "desligado" => false, _ => null };
        return null;
    }
}
