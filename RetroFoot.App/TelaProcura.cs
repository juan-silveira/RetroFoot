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

// A Lista de Jogadores do menu Jogador > Procurar: a TcheckPlayerDlg do jogo classico (seg10:
// 29f0, Run), com os criterios (nome, posicao, nacionalidade, forca, preco e "nao mostrar
// os jogadores da minha equipa") e a lista do que foi achado. "Equipa..." (ou o duplo
// clique) fecha a janela e abre o plantel da equipa do jogador.
//
// Com as regras do launcher (Regras.CartoesAmarelos), o que as EXEs do launcher acrescentam:
// os filtros de Comportamento, Nota e Risco de lesao (tools/divisoes/filtros.py), as colunas
// Nota e RL na lista, com a janela 50 pontos mais larga (procura.py) e o botao Comprar
// (comprar.py: compra direta do escolhido, sem confirmar; a linha fica sem preco).
//
// Como no original, a janela guarda os criterios e a lista de uma vez para a outra enquanto
// o treinador for o mesmo.
public static class TelaProcura
{
    // Abre a Lista de Jogadores para a equipa humana e (na thread da interface, com o motor
    // parado: da TelaEquipa). escolher recebe o jogador do "Equipa..." (null = abre o
    // plantel da equipa dele, com e como compradora); aoFechar, quando a janela fecha (o
    // dinheiro da equipa pode ter mudado com o Comprar do launcher).
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

    // RETROFOOT_TELA=procura|plantel|observados: o jogo de teste, com a janela aberta por cima
    // da tela da equipa (RETROFOOT_LAUNCHER=1: com as regras do launcher)
    public static Control Demo() => Environment.GetEnvironmentVariable("RETROFOOT_TELA") switch
    {
        "plantel" => TelaPlantel.Demo(),
        "observados" => TelaObservados.Demo(),
        _ => MercadoForm.Demo((s, e) => Abrir(s, e)),
    };

    // ------------------------------------------------------------ o estado guardado
    // (a TcheckPlayerDlg e criada uma vez: FormCreate, 10:3a65)
    static bool _criada;
    static ushort _treinador;
    static string _nome = "";
    static int _pais = -1, _forcaMin = -1, _forcaMax = -1, _faixa = -1, _posicao = -1, _preco = -1;
    static bool _naoMinha, _procurar;
    static List<ushort> _achados = new();
    // os filtros do launcher (ItemIndex; 0 = "Qualquer")
    static int _comp, _nota, _risco;

    // a forca em duas caixas, minimo e maximo (no original, faixas fixas de DS:02D6): aqui
    // todos os numeros de 1 a 50 e, em primeiro, o vazio (sem limite). O indice e o valor.
    const int ForcaMaxima = 50;
    static readonly string[] Forcas = new[] { "" }.Concat(Enumerable.Range(1, ForcaMaxima).Select(f => " " + f)).ToArray();
    // (Configuracoes.ForcaPorFaixas) as faixas fixas do original (DS:02D6): uma caixa so
    static readonly int[][] FaixasForca = { new[] { 1, 10 }, new[] { 5, 15 }, new[] { 10, 20 }, new[] { 20, 30 }, new[] { 30, 40 }, new[] { 40, 50 } };
    static readonly string[] Faixas = FaixasForca.Select(f => $" {f[0]} - {f[1]}").ToArray();

    static readonly string[] ComportamentosLauncher = { "Qualquer", "Fair Play", "Cordeirinho", "Cavalheiro", "Caneleiro", "Caceteiro", "Sarrafeiro" };
    static string[] NotasLauncher => new[] { T("Qualquer"), "10" }.Concat(Enumerable.Range(2, 8).Reverse().Select(n => T("{0} ou mais", n))).ToArray();
    static string[] RiscosLauncher => new[] { T("Qualquer"), "0" }.Concat(Enumerable.Range(1, 9).Select(n => T("Até {0}", n))).ToArray();

    sealed class Form : MercadoForm
    {
        readonly SEquipa _e;
        readonly Action<ushort> _escolher;
        readonly bool _launcher;
        // o deslocamento do lado direito (procura.py: a janela 50 pontos mais larga)
        readonly double _mais;
        readonly List<(string Cod, string Nome)> _paises;
        readonly string[] _precos;
        readonly long[][] _faixasPreco;
        readonly Lista _lista;
        readonly Botao _equipaBtn, _procurarBtn;
        readonly TextBox _caixaNome;
        readonly bool _porFaixas = OpcoesDoJogo.ForcaPorFaixasGravada;

