using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RetroFoot.Motor.Partida;
using Mem = RetroFoot.Motor.Partida.Memoria;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;

namespace RetroFoot.App;

// Os outros dialogos da partida no visual Moderno: a lesao (TforcedSubstitutionDlg), o penalti
// (TpenaltyDlg), o desempate por penaltis (TPenaltiesDlg) e o sorteio da Taca (TsorteioDlg).
// As classes originais (TelaSubstituicaoForcada, TelaPenalti, TelaPenaltis, TelaSorteio)
// continuam com as regras e os relogios; estas so mostram.

// o cabecalho de equipa: logo, um rotulo pequeno e o nome
static class CabecalhoM
{
    public static Control Equipa(SEquipa? e, string rotulo, IBrush corRotulo, Control? direita = null)
    {
        var g = new DockPanel { Background = PM.Barra };
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(18, 14) };
        p.Children.Add(new EscudoM(e) { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Center });
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        t.Children.Add(new TextBlock { Text = rotulo.ToUpperInvariant(), FontSize = 11.5, FontWeight = FontWeight.Bold, Foreground = corRotulo, LetterSpacing = 0.8 });
        t.Children.Add(PM.Rotulo(P98.NomeEquipa(e), 19, PM.Texto, true));
        p.Children.Add(t);
        if (direita != null)
        {
            direita.Margin = new Thickness(0, 0, 18, 0);
            direita.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(direita, Dock.Right);
            g.Children.Add(direita);
        }
        g.Children.Add(p);
        return g;
    }

    public static Border Caixa(Control filho, Thickness margem) => new()
    {
        Background = TemaVerde.Cartao, CornerRadius = new CornerRadius(10), Padding = new Thickness(0, 4), Margin = margem, Child = filho,
        BorderBrush = TemaVerde.Borda, BorderThickness = new Thickness(1),
    };
}

// ---------------------------------------------------------------- a lesao

sealed class SubstituicaoForcadaModerna : UserControl
{
    public event Action? Fechou;

    readonly SEquipa _e;
    readonly SJogador _saiu;
    readonly List<SJogador> _res = new();
    readonly ListaM _lista;
    readonly BotaoM _ok;
    readonly int _goleiros;
    bool _fechou;

