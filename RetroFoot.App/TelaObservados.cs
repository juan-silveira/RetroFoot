using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RetroFoot.Motor.Mercado;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// Os jogadores sob observacao do treinador da equipa humana: a TObservedPlayersDlg do
// jogo classico (seg07:1dec, Run). A lista do treinador e ordenada por posicao e nome ao
// abrir (14:0e0d com 11:1826: fica assim no save). Com mais de 15 (o Observado do plantel
// acabou de acrescentar um), Fechar e Equipa ficam desligados e o aviso vermelho diz "So
// pode ter 15 jogadores sob observacao" ate um ser removido. "Equipa..." (ou o duplo
// clique) fecha e entrega o jogador escolhido.
//
// Larguras do launcher (larguras.py): lista 16 pontos mais larga, janela e Fechar tambem.
public static class TelaObservados
{
    // Abre os observados do treinador da equipa e (na thread da interface, com o motor
    // parado). escolher recebe o jogador do "Equipa..." (null = abre o plantel da equipa
    // dele, com e como compradora); aoFechar, quando a janela fecha (por qualquer botao).
    public static void Abrir(Sessao s, SEquipa e, Action<ushort>? escolher = null, Action? aoFechar = null)
    {
        escolher ??= id =>
        {
            var (eq, _) = MercadoJogo.EquipaDoJogador(s.S, id);
            if (eq != null) TelaPlantel.Abrir(s, eq, e, id);
        };
        var f = new Form(s, e, escolher);
        if (aoFechar != null) f.Fechou += aoFechar;
        f.Mostrar();
    }

    // RETROFOOT_TELA=observados: o jogo de teste; se a lista do treinador estiver vazia, poe
    // nela os 3 jogadores mais fortes de outras equipas
    public static Control Demo() => MercadoForm.Demo((s, e) =>
    {
        if (MercadoJogo.Observados(s.S, e.Treinador).Count == 0)
            foreach (var p in s.S.Equipas.Where(x => x != e).SelectMany(x => x.Jogadores).OrderByDescending(p => p.Forca).Take(3))
                MercadoJogo.ObservarJogador(s.S, e.Treinador, p.ID);
        Abrir(s, e);
    });

    sealed class Form : MercadoForm
    {
        readonly SEquipa _e;
        readonly ushort _tr;
        readonly Action<ushort> _escolher;
        readonly Lista _lista;

        public Form(Sessao s, SEquipa e, Action<ushort> escolher) : base(s, 551 + 16, 378)
        {
            _e = e;
            _tr = e.Treinador;
            _escolher = escolher;
            Titulo = T("Jogadores sob observação");
            Ordenar();
            _lista = new Lista
            {
                R = new Rect(16, 16, 537, 246),
                N = () => Ids().Count,
                Item = DesenharJogador,
                AoDuplo = _ => Equipa(),
            };
            Comps.Add(_lista);
            ListaPrincipal = _lista;
            Comps.Add(new Botao
            {
                R = new Rect(8, 280, 64, 64), Texto = T("Limpar"), Glifo = "Mercado/TObservedPlayersDlg.clearBtn.png", Negrito = true,
                Habilitado = () => Ids().Count > 0,
                Clique = () => { MercadoJogo.LimparObservados(S.S, _tr); _lista.Sel = -1; },
            });
            Comps.Add(new Botao
            {
                R = new Rect(99, 280, 64, 64), Texto = T("Remover"), Glifo = "Mercado/TObservedPlayersDlg.removeBtn.png", Negrito = true,
                Habilitado = () => Escolhido() != 0, Clique = Remover,
            });
            Comps.Add(new Botao
            {
                R = new Rect(381, 280, 64, 64), Texto = T("Time..."), Glifo = "Mercado/TObservedPlayersDlg.teamBtn.png", Negrito = true,
                Habilitado = () => Escolhido() != 0 && !Cheia(), Clique = Equipa,
            });
            // FormCloseQuery (7:2108): so fecha com o Fechar ligado
            Comps.Add(new Botao
            {
                R = new Rect(480 + 16, 280, 64, 64), Texto = T("Fechar"), Glifo = "Mercado/BBOK.png", Negrito = true,
                Padrao = true, Cancelar = true, Habilitado = () => !Cheia(), Clique = Fechar,
            });
        }

