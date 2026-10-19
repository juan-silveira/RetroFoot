using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;
using static RetroFoot.App.ConfigEstilo;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// Os dialogos do menu RetroFoot (Opcoes, Sobre, Extras) e do menu Tecnico (Gestao,
// Perfil) e o Convite no visual Moderno: as caixas escuras (CaixaModerna) com as mesmas
// opcoes, a mesma logica e o mesmo teclado dos do original (TelaOpcoes, TelaAcerca,
// TelaTreinadores); so o desenho muda. Ficam no quadro de 800x600 dos dialogos, entao
// servem igual em 4:3 e em 16:9.

// ---------------------------------------------------------------- Opcoes
static class OpcoesModernas
{
    public static void Abrir(Dialogos d, OpcoesDoJogo op, Jogo98? save, Mundo? m, Action<OpcoesDoJogo> ok)
    {
        var c = new CaixaModerna(T("Opções"), 620);
        c.Sub(T("Como o jogo corre e o que ele mostra. Valem a partir de já."));
        var abas = new AbasModernas();
        c.Corpo.Children.Add(abas);

        // Geral
        var chic = Fichas(Math.Clamp((int)op.MostrarChicotadas, 0, 2), T("Nunca"), T("Dos humanos"), T("Todas"));
        var sort = Fichas(Math.Clamp((int)op.VerSorteio, -1, 2), T("Nunca"), T("Quando houver humanos"), T("Sempre"));
        var grav = Fichas(Math.Clamp(op.GravarQuando - 1, -1, 2), T("Todas as rodadas"), T("De 3 em 3 rodadas"), T("No início da temporada"));
        var som = Interruptor(op.Som, T("Sim"), T("Não"));
        var geral = new StackPanel { Spacing = 14 };
        geral.Children.Add(Linha(T("Mostrar demissões de treinadores"), chic));
        geral.Children.Add(Linha(T("Ver sorteio da copa"), sort));
        geral.Children.Add(Linha(T("Salvar o jogo"), grav));
        geral.Children.Add(LinhaInterruptor(T("Habilitar som"), "", som));

        // Jogo
        int nPerfis = OpcoesDoJogo.PerfisDisponiveis();
        // 1 (lento) a 6 (ultrassonico) numa linha; o nome e quanto leva cada partida no tooltip
        var perfis = Enumerable.Range(0, nPerfis).Select(i => ((i + 1).ToString(), (string?)OpcoesDoJogo.DicaPerfil(i))).ToList();
        // ItemIndex = DS:2D3C - 1; fora da lista, o 1o
        int vi = op.Velocidade - 1;
        var vel = new EscolhaModerna(perfis, vi >= 0 && vi < nPerfis ? vi : 0, chips: true);
        int lances = TelaJornada.LancesMostrados;
        var painelLances = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        foreach (var (bit, nome) in TelaJornada.TiposDeLance)
            painelLances.Children.Add(Ficha(T(nome), (lances & bit) != 0, on => lances = on ? lances | bit : lances & ~bit));
        var inter = Interruptor(op.SubstituirNoIntervalo, T("Sim"), T("Não"));
        var pen = Interruptor(op.VerPenaltisCPU, T("Sim"), T("Não"));
        var jogo = new StackPanel { Spacing = 14 };
        jogo.Children.Add(Linha(T("Tempo de jogo"), vel, T("A velocidade do relógio da jornada: de 1 (lento) a 6 (ultrassônico).")));
        jogo.Children.Add(Separador());
        jogo.Children.Add(Linha(T("Mostrar ao lado do placar"), painelLances, T("Os lances que aparecem ao lado de cada partida na jornada.")));
        jogo.Children.Add(Separador());
        jogo.Children.Add(LinhaInterruptor(T("Substituições no intervalo"), T("Parar no intervalo para mexer na equipa."), inter));
        jogo.Children.Add(Separador());
        jogo.Children.Add(LinhaInterruptor(T("Ver as disputas de pênaltis"), T("Também nos jogos sem treinadores humanos."), pen));

        abas.Pagina(T("Geral"), geral);
        abas.Pagina(T("Jogo"), jogo);
        var paginas = new Panel { Children = { geral, jogo } };
        // Controle (add-in 0x5C)
        if (Registo.AddIn(Registo.AddInPaginaControle))
        {
            var ctrl = new StackPanel { Spacing = 14 };
            ctrl.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 12,
                Children = { new TextBlock { Text = T("Nível de inflação"), FontSize = 14.5, Foreground = Texto, VerticalAlignment = VerticalAlignment.Center }, Etiqueta(OpcoesInflacao(m), Amarelo, TemaVerde.DestaqueFundo) },
            });
            var arb = Botao(T("Árbitros"), false, () => { if (save != null) TelaOpcoes.Arbitros(d, save); });
            var emp = Botao(T("Times com empréstimos..."), false, () => { if (save != null) TelaOpcoes.Emprestimos(d, save); });
            var div = Botao(T("Times endividados..."), false, () => { if (save != null) TelaOpcoes.Endividados(d, save); });
            foreach (var b in new[] { arb, emp, div }) b.Habilitado = save != null;
            ctrl.Children.Add(new WrapPanel { ItemSpacing = 10, LineSpacing = 10, Children = { arb, div, emp } });
            abas.Pagina(T("Controle"), ctrl);
            paginas.Children.Add(ctrl);
        }
        c.Corpo.Children.Add(new Border { MinHeight = 250, Child = paginas });

        void Cancelar() => d.Fechar(c);
        void Ok()
        {
            if (vel.Selecionado >= 0) op.Velocidade = (short)(vel.Selecionado + 1);
            TelaJornada.LancesMostrados = lances;
            op.VerSorteio = (short)sort.Selecionado;
            op.GravarQuando = (short)(grav.Selecionado + 1);
            op.SubstituirNoIntervalo = inter.IsChecked == true;
            op.MostrarChicotadas = (short)chic.Selecionado;
            op.VerPenaltisCPU = pen.IsChecked == true;
            op.Som = som.IsChecked == true;
            d.Fechar(c);
            ok(op);
        }
        c.Rodape((T("Cancelar"), false, Cancelar), (T("OK"), true, Ok));
        c.AoEnter = Ok;
        c.AoFechar = Cancelar;
        d.AbrirControle(c);
    }

    static string OpcoesInflacao(Mundo? m) => m == null ? "—" : TelaOpcoes.Inflacao(m);

    internal static EscolhaModerna Fichas(int sel, params string[] itens) =>
        new(itens.Select(i => (i, (string?)null)).ToList(), sel, chips: true);

    // uma ficha liga/desliga (escolha multipla), no desenho das fichas da EscolhaModerna
    static Border Ficha(string texto, bool ligada, Action<bool> mudou)
    {
        var t = new TextBlock { Text = texto, FontSize = 14, FontWeight = FontWeight.Medium };
        var b = new Border { Child = t, CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Padding = new Thickness(16, 6), Cursor = new Cursor(StandardCursorType.Hand) };
        void Pintar()
        {
            b.Background = ligada ? ConfigEstilo.Azul : Brushes.Transparent;
            b.BorderBrush = ligada ? ConfigEstilo.Azul : ConfigEstilo.Linha;
            t.Foreground = ligada ? ConfigEstilo.TextoEscuro : ConfigEstilo.Texto;
        }
        b.PointerPressed += (_, e) => { e.Handled = true; ligada = !ligada; Pintar(); mudou(ligada); };
        Pintar();
        return b;
    }

    internal static Control Linha(string titulo, Control controle, string? ajuda = null)
    {
        var p = new StackPanel { Spacing = 8 };
        p.Children.Add(new TextBlock { Text = titulo, FontSize = 14.5, FontWeight = FontWeight.Medium, Foreground = Texto });
        if (ajuda != null) p.Children.Add(Ajuda(ajuda));
        p.Children.Add(controle);
        return p;
    }
}