    public SubstituicaoForcadaModerna(SEquipa e, SJogador saiu)
    {
        _e = e; _saiu = saiu;
        FontFamily = PM.Inter;
        Focusable = true;
        Width = 480;
        foreach (var p in e.Jogadores)
            if (Mem.Escalacao(p) == Mem.EscReserva)
            {
                if (p.Posicao == 0) _goleiros++;
                _res.Add(p);
            }
        var cabeca = CabecalhoM.Equipa(e, Idioma.T("Lesão"), PM.Vermelho);

        string pos = P98.Posicao(saiu.Posicao);
        var texto = new StackPanel { Margin = new Thickness(18, 14, 18, 8), Spacing = 4 };
        var linha = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        linha.Children.Add(new IconeLance(TipoLance.Lesao) { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center });
        linha.Children.Add(new TextBlock
        {
            Text = Idioma.T("{0} ({1}) lesionou-se", P98.NomeJogador(saiu), pos), FontSize = 15, FontWeight = FontWeight.SemiBold,
            Foreground = PM.Texto, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        texto.Children.Add(linha);
        texto.Children.Add(PM.Rotulo(Idioma.T("Tem de ser substituído. Escolha o jogador a entrar."), 13, PM.Fraco));

        _lista = new ListaM { AlturaItem = 32, Vazia = Idioma.T("Banco vazio"), Height = Math.Clamp(_res.Count, 3, 9) * 32 };
        _lista.Quantos = () => _res.Count;
        bool algum = _res.Exists(Elegivel);
        _lista.Desenhar = (dc, i, r, esc) => { if (i < _res.Count) PM.LinhaJogador(dc, _res[i], r, esc, algum && !Elegivel(_res[i])); };
        _lista.Clique += _ => Atualizar();
        _lista.Duplo += _ => Ok();

        _ok = new BotaoM(Idioma.T("OK"), true) { Habilitado = !algum };
        _ok.Clique += Ok;
        var rodape = PM.Rodape(PM.Rotulo(Mem.Substituicoes(e) == 1 ? Idioma.T("1 substituição restante") : Idioma.T("{0} substituições restantes", Mem.Substituicoes(e)), 12, PM.Fraco), _ok);

        var p2 = new StackPanel();
        p2.Children.Add(cabeca);
        p2.Children.Add(texto);
        p2.Children.Add(CabecalhoM.Caixa(_lista, new Thickness(16, 0, 16, 16)));
        p2.Children.Add(rodape);
        Content = PM.Cartao(p2);
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => _lista.Focus(), DispatcherPriority.Loaded);
    }

    bool Elegivel(SJogador p) => TelaSubstituicaoForcada.Elegivel(_saiu, p, _goleiros);

    // seg06:38c1
    void Atualizar()
    {
        if (_lista.Sel < 0 || _lista.Sel >= _res.Count) return;
        _ok.Habilitado = Elegivel(_res[_lista.Sel]) || !_res.Exists(Elegivel);
    }

    void Ok()
    {
        if (_fechou || !_ok.Habilitado) return;
        TelaSubstituicaoForcada.Confirmar(_e, _saiu,
            _lista.Sel >= 0 && _lista.Sel < _res.Count && Elegivel(_res[_lista.Sel]) ? _res[_lista.Sel] : null);
        _fechou = true;
        Fechou?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.Enter) { Ok(); e.Handled = true; }
    }
}

// o icone de um lance (Icones da jornada) como controle
sealed class IconeLance : Control
{
    readonly int _tipo;
    public IconeLance(int tipo) { _tipo = tipo; IsHitTestVisible = false; }
    public override void Render(DrawingContext dc) => Icones.Desenhar(dc, _tipo, false, new Rect(Bounds.Size));
}

// ---------------------------------------------------------------- o penalti

sealed class PenaltiModerno : UserControl
{
    // o humano escolheu o batedor (Chutar)
    public event Action<SJogador>? Chutou;

    readonly List<SJogador> _titulares;
    readonly ListaM? _lista;
    readonly BotaoM? _chutar;
    readonly Control? _escolha, _rodape;
    readonly StackPanel _cena;
    readonly TextBlock _nome, _resultado, _espera;

