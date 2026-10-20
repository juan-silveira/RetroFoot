using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Partida;
using RetroFoot.Motor.Patrocinio;
using Mem = RetroFoot.Motor.Partida.Memoria;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// As outras janelas da partida no celular (Celular.cs), nas folhas do kit (CelularUi.cs), nas
// duas peles: a substituicao (TelaSubstituicao: intervalo, goleiro expulso, detalhes de um
// jogo), o penalti (TelaPenalti), o desempate por penaltis (TelaPenaltis), o sorteio da Taca
// (TelaSorteio), os detalhes do jogo do Moderno (TelaDetalhesModerna) e a proposta de
// patrocinio (TelaPatrocinio). As classes originais continuam com as regras e os relogios;
// estas so mostram e recolhem o toque.

// ---------------------------------------------------------------- pecas comuns
static class PC
{
    static readonly BoxShadows Saltado = BoxShadows.Parse("inset 1 1 0 0 #FFFFFF, inset -1 -1 0 0 #6A6A6A");
    static readonly BoxShadows Afundado = BoxShadows.Parse("inset 1 1 0 0 #6A6A6A, inset -1 -1 0 0 #FFFFFF");

    // o placar: casa (escudo e nome), o rotulo (momento, minuto) e os golos, visitante
    public static Control Placar(SEquipa? casa, SEquipa? fora, int gc, int gf, string rotulo, IBrush? corRotulo = null, double tamPlacar = 34)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
        g.Children.Add(Equipa(casa));
        var meio = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0) };
        if (rotulo.Length > 0)
        {
            var r = CUi.Txt(rotulo, 12.5, corRotulo ?? (CUi.Retro ? W95.Marinho : TemaVerde.Destaque), FontWeight.Bold);
            r.HorizontalAlignment = HorizontalAlignment.Center;
            meio.Children.Add(r);
        }
        var p = CUi.Txt($"{gc} - {gf}", tamPlacar, null, FontWeight.Bold);
        p.HorizontalAlignment = HorizontalAlignment.Center;
        meio.Children.Add(p);
        Grid.SetColumn(meio, 1);
        g.Children.Add(meio);
        var ef = Equipa(fora);
        Grid.SetColumn(ef, 2);
        g.Children.Add(ef);
        return CUi.Cartao_(null, g);
    }

    // a equipa do placar: o escudo (no Retro, o circulo nas cores) e o nome embaixo
    public static Control Equipa(SEquipa? e)
    {
        var s = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
        var esc = CUi.Escudo(e, 46);
        esc.HorizontalAlignment = HorizontalAlignment.Center;
        s.Children.Add(esc);
        if (e == null) return s;
        Control nome;
        if (CUi.Retro)
        {
            nome = CUi.NomeEquipa(e, P98.NomeEquipa(e), 13, true);
            nome.HorizontalAlignment = HorizontalAlignment.Center;
        }
        else
        {
            var t = CUi.Txt(P98.NomeEquipa(e), 14, null, FontWeight.SemiBold);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.TextAlignment = TextAlignment.Center;
            nome = t;
        }
        s.Children.Add(nome);
        return s;
    }

    // o nome da equipa que encolhe com reticencias na largura que houver (CUi.NomeEquipa no
    // Retro, a caixa nas cores; no Moderno, o logo e o nome); direita: encostado a direita
    public static Control NomeCurto(SEquipa e, double tam, bool direita = false, bool negrito = false)
    {
        if (CUi.Retro)
        {
            var c = CUi.NomeEquipa(e, P98.NomeEquipa(e), tam, negrito);
            c.HorizontalAlignment = direita ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            return c;
        }
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(direita ? "*,Auto" : "Auto,*"), ColumnSpacing = 7, VerticalAlignment = VerticalAlignment.Center };
        var esc = CUi.Escudo(e, tam + 8);
        var t = CUi.Txt(P98.NomeEquipa(e), tam, null, negrito ? FontWeight.Bold : FontWeight.Medium);
        t.HorizontalAlignment = direita ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        Grid.SetColumn(esc, direita ? 1 : 0);
        Grid.SetColumn(t, direita ? 0 : 1);
        g.Children.Add(esc);
        g.Children.Add(t);
        return g;
    }

    // um filtro que liga e desliga (amarelos, vermelhos, lesoes): pilula com o visto
    public static Clicavel Filtro(string texto, bool marcado, Action<bool> mudou)
    {
        var t = CUi.Txt("", 14, null, FontWeight.SemiBold);
        var c = new Clicavel
        {
            Child = t, MinHeight = 44, Padding = new Thickness(12, 6), CornerRadius = new CornerRadius(CUi.Retro ? 0 : 22),
            BorderThickness = new Thickness(CUi.Retro ? 1 : 1.5), BorderBrush = CUi.Retro ? W95.Preto : TemaVerde.Borda, CorFoco = null,
        };
        void Pintar()
        {
            t.Text = (marcado ? "✓ " : "") + texto;
            if (CUi.Retro)
            {
                c.Background = marcado ? W95.Marinho : W95.Face;
                t.Foreground = marcado ? W95.Branco : W95.Preto;
                c.BoxShadow = marcado ? Afundado : Saltado;
            }
            else
            {
                c.Background = marcado ? TemaVerde.Selecao : TemaVerde.BotaoSecundario;
                c.BorderBrush = marcado ? TemaVerde.Destaque : TemaVerde.Borda;
                t.Foreground = TemaVerde.Texto;
            }
        }
        c.Clique += () => { marcado = !marcado; Pintar(); mudou(marcado); };
        Pintar();
        return c;
    }

    // o texto que nao rola junto (o rodape): centrado e com quebra
    public static TextBlock Nota(string s, IBrush? cor = null)
    {
        var t = CUi.Txt(s, 13, cor ?? CUi.Fraco, FontWeight.Normal, true);
        t.TextAlignment = TextAlignment.Center;
        t.HorizontalAlignment = HorizontalAlignment.Stretch;
        return t;
    }

    // o titulo de uma secao numa folha inteira (sobre o fundo) com a margem de cima
    public static Control Secao(string s)
    {
        var t = CUi.TituloSecao_(s);
        t.Margin = new Thickness(4, 6, 0, 0);
        return t;
    }

    // um ponto redondo com o visto (golo) ou o X (falhou), como na grelha dos penaltis
    public static Control Marca(bool gol) => new Border
    {
        Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = gol ? CUi.Bom : CUi.Erro,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = gol ? "✓" : "✕", FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Tag = "fixo",
        },
    };
}

