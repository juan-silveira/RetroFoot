using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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

// As janelas da partida no celular na pele Retro: as do computador (TelaSubstituicao,
// TelaSubstituicaoForcada, TelaPenalti, TelaPenaltis, TelaSorteio, TelaPatrocinio) adaptadas a
// tela em pe e ao toque. Os mesmos elementos, textos e cores (a janela, a letra e as listas nas
// cores da equipa; o verde-claro dos penaltis, o amarelo-claro do sorteio, o vermelho do
// patrocinio), os botoes com os glifos do original, tudo empilhado na largura da tela. As
// classes do celular (PartidaCelularJanelas.cs, PartidaCelular.cs) passam para estas quando o
// visual e o Retro; as regras e os relogios continuam nas janelas originais.

// ---------------------------------------------------------------- pecas do Retro
static class PR
{
    public static readonly IBrush VerdeClaro = W95.Cor(0x80, 0xFF, 0x80);     // Color = 8454016
    public static readonly IBrush AmareloClaro = W95.Cor(0xFF, 0xFF, 0x80);   // Color = 8454143
    static readonly IBrush Cinza80 = W95.Cor(0x80, 0x80, 0x80);

    public static TextBlock Txt(string s, double tam, IBrush cor, bool negrito = false, bool quebra = false) =>
        CUi.Txt(s, tam, cor, negrito ? FontWeight.Bold : FontWeight.Normal, quebra);

    // a caixa da TListBox (W95.Caixa): o anel sombra/realce e por dentro o fio da moldura
    public static Border CaixaLista(Control c, IBrush fundo) => new()
    {
        BoxShadow = BoxShadows.Parse("inset 1 1 0 0 #A6A6A6, inset -1 -1 0 0 #FFFFFF"), Padding = new Thickness(1),
        Child = new Border { BorderBrush = W95.Moldura, BorderThickness = new Thickness(1), Background = fundo, Padding = new Thickness(1), Child = c, ClipToBounds = true },
    };

    // o TBevel (bsLowered) em volta das listas da lesao e do penalti
    public static Border Bevel(Control c) => new()
    {
        BoxShadow = BoxShadows.Parse("inset 1 1 0 0 #808080, inset -1 -1 0 0 #FFFFFF"), Padding = new Thickness(8), Child = c,
    };

    // a lista de jogadores de um dialogo (P98.ListaJogadores): nas cores da equipa (fundo e
    // letra), a posicao, o nome com a estrela em negrito e a forca em negrito; o escolhido com
    // as cores trocadas. escolheu = null: a lista nao responde (o original desabilitado)
    public static (Control Lista, Action<int> Marcar) ListaJogadores(IReadOnlyList<SJogador> js, SEquipa e, Action<int>? escolheu, Func<int, bool>? habilitada = null)
    {
        var letra = P98.Letra(e); var fundo = P98.Fundo(e);
        var itens = new List<(Clicavel C, TextBlock[] Ts)>();
        void Marcar(int k)
        {
            for (int i = 0; i < itens.Count; i++)
            {
                bool m = i == k;
                itens[i].C.Background = m ? letra : Brushes.Transparent;
                foreach (var t in itens[i].Ts) t.Foreground = m ? fundo : letra;
            }
        }
        var p = new StackPanel();
        for (int i = 0; i < js.Count; i++)
        {
            int k = i;
            var j = js[i];
            var pos = Txt(Idioma.Posicao(Math.Min((int)j.Posicao, 3)), 14, letra);
            var nome = Txt(P98.NomeJogador(j), 15, letra, true);
            var f = Txt(j.Forca.ToString(), 15, letra, true);
            f.HorizontalAlignment = HorizontalAlignment.Right;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*,40") };
            g.Children.Add(pos);
            Grid.SetColumn(nome, 1); g.Children.Add(nome);
            Grid.SetColumn(f, 2); g.Children.Add(f);
            if (habilitada != null && !habilitada(i)) g.Opacity = 0.5;
            var c = new Clicavel { Background = Brushes.Transparent, CorFoco = null, MinHeight = 40, Padding = new Thickness(8, 0, 10, 0), Child = g };
            if (escolheu == null) c.Cursor = Avalonia.Input.Cursor.Default;
            else c.Clique += () => { Marcar(k); escolheu(k); };
            itens.Add((c, new[] { pos, nome, f }));
            p.Children.Add(c);
        }
        return (CaixaLista(p, fundo), Marcar);
    }

