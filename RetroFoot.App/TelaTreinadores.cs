using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Save;

using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// O menu Tecnico (a Historia e o Ranking estao em TelaListas) e o convite: a TcoachManagerDlg
// (gestao dos treinadores humanos), a TcoachOptionsDlg (perfil) e a TinviteCoachDlg. No
// visual Moderno, as mesmas caixas no tema escuro (TreinadoresModernos, em
// ModernoConfigDialogos.cs), com a mesma logica.
public static class TelaTreinadores
{
    // o convite chama o motor; Ligar poe a janela no gancho Humano.Convite
    public static void Ligar(Sessao s) => s.J.H.Convite = (t, e) => Convite(s, t, e);

    // o "Elenco..." do convite (add-in 0x0B) e de quem faz o plantel (TPlantelDlg)
    public static Action<Equipa>? AbrirPlantel;

    // ---------------------------------------------------------------- gestao
    // TcoachManagerDlg (11:01d3): os treinadores humanos (+0x07 != 0), por nome (11:035b com
    // 11:1f2e). Adicionar (ate 6), Remover (com mais de um, depois de "<nome> deixa de
    // jogar?") e "de ferias" (add-in 0x28; todos de ferias so com o 0x3C). O Fechar fica
    // desligado enquanto todos estao de ferias (o Run repete o ShowModal). fechou: a tela
    // da equipa pode ter de mudar (o treinador dela saiu ou foi de ferias).
    public static void Gestao(Sessao s, Action? fechou = null) => Gestao((Dialogos)s, s, fechou);