// o icone de um lance (os Icones da jornada), com o 2o amarelo e as setas da substituicao
sealed class IconeLanceC : Control
{
    readonly int _tipo;
    readonly bool _segundo;
    // tipo 101: substituicao (as duas setas); 102: penalti sem golo
    public IconeLanceC(int tipo, bool segundo = false, double tam = 16)
    {
        _tipo = tipo; _segundo = segundo;
        Width = tam; Height = tam;
        IsHitTestVisible = false;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public override void Render(DrawingContext dc)
    {
        var r = new Rect(Bounds.Size);
        if (_tipo == 101)
        {
            double q = r.Height / 2;
            Icones.Seta(dc, true, new Rect(r.X + (r.Width - q) / 2, r.Y, q, q));
            Icones.Seta(dc, false, new Rect(r.X + (r.Width - q) / 2, r.Y + q, q, q));
        }
        else if (_tipo == 102) Icones.PenaltiFalhado(dc, r);
        else Icones.Desenhar(dc, _tipo, _segundo, r);
    }
}

// ---------------------------------------------------------------- a substituicao
// Na tela toda: em cima o placar e o momento (intervalo, goleiro expulso, em jogo), os lances
// com os filtros (os mesmos do original: as opcoes de ver expulsoes e lesoes; o amarelo
// desmarcado a cada vez), e a equipa: quem esta em campo e o banco. Toca-se em quem sai e em
// quem entra e Substituir; Continuar (ou o Voltar) fecha. As regras: TelaSubstituicao.PodeTrocar
// e Trocar.
sealed class SubstituicaoCelular : UserControl
{
    public event Action? Fechou;

    readonly JogoAtual _jg;
    readonly SEquipa _e;
    readonly int _motivo;
    readonly Opcoes _op;
    readonly bool _completa;
    readonly List<SJogador> _tit = new(), _res = new();
    readonly ContentControl _lances = new(), _campo = new(), _banco = new();
    readonly CUi.Botao? _substituir;
    readonly TextBlock? _restantes;
    readonly StackPanel? _troca;
    bool _amarelos, _pode, _fechou;
    int _selT = -1, _selR = -1;

    public SubstituicaoCelular(JogoAtual jg, int lado, int motivo, Opcoes op, bool cartoesAmarelos, bool completa, bool pode)
    {
        _jg = jg; _e = jg.Equipa[lado]!; _motivo = motivo; _op = op; _pode = pode; _completa = completa;
        // no Retro, a janela do computador adaptada (PartidaCelularRetro.cs)
        if (CUi.Retro) { Content = new SubstituicaoRetro(jg, lado, motivo, op, cartoesAmarelos, completa, pode, Fechar); return; }
        var casa = jg.Equipa[Lado.Casa]; var fora = jg.Equipa[Lado.Visit];
        string momento = T(motivo switch { 4 => "Intervalo", 3 => "Guarda-redes expulso", _ => "Em jogo" });

        var corpo = new StackPanel { Spacing = 10 };
        corpo.Children.Add(PC.Placar(casa, fora, casa == null ? 0 : Mem.Golos(casa), fora == null ? 0 : Mem.Golos(fora), momento,
            motivo == 3 ? CUi.Erro : null));

        // os lances e os filtros (os textos de cada pele, como nas janelas do computador)
        corpo.Children.Add(PC.Secao(T("Lances")));
        var filtros = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        if (cartoesAmarelos)
            filtros.Children.Add(PC.Filtro(T(CUi.Retro ? "Cartão amarelo" : "Amarelos"), false, v => { _amarelos = v; Lances(); }));
        filtros.Children.Add(PC.Filtro(T(CUi.Retro ? (cartoesAmarelos ? "Cartão vermelho" : "Cartões vermelhos") : "Vermelhos"), op.VerExpulsoes,
            v => { _op.VerExpulsoes = v; Lances(); }));
        filtros.Children.Add(PC.Filtro(T(CUi.Retro && cartoesAmarelos ? "Lesão" : "Lesões"), op.VerLesoes, v => { _op.VerLesoes = v; Lances(); }));
        corpo.Children.Add(filtros);
        corpo.Children.Add(_lances);

        Control rodape;
        var continuar = new CUi.Botao(T("Continuar"), !completa || !pode, Fechar);
        if (completa)
        {
            foreach (var p in _e.Jogadores)
                switch (Mem.Escalacao(p))
                {
                    case Mem.EscTitular: _tit.Add(p); break;
                    case Mem.EscReserva: _res.Add(p); break;
                }
            corpo.Children.Add(PC.Secao(T("Em campo · {0}", P98.NomeEquipa(_e))));
            corpo.Children.Add(_campo);
            corpo.Children.Add(PC.Secao(T("Banco")));
            corpo.Children.Add(_banco);

            _substituir = new CUi.Botao(T("Substituir"), true, Substituir) { Habilitado = false };
            _restantes = PC.Nota("");
            _troca = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center };
            var r = new StackPanel { Spacing = 8 };
            r.Children.Add(_troca);
            r.Children.Add(_restantes);
            r.Children.Add(pode ? CUi.Botoes(_substituir, continuar) : CUi.Botoes(continuar));
            rodape = r;
        }
        else rodape = CUi.Botoes(continuar);

