using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Mercado;
using Mem = RetroFoot.Motor.Partida.Memoria;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// O plantel de uma equipa: a TPlantelDlg do jogo classico (seg10:129e, Run), com a compra
// direta de jogadores pela equipa humana (buyBtn), o "Observado" (a lista do treinador), a
// Lista dos observados e o Procurar. A caixa de cima troca a equipa mostrada.
//
// Larguras do launcher (larguras.py): a lista 16 pontos mais larga e tudo a direita dela
// 16 pontos para la. Com as regras do launcher, a ficha do jogador tem Nota e Risco de
// lesao (ficha.py) e o Historial a linha dos amarelos (historial.py).
public static partial class TelaPlantel
{
    // Ganchos para as janelas que nao sao do mercado (o integrador liga): null = o botao
    // fica desligado (Calendario, Balanco) ou escondido (Emprestimo). Recebem a sessao e a
    // equipa (Calendario/Balanco: a mostrada; Emprestimo: a compradora) e um "pronto" para
    // chamar quando a janela deles fechar (o plantel se redesenha).
    public static Action<Sessao, SEquipa, Action>? Calendario, Balanco, Emprestimo;

    // Abre o plantel da equipa mostrada (na thread da interface, com o motor parado).
    // comprador: a equipa humana que pode comprar (null = so ver); jogador: o ja escolhido
    // (0 = nenhum); aoFechar: quando a janela fecha.
    public static void Abrir(Sessao s, SEquipa mostrada, SEquipa? comprador, ushort jogador = 0, Action? aoFechar = null)
    {
        var f = new Form(s, mostrada, comprador, jogador);
        if (aoFechar != null) f.Fechou += aoFechar;
        f.Mostrar();
    }

    // RETROFOOT_TELA=plantel: o jogo de teste, com o plantel da 1a equipa que nao e a humana
    public static Control Demo() => MercadoForm.Demo((s, e) =>
    {
        var outra = s.S.Equipas.FirstOrDefault(x => x != e && s.TreinadorDa(x) is not { Humano: not 0 }) ?? e;
        Abrir(s, outra, e);
    });

    sealed partial class Form : MercadoForm
    {
        const double B = 16;    // larguras.py
        readonly bool _launcher;
        ushort _equipa, _inicial, _sel;
        readonly ushort _comprador;
        int _preco;
        readonly List<string> _nomes;
        readonly Lista _lista;
        readonly Botao _comprar, _observado;
        bool _observadoLigado;
        IBrush _letra = Preto, _fundo = Branco;

