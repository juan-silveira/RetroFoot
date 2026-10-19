using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;

using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// As opcoes do jogo do elif98.ini ([Options], seg05:21ea) que a TOptionsDlg mostra; os
// valores sao os das variaveis do original (Velocidade e GravarQuando comecam em 1).
public sealed class OpcoesDoJogo
{
    public short Velocidade = 2;            // DS:2D3C gameTimeProfile (1..Perfis)
    public short MostrarChicotadas = 1;     // DS:2B34 0 nunca, 1 dos humanos, 2 todas
    public short GravarQuando = 1;          // DS:2B3C 1 todas as rodadas, 2 de 3 em 3, 3 no inicio da temporada
    public short VerSorteio = 1;            // DS:2B3A 0 nunca, 1 quando houver humanos, 2 sempre
    public bool Som = true;                 // DS:2C6C
    public bool SubstituirNoIntervalo = true; // DS:2B32
    public bool VerPenaltisCPU;             // DS:2B33

    // os perfis de tempo (11:3f0c -> 11:3d71: nome e tres numeros, em +0x15, +0x19, +0x1D
    // do registro DS:2C55 + 0x21 x k); so os 3 primeiros sem add-ins (5:21f9): 4 com o
    // 0x1E (super-rapidos), 6 com o 0x3D (ultrassonicos)
    public static readonly (string Nome, int A, int B, int C)[] Perfis =
    {
        ("Lento", 100, 1000, 200), ("Médio", 100, 800, 150), ("Rápido", 100, 600, 100),
        ("Super-rápido", 100, 400, 125), ("Rapidíssimo ", 100, 250, 50), ("Ultrassônico", 50, 50, 0),
    };

    // UpdateTimerIntervall (seg10:0ba3): o relogio da jornada anda de A em A ms sem humanos a
    // jogar; com H humanos (contados nos dois lados de cada jogo), de B + (H - 1) x C ms
    public static int Intervalo(int velocidade, int humanos)
    {
        var p = Perfis[Math.Clamp(velocidade, 1, Perfis.Length) - 1];
        return humanos == 0 ? p.A : p.B + (humanos - 1) * p.C;
    }

    public static int Intervalo(int velocidade, RetroFoot.Motor.Partida.Jornada jn)
    {
        int h = 0;
        foreach (var jg in jn.Jogos)
            for (int l = 0; l < 2; l++)
                if (jg.Equipa[l] != null && MercadoJogo.HumanoAtivo(jg.Treinador[l])) h++;
        return Intervalo(velocidade, h);
    }