        string sub = jg.Arbitro is { } a ? T("Árbitro: {0} ({1})", P98.Texto(a.Nome), P98.NomePais(P98.Texto(a.Pais))) : "";
        string titulo = completa ? P98.NomeEquipa(_e) : $"{P98.NomeEquipa(casa)} - {P98.NomeEquipa(fora)}";
        Content = new CUi.Folha(titulo, corpo, rodape, sub, inteira: true, fechar: Fechar);
        Lances();
        Montar();
        Atualizar();
    }

    // seg02:24a1, com os mesmos filtros: minuto, icone, nome, o placar no golo e a equipa
    void Lances()
    {
        var linhas = new List<Control>();
        int gc = 0, gf = 0;
        foreach (var l in _jg.Lances)
        {
            string placar = "", det = "";
            switch (l.Tipo)
            {
                case TipoLance.Gol:
                case TipoLance.Penalti:
                    if (l.Lado == Lado.Casa) gc++; else gf++;
                    placar = $"{gc}-{gf}";
                    if (l.Tipo == TipoLance.Penalti) det = T(" (pen.)").Trim();
                    break;
                case TipoLance.Amarelo: if (!_amarelos) continue; break;
                case TipoLance.Expulsao: if (!_op.VerExpulsoes) continue; break;
                case TipoLance.Lesao: if (!_op.VerLesoes) continue; break;
                case TipoLance.GolAnulado: det = T("Gol anulado"); break;
                default: continue;
            }
            linhas.Add(CUi.Toque(Linha(l, placar, det), null, 40));
        }
        _lances.Content = linhas.Count == 0 ? CUi.Txt(T("Sem lances"), 14, CUi.Retro ? W95.Branco : CUi.Fraco) : CUi.Grupo(linhas);
    }

    Control Linha(Lance l, string placar, string det)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("46,26,*,Auto,Auto") };
        var m = CUi.Txt(MinutoTexto.Texto(l.Minuto, l.Acrescimo), l.Acrescimo > 0 ? 11.5 : 13, CUi.Fraco, FontWeight.Bold);
        g.Children.Add(m);
        var ic = new IconeLanceC(l.Tipo, l.SegundoAmarelo);
        Grid.SetColumn(ic, 1);
        g.Children.Add(ic);
        var nome = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        nome.Children.Add(CUi.Txt(P98.NomeSimples(l.Jogador).Trim(), 15, l.Tipo == TipoLance.GolAnulado ? CUi.Fraco : null, FontWeight.Medium));
        if (det.Length > 0) nome.Children.Add(CUi.Txt(det, 12.5, CUi.Fraco));
        Grid.SetColumn(nome, 2);
        g.Children.Add(nome);
        if (placar.Length > 0)
        {
            var p = CUi.Txt(placar, 15, null, FontWeight.Bold);
            p.Margin = new Thickness(8, 0);
            Grid.SetColumn(p, 3);
            g.Children.Add(p);
        }
        var esc = CUi.Escudo(_jg.Equipa[l.Lado == Lado.Casa ? Lado.Casa : Lado.Visit], 22);
        Grid.SetColumn(esc, 4);
        g.Children.Add(esc);
        return g;
    }

    // as duas listas (refeitas a cada toque: o banco mostra quem pode entrar por quem sai)
    void Montar()
    {
        if (!_completa) return;
        if (_tit.Count == 0) _campo.Content = CUi.Txt(T("Ninguém em campo"), 14, CUi.Retro ? W95.Branco : CUi.Fraco);
        else if (!_pode) _campo.Content = CUi.Grupo(_tit.Select(p => (Control)CUi.Toque(CUi.LinhaJogador(p), null, 44)));
        else
        {
            var (lista, marcar) = CUi.ListaEscolha(_tit.Select(p => CUi.LinhaJogador(p)).ToList(), i => { _selT = i; MontarBanco(); Atualizar(); });
            if (_selT >= 0) marcar(_selT);
            _campo.Content = lista;
        }
        MontarBanco();
    }

    void MontarBanco()
    {
        if (_res.Count == 0) { _banco.Content = CUi.Txt(T("Banco vazio"), 14, CUi.Retro ? W95.Branco : CUi.Fraco); return; }
        if (!_pode) { _banco.Content = CUi.Grupo(_res.Select(p => (Control)CUi.Toque(CUi.LinhaJogador(p), null, 44))); return; }
        var sai = _selT >= 0 && _selT < _tit.Count ? _tit[_selT] : null;
        var (lista, marcar) = CUi.ListaEscolha(_res.Select(p => CUi.LinhaJogador(p)).ToList(), i => { _selR = i; Atualizar(); },
            i => sai == null || TelaSubstituicao.PodeTrocar(sai, _res[i], _motivo));
        if (_selR >= 0) marcar(_selR);
        _banco.Content = lista;
    }

    // seg02:2905
    void Atualizar()
    {
        if (_substituir == null || _restantes == null || _troca == null) return;
        bool temT = _selT >= 0 && _selT < _tit.Count, temR = _selR >= 0 && _selR < _res.Count;
        _substituir.Habilitado = _pode && temT && temR && TelaSubstituicao.PodeTrocar(_tit[_selT], _res[_selR], _motivo);
        int n = Mem.Substituicoes(_e);
        if (n <= 0 && _pode) { _pode = false; _selT = _selR = -1; Montar(); }
        _restantes.Text = !_pode && n > 0 ? T("Substituições só ao intervalo")
            : n == 1 ? T("1 substituição restante") : T("{0} substituições restantes", n);
        // quem sai e quem entra (as setas da linha do tempo)
        _troca.Children.Clear();
        _troca.IsVisible = _pode;
        if (!_pode) return;
        if (!temT && !temR) { _troca.Children.Add(PC.Nota(T("Toque em quem sai e depois em quem entra"), CUi.Texto)); return; }
        void Por(bool entra, SJogador? p)
        {
            var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var seta = CUi.Txt(entra ? "▲" : "▼", 13, entra ? CUi.Bom : CUi.Erro, FontWeight.Bold);
            s.Children.Add(seta);
            s.Children.Add(CUi.Txt(p == null ? "…" : TextosMercado.NomeJogador(p), 14.5, p == null ? CUi.Fraco : null, FontWeight.SemiBold));
            _troca.Children.Add(s);
        }
        Por(false, temT ? _tit[_selT] : null);
        Por(true, temR ? _res[_selR] : null);
    }

    // seg02:2a4f
    void Substituir()
    {
        if (_substituir == null || !_substituir.Habilitado) return;
        if (_selT < 0 || _selT >= _tit.Count || _selR < 0 || _selR >= _res.Count) return;
        TelaSubstituicao.Trocar(_e, _tit, _res, _tit[_selT], _res[_selR]);
        _selT = _selR = -1;
        Montar();
        Atualizar();
    }

    void Fechar()
    {
        if (_fechou) return;
        _fechou = true;
        Fechou?.Invoke();
    }
}