        public Form(Sessao s, SEquipa mostrada, SEquipa? comprador, ushort jogador)
            : base(s, 632 + B, 453)
        {
            _launcher = s.J.M.Regras.CartoesAmarelos;
            _equipa = mostrada.ID;
            _inicial = jogador;
            _comprador = comprador?.ID ?? 0;
            // FillComboCurrentTeam (10:120e): os nomes curtos de todas as equipas (Sorted)
            _nomes = s.S.Equipas.Select(x => Texto(x.NomeCurto)).OrderBy(n => n, StringComparer.Create(System.Globalization.CultureInfo.InvariantCulture, ignoreCase: true)).ToList();

            Comps.Add(new Combo
            {
                R = new Rect(8, 8, 241, 21), Plano = true, Tam = 11, Negrito = true, Fonte = SansSerif, ItemA = 13 + 3,
                Itens = () => _nomes,
                Indice = () => Mostrada() is { } m ? _nomes.IndexOf(Texto(m.NomeCurto)) : -1,
                Texto = () => Mostrada() is { } m ? Texto(m.NomeCurto) : "",
                Escolher = EscolherEquipa,
            });
            _lista = new Lista
            {
                R = new Rect(16, 40, 353 + B, 390),
                N = () => Mostrada()?.Jogadores.Count ?? 0,
                Item = DesenharJogador,
                Fundo = () => _fundo,
                AoClicar = Clique,
            };
            Comps.Add(_lista);
            ListaPrincipal = _lista;
            double d = B;
            Comps.Add(new Botao
            {
                R = new Rect(412 + d, 32, 64, 64), Texto = T("Balanço"), Glifo = "Mercado/TPlantelDlg.balanceBtn.png",
                Visivel = () => Balanco != null,
                Clique = () => { if (Mostrada() is { } m) Balanco?.Invoke(S, m, Redesenhar); },
            });
            Comps.Add(new Botao
            {
                R = new Rect(516 + d, 32, 64, 64), Texto = T("Calendário"), Glifo = "Mercado/TPlantelDlg.calendarBtn.png",
                Habilitado = () => Calendario != null,
                Clique = () => { if (Mostrada() is { } m) Calendario?.Invoke(S, m, Redesenhar); },
            });
            // FirstUpdate: o Procurar so com o add-in dos observados e uma equipa que compra
            Comps.Add(new Botao
            {
                R = new Rect(404 + d, 316, 64, 64), Texto = T("Procurar"), Glifo = "Mercado/TPlantelDlg.findBtn.png", Glifos = 1,
                Visivel = () => Compradora() != null,
                Clique = () => { if (Compradora() is { } c) TelaProcura.Abrir(S, c, MostrarJogador); },
            });
            _comprar = new Botao
            {
                R = new Rect(468 + d, 316, 64, 64), Texto = T("Comprar"), Glifo = "Mercado/TPlantelDlg.buyBtn.png",
                Visivel = PodeComprarAqui, Habilitado = () => _preco > 0, Clique = Comprar,
            };
            Comps.Add(_comprar);
            Comps.Add(new Botao
            {
                R = new Rect(532 + d, 316, 64, 64), Texto = T("Empréstimo"), Glifo = "Mercado/TPlantelDlg.loanBtn.png",
                Visivel = () => PodeComprarAqui() && Emprestimo != null,
                Clique = () => { if (Compradora() is { } c) Emprestimo?.Invoke(S, c, () => { AtualizarCompra(); Redesenhar(); }); },
            });
            _observado = new Botao
            {
                R = new Rect(404 + d, 380, 64, 64), Texto = T("Observado"), Glifo = "Mercado/TPlantelDlg.SpeedButtonObserved.png",
                Visivel = () => Compradora() != null, Habilitado = () => _observadoLigado,
                Baixo = () => _observadoLigado && Compradora() is { } c && MercadoJogo.Observado(S.S, c.Treinador, _sel),
                Clique = Observar,
            };
            Comps.Add(_observado);
            Comps.Add(new Botao
            {
                R = new Rect(468 + d, 380, 64, 64), Texto = T("Lista"), Glifo = "Mercado/TPlantelDlg.observedPlayersBtn.png",
                Visivel = () => Compradora() is { } c && Humana(c),
                Clique = () => { if (Compradora() is { } c) TelaObservados.Abrir(S, c, MostrarJogador); },
            });
            Comps.Add(new Botao
            {
                R = new Rect(532 + d, 380, 64, 64), Texto = T("Fechar"), Glifo = "Mercado/TPlantelDlg.OKBtn.png",
                Padrao = true, Cancelar = true, Clique = Fechar,
            });
            Atualizar();
        }

        SEquipa? Mostrada() => S.Equipa(_equipa);
        SEquipa? Compradora() => _comprador == 0 ? null : S.Equipa(_comprador);

        // Update: o buyBtn so se ha quem compre e a equipa mostrada nao e humana
        bool PodeComprarAqui() => Compradora() != null && Mostrada() is { } m && !Humana(m);

        (SJogador? P, int K) Escolhido()
        {
            var e = Mostrada();
            if (e == null || _sel == 0) return (null, -1);
            int k = e.Jogadores.FindIndex(j => j.ID == _sel);
            return k < 0 ? (null, -1) : (e.Jogadores[k], k);
        }