    public PenaltiModerno(SEquipa atacante, string treinador, List<SJogador> titulares, bool humano)
    {
        _titulares = titulares;
        FontFamily = PM.Inter;
        Focusable = true;
        Width = 480;
        var cabeca = CabecalhoM.Equipa(atacante, Idioma.T("Penalti"), PM.Azul);

        // a cena: o batedor e, no tique 3, o resultado
        _cena = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsVisible = !humano };
        _cena.Children.Add(new TextBlock { Text = Idioma.T("MARCADOR"), FontSize = 11.5, FontWeight = FontWeight.Bold, Foreground = PM.Fraco, LetterSpacing = 0.8, HorizontalAlignment = HorizontalAlignment.Center });
        _nome = new TextBlock { FontSize = 24, FontWeight = FontWeight.Bold, Foreground = PM.Texto, HorizontalAlignment = HorizontalAlignment.Center };
        _cena.Children.Add(_nome);
        _espera = new TextBlock { Text = Idioma.T("Na marca de penalti…"), FontSize = 13, Foreground = PM.Fraco, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
        _cena.Children.Add(_espera);
        _resultado = new TextBlock { FontSize = 46, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };
        _cena.Children.Add(_resultado);

        var corpo = new Grid();
        Control? rodape = null;
        if (humano)
        {
            _lista = new ListaM { AlturaItem = 30 };
            _lista.Quantos = () => titulares.Count;
            _lista.Desenhar = (dc, i, r, esc) => { if (i < titulares.Count) PM.LinhaJogador(dc, titulares[i], r, esc, false); };
            _lista.Height = Math.Max(4, titulares.Count) * 30;
            _lista.Clique += _ => { if (_chutar != null) _chutar.Habilitado = true; };
            _lista.Duplo += _ => Chutar();
            var escolha = new StackPanel { Margin = new Thickness(16, 12, 16, 16), Spacing = 10 };
            escolha.Children.Add(PM.Rotulo(treinador.Length > 0 ? Idioma.T("{0}, escolha o jogador para marcar o penalti", treinador) : Idioma.T("Escolha o jogador para marcar o penalti"), 13, PM.Fraco));
            escolha.Children.Add(CabecalhoM.Caixa(_lista, new Thickness(0)));
            _escolha = escolha;
            corpo.Children.Add(escolha);
            _chutar = new BotaoM(Idioma.T("Chutar"), true) { Habilitado = false };
            _chutar.Clique += Chutar;
            rodape = _rodape = PM.Rodape(null, _chutar);
        }
        else corpo.Height = 200;
        corpo.Children.Add(_cena);

        var p = new DockPanel();
        DockPanel.SetDock(cabeca, Dock.Top);
        p.Children.Add(cabeca);
        if (rodape != null) { DockPanel.SetDock(rodape, Dock.Bottom); p.Children.Add(rodape); }
        p.Children.Add(corpo);
        Content = PM.Cartao(p);
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => { if (_lista is { IsVisible: true }) _lista.Focus(); else Focus(); }, DispatcherPriority.Loaded);
    }

    void Chutar()
    {
        if (_lista == null || _chutar == null || !_chutar.Habilitado || !_chutar.IsVisible) return;
        if (_lista.Sel < 0 || _lista.Sel >= _titulares.Count) return;
        Chutou?.Invoke(_titulares[_lista.Sel]);
    }

    // o batedor na cena (o humano: logo ao chutar; o computador: no 1o tique)
    public void MostrarBatedor(SJogador b)
    {
        // (a escolha some mas guarda o lugar: o cartao nao muda de tamanho)
        if (_escolha != null) { _escolha.Opacity = 0; _escolha.IsHitTestVisible = false; }
        if (_rodape != null) { _rodape.Opacity = 0; _rodape.IsHitTestVisible = false; }
        if (_chutar != null) _chutar.IsVisible = false;
        _nome.Text = P98.NomeJogador(b);
        _cena.IsVisible = true;
        Focus();
    }

    public void MostrarResultado(int r)
    {
        _espera.IsVisible = false;
        _resultado.Text = r == ResultadoPenalti.Gol ? Idioma.T("GOLO!") : P98.Resultado(r);
        _resultado.Foreground = r == ResultadoPenalti.Gol ? PM.Vivo : PM.Vermelho;
        _resultado.IsVisible = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && e.Key is Key.Enter or Key.Space && _chutar is { IsVisible: true }) { Chutar(); e.Handled = true; }
    }
}

// ---------------------------------------------------------------- os penaltis

sealed class PenaltisModerno : UserControl
{
    readonly JogoAtual _jg;
    readonly ListaM _lista;
    readonly TextBlock _placar, _estado;
    List<(SJogador? Bat, int Res)[]> _pares = new();