        List<ushort> Ids() => MercadoJogo.Observados(S.S, _tr);
        bool Cheia() => Ids().Count > MercadoJogo.LimiteObservados;
        ushort Escolhido() => _lista.Sel >= 0 && _lista.Sel < Ids().Count ? Ids()[_lista.Sel] : (ushort)0;

        // a bolha de 14:0e0d com 11:1826 sobre os jogadores, e a lista refeita nessa ordem
        void Ordenar()
        {
            var l = new List<ushort>(Ids());
            var ps = l.Select(id => { var (eq, k) = MercadoJogo.EquipaDoJogador(S.S, id); return eq?.Jogadores[k]; }).ToList();
            int n = ps.Count;
            for (int i = 1; i <= n - 1; i++)
                for (int k = n - 1; k >= i; k--)
                    if (ps[k - 1] is { } a && ps[k] is { } b && !Antes(a, b))
                    {
                        (ps[k - 1], ps[k]) = (ps[k], ps[k - 1]);
                        (l[k - 1], l[k]) = (l[k], l[k - 1]);
                    }
            MercadoJogo.LimparObservados(S.S, _tr);
            foreach (var id in l) MercadoJogo.ObservarJogador(S.S, _tr, id);
        }

        static bool Antes(SJogador a, SJogador b)
        {
            if (a.Posicao != b.Posicao) return a.Posicao < b.Posicao;
            return RetroFoot.Motor.Partida.Jornada.NomeNaTela(a).AsSpan().SequenceCompareTo(RetroFoot.Motor.Partida.Jornada.NomeNaTela(b)) < 0;
        }

        // removeBtnClick (7:2173)
        void Remover()
        {
            var id = Escolhido();
            if (id == 0) return;
            MercadoJogo.DesobservarJogador(S.S, _tr, id);
            _lista.Sel = -1;
        }

        // teamBtnClick (7:228e)
        void Equipa()
        {
            var id = Escolhido();
            if (id == 0 || Cheia()) return;
            Fechar();
            _escolher(id);
        }

        protected override void DesenharFundo(DrawingContext dc)
        {
            Bevel(dc, new Rect(8, 8, 537 + 16, 257));
            // Memo1: "So pode ter 15 jogadores sob observacao", com mais de 15
            if (Cheia())
            {
                var r = new Rect(6, 356, 539 + 16, 17);
                dc.FillRectangle(Branco, r);
                Rotulo(dc, T("Só pode ter {0} jogadores sob observação", MercadoJogo.LimiteObservados), r, 11, Vermelho, centro: true);
            }
        }

        // ListBoxObservedPlayersDrawItem (7:1f77): colunas 0xEC63 (posicao, nome, equipa,
        // forca, S, L, pais, preco para a equipa do treinador, comportamento)
        void DesenharJogador(DrawingContext dc, int i, Rect r, bool sel)
        {
            var ids = Ids();
            if (i >= ids.Count) return;
            var (eq, k) = MercadoJogo.EquipaDoJogador(S.S, ids[i]);
            if (eq == null) return;
            var p = eq.Jogadores[k];
            var cor = Preto;
            if (sel) cor = MarcarEscolhido(dc, r);
            using var c = new Colunas(dc, r, cor, false);
            c.Col(0, "");
            c.Col(15, Idioma.Posicao(Math.Min((int)p.Posicao, 3)));
            c.Negrito = true;
            c.Col(120, NomeJogador(p));
            c.Negrito = false;
            c.Col(140, Texto(eq.NomeCurto));
            c.Col(20, p.Forca.ToString(), true);
            c.Col(10, p.Suspensao != 0 ? Abrev("coluna: S", " S") : "");
            c.Col(10, p.JogosLesao != 0 ? Abrev("coluna: L", " L") : "");
            c.Col(25, PaisDe(p.Pais));
            int v = S.Mercado.PrecoDeVenda(eq, _e, p, MercadoJogo.ForcaMedia(eq));
            c.Col(80, v > 0 ? Dinheiro(v) : "", true);
            c.Col(20, ComportamentoCurto(p));
        }
    }
}