        // um jogador de outra equipa (Observado, Lista e Procurar): a equipa dele, ja escolhido
        void MostrarJogador(ushort id)
        {
            var (e, _) = MercadoJogo.EquipaDoJogador(S.S, id);
            if (e == null) return;
            _equipa = e.ID;
            _inicial = id;
            Atualizar();
        }

        // ComboCurrentTeamChange (10:1c8f): a 1a equipa com o nome curto escolhido
        void EscolherEquipa(int i)
        {
            if (i < 0 || i >= _nomes.Count) return;
            var e = S.S.Equipas.FirstOrDefault(x => Texto(x.NomeCurto) == _nomes[i]);
            if (e == null) return;
            _equipa = e.ID;
            _inicial = 0;
            Atualizar();
        }

        // Update (10:13fa)
        void Atualizar()
        {
            var e = Mostrada();
            if (e == null) return;
            (_letra, _fundo) = Cores(e);
            FundoCliente = _fundo;
            // titulo: nome completo + " (" + pais + ")"
            Titulo = Texto(e.NomeCompleto) + " (" + NomePais(PaisDe(e.Pais)) + ")";
            LogoTitulo = Escudos.Escudo(e);
            // a lista por posicao e nome (14:0e0d com 11:1826), como o original, na equipa
            RetroFoot.Motor.Partida.Jornada.OrdenarPorPosicaoNome(e);
            _observadoLigado = false;
            _lista.Sel = -1;
            _lista.Rolagem = 0;
            _sel = 0; _preco = 0;
            if (_inicial != 0)
            {
                int k = e.Jogadores.FindIndex(j => j.ID == _inicial);
                if (k >= 0)
                {
                    _lista.Sel = k;
                    _lista.MostrarEscolhido();
                    Clique(k);
                    return;
                }
            }
            AtualizarCompra();
            Redesenhar();
        }

        // ListBoxOfPlayersClick (10:1fa7): o jogador escolhido, a ficha e o Observado
        void Clique(int i)
        {
            var e = Mostrada();
            if (e == null || i < 0 || i >= e.Jogadores.Count) { _sel = 0; AtualizarCompra(); return; }
            _sel = e.Jogadores[i].ID;
            _observadoLigado = true;
            AtualizarCompra();
            Redesenhar();
        }

        // UpdateBuyBtn (10:1939): o preco (3:63fa, com a equipa que compra) e o Comprar so se
        // o preco > 0, menor que o dinheiro dela e PodeComprar (3:633d)
        void AtualizarCompra()
        {
            var (p, _) = Escolhido();
            var e = Mostrada();
            var c = Compradora();
            _preco = p == null || e == null ? 0 : S.Mercado.PrecoParaComprar(c, e, p);
        }

        // buyBtnClick (10:1d66): MercadoJogo.Comprar e Update. A compra direta exige o jogo
        // registado (Opcoes.Registrado, sempre true: o RetroFoot nao tem registo).
        void Comprar()
        {
            var (p, k) = Escolhido();
            var e = Mostrada();
            var c = Compradora();
            if (p == null || e == null || c == null || _preco <= 0) return;
            if (S.Remoto is { } rm) { rm.Executar(new RetroFoot.Motor.Online.Comprar(e.ID, p.ID, _preco)); return; }
            S.Mercado.Comprar(c, e, k, _preco, registado: S.J.Op.Registrado);
            // (as janelas que mostram o dinheiro em caixa se refazem: a Lista de Jogadores
            // aberta por baixo, a tela da equipa)
            TelaDinheiro.Avisar(c);
            _inicial = 0;
            Atualizar();
        }