    public PenaltisModerno(JogoAtual jg)
    {
        _jg = jg;
        FontFamily = PM.Inter;
        Width = 660;
        Height = 540;
        var casa = jg.Equipa[Lado.Casa]; var fora = jg.Equipa[Lado.Visit];

        var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Margin = new Thickness(16, 6, 16, 14) };
        topo.Children.Add(PM.Equipa(casa, 52, 15));
        _placar = new TextBlock
        {
            Text = "0 - 0", FontSize = 40, FontWeight = FontWeight.Bold, Foreground = PM.Texto, Margin = new Thickness(16, 0),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
        };
        Grid.SetColumn(_placar, 1);
        topo.Children.Add(_placar);
        var ef = PM.Equipa(fora, 52, 15);
        Grid.SetColumn(ef, 2);
        topo.Children.Add(ef);
        var cabeca = new StackPanel { Background = PM.Barra };
        cabeca.Children.Add(new TextBlock
        {
            Text = Idioma.T("DESEMPATE POR PENALTIS"), FontSize = 12, FontWeight = FontWeight.Bold, Foreground = PM.Azul, LetterSpacing = 1,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0),
        });
        cabeca.Children.Add(topo);

        _lista = new ListaM { AlturaItem = 42, Desenhar = DesenharPar, Habilitado = false };
        _lista.Quantos = () => _pares.Count;
        _estado = PM.Rotulo(Idioma.T("Penaltis alternados"), 12.5, PM.Fraco);
        var rodape = PM.Rodape(_estado);
        rodape.Child!.Margin = new Thickness(18, 14);

        var p = new DockPanel();
        DockPanel.SetDock(cabeca, Dock.Top);
        p.Children.Add(cabeca);
        DockPanel.SetDock(rodape, Dock.Bottom);
        p.Children.Add(rodape);
        p.Children.Add(CabecalhoM.Caixa(_lista, new Thickness(16, 14, 16, 14)));
        Content = PM.Cartao(p);
    }

    public void Atualizar(List<(SJogador? Bat, int Res)[]> pares, int[] golos)
    {
        _pares = pares;
        _placar.Text = $"{golos[Lado.Casa]} - {golos[Lado.Visit]}";
        _lista.Sel = pares.Count - 1;
    }

    public void Fim(bool passaVisitante)
    {
        var e = _jg.Equipa[passaVisitante ? Lado.Visit : Lado.Casa];
        _estado.Text = Idioma.T("{0} passa à próxima fase", P98.NomeEquipa(e));
        _estado.Foreground = PM.Vivo;
        _estado.FontWeight = FontWeight.SemiBold;
    }

    // uma rodada: a casa a esquerda (encostada ao centro), o numero no meio, o visitante a direita
    void DesenharPar(DrawingContext dc, int i, Rect r, bool escolhido)
    {
        if (i >= _pares.Count) return;
        if (escolhido) PM.Escolha(dc, r.Deflate(new Thickness(6, 2)));
        double c = Math.Round(r.X + r.Width / 2), cy = r.Y + r.Height / 2;
        var tn = PM.T((i + 1).ToString(), 12, PM.Fraco, true);
        dc.DrawText(tn, new Point(c - tn.Width / 2, cy - tn.Height / 2));
        for (int k = 0; k < 2; k++)
        {
            var (b, res) = _pares[i][k];
            if (b == null) continue;
            bool esq = k == 0;
            double raio = 9;
            double xm = esq ? c - 26 - raio : c + 26 + raio;
            Marca(dc, new Point(xm, cy), raio, res);
            var nome = PM.T(P98.NomeJogador(b), 14, PM.Texto, true);
            var det = PM.T(P98.Resultado(res), 11.5, res == ResultadoPenalti.Gol ? PM.Vivo : PM.Fraco);
            double alto = nome.Height + det.Height - 2, yt = cy - alto / 2;
            double largura = c - 26 - 2 * raio - 12 - r.X - 16;
            if (esq)
            {
                double xr = xm - raio - 12;
                using (dc.PushClip(new Rect(r.X + 16, r.Y, largura, r.Height)))
                {
                    dc.DrawText(nome, new Point(xr - nome.Width, yt));
                    dc.DrawText(det, new Point(xr - det.Width, yt + nome.Height - 2));
                }
            }
            else
            {
                double xl = xm + raio + 12;
                using (dc.PushClip(new Rect(xl, r.Y, largura, r.Height)))
                {
                    dc.DrawText(nome, new Point(xl, yt));
                    dc.DrawText(det, new Point(xl, yt + nome.Height - 2));
                }
            }
        }
    }

    // o golo: circulo verde com o visto; o resto: vermelho com o X
    static void Marca(DrawingContext dc, Point c, double raio, int res)
    {
        bool gol = res == ResultadoPenalti.Gol;
        dc.DrawEllipse(gol ? PM.Vivo : PM.Vermelho, null, c, raio, raio);
        var pen = new Pen(PM.Escuro, 2, lineCap: PenLineCap.Round);
        if (gol)
        {
            dc.DrawLine(pen, new Point(c.X - 4, c.Y + 0.5), new Point(c.X - 1, c.Y + 3.5));
            dc.DrawLine(pen, new Point(c.X - 1, c.Y + 3.5), new Point(c.X + 4.5, c.Y - 3));
        }
        else
        {
            dc.DrawLine(pen, new Point(c.X - 3.5, c.Y - 3.5), new Point(c.X + 3.5, c.Y + 3.5));
            dc.DrawLine(pen, new Point(c.X + 3.5, c.Y - 3.5), new Point(c.X - 3.5, c.Y + 3.5));
        }
    }
}

