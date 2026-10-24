using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Save;
using Mem = RetroFoot.Motor.Partida.Memoria;
using Chicotada = RetroFoot.Motor.Rodada.Chicotada;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// As listas dos menus Campeonato e Tecnico da tela da equipa, todas na TPrintListDlg
// (13:2c32, chamada por 14:0634): o titulo, as opcoes (2 numerada, 4 o duplo clique
// devolve o item, 8 larga, 0x10 o ultimo item escolhido) e a linha de cada item (o
// Formatar da classe do item, com os codigos de Formatado). Chamar na thread da interface,
// com o motor parado (na tela da equipa).
public static class TelaListas
{
    public const int OpNumerada = 2, OpDuplo = 4, OpLarga = 8, OpUltimo = 0x10;

    // compra directa (add-in 0x10): o duplo clique numa transferencia abre o plantel da
    // equipa do jogador, e no calendario o do adversario; quem faz o plantel (TPlantelDlg)
    // liga aqui (equipa, id do jogador ou 0)
    public static Action<Equipa, ushort>? AbrirPlantel;

    // ---------------------------------------------------------------- a TPrintListDlg
    // Executar (13:2c32): a lista cresce (ou encolhe) para caber os itens, sem passar da
    // altura da tela (600); a janela e o OK acompanham. Larga (8): 200 pontos a mais.
    internal static void Lista(Dialogos d, string titulo, int n, Func<int, byte[]> linha, int opcoes,
        Action<int>? duplo = null, Action<DrawingContext, int, Rect, bool>? desenhar = null, Action? fechou = null)
    {
        // o celular: a lista na tela toda, em letra legivel (ListasCelular.cs)
        if (Celular.Ativo) { ListasCelular.Generica(d, titulo, n, linha, opcoes, duplo, fechou); return; }
        bool larga = (opcoes & OpLarga) != 0, numerada = (opcoes & OpNumerada) != 0;
        if (Visual.Moderno) { ListaModernaDlg(d, titulo, n, linha, opcoes, duplo, desenhar, fechou); return; }
        double cw = 374 + (larga ? 200 : 0), ch = 297;
        double lw = 345 + (larga ? 200 : 0), lh = 225;
        // n x ItemHeight + 2 - Height (aqui + 4: a caixa da Lista tem 2 pontos de cada lado)
        double dh = Math.Min(n * 16 + 4 - lh, 600 - (ch + 32));
        lh += dh; ch += dh;
        var j = new Janela(titulo, cw, ch, W95.Face);
        j.Por(new Bevel(), 8, 8, 361 + (larga ? 200 : 0), 241 + dh);
        var ok = j.Por(Botao.Ok(W95.MsSans11b, false), 152 + (larga ? 100 : 0), 260 + dh, 77, 27);
        var l = j.Por(new Lista { AlturaItem = 16 }, 16, 16, lw, lh);
        l.Quantos = () => n;
        byte[] Linha(int i) => numerada ? Numerada(i, linha(i)) : linha(i);
        // com a barra de rolagem, a coluna larga encolhe para o fim da linha nao ficar debaixo dela
        double area = lw - 4 - (n > (int)((lh - 4) / 16) ? 16 : 0);
        double enc = Formatado.Excesso(W95.MsSans11, n, Linha, area);
        l.Desenhar = Encolhida(enc, desenhar ?? ((dc, i, r, sel) =>
        {
            if (sel) dc.FillRectangle(W95.Destaque, r);
            Formatado.Desenhar(dc, W95.MsSans11, sel ? W95.Branco : W95.Preto, Linha(i), r);
        }));
        if ((opcoes & OpUltimo) != 0 && n > 0) l.Sel = n - 1;
        CaixaDialogo? c = null;
        void Fechar() { d.Fechar(c!); fechou?.Invoke(); }
        ok.Clique += Fechar;
        j.AoEnter = Fechar;
        j.AoFechar = Fechar;
        if ((opcoes & OpDuplo) != 0 && duplo != null)
            l.Duplo += i => { d.Fechar(c!); duplo(i); };
        j.Foco = l;
        c = d.Abrir(j);
    }