        // SpeedButtonObservedClick (10:22dd): tira o jogador da lista do treinador que compra,
        // ou poe; se a lista passou de 15, a janela dos observados e, do "Equipa..." dela, o
        // jogador escolhido la
        void Observar()
        {
            var c = Compradora();
            if (c == null || _sel == 0) return;
            if (MercadoJogo.Observado(S.S, c.Treinador, _sel))
            {
                MercadoJogo.DesobservarJogador(S.S, c.Treinador, _sel);
                return;
            }
            if (MercadoJogo.ObservarJogador(S.S, c.Treinador, _sel))
                TelaObservados.Abrir(S, c, MostrarJogador, Redesenhar);
        }

        // ------------------------------------------------------------ desenho
        protected override void DesenharFundo(DrawingContext dc)
        {
            var e = Mostrada();
            if (e == null) return;
            const double d = B;
            Bevel(dc, new Rect(8, 32, 369 + B, 401));
            int dv = S.J.M.DivisaoDa(e);
            if (dv >= 0 && dv < S.S.Divisoes.Count)
                Rotulo(dc, dv >= 4 ? T("Distrital") : Pascal(S.S.Divisoes[dv].Nome, 20), new Rect(256, 8, 129, 14), 13, _letra);
            MedidorMoral(dc, new Rect(396 + d, 8, 201, 17), e, 2);

            // a ficha do jogador escolhido (PanelPlayer, GroupBox1)
            var (p, _) = Escolhido();
            double px = 404 + d, py = 104;
            const double t = 11;
            string comp = p == null ? "" : p.Comportamento < Comportamentos.Length ? T(Comportamentos[p.Comportamento]) : p.Comportamento.ToString();
            Rect grupo;
            double ySus, yLes, ySal;
            var linhas = new List<(string Rot, string Valor, double Y)>();
            if (_launcher)
            {
                Escreve(dc, T("Comportamento"), px, py, t, _letra);
                Escreve(dc, T("Nota"), px, py + 15, t, _letra);
                Escreve(dc, T("Risco de lesão"), px, py + 30, t, _letra);
                Escreve(dc, T("Gols na Temporada"), px, py + 45, t, _letra);
                if (p != null)
                {
                    Escreve(dc, comp, px + 104, py, t, _letra);
                    Escreve(dc, p.Nota.ToString(), px + 104, py + 15, t, _letra);
                    Escreve(dc, p.Lesao.ToString(), px + 104, py + 30, t, _letra);
                    Escreve(dc, TextosMercado.GolsComMedia(p, true), px + 104, py + 45, t, _letra);
                }
                grupo = new Rect(px, 164, 185, 97);
                foreach (var (rot, y, qual) in new[] { (T("Jogos"), 16.0, Mem.HistJogos), (T("Gols"), 32, Mem.HistGolos), (T("Cartões amarelos"), 48, -1), (T("Cartões vermelhos"), 64, Mem.HistVermelhos), (T("Lesões"), 80, Mem.HistLesoes) })
                    linhas.Add((rot, p == null ? "" : qual < 0 ? RetroFoot.Motor.Rodada.Campos.JgB(p, RetroFoot.Motor.Rodada.Campos.JgAmarelosAcumulados).ToString()
                        : qual == Mem.HistGolos ? TextosMercado.GolsComMedia(p, false) : Hist(p, qual).ToString(), y));
                ySus = py + 158; yLes = py + 172; ySal = 291;
            }
            else
            {
                Escreve(dc, T("Comportamento"), px, py, t, _letra);
                Escreve(dc, T("Gols na Temporada"), px, py + 20, t, _letra);
                if (p != null)
                {
                    Escreve(dc, comp, px + 88, py, t, _letra);
                    // o valor em 104 como no launcher: "Gols na Temporada" nao cabe antes de 96
                    Escreve(dc, TextosMercado.GolsComMedia(p, true), px + 104, py + 20, t, _letra);
                }
                grupo = new Rect(px, 140, 185, 105);
                foreach (var (rot, y, qual) in new[] { (T("Jogos"), 24.0, Mem.HistJogos), (T("Gols"), 43, Mem.HistGolos), (T("Cartões vermelhos"), 61, Mem.HistVermelhos), (T("Lesões"), 80, Mem.HistLesoes) })
                    linhas.Add((rot, p == null ? "" : qual == Mem.HistGolos ? TextosMercado.GolsComMedia(p, false) : Hist(p, qual).ToString(), y));
                ySus = py + 143; yLes = py + 159; ySal = 284;
            }
            Grupo(dc, grupo, T("Histórico"), t, _letra, _fundo);
            foreach (var (rot, v, y) in linhas)
            {
                Escreve(dc, rot, grupo.X + 8, grupo.Y + y, t, _letra);
                Direita(dc, v, grupo.X + 174, grupo.Y + y, t, _letra);
            }
            // "Suspenso por"/"Lesionado por" (so no jogo registado), em clWindowText
            if (p != null)
            {
                short sus = (short)p.Suspensao, les = (short)p.JogosLesao;
                if (sus > 0) Escreve(dc, sus == 1 ? T("Suspenso por 1 jogo") : T("Suspenso por {0} jogos", sus), px, ySus, t, Preto, true);
                if (les > 0) Escreve(dc, les == 1 ? T("Lesionado por 1 jogo") : T("Lesionado por {0} jogos", les), px, yLes, t, Preto, true);
            }
            // "Salario pretendido:" + o salario que o jogador pediria na compradora (3:7ff4) +
            // o nome da moeda, com o Comprar ligado
            bool salario = false;
            if (p != null && _preco > 0 && _comprar.EstaVisivel && Compradora() is { } c)
            {
                int sal = S.Mercado.Salario(p, Ext.Bytes(c.B79));
                Rotulo(dc, T("Salário pretendido:") + "\n" + FormatoDinheiro.Valor(S.S, sal), new Rect(388 + d, ySal, 137, 32), t, _letra);
                salario = true;
            }
            // (RetroFoot) o valor de mercado, no espaco livre abaixo (depois do salario pretendido)
            if (p != null)
                Rotulo(dc, T("Valor de mercado:") + "\n" + TextosMercado.TextoValorMercado(S, p), new Rect(388 + d, ySal + (salario ? 36 : 0), 137, 32), t, _letra);
        }

