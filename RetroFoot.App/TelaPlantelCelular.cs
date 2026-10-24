using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.Mercado;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;
using TM = RetroFoot.App.TextosMercado;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// O plantel de outra equipa no celular: a tela inteira, como o elenco da nossa (o escudo, a
// divisao e o moral em cima, os botoes da janela, os jogadores por setor com o preco), e o
// toque num jogador abre a ficha dele numa folha com o Observado e o Comprar. A logica e a do
// Form (TelaPlantel.cs): esta tela so chama os mesmos metodos e se refaz.
public static partial class TelaPlantel
{
    sealed partial class Form
    {
        protected override Control? VersaoCelular() => new PlantelCelular(this);

        sealed class PlantelCelular : UserControl
        {
            static readonly string[] Setores = { "Goleiros", "Defensores", "Meio-campistas", "Atacantes" };
            readonly Form _f;
            ScrollViewer _rolar = new();
            ushort _equipaMostrada;
            // a ficha aberta (o jogador e o que a fecha) e o jogador ja mostrado ao abrir
            Action? _fecharFicha;
            ushort _fichaDe;
            (ushort, ushort) _inicialMostrado;

            public PlantelCelular(Form f)
            {
                _f = f;
                f.AoRedesenhar = Refazer;
                Refazer();
            }

            void Refazer()
            {
                var e = _f.Mostrada();
                if (e == null) return;
                if (e.ID != _equipaMostrada || Content == null)
                {
                    // outra equipa: a folha nova (o titulo e o nome dela), do comeco da lista
                    _equipaMostrada = e.ID;
                    _rolar = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                    if (CUi.Retro)
                    {
                        // no Retro, o titulo do computador ("NOME (Pais)") e a janela na cor da equipa
                        var f = new CUi.Folha(Texto(e.NomeCompleto) + " (" + NomePais(PaisDe(e.Pais)) + ")", _rolar, new Border(), null, inteira: true, rolar: false, fechar: _f.Fechar);
                        MercadoRetro.Pintar(f, Cores(e).Fundo);
                        Content = f;
                    }
                    else Content = new CUi.Folha(Texto(e.NomeCompleto), _rolar, null, NomePais(PaisDe(e.Pais)), inteira: true, rolar: false, fechar: _f.Fechar);
                }
                if (CUi.Retro && Content is CUi.Folha fr) fr.Rodape(RodapeRetro());
                // a mesma equipa: so o corpo (a rolagem fica onde estava)
                _rolar.Content = new Border { Padding = new Thickness(12, 10, 12, 16), Child = CUi.Retro ? CorpoRetro(e) : Corpo(e) };
                // a ficha aberta de outro jogador (a equipa mudou pelos observados): fecha; do
                // mesmo, refeita (o Observado, o preco)
                if (_fecharFicha != null && _fichaDe != _f._sel) FecharFicha();
                else if (_fecharFicha != null && _f.Escolhido().P is { } pj) AbrirFicha(pj);
                // o jogador ja escolhido ao abrir (da Lista de Jogadores, dos observados): a ficha
                if (_f._inicial != 0 && _f._sel == _f._inicial && _inicialMostrado != (e.ID, _f._inicial) && _f.Escolhido().P is { } p0)
                {
                    _inicialMostrado = (e.ID, _f._inicial);
                    Dispatcher.UIThread.Post(() => { if (_f.Escolhido().P == p0 && _fecharFicha == null) AbrirFicha(p0); }, DispatcherPriority.Loaded);
                }
            }