    // o arquivo das opcoes (o [Options] do elif98.ini), ao lado das Configuracoes:
    // .../retrofoot/opcoes.json. Ler devolve null sem arquivo (ficam as do jogo).
    public static string Arquivo => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Configuracoes.Arquivo)!, "opcoes.json");

    public static OpcoesDoJogo? Ler(string? arquivo = null)
    {
        try
        {
            arquivo ??= Arquivo;
            if (!System.IO.File.Exists(arquivo)) return null;
            var o = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(arquivo)) as System.Text.Json.Nodes.JsonObject;
            if (o == null) return null;
            var r = new OpcoesDoJogo();
            short N(string k, short padrao, int min, int max) =>
                o[k] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<int>(out var i) && i >= min && i <= max ? (short)i : padrao;
            bool B(string k, bool padrao) =>
                o[k] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<bool>(out var b) ? b : padrao;
            r.Velocidade = N("velocidade", r.Velocidade, 1, Perfis.Length);
            r.MostrarChicotadas = N("mostrarChicotadas", r.MostrarChicotadas, 0, 2);
            r.GravarQuando = N("gravarQuando", r.GravarQuando, 1, 3);
            r.VerSorteio = N("verSorteio", r.VerSorteio, 0, 2);
            r.Som = B("som", r.Som);
            r.SubstituirNoIntervalo = B("substituirNoIntervalo", r.SubstituirNoIntervalo);
            r.VerPenaltisCPU = B("verPenaltisCPU", r.VerPenaltisCPU);
            return r;
        }
        catch { return null; }
    }

    public void Gravar(string? arquivo = null)
    {
        arquivo ??= Arquivo;
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(arquivo)!);
        var o = new System.Text.Json.Nodes.JsonObject
        {
            ["velocidade"] = (int)Velocidade, ["mostrarChicotadas"] = (int)MostrarChicotadas,
            ["gravarQuando"] = (int)GravarQuando, ["verSorteio"] = (int)VerSorteio, ["som"] = Som,
            ["substituirNoIntervalo"] = SubstituirNoIntervalo, ["verPenaltisCPU"] = VerPenaltisCPU,
        };
        var tmp = arquivo + ".tmp";
        System.IO.File.WriteAllText(tmp, o.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        System.IO.File.Move(tmp, arquivo, true);
    }

    // o nome do perfil no idioma (chave "velocidade:Médio": "Médio" sozinho e a posicao)
    public static string NomePerfil(int i)
    {
        var n = Perfis[i].Nome.Trim();
        var t = Idioma.T("velocidade:" + n);
        return t.StartsWith("velocidade:", StringComparison.Ordinal) ? n : t;
    }

    // quanto leva uma partida (90 minutos, um tique por minuto) com 1 tecnico humano, para o tooltip
    public static string DicaPerfil(int i)
    {
        int ms = 90 * Intervalo(i + 1, 1);
        int seg = (ms + 500) / 1000;
        string t = seg >= 60 ? seg / 60 + " min" + (seg % 60 != 0 ? " " + seg % 60 + " s" : "") : (ms / 1000.0).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " s";
        return NomePerfil(i) + ": " + Idioma.T("cerca de {0} por partida", t);
    }

    public static int PerfisDisponiveis()
    {
        int n = 3;
        if (Registo.AddIn(Registo.AddInSuperRapidos)) n++;
        if (Registo.AddIn(Registo.AddInUltrassonicos)) n = 6;
        return n;
    }

    public static OpcoesDoJogo De(Opcoes o, bool som = true) => new()
    {
        Velocidade = o.Velocidade, MostrarChicotadas = o.MostrarChicotadas, GravarQuando = o.GravarQuando,
        VerSorteio = o.VerSorteio, Som = som, SubstituirNoIntervalo = o.SubstituirNoIntervalo, VerPenaltisCPU = o.VerPenaltisCPU,
    };

    public Opcoes Em(Opcoes o) => o with
    {
        Velocidade = Velocidade, MostrarChicotadas = MostrarChicotadas, GravarQuando = GravarQuando,
        VerSorteio = VerSorteio, SubstituirNoIntervalo = SubstituirNoIntervalo, VerPenaltisCPU = VerPenaltisCPU,
    };

    // o motor esta parado (na tela da equipa): as opcoes valem a partir de ja
    public void Aplicar(Sessao s)
    {
        s.J.Op = Em(s.J.Op);
        s.J.M.MostrarChicotadas = MostrarChicotadas;
        s.J.M.TacaModo = VerSorteio;
    }
}

// TOptionsDlg (seg06:2cfb, "Opções"): Geral, Jogo e Controle (so com o add-in 0x5C,
// "Página de controle"). O preenchimento e o OK sao os de 6:2ff5. No visual Moderno, a
// mesma caixa no tema escuro (OpcoesModernas, em ModernoConfigDialogos.cs).
public static class TelaOpcoes
{
    // na tela da equipa: as opcoes da Sessao; OK aplica (OpcoesDoJogo.Aplicar) e chama ok
    // (quem grava o elif98.ini / as configuracoes liga ai)
    public static void Abrir(Sessao s, Action<OpcoesDoJogo>? ok = null, bool som = true)
    {
        var op = OpcoesDoJogo.De(s.J.Op, som);
        Abrir(s, op, s.S, s.J.M, o => { o.Aplicar(s); ok?.Invoke(o); });
    }

