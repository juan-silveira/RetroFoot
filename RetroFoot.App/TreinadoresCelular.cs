using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Save;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// O menu Tecnico no celular (Celular.cs), nas folhas do kit (CelularUi.cs): a gestao dos
// treinadores humanos (TcoachManagerDlg), o perfil (TcoachOptionsDlg) e o convite
// (TinviteCoachDlg). A mesma logica de TelaTreinadores / TreinadoresModernos (os botoes ligam e
// desligam pelas mesmas regras); so o desenho muda.
static class TreinadoresCelular
{
    // ---------------------------------------------------------------- perguntas
    // o Sim / Nao numa folha (o MessageBox): Nao e o voltar
    internal static void Perguntar(Dialogos d, string titulo, string texto, Action sim, Action? nao = null)
    {
        CUi.Folha? f = null;
        bool respondeu = false;
        void Responder(bool s)
        {
            if (respondeu) return;
            respondeu = true;
            d.Fechar(f!);
            if (s) sim(); else nao?.Invoke();
        }
        f = CUi.Pergunta(titulo, texto, new (string, bool, Action)[] { (T("Não"), false, () => Responder(false)), (T("Sim"), true, () => Responder(true)) }, () => Responder(false));
        d.Abrir(f);
    }

    // a TInputDlg numa folha: o rotulo, a caixa (o teclado do telefone), Cancelar e OK
    internal static void PedirTexto(Dialogos d, string titulo, string rotulo, string inicial, int maximo, Action<string> ok)
    {
        CUi.Folha? f = null;
        bool fechou = false;
        var caixa = CUi.Caixa(inicial, maximo);
        void Ok() { if (fechou) return; fechou = true; d.Fechar(f!); ok(caixa.Text ?? ""); }
        void Cancelar() { if (fechou) return; fechou = true; d.Fechar(f!); }
        caixa.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Ok(); } };
        var corpo = new StackPanel { Spacing = 10, Children = { CUi.Txt(rotulo, 14.5, CUi.Texto, FontWeight.Normal, true), caixa } };
        f = new CUi.Folha(titulo, corpo, CUi.Botoes(new CUi.Botao(T("Cancelar"), false, Cancelar), new CUi.Botao(T("OK"), true, Ok)), fechar: Cancelar);
        d.Abrir(f);
        Dispatcher.UIThread.Post(() => { caixa.Focus(); caixa.SelectAll(); }, DispatcherPriority.Loaded);
    }

    // ---------------------------------------------------------------- gestao
    // TcoachManagerDlg (11:01d3): os treinadores humanos por nome; tocar escolhe. Adicionar
    // (ate 6), Remover (com mais de um, depois da pergunta) e "de ferias" (add-in 0x28; todos
    // de ferias so com o 0x3C). O OK (e o voltar) so fecha com algum treinador fora de ferias.
    internal static void Gestao(Dialogos d, Sessao s, Action? fechou)
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
        var lista = new ContentControl();
        var ferias = new ContentControl();
        CUi.Folha? f = null;
        var add = new CUi.Botao(T("Adicionar..."), false, () => { });
        var rem = new CUi.Botao(T("Remover"), false, () => { });
        CUi.Botao? ok = null;

        // 11:04a0: os botoes e o interruptor conforme o escolhido
        void Atualizar()
        {
            var linhas = new List<Control>();
            if (CUi.Retro)
            {
                // a lista do computador (TCoach.Formatar 0x80000013): o nome, a equipa e "ferias";
                // tocar escolhe (o realce azul)
                for (int i = 0; i < humanos.Count; i++)
                {
                    int k = i;
                    var t = humanos[i];
                    var e = m.EquipaDoTreinador(t.ID);
                    var nome = ListasCelular.R(Dlg.Texto(t.Nome), false, null, 15);
                    nome.Margin = new Thickness(0, 0, 8, 0);
                    linhas.Add(ListasCelular.LinhaR(ListasCelular.Grade("130,*,Auto", nome, ListasCelular.R(e != null ? Dlg.Texto(e.NomeCurto) : " ", false, null, 15),
                        t.B06 != 0 ? ListasCelular.R(T("férias"), false, null, 15) : null), () => { sel = k; Atualizar(); }, sel: i == sel));
                }
                lista.Content = ListasCelular.CaixaR(linhas);
            }
            else
            {
            for (int i = 0; i < humanos.Count; i++)
            {
                var t = humanos[i];
                var e = m.EquipaDoTreinador(t.ID);
                var nome = CUi.Txt(Dlg.Texto(t.Nome), 15, null, FontWeight.SemiBold);
                var sub = CUi.Txt(e == null ? T("sem equipa") : P98.NomeEquipa(e), 12.5, CUi.Fraco);
                var txt = new StackPanel { Spacing = 1, Children = { nome, sub }, Margin = new Thickness(10, 0, 0, 0) };
                Control? et = t.B06 != 0 ? CUi.Etiqueta(T("de férias")) : null;
                linhas.Add(ListasCelular.Grade("Auto,*,Auto", CUi.Escudo(e, 34), txt, et));
            }
            var (l, marcar) = CUi.ListaEscolha(linhas, i => { sel = i; Atualizar(); });
            if (sel >= 0) marcar(sel);
            lista.Content = humanos.Count == 0 ? CUi.Txt(T("Nada a mostrar."), 14, CUi.Fraco) : l;
            }

            int deFerias = humanos.Count(t => t.B06 != 0);
            add.Habilitado = humanos.Count < 6;
            if (sel >= 0 && sel < humanos.Count)
            {
                var t = humanos[sel];
                rem.Habilitado = humanos.Count > 1;
                bool pode = t.B06 != 0 || humanos.Count - 1 > deFerias || Registo.AddIn(Registo.AddInTodosDeFerias);
                // 11:091c
                ferias.Content = Registo.AddIn(Registo.AddInFerias)
                    ? CUi.Retro ? MarcaR(T("de férias"), t.B06 != 0, v => { MercadoJogo.Ferias(t, v); Escolher(t); }, pode) : CUi.Interruptor(T("De férias"), T("O computador cuida da equipa enquanto o treinador está de férias."), t.B06 != 0, v =>
                    {
                        MercadoJogo.Ferias(t, v);
                        Escolher(t);
                    }, pode)
                    : null;
                if (ok != null) ok.Habilitado = deFerias < humanos.Count || Registo.AddIn(Registo.AddInTodosDeFerias);
            }
            else
            {
                rem.Habilitado = false;
                ferias.Content = null;
                if (ok != null) ok.Habilitado = humanos.Count > 0;
            }
        }
        void Escolher(Treinador? t)
        {
            Recolher();
            sel = t == null ? -1 : humanos.IndexOf(t);
            Atualizar();
        }
        // 11:06e9
        rem.Clique += () =>
        {
            if (!rem.Habilitado || sel < 0 || sel >= humanos.Count) return;
            var t = humanos[sel];
            Perguntar(d, T("Gestão de Treinadores"), T("{0} deixa de jogar?", Dlg.Texto(t.Nome)), () =>
            {
                s.Mercado.RemoverTreinador(t.ID);
                m.Reindexar();
                Escolher(null);
            });
        };
        // 11:09bc: o TCoach e criado antes da pergunta (DS:2722 anda mesmo se cancelar)
        add.Clique += () =>
        {
            if (!add.Habilitado) return;
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
        bool fechado = false;
        void Fechar()
        {
            if (fechado || ok == null || !ok.Habilitado) return;
            fechado = true;
            d.Fechar(f!);
            fechou?.Invoke();
        }
        ok = new CUi.Botao(T("OK"), true, Fechar) { HorizontalAlignment = HorizontalAlignment.Stretch };
        if (CUi.Retro)
        {
            // os TBitBtn do computador: a cruz azul, o traco e o visto verde
            add.ComIcone("Imagens/treinador-adicionar.png", "Imagens/treinador-adicionar-desab.png", 20);
            rem.ComIcone("Imagens/treinador-remover.png", "Imagens/treinador-remover-desab.png", 20);
            ok.ComIcone("Imagens/ok.png", "Imagens/ok-desab.png");
            ok.HorizontalAlignment = HorizontalAlignment.Center;
            ok.MinWidth = 150;
        }
        Atualizar();
        var corpo = new StackPanel { Spacing = 12, Children = { lista, ferias, CUi.Botoes(add, rem) } };
        f = CUi.Retro
            ? new CUi.Folha(T("Gestão de Treinadores"), corpo, ok, fechar: Fechar)
            : new CUi.Folha(T("Gestão de Treinadores"), corpo, ok, T("Os treinadores humanos deste jogo (até 6)."), fechar: Fechar);
        d.Abrir(f);
    }

    // ---------------------------------------------------------------- perfil
    // TcoachOptionsDlg (7:255d): o perfil +0x1A (salarios pelo computador dos nacionais, dos
    // Bosman e dos outros estrangeiros; as ofertas no leilao; esconder as ofertas). OK grava e
    // chama ok; Cancelar (e o voltar) larga as mudancas.
    internal static void Perfil(Dialogos d, Treinador t, Action? ok)
    {
        if (CUi.Retro) { PerfilRetro(d, t, ok); return; }
        var caixas = new[] { t.B1A[0] != 0, t.B1A[1] != 0, t.B1A[2] != 0 };
        int oferta = t.B1A[3];
        bool esconder = t.B1A[4] != 0;
        var corpo = new StackPanel { Spacing = 8 };
        corpo.Children.Add(CUi.SecaoFolha(T("Gestão de Salários")));
        corpo.Children.Add(CUi.Txt(T("Quero que o computador cuide dos salários:"), 14.5, CUi.Texto, FontWeight.Normal, true));
        string[] rot = { T("Dos jogadores nacionais"), T("Dos estrangeiros abrangidos pela Lei Bosman"), T("Dos outros jogadores estrangeiros") };
        var sal = new List<Control>();
        for (int i = 0; i < 3; i++)
        {
            int k = i;
            sal.Add(CUi.Interruptor(rot[i], null, caixas[i], v => caixas[k] = v));
        }
        corpo.Children.Add(CUi.Grupo(sal));
        var sec2 = CUi.SecaoFolha(T("Compra em leilão"));
        sec2.Margin = new Thickness(0, 10, 0, 0);
        corpo.Children.Add(sec2);
        corpo.Children.Add(CUi.Grupo(new[] { CUi.Interruptor(T("Esconder as minhas ofertas"), T("Os outros não veem as minhas ofertas nos leilões."), esconder, v => esconder = v) }));
        var sec3 = CUi.SecaoFolha(T("Ofertas"));
        sec3.Margin = new Thickness(0, 10, 0, 0);
        corpo.Children.Add(sec3);
        string[] ofs = { T("Quero fazer ofertas a todos os jogadores"), T("Não quero fazer ofertas aos jogadores mais fracos"), T("Não quero comprar jogadores em leilão") };
        var (lista, marcar) = CUi.ListaEscolha(ofs.Select(x => (Control)CUi.Txt(x, 14.5, null, FontWeight.Normal, true)).ToList(), i => oferta = i);
        if (oferta is >= 0 and < 3) marcar(oferta);
        corpo.Children.Add(lista);
        CUi.Folha? f = null;
        bool fechado = false;
        void Cancelar() { if (fechado) return; fechado = true; d.Fechar(f!); }
        void Ok()
        {
            if (fechado) return;
            fechado = true;
            for (int i = 0; i < 3; i++) t.B1A[i] = (byte)(caixas[i] ? 1 : 0);
            t.B1A[3] = (byte)Math.Max(0, oferta);
            t.B1A[4] = (byte)(esconder ? 1 : 0);
            d.Fechar(f!);
            ok?.Invoke();
        }
        f = new CUi.Folha(T("Perfil de {0}", Dlg.Texto(t.Nome)), corpo,
            CUi.Botoes(new CUi.Botao(T("Cancelar"), false, Cancelar), new CUi.Botao(T("OK"), true, Ok)), fechar: Cancelar);
        d.Abrir(f);
    }

    // ---------------------------------------------------------------- convite
    // TinviteCoachDlg: "Quer vir treinar o ...?", Sim / Nao (e o "Elenco..." com o add-in 0x0B);
    // so fecha pelos botoes (o voltar nao responde)
    internal static Control Convite(Treinador t, Equipa e, Action<bool> fim)
    {
        if (CUi.Retro) return ConviteRetro(t, e, fim);
        bool feito = false;
        void Fim(bool v)
        {
            if (feito) return;
            feito = true;
            fim(v);
        }
        var corpo = new StackPanel { Spacing = 8 };
        corpo.Children.Add(CUi.Txt(T("Quer vir treinar o {0}?", Dlg.Texto(e.NomeCurto)), 18, null, FontWeight.Bold, true));
        if (P98.NomeEquipa(e) is { Length: > 0 } nome && nome != Dlg.Texto(e.NomeCurto)) corpo.Children.Add(CUi.Txt(nome, 14, CUi.Fraco, FontWeight.Normal, true));
        var bs = new List<Control>();
        if (Registo.AddIn(Registo.AddInPlantelNoConvite)) bs.Add(new CUi.Botao(T("Elenco..."), false, () => TelaTreinadores.AbrirPlantel?.Invoke(e)));
        bs.Add(new CUi.Botao(T("Não"), false, () => Fim(false)));
        bs.Add(new CUi.Botao(T("Sim"), true, () => Fim(true)));
        var esc = CUi.Escudo(e, 44);
        return new CUi.Folha(T("Convite"), corpo, CUi.Botoes(bs.ToArray()), Dlg.Texto(t.Nome), esc);
    }

    // ---------------------------------------------------------------- Retro
    // as pecas do Windows 95 do computador no tamanho do dedo: a caixa de marcar (TCheckBox) e
    // a opcao do grupo (TRadioButton), com o texto do original ao lado
    static readonly BoxShadows Afundada = BoxShadows.Parse("inset 1 1 0 0 #808080, inset 2 2 0 0 #000000, inset -1 -1 0 0 #FFFFFF, inset -2 -2 0 0 #DFDFDF");

    internal static Control MarcaR(string texto, bool marcado, Action<bool> mudou, bool habilitado = true)
    {
        bool v = marcado;
        var visto = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M0,4 L3,7 L9,1"), Stroke = habilitado ? W95.Preto : W95.Cinzento, StrokeThickness = 2.2,
            Width = 11, Height = 9, Stretch = Stretch.Uniform, IsVisible = v,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var caixa = new Border { Width = 20, Height = 20, Background = habilitado ? W95.Branco : W95.Face, BoxShadow = Afundada, Child = visto };
        var t = ListasCelular.R(texto, false, habilitado ? null : W95.Cinzento, 15);
        t.TextWrapping = TextWrapping.Wrap;
        t.Margin = new Thickness(10, 0, 0, 0);
        var c = new Clicavel { Background = Brushes.Transparent, Child = ListasCelular.Grade("Auto,*", caixa, t), MinHeight = 44, CorFoco = null };
        if (habilitado) c.Clique += () => { v = !v; visto.IsVisible = v; mudou(v); };
        else c.Cursor = Cursor.Default;
        return c;
    }

    // as opcoes de um grupo: so uma marcada; escolheu(i)
    internal static Control OpcoesR(IReadOnlyList<string> textos, int atual, Action<int> escolheu)
    {
        var p = new StackPanel();
        var pontos = new List<Border>();
        for (int i = 0; i < textos.Count; i++)
        {
            int k = i;
            var ponto = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = W95.Preto, IsVisible = i == atual };
            var roda = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = W95.Branco, Child = ponto,
                BorderBrush = W95.Cinzento, BorderThickness = new Thickness(1.5),
            };
            ponto.HorizontalAlignment = HorizontalAlignment.Center; ponto.VerticalAlignment = VerticalAlignment.Center;
            pontos.Add(ponto);
            var t = ListasCelular.R(textos[i], false, null, 15);
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(10, 0, 0, 0);
            var c = new Clicavel { Background = Brushes.Transparent, Child = ListasCelular.Grade("Auto,*", roda, t), MinHeight = 44, CorFoco = null };
            c.Clique += () => { for (int j = 0; j < pontos.Count; j++) pontos[j].IsVisible = j == k; escolheu(k); };
            p.Children.Add(c);
        }
        return p;
    }

    // as abas do TNotebook: a escolhida mais alta, ligada a pagina de baixo
    static Control AbasR(IReadOnlyList<string> nomes, IReadOnlyList<Control> paginas)
    {
        var fila = new Grid { ColumnSpacing = 0 };
        var area = new ContentControl { Content = paginas[0] };
        var abas = new List<Clicavel>();
        void Marcar(int k)
        {
            for (int i = 0; i < abas.Count; i++)
            {
                bool m = i == k;
                abas[i].Margin = new Thickness(0, m ? 0 : 4, 0, m ? -1 : 0);
                abas[i].BorderThickness = new Thickness(1, 1, 1, m ? 0 : 1);
            }
            area.Content = paginas[k];
        }
        for (int i = 0; i < nomes.Count; i++)
        {
            int k = i;
            fila.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var t = ListasCelular.R(nomes[i], false, null, 14.5);
            var a = new Clicavel
            {
                Background = W95.Face, Child = t, MinHeight = 40, Padding = new Thickness(14, 4), CorFoco = null,
                BorderBrush = W95.Cinzento, BoxShadow = BoxShadows.Parse("inset 1 1 0 0 #FFFFFF"), ZIndex = 1,
            };
            a.Clique += () => Marcar(k);
            Grid.SetColumn(a, i);
            fila.Children.Add(a);
            abas.Add(a);
        }
        Marcar(0);
        var pagina = new Border
        {
            BorderBrush = W95.Cinzento, BorderThickness = new Thickness(1), BoxShadow = BoxShadows.Parse("inset 1 1 0 0 #FFFFFF"),
            Padding = new Thickness(12, 14, 12, 12), Child = area,
        };
        return new StackPanel { Children = { fila, pagina } };
    }

    // o perfil como no computador: as abas "Gestao de Salarios" (as tres caixas) e "Compra em
    // leilao" (esconder as ofertas e o grupo Ofertas); OK e Cancelar com os glifos
    static void PerfilRetro(Dialogos d, Treinador t, Action? ok)
    {
        var caixas = new[] { t.B1A[0] != 0, t.B1A[1] != 0, t.B1A[2] != 0 };
        int oferta = t.B1A[3];
        bool esconder = t.B1A[4] != 0;
        var p1 = new StackPanel { Spacing = 4 };
        var rot = ListasCelular.R(T("Quero que o computador cuide dos salários:"), false, null, 15);
        rot.TextWrapping = TextWrapping.Wrap;
        rot.Margin = new Thickness(0, 0, 0, 10);
        p1.Children.Add(rot);
        string[] txt = { T("aos jogadores nacionais"), T("aos jogadores estrangeiros abrangidos pela Lei Bosman"), T("aos outros jogadores estrangeiros") };
        for (int i = 0; i < 3; i++) { int k = i; p1.Children.Add(MarcaR(txt[i], caixas[i], v => caixas[k] = v)); }
        var p2 = new StackPanel { Spacing = 8 };
        p2.Children.Add(MarcaR(T("Não quero que os outros vejam as minhas ofertas"), esconder, v => esconder = v));
        p2.Children.Add(CUi.GrupoRetro(T("Ofertas"), OpcoesR(new[] { T("Quero fazer ofertas a todos os jogadores"),
            T("Não quero fazer ofertas aos jogadores mais fracos"), T("Não quero comprar jogadores em leilão") }, oferta, i => oferta = i)));
        CUi.Folha? f = null;
        bool fechado = false;
        void Cancelar() { if (fechado) return; fechado = true; d.Fechar(f!); }
        void Ok()
        {
            if (fechado) return;
            fechado = true;
            for (int i = 0; i < 3; i++) t.B1A[i] = (byte)(caixas[i] ? 1 : 0);
            t.B1A[3] = (byte)Math.Max(0, oferta);
            t.B1A[4] = (byte)(esconder ? 1 : 0);
            d.Fechar(f!);
            ok?.Invoke();
        }
        var bok = new CUi.Botao(T("OK"), true, Ok).ComIcone("Imagens/ok.png", "Imagens/ok-desab.png");
        var bc = new CUi.Botao(T("Cancelar"), false, Cancelar).ComIcone("Imagens/cancelar.png");
        f = new CUi.Folha(T("Perfil de {0}", Dlg.Texto(t.Nome)), AbasR(new[] { T("Gestão de Salários"), T("Compra em leilão") }, new Control[] { p1, p2 }),
            CUi.Botoes(bok, bc), fechar: Cancelar);
        d.Abrir(f);
    }

    // o convite como no computador: o fundo verde-claro, a moldura com o nome do treinador (em
    // negrito) e "Quer vir treinar o EQUIPA ?", e os botoes Sim, Nao e Elenco... com os glifos
    static Control ConviteRetro(Treinador t, Equipa e, Action<bool> fim)
    {
        bool feito = false;
        void Fim(bool v)
        {
            if (feito) return;
            feito = true;
            fim(v);
        }
        var nome = ListasCelular.R(Dlg.Texto(t.Nome), true, null, 15.5);
        var per = ListasCelular.R(T("Quer vir treinar o") + "  " + Dlg.Texto(e.NomeCurto) + " ?", false, null, 15.5);
        per.TextWrapping = TextWrapping.Wrap;
        var moldura = CUi.GrupoRetro(null, new StackPanel { Spacing = 14, Margin = new Thickness(0, 8), Children = { nome, per } });
        var bs = new List<Control>
        {
            new CUi.Botao(T("Sim"), true, () => Fim(true)).ComIcone("Imagens/convite-sim.png"),
            new CUi.Botao(T("Não"), false, () => Fim(false)).ComIcone("Imagens/convite-nao.png"),
        };
        if (Registo.AddIn(Registo.AddInPlantelNoConvite))
            bs.Add(new CUi.Botao(T("Elenco..."), false, () => TelaTreinadores.AbrirPlantel?.Invoke(e)).ComIcone("Imagens/elenco.png"));
        foreach (var b in bs) ((CUi.Botao)b).Padding = new Thickness(8, 8);
        var corpo = new StackPanel { Spacing = 14, Children = { moldura, CUi.Botoes(bs.ToArray()) } };
        var f = new CUi.Folha(T("Convite"), corpo);
        if (f.Content is Border b0) b0.Background = Dlg.VerdeClaro;
        return f;
    }
}