            Control Corpo(SEquipa e)
            {
                var s = _f.S;
                var p = new StackPanel { Spacing = 8 };
                // o cabecalho: o escudo, o nome, a divisao, o moral e o trocar de equipa
                var cab = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*,Auto") };
                var esc = CUi.Escudo(e, 48);
                cab.Children.Add(esc);
                var nomes = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
                nomes.Children.Add(CUi.Retro ? CUi.NomeEquipa(e, Texto(e.NomeCurto), 16, true) : CUi.Txt(Texto(e.NomeCurto), 17, null, FontWeight.Bold));
                int dv = s.J.M.DivisaoDa(e);
                if (dv >= 0 && dv < s.S.Divisoes.Count)
                    nomes.Children.Add(CUi.Txt(dv >= 4 ? T("Distrital") : Pascal(s.S.Divisoes[dv].Nome, 20), 13, CUi.Fraco));
                var moral = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,8,120"), HorizontalAlignment = HorizontalAlignment.Left };
                moral.Children.Add(CUi.Txt(T("Moral"), 12.5, CUi.Fraco));
                var barra = MercadoCelular.Moral(e);
                Grid.SetColumn(barra, 2); moral.Children.Add(barra);
                nomes.Children.Add(moral);
                Grid.SetColumn(nomes, 2); cab.Children.Add(nomes);
                var trocar = new CUi.Botao(T("Trocar"), false, EscolherEquipa) { Padding = new Thickness(12, 4), MinHeight = 40 };
                trocar.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(trocar, 3); cab.Children.Add(trocar);
                var cartao = new StackPanel { Spacing = 10, Children = { cab } };
                // o dinheiro em caixa de quem compra (o preco e comparado com ele)
                if (_f.Compradora() is { } cc && !_f.Humana(e))
                    cartao.Children.Add(CUi.Linha_(T("Dinheiro em caixa"), FormatoDinheiro.Valor(s.S, (int)cc.Dinheiro)));
                p.Children.Add(CUi.Cartao_(null, cartao));

                // os botoes da janela (Balanco, Calendario, Procurar, Lista, Emprestimo)
                var bs = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
                CUi.Botao B(string t, Action a, bool hab = true)
                {
                    var b = new CUi.Botao(t, false, a) { Padding = new Thickness(14, 4), MinHeight = 42, Habilitado = hab };
                    bs.Children.Add(b);
                    return b;
                }
                if (Balanco != null) B(T("Balanço"), () => { if (_f.Mostrada() is { } m) Balanco?.Invoke(s, m, Refazer); });
                B(T("Calendário"), () => { if (_f.Mostrada() is { } m) Calendario?.Invoke(s, m, Refazer); }, Calendario != null);
                if (_f.Compradora() is { } c)
                {
                    B(T("Procurar"), () => { if (_f.Compradora() is { } c2) TelaProcura.Abrir(s, c2, _f.MostrarJogador); });
                    if (_f.Humana(c)) B(T("Jogadores sob observação"), () => { if (_f.Compradora() is { } c2) TelaObservados.Abrir(s, c2, _f.MostrarJogador); });
                    if (_f.PodeComprarAqui() && Emprestimo != null)
                        B(T("Empréstimo"), () => { if (_f.Compradora() is { } c2) Emprestimo?.Invoke(s, c2, () => { _f.AtualizarCompra(); Refazer(); }); });
                }
                if (bs.Children.Count > 0) p.Children.Add(bs);

                // os jogadores por setor, na ordem do original (posicao e nome)
                bool comPreco = _f.Compradora() != null && !_f.Humana(e);
                var jogadores = e.Jogadores;
                foreach (var grupo in Enumerable.Range(0, jogadores.Count).GroupBy(i => Math.Min((int)jogadores[i].Posicao, 3)))
                {
                    var t = CUi.TituloSecao_(T(Setores[grupo.Key]) + "  ·  " + grupo.Count());
                    t.Margin = new Thickness(4, 8, 0, 0);
                    p.Children.Add(t);
                    var linhas = new List<Control>();
                    foreach (int i in grupo) linhas.Add(Linha(e, i, comPreco));
                    p.Children.Add(CUi.Grupo(linhas));
                }
                return p;
            }