        public Form(Sessao s, SEquipa e, Action<ushort> escolher)
            : base(s, 632 + (s.J.M.Regras.CartoesAmarelos ? 50 : 0), 453)
        {
            _e = e;
            _escolher = escolher;
            _launcher = s.J.M.Regras.CartoesAmarelos;
            _mais = _launcher ? 50 : 0;
            Titulo = T("Lista de Jogadores");
            FundoCliente = Amarelo;
            // a caixa e Sorted: os paises pelo nome
            _paises = Paises().OrderBy(p => p.Nome, StringComparer.Create(System.Globalization.CultureInfo.InvariantCulture, ignoreCase: true)).ToList();
            // DS:28A8 (10:2bc6): 7 faixas em multiplos do +0x43 da moeda
            long m = Moeda.DoSave(s.S).D43;
            _faixasPreco = new[]
            {
                new long[] { 0, 0 }, new[] { 1, 50000 * m }, new[] { 50000 * m, 200000 * m }, new[] { 200000 * m, 500000 * m },
                new[] { 500000 * m, 1000000 * m }, new[] { 1000000 * m, 2500000 * m }, new[] { 2500000 * m, 5000000 * m }, new[] { 5000000 * m, 0 },
            };
            var precos = new List<string> { T("Qualquer"), T(" Até {0}", Dinheiro(_faixasPreco[1][1])) };
            for (int i = 2; i < 7; i++) precos.Add(T(" De {0} até {1}", Dinheiro(_faixasPreco[i][0]), Dinheiro(_faixasPreco[i][1])));
            precos.Add(T(" Mais de {0}", Dinheiro(_faixasPreco[7][0])));
            _precos = precos.ToArray();

            if (!_criada)
            {
                // "Nao mostrar os jogadores da minha equipa" comeca marcada (DFM: cbChecked)
                _criada = true; _naoMinha = true; _treinador = 0;
            }
            // outro treinador: tudo limpo (a caixa de marcar e os filtros do launcher ficam)
            if (e.Treinador != _treinador)
            {
                _nome = ""; _pais = _forcaMin = _forcaMax = _faixa = _posicao = _preco = -1;
                _achados = new(); _procurar = true;
            }

            // --- criterios (coordenadas do DFM)
            Comps.Add(Pequeno(new Rect(316, 8, 25, 25), "clearNameBtn", () => { _caixaNome!.Text = ""; Alterado(); }));
            if (_porFaixas) Comps.Add(Caixa(new Rect(108, 40, 200, 24), () => Faixas, () => _faixa, i => _faixa = i));
            else
            {
                Comps.Add(Caixa(new Rect(108, 40, 88, 24), () => Forcas, () => _forcaMin, i => _forcaMin = i));
                Comps.Add(Caixa(new Rect(220, 40, 88, 24), () => Forcas, () => _forcaMax, i => _forcaMax = i));
            }
            Comps.Add(Pequeno(new Rect(316, 40, 25, 25), "clearSkillBtn", () => { _forcaMin = _forcaMax = _faixa = -1; Alterado(); }));
            Comps.Add(Caixa(new Rect(108, 72, 200, 24), () => Posicoes.Select(x => T(x)).ToArray(), () => _posicao, i => _posicao = i));
            Comps.Add(Pequeno(new Rect(316, 72, 25, 25), "clearPositionBtn", () => { _posicao = -1; Alterado(); }));
            var pais = Caixa(new Rect(108, 104, 200, 37), () => _paises.Select(p => p.Nome).ToList(), () => _pais, i => _pais = i);
            pais.ItemA = 31;
            pais.DesenharItem = ItemPais;   // comboCountryDrawItem (10:36c4): a bandeira e o nome
            Comps.Add(pais);
            Comps.Add(Pequeno(new Rect(316, 108, 25, 25), "clearCountryBtn", () => { _pais = -1; Alterado(); }));
            Comps.Add(Pequeno(new Rect(348, 108, 25, 25), "myCountryBtn", () =>
            {
                // 10:3930: o pais da equipa
                var cod = PaisDe(_e.Pais);
                int k = _paises.FindIndex(p => p.Cod == cod);
                if (k >= 0) _pais = k;
                Alterado();
            }));
            Comps.Add(Caixa(new Rect(108, 148, 201, 24), () => _precos, () => _preco, i => _preco = i));
            Comps.Add(Pequeno(new Rect(316, 148, 25, 25), "clearPriceBtn", () => { _preco = -1; Alterado(); }));
            if (_launcher)
            {
                // filtros.py: na coluna da direita, nas linhas do Nome, da Forca e da Posicao; o X
                // de cada um (fora do launcher, como os da esquerda) volta ao "Qualquer"
                Comps.Add(Caixa(new Rect(505, 8, 140, 24), () => ComportamentosLauncher.Select(x => T(x)).ToArray(), () => _comp, i => _comp = i));
                Comps.Add(Pequeno(new Rect(650, 8, 25, 25), "clearPositionBtn", () => { _comp = 0; Alterado(); }));
                Comps.Add(Caixa(new Rect(505, 40, 140, 24), () => NotasLauncher, () => _nota, i => _nota = i));
                Comps.Add(Pequeno(new Rect(650, 40, 25, 25), "clearSkillBtn", () => { _nota = 0; Alterado(); }));
                Comps.Add(Caixa(new Rect(505, 72, 140, 24), () => RiscosLauncher, () => _risco, i => _risco = i));
                Comps.Add(Pequeno(new Rect(650, 72, 25, 25), "clearPositionBtn", () => { _risco = 0; Alterado(); }));
                Comps.Add(new Marcar { R = new Rect(389, 102, 21, 29), Marcado = () => _naoMinha, Clique = TrocarNaoMinha });
            }
            else Comps.Add(new Marcar { R = new Rect(424, 96, 21, 29), Marcado = () => _naoMinha, Clique = TrocarNaoMinha });

            // --- a lista e os botoes (larguras.py: lista 16 pontos mais larga; procura.py: +50)
            _lista = new Lista
            {
                R = new Rect(16, 192, 501 + _mais, 226),
                N = () => _achados.Count,
                Item = DesenharJogador,
                AoClicar = _ => { },
                AoDuplo = _ => Equipa(),
            };
            Comps.Add(_lista);
            ListaPrincipal = _lista;
            double bx = 532 + _mais;
            _procurarBtn = new Botao
            {
                R = new Rect(bx, 184, 64, 64), Texto = T("Procurar"), Glifo = "Mercado/TcheckPlayerDlg.findBtn.png", Glifos = 1,
                Padrao = true, Habilitado = () => _procurar, Clique = Procurar,
            };
            Comps.Add(_procurarBtn);
            _equipaBtn = new Botao
            {
                R = new Rect(bx, _launcher ? 252 : 284, 64, 64), Texto = T("Time..."), Glifo = "Mercado/TcheckPlayerDlg.teamBtn.png",
                Negrito = true, Habilitado = () => Escolhido() != null, Clique = Equipa,
            };
            Comps.Add(_equipaBtn);
            if (_launcher)
                // comprar.py: o icone do Comprar da janela da equipa; ligado so com o jogador compravel
                Comps.Add(new Botao
                {
                    R = new Rect(bx, 320, 64, 64), Texto = T("Comprar"), Glifo = "Mercado/TPlantelDlg.buyBtn.png",
                    Negrito = true, Habilitado = () => PrecoDoEscolhido() > 0, Clique = Comprar,
                });
            Comps.Add(new Botao
            {
                R = new Rect(bx, _launcher ? 388 : 359, 64, 64), Texto = T("Fechar"), Glifo = "Mercado/TcheckPlayerDlg.OKBtn.png",
                Negrito = true, Cancelar = true, Clique = () => { _treinador = _e.Treinador; Fechar(); },
            });

            _caixaNome = CaixaTexto(new Rect(108, 8, 200, 24), 20, t =>
            {
                // textNameChange (10:39b1)
                if (t == _nome) return;
                _nome = t;
                _procurar = true;
                InvalidateVisual();
            });
            _caixaNome.Text = _nome;
        }