    public static void Gestao(Dialogos d, Sessao s, Action? fechou = null)
    {
        if (Visual.Moderno) { TreinadoresModernos.Gestao(d, s, fechou); return; }
        var m = s.J.M;
        var humanos = new List<Treinador>();
        void Recolher()
        {
            humanos.Clear();
            humanos.AddRange(s.S.Treinadores.Where(t => t.Humano != 0)
                .OrderBy(t => Dlg.Bytes(t.Nome), Comparer<byte[]>.Create((a, b) => a.AsSpan().SequenceCompareTo(b))));
        }
        Recolher();
        var j = new Janela(T("Gestão de Treinadores"), 457, 203, W95.Face);
        j.Por(new Bevel(), 8, 8, 441, 113);
        var add = j.Por(Dlg.BotaoGrande(T("Adicionar..."), "treinador-adicionar.png", Dlg.Arial9), 184, 128, 64, 64);
        var rem = j.Por(Dlg.BotaoGrande(T("Remover"), "treinador-remover.png", Dlg.Arial9), 264, 128, 64, 64);
        var lista = j.Por(new Lista { AlturaItem = 16 }, 16, 16, 425, 98);
        var ferias = j.Por(new Marca { Texto = T("de férias"), Fonte = W95.Arial11 }, 16, 128, 97, 17);
        var ok = j.Por(Dlg.BotaoGrande(T("OK"), "Botoes/BBOK.png"), 344, 128, 64, 64);
        lista.Quantos = () => humanos.Count;
        // 11:0a7e: TCoach.Formatar com 0x80000013 (nome sem negrito, equipa, ferias)
        lista.Desenhar = (dc, i, r, sel) =>
        {
            if (sel) dc.FillRectangle(W95.Destaque, r);
            Formatado.Desenhar(dc, W95.Arial13, sel ? W95.Branco : W95.Preto, TelaListas.LinhaTreinador(s, humanos[i], 0x80000013u), r);
        };
        lista.Sel = -1;

        // 11:04a0: os botoes e a caixa conforme o escolhido
        void Atualizar()
        {
            int deFerias = humanos.Count(t => t.B06 != 0);
            add.Habilitado = humanos.Count < 6;
            if (lista.Sel >= 0 && lista.Sel < humanos.Count)
            {
                var t = humanos[lista.Sel];
                rem.Habilitado = humanos.Count > 1;
                ferias.IsVisible = Registo.AddIn(Registo.AddInFerias);
                ferias.Marcado = t.B06 != 0;
                ferias.Habilitado = t.B06 != 0 || humanos.Count - 1 > deFerias || Registo.AddIn(Registo.AddInTodosDeFerias);
                ok.Habilitado = deFerias < humanos.Count || Registo.AddIn(Registo.AddInTodosDeFerias);
            }
            else
            {
                rem.Habilitado = false;
                ferias.IsVisible = false;
                ok.Habilitado = humanos.Count > 0;
            }
            j.InvalidateTudo();
        }
        void Escolher(Treinador? t)
        {
            Recolher();
            lista.Sel = t == null ? -1 : humanos.IndexOf(t);
            Atualizar();
        }
        Atualizar();
        rem.Habilitado = false;
        lista.Clique += _ => Atualizar();
        // 11:091c (OnMouseUp)
        ferias.Clique += () =>
        {
            if (lista.Sel < 0 || lista.Sel >= humanos.Count) return;
            var t = humanos[lista.Sel];
            MercadoJogo.Ferias(t, ferias.Marcado);
            Escolher(t);
        };
        // 11:06e9
        rem.Clique += () =>
        {
            if (lista.Sel < 0 || lista.Sel >= humanos.Count) return;
            var t = humanos[lista.Sel];
            Dlg.Perguntar(d, T("Gestão de Treinadores"), T("{0} deixa de jogar?", Dlg.Texto(t.Nome)), () =>
            {
                s.Mercado.RemoverTreinador(t.ID);
                m.Reindexar();
                Escolher(null);
            });
        };
        // 11:09bc: o TCoach e criado antes da pergunta (DS:2722 anda mesmo se cancelar)
        add.Clique += () =>
        {
            ushort id = s.S.Cabecalho.G2722;
            s.S.Cabecalho.G2722++;
            Dlg.PedirTexto(d, T("Novo treinador"), T("Nome do novo treinador:"), "", 20, nome =>
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
        CaixaDialogo? c = null;
        void Fechar()
        {
            if (!ok.Habilitado) return;
            d.Fechar(c!);
            fechou?.Invoke();
        }
        ok.Clique += Fechar;
        j.AoEnter = Fechar;
        j.Foco = lista;
        c = d.Abrir(j);
    }

    // ---------------------------------------------------------------- perfil
    // TcoachOptionsDlg (7:255d, chamada por 3:8c96; menu Tecnico > Perfil): "Perfil de " +
    // nome; as caixas e o grupo sao o perfil +0x1A (salarios dos nacionais, Bosman e
    // estrangeiros pelo computador; ofertas em leilao; esconder as ofertas). OK grava e chama
    // ok (4:3f47 entao refaz a tela da equipa).
    public static void Perfil(Sessao s, Treinador? t, Action? ok = null) => Perfil((Dialogos)s, t, ok);

    public static void Perfil(Dialogos d, Treinador? t, Action? ok = null)
    {
        if (t == null) return;
        if (Visual.Moderno) { TreinadoresModernos.Perfil(d, t, ok); return; }
        var j = new Janela(T("Perfil de {0}", Dlg.Texto(t.Nome)), 434, 273, W95.Face);
        var nb = j.Por(new Notebook { FonteAbas = Dlg.MsSans12, TopoPagina = 26, AbasPorLinha = 3 }, 0, 0, 434, 273);
        var p1 = nb.Pagina(T("Gestão de Salários"));
        var p2 = nb.Pagina(T("Compra em leilão"));
        nb.Por(p1, new Rotulo { Texto = T("Quero que o computador cuide dos salários:"), Fonte = W95.Arial13 }, 32, 48, 193, 41);
        var caixas = new[]
        {
            nb.Por(p1, new Marca { Texto = T("aos jogadores nacionais") }, 40, 124, 169, 17),
            nb.Por(p1, new Marca { Texto = T("aos jogadores estrangeiros abrangidos pela Lei Bosman") }, 40, 156, 345, 17),
            nb.Por(p1, new Marca { Texto = T("aos outros jogadores estrangeiros") }, 40, 188, 225, 17),
        };
        var grupo = nb.Por(p2, new Grupo { Titulo = T("Ofertas") }, 8, 72, 333, 105);
        grupo.Itens(T("Quero fazer ofertas a todos os jogadores"), T("Não quero fazer ofertas aos jogadores mais fracos"),
            T("Não quero comprar jogadores em leilão"));
        var esconder = nb.Por(p2, new Marca { Texto = T("Não quero que os outros vejam as minhas ofertas") }, 16, 28, 361, 25);
        var bok = j.Por(Dlg.BotaoGrande(T("OK"), "Botoes/BBOK.png", W95.MsSans11b), 356, 34, 64, 64);
        var bc = j.Por(Dlg.BotaoGrande(T("Cancelar"), "Botoes/BBCANCEL.png", W95.MsSans11b), 356, 106, 64, 64);
        for (int i = 0; i < 3; i++) caixas[i].Marcado = t.B1A[i] != 0;
        grupo.Indice = t.B1A[3];
        esconder.Marcado = t.B1A[4] != 0;
        CaixaDialogo? c = null;
        void Cancelar() => d.Fechar(c!);
        void Ok()
        {
            for (int i = 0; i < 3; i++) t.B1A[i] = (byte)(caixas[i].Marcado ? 1 : 0);
            t.B1A[3] = (byte)Math.Max(0, grupo.Indice);
            t.B1A[4] = (byte)(esconder.Marcado ? 1 : 0);
            d.Fechar(c!);
            ok?.Invoke();
        }
        bok.Clique += Ok;
        bc.Clique += Cancelar;
        j.AoEnter = Ok;
        j.AoFechar = Cancelar;
        c = d.Abrir(j);
    }

    // ---------------------------------------------------------------- convite
    // TinviteCoachDlg.Executar (6:3de2), chamada por 3:8bcc (AceitaConvite) na thread do
    // motor: o nome do treinador, "Quer vir treinar o" + nome curto + " ?"; Sim / Nao; o
    // "Elenco..." so com o add-in 0x0B. A janela so fecha pelos botoes (FormCloseQuery).
    public static bool Convite(Sessao s, Treinador t, Equipa e) =>
        s.EsperarDialogo<bool>(fim => JanelaConvite(t, e, fim));

    internal static Control JanelaConvite(Treinador t, Equipa e, Action<bool> fim)
    {
        if (Visual.Moderno) return TreinadoresModernos.Convite(t, e, fim);
        var j = new Janela(T("Convite"), 360, 201, Dlg.VerdeClaro);
        j.Por(new Bevel(), 8, 8, 345, 97);
        j.Por(new Rotulo { Texto = Dlg.Texto(t.Nome), Fonte = W95.Arial13b }, 16, 32, 265, 13);
        j.Por(new Rotulo { Texto = T("Quer vir treinar o"), Fonte = W95.Arial13, Auto = true }, 16, 64, 95, 16);
        j.Por(new Rotulo { Texto = Dlg.Texto(e.NomeCurto) + " ?", Fonte = W95.Arial13 }, 120, 64, 217, 16);
        var sim = j.Por(Dlg.BotaoGrande(T("Sim"), "convite-sim.png"), 106, 124, 64, 64);
        var nao = j.Por(Dlg.BotaoGrande(T("Não"), "convite-nao.png"), 190, 124, 64, 64);
        var elenco = j.Por(Dlg.BotaoGrande(T("Elenco..."), "BBHELP.png"), 274, 124, 64, 64);
        elenco.IsVisible = Registo.AddIn(Registo.AddInPlantelNoConvite);
        bool feito = false;
        void Fim(bool v)
        {
            if (feito) return;
            feito = true;
            fim(v);
        }
        sim.Clique += () => Fim(true);
        nao.Clique += () => Fim(false);
        elenco.Clique += () => AbrirPlantel?.Invoke(e);
        j.AoEnter = () => Fim(true);
        return new CaixaDialogo(j);
    }
}
