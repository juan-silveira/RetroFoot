using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RetroFoot.Motor.Mercado;
using TM = RetroFoot.App.TextosMercado;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// A Lista de Jogadores no celular: a tela inteira com o nome a procurar em cima, os criterios
// em pilulas (cada uma abre a escolha numa folha), o "nao mostrar os do meu time", o Procurar
// embaixo e a lista do que foi achado (o preco e a equipa de cada um; aos 50, "Mostrar mais").
// O toque num jogador abre a ficha com o Equipa... (e o Comprar do launcher). Os criterios, a
// busca e os botoes sao os do Form (TelaProcura.cs), guardados de uma vez para a outra.
public static partial class TelaProcura
{
    sealed partial class Form
    {
        protected override Control? VersaoCelular() => new ProcuraCelular(this);

        sealed class ProcuraCelular : UserControl
        {
            const int Pagina = 50;
            readonly Form _f;
            readonly WrapPanel _filtros = new() { ItemSpacing = 8, LineSpacing = 8 };
            readonly StackPanel _resultados = new() { Spacing = 8 };
            CUi.Botao _botao;
            // (o Retro: os criterios refeitos a cada troca, o Time... e o Comprar do launcher)
            readonly ContentControl _criterios = new();
            CUi.Botao? _equipaR, _comprarR;
            int _mostrar = Pagina;
            Action? _fecharFolha;

            public ProcuraCelular(Form f)
            {
                _f = f;
                var corpo = new StackPanel { Spacing = 12 };
                // o nome (TEdit, 20 letras, maiusculas)
                var nome = CUi.Retro ? MercadoRetro.Edit(_nome, 20, 16) : CUi.Caixa(_nome, 20, T("Nome"));
                bool trocando = false;
                nome.TextChanged += (_, _) =>
                {
                    if (trocando) return;
                    var t = nome.Text ?? "";
                    var m = t.ToUpperInvariant();
                    if (m != t) { trocando = true; int c = nome.CaretIndex; nome.Text = m; nome.CaretIndex = c; trocando = false; }
                    // textNameChange (10:39b1)
                    if (m == _nome) return;
                    _nome = m;
                    _procurar = true;
                    Atualizar();
                };
                nome.KeyDown += (_, e) => { if (e.Key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Return && _procurar) { e.Handled = true; Procurar(); } };
                if (CUi.Retro)
                {
                    _botao = null!;
                    ConstruirRetro(nome);
                    return;
                }
                corpo.Children.Add(nome);
                corpo.Children.Add(_filtros);
                corpo.Children.Add(CUi.Grupo(new Control[]
                {
                    CUi.Interruptor(T("Não mostrar os jogadores do meu time"), null, _naoMinha, v => { if (v != _naoMinha) { _f.TrocarNaoMinha(); Atualizar(); } }),
                    MercadoCelular.Linha(T("Dinheiro em caixa"), _f.Dinheiro((int)_f._e.Dinheiro)),
                }));
                corpo.Children.Add(_resultados);
                _botao = new CUi.Botao(T("Procurar"), true, Procurar) { HorizontalAlignment = HorizontalAlignment.Stretch };
                Content = new CUi.Folha(T("Lista de Jogadores"), corpo, _botao, null, inteira: true, fechar: Fechar);
                Atualizar();
                Lista();
            }

            void Fechar()
            {
                FecharFolha();
                _treinador = _f._e.Treinador;
                _f.Fechar();
            }

            void Procurar()
            {
                if (!_procurar) return;
                _f.Procurar();
                _mostrar = Pagina;
                Atualizar();
                Lista();
            }