    // o TGroupBox (GrupoPartida): o fio gravado e o titulo por cima, a esquerda; na cor dada
    // (o quadro da equipa na substituicao: o fundo e a letra dela)
    public static Control Grupo(string? titulo, Control conteudo, IBrush? fundo = null, IBrush? cor = null, double tam = 13)
    {
        fundo ??= W95.Face;
        cor ??= W95.Preto;
        var moldura = new Border
        {
            Margin = new Thickness(0, titulo != null ? 9 : 0, 0, 0), Padding = new Thickness(10, titulo != null ? 16 : 10, 10, 10),
            BorderBrush = Cinza80, BorderThickness = new Thickness(1), Background = fundo,
            BoxShadow = BoxShadows.Parse("inset 1 1 0 0 #FFFFFF"), Child = conteudo,
        };
        if (titulo == null) return moldura;
        var rot = new Border
        {
            Background = fundo, Padding = new Thickness(4, 0), Margin = new Thickness(8, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Child = Txt(titulo, tam, cor, true),
        };
        return new Panel { Children = { moldura, rot } };
    }

    // a TCheckBox ao toque: a caixa branca afundada com o visto e o texto; a linha toda se toca
    public static Control Caixinha(string texto, bool marcada, Action<bool> mudou)
    {
        var visto = new Path
        {
            Data = Geometry.Parse("M 4,10 L 8,14 L 16,5"), Stroke = W95.Preto, StrokeThickness = 2.5,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsVisible = marcada,
        };
        var caixa = new Border
        {
            Width = 22, Height = 22, Background = W95.Branco, VerticalAlignment = VerticalAlignment.Center, Child = visto,
            BoxShadow = BoxShadows.Parse("inset 1 1 0 0 #808080, inset 2 2 0 0 #000000, inset -1 -1 0 0 #FFFFFF, inset -2 -2 0 0 #DFDFDF"),
        };
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { caixa, Txt(texto, 15, W95.Preto) } };
        var c = new Clicavel { Background = Brushes.Transparent, CorFoco = null, MinHeight = 44, Padding = new Thickness(2, 0, 10, 0), Child = s };
        c.Clique += () => { marcada = !marcada; visto.IsVisible = marcada; mudou(marcada); };
        return c;
    }

    // o glifo do TBitBtn (Botoes/<nome>.png: n glifos lado a lado, o 2o o desligado) antes do
    // texto do botao do kit
    public static CUi.Botao Glifo(CUi.Botao b, string arquivo, int n = 2, double tam = 22)
    {
        if (W95.Imagem(arquivo) is not { } bmp || b.Child is not TextBlock t) return b;
        int w = bmp.PixelSize.Width / n, h = bmp.PixelSize.Height;
        IImage lig = new CroppedBitmap(bmp, new PixelRect(0, 0, w, h));
        IImage? des = n > 1 ? new CroppedBitmap(bmp, new PixelRect(w, 0, w, h)) : null;
        var img = new Image { Width = tam * w / h, Height = tam, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapInterpolationMode(img, BitmapInterpolationMode.None);
        void Pintar() => img.Source = b.IsEffectivelyEnabled ? lig : des ?? lig;
        b.Child = null;
        b.Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Children = { img, t } };
        b.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Input.InputElement.IsEffectivelyEnabledProperty) Pintar(); };
        Pintar();
        return b;
    }

    // a janela na cor dela (a equipa, o verde-claro...): o corpo e o rodape da folha do kit
    public static CUi.Folha Pintar(CUi.Folha f, IBrush fundo)
    {
        if (f.Content is not Border b) return f;
        b.Background = fundo;
        if (b.Child is DockPanel d)
            foreach (var c in d.Children)
                if (c is Border r && r.Child is ContentControl) { r.Background = fundo; r.BorderBrush = Brushes.Transparent; }
        return f;
    }
}