        // historial (4 bytes); os 2 de cima das lesoes e das expulsoes guardam os amarelos do
        // launcher (cartoes.asm): so os 2 de baixo
        static int Hist(SJogador p, int qual)
        {
            int v = Mem.Historial(p, qual);
            return qual == Mem.HistVermelhos || qual == Mem.HistLesoes ? v & 0xFFFF : v;
        }

        // ListBoxOfPlayersDrawItem (10:1aba): colunas 0xC47 (posicao, nome, forca, S, L,
        // nacional) e, se a mostrada e do computador e ha quem compre, o preco (0x2000)
        void DesenharJogador(DrawingContext dc, int i, Rect r, bool sel)
        {
            var e = Mostrada();
            if (e == null || i >= e.Jogadores.Count) return;
            var p = e.Jogadores[i];
            var cor = _letra;
            if (sel) cor = MarcarEscolhido(dc, r);
            using var c = new Colunas(dc, r, cor, true);
            c.Col(0, "");
            c.Col(15, Idioma.Posicao(Math.Min((int)p.Posicao, 3)));
            c.Col(120, NomeJogador(p));
            c.Col(20, p.Forca.ToString(), true);
            c.Col(10, p.Suspensao != 0 ? Abrev("coluna: S", " S") : "");
            c.Col(10, p.JogosLesao != 0 ? Abrev("coluna: L", " L") : "");
            c.Col(25, Nacional(p));
            if (Compradora() is { } comp && !Humana(e))
            {
                int v = S.Mercado.PrecoDeVenda(e, comp, p, MercadoJogo.ForcaMedia(e));
                c.Col(80, v > 0 ? Dinheiro(v) : "", true);
            }
        }
    }
}