            // ------------------------------------------------------------ os criterios
            void Atualizar()
            {
                _botao.Habilitado = _procurar;
                if (CUi.Retro) { AtualizarRetro(); return; }
                _filtros.Children.Clear();
                string forca = _f._porFaixas
                    ? (_faixa >= 0 && _faixa < Faixas.Length ? Faixas[_faixa].Trim() : "")
                    : _forcaMin > 0 && _forcaMax > 0 ? _forcaMin + " – " + _forcaMax
                    : _forcaMin > 0 ? "≥ " + _forcaMin : _forcaMax > 0 ? "≤ " + _forcaMax : "";
                Pilula(T("Força"), forca, EscolherForca);
                Pilula(T("Posição"), _posicao >= 0 && _posicao < Posicoes.Length ? T(Posicoes[_posicao]) : "", () =>
                    Escolher(T("Posição"), Posicoes.Select(x => T(x)).ToList(), _posicao, true, i => _posicao = i));
                Pilula(T("Nacionalidade"), _pais >= 0 && _pais < _f._paises.Count ? _f._paises[_pais].Nome : "", EscolherPais);
                Pilula(T("Preço"), _preco >= 0 && _preco < _f._precos.Length ? _f._precos[_preco].Trim() : "", () =>
                    Escolher(T("Preço"), _f._precos.Select(x => x.Trim()).ToList(), _preco, true, i => _preco = i));
                if (_f._launcher)
                {
                    Pilula(T("Comportamento"), _comp > 0 ? T(ComportamentosLauncher[_comp]) : "", () =>
                        Escolher(T("Comportamento"), ComportamentosLauncher.Select(x => T(x)).ToList(), _comp, false, i => _comp = i));
                    Pilula(T("Nota"), _nota > 0 ? NotasLauncher[_nota] : "", () =>
                        Escolher(T("Nota"), NotasLauncher, _nota, false, i => _nota = i));
                    Pilula(T("Risco de lesão"), _risco > 0 ? RiscosLauncher[_risco] : "", () =>
                        Escolher(T("Risco de lesão"), RiscosLauncher, _risco, false, i => _risco = i));
                }
            }

            // uma pilula de criterio: o nome e, se escolhido, o valor (em destaque)
            void Pilula(string rotulo, string valor, Action abrir)
            {
                bool m = valor.Length > 0;
                var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                s.Children.Add(CUi.Txt(m ? rotulo + ":" : rotulo, 14, m && CUi.Retro ? W95.Branco : CUi.Texto, m ? FontWeight.Normal : FontWeight.SemiBold));
                if (m) s.Children.Add(CUi.Txt(valor, 14, CUi.Retro ? W95.Branco : TemaVerde.Destaque, FontWeight.Bold));
                s.Children.Add(CUi.Txt("▾", 12, m && CUi.Retro ? W95.Branco : CUi.Fraco));
                var c = new Clicavel
                {
                    Child = s, MinHeight = 42, Padding = new Thickness(14, 6), CorFoco = null, MaxWidth = 360,
                    CornerRadius = new CornerRadius(CUi.Retro ? 0 : 21), BorderThickness = new Thickness(CUi.Retro ? 1 : 1.5),
                    BorderBrush = CUi.Retro ? W95.Preto : m ? TemaVerde.Destaque : TemaVerde.Borda,
                    Background = CUi.Retro ? (m ? W95.Marinho : W95.Face) : (m ? TemaVerde.Selecao : TemaVerde.BotaoSecundario),
                };
                c.Clique += abrir;
                _filtros.Children.Add(c);
            }

            // escolhe um item (limpar: a primeira linha e o X do original, sem criterio = -1)
            void Escolher(string titulo, IReadOnlyList<string> itens, int atual, bool limpar, Action<int> escolheu)
            {
                var linhas = new List<Control>();
                if (limpar) linhas.Add(CUi.Txt(T("Sem filtro"), 15, CUi.Fraco, FontWeight.SemiBold));
                foreach (var t in itens) linhas.Add(CUi.Txt(t, 15));
                int d = limpar ? 1 : 0;
                var (lista, marcar) = CUi.ListaEscolha(linhas, i =>
                {
                    FecharFolha();
                    escolheu(i - d);
                    _f.Alterado();
                    Atualizar();
                });
                marcar(atual + d);
                _fecharFolha = MercadoCelular.Abrir(_f.S, new CUi.Folha(titulo, lista, null, fechar: FecharFolha));
            }