// ---------------------------------------------------------------- Sobre, Extras
static class AcercaModerna
{
    public static void Abrir(Dialogos d, Action? fechou)
    {
        var c = new CaixaModerna(T("Sobre"), 600);
        var hero = new StackPanel { Spacing = 4 };
        hero.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 16,
            Children =
            {
                Logotipo.Logo(60),   // a bola e o nome (Assets/Marca)
                Etiqueta(T("Versão {0}", TelaAcerca.Versao), Azul, AzulFundo),
            },
        });
        hero.Children.Add(new TextBlock { Text = TelaAcerca.Slogan, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Texto });
        c.Corpo.Children.Add(hero);

        void Fechar() { d.Fechar(c); fechou?.Invoke(); }
        c.Rodape((T("OK"), true, Fechar));
        c.AoEnter = Fechar;
        c.AoFechar = Fechar;
        d.AbrirControle(c);
    }

    // TAddInsDlg: os add-ins ativos, por nome (Sorted)
    public static void Extras(Dialogos d)
    {
        var nomes = Registo.AddIns.Where(a => Registo.AddIn(a.Id)).Select(a => T(a.Nome))
            .OrderBy(n => n, StringComparer.Ordinal).ToList();
        var c = new CaixaModerna(T("Extras"), 480, rolar: true);
        c.Sub(T("{0} extras ativos", nomes.Count));
        foreach (var n in nomes)
            c.Corpo.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, -4, 0, -4),
                Children = { new TextBlock { Text = "✓", Foreground = Verde, FontWeight = FontWeight.Bold, FontSize = 14 }, new TextBlock { Text = n, FontSize = 14, Foreground = Texto } },
            });
        void Fechar() => d.Fechar(c);
        c.Rodape((T("OK"), true, Fechar));
        c.AoEnter = Fechar;
        c.AoFechar = Fechar;
        d.AbrirControle(c);
    }
}