            Control Linha(SEquipa e, int i, bool comPreco)
            {
                var j = e.Jogadores[i];
                var selos = MercadoCelular.Selos(j);
                if (_f.Compradora() is { } c && MercadoJogo.Observado(_f.S.S, c.Treinador, j.ID))
                    selos.Add(MercadoCelular.Selo("👁", Brushes.Transparent, CUi.Retro ? W95.Marinho : TemaVerde.Destaque));
                Control? extra = null;
                if (comPreco && _f.Compradora() is { } comp)
                {
                    int v = _f.S.Mercado.PrecoDeVenda(e, comp, j, MercadoJogo.ForcaMedia(e));
                    extra = MercadoCelular.Extra(v > 0 ? _f.Dinheiro(v) : "", null);
                }
                int k = i;
                var linha = CUi.Toque(MercadoCelular.Jogador(j, selos, extra), () => Tocar(k), 44);
                if (j.ID == _f._sel)
                {
                    linha.Background = CUi.Selecao;
                    if (CUi.Retro) Pintar(linha.Child, W95.Branco);
                }
                return linha;
            }

            static void Pintar(Control? c, IBrush cor)
            {
                switch (c)
                {
                    case TextBlock t when t.Tag is not "fixo": t.Foreground = cor; break;
                    case Panel p: foreach (var x in p.Children) Pintar(x, cor); break;
                    case Decorator d: Pintar(d.Child, cor); break;
                }
            }

            // ListBoxOfPlayersClick: o escolhido (o preco, o Observado) e a ficha
            void Tocar(int i)
            {
                _f._lista.Sel = i;
                _f.Clique(i);
                if (_f.Escolhido().P is { } p) AbrirFicha(p);
            }

            // ------------------------------------------------------------ a ficha
            void AbrirFicha(SJogador p)
            {
                if (CUi.Retro) { AbrirFichaRetro(p); return; }
                var s = _f.S;
                var e = _f.Mostrada();
                if (e == null) return;
                bool launcher = s.J.M.Regras.CartoesAmarelos;
                var antes = new List<(string, string)>();
                var nac = Nacional(p);
                if (nac.Length > 0) antes.Add((T("Nacionalidade"), nac.StartsWith("* ") ? NomePais(nac[2..]) + " *" : NomePais(nac)));
                var c = _f.Compradora();
                if (c != null && !_f.Humana(e))
                {
                    int v = s.Mercado.PrecoDeVenda(e, c, p, MercadoJogo.ForcaMedia(e));
                    antes.Add((T("Preço"), v > 0 ? _f.Dinheiro(v) : "–"));
                }
                // "Salario pretendido" com o Comprar ligado (o salario na compradora, 3:7ff4)
                if (c != null && _f._preco > 0 && _f.PodeComprarAqui())
                    antes.Add((T("Salário pretendido"), FormatoDinheiro.Valor(s.S, s.Mercado.Salario(p, Ext.Bytes(c.B79)))));
                var corpo = new StackPanel { Spacing = 12 };
                corpo.Children.Add(CUi.Cartao_(null, MercadoCelular.Cabecalho(p, e, Texto(e.NomeCurto))));
                corpo.Children.Add(MercadoCelular.Ficha(p, launcher, antes, TextosMercado.TextoValorMercado(s, p)));
                var bs = new List<Control>();
                if (c != null)
                {
                    bool obs = MercadoJogo.Observado(s.S, c.Treinador, p.ID);
                    bs.Add(new CUi.Botao(obs ? "✓ " + T("A observar") : T("Observar"), false, () =>
                    {
                        _f.Observar();
                        Refazer();
                    }) { Habilitado = _f._observadoLigado });
                }
                if (_f.PodeComprarAqui())
                    bs.Add(new CUi.Botao(T("Comprar"), true, () =>
                    {
                        FecharFicha();
                        _f.Comprar();
                        Refazer();
                    }) { Habilitado = _f._preco > 0 });
                var folha = new CUi.Folha(TM.NomeJogador(p), corpo, bs.Count > 0 ? CUi.Botoes(bs.ToArray()) : null, Texto(e.NomeCompleto), fechar: () => FecharFicha());
                var antiga = _fecharFicha;
                _fecharFicha = null;
                antiga?.Invoke();
                _fichaDe = p.ID;
                _fecharFicha = MercadoCelular.Abrir(s, folha);
            }