            // a forca: as faixas do original (Configuracoes) ou o minimo e o maximo (0 = sem limite)
            void EscolherForca()
            {
                if (_f._porFaixas)
                {
                    Escolher(T("Força"), Faixas.Select(x => x.Trim()).ToList(), _faixa, true, i => _faixa = i);
                    return;
                }
                string Txt(double v) => v < 1 ? T("Sem limite") : ((int)v).ToString();
                var corpo = new StackPanel { Spacing = 14 };
                corpo.Children.Add(CUi.Deslizador(T("Mínimo"), 0, ForcaMaxima, Math.Max(0, _forcaMin), Txt, v => { _forcaMin = (int)v; _f.Alterado(); Atualizar(); }));
                corpo.Children.Add(CUi.Deslizador(T("Máximo"), 0, ForcaMaxima, Math.Max(0, _forcaMax), Txt, v => { _forcaMax = (int)v; _f.Alterado(); Atualizar(); }));
                var limpar = new CUi.Botao(T("Sem filtro"), false, () => { FecharFolha(); _forcaMin = _forcaMax = _faixa = -1; _f.Alterado(); Atualizar(); });
                var ok = new CUi.Botao(T("OK"), true, FecharFolha);
                _fecharFolha = MercadoCelular.Abrir(_f.S, new CUi.Folha(T("Força"), corpo, CUi.Botoes(limpar, ok), fechar: FecharFolha));
            }