// ---------------------------------------------------------------- o sorteio da Taca

sealed class SorteioModerno : UserControl
{
    readonly List<SEquipa> _pote;
    readonly List<(SEquipa? Casa, SEquipa? Fora)> _feitos = new();
    readonly ListaM _jogos;
    readonly PoteM _listaPote;
    readonly TextBlock _titPote, _titJogos;
    readonly Vaga _casa, _fora;
    readonly int _total;
    readonly Dictionary<SEquipa, Bitmap?> _logos = new();

    public SorteioModerno(RetroFoot.Motor.Save.Jogo98 s, List<SEquipa> pote, int jogos)
    {
        _pote = pote; _total = jogos;
        FontFamily = PM.Inter;
        Width = 780;
        Height = 572;

        // o topo: o logo da Taca e o titulo
        var cabeca = new DockPanel { Background = PM.Barra };
        var tp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(18, 12) };
        var logo = Escudos.Taca(s);
        if (logo != null) tp.Children.Add(new Image { Source = logo, Width = 36, Height = 36 });
        var tt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        tt.Children.Add(new TextBlock { Text = Idioma.T("TAÇA"), FontSize = 11.5, FontWeight = FontWeight.Bold, Foreground = PM.Azul, LetterSpacing = 0.8 });
        tt.Children.Add(PM.Rotulo(Idioma.T("Sorteio dos jogos"), 19, PM.Texto, true));
        tp.Children.Add(tt);
        cabeca.Children.Add(tp);