// ---------------------------------------------------------------- a substituicao
// A TsubstitutionDlg na tela toda, na face cinza: o titulo "Casa, golos - Fora, golos", os
// lances como texto ("1:0 Nome 12'"), as caixinhas, o Arbitro no quadro e (completa) o quadro
// da equipa nas cores dela com os titulares e o banco; Substituir e Continuar com os glifos.
sealed class SubstituicaoRetro : UserControl
{
    readonly JogoAtual _jg;
    readonly SEquipa _e;
    readonly int _motivo;
    readonly Opcoes _op;
    readonly Action _fechar;
    readonly List<SJogador> _tit = new(), _res = new();
    readonly StackPanel _lances = new() { Spacing = 2 };
    readonly ContentControl _campo = new(), _banco = new();
    readonly CUi.Botao _continuar;
    readonly CUi.Botao? _substituir;
    bool _amarelos, _pode;
    int _selT = -1, _selR = -1;

    public SubstituicaoRetro(JogoAtual jg, int lado, int motivo, Opcoes op, bool cartoesAmarelos, bool completa, bool pode, Action fechar)
    {
        _jg = jg; _e = jg.Equipa[lado]!; _motivo = motivo; _op = op; _pode = pode; _fechar = fechar;
        var casa = jg.Equipa[Lado.Casa]; var fora = jg.Equipa[Lado.Visit];
        string titulo = $"{P98.NomeEquipa(casa)}, {(casa == null ? 0 : Mem.Golos(casa))} - {P98.NomeEquipa(fora)}, {(fora == null ? 0 : Mem.Golos(fora))}";

        var corpo = new StackPanel { Spacing = 10 };
        _lances.Margin = new Thickness(4, 0);
        corpo.Children.Add(_lances);
        var caixas = new WrapPanel { ItemSpacing = 6 };
        if (cartoesAmarelos)
        {
            caixas.Children.Add(PR.Caixinha(T("Cartão amarelo"), false, v => { _amarelos = v; Lances(); }));
            caixas.Children.Add(PR.Caixinha(T("Cartão vermelho"), op.VerExpulsoes, v => { _op.VerExpulsoes = v; Lances(); }));
            caixas.Children.Add(PR.Caixinha(T("Lesão"), op.VerLesoes, v => { _op.VerLesoes = v; Lances(); }));
        }
        else
        {
            caixas.Children.Add(PR.Caixinha(T("Cartões vermelhos"), op.VerExpulsoes, v => { _op.VerExpulsoes = v; Lances(); }));
            caixas.Children.Add(PR.Caixinha(T("Lesões"), op.VerLesoes, v => { _op.VerLesoes = v; Lances(); }));
        }
        corpo.Children.Add(caixas);
        string arb = jg.Arbitro is { } a ? P98.Texto(a.Nome) + " (" + P98.NomePais(P98.Texto(a.Pais)) + ")" : "";
        corpo.Children.Add(PR.Grupo(T("Árbitro"), PR.Txt(arb, 15, W95.Preto, true)));

        _continuar = PR.Glifo(new CUi.Botao(T("Continuar"), true, fechar), "Botoes/BBOK.png");
        Control rodape;
        if (completa)
        {
            foreach (var p in _e.Jogadores)
                switch (Mem.Escalacao(p))
                {
                    case Mem.EscTitular: _tit.Add(p); break;
                    case Mem.EscReserva: _res.Add(p); break;
                }
            var listas = new StackPanel { Spacing = 10, Children = { _campo, _banco } };
            corpo.Children.Add(PR.Grupo(P98.NomeEquipa(_e), listas, P98.Fundo(_e), P98.Letra(_e), 15));
            _substituir = PR.Glifo(new CUi.Botao(T("Substituir"), false, Substituir) { Habilitado = false }, "Botoes/SUBSTITUIR.png");
            _substituir.IsVisible = pode;
            rodape = CUi.Botoes(_substituir, _continuar);
        }
        else rodape = CUi.Botoes(_continuar);
        var f = new CUi.Folha(titulo, corpo, rodape, inteira: true, fechar: fechar) { Background = W95.Face };
        Content = f;
        Lances();
        Montar();
        Atualizar();
    }