            // a nacionalidade: a bandeira e o nome; o pais do meu time (10:3930) em primeiro
            void EscolherPais()
            {
                var corpo = new StackPanel { Spacing = 10 };
                var busca = CUi.Caixa("", 30, T("Procurar"));
                var lista = new ContentControl();
                corpo.Children.Add(busca);
                corpo.Children.Add(lista);
                void Encher()
                {
                    var q = (busca.Text ?? "").Trim();
                    var linhas = new List<Control>();
                    var ks = new List<int>();
                    if (q.Length == 0)
                    {
                        linhas.Add(CUi.Txt(T("Sem filtro"), 15, CUi.Fraco, FontWeight.SemiBold)); ks.Add(-1);
                        var cod = PaisDe(_f._e.Pais);
                        int meu = _f._paises.FindIndex(p => p.Cod == cod);
                        if (meu >= 0) { linhas.Add(Pais(meu, T("País da minha equipa"))); ks.Add(-2); }
                    }
                    for (int i = 0; i < _f._paises.Count; i++)
                        if (q.Length == 0 || _f._paises[i].Nome.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                        {
                            linhas.Add(Pais(i, null)); ks.Add(i);
                        }
                    var (l, marcar) = CUi.ListaEscolha(linhas, i =>
                    {
                        FecharFolha();
                        int k = ks[i];
                        if (k == -2)
                        {
                            var cod = PaisDe(_f._e.Pais);
                            k = _f._paises.FindIndex(p => p.Cod == cod);
                            if (k < 0) return;
                        }
                        _pais = k;
                        _f.Alterado();
                        Atualizar();
                    });
                    int at = ks.IndexOf(_pais);
                    if (at >= 0) marcar(at);
                    lista.Content = l;
                }
                busca.TextChanged += (_, _) => Encher();
                Encher();
                _fecharFolha = MercadoCelular.Abrir(_f.S, new CUi.Folha(T("Nacionalidade"), corpo, null, fechar: FecharFolha));
            }

            Control Pais(int i, string? texto)
            {
                var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                if (CUi.Bandeira(_f._paises[i].Cod, 24) is { } b) s.Children.Add(b);
                else s.Children.Add(new Border { Width = 24 });
                s.Children.Add(CUi.Txt(texto ?? _f._paises[i].Nome, 15, null, texto != null ? FontWeight.SemiBold : FontWeight.Normal));
                if (texto != null) s.Children.Add(CUi.Txt("· " + _f._paises[i].Nome, 15, CUi.Fraco));
                return s;
            }

            void FecharFolha()
            {
                var f = _fecharFolha;
                _fecharFolha = null;
                f?.Invoke();
            }

            // ------------------------------------------------------------ o que foi achado
            void Lista()
            {
                if (CUi.Retro) { ListaRetro(); return; }
                _resultados.Children.Clear();
                var s = _f.S;
                var achados = _achados;
                var t = CUi.TituloSecao_(T("{0} jogadores", achados.Count));
                t.Margin = new Thickness(4, 4, 0, 0);
                _resultados.Children.Add(t);
                if (achados.Count == 0)
                {
                    // (a busca ja feita com estes criterios: nada; senao, como procurar)
                    _resultados.Children.Add(CUi.Cartao_(null, CUi.Txt(_procurar ? T("Escolha os critérios e toque em Procurar.") : T("Nenhum jogador encontrado."), 14.5, CUi.Fraco, FontWeight.Normal, true)));
                    return;
                }
                var linhas = new List<Control>();
                int n = Math.Min(_mostrar, achados.Count);
                for (int i = 0; i < n; i++)
                {
                    var (eq, k) = MercadoJogo.EquipaDoJogador(s.S, achados[i]);
                    if (eq == null) continue;
                    var p = eq.Jogadores[k];
                    int v = s.Mercado.PrecoDeVenda(eq, _f._e, p, MercadoJogo.ForcaMedia(eq));
                    string baixo = Texto(eq.NomeCurto);
                    if (_f._launcher) baixo += " · " + T("Nota") + " " + p.Nota + " · RL " + p.Lesao;
                    int ii = i;
                    linhas.Add(CUi.Toque(MercadoCelular.Jogador(p, MercadoCelular.Selos(p), MercadoCelular.Extra(v > 0 ? _f.Dinheiro(v) : "", baixo)), () => Tocar(ii), 48));
                }
                _resultados.Children.Add(CUi.Grupo(linhas));
                if (n < achados.Count)
                    _resultados.Children.Add(MercadoCelular.Largo(T("Mostrar mais ({0})", achados.Count - n), false, () => { _mostrar += Pagina; Lista(); }));
            }

            // a ficha do jogador achado: o Equipa... e o Comprar (launcher)
            void Tocar(int i)
            {
                if (i < 0 || i >= _achados.Count) return;
                _f._lista.Sel = i;
                var p = _f.Escolhido(out var eq);
                if (p == null || eq == null) return;
                var s = _f.S;
                int v = s.Mercado.PrecoDeVenda(eq, _f._e, p, MercadoJogo.ForcaMedia(eq));
                var corpo = new StackPanel { Spacing = 12 };
                corpo.Children.Add(CUi.Cartao_(null, MercadoCelular.Cabecalho(p, eq, Texto(eq.NomeCurto))));
                corpo.Children.Add(MercadoCelular.Ficha(p, _f._launcher, new[] { (T("Preço"), v > 0 ? _f.Dinheiro(v) : "–") }));
                var bs = new List<Control>();
                if (_f._launcher)
                    bs.Add(new CUi.Botao(T("Comprar"), false, () =>
                    {
                        FecharFolha();
                        _f._lista.Sel = i;
                        _f.Comprar();
                        Lista();
                    }) { Habilitado = _f.PrecoDoEscolhido() > 0 });
                bs.Add(new CUi.Botao(T("Time..."), true, () =>
                {
                    FecharFolha();
                    _f._lista.Sel = i;
                    _f.Equipa();
                }));
                FecharFolha();
                _fecharFolha = MercadoCelular.Abrir(s, new CUi.Folha(TM.NomeJogador(p), corpo, CUi.Botoes(bs.ToArray()), Texto(eq.NomeCompleto), fechar: FecharFolha));
            }

            // ------------------------------------------------------------ o Retro
            // A janela amarela do computador (TcheckPlayerDlg) em pe: os criterios em linhas
            // (o rotulo, a caixa de escolha e os botoes pequenos de limpar), a caixa de marcar,
            // o dinheiro em caixa em azul, a lista branca afundada e Procurar, Time... (Comprar
            // no launcher) e Fechar com os glifos. O toque escolhe a linha (o realce azul); na
            // escolhida, e o duplo clique do original (o Time...)
            void ConstruirRetro(TextBox nome)
            {
                var corpo = new StackPanel { Spacing = 8 };
                corpo.Children.Add(Criterio(T("Nome"), nome, Pequeno("clearNameBtn", () => nome.Text = "")));
                corpo.Children.Add(_criterios);
                corpo.Children.Add(_resultados);
                _botao = MercadoRetro.Botao(T("Procurar"), "Mercado/TcheckPlayerDlg.findBtn.png", 1, Procurar, true);
                _equipaR = MercadoRetro.Botao(T("Time..."), "Mercado/TcheckPlayerDlg.teamBtn.png", 2, () => _f.Equipa());
                var fechar = MercadoRetro.Botao(T("Fechar"), "Mercado/TcheckPlayerDlg.OKBtn.png", 2, Fechar, false, 36);
                var bs = new List<Control> { _botao, _equipaR };
                if (_f._launcher)
                {
                    _comprarR = MercadoRetro.Botao(T("Comprar"), "Mercado/TPlantelDlg.buyBtn.png", 2, () => { _f.Comprar(); Lista(); });
                    bs.Add(_comprarR);
                }
                bs.Add(fechar);
                var folha = new CUi.Folha(T("Lista de Jogadores"), corpo, MercadoRetro.Fila(bs.ToArray()), null, inteira: true, fechar: Fechar);
                MercadoRetro.Pintar(folha, W95.Amarelo);
                Content = folha;
                Atualizar();
                Lista();
            }

            // uma linha de criterio: o rotulo (preto sobre o amarelo), a caixa e os botoes
            static Control Criterio(string rotulo, Control caixa, params Control[] botoes)
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("104,*") };
                g.Children.Add(MercadoRetro.Txt(rotulo, 14, W95.Preto, false, true));
                Grid.SetColumn(caixa, 1); g.Children.Add(caixa);
                foreach (var b in botoes)
                {
                    g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                    b.Margin = new Thickness(6, 0, 0, 0);
                    b.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(b, g.ColumnDefinitions.Count - 1);
                    g.Children.Add(b);
                }
                return g;
            }