// ---------------------------------------------------------------- o penalti
// Uma folha nas cores do kit: o humano escolhe na lista dos titulares e Chutar; depois (ou
// logo, quando o computador cobra) a cena: o marcador e, no tique 3 do relogio da TelaPenalti,
// o resultado. A janela nao fecha pelo Voltar (so no fim do relogio, como o original).
sealed class PenaltiCelular : UserControl
{
    public event Action<SJogador>? Chutou;

    readonly List<SJogador> _titulares;
    readonly CUi.Folha _folha;
    readonly Control? _escolha;
    readonly StackPanel _cena;
    readonly TextBlock _nome, _espera, _resultado;
    readonly CUi.Botao? _chutar;
    readonly PenaltiRetro? _r;
    int _sel = -1;

    public PenaltiCelular(SEquipa atacante, string treinador, List<SJogador> titulares, bool humano)
    {
        _titulares = titulares;
        // no Retro, a janela do computador adaptada (PartidaCelularRetro.cs)
        if (CUi.Retro)
        {
            Content = _r = new PenaltiRetro(atacante, treinador, titulares, humano);
            _r.Chutou += b => Chutou?.Invoke(b);
            _folha = null!; _cena = null!; _nome = _espera = _resultado = null!;
            return;
        }
        var corpo = new StackPanel { Spacing = 12 };
        _cena = new StackPanel { Spacing = 6, IsVisible = !humano, Margin = new Thickness(0, 18, 0, 22), MinHeight = 170 };
        var marcador = CUi.SecaoFolha(T("MARCADOR"));
        marcador.HorizontalAlignment = HorizontalAlignment.Center;
        _cena.Children.Add(marcador);
        _nome = CUi.Txt("", 24, null, FontWeight.Bold);
        _nome.HorizontalAlignment = HorizontalAlignment.Center;
        _cena.Children.Add(_nome);
        _espera = CUi.Txt(T("Na marca de penalti…"), 14, CUi.Fraco);
        _espera.HorizontalAlignment = HorizontalAlignment.Center;
        _espera.Margin = new Thickness(0, 16, 0, 0);
        _cena.Children.Add(_espera);
        _resultado = CUi.Txt("", 44, null, FontWeight.Bold);
        _resultado.HorizontalAlignment = HorizontalAlignment.Center;
        _resultado.IsVisible = false;
        _cena.Children.Add(_resultado);

        Control? rodape = null;
        if (humano)
        {
            var escolha = new StackPanel { Spacing = 10 };
            escolha.Children.Add(CUi.Txt(treinador.Length > 0 ? T("{0}, escolha o jogador para marcar o penalti", treinador)
                : T("Escolha o jogador para marcar o penalti"), 15, null, FontWeight.Normal, true));
            _chutar = new CUi.Botao(T("Chutar"), true, Chutar) { Habilitado = false };
            var (lista, _) = CUi.ListaEscolha(titulares.Select(p => CUi.LinhaJogador(p)).ToList(), i => { _sel = i; _chutar.Habilitado = true; });
            escolha.Children.Add(lista);
            corpo.Children.Add(_escolha = escolha);
            rodape = CUi.Botoes(_chutar);
        }
        corpo.Children.Add(_cena);
        Content = _folha = new CUi.Folha(T("Penalti"), corpo, rodape, P98.NomeEquipa(atacante), CUi.Escudo(atacante, 34));
    }

    void Chutar()
    {
        if (_chutar == null || !_chutar.Habilitado || !_chutar.IsVisible) return;
        if (_sel < 0 || _sel >= _titulares.Count) return;
        Chutou?.Invoke(_titulares[_sel]);
    }