    // seg02:24a1, os textos da janela do computador
    void Lances()
    {
        _lances.Children.Clear();
        int gc = 0, gf = 0;
        foreach (var l in _jg.Lances)
        {
            string nome = P98.NomeSimples(l.Jogador);
            string min = MinutoTexto.SemAspa(l.Minuto, l.Acrescimo);
            string? s = null;
            switch (l.Tipo)
            {
                case TipoLance.Gol:
                case TipoLance.Penalti:
                    if (l.Lado == Lado.Casa) gc++; else gf++;
                    s = $"{gc}:{gf} {nome} {min}'" + (l.Tipo == TipoLance.Penalti ? T(" (pen.)") : "");
                    break;
                case TipoLance.Amarelo: if (_amarelos) s = T("{0} amarelo {1}'", nome, min); break;
                case TipoLance.Expulsao: if (_op.VerExpulsoes) s = T("{0} expulso {1}'", nome, min); break;
                case TipoLance.Lesao: if (_op.VerLesoes) s = T("{0} lesionou-se {1}'", nome, min); break;
                case TipoLance.GolAnulado: s = T("{0} {1}' - Anulado", nome, min); break;
            }
            if (s != null) _lances.Children.Add(PR.Txt(s, 15, W95.Preto, false, true));
        }
    }

    // as duas listas nas cores da equipa (so respondem quando pode substituir)
    void Montar()
    {
        if (_substituir == null) return;
        var (lt, mt) = PR.ListaJogadores(_tit, _e, _pode ? i => { _selT = i; MontarBanco(); Atualizar(); } : null);
        if (_pode && _selT >= 0) mt(_selT);
        _campo.Content = lt;
        MontarBanco();
    }

    void MontarBanco()
    {
        var sai = _selT >= 0 && _selT < _tit.Count ? _tit[_selT] : null;
        var (lr, mr) = PR.ListaJogadores(_res, _e, _pode ? i => { _selR = i; Atualizar(); } : null,
            _pode ? i => sai == null || TelaSubstituicao.PodeTrocar(sai, _res[i], _motivo) : null);
        if (_pode && _selR >= 0) mr(_selR);
        _banco.Content = lr;
    }

    // seg02:2905: o Substituir some (e as listas param) quando acabam as substituicoes
    void Atualizar()
    {
        if (_substituir == null) return;
        bool temT = _selT >= 0 && _selT < _tit.Count, temR = _selR >= 0 && _selR < _res.Count;
        _substituir.Habilitado = _pode && temT && temR && TelaSubstituicao.PodeTrocar(_tit[_selT], _res[_selR], _motivo);
        if (_pode && Mem.Substituicoes(_e) <= 0)
        {
            _pode = false;
            _selT = _selR = -1;
            _substituir.IsVisible = false;
            Montar();
        }
        // sem o Substituir, o Continuar fica com a largura toda
        Grid.SetColumn(_continuar, _substituir.IsVisible ? 1 : 0);
        Grid.SetColumnSpan(_continuar, _substituir.IsVisible ? 1 : 2);
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
}

// ---------------------------------------------------------------- a lesao
// A TforcedSubstitutionDlg: a janela, o texto e a lista nas cores da equipa e o OK com o visto
static class LesaoRetro
{
    public static CUi.Folha Folha(SEquipa e, SJogador saiu, Action<SJogador?> confirmar)
    {
        var letra = P98.Letra(e); var fundo = P98.Fundo(e);
        var res = e.Jogadores.Where(p => Mem.Escalacao(p) == Mem.EscReserva).ToList();
        int goleiros = res.Count(p => p.Posicao == 0);
        bool algum = res.Exists(p => TelaSubstituicaoForcada.Elegivel(saiu, p, goleiros));
        int sel = -1;
        var ok = PR.Glifo(new CUi.Botao(T("OK"), true, () =>
            confirmar(sel >= 0 && TelaSubstituicaoForcada.Elegivel(saiu, res[sel], goleiros) ? res[sel] : null)) { Habilitado = !algum }, "Botoes/BBOK.png");
        var corpo = new StackPanel { Spacing = 14 };
        corpo.Children.Add(PR.Txt(T("{0} ({1}) lesionou-se e tem de ser substituído.\rEscolha o jogador a entrar.",
            P98.NomeJogador(saiu), P98.Posicao(saiu.Posicao)).Replace('\r', '\n'), 15, letra, true, true));
        var (lista, _) = PR.ListaJogadores(res, e, i =>
        {
            sel = i;
            ok.Habilitado = TelaSubstituicaoForcada.Elegivel(saiu, res[i], goleiros) || !algum;
        }, i => TelaSubstituicaoForcada.Elegivel(saiu, res[i], goleiros));
        corpo.Children.Add(PR.Bevel(lista));
        ok.HorizontalAlignment = HorizontalAlignment.Stretch;
        return PR.Pintar(new CUi.Folha(P98.NomeEquipa(e), corpo, ok), fundo);
    }
}

// ---------------------------------------------------------------- o penalti
// A TpenaltyDlg: nas cores de quem cobra, "P E N A L T I" grande, o pedido ao treinador, a
// lista dos titulares e o Chutar; depois "Marcador: Nome" e o resultado grande em italico.
sealed class PenaltiRetro : UserControl
{
    public event Action<SJogador>? Chutou;