    // a mesma janela no visual Moderno (ModernoMercado.cs): o cartao verde, a lista com as
    // linhas de 18 pontos (cresce ate caber, sem passar dos 600 do quadro) e o OK amarelo;
    // as mesmas opcoes, o mesmo teclado (Enter e Esc fecham, setas na lista)
    static void ListaModernaDlg(Dialogos d, string titulo, int n, Func<int, byte[]> linha, int opcoes,
        Action<int>? duplo, Action<DrawingContext, int, Rect, bool>? desenhar, Action? fechou)
    {
        bool larga = (opcoes & OpLarga) != 0, numerada = (opcoes & OpNumerada) != 0;
        const double alt = 18;
        double cw = 374 + (larga ? 200 : 0), ch = 297;
        double dh = Math.Min(n * alt + 8 - 241, 600 - (ch + 32));
        ch += dh;
        var j = new Janela(titulo, cw, ch, W95.Face);
        MM.Vestir(j, titulo, true);
        var l = j.Por(new ListaModerna { AlturaItem = alt }, 10, 10, cw - 20, 241 + dh);
        var ok = j.Por(Botao.Ok(W95.MsSans11b, false), (cw - 96) / 2, 260 + dh, 96, 28);
        MM.Cobrir(ok, j.Cliente, padrao: true);
        var fonte = new Fonte(MM.Inter, 11, false);
        l.Quantos = () => n;
        byte[] Linha(int i) => numerada ? Numerada(i, linha(i)) : linha(i);
        // a linha tem 8 pontos de cada lado dentro da area (a barra tem 12)
        double area = cw - 20 - 2 - (n * alt > 241 + dh - 8 ? 12 : 0) - 8;
        double enc = Formatado.Excesso(fonte, n, Linha, area);
        var desenho = Encolhida(enc, desenhar ?? ((dc, i, r, sel) =>
            Formatado.Desenhar(dc, fonte, sel ? MM.TextoEscolhido : MM.Texto, Linha(i), r)));
        // o texto e desenhado no topo da linha: desce o necessario para ficar no meio da faixa
        l.Desenhar = (dc, i, r, sel) =>
        {
            double dy = Math.Max(0, Math.Floor((r.Height - Dlg.Txt("Ág", fonte.Familia, fonte.Tamanho, MM.Texto).Height) / 2));
            using (dc.PushTransform(Matrix.CreateTranslation(0, dy)))
                desenho(dc, i, r, sel);
        };
        if ((opcoes & OpUltimo) != 0 && n > 0) l.Sel = n - 1;
        CaixaDialogo? c = null;
        void Fechar() { d.Fechar(c!); fechou?.Invoke(); }
        ok.Clique += Fechar;
        j.AoEnter = Fechar;
        j.AoFechar = Fechar;
        if ((opcoes & OpDuplo) != 0 && duplo != null)
            l.Duplo += i => { d.Fechar(c!); duplo(i); };
        j.Foco = l;
        c = d.Abrir(j);
    }

    // o numero (a direita em 20) antes da linha (OpNumerada)
    internal static byte[] Numerada(int i, byte[] b)
    {
        var p = new List<byte> { 6, 0x14 };
        p.AddRange(Dlg.Latin1((i + 1).ToString()));
        p.Add(9);
        p.AddRange(b);
        return p.ToArray();
    }

    // o desenho de cada item com a coluna larga encolhida enc pontos (Formatado.Encolher)
    static Action<DrawingContext, int, Rect, bool> Encolhida(double enc, Action<DrawingContext, int, Rect, bool> desenhar) =>
        enc <= 0 ? desenhar : (dc, i, r, sel) =>
        {
            Formatado.Encolher = enc;
            try { desenhar(dc, i, r, sel); }
            finally { Formatado.Encolher = 0; }
        };

