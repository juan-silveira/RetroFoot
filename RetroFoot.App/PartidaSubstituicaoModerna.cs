using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Partida;
using Mem = RetroFoot.Motor.Partida.Memoria;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;

namespace RetroFoot.App;

// A TsubstitutionDlg no visual Moderno (TelaSubstituicao a abre no lugar da original): no topo
// os logos, o placar e o momento (intervalo, goleiro expulso, em jogo) e o arbitro; embaixo os
// lances (com os filtros de amarelos, vermelhos e lesoes, as mesmas opcoes do original), quem
// esta em campo e o banco. As regras sao as da original (TelaSubstituicao.PodeTrocar e
// Trocar): escolher quem sai e quem entra e Substituir (ou duplo clique no banco, ou S).
// Enter e Esc continuam. Sem as listas (equipa do computador), so o topo e os lances.
sealed class SubstituicaoModerna : UserControl
{
    public event Action? Fechou;

    readonly JogoAtual _jg;
    readonly SEquipa _e;
    readonly int _motivo;
    readonly Opcoes _op;
    readonly List<SJogador> _tit = new(), _res = new();
    readonly List<(Lance L, string Placar)> _lances = new();
    readonly ListaM _listaLances;
    readonly ListaM? _listaTit, _listaRes;
    readonly BotaoM? _substituir;
    readonly TextBlock? _restantes;
    readonly Chip? _amarelos;
    readonly Chip _vermelhos, _lesoes;
    readonly Bitmap?[] _logos = new Bitmap?[2];
    readonly Color[] _fundos = new Color[2], _letras = new Color[2];
    bool _pode;