    // o batedor na cena (o humano: logo ao chutar; o computador: no 1o tique)
    public void MostrarBatedor(SJogador b)
    {
        if (_r != null) { _r.MostrarBatedor(b); return; }
        if (_escolha != null) _escolha.IsVisible = false;
        if (_chutar != null) _chutar.IsVisible = false;
        _folha.Rodape(null);
        _nome.Text = P98.NomeJogador(b);
        _cena.IsVisible = true;
    }

    public void MostrarResultado(int r)
    {
        if (_r != null) { _r.MostrarResultado(r); return; }
        _espera.IsVisible = false;
        _resultado.Text = r == ResultadoPenalti.Gol ? T("GOLO!") : P98.Resultado(r);
        _resultado.Foreground = r == ResultadoPenalti.Gol ? CUi.Bom : CUi.Erro;
        _resultado.IsVisible = true;
    }
}

// ---------------------------------------------------------------- os penaltis
// A folha do desempate: as equipas e o placar em cima, uma linha por rodada (a casa a esquerda,
// o numero no meio, o visitante a direita, com o ponto verde do golo ou vermelho do falhado) e
// quem passa no fim. Os relogios sao os da TelaPenaltis; a janela nao fecha pelo Voltar.
sealed class PenaltisCelular : UserControl
{
    readonly JogoAtual _jg;
    readonly StackPanel _linhas = new() { Spacing = 0 };
    readonly ScrollViewer _rolar;
    readonly TextBlock _placar, _estado;
    readonly PenaltisRetro? _r;

    public PenaltisCelular(JogoAtual jg)
    {
        _jg = jg;
        // no Retro, a janela do computador adaptada (PartidaCelularRetro.cs)
        if (CUi.Retro) { Content = _r = new PenaltisRetro(jg); _rolar = null!; _placar = _estado = null!; return; }
        var casa = jg.Equipa[Lado.Casa]; var fora = jg.Equipa[Lado.Visit];
        var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
        topo.Children.Add(PC.Equipa(casa));
        _placar = CUi.Txt("0 - 0", 36, null, FontWeight.Bold);
        _placar.Margin = new Thickness(10, 0);
        Grid.SetColumn(_placar, 1);
        topo.Children.Add(_placar);
        var ef = PC.Equipa(fora);
        Grid.SetColumn(ef, 2);
        topo.Children.Add(ef);
        _estado = PC.Nota(T("Penaltis alternados"));
        _rolar = new ScrollViewer
        {
            Content = new Border { Child = _linhas }, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var d = new DockPanel { Margin = new Thickness(14, 10, 14, 14) };
        var cab = new StackPanel { Spacing = 8 };
        var t = CUi.SecaoFolha(T("DESEMPATE POR PENALTIS"));
        t.HorizontalAlignment = HorizontalAlignment.Center;
        cab.Children.Add(t);
        cab.Children.Add(topo);
        DockPanel.SetDock(cab, Dock.Top);
        d.Children.Add(cab);
        d.Children.Add(_rolar);
        Content = new CUi.Folha(T("Penaltis"), d, _estado, rolar: false, icone: Logotipo.ImagemBola(30));
        MinHeight = 380;
    }

    public void Atualizar(List<(SJogador? Bat, int Res)[]> pares, int[] golos)
    {
        if (_r != null) { _r.Atualizar(pares, golos); return; }
        _placar.Text = $"{golos[Lado.Casa]} - {golos[Lado.Visit]}";
        var ls = new List<Control>();
        for (int i = 0; i < pares.Count; i++)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,40,*"), MinHeight = 48 };
            for (int k = 0; k < 2; k++)
            {
                var (b, res) = pares[i][k];
                if (b == null) continue;
                var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = k == 0 ? HorizontalAlignment.Right : HorizontalAlignment.Left };
                var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var n = CUi.Txt(P98.NomeJogador(b), 14.5, null, FontWeight.SemiBold);
                var r = CUi.Txt(P98.Resultado(res), 12, res == ResultadoPenalti.Gol ? CUi.Bom : CUi.Fraco);
                n.HorizontalAlignment = r.HorizontalAlignment = k == 0 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                txt.Children.Add(n);
                txt.Children.Add(r);
                var m = PC.Marca(res == ResultadoPenalti.Gol);
                if (k == 0) { s.Children.Add(txt); s.Children.Add(m); } else { s.Children.Add(m); s.Children.Add(txt); }
                Grid.SetColumn(s, k * 2);
                g.Children.Add(s);
            }
            var num = CUi.Txt((i + 1).ToString(), 13, CUi.Fraco, FontWeight.Bold);
            num.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(num, 1);
            g.Children.Add(num);
            var c = CUi.Toque(g, null, 48);
            c.Padding = new Thickness(8, 4);
            // a ultima fica escolhida (como a lista do original)
            if (i == pares.Count - 1) c.Background = CUi.Retro ? W95.Amarelo : TemaVerde.Selecao;
            ls.Add(c);
        }
        _linhas.Children.Clear();
        _linhas.Children.Add(CUi.Grupo(ls));
        Dispatcher.UIThread.Post(_rolar.ScrollToEnd, DispatcherPriority.Loaded);
    }

    public void Fim(bool passaVisitante)
    {
        // (o original so fecha depois de 3 s, sem texto)
        if (_r != null) return;
        var e = _jg.Equipa[passaVisitante ? Lado.Visit : Lado.Casa];
        _estado.Text = T("{0} passa à próxima fase", P98.NomeEquipa(e));
        _estado.Foreground = CUi.Bom;
        _estado.FontWeight = FontWeight.Bold;
    }
}