        // as duas vagas do jogo que esta sendo sorteado
        _casa = new Vaga(this); _fora = new Vaga(this);
        var vagas = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Margin = new Thickness(16, 14, 16, 4), Height = 64 };
        vagas.Children.Add(_casa);
        var x = new TextBlock { Text = "×", FontSize = 22, Foreground = PM.Fraco, Margin = new Thickness(14, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(x, 1);
        vagas.Children.Add(x);
        Grid.SetColumn(_fora, 2);
        vagas.Children.Add(_fora);

        _listaPote = new PoteM(this);
        _jogos = new ListaM { AlturaItem = 30, Habilitado = false, Desenhar = DesenharJogo };
        _jogos.Quantos = () => _feitos.Count;
        _titPote = (TextBlock)PM.Titulo("", 12);
        _titJogos = (TextBlock)PM.Titulo("", 12);
        var corpo = new Grid { ColumnDefinitions = new ColumnDefinitions("*,320"), Margin = new Thickness(8, 8, 8, 16) };
        corpo.Children.Add(Coluna(_titPote, _listaPote));
        var cj = Coluna(_titJogos, _jogos);
        Grid.SetColumn(cj, 1);
        corpo.Children.Add(cj);
        Titulos();

        var p = new DockPanel();
        DockPanel.SetDock(cabeca, Dock.Top);
        p.Children.Add(cabeca);
        DockPanel.SetDock(vagas, Dock.Top);
        p.Children.Add(vagas);
        p.Children.Add(corpo);
        Content = PM.Cartao(p);
    }

    static Control Coluna(Control titulo, Control lista)
    {
        var d = new DockPanel { Margin = new Thickness(8, 0) };
        DockPanel.SetDock(titulo, Dock.Top);
        d.Children.Add(titulo);
        d.Children.Add(CabecalhoM.Caixa(lista, new Thickness(0, 8, 0, 0)));
        return d;
    }

    void Titulos()
    {
        _titPote.Text = Idioma.T("NO POTE · {0}", _pote.Count);
        _titJogos.Text = Idioma.T("JOGOS · {0} DE {1}", _feitos.Count, _total);
    }

    internal Bitmap? Logo(SEquipa e)
    {
        if (!_logos.TryGetValue(e, out var b)) _logos[e] = b = Escudos.Escudo(e);
        return b;
    }

    internal static Color Fundo(SEquipa e) => e.CorFundo.Length >= 3 ? Color.FromRgb(e.CorFundo[0], e.CorFundo[1], e.CorFundo[2]) : Colors.White;
    internal static Color Letra(SEquipa e) => e.CorLetra.Length >= 3 ? Color.FromRgb(e.CorLetra[0], e.CorLetra[1], e.CorLetra[2]) : Colors.Black;

    internal void LogoEm(DrawingContext dc, SEquipa e, Rect r) => TelaJornada.Logo(dc, Logo(e), Fundo(e), Letra(e), r);

    // a casa sorteada (limpa o visitante) ou o visitante; a equipa sai do pote
    public void Casa(SEquipa? e) { _fora.Mudar(null); _casa.Mudar(e); Tirar(e); }
    public void Fora(SEquipa? e) { _fora.Mudar(e); Tirar(e); }

    void Tirar(SEquipa? e)
    {
        if (e == null || !_pote.Remove(e)) return;
        _listaPote.InvalidateVisual();
        Titulos();
    }

    public void Jogo(SEquipa? casa, SEquipa? fora)
    {
        _feitos.Add((casa, fora));
        _jogos.Sel = _feitos.Count - 1;
        Titulos();
    }

    void DesenharJogo(DrawingContext dc, int i, Rect r, bool escolhido)
    {
        if (i >= _feitos.Count) return;
        if (escolhido) PM.Escolha(dc, r.Deflate(new Thickness(4, 1)));
        var (casa, fora) = _feitos[i];
        double c = Math.Round(r.X + r.Width / 2), cy = r.Y + r.Height / 2, q = 18;
        var tx = PM.T("×", 13, PM.Fraco);
        dc.DrawText(tx, new Point(c - tx.Width / 2, cy - tx.Height / 2));
        if (casa != null)
        {
            LogoEm(dc, casa, new Rect(c - 12 - q, cy - q / 2, q, q));
            var t = PM.T(P98.NomeEquipa(casa), 12.5, PM.Texto, true);
            double xr = c - 12 - q - 8;
            using (dc.PushClip(new Rect(r.X + 8, r.Y, Math.Max(0, xr - r.X - 8), r.Height)))
                dc.DrawText(t, new Point(xr - t.Width, cy - t.Height / 2));
        }
        if (fora != null)
        {
            LogoEm(dc, fora, new Rect(c + 12, cy - q / 2, q, q));
            var t = PM.T(P98.NomeEquipa(fora), 12.5, PM.Texto, true);
            double xl = c + 12 + q + 8;
            using (dc.PushClip(new Rect(xl, r.Y, Math.Max(0, r.Right - 8 - xl), r.Height)))
                dc.DrawText(t, new Point(xl, cy - t.Height / 2));
        }
    }

    // a vaga de uma equipa: o logo e o nome num cartao (vazia: tracejada)
    sealed class Vaga : Control
    {
        readonly SorteioModerno _s;
        SEquipa? _e;
        public Vaga(SorteioModerno s) { _s = s; }
        public void Mudar(SEquipa? e) { _e = e; InvalidateVisual(); }

        public override void Render(DrawingContext dc)
        {
            var r = new Rect(Bounds.Size).Deflate(1);
            if (_e == null)
            {
                dc.DrawRectangle(null, new Pen(TemaVerde.Borda, 1.5, new DashStyle(new double[] { 4, 3 }, 0)), r, 12, 12);
                var tv = PM.T("?", 20, TemaVerde.Apagado, true);
                dc.DrawText(tv, new Point(r.Center.X - tv.Width / 2, r.Center.Y - tv.Height / 2));
                return;
            }
            dc.DrawRectangle(TemaVerde.CartaoForte, new Pen(TemaVerde.Ouro, 1.5), r, 12, 12);
            double q = 40;
            var t = PM.T(P98.NomeEquipa(_e), 17, PM.Texto, true);
            double w = q + 12 + t.Width, x = Math.Max(r.X + 12, r.Center.X - w / 2);
            _s.LogoEm(dc, _e, new Rect(x, r.Center.Y - q / 2, q, q));
            using (dc.PushClip(new Rect(x + q + 12, r.Y, Math.Max(0, r.Right - 12 - x - q - 12), r.Height)))
                dc.DrawText(t, new Point(x + q + 12, r.Center.Y - t.Height / 2));
        }
    }

    // as equipas do pote em 2 colunas (o logo e o nome), rolando com a roda
    sealed class PoteM : Control
    {
        readonly SorteioModerno _s;
        const double Alto = 26;
        double _topo;
        public PoteM(SorteioModerno s) { _s = s; ClipToBounds = true; }

        int Colunas => Bounds.Width > 540 ? 3 : 2;
        double Total => Math.Ceiling(_s._pote.Count / (double)Colunas) * Alto + 4;

        public override void Render(DrawingContext dc)
        {
            var r = new Rect(Bounds.Size);
            dc.FillRectangle(Brushes.Transparent, r);
            var pote = _s._pote;
            if (pote.Count == 0)
            {
                var v = PM.T(Idioma.T("Todas as equipas sorteadas"), 13, PM.Fraco);
                dc.DrawText(v, new Point((r.Width - v.Width) / 2, 24));
                return;
            }
            _topo = Math.Clamp(_topo, 0, Math.Max(0, Total - r.Height));
            int nc = Colunas;
            double lc = (r.Width - 16) / nc;
            for (int i = 0; i < pote.Count; i++)
            {
                double y = 2 + i / nc * Alto - _topo;
                if (y + Alto < 0) continue;
                if (y > r.Height) break;
                double x = 8 + i % nc * lc, cy = y + Alto / 2;
                _s.LogoEm(dc, pote[i], new Rect(x + 4, cy - 8, 16, 16));
                var t = PM.T(P98.NomeEquipa(pote[i]), 12.5, PM.Texto);
                using (dc.PushClip(new Rect(x + 26, y, Math.Max(0, lc - 30), Alto)))
                    dc.DrawText(t, new Point(x + 26, cy - t.Height / 2));
            }
            if (Total > r.Height)
            {
                double h = Math.Max(24, r.Height * r.Height / Total);
                double y = (r.Height - h) * (_topo / Math.Max(1, Total - r.Height));
                dc.DrawRectangle(PM.Rolagem, null, new Rect(r.Width - 5, y, 4, h), 2, 2);
            }
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            _topo = Math.Clamp(_topo - e.Delta.Y * Alto * 2, 0, Math.Max(0, Total - Bounds.Height));
            InvalidateVisual();
            e.Handled = true;
        }
    }
}