    public SubstituicaoModerna(JogoAtual jg, int lado, int motivo, Opcoes op, bool cartoesAmarelos, bool completa, bool pode)
    {
        _jg = jg; _e = jg.Equipa[lado]!; _motivo = motivo; _op = op; _pode = pode;
        FontFamily = PM.Inter;
        Focusable = true;
        for (int l = 0; l < 2; l++)
            if (jg.Equipa[l] is { } eq)
            {
                _logos[l] = Escudos.Escudo(eq);
                _fundos[l] = eq.CorFundo.Length >= 3 ? Color.FromRgb(eq.CorFundo[0], eq.CorFundo[1], eq.CorFundo[2]) : Colors.White;
                _letras[l] = eq.CorLetra.Length >= 3 ? Color.FromRgb(eq.CorLetra[0], eq.CorLetra[1], eq.CorLetra[2]) : Colors.Black;
            }

        // o topo: casa, placar e o momento, visitante; o arbitro
        var casa = jg.Equipa[Lado.Casa]; var fora = jg.Equipa[Lado.Visit];
        var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Margin = new Thickness(16, 14, 16, 8) };
        topo.Children.Add(PM.Equipa(casa, 44, 14));
        var placar = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
        string momento = Idioma.T(motivo switch { 4 => "Intervalo", 3 => "Guarda-redes expulso", _ => "Em jogo" });
        placar.Children.Add(new TextBlock
        {
            Text = momento, FontSize = 12, FontWeight = FontWeight.Bold, Foreground = motivo == 3 ? PM.Vermelho : PM.Azul,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        placar.Children.Add(new TextBlock
        {
            Text = $"{(casa == null ? 0 : Mem.Golos(casa))} - {(fora == null ? 0 : Mem.Golos(fora))}", FontSize = 30,
            FontWeight = FontWeight.Bold, Foreground = PM.Texto, HorizontalAlignment = HorizontalAlignment.Center,
        });
        Grid.SetColumn(placar, 1);
        topo.Children.Add(placar);
        var ef = PM.Equipa(fora, 44, 14);
        Grid.SetColumn(ef, 2);
        topo.Children.Add(ef);
        var cabeca = new StackPanel { Background = PM.Barra };
        cabeca.Children.Add(topo);
        if (jg.Arbitro is { } a)
            cabeca.Children.Add(new TextBlock
            {
                Text = Idioma.T("Árbitro: {0} ({1})", P98.Texto(a.Nome), P98.NomePais(P98.Texto(a.Pais))), FontSize = 12, Foreground = PM.Fraco,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10),
            });

        // os lances e os filtros (o amarelo desmarcado a cada vez, como no original)
        var filtros = new WrapPanel { Margin = new Thickness(0, 8, 0, 6) };
        if (cartoesAmarelos) filtros.Children.Add(_amarelos = new Chip(Idioma.T("Amarelos"), false));
        filtros.Children.Add(_vermelhos = new Chip(Idioma.T("Vermelhos"), op.VerExpulsoes));
        filtros.Children.Add(_lesoes = new Chip(Idioma.T("Lesões"), op.VerLesoes));
        _vermelhos.Mudou += () => { _op.VerExpulsoes = _vermelhos.Marcado; Lances(); };
        _lesoes.Mudou += () => { _op.VerLesoes = _lesoes.Marcado; Lances(); };
        if (_amarelos != null) _amarelos.Mudou += Lances;
        _listaLances = new ListaM { AlturaItem = 30, SemSelecao = true, Vazia = Idioma.T("Sem lances"), Desenhar = DesenharLance, Focusable = false };
        _listaLances.Quantos = () => _lances.Count;
        var colLances = Coluna(Idioma.T("Lances"), filtros, _listaLances);

        Control corpo;
        double largura;
        if (completa)
        {
            foreach (var p in _e.Jogadores)
                switch (Mem.Escalacao(p))
                {
                    case Mem.EscTitular: _tit.Add(p); break;
                    case Mem.EscReserva: _res.Add(p); break;
                }
            _listaTit = Jogadores(_tit);
            _listaRes = Jogadores(_res);
            _listaTit.Vazia = Idioma.T("Ninguém em campo");
            _listaRes.Vazia = Idioma.T("Banco vazio");
            _listaTit.Clique += _ => Atualizar();
            _listaRes.Clique += _ => Atualizar();
            _listaTit.Duplo += _ => { if (_listaRes.Quantos() > 0) { _listaRes.Focus(); if (_listaRes.Sel < 0) _listaRes.Sel = 0; Atualizar(); } };
            _listaRes.Duplo += _ => Substituir();
            _listaTit.Habilitado = _listaRes.Habilitado = pode;

            _substituir = new BotaoM(Idioma.T("Substituir"), true) { Habilitado = false, HorizontalAlignment = HorizontalAlignment.Stretch };
            ((TextBlock)_substituir.Child!).HorizontalAlignment = HorizontalAlignment.Center;
            _substituir.Clique += Substituir;
            _substituir.IsVisible = pode;
            _restantes = PM.Rotulo("", 12, PM.Fraco);
            _restantes.Margin = new Thickness(2, 0, 0, 8);
            var banco = new DockPanel();
            var pe = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            pe.Children.Add(_restantes);
            pe.Children.Add(_substituir);
            DockPanel.SetDock(pe, Dock.Bottom);
            banco.Children.Add(pe);
            banco.Children.Add(_listaRes);

            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*,*"), Margin = new Thickness(8, 4, 8, 0) };
            g.Children.Add(colLances);
            var cTit = Coluna(Idioma.T("Em campo · {0}", P98.NomeEquipa(_e)), null, _listaTit);
            Grid.SetColumn(cTit, 1);
            g.Children.Add(cTit);
            var cRes = Coluna(Idioma.T("Banco"), null, banco);
            Grid.SetColumn(cRes, 2);
            g.Children.Add(cRes);
            corpo = g;
            largura = 780;
            Height = 572;
        }
        else
        {
            corpo = new Border { Margin = new Thickness(8, 4, 8, 0), Child = colLances };
            largura = 480;
            Height = 440;
        }
        Width = largura;

        var continuar = new BotaoM(Idioma.T("Continuar"), !completa || !pode);
        continuar.Clique += Fechar;
        Control? dica = completa && pode ? PM.Rotulo(Idioma.T("Escolha quem sai e quem entra · S substitui · Enter continua"), 12, PM.Fraco) : null;
        var rodape = PM.Rodape(dica, continuar);
        var dp = new DockPanel();
        DockPanel.SetDock(cabeca, Dock.Top);
        dp.Children.Add(cabeca);
        DockPanel.SetDock(rodape, Dock.Bottom);
        dp.Children.Add(rodape);
        dp.Children.Add(corpo);
        Content = PM.Cartao(dp);

        Lances();
        Atualizar();
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            _listaLances.AoFim();
            if (_listaTit is { Habilitado: true }) _listaTit.Focus(); else Focus();
        }, DispatcherPriority.Loaded);
    }

    static Control Coluna(string titulo, Control? extra, Control lista)
    {
        var d = new DockPanel { Margin = new Thickness(8, 8, 8, 0) };
        var t = PM.Titulo(titulo, 12);
        ((TextBlock)t).TextTrimming = TextTrimming.CharacterEllipsis;
        DockPanel.SetDock(t, Dock.Top);
        d.Children.Add(t);
        if (extra != null) { DockPanel.SetDock(extra, Dock.Top); d.Children.Add(extra); }
        var caixa = new Border
        {
            Background = TemaVerde.Cartao, CornerRadius = new CornerRadius(10), Padding = new Thickness(0, 4),
            BorderBrush = TemaVerde.Borda, BorderThickness = new Thickness(1),
            Margin = new Thickness(0, extra == null ? 8 : 0, 0, 0), Child = lista,
        };
        d.Children.Add(caixa);
        return d;
    }

    ListaM Jogadores(List<SJogador> l)
    {
        var lista = new ListaM { AlturaItem = 30 };
        lista.Quantos = () => l.Count;
        lista.Desenhar = (dc, i, r, esc) => { if (i < l.Count) PM.LinhaJogador(dc, l[i], r, esc, !lista.Habilitado); };
        return lista;
    }

    // seg02:24a1, com os mesmos filtros
    void Lances()
    {
        _lances.Clear();
        int gc = 0, gf = 0;
        bool amarelos = _amarelos?.Marcado == true;
        foreach (var l in _jg.Lances)
            switch (l.Tipo)
            {
                case TipoLance.Gol:
                case TipoLance.Penalti:
                    if (l.Lado == Lado.Casa) gc++; else gf++;
                    _lances.Add((l, $"{gc}-{gf}"));
                    break;
                case TipoLance.Amarelo: if (amarelos) _lances.Add((l, "")); break;
                case TipoLance.Expulsao: if (_vermelhos.Marcado) _lances.Add((l, "")); break;
                case TipoLance.Lesao: if (_lesoes.Marcado) _lances.Add((l, "")); break;
                case TipoLance.GolAnulado: _lances.Add((l, "")); break;
            }
        _listaLances.InvalidateVisual();
        _listaLances.AoFim();
    }

    // o lance: minuto, icone, nome (e o placar no golo) e o logo da equipa a direita
    void DesenharLance(DrawingContext dc, int i, Rect r, bool _)
    {
        if (i >= _lances.Count) return;
        var (l, placar) = _lances[i];
        double cy = r.Y + r.Height / 2;
        var tm = PM.T(MinutoTexto.Texto(l.Minuto, l.Acrescimo), l.Acrescimo > 0 ? 10 : 11.5, PM.Fraco, true);
        dc.DrawText(tm, new Point(r.X + 36 - tm.Width, cy - tm.Height / 2));
        Icones.Desenhar(dc, l.Tipo, l.SegundoAmarelo, new Rect(r.X + 44, cy - 7, 14, 14));
        double xl = r.Right - 10 - 18;
        int lado = l.Lado == Lado.Casa ? 0 : 1;
        TelaJornada.Logo(dc, _logos[lado], _fundos[lado], _letras[lado], new Rect(xl, cy - 9, 18, 18));
        double xd = xl - 6;
        if (placar.Length > 0)
        {
            var tp = PM.T(placar, 12, PM.Texto, true);
            xd -= tp.Width;
            dc.DrawText(tp, new Point(xd, cy - tp.Height / 2));
            xd -= 6;
        }
        var tn = PM.T(P98.NomeSimples(l.Jogador).Trim(), 12.5, l.Tipo == TipoLance.GolAnulado ? PM.Fraco : PM.Texto, true);
        double xn = r.X + 66;
        using (dc.PushClip(new Rect(xn, r.Y, Math.Max(0, xd - xn), r.Height)))
            dc.DrawText(tn, new Point(xn, cy - tn.Height / 2));
    }

    // seg02:2905
    void Atualizar()
    {
        if (_listaTit == null || _listaRes == null || _substituir == null || _restantes == null) return;
        bool ok = _pode && _listaTit.Sel >= 0 && _listaTit.Sel < _tit.Count && _listaRes.Sel >= 0 && _listaRes.Sel < _res.Count
            && TelaSubstituicao.PodeTrocar(_tit[_listaTit.Sel], _res[_listaRes.Sel], _motivo);
        _substituir.Habilitado = ok;
        int n = Mem.Substituicoes(_e);
        if (n <= 0 && _pode) { _pode = false; _listaTit.Habilitado = _listaRes.Habilitado = false; _substituir.IsVisible = false; }
        _restantes.Text = !_pode && n > 0 ? Idioma.T("Substituições só ao intervalo") : n == 1 ? Idioma.T("1 substituição restante") : Idioma.T("{0} substituições restantes", n);
    }

    // seg02:2a4f
    void Substituir()
    {
        if (_listaTit == null || _listaRes == null || _substituir == null || !_substituir.Habilitado) return;
        if (_listaTit.Sel < 0 || _listaTit.Sel >= _tit.Count || _listaRes.Sel < 0 || _listaRes.Sel >= _res.Count) return;
        TelaSubstituicao.Trocar(_e, _tit, _res, _tit[_listaTit.Sel], _res[_listaRes.Sel]);
        _listaTit.Sel = -1;
        _listaRes.Sel = -1;
        Atualizar();
        _listaTit.InvalidateVisual();
        _listaRes.InvalidateVisual();
        if (_listaTit.Habilitado) _listaTit.Focus(); else Focus();
    }

    void Fechar() => Fechou?.Invoke();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (e.Key is Key.S or Key.Space && _substituir is { IsVisible: true, Habilitado: true }) { Substituir(); e.Handled = true; }
        else if (e.Key is Key.Left or Key.Right && _listaTit != null && _listaRes != null && _pode)
        {
            var l = e.Key == Key.Left ? _listaTit : _listaRes;
            l.Focus();
            if (l.Sel < 0 && l.Quantos() > 0) { l.Sel = 0; Atualizar(); }
            e.Handled = true;
        }
    }
}

// o filtro em pilula (marcado: amarelo); muda com o clique
sealed class Chip : Border
{
    readonly TextBlock _t;
    bool _marcado;
    public event Action? Mudou;

    public Chip(string texto, bool marcado)
    {
        _t = new TextBlock { Text = texto, FontSize = 12, FontWeight = FontWeight.SemiBold, FontFamily = PM.Inter };
        Child = _t;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(10, 3);
        Margin = new Thickness(0, 0, 6, 4);
        Cursor = new Cursor(StandardCursorType.Hand);
        Marcado = marcado;
    }

    public bool Marcado
    {
        get => _marcado;
        set
        {
            _marcado = value;
            Background = value ? TemaVerde.DestaqueFundo : Brushes.Transparent;
            BorderBrush = value ? PM.Azul : TemaVerde.Borda;
            _t.Foreground = value ? TemaVerde.DestaqueClaro : TemaVerde.TextoSuave;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true;
        Marcado = !Marcado;
        Mudou?.Invoke();
    }
}