    readonly List<SJogador> _titulares;
    readonly CUi.Folha _folha;
    readonly Control? _escolha;
    readonly TextBlock _marcador, _resultado;
    readonly CUi.Botao? _chutar;
    int _sel = -1;

    public PenaltiRetro(SEquipa atacante, string treinador, List<SJogador> titulares, bool humano)
    {
        _titulares = titulares;
        var letra = P98.Letra(atacante); var fundo = P98.Fundo(atacante);
        var corpo = new StackPanel { Spacing = 12, MinHeight = 260 };
        var tit = new TextBlock
        {
            Text = T("P E N A L T I"), FontFamily = P98.FamTimes, FontSize = 40 * Acessibilidade.Texto, FontWeight = FontWeight.Bold,
            Foreground = letra, HorizontalAlignment = HorizontalAlignment.Center,
        };
        corpo.Children.Add(tit);
        _marcador = PR.Txt("", 16, letra, true, true);
        _marcador.IsVisible = false;
        corpo.Children.Add(_marcador);
        _resultado = new TextBlock
        {
            FontFamily = P98.FamTimes, FontSize = 50 * Acessibilidade.Texto, FontWeight = FontWeight.Bold, FontStyle = FontStyle.Italic,
            Foreground = letra, HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false, Margin = new Thickness(0, 24, 0, 0), Padding = new Thickness(4, 0, 16, 0),
        };
        corpo.Children.Add(_resultado);
        Control? rodape = null;
        if (humano)
        {
            var escolha = new StackPanel { Spacing = 12 };
            escolha.Children.Add(PR.Txt(T("{0}, escolha o jogador para marcar o penalti!", treinador), 15, letra, true, true));
            _chutar = PR.Glifo(new CUi.Botao(T("Chutar"), true, Chutar) { Habilitado = false }, "Botoes/BBOK.png");
            var (lista, _) = PR.ListaJogadores(titulares, atacante, i => { _sel = i; _chutar.Habilitado = true; });
            escolha.Children.Add(PR.Bevel(lista));
            corpo.Children.Add(_escolha = escolha);
            rodape = CUi.Botoes(_chutar);
        }
        Content = _folha = PR.Pintar(new CUi.Folha(P98.NomeEquipa(atacante), corpo, rodape), fundo);
    }

    void Chutar()
    {
        if (_chutar == null || !_chutar.Habilitado || !_chutar.IsVisible) return;
        if (_sel < 0 || _sel >= _titulares.Count) return;
        Chutou?.Invoke(_titulares[_sel]);
    }

    // seg08:3751: some a escolha e aparece "Marcador: Nome"
    public void MostrarBatedor(SJogador b)
    {
        if (_escolha != null) _escolha.IsVisible = false;
        if (_chutar != null) _chutar.IsVisible = false;
        _folha.Rodape(null);
        _marcador.Text = T("Marcador: {0}", P98.NomeJogador(b));
        _marcador.IsVisible = true;
    }

    public void MostrarResultado(int r)
    {
        _resultado.Text = P98.Resultado(r);
        _resultado.IsVisible = true;
    }
}

// ---------------------------------------------------------------- os penaltis
// A TPenaltiesDlg no verde-claro: o titulo em italico, o placar azul, as equipas nas cores
// delas nas pontas e a lista das rodadas (batedor e resultado de cada lado; a ultima escolhida)
sealed class PenaltisRetro : UserControl
{
    readonly StackPanel _linhas = new();
    readonly ScrollViewer _rolar;
    readonly TextBlock _placar;