            void FecharFicha()
            {
                var f = _fecharFicha;
                _fecharFicha = null;
                _fichaDe = 0;
                f?.Invoke();
            }

            // ------------------------------------------------------------ trocar de equipa
            // a caixa de cima (ComboCurrentTeamChange): todas as equipas pelo nome curto
            void EscolherEquipa()
            {
                var s = _f.S;
                var atual = _f.Mostrada();
                var linhas = new List<Control>();
                var equipas = _f._nomes.Select(n => s.S.Equipas.FirstOrDefault(x => Texto(x.NomeCurto) == n)).ToList();
                for (int i = 0; i < _f._nomes.Count; i++)
                    linhas.Add(equipas[i] is { } eq ? CUi.NomeEquipa(eq, _f._nomes[i], 15) : CUi.Txt(_f._nomes[i], 15));
                Action fechar = () => { };
                var (lista, marcar) = CUi.ListaEscolha(linhas, i =>
                {
                    fechar();
                    _f.EscolherEquipa(i);
                    Refazer();
                });
                int k = atual == null ? -1 : _f._nomes.IndexOf(Texto(atual.NomeCurto));
                if (k >= 0) marcar(k);
                fechar = MercadoCelular.Abrir(s, new CUi.Folha(T("Trocar de equipa"), lista, null, fechar: () => fechar()));
            }

            // ------------------------------------------------------------ o Retro
            // A TPlantelDlg do computador em pe, nas cores da equipa: a bandeira do pais, a
            // caixa da equipa (o toque troca), a divisao e o medidor do moral; o Balanco e o
            // Calendario com os glifos; a lista afundada na cor da equipa (P, nome, F, S, L, a
            // nacionalidade e o preco, em negrito, o traco no fim de cada posicao) e embaixo
            // Procurar, Emprestimo, Lista e Fechar. O toque num jogador escolhe a linha (o
            // realce azul) e abre a ficha (o Observado e o Comprar com os glifos)
            Control CorpoRetro(SEquipa e)
            {
                var s = _f.S;
                var (letra, fundo) = Cores(e);
                var p = new StackPanel { Spacing = 8 };
                var cab = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,10,*") };
                if (CUi.Bandeira(PaisDe(e.Pais), 44) is { } band) cab.Children.Add(band);
                var combo = MercadoRetro.Combo(MercadoRetro.Txt(Texto(e.NomeCurto), 15, W95.Preto, true), EscolherEquipa, 42);
                Grid.SetColumn(combo, 2); cab.Children.Add(combo);
                p.Children.Add(cab);
                var linha2 = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*") };
                int dv = s.J.M.DivisaoDa(e);
                if (dv >= 0 && dv < s.S.Divisoes.Count)
                    linha2.Children.Add(MercadoRetro.Txt(dv >= 4 ? T("Distrital") : Pascal(s.S.Divisoes[dv].Nome, 20), 15, letra));
                var moral = MercadoRetro.Moral(e, 14);
                Grid.SetColumn(moral, 2); linha2.Children.Add(moral);
                p.Children.Add(linha2);
                // o Balanco e o Calendario (em cima, a direita, no computador)
                var cima = new List<Control>();
                if (Balanco != null)
                    cima.Add(MercadoRetro.Botao(T("Balanço"), "Mercado/TPlantelDlg.balanceBtn.png", 2, () => { if (_f.Mostrada() is { } m) Balanco?.Invoke(s, m, Refazer); }));
                var cal = MercadoRetro.Botao(T("Calendário"), "Mercado/TPlantelDlg.calendarBtn.png", 2, () => { if (_f.Mostrada() is { } m) Calendario?.Invoke(s, m, Refazer); });
                cal.Habilitado = Calendario != null;
                cima.Add(cal);
                p.Children.Add(MercadoRetro.Fila(cima.ToArray()));

                // a lista (ListBoxOfPlayersDrawItem) na cor da equipa
                bool comPreco = _f.Compradora() != null && !_f.Humana(e);
                var lista = new StackPanel();
                var js = e.Jogadores;
                for (int i = 0; i < js.Count; i++)
                {
                    bool fim = i < js.Count - 1 && Math.Min((int)js[i].Posicao, 3) != Math.Min((int)js[i + 1].Posicao, 3);
                    var l = LinhaRetro(e, i, comPreco, letra);
                    lista.Children.Add(new Border { Child = l, BorderBrush = letra, BorderThickness = new Thickness(0, 0, 0, fim ? 1 : 0), Margin = new Thickness(0, 0, 0, fim ? 3 : 0) });
                }
                p.Children.Add(MercadoRetro.Lista(lista, fundo));
                return p;
            }