// ---------------------------------------------------------------- Treinadores
static class TreinadoresModernos
{
    // TcoachManagerDlg (a mesma logica de TelaTreinadores.Gestao)
    public static void Gestao(Dialogos d, Sessao s, Action? fechou)
    {
        var m = s.J.M;
        var humanos = new List<Treinador>();
        int sel = -1;
        void Recolher()
        {
            humanos.Clear();
            humanos.AddRange(s.S.Treinadores.Where(t => t.Humano != 0)
                .OrderBy(t => Dlg.Bytes(t.Nome), Comparer<byte[]>.Create((a, b) => a.AsSpan().SequenceCompareTo(b))));
        }
        Recolher();
        var c = new CaixaModerna(T("Gestão de Treinadores"), 580);
        c.Sub(T("Os treinadores humanos deste jogo (até 6)."));
        var linhas = new StackPanel { Spacing = 6 };
        var ferias = Interruptor(false, T("Sim"), T("Não"));
        var linhaFerias = LinhaInterruptor(T("De férias"), T("O computador cuida da equipa enquanto o treinador está de férias."), ferias);
        var add = Botao(T("Adicionar..."), false, () => { });
        var rem = Botao(T("Remover"), false, () => { });
        var acoes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { add, rem } };
        c.Corpo.Children.Add(new Border { MinHeight = 200, Child = linhas });
        c.Corpo.Children.Add(Separador());
        c.Corpo.Children.Add(linhaFerias);
        c.Corpo.Children.Add(acoes);
        var ok = c.Rodape((T("OK"), true, () => { }))[0];
        bool mudando = false;