    public PenaltisRetro(JogoAtual jg)
    {
        var corpo = new DockPanel();
        var cab = new StackPanel { Spacing = 6 };
        var t = new TextBlock
        {
            Text = T("D e s e m p a t e   p o r   P e n a l t i s"), FontFamily = P98.FamTimes, FontSize = 21 * Acessibilidade.Texto,
            FontWeight = FontWeight.Bold, FontStyle = FontStyle.Italic, Foreground = W95.Preto, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        cab.Children.Add(t);
        _placar = PR.Txt("0:0", 44, W95.Azul, true);
        _placar.HorizontalAlignment = HorizontalAlignment.Center;
        cab.Children.Add(_placar);
        var nomes = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 6) };
        for (int lado = 0; lado < 2; lado++)
        {
            var e = jg.Equipa[lado];
            var n = new Border
            {
                Background = P98.Fundo(e), Padding = new Thickness(3, 1), VerticalAlignment = VerticalAlignment.Center,
                Child = PR.Txt(P98.NomeEquipa(e), 16, P98.Letra(e), true),
            };
            Grid.SetColumn(n, lado == Lado.Casa ? 0 : 2);
            nomes.Children.Add(n);
        }
        cab.Children.Add(nomes);
        DockPanel.SetDock(cab, Dock.Top);
        corpo.Children.Add(cab);
        _rolar = new ScrollViewer { Content = _linhas, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        corpo.Children.Add(PR.CaixaLista(_rolar, W95.Branco));
        var f = new CUi.Folha(T("Penaltis"), new Border { Child = corpo, Margin = new Thickness(14, 10, 14, 14) }, null, rolar: false);
        Content = PR.Pintar(f, PR.VerdeClaro);
        MinHeight = 420;
    }

    public void Atualizar(List<(SJogador? Bat, int Res)[]> pares, int[] golos)
    {
        _placar.Text = $"{golos[Lado.Casa]}:{golos[Lado.Visit]}";
        _linhas.Children.Clear();
        for (int i = 0; i < pares.Count; i++)
        {
            bool esc = i == pares.Count - 1;
            var cor = esc ? W95.Branco : W95.Preto;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,14,*,Auto"), MinHeight = 36 };
            for (int k = 0; k < 2; k++)
            {
                var (b, res) = pares[i][k];
                if (b == null) continue;
                var n = PR.Txt(P98.NomeJogador(b), 15, cor);
                var r = PR.Txt(res != 0 ? P98.Resultado(res) : "", 15, cor, true);
                r.Margin = new Thickness(6, 0, 0, 0);
                Grid.SetColumn(n, k * 3);
                Grid.SetColumn(r, k * 3 + 1);
                g.Children.Add(n);
                g.Children.Add(r);
            }
            _linhas.Children.Add(new Border { Background = esc ? W95.Destaque : Brushes.Transparent, Padding = new Thickness(4, 0), Child = g });
        }
        Dispatcher.UIThread.Post(_rolar.ScrollToEnd, DispatcherPriority.Loaded);
    }
}

// ---------------------------------------------------------------- o sorteio da Taca
// A TsorteioDlg no amarelo-claro: em cima as duas etiquetas do jogo sorteado (nas cores das
// equipas), a lista dos jogos (o ultimo escolhido) e a lista das equipas da Taca em colunas
sealed class SorteioRetro : UserControl
{
    readonly List<SEquipa> _pote;
    readonly List<string> _feitos = new();
    readonly Border _casa, _fora;
    readonly StackPanel _jogos = new();
    readonly ScrollViewer _rolarJogos;
    readonly Grid _equipas = new() { ColumnDefinitions = new ColumnDefinitions("*,*") };