        Botao Pequeno(Rect r, string nome, Action clique) =>
            new() { R = r, Glifo = $"Mercado/TcheckPlayerDlg.{nome}.png", Clique = () => { clique(); InvalidateVisual(); } };

        Combo Caixa(Rect r, Func<IReadOnlyList<string>> itens, Func<int> indice, Action<int> escolher) => new()
        {
            R = r, Itens = itens, Indice = indice,
            Texto = () => { int i = indice(); var l = itens(); return i >= 0 && i < l.Count ? l[i] : ""; },
            Escolher = i => { escolher(i); Alterado(); },
        };

        void Alterado()
        {
            _procurar = true;
            InvalidateVisual();
        }

        void TrocarNaoMinha()
        {
            _naoMinha = !_naoMinha;
            Alterado();
        }

        void ItemPais(DrawingContext dc, int i, Rect r, IBrush cor)
        {
            if (i < 0 || i >= _paises.Count) return;
            Bandeira(dc, _paises[i].Cod, new Rect(r.X + 2, r.Y + 2, 36, r.Height - 4));
            var t = Txt(_paises[i].Nome, 13, cor);
            dc.DrawText(t, new Point(r.X + 44, r.Y + Math.Floor((r.Height - t.Height) / 2)));
        }

        // ------------------------------------------------------------ dados
        SJogador? Escolhido(out SEquipa? eq)
        {
            eq = null;
            if (_lista.Sel < 0 || _lista.Sel >= _achados.Count) return null;
            var (e, k) = MercadoJogo.EquipaDoJogador(S.S, _achados[_lista.Sel]);
            eq = e;
            return e == null ? null : e.Jogadores[k];
        }