        // 11:04a0: os botoes e a caixa conforme o escolhido
        void Atualizar()
        {
            linhas.Children.Clear();
            for (int i = 0; i < humanos.Count; i++)
            {
                int k = i;
                var t = humanos[i];
                var e = s.S.Equipas.FirstOrDefault(q => q.Treinador == t.ID);
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*,Auto") };
                var nomeT = Dlg.Texto(t.Nome);
                g.Children.Add(e != null ? new Escudo(e) { Width = 32, Height = 32 } : new Border
                {
                    Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = AzulFundo,
                    Child = new TextBlock { Text = nomeT.Length > 0 ? nomeT[..1].ToUpperInvariant() : "?", FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Azul, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                });
                var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                txt.Children.Add(new TextBlock { Text = Dlg.Texto(t.Nome), FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Texto });
                txt.Children.Add(new TextBlock { Text = e == null ? T("sem equipa") : P98.NomeEquipa(e), FontSize = 12.5, Foreground = TextoFraco });
                Grid.SetColumn(txt, 2);
                g.Children.Add(txt);
                if (t.B06 != 0)
                {
                    var f = Etiqueta(T("de férias"), Amarelo, TemaVerde.DestaqueFundo);
                    Grid.SetColumn(f, 3);
                    g.Children.Add(f);
                }
                var b = new Border
                {
                    Child = g, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 7), BorderThickness = new Thickness(1),
                    Background = k == sel ? AzulFundo : Brushes.Transparent, BorderBrush = k == sel ? Azul : Linha,
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                b.PointerPressed += (_, a) => { a.Handled = true; sel = k; Atualizar(); };
                linhas.Children.Add(b);
            }
            int deFerias = humanos.Count(t => t.B06 != 0);
            add.Habilitado = humanos.Count < 6;
            mudando = true;
            if (sel >= 0 && sel < humanos.Count)
            {
                var t = humanos[sel];
                rem.Habilitado = humanos.Count > 1;
                linhaFerias.IsVisible = Registo.AddIn(Registo.AddInFerias);
                ferias.IsChecked = t.B06 != 0;
                ferias.IsEnabled = t.B06 != 0 || humanos.Count - 1 > deFerias || Registo.AddIn(Registo.AddInTodosDeFerias);
                ok.Habilitado = deFerias < humanos.Count || Registo.AddIn(Registo.AddInTodosDeFerias);
            }
            else
            {
                rem.Habilitado = false;
                linhaFerias.IsVisible = false;
                ok.Habilitado = humanos.Count > 0;
            }
            mudando = false;
        }
        void Escolher(Treinador? t)
        {
            Recolher();
            sel = t == null ? -1 : humanos.IndexOf(t);
            Atualizar();
        }
        // 11:091c
        ferias.IsCheckedChanged += (_, _) =>
        {
            if (mudando || sel < 0 || sel >= humanos.Count) return;
            var t = humanos[sel];
            MercadoJogo.Ferias(t, ferias.IsChecked == true);
            Escolher(t);
        };
        // 11:06e9
        rem.Clique = () =>
        {
            if (sel < 0 || sel >= humanos.Count) return;
            var t = humanos[sel];
            Perguntar(d, T("Gestão de Treinadores"), T("{0} deixa de jogar?", Dlg.Texto(t.Nome)), () =>
            {
                s.Mercado.RemoverTreinador(t.ID);
                m.Reindexar();
                Escolher(null);
            });
        };
        // 11:09bc: o TCoach e criado antes da pergunta (DS:2722 anda mesmo se cancelar)
        add.Clique = () =>
        {
            ushort id = s.S.Cabecalho.G2722;
            s.S.Cabecalho.G2722++;
            PedirTexto(d, T("Novo treinador"), T("Nome do novo treinador:"), "", 20, nome =>
            {
                var t = new Treinador
                {
                    ID = id, Nome = Cifra.PorTextoEFT(Dlg.Latin1(new string(nome.Where(ch => ch < 256).ToArray()))),
                    Humano = 1, B1A = new byte[] { 0, 0, 0, 1, 0 },
                };
                s.S.Treinadores.Add(t);
                m.Reindexar();
                Escolher(t);
            });
        };
        void Fechar()
        {
            if (!ok.Habilitado) return;
            d.Fechar(c);
            fechou?.Invoke();
        }
        ok.Clique = Fechar;
        c.AoEnter = Fechar;
        Atualizar();
        d.AbrirControle(c);
    }