    public SorteioRetro(List<SEquipa> pote)
    {
        _pote = pote;
        Border Etiqueta() => new() { MinHeight = 30, Padding = new Thickness(4, 2) };
        _casa = Etiqueta();
        _fora = Etiqueta();
        var vagas = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*"), Margin = new Thickness(0, 0, 0, 10) };
        vagas.Children.Add(_casa);
        Grid.SetColumn(_fora, 2);
        vagas.Children.Add(_fora);
        Mostrar(_casa, null);
        Mostrar(_fora, null);
        _rolarJogos = new ScrollViewer { Content = _jogos, Height = 150, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var d = new DockPanel { Margin = new Thickness(12, 12, 12, 12) };
        DockPanel.SetDock(vagas, Dock.Top);
        d.Children.Add(vagas);
        var lj = PR.CaixaLista(_rolarJogos, W95.Branco);
        lj.Margin = new Thickness(0, 0, 0, 10);
        DockPanel.SetDock(lj, Dock.Top);
        d.Children.Add(lj);
        d.Children.Add(PR.CaixaLista(new ScrollViewer
        {
            Content = new Border { Padding = new Thickness(4, 2), Child = _equipas },
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        }, W95.Branco));
        var f = new CUi.Folha(T("Sorteio dos jogos da taça"), d, null, inteira: true, rolar: false) { Background = PR.AmareloClaro };
        Content = f;
        Equipas();
    }

    // o nome na cor da equipa (vazio: a etiqueta some no amarelo)
    static void Mostrar(Border b, SEquipa? e)
    {
        b.Background = e == null ? Brushes.Transparent : P98.Fundo(e);
        var t = PR.Txt(e == null ? "" : P98.NomeEquipa(e), 16, e == null ? W95.Preto : P98.Letra(e));
        t.HorizontalAlignment = HorizontalAlignment.Center;
        b.Child = t;
    }

    // as equipas da Taca que faltam, descendo em duas colunas (a TListBox de 3 colunas)
    void Equipas()
    {
        _equipas.Children.Clear();
        _equipas.RowDefinitions.Clear();
        int porColuna = (_pote.Count + 1) / 2;
        for (int r = 0; r < porColuna; r++) _equipas.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int i = 0; i < _pote.Count; i++)
        {
            var t = PR.Txt(P98.NomeEquipa(_pote[i]), 14, W95.Preto);
            t.Margin = new Thickness(0, 2);
            Grid.SetColumn(t, i / Math.Max(1, porColuna));
            Grid.SetRow(t, i % Math.Max(1, porColuna));
            _equipas.Children.Add(t);
        }
    }

    public void Casa(SEquipa? e) { Mostrar(_fora, null); Mostrar(_casa, e); Tirar(e); }
    public void Fora(SEquipa? e) { Mostrar(_fora, e); Tirar(e); }

    void Tirar(SEquipa? e)
    {
        if (e == null || !_pote.Remove(e)) return;
        Equipas();
    }

    public void Jogo(SEquipa? casa, SEquipa? fora)
    {
        _feitos.Add(P98.NomeEquipa(casa) + " - " + P98.NomeEquipa(fora));
        _jogos.Children.Clear();
        for (int i = 0; i < _feitos.Count; i++)
        {
            bool esc = i == _feitos.Count - 1;
            _jogos.Children.Add(new Border
            {
                Background = esc ? W95.Destaque : Brushes.Transparent, Padding = new Thickness(4, 3),
                Child = PR.Txt(_feitos[i], 14, esc ? W95.Branco : W95.Preto),
            });
        }
        Dispatcher.UIThread.Post(_rolarJogos.ScrollToEnd, DispatcherPriority.Loaded);
    }
}

// ---------------------------------------------------------------- o patrocinio
// A proposta do patch: a janela vermelha (ou o FUNDO.BMP da pasta das marcas), "Proposta de
// patrocinio" em azul-marinho, o texto do original em preto, o logo e os botoes Sim e Nao
static class PatrocinioRetro
{
    public static CUi.Folha Folha(Proposta p, Marcas? marcas, Moeda moeda, Action<bool> responder)
    {
        var corpo = new StackPanel { Spacing = 14 };
        corpo.Children.Add(PR.Txt(T("Proposta de patrocínio"), 19, W95.Marinho, true, true));
        var texto = Patrocinios.Texto(p, v => FormatoDinheiro.Valor(moeda, v)).Replace('\r', '\n');
        corpo.Children.Add(PR.Txt(texto, 15.5, W95.Preto, true, true));
        if (TelaPatrocinio.Logo(p.Registro != null ? marcas?.CaminhoLogo(p.Registro) : null) is { } img)
            corpo.Children.Add(new Image { Source = img, Stretch = Stretch.Uniform, Width = 180, Height = 90, HorizontalAlignment = HorizontalAlignment.Center });
        var sim = PR.Glifo(new CUi.Botao(T("Sim"), true, () => responder(true)), "Botoes/BBYES.png");
        var nao = PR.Glifo(new CUi.Botao(T("Não"), false, () => responder(false)), "Botoes/BBNO.png");
        var f = new CUi.Folha(T("Patrocínio"), corpo, CUi.Botoes(sim, nao), fechar: () => responder(false));
        IBrush fundo = TelaPatrocinio.Logo(marcas?.CaminhoFundo()) is { } bmp
            ? new ImageBrush(bmp) { Stretch = Stretch.Fill }
            : W95.Vermelho;
        return PR.Pintar(f, fundo);
    }
}