            Control LinhaRetro(SEquipa e, int i, bool comPreco, IBrush letra)
            {
                var j = e.Jogadores[i];
                bool sel = j.ID == _f._sel;
                var cor = sel ? W95.Branco : letra;
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions(comPreco ? "20,*,26,16,16,44,96" : "20,*,26,16,16,44") };
                MercadoRetro.Col(g, 0, Idioma.Posicao(Math.Min((int)j.Posicao, 3)), cor, true);
                MercadoRetro.Col(g, 1, NomeJogador(j), cor, true);
                MercadoRetro.Col(g, 2, j.Forca.ToString(), cor, true, 14, true);
                MercadoRetro.Col(g, 3, j.Suspensao != 0 ? Abrev("coluna: S", " S").Trim() : "", cor, true, 14, true);
                MercadoRetro.Col(g, 4, j.JogosLesao != 0 ? Abrev("coluna: L", " L").Trim() : "", cor, true, 14, true);
                MercadoRetro.Col(g, 5, Nacional(j), cor, true, 13).Margin = new Thickness(6, 0, 0, 0);
                if (comPreco && _f.Compradora() is { } comp)
                {
                    int v = _f.S.Mercado.PrecoDeVenda(e, comp, j, MercadoJogo.ForcaMedia(e));
                    MercadoRetro.Col(g, 6, v > 0 ? _f.Dinheiro(v) : "", cor, true, 13, true);
                }
                int k = i;
                return MercadoRetro.LinhaLista(g, sel, () => Tocar(k), 38);
            }

            // os botoes de baixo (Procurar, Emprestimo, Lista e Fechar, nas condicoes do Form)
            Control RodapeRetro()
            {
                var s = _f.S;
                var bs = new List<Control>();
                if (_f.Compradora() is { } c)
                {
                    bs.Add(MercadoRetro.Botao(T("Procurar"), "Mercado/TPlantelDlg.findBtn.png", 1, () => { if (_f.Compradora() is { } c2) TelaProcura.Abrir(s, c2, _f.MostrarJogador); }));
                    if (_f.PodeComprarAqui() && Emprestimo != null)
                        bs.Add(MercadoRetro.Botao(T("Empréstimo"), "Mercado/TPlantelDlg.loanBtn.png", 2, () => { if (_f.Compradora() is { } c2) Emprestimo?.Invoke(s, c2, () => { _f.AtualizarCompra(); Refazer(); }); }));
                    if (_f.Humana(c))
                        bs.Add(MercadoRetro.Botao(T("Lista"), "Mercado/TPlantelDlg.observedPlayersBtn.png", 2, () => { if (_f.Compradora() is { } c2) TelaObservados.Abrir(s, c2, _f.MostrarJogador); }));
                }
                bs.Add(MercadoRetro.Botao(T("Fechar"), "Mercado/TPlantelDlg.OKBtn.png", 2, _f.Fechar, true, 36));
                return MercadoRetro.Fila(bs.ToArray());
            }