        SJogador? Escolhido() => Escolhido(out _);

        // o Comprar do launcher fica ligado quando o da janela da equipa estaria (UpdateBuyBtn)
        int PrecoDoEscolhido()
        {
            var p = Escolhido(out var eq);
            return p == null || eq == null ? 0 : S.Mercado.PrecoParaComprar(_e, eq, p);
        }

        // ------------------------------------------------------------ botoes
        // findBtnClick (10:34f5): os jogadores da lista global (DS:26FE) do fim para o comeco
        // que passam nos filtros (no original, so os primeiros 50 na copia nao registada; o
        // RetroFoot nao tem registo: a lista vem toda); a lista e Sorted (pelo nome, sem
        // diferenciar maiusculas)
        void Procurar()
        {
            _procurar = false;
            _lista.Sel = -1;
            _lista.Rolagem = 0;
            var todos = S.S.Equipas.SelectMany(eq => eq.Jogadores.Select(p => (eq, p))).ToList();
            var itens = new List<(string Chave, ushort Id)>();
            for (int i = todos.Count - 1; i >= 0; i--)
            {
                var (eq, p) = todos[i];
                if (!PassaFiltro(eq, p)) continue;
                itens.Add((NomeJogador(p) + "," + p.ID, p.ID));
            }
            // LBS_SORT: lstrcmpi (sem diferenciar maiusculas, acentos so no fim)
            var cmp = StringComparer.Create(System.Globalization.CultureInfo.InvariantCulture, ignoreCase: true);
            _achados = itens.OrderBy(x => x.Chave, cmp).Select(x => x.Id).ToList();
            InvalidateVisual();
        }

        // a funcao aninhada 10:3223 (+ filtros.asm do launcher)
        bool PassaFiltro(SEquipa eq, SJogador p)
        {
            if (_posicao >= 0 && p.Posicao != _posicao) return false;
            if (_pais >= 0 && _pais < _paises.Count && PaisDe(p.Pais) != _paises[_pais].Cod) return false;
            if (_nome != "" && !NomeJogador(p).ToUpperInvariant().Contains(_nome.ToUpperInvariant())) return false;
            short f = (short)p.Forca;
            if (_porFaixas)
            {
                if (_faixa >= 0 && _faixa < FaixasForca.Length && (f < FaixasForca[_faixa][0] || f > FaixasForca[_faixa][1])) return false;
            }
            else
            {
                // 0 (o vazio) ou -1: sem limite
                if (_forcaMin > 0 && f < _forcaMin) return false;
                if (_forcaMax > 0 && f > _forcaMax) return false;
            }
            if (_preco >= 0)
            {
                long v = S.Mercado.PrecoDeVenda(eq, _e, p, MercadoJogo.ForcaMedia(eq));
                if (_preco == 0) { if (v == 0) return false; }
                else if (_preco < 7) { if (v < _faixasPreco[_preco][0] || v > _faixasPreco[_preco][1]) return false; }
                else if (v < _faixasPreco[7][0]) return false;
            }
            if (_naoMinha && eq.ID == _e.ID) return false;
            if (_launcher)
            {
                if (_comp > 0 && p.Comportamento != _comp - 1) return false;
                if (_nota > 0 && (short)p.Nota + _nota < 11) return false;
                if (_risco > 0 && (short)p.Lesao >= _risco) return false;
            }
            return true;
        }