    // TcoachOptionsDlg: o perfil +0x1A
    public static void Perfil(Dialogos d, Treinador t, Action? ok)
    {
        var c = new CaixaModerna(T("Perfil de {0}", Dlg.Texto(t.Nome)), 580);
        var abas = new AbasModernas();
        c.Corpo.Children.Add(abas);
        var caixas = new[]
        {
            Interruptor(t.B1A[0] != 0, T("Sim"), T("Não")), Interruptor(t.B1A[1] != 0, T("Sim"), T("Não")), Interruptor(t.B1A[2] != 0, T("Sim"), T("Não")),
        };
        var p1 = new StackPanel { Spacing = 14 };
        p1.Children.Add(new TextBlock { Text = T("Quero que o computador cuide dos salários:"), FontSize = 14.5, Foreground = Texto });
        p1.Children.Add(LinhaInterruptor(T("Dos jogadores nacionais"), "", caixas[0]));
        p1.Children.Add(Separador());
        p1.Children.Add(LinhaInterruptor(T("Dos estrangeiros abrangidos pela Lei Bosman"), "", caixas[1]));
        p1.Children.Add(Separador());
        p1.Children.Add(LinhaInterruptor(T("Dos outros jogadores estrangeiros"), "", caixas[2]));
        var grupo = new EscolhaModerna(new (string, string?)[]
        {
            (T("Quero fazer ofertas a todos os jogadores"), null), (T("Não quero fazer ofertas aos jogadores mais fracos"), null),
            (T("Não quero comprar jogadores em leilão"), null),
        }, t.B1A[3]);
        var esconder = Interruptor(t.B1A[4] != 0, T("Sim"), T("Não"));
        var p2 = new StackPanel { Spacing = 14 };
        p2.Children.Add(LinhaInterruptor(T("Esconder as minhas ofertas"), T("Os outros não veem as minhas ofertas nos leilões."), esconder));
        p2.Children.Add(OpcoesModernas.Linha(T("Ofertas"), grupo));
        abas.Pagina(T("Gestão de Salários"), p1);
        abas.Pagina(T("Compra em leilão"), p2);
        c.Corpo.Children.Add(new Border { MinHeight = 250, Child = new Panel { Children = { p1, p2 } } });
        void Cancelar() => d.Fechar(c);
        void Ok()
        {
            for (int i = 0; i < 3; i++) t.B1A[i] = (byte)(caixas[i].IsChecked == true ? 1 : 0);
            t.B1A[3] = (byte)Math.Max(0, grupo.Selecionado);
            t.B1A[4] = (byte)(esconder.IsChecked == true ? 1 : 0);
            d.Fechar(c);
            ok?.Invoke();
        }
        c.Rodape((T("Cancelar"), false, Cancelar), (T("OK"), true, Ok));
        c.AoEnter = Ok;
        c.AoFechar = Cancelar;
        d.AbrirControle(c);
    }

    // TinviteCoachDlg: Sim / Nao (e o "Elenco..." com o add-in 0x0B); so fecha pelos botoes
    public static Control Convite(Treinador t, Equipa e, Action<bool> fim)
    {
        var c = new CaixaModerna(T("Convite"), 460, new Escudo(e) { Width = 56, Height = 56 });
        c.Sub(Dlg.Texto(t.Nome));
        c.Corpo.Children.Add(new TextBlock
        {
            Text = T("Quer vir treinar o {0}?", Dlg.Texto(e.NomeCurto)), FontSize = 18, FontWeight = FontWeight.SemiBold,
            Foreground = Texto, TextWrapping = TextWrapping.Wrap,
        });
        if (P98.NomeEquipa(e) is { Length: > 0 } nome && nome != Dlg.Texto(e.NomeCurto)) c.Corpo.Children.Add(Ajuda(nome, 13));
        bool feito = false;
        void Fim(bool v)
        {
            if (feito) return;
            feito = true;
            fim(v);
        }
        if (Registo.AddIn(Registo.AddInPlantelNoConvite))
            c.RodapeEsquerda(Botao(T("Elenco..."), false, () => TelaTreinadores.AbrirPlantel?.Invoke(e)));
        c.Rodape((T("Não"), false, () => Fim(false)), (T("Sim"), true, () => Fim(true)));
        c.AoEnter = () => Fim(true);
        return c;
    }
}