// ---------------------------------------------------------------- o sorteio da Taca
// Na tela toda: o jogo que esta sendo sorteado (as duas vagas), os jogos ja sorteados (o mais
// novo em cima, escolhido) e as equipas que ainda estao no pote. O relogio e o da TelaSorteio;
// a janela fecha sozinha no fim (nao ha Voltar).
sealed class SorteioCelular : UserControl
{
    readonly List<SEquipa> _pote;
    readonly List<(SEquipa? Casa, SEquipa? Fora)> _feitos = new();
    readonly int _total;
    readonly ContentControl _casa = new(), _fora = new(), _jogos = new();
    readonly WrapPanel _listaPote = new() { ItemSpacing = 10, LineSpacing = 8 };
    readonly TextBlock _titPote, _titJogos;
    readonly SorteioRetro? _r;

    public SorteioCelular(RetroFoot.Motor.Save.Jogo98 s, List<SEquipa> pote, int jogos)
    {
        _pote = pote; _total = jogos;
        // no Retro, a janela do computador adaptada (PartidaCelularRetro.cs)
        if (CUi.Retro) { Content = _r = new SorteioRetro(pote); _titPote = _titJogos = null!; return; }
        var vagas = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
        vagas.Children.Add(_casa);
        var x = CUi.Txt("×", 22, CUi.Fraco, FontWeight.Bold);
        x.Margin = new Thickness(10, 0);
        Grid.SetColumn(x, 1);
        vagas.Children.Add(x);
        Grid.SetColumn(_fora, 2);
        vagas.Children.Add(_fora);
        Vaga(_casa, null);
        Vaga(_fora, null);
        var topo = new StackPanel { Spacing = 10 };
        var cab = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (Escudos.Taca(s) is { } logo) cab.Children.Add(new Image { Source = logo, Width = 32, Height = 32 });
        cab.Children.Add(CUi.Txt(T("Sorteio dos jogos"), 15, CUi.Fraco, FontWeight.Bold));
        topo.Children.Add(cab);
        topo.Children.Add(vagas);

        _titJogos = CUi.TituloSecao_("");
        _titPote = CUi.TituloSecao_("");
        _titPote.Margin = new Thickness(4, 8, 0, 0);
        _titJogos.Margin = new Thickness(4, 0, 0, 0);
        var lista = new StackPanel { Spacing = 8 };
        lista.Children.Add(_titJogos);
        lista.Children.Add(_jogos);
        lista.Children.Add(_titPote);
        lista.Children.Add(new Border
        {
            Background = CUi.Retro ? W95.Branco : TemaVerde.CartaoHub, CornerRadius = new CornerRadius(CUi.Retro ? 0 : 14),
            BorderBrush = CUi.Retro ? W95.Escura : CUi.Linha, BorderThickness = new Thickness(1), Padding = new Thickness(12, 10), Child = _listaPote,
        });
        var d = new DockPanel();
        var ct = CUi.Cartao_(null, topo);
        ct.Margin = new Thickness(12, 12, 12, 4);
        DockPanel.SetDock(ct, Dock.Top);
        d.Children.Add(ct);
        d.Children.Add(new ScrollViewer
        {
            Content = new Border { Padding = new Thickness(12, 8, 12, 16), Child = lista },
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        });
        Content = new CUi.Folha(T("Sorteio dos jogos da taça"), d, null, inteira: true, rolar: false);
        Jogos();
        Pote();
    }

    static void Vaga(ContentControl c, SEquipa? e)
    {
        Control dentro;
        if (e == null)
        {
            var q = CUi.Txt("?", 22, CUi.Fraco, FontWeight.Bold);
            q.HorizontalAlignment = HorizontalAlignment.Center;
            dentro = q;
        }
        else
        {
            dentro = PC.Equipa(e);
        }
        c.Content = new Border
        {
            MinHeight = 92, Padding = new Thickness(6), CornerRadius = new CornerRadius(CUi.Retro ? 0 : 12),
            BorderThickness = new Thickness(e == null ? 1 : 1.5),
            BorderBrush = e == null ? CUi.Linha : (CUi.Retro ? W95.Preto : TemaVerde.Ouro),
            Background = CUi.Retro ? W95.Branco : (e == null ? null : TemaVerde.CartaoForte),
            Child = dentro,
        };
    }

    void Jogos()
    {
        _titJogos.Text = T("JOGOS · {0} DE {1}", _feitos.Count, _total).ToUpperInvariant();
        if (_feitos.Count == 0) { _jogos.Content = null; return; }
        var ls = new List<Control>();
        for (int i = _feitos.Count - 1; i >= 0; i--)
        {
            var (casa, fora) = _feitos[i];
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
            if (casa != null) g.Children.Add(PC.NomeCurto(casa, 13.5, true));
            var x = CUi.Txt("×", 14, CUi.Fraco);
            x.Margin = new Thickness(8, 0);
            Grid.SetColumn(x, 1);
            g.Children.Add(x);
            if (fora != null) { var n = PC.NomeCurto(fora, 13.5); Grid.SetColumn(n, 2); g.Children.Add(n); }
            var c = CUi.Toque(g, null, 40);
            c.Padding = new Thickness(8, 4);
            g.ClipToBounds = true;
            if (i == _feitos.Count - 1) c.Background = CUi.Retro ? W95.Amarelo : TemaVerde.Selecao;
            ls.Add(c);
        }
        _jogos.Content = CUi.Grupo(ls);
    }