        // teamBtnClick / ListBoxOfPlayersDblClick: fecha e abre o plantel do jogador
        void Equipa()
        {
            if (_lista.Sel < 0 || _lista.Sel >= _achados.Count) return;
            var id = _achados[_lista.Sel];
            _treinador = _e.Treinador;
            Fechar();
            _escolher(id);
        }

        // comprar.asm: compra direta (buyBtnClick da janela da equipa), o dinheiro em caixa
        // atualizado e a linha redesenhada (fica sem preco). A compra direta exige o jogo
        // registado (Opcoes.Registrado, sempre true: o RetroFoot nao tem registo).
        void Comprar()
        {
            var p = Escolhido(out var eq);
            if (p == null || eq == null) return;
            int preco = S.Mercado.PrecoParaComprar(_e, eq, p);
            if (preco <= 0) return;
            S.Mercado.Comprar(_e, eq, eq.Jogadores.IndexOf(p), preco, registado: S.J.Op.Registrado);
            InvalidateVisual();
        }

        // ------------------------------------------------------------ desenho
        protected override void DesenharFundo(DrawingContext dc)
        {
            Bevel(dc, new Rect(8, 184, 517 + _mais, 241));
            Escreve(dc, T("Nome"), 20, 12, 13, Preto);
            Escreve(dc, T("Força"), 20, 44, 13, Preto);
            if (!_porFaixas) Escreve(dc, "–", 202, 44, 13, Preto);
            Escreve(dc, T("Posição"), 20, 76, 13, Preto);
            Escreve(dc, T("Nacionalidade"), 20, 112, 13, Preto);
            Escreve(dc, T("Preço"), 20, 152, 13, Preto);
            // o TEdit (a TextBox fica por cima)
            dc.FillRectangle(Branco, new Rect(108, 8, 200, 24));
            Afundada(dc, new Rect(108, 8, 200, 24));
            if (_launcher)
            {
                Escreve(dc, T("Comportamento"), 385, 12, 13, Preto);
                Escreve(dc, T("Nota"), 385, 44, 13, Preto);
                Escreve(dc, T("Risco de lesão"), 385, 76, 13, Preto);
                Rotulo(dc, T("Não mostrar os jogadores do meu time"), new Rect(417, 108, 270, 16), 13, Preto);
                Escreve(dc, T("Dinheiro em caixa"), 385, 146, 13, Azul);
                Rotulo(dc, Dinheiro((int)_e.Dinheiro), new Rect(505, 146, 170, 16), 13, Azul);
                // o rotulo das colunas novas (procura.py)
                Escreve(dc, T("Nota   RL"), 18 + 466 - 6, 168, 13, Preto);
            }
            else
            {
                Rotulo(dc, T("Não mostrar os jogadores do meu time"), new Rect(452, 96, 149, 32), 13, Preto);
                Escreve(dc, T("Dinheiro em caixa"), 420, 12, 13, Azul);
                Rotulo(dc, Dinheiro((int)_e.Dinheiro), new Rect(420, 36, 205, 16), 13, Azul);
            }
        }

        // ListBoxOfPlayersDrawItem (10:3168): colunas 0xE063 (posicao, nome, equipa, forca,
        // pais, preco para a equipa humana, comportamento); no launcher, Nota e RL
        void DesenharJogador(DrawingContext dc, int i, Rect r, bool sel)
        {
            var (eq, k) = MercadoJogo.EquipaDoJogador(S.S, _achados[i]);
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
            c.Col(25, PaisDe(p.Pais));
            int v = S.Mercado.PrecoDeVenda(eq, _e, p, MercadoJogo.ForcaMedia(eq));
            c.Col(80, v > 0 ? Dinheiro(v) : "", true);
            c.Col(20, ComportamentoCurto(p));
            if (_launcher)
            {
                // procura.asm: Canvas.TextOut(Left + x, Top, IntToStr(campo))
                Escreve(dc, p.Nota.ToString(), r.X + 466, r.Y, 11, cor);
                Escreve(dc, p.Lesao.ToString(), r.X + 500, r.Y, 11, cor);
            }
        }
    }
}