    // ---------------------------------------------------------------- Campeonato
    // menuCalendrioClick (4:3b45): as entradas do calendario da equipa (TCalendarEntry),
    // cada uma nas cores do jogo (3:254c fundo, 3:2597 letra): em casa, fundo azul
    // (vermelho na Taca) e letra branca; fora, fundo branco e letra azul (ou vermelha).
    // Com a compra directa (add-in 0x10), o duplo clique abre o plantel do adversario. No
    // fim o original refaz a tela da equipa (4:2bd8): fechou.
    public static void Calendario(Sessao s, Equipa e, Action? fechou = null) => Calendario((Dialogos)s, s, e, fechou);

    public static void Calendario(Dialogos d, Sessao s, Equipa e, Action? fechou = null)
    {
        var cal = e.Calendario.ToList();
        int op = Registo.AddIn(Registo.AddInCompraDirecta) ? OpDuplo : 0;
        if (Celular.Ativo)
        {
            ListasCelular.Calendario(d, s, cal, op != 0 ? i =>
            {
                if (s.Equipa(cal[i].W08) is { } adv) AbrirPlantel?.Invoke(adv, 0);
                fechou?.Invoke();
            } : null, fechou, Dlg.Texto(e.NomeCurto));
            return;
        }
        Lista(d, T("Calendário"), cal.Count, i => LinhaCalendario(s, cal[i]), op, i =>
        {
            if (s.Equipa(cal[i].W08) is { } adv) AbrirPlantel?.Invoke(adv, 0);
            fechou?.Invoke();
        }, Visual.Moderno ? (dc, i, r, sel) =>
        {
            // Moderno: a barra a esquerda marca o jogo em casa (azul claro como o azul do
            // original; vermelha na Taca), a letra da Taca em vermelho claro
            var c = cal[i];
            bool taca = c.B07 == 1;
            if (c.W0E == 0) MM.Cartao_(dc, new Rect(r.X - 6, r.Y - 1, 3, r.Height + 2), taca ? MM.Vermelho : TemaVerde.Azul, 1.5);
            Formatado.Desenhar(dc, new Fonte(MM.Inter, 11, false), sel ? MM.TextoEscolhido : taca ? MM.Vermelho : MM.Texto, LinhaCalendario(s, c, true), r);
            if (s.Equipa(c.W08) is { } adv && Escudos.Escudo(adv) is { } logo)
                MM.Glifo(dc, logo, new Rect(logo.Size), new Rect(r.X + 32, r.Y, 15, 15), true);
        } : (dc, i, r, _) =>
        {
            var c = cal[i];
            var cor = c.B07 == 1 ? W95.Vermelho : W95.Azul;
            var (letra, fundo) = c.W0E == 0 ? (W95.Branco, cor) : (cor, W95.Branco);
            dc.FillRectangle(fundo, r);
            Formatado.Desenhar(dc, W95.MsSans11, letra, LinhaCalendario(s, c, true), r);
            // o logo do adversario (se houver) antes do nome (LinhaCalendario abre o espaco)
            if (s.Equipa(c.W08) is { } adv && Escudos.Escudo(adv) is { } logo)
                using (dc.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality }))
                    dc.DrawImage(logo, new Rect(r.X + 32, r.Y + 1, 14, 14));
        }, fechou);
    }

    // TCalendarEntry.Formatar (3:217b): jornada (a direita em 20), adversario (120), C/F/N
    // e, com resultado: golos : golos, "a. p." / "v. pen." / "d. pen." e o publico (70)
    // (comLogo e o adversario tem logo: 18 pontos para ele antes do nome, tirados do publico)
    static byte[] LinhaCalendario(Sessao s, EntradaCalendario c, bool comLogo = false)
    {
        var b = new List<byte>();
        var adv = s.Equipa(c.W08);
        if (adv == null) return Array.Empty<byte>();
        bool logo = comLogo && Escudos.Escudo(adv) != null;
        Formatado.Col(b, (short)c.W04 > 0 ? c.W04.ToString() : " ", 6, 0x14);
        if (logo) Formatado.Col(b, " ", 8);   // 8 + os 10 do codigo 6 da jornada (que valem dai em diante)
        Formatado.Col(b, Dlg.Bytes(adv.NomeCurto), (byte)'x');
        Formatado.Col(b, Local("CFN"[Math.Min((int)c.W0E, 2)]), 0x0F);
        if ((short)c.W0A < 0) return b.ToArray();
        Formatado.Col(b, ((short)c.W0A).ToString(), 7, 0x0F);
        Formatado.Col(b, ":", 7, 0x0A);
        Formatado.Col(b, ((short)c.W0C).ToString(), 7, 0x0F);
        Formatado.Col(b, c.B06 switch { 1 => T("  a. p."), 2 => T("  v. pen."), 3 => T("  d. pen."), _ => " " }, 0x1E);
        Formatado.Col(b, ((int)c.D10).ToString(), 6, (byte)(logo ? 'F' - 18 : 'F'));
        return b.ToArray();
    }

    // campeonatoMelhoresmarcadoresClick (4:3393) / Melhoresmarcadoresdesempre1Click
    // (4:432d): os melhores marcadores (3:d83a), numerados, com nome, golos e equipa
    public static void Marcadores(Sessao s, bool deSempre) => Marcadores((Dialogos)s, s, deSempre);

    public static void Marcadores(Dialogos d, Sessao s, bool deSempre)
    {
        var ms = MelhoresMarcadores(s, deSempre);
        if (ms.Count == 0) return;
        // com a compra directa (add-in 0x10), o duplo clique abre o plantel do time do jogador
        // (com ele escolhido), como nas ultimas transferencias
        bool compra = Registo.AddIn(Registo.AddInCompraDirecta);
        Action<int>? abrir = compra ? i => AbrirPlantel?.Invoke(ms[i].E, ms[i].P.ID) : null;
        // (RetroFoot) a media de gols por jogo: da carreira (de sempre) ou da temporada
        string Media(int i) => TextosMercado.MediaGols(ms[i].P, !deSempre);
        if (Celular.Ativo) { ListasCelular.Marcadores(d, deSempre ? T("Artilheiros de todos os tempos") : T("Artilheiros desta temporada"), ms, abrir, Media); return; }
        Lista(d, deSempre ? T("Artilheiros de todos os tempos") : T("Artilheiros desta temporada"), ms.Count, i =>
        {
            var (p, e, g) = ms[i];
            var b = new List<byte>();
            Formatado.Col(b, " ", 0);
            Formatado.Col(b, Jornada.NomeNaTela(p), 2, (byte)'x');
            Formatado.Col(b, g.ToString(), 6, 0x14);
            Formatado.Col(b, "(" + Media(i) + ")", 6, 0x34);
            Formatado.Logo(b, e);
            Formatado.Col(b, Dlg.Bytes(e.NomeCurto), 4, 0x8C);
            return b.ToArray();
        }, OpNumerada | (compra ? OpDuplo : 0), abrir);
    }

    // 3:d83a: o maximo M de golos (da epoca, +0x14, ou de sempre, historial) entre todos os
    // jogadores (lista DS:26FE); entram, nessa ordem, os que tem de M ate Max(1, Round(M x
    // 0,7)) golos (de sempre: 0,85); se forem 5 ou menos, desce ate Max(1, Round(M x 0,51)).
    // Depois, mais golos primeiro e, no empate, mais forca (11:18d7, 11:19ca).
    public static List<(Jogador P, Equipa E, int Golos)> MelhoresMarcadores(Sessao s, bool deSempre)
    {
        var todos = new List<(Jogador, Equipa, int)>();
        int max = 0;
        s.J.M.TodosJogadores((e, p) =>
        {
            int g = deSempre ? Mem.Historial(p, Mem.HistGolos) : Mem.Golos14(p);
            todos.Add((p, e, g));
            max = Math.Max(max, g);
        });
        var r = new List<(Jogador P, Equipa E, int Golos)>();
        if (max <= 0) return r;
        int ate = Math.Max(1, (int)Math.Round(max * (deSempre ? 0.85 : 0.7), MidpointRounding.ToEven));
        void Juntar(int de, int a)
        {
            for (int g = de; g >= a; g--)
                foreach (var x in todos)
                    if (x.Item3 == g) r.Add(x);
        }
        Juntar(max, ate);
        if (r.Count <= 5)
        {
            int ate2 = Math.Max(1, (int)Math.Round(max * 0.51, MidpointRounding.ToEven));
            if (ate2 <= ate - 1) Juntar(ate - 1, ate2);
        }
        return r.OrderByDescending(x => x.Golos).ThenByDescending(x => x.P.Forca).ToList();
    }

    // ltimosvencedores1Click (4:4075): a lista DS:270E, larga; TLastWinners.Formatar
    // (3:cbd3): ano (40), campeao (120), treinador (130, negrito se humano), vencedor da
    // Taca e o treinador dele
    public static void UltimosVencedores(Sessao s) => UltimosVencedores((Dialogos)s, s);

    public static void UltimosVencedores(Dialogos d, Sessao s)
    {
        var cs = s.S.Campeoes.ToList();
        if (Celular.Ativo) { ListasCelular.UltimosVencedores(d, s, cs.Select(c => c.W).ToList()); return; }
        Lista(d, T("Últimos vencedores do campeonato e da copa"), cs.Count, i =>
        {
            var w = cs[i].W;
            var b = new List<byte>();
            Formatado.Col(b, w[0].ToString(), (byte)'(');
            void Eq(ushort id)
            {
                Formatado.Logo(b, s.Equipa(id));
                Formatado.Col(b, s.Equipa(id) is { } e ? Dlg.Bytes(e.NomeCurto) : Dlg.Latin1(" "), 4, (byte)'x');
            }
            void Tr(ushort id)
            {
                var t = s.J.M.Treinador(id);
                if (t == null) { Formatado.Col(b, " ", 0x82); return; }
                if (t.Humano != 0) b.Add(2);
                Formatado.Col(b, Dlg.Bytes(t.Nome), 0x82);
            }
            Eq(w[1]); Tr(w[2]); Eq(w[3]); Tr(w[4]);
            return b.ToArray();
        }, OpLarga);
    }

    // ltimastransferncias1Click (4:4269): a lista DS:2712, larga, com o ultimo escolhido;
    // com a compra directa (add-in 0x10), o duplo clique abre o plantel da equipa do jogador
    public static void UltimasTransferencias(Sessao s) => UltimasTransferencias((Dialogos)s, s);

    public static void UltimasTransferencias(Dialogos d, Sessao s)
    {
        var tr = s.S.Transferencias.ToList();
        int op = OpLarga | OpUltimo;
        if (Registo.AddIn(Registo.AddInCompraDirecta)) op |= OpDuplo;
        if (Celular.Ativo)
        {
            ListasCelular.UltimasTransferencias(d, s, tr, (op & OpDuplo) != 0 ? i =>
            {
                if (s.J.M.EquipaDoJogador(tr[i].W06).E is { } e) AbrirPlantel?.Invoke(e, tr[i].W06);
            } : null);
            return;
        }
        Lista(d, T("Últimas transferências"), tr.Count, i => LinhaTransferencia(s, tr[i]), op, i =>
        {
            if (s.J.M.EquipaDoJogador(tr[i].W06).E is { } e) AbrirPlantel?.Invoke(e, tr[i].W06);
        });
    }

    // TLastTransfer.Formatar (3:d0e0): compra directa em vermelho; posicao (15), nome
    // (120), pais (25), forca na hora (20, a direita); o preco (80, a direita) e a equipa
    // compradora (180)
    static byte[] LinhaTransferencia(Sessao s, Transferencia t)
    {
        var b = new List<byte>();
        if (t.B04 != 0) b.Add(0xFC);
        var (eq, k) = s.J.M.EquipaDoJogador(t.W06);
        if (eq != null)
        {
            var p = eq.Jogadores[k];
            Formatado.Col(b, Idioma.Posicao(Math.Min((int)p.Posicao, 3)), 0x0F);
            Formatado.Col(b, Jornada.NomeNaTela(p), (byte)'x');
            Formatado.Col(b, Dlg.Bytes(p.Pais), 0x19);
            Formatado.Col(b, ((short)t.W08).ToString(), 6, 0x14);
        }
        else
        {
            Formatado.Col(b, " ", 0x0F);
            Formatado.Col(b, " ", (byte)'x');
            Formatado.Col(b, " ", 0x19);
            Formatado.Col(b, " ", 0x14);
        }
        Formatado.Col(b, FormatoDinheiro.Valor(s.S, (int)t.D0C), 6, (byte)'P');
        Formatado.Logo(b, s.Equipa(t.W0A));
        Formatado.Col(b, s.Equipa(t.W0A) is { } c ? Dlg.Bytes(c.NomeCurto) : Dlg.Latin1(" "), 0xB4);
        return b.ToArray();
    }

    // ---------------------------------------------------------------- Time e Jogador
    // Historial1Click (4:44cd): os eventos da equipa (+0x61) com o nome completo no titulo, o
    // ultimo escolhido. TTeamEvent.Formatar (3:bb3f): o ano (30); em negrito se o treinador
    // do evento e humano; o titulo (80) e o nome do treinador.
    public static void HistoricoEquipa(Sessao s, Equipa e, Action? fechou = null) => HistoricoEquipa((Dialogos)s, s, e, fechou);

    public static void HistoricoEquipa(Dialogos d, Sessao s, Equipa e, Action? fechou = null)
    {
        var ev = e.Eventos.ToList();
        if (Celular.Ativo) { ListasCelular.HistoricoEquipa(d, s, e, ev, fechou); return; }
        Lista(d, Dlg.Texto(e.NomeCompleto), ev.Count, i =>
        {
            var v = ev[i];
            var b = new List<byte>();
            Formatado.Col(b, ((int)v.D05).ToString(), 0x1E);
            var t = s.J.M.Treinador(v.W09);
            if (t != null && t.Humano != 0) b.Add(2);
            string tit = v.B04 switch { 1 => T("Campeonato"), 2 => T("Copa"), 3 => T("DOBRADINHA"), 4 => T("2º lugar"), 5 => T("3º lugar"), _ => "" };
            if (tit != "") Formatado.Col(b, tit, (byte)'P');
            if (t != null) Formatado.Col(b, Dlg.Bytes(t.Nome), 0);
            return b.ToArray();
        }, OpUltimo, fechou: fechou);
    }

    // EquipaAquisicoes1Click (4:43c5): os jogadores (DS:26FE) com +0x36 = a equipa (as
    // compras que ainda nao chegaram), TPlayer.Formatar com as colunas 0x4063: posicao, nome,
    // equipa, forca e pais
    public static void Contratacoes(Sessao s, Equipa e, Action? fechou = null) => Contratacoes((Dialogos)s, s, e, fechou);

    public static void Contratacoes(Dialogos d, Sessao s, Equipa e, Action? fechou = null)
    {
        var ps = new List<(Jogador P, Equipa E)>();
        s.J.M.TodosJogadores((eq, p) => { if (p.W36 == e.ID) ps.Add((p, eq)); });
        if (Celular.Ativo) { ListasCelular.Contratacoes(d, ps, fechou); return; }
        Lista(d, T("Jogadores adquiridos"), ps.Count, i =>
        {
            var (p, eq) = ps[i];
            var b = new List<byte>();
            Formatado.Col(b, " ", 0);
            Formatado.Col(b, Idioma.Posicao(Math.Min((int)p.Posicao, 3)), 0x0F);
            Formatado.Col(b, Jornada.NomeNaTela(p), 2, (byte)'x');
            Formatado.Logo(b, eq);
            Formatado.Col(b, Dlg.Bytes(eq.NomeCurto), 4, 0x8C);
            Formatado.Col(b, p.Forca.ToString(), 6, 0x14);
            Formatado.Col(b, Dlg.Bytes(p.Pais), 0x19);
            return b.ToArray();
        }, 0, fechou: fechou);
    }

    // ---------------------------------------------------------------- Tecnico
    // treinadorHistoriaClick (4:32b4 -> 3:8c2f): os eventos do treinador (+0x12) com o nome
    // dele no titulo, o ultimo escolhido
    public static void HistoriaTreinador(Sessao s, Treinador? t) => HistoriaTreinador((Dialogos)s, s, t);

    public static void HistoriaTreinador(Dialogos d, Sessao s, Treinador? t)
    {
        if (t == null) return;
        var ev = t.Eventos.ToList();
        if (Celular.Ativo) { ListasCelular.HistoriaTreinador(d, s, t, ev); return; }
        Lista(d, Dlg.Texto(t.Nome), ev.Count, i => LinhaEventoTreinador(s, ev[i]), OpUltimo);
    }

    // TCoachEvent.Formatar (3:b6df): o ano (30) e o texto do tipo; os titulos em negrito
    static byte[] LinhaEventoTreinador(Sessao s, EventoTreinador ev)
    {
        var b = new List<byte>();
        Formatado.Col(b, ((int)ev.D05).ToString(), 0x1E);
        // (Moderno) o logo da equipa do evento no comeco do texto
        if (ev.B04 is 1 or 5 or 6 or 7) Formatado.Logo(b, s.Equipa(ev.W09));
        byte[] Equipa() => s.Equipa(ev.W09) is { } e ? Dlg.Bytes(e.NomeCurto) : Array.Empty<byte>();
        void Txt(string t) => b.AddRange(Dlg.Latin1(t));
        switch (ev.B04)
        {
            case 1: Txt("\0" + T("Contratado pelo ")); b.AddRange(Equipa()); b.Add(9); break;
            case 2: Txt("\0" + T("Despedido")); b.Add(9); break;
            case 3: b.Add(2); Txt("\0" + T("VENCEDOR DO CAMPEONATO") + "\t"); break;
            case 4: b.Add(2); Txt("\0" + T("VENCEDOR DA COPA") + "\t"); break;
            case 5: Txt("\0" + T("Entrada no ")); b.AddRange(Equipa()); b.Add(9); break;
            case 6: Txt("\0" + T("Rebaixado para o ")); b.AddRange(Equipa()); b.Add(9); break;
            case 7: Txt("\0" + T("Promovido para o ")); b.AddRange(Equipa()); b.Add(9); break;
            case 8: b.Add(2); Txt("\0" + T("DOBRADINHA") + "\t"); break;
            case 9: Txt("P" + T("2º lugar") + "\t"); break;
            case 10: Txt("P" + T("3º lugar") + "\t"); break;
        }
        return b.ToArray();
    }

    // 5:025a: a janela "Chicotadas Psicologicas" ("Demissoes de Tecnicos" no pt-BR) com a
    // lista DS:2716 (13:2c32 direto, opcoes 1). Chamar na thread da interface.
    public static void Chicotadas(Dialogos d, Sessao s, IReadOnlyList<Chicotada> l, Action? fechou = null)
    {
        if (Celular.Ativo) { ListasCelular.Chicotadas(d, s, l, fechou); return; }
        Lista(d, T("Demissões de Técnicos"), l.Count, i => LinhaChicotada(s, l[i]), 0, fechou: fechou);
    }

    // TLash.Formatar (3:b2c8): negrito (2) se o treinador e humano; uma coluna so (largura 0):
    // tipo 1 "<treinador> foi despedido do <equipa>"; sem treinador, linha vazia; sem
    // equipa, "Para o seu lugar foi contratado <treinador>"; senao "Troca com <treinador>
    // (<equipa>)"
    static byte[] LinhaChicotada(Sessao s, Chicotada c)
    {
        var b = new List<byte>();
        var t = s.J.M.Treinador(c.Treinador);
        var e = s.Equipa(c.Equipa);
        // (Moderno) o logo da equipa no comeco da linha
        if (e != null && (c.Tipo != 0 || t != null)) Formatado.Logo(b, e);
        if (t != null && t.Humano != 0) b.Add(2);
        b.Add(0);
        byte[] nome = t != null ? Dlg.Bytes(t.Nome) : Array.Empty<byte>();
        byte[] eq = e != null ? Dlg.Bytes(e.NomeCurto) : Array.Empty<byte>();
        void Txt(string x) => b.AddRange(Dlg.Latin1(x));
        if (c.Tipo != 0) { b.AddRange(nome); Txt(T(" foi demitido do ")); b.AddRange(eq); }
        else if (t == null) { }
        else if (e == null) { Txt(T("Para o seu lugar foi contratado ")); b.AddRange(nome); }
        else { Txt(T("Troca com ")); b.AddRange(nome); Txt(" ("); b.AddRange(eq); Txt(")"); }
        b.Add(9);
        return b.ToArray();
    }

    // coachRankingClick (4:3ec1): ordena a lista GLOBAL de treinadores (DS:2702, 11:1d59;
    // fica assim no save) e mostra-a numerada e larga, com nome, equipa e titulos e, com o
    // add-in "Ver pontuacao dos treinadores" (0x51), os pontos
    public static void RankingTreinadores(Sessao s) => RankingTreinadores((Dialogos)s, s);

    public static void RankingTreinadores(Dialogos d, Sessao s)
    {
        MercadoJogo.OrdenarRanking(s.S);
        s.J.M.Reindexar();
        int flags = 0x2B | (Registo.AddIn(Registo.AddInPontuacaoTreinador) ? 4 : 0);
        var tr = s.S.Treinadores.ToList();
        if (Celular.Ativo) { ListasCelular.RankingTreinadores(d, s, tr, (flags & 4) != 0); return; }
        Lista(d, T("Ranking de Treinadores"), tr.Count, i => LinhaTreinador(s, tr[i], flags), OpNumerada | OpLarga);
    }

    // TCoach.Formatar (3:879f), colunas por flags: 0x01 nome (130; negrito se humano),
    // 0x02 equipa (180), 0x20 campeonatos " + " tacas " = " soma, 0x04 pontos (40, a
    // direita), 0x10 "ferias" (80), 0x80000000 o nome nunca em negrito
    internal static byte[] LinhaTreinador(Sessao s, Treinador t, uint flags)
    {
        var b = new List<byte>();
        if ((flags & 1) != 0)
        {
            if (t.Humano != 0 && (flags & 0x80000000) == 0) b.Add(2);
            Formatado.Col(b, Dlg.Bytes(t.Nome), 0x82);
        }
        if ((flags & 2) != 0)
        {
            Formatado.Logo(b, s.J.M.EquipaDoTreinador(t.ID));
            Formatado.Col(b, s.J.M.EquipaDoTreinador(t.ID) is { } e ? Dlg.Bytes(e.NomeCurto) : Dlg.Latin1(" "), 0xB4);
        }
        if ((flags & 0x20) != 0)
        {
            short c = (short)t.D16, ta = (short)(t.D16 >> 16);
            Formatado.Col(b, c + " + ", 6, 0x14);
            Formatado.Col(b, ta + " = ", 6, 0x14);
            Formatado.Col(b, (c + ta).ToString(), 6, 0x14);
        }
        if ((flags & 4) != 0) Formatado.Col(b, ((int)t.D0E).ToString(), 6, (byte)'(');
        if ((flags & 0x10) != 0) Formatado.Col(b, t.B06 != 0 ? T("férias") : " ", (byte)'P');
        return b.ToArray();
    }
    // C/F/N (casa, fora, neutro) do calendario: a letra traduzida (chave "local:C"...) ou a do original
    internal static string Local(char c) { var k = "local:" + c; var t = T(k); return t == k ? c.ToString() : t; }

    internal static byte[] LinhaTreinador(Sessao s, Treinador t, int flags) => LinhaTreinador(s, t, (uint)flags);
}