    void Pote()
    {
        _titPote.Text = T("NO POTE · {0}", _pote.Count).ToUpperInvariant();
        _listaPote.Children.Clear();
        if (_pote.Count == 0) _listaPote.Children.Add(CUi.Txt(T("Todas as equipas sorteadas"), 14, CUi.Fraco));
        foreach (var e in _pote) _listaPote.Children.Add(CUi.NomeEquipa(e, P98.NomeEquipa(e), 13));
    }

    // a casa sorteada (limpa o visitante) ou o visitante; a equipa sai do pote
    public void Casa(SEquipa? e) { if (_r != null) { _r.Casa(e); return; } Vaga(_fora, null); Vaga(_casa, e); Tirar(e); }
    public void Fora(SEquipa? e) { if (_r != null) { _r.Fora(e); return; } Vaga(_fora, e); Tirar(e); }

    void Tirar(SEquipa? e)
    {
        if (e == null || !_pote.Remove(e)) return;
        Pote();
    }

    public void Jogo(SEquipa? casa, SEquipa? fora)
    {
        if (_r != null) { _r.Jogo(casa, fora); return; }
        _feitos.Add((casa, fora));
        Jogos();
    }
}

// ---------------------------------------------------------------- os detalhes do jogo
// Os detalhes do Moderno (TelaDetalhesModerna) na tela toda: o placar com o minuto, o arbitro
// e a linha do tempo de cima para baixo (a casa a esquerda, o visitante a direita, o minuto no
// meio, os marcadores do intervalo e do fim do tempo normal); Escalacao (quando a janela
// original mostraria as listas) troca para a substituicao, e Continuar (ou o Voltar) fecha.
static class DetalhesCelular
{
    public static CUi.Folha Folha(JogoAtual jg, int minuto, int acrescimo, bool noIntervalo, List<TelaDetalhesModerna.Item> itens,
        Action? escalacao, Action fechar)
    {
        var casa = jg.Equipa[Lado.Casa]; var fora = jg.Equipa[Lado.Visit];
        int gc = casa == null ? 0 : Mem.Golos(casa), gf = fora == null ? 0 : Mem.Golos(fora);
        var corpo = new StackPanel { Spacing = 10 };
        corpo.Children.Add(PC.Placar(casa, fora, gc, gf, noIntervalo ? T("Intervalo") : minuto <= 0 ? "0'" : MinutoTexto.Texto(minuto, acrescimo)));
        corpo.Children.Add(PC.Secao(T("Lances")));
        if (itens.Count == 0) corpo.Children.Add(CUi.Txt(T("Ainda sem lances"), 14, CUi.Retro ? W95.Branco : CUi.Fraco));
        else corpo.Children.Add(CUi.Grupo(itens.Select(Linha)));
        var continuar = new CUi.Botao(T("Continuar"), true, fechar);
        Control rodape = escalacao != null ? CUi.Botoes(new CUi.Botao(T("Escalação"), false, escalacao), continuar) : CUi.Botoes(continuar);
        string sub = jg.Arbitro is { } a ? T("Árbitro") + ": " + P98.Texto(a.Nome) + " (" + P98.NomePais(P98.Texto(a.Pais)) + ")" : "";
        var f = new CUi.Folha($"{P98.NomeEquipa(casa)} - {P98.NomeEquipa(fora)}", corpo, rodape, sub, inteira: true, fechar: fechar);
        // aberta durante o jogo: a linha do tempo mostra os ultimos lances
        corpo.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            for (Avalonia.Visual? v = corpo; v != null; v = Avalonia.VisualTree.VisualExtensions.GetVisualParent(v))
                if (v is ScrollViewer sv) { sv.ScrollToEnd(); break; }
        }, DispatcherPriority.Loaded);
        return f;
    }

    // uma linha da linha do tempo: o minuto no meio, o lance do lado da equipa
    static Control Linha(TelaDetalhesModerna.Item it)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,52,*"), MinHeight = 50 };
        // o fio vertical no meio
        var fio = new Border { Width = 2, Background = CUi.Linha, HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumn(fio, 1);
        g.Children.Add(fio);
        if (it.Tipo == 0)
        {
            // o marcador ("Intervalo 1 - 0") no meio
            var e = CUi.Etiqueta(it.Nome + "  " + it.Detalhe);
            e.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumnSpan(e, 3);
            g.Children.Add(e);
            return new Border { Child = g, Padding = new Thickness(8, 0) };
        }
        var m = CUi.Txt(MinutoTexto.Texto(it.Minuto, it.Acrescimo), it.Acrescimo > 0 ? 10.5 : 12.5, null, FontWeight.Bold);
        m.HorizontalAlignment = HorizontalAlignment.Center;
        var circulo = new Border
        {
            MinWidth = 38, Height = 30, CornerRadius = new CornerRadius(CUi.Retro ? 0 : 15), Background = CUi.Retro ? W95.Face : TemaVerde.CartaoForte,
            BorderBrush = CUi.Linha, BorderThickness = new Thickness(1.5), Child = m, Padding = new Thickness(3, 0),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(circulo, 1);
        g.Children.Add(circulo);
        bool casa = it.Lado == Lado.Casa;
        // o texto e o icone junto ao meio (o texto encolhe e quebra, nunca passa por cima do minuto)
        var s = new Grid { ColumnDefinitions = new ColumnDefinitions(casa ? "*,8,Auto" : "Auto,8,*"), VerticalAlignment = VerticalAlignment.Center };
        var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        bool apagado = it.Tipo is TipoLance.GolAnulado or TelaDetalhesModerna.PenaltiFalhado;
        var n = CUi.Txt(it.Nome, 14, apagado ? CUi.Fraco : null, FontWeight.SemiBold);
        var d = CUi.Txt(it.Detalhe, 12, CUi.Fraco, FontWeight.Normal, true);
        n.HorizontalAlignment = d.HorizontalAlignment = casa ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        n.TextAlignment = d.TextAlignment = casa ? TextAlignment.Right : TextAlignment.Left;
        txt.Children.Add(n);
        txt.Children.Add(d);
        int tipo = it.Tipo == TelaDetalhesModerna.Substituicao ? 101 : it.Tipo == TelaDetalhesModerna.PenaltiFalhado ? 102 : it.Tipo;
        var ic = new IconeLanceC(tipo, it.SegundoAmarelo, 18);
        Grid.SetColumn(txt, casa ? 0 : 2);
        Grid.SetColumn(ic, casa ? 2 : 0);
        s.Children.Add(txt);
        s.Children.Add(ic);
        s.Margin = new Thickness(0, 4);
        Grid.SetColumn(s, casa ? 0 : 2);
        g.Children.Add(s);
        g.ClipToBounds = true;
        return new Border { Child = g, Padding = new Thickness(4, 0) };
    }
}