            CUi.Botao Pequeno(string nome, Action clique) => MercadoRetro.Pequeno("Mercado/TcheckPlayerDlg." + nome + ".png", 2, () =>
            {
                clique();
                _f.Alterado();
                Atualizar();
            });

            static Clicavel Combo(string texto, Action abrir) => MercadoRetro.Combo(MercadoRetro.Txt(texto, 14.5, W95.Preto), abrir);

            void AtualizarRetro()
            {
                var p = new StackPanel { Spacing = 8 };
                // a forca: as faixas (uma caixa) ou o minimo e o maximo (o vazio: sem limite)
                Control forca;
                if (_f._porFaixas)
                    forca = Combo(_faixa >= 0 && _faixa < Faixas.Length ? Faixas[_faixa].Trim() : "", () =>
                        Escolher(T("Força"), Faixas.Select(x => x.Trim()).ToList(), _faixa, true, i => _faixa = i));
                else
                {
                    var um = Enumerable.Range(1, ForcaMaxima).Select(x => x.ToString()).ToList();
                    var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,18,*") };
                    g.Children.Add(Combo(_forcaMin > 0 ? _forcaMin.ToString() : "", () =>
                        Escolher(T("Mínimo"), um, _forcaMin > 0 ? _forcaMin - 1 : -1, true, i => _forcaMin = i >= 0 ? i + 1 : -1)));
                    var traco = MercadoRetro.Txt("–", 14, W95.Preto);
                    traco.HorizontalAlignment = HorizontalAlignment.Center;
                    Grid.SetColumn(traco, 1); g.Children.Add(traco);
                    var max = Combo(_forcaMax > 0 ? _forcaMax.ToString() : "", () =>
                        Escolher(T("Máximo"), um, _forcaMax > 0 ? _forcaMax - 1 : -1, true, i => _forcaMax = i >= 0 ? i + 1 : -1));
                    Grid.SetColumn(max, 2); g.Children.Add(max);
                    forca = g;
                }
                p.Children.Add(Criterio(T("Força"), forca, Pequeno("clearSkillBtn", () => _forcaMin = _forcaMax = _faixa = -1)));
                p.Children.Add(Criterio(T("Posição"), Combo(_posicao >= 0 && _posicao < Posicoes.Length ? T(Posicoes[_posicao]) : "", () =>
                    Escolher(T("Posição"), Posicoes.Select(x => T(x)).ToList(), _posicao, true, i => _posicao = i)),
                    Pequeno("clearPositionBtn", () => _posicao = -1)));
                // a nacionalidade: a bandeira e o nome (comboCountryDrawItem), o X e o pais do time
                var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                if (_pais >= 0 && _pais < _f._paises.Count)
                {
                    if (CUi.Bandeira(_f._paises[_pais].Cod, 26) is { } b) sp.Children.Add(b);
                    sp.Children.Add(MercadoRetro.Txt(_f._paises[_pais].Nome, 14.5, W95.Preto));
                }
                p.Children.Add(Criterio(T("Nacionalidade"), MercadoRetro.Combo(sp, EscolherPais, 44),
                    Pequeno("clearCountryBtn", () => _pais = -1),
                    Pequeno("myCountryBtn", () =>
                    {
                        // 10:3930: o pais da equipa
                        var cod = PaisDe(_f._e.Pais);
                        int k = _f._paises.FindIndex(x => x.Cod == cod);
                        if (k >= 0) _pais = k;
                    })));
                p.Children.Add(Criterio(T("Preço"), Combo(_preco >= 0 && _preco < _f._precos.Length ? _f._precos[_preco].Trim() : "", () =>
                    Escolher(T("Preço"), _f._precos.Select(x => x.Trim()).ToList(), _preco, true, i => _preco = i)),
                    Pequeno("clearPriceBtn", () => _preco = -1)));
                if (_f._launcher)
                {
                    p.Children.Add(Criterio(T("Comportamento"), Combo(T(ComportamentosLauncher[Math.Clamp(_comp, 0, ComportamentosLauncher.Length - 1)]), () =>
                        Escolher(T("Comportamento"), ComportamentosLauncher.Select(x => T(x)).ToList(), _comp, false, i => _comp = i)),
                        Pequeno("clearPositionBtn", () => _comp = 0)));
                    p.Children.Add(Criterio(T("Nota"), Combo(NotasLauncher[Math.Clamp(_nota, 0, NotasLauncher.Length - 1)], () =>
                        Escolher(T("Nota"), NotasLauncher, _nota, false, i => _nota = i)),
                        Pequeno("clearSkillBtn", () => _nota = 0)));
                    p.Children.Add(Criterio(T("Risco de lesão"), Combo(RiscosLauncher[Math.Clamp(_risco, 0, RiscosLauncher.Length - 1)], () =>
                        Escolher(T("Risco de lesão"), RiscosLauncher, _risco, false, i => _risco = i)),
                        Pequeno("clearPositionBtn", () => _risco = 0)));
                }
                p.Children.Add(MercadoRetro.Marcar(T("Não mostrar os jogadores do meu time"), _naoMinha, W95.Preto, () => { _f.TrocarNaoMinha(); Atualizar(); }));
                // "Dinheiro em caixa" em azul (clBlue), como no computador
                var din = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                din.Children.Add(MercadoRetro.Txt(T("Dinheiro em caixa"), 14, W95.Azul));
                din.Children.Add(MercadoRetro.Txt(_f.Dinheiro((int)_f._e.Dinheiro), 14, W95.Azul, true));
                p.Children.Add(din);
                _criterios.Content = p;
            }