            // a ficha (PanelPlayer, GroupBox1) numa folha nas cores da equipa: o jogador, o
            // preco, o Comportamento, os gols, o Historico, o suspenso / lesionado e o "Salario
            // pretendido"; o Observado (premido se ja observado) e o Comprar
            void AbrirFichaRetro(SJogador p)
            {
                var s = _f.S;
                var e = _f.Mostrada();
                if (e == null) return;
                var (letra, fundo) = Cores(e);
                bool launcher = s.J.M.Regras.CartoesAmarelos;
                var corpo = new StackPanel { Spacing = 6 };
                var cab = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                if (CUi.Bandeira(TM.PaisDe(p.Pais), 26) is { } b) cab.Children.Add(b);
                cab.Children.Add(MercadoRetro.Txt(TM.NomeJogador(p), 18, letra, true));
                cab.Children.Add(MercadoRetro.Txt(TM.Posicao(p) + " · " + p.Forca, 15, letra));
                corpo.Children.Add(cab);
                var nac = Nacional(p);
                if (nac.Length > 0) corpo.Children.Add(MercadoRetro.Linha(T("Nacionalidade"), nac.StartsWith("* ") ? NomePais(nac[2..]) + " *" : NomePais(nac), letra, 170, 14, false));
                var c = _f.Compradora();
                if (c != null && !_f.Humana(e))
                {
                    int v = s.Mercado.PrecoDeVenda(e, c, p, MercadoJogo.ForcaMedia(e));
                    corpo.Children.Add(MercadoRetro.Linha(T("Preço"), v > 0 ? _f.Dinheiro(v) : "–", letra, 170, 14, true));
                }
                corpo.Children.Add(MercadoRetro.Ficha(p, launcher, fundo, letra, TextosMercado.TextoValorMercado(s, p)));
                MercadoRetro.Avisos(corpo, p, letra);
                if (c != null && _f._preco > 0 && _f.PodeComprarAqui())
                    corpo.Children.Add(MercadoRetro.Linha(T("Salário pretendido:"), FormatoDinheiro.Valor(s.S, s.Mercado.Salario(p, Ext.Bytes(c.B79))), letra, 170, 14, true));
                var bs = new List<Control>();
                if (c != null)
                {
                    var obs = MercadoRetro.Botao(T("Observado"), "Mercado/TPlantelDlg.SpeedButtonObserved.png", 2, () => { _f.Observar(); Refazer(); });
                    obs.Habilitado = _f._observadoLigado;
                    MercadoRetro.Baixo(obs, _f._observadoLigado && MercadoJogo.Observado(s.S, c.Treinador, p.ID));
                    bs.Add(obs);
                }
                if (_f.PodeComprarAqui())
                {
                    var comprar = MercadoRetro.Botao(T("Comprar"), "Mercado/TPlantelDlg.buyBtn.png", 2, () =>
                    {
                        FecharFicha();
                        _f.Comprar();
                        Refazer();
                    });
                    comprar.Habilitado = _f._preco > 0;
                    bs.Add(comprar);
                }
                var folha = new CUi.Folha(TM.NomeJogador(p), corpo, bs.Count > 0 ? MercadoRetro.Fila(bs.ToArray()) : null, Texto(e.NomeCompleto),
                    CUi.Bandeira(PaisDe(e.Pais), 30), fechar: () => FecharFicha());
                MercadoRetro.Pintar(folha, fundo);
                var antiga = _fecharFicha;
                _fecharFicha = null;
                antiga?.Invoke();
                _fichaDe = p.ID;
                _fecharFicha = MercadoCelular.Abrir(s, folha);
            }
        }
    }
}