    // sem jogo (s = null): a pagina Controle fica sem inflacao e sem as listas
    public static void Abrir(Dialogos d, OpcoesDoJogo op, Jogo98? save, Mundo? m, Action<OpcoesDoJogo> ok)
    {
        if (Visual.Moderno) { OpcoesModernas.Abrir(d, op, save, m, ok); return; }
        var j = new Janela(T("Opções"), 508, 282, W95.Face);
        var a11 = W95.Arial11;
        var nb = j.Por(new Notebook { FonteAbas = W95.Arial13, CorAbas = W95.Azul, TopoPagina = 29, AbasPorLinha = 4 }, 0, 0, 508, 282);
        var geral = nb.Pagina(T("Geral"));
        var jogo = nb.Pagina(T("Jogo"));
        Canvas? ctrl = Registo.AddIn(Registo.AddInPaginaControle) ? nb.Pagina(T("Controle")) : null;

        Combo NovoCombo(Canvas p, double x, double y, double w, int indice, params string[] itens)
        {
            var c = nb.Por(p, new Combo { Fonte = a11, AlturaItem = 14 }, x, y, w, 22);
            c.Itens.AddRange(itens);
            c.Indice = indice;
            return c;
        }
        // Geral
        nb.Por(geral, new Rotulo { Texto = T("Mostrar demissões de treinadores"), Fonte = a11, Auto = true }, 36, 40, 152, 14);
        nb.Por(geral, new Rotulo { Texto = T("Salvar o jogo"), Fonte = a11, Auto = true }, 36, 144, 66, 14);
        nb.Por(geral, new Rotulo { Texto = T("Ver sorteio da copa"), Fonte = a11, Auto = true }, 36, 92, 121, 14);
        nb.Por(geral, new Rotulo { Texto = T("Habilitar som"), Fonte = a11, Auto = true }, 36, 196, 61, 14);
        var chic = NovoCombo(geral, 244, 36, 105, Math.Clamp((int)op.MostrarChicotadas, 0, 2), T("Nunca"), T("Dos humanos"), T("Todas"));
        var grav = NovoCombo(geral, 208, 144, 140, Math.Clamp(op.GravarQuando - 1, -1, 2), T("Todas as rodadas"), T("De 3 em 3 rodadas"), T("No início da temporada"));
        var sort = NovoCombo(geral, 208, 88, 140, Math.Clamp((int)op.VerSorteio, -1, 2), T("Nunca"), T("Quando houver humanos"), T("Sempre"));
        var som = NovoCombo(geral, 244, 196, 105, op.Som ? 0 : 1, T("Sim"), T("Não"));
        // Jogo
        nb.Por(jogo, new Rotulo { Texto = T("Tempo de jogo"), Fonte = a11, Auto = true }, 36, 140, 70, 14);
        nb.Por(jogo, new Rotulo { Texto = T("Substituições no intervalo"), Fonte = a11, Auto = true }, 36, 40, 124, 14);
        nb.Por(jogo, new Rotulo { Texto = T("Ver as disputas de pênaltis nos jogos sem treinadores humanos"), Fonte = a11 }, 36, 84, 177, 29);
        int nPerfis = OpcoesDoJogo.PerfisDisponiveis();
        var nomes = new string[nPerfis];
        for (int i = 0; i < nPerfis; i++) nomes[i] = OpcoesDoJogo.NomePerfil(i);
        // ItemIndex = DS:2D3C - 1; fora da lista, o 1o
        int vi = op.Velocidade - 1;
        var vel = NovoCombo(jogo, 184, 140, 169, vi >= 0 && vi < nPerfis ? vi : 0, nomes);
        var inter = NovoCombo(jogo, 244, 36, 105, op.SubstituirNoIntervalo ? 1 : 0, T("Não"), T("Sim"));
        var pen = NovoCombo(jogo, 244, 88, 105, op.VerPenaltisCPU ? 1 : 0, T("Não"), T("Sim"));
        // os lances mostrados ao lado do placar na jornada
        nb.Por(jogo, new Rotulo { Texto = T("Mostrar ao lado do placar"), Fonte = a11, Auto = true }, 36, 182, 160, 14);
        var lances = new System.Collections.Generic.List<(int Bit, Marca M)>();
        for (int i = 0; i < TelaJornada.TiposDeLance.Length; i++)
        {
            var (bit, nome) = TelaJornada.TiposDeLance[i];
            var mk = nb.Por(jogo, new Marca { Texto = T(nome) }, 36 + i % 3 * 116, 202 + i / 3 * 20, 112, 17);
            mk.Marcado = (TelaJornada.LancesMostrados & bit) != 0;
            lances.Add((bit, mk));
        }
        // Controle
        if (ctrl != null)
        {
            nb.Por(ctrl, new Rotulo { Texto = T("Nível de inflação"), Fonte = W95.Arial13, Auto = true }, 256, 184, 100, 16);
            // Round(DS:2B46 x 10)
            string infl = Inflacao(m);
            nb.Por(ctrl, new Rotulo { Texto = infl, Fonte = W95.Arial16b, Auto = true }, 376, 184, 130, 19);
            var arb = nb.Por(ctrl, new Botao { Texto = T("Árbitros"), Fonte = W95.Arial13 }, 16, 64, 193, 33);
            var emp = nb.Por(ctrl, new Botao { Texto = T("Times com empréstimos..."), Fonte = W95.Arial13 }, 16, 176, 193, 33);
            var div = nb.Por(ctrl, new Botao { Texto = T("Times endividados..."), Fonte = W95.Arial13 }, 16, 136, 193, 33);
            if (save != null)
            {
                arb.Clique += () => Arbitros(d, save);
                emp.Clique += () => Emprestimos(d, save);
                div.Clique += () => Endividados(d, save);
            }
        }
        var bok = j.Por(Dlg.BotaoGrande(T("OK"), "Botoes/BBOK.png"), 416, 62, 64, 64);
        var bc = j.Por(Dlg.BotaoGrande(T("Cancelar"), "Botoes/BBCANCEL.png"), 416, 130, 64, 64);
        CaixaDialogo? c = null;
        void Cancelar() => d.Fechar(c!);
        void Ok()
        {
            if (vel.Indice >= 0) op.Velocidade = (short)(vel.Indice + 1);
            op.VerSorteio = (short)sort.Indice;
            op.GravarQuando = (short)(grav.Indice + 1);
            op.SubstituirNoIntervalo = inter.Indice == 1;
            op.MostrarChicotadas = (short)chic.Indice;
            op.VerPenaltisCPU = pen.Indice == 1;
            op.Som = som.Indice == 0;
            int ml = 0;
            foreach (var (bit, mk) in lances) if (mk.Marcado) ml |= bit;
            TelaJornada.LancesMostrados = ml;
            d.Fechar(c!);
            ok(op);
        }
        bok.Clique += Ok;
        bc.Clique += Cancelar;
        j.AoEnter = Ok;
        j.AoFechar = Cancelar;
        c = d.Abrir(j);
    }