            void ListaRetro()
            {
                _resultados.Children.Clear();
                var s = _f.S;
                var achados = _achados;
                var linhas = new StackPanel();
                int n = Math.Min(_mostrar, achados.Count);
                for (int i = 0; i < n; i++)
                {
                    var (eq, k) = MercadoJogo.EquipaDoJogador(s.S, achados[i]);
                    if (eq == null) continue;
                    var p = eq.Jogadores[k];
                    bool sel = i == _f._lista.Sel;
                    var cor = sel ? W95.Branco : W95.Preto;
                    int v = s.Mercado.PrecoDeVenda(eq, _f._e, p, MercadoJogo.ForcaMedia(eq));
                    var b = new List<(string, double, bool)> { (PaisDe(p.Pais), 40, false), (ComportamentoCurto(p), 36, false) };
                    // (launcher: as colunas Nota e RL)
                    if (_f._launcher) { b.Add((p.Nota.ToString(), 24, true)); b.Add((p.Lesao.ToString(), 24, true)); }
                    var c = MercadoRetro.Jogador2(cor, Idioma.Posicao(Math.Min((int)p.Posicao, 3)), TM.NomeJogador(p),
                        new[] { (p.Forca.ToString(), 24.0, true), (v > 0 ? _f.Dinheiro(v) : "", 100, true) }, Texto(eq.NomeCurto), b.ToArray());
                    int ii = i;
                    linhas.Children.Add(MercadoRetro.LinhaLista(c, sel, () => TocarRetro(ii), 48));
                }
                _resultados.Children.Add(MercadoRetro.Lista(new Border { MinHeight = 220, Child = linhas }, W95.Branco));
                if (n < achados.Count)
                    _resultados.Children.Add(MercadoCelular.Largo(T("Mostrar mais ({0})", achados.Count - n), false, () => { _mostrar += Pagina; Lista(); }));
                if (_equipaR != null) _equipaR.Habilitado = _f.Escolhido() != null;
                if (_comprarR != null) _comprarR.Habilitado = _f.PrecoDoEscolhido() > 0;
            }

            void TocarRetro(int i)
            {
                if (i < 0 || i >= _achados.Count) return;
                if (i == _f._lista.Sel) { _f.Equipa(); return; }
                _f._lista.Sel = i;
                Lista();
            }
        }
    }
}