// ---------------------------------------------------------------- o patrocinio
// A proposta numa folha: o logo da marca (ou a inicial), o nome e o tipo, as linhas (valor,
// duracao, multa) e a pergunta; Nao/Sim (no Retro, como o original) ou Recusar/Aceitar. O
// Voltar recusa (o fechar e o Esc do original).
static class PatrocinioCelular
{
    public static CUi.Folha Folha(Proposta p, Marcas? marcas, Moeda moeda, Action<bool> responder)
    {
        // no Retro, a janela do computador adaptada (PartidaCelularRetro.cs)
        if (CUi.Retro) return PatrocinioRetro.Folha(p, marcas, moeda, responder);
        string Ext(long v) => FormatoDinheiro.Valor(moeda, v);
        var corpo = new StackPanel { Spacing = 14 };
        var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,14,*") };
        var logo = TelaPatrocinio.Logo(p.Registro != null ? marcas?.CaminhoLogo(p.Registro) : null);
        Control dentro;
        if (logo != null) dentro = new Image { Source = logo, Stretch = Stretch.Uniform, Width = 120, Height = 60 };
        else
        {
            var l = CUi.Txt(p.Nome.Length > 0 ? p.Nome[..1] : "?", 32, CUi.Retro ? W95.Marinho : ClubeModerno.Escuro, FontWeight.Bold);
            l.HorizontalAlignment = HorizontalAlignment.Center;
            dentro = l;
        }
        var placa = new Border
        {
            Width = 132, Height = 72, CornerRadius = new CornerRadius(CUi.Retro ? 0 : 10), Child = dentro,
            Background = CUi.Retro ? W95.Branco : TemaVerde.DiscoLogo, BorderBrush = CUi.Retro ? W95.Escura : null,
            BorderThickness = new Thickness(CUi.Retro ? 1 : 0),
        };
        topo.Children.Add(placa);
        var nome = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
        nome.Children.Add(CUi.Txt(p.Tipo switch
        {
            Proposta.Renovacao => T("Renovação de patrocinador master"),
            Proposta.Pontual => T("Patrocínio no uniforme"),
            _ => T("Patrocinador master"),
        }, 12.5, CUi.Fraco, FontWeight.SemiBold, true));
        nome.Children.Add(CUi.Txt(p.Nome, 20, null, FontWeight.Bold, true));
        Grid.SetColumn(nome, 2);
        topo.Children.Add(nome);
        corpo.Children.Add(topo);

        bool pontual = p.Tipo == Proposta.Pontual;
        string n = p.Quantidade.ToString();
        var linhas = new StackPanel { Spacing = 2 };
        linhas.Children.Add(CUi.Linha_(T("Valor"), Ext(p.Valor) + (pontual ? T(" por jogo") : T(" por temporada")), CUi.Bom));
        linhas.Children.Add(CUi.Linha_(T("Duração"), n + (pontual ? (p.Quantidade == 1 ? T(" jogo") : T(" jogos")) : (p.Quantidade == 1 ? T(" temporada") : T(" temporadas")))));
        if (!pontual && p.Multa != 0) linhas.Children.Add(CUi.Linha_(T("Multa pela troca"), Ext(p.Multa), CUi.Erro));
        corpo.Children.Add(new Border
        {
            Background = CUi.Retro ? W95.Branco : TemaVerde.CartaoHub, CornerRadius = new CornerRadius(CUi.Retro ? 0 : 14),
            BorderBrush = CUi.Retro ? W95.Escura : CUi.Linha, BorderThickness = new Thickness(1), Padding = new Thickness(12, 8), Child = linhas,
        });
        corpo.Children.Add(CUi.Txt(pontual ? T("Para estampar a marca no uniforme. Aceita?")
            : p.Tipo == Proposta.Renovacao ? T("Para renovar o contrato de patrocinador master. Aceita?")
            : T("Para ser o patrocinador master do seu time. Aceita?"), 15, null, FontWeight.Medium, true));

        var nao = new CUi.Botao(T(CUi.Retro ? "Não" : "Recusar"), false, () => responder(false));
        var sim = new CUi.Botao(T(CUi.Retro ? "Sim" : "Aceitar"), true, () => responder(true));
        return new CUi.Folha(T("Patrocínio"), corpo, CUi.Botoes(nao, sim), T("Proposta de patrocínio"), fechar: () => responder(false));
    }
}