    // Round(DS:2B46 x 10)
    internal static string Inflacao(Mundo? m) => m != null ? ((long)m.Inflacao.Mul(RetroFoot.Motor.Base.Ext.F64(10)).Round()).ToString() : "";

    // Button1Click (6:3344): DS:2706 numerada, TReferee.Formatar (3:c87f): o nome e
    // " (" pais ")"
    internal static void Arbitros(Dialogos d, Jogo98 save) => TelaListas.Lista(d, T("Árbitros"), save.Arbitros.Count, i =>
    {
        var a = save.Arbitros[i];
        var b = new System.Collections.Generic.List<byte> { 0 };
        b.AddRange(Dlg.Bytes(a.Nome));
        var p = Dlg.Texto(a.Pais);
        if (p.Length > 0) b.AddRange(Dlg.Latin1(" (" + p.ToUpperInvariant() + ")"));
        return b.ToArray();
    }, TelaListas.OpNumerada);

    // 6:3389 (emprestimo +0x6D > 0) e 6:34ae (dinheiro +0x71 < 0), na ordem da lista
    internal static void Emprestimos(Dialogos d, Jogo98 save) => Equipas(d, save, T("Times com empréstimos"), e => (int)e.D6D > 0, e => (int)e.D6D);
    internal static void Endividados(Dialogos d, Jogo98 save) => Equipas(d, save, T("Times endividados"), e => (int)e.Dinheiro < 0, e => (int)e.Dinheiro);

    static void Equipas(Dialogos d, Jogo98 save, string titulo, Func<Equipa, bool> filtro, Func<Equipa, int> valor)
    {
        var l = save.Equipas.FindAll(e => filtro(e));
        TelaListas.Lista(d, titulo, l.Count, i =>
        {
            var b = new System.Collections.Generic.List<byte>();
            Formatado.Col(b, Dlg.Bytes(l[i].NomeCurto), (byte)'x');
            Formatado.Col(b, valor(l[i]).ToString(), 6, (byte)'P');
            return b.ToArray();
        }, 0);
    }
}
