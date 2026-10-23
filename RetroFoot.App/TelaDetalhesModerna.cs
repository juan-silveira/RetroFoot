using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using RetroFoot.Motor.Partida;
using Mem = RetroFoot.Motor.Partida.Memoria;
using SEquipa = RetroFoot.Motor.Save.Equipa;

namespace RetroFoot.App;

// Os detalhes do jogo no visual Moderno (Visual.cs): o clique num jogo da jornada abre isto
// em vez da TsubstitutionDlg (motivo 1). Em cima os dois logos e o placar com o minuto; o
// arbitro; e a linha do tempo vertical, centrada no minuto: os lances da casa a esquerda e
// os do visitante a direita, com o marcador "Intervalo X-Y" (e "Fim do tempo normal X-Y" na
// prorrogacao da Taca).
//
// Os lances que o motor guarda de cada jogo (JogoAtual.Lances: golo, golo de penalti, amarelo,
// expulsao com o 2o amarelo, lesao e golo anulado) e os extras desta sessao (JogoAtual.Extras,
// fora do save): substituicoes (seta verde de quem entra, vermelha de quem sai; as do
// intervalo depois do marcador, no minuto 46 ou 91) e penaltis sem golo. Um jogo de antes de
// carregar o save nao tem os extras.
//
// "Escalação" (quando a janela original mostraria as listas: equipa humana, ou o add-in de
// ver os outros) troca para a TsubstitutionDlg original, com as substituicoes como antes.
public sealed class TelaDetalhesModerna : UserControl
{
    // as cores sao as do TemaVerde (o tema do Moderno); Azul e o destaque (amarelo)
    static readonly IBrush Fundo = TemaVerde.Fundo, Barra = TemaVerde.Barra, Linha = TemaVerde.Linha;
    static readonly IBrush Texto = TemaVerde.Texto, Fraco = TemaVerde.TextoFraco, Azul = TemaVerde.Destaque;
    static readonly FontFamily Inter = new("fonts:Inter#Inter");

    public event Action? Fechou;
    bool _fechou;

    readonly JogoAtual _jg;
    readonly Func<Control>? _escalacao;
    bool _naEscalacao;
    ScrollViewer? _rolagem;

    // acrescimo: o n de "45'+n'" do jogo agora; noIntervalo: o relogio parado no intervalo
    public TelaDetalhesModerna(JogoAtual jg, int minuto, int acrescimo, bool noIntervalo, Func<Control>? escalacao)
    {
        Focusable = true;
        _jg = jg; _escalacao = escalacao;
        Montar(minuto, acrescimo, noIntervalo);
        // aberta durante o jogo: a linha do tempo mostra os ultimos lances
        AttachedToVisualTree += (_, _) =>
        {
            Focus();
            if (_rolagem is { } r) Avalonia.Threading.Dispatcher.UIThread.Post(r.ScrollToEnd, Avalonia.Threading.DispatcherPriority.Loaded);
        };
    }

    // (jogo online, o jogo de outro time aberto so para ver) a janela refeita a cada minuto, com o
    // relogio correndo; na escalacao, fica como esta
    public void Atualizar(int minuto, int acrescimo, bool noIntervalo)
    {
        if (_naEscalacao) return;
        Montar(minuto, acrescimo, noIntervalo);
        if (_rolagem is { } r) Avalonia.Threading.Dispatcher.UIThread.Post(r.ScrollToEnd, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    void Montar(int minuto, int acrescimo, bool noIntervalo)
    {
        var jg = _jg; var escalacao = _escalacao;
        if (Celular.Ativo)
        {
            // o celular (PartidaCelularJanelas.cs): a tela toda; Escalacao troca para a substituicao
            Content = DetalhesCelular.Folha(jg, minuto, acrescimo, noIntervalo, Itens(jg, minuto, noIntervalo),
                escalacao == null ? null : () => { _naEscalacao = true; var t = escalacao(); Content = t; t.Focus(); }, Fechar);
            return;
        }
        Width = 580;
        Height = 540;
        FontFamily = Inter;
        var casa = jg.Equipa[Lado.Casa]; var fora = jg.Equipa[Lado.Visit];
        int gc = casa == null ? 0 : Mem.Golos(casa), gf = fora == null ? 0 : Mem.Golos(fora);

        // o topo: casa, placar e minuto, visitante
        var topo = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Margin = new Thickness(16, 18, 16, 12) };
        topo.Children.Add(Equipa(casa));
        var placar = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
        placar.Children.Add(new TextBlock
        {
            Text = noIntervalo ? Idioma.T("Intervalo") : minuto <= 0 ? "0'" : MinutoTexto.Texto(minuto, acrescimo), FontSize = 13, FontWeight = FontWeight.Bold,
            Foreground = Azul, HorizontalAlignment = HorizontalAlignment.Center,
        });
        placar.Children.Add(new TextBlock
        {
            Text = $"{gc} - {gf}", FontSize = 34, FontWeight = FontWeight.Bold, Foreground = Texto,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        Grid.SetColumn(placar, 1);
        topo.Children.Add(placar);
        var ef = Equipa(fora);
        Grid.SetColumn(ef, 2);
        topo.Children.Add(ef);
        var cabeca = new StackPanel { Background = Barra };
        cabeca.Children.Add(topo);
        if (jg.Arbitro is { } a)
            cabeca.Children.Add(new TextBlock
            {
                Text = Idioma.T("Árbitro") + ": " + P98.Texto(a.Nome) + " (" + P98.NomePais(P98.Texto(a.Pais)) + ")", FontSize = 12, Foreground = Fraco,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12),
            });

        var linha = new LinhaDoTempo(Itens(jg, minuto, noIntervalo));
        var rolagem = _rolagem = new ScrollViewer
        {
            Content = linha, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };

        // o rodape: Escalacao (se houver) e Continuar
        var botoes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 10) };
        if (escalacao != null)
            botoes.Children.Add(Botao(Idioma.T("Escalação"), false, () =>
            {
                _naEscalacao = true;
                var t = escalacao();
                Content = t;
                Width = double.NaN; Height = double.NaN;
                t.Focus();
            }));
        botoes.Children.Add(Botao(Idioma.T("Continuar"), true, Fechar));
        var rodape = new Border { BorderBrush = Linha, BorderThickness = new Thickness(0, 1, 0, 0), Child = botoes };

        var corpo = new DockPanel();
        DockPanel.SetDock(cabeca, Dock.Top);
        corpo.Children.Add(cabeca);
        DockPanel.SetDock(rodape, Dock.Bottom);
        corpo.Children.Add(rodape);
        corpo.Children.Add(rolagem);
        Content = new Border
        {
            Background = Fundo, CornerRadius = new CornerRadius(14), ClipToBounds = true, BorderBrush = TemaVerde.Borda, BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 8 28 0 #80000000"),
            Child = new ThemeVariantScope { RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark, Child = corpo },
        };
    }

    // a equipa no topo: o logo (ou um circulo nas cores dela) e o nome curto
    static Control Equipa(SEquipa? e)
    {
        var p = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        p.Children.Add(new Escudo(e) { Width = 52, Height = 52, HorizontalAlignment = HorizontalAlignment.Center });
        p.Children.Add(new TextBlock
        {
            Text = P98.NomeEquipa(e), FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Texto,
            HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return p;
    }

    sealed class Escudo : Control
    {
        readonly Bitmap? _b;
        readonly Color _fundo, _letra;
        public Escudo(SEquipa? e)
        {
            _b = e == null ? null : Escudos.Escudo(e);
            _fundo = e != null && e.CorFundo.Length >= 3 ? Color.FromRgb(e.CorFundo[0], e.CorFundo[1], e.CorFundo[2]) : Colors.White;
            _letra = e != null && e.CorLetra.Length >= 3 ? Color.FromRgb(e.CorLetra[0], e.CorLetra[1], e.CorLetra[2]) : Colors.Black;
        }
        public override void Render(DrawingContext dc) =>
            TelaJornada.Logo(dc, _b, _fundo, _letra, new Rect(Bounds.Size).Deflate(3));
    }

    static Border Botao(string texto, bool principal, Action clique)
    {
        var t = new TextBlock { Text = texto, FontSize = 14, FontWeight = FontWeight.SemiBold };
        var b = new Border
        {
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(20, 7),
            Cursor = new Cursor(StandardCursorType.Hand), Child = t,
        };
        ClubeModerno.Pilula(b, t, principal, true, false);
        b.PointerEntered += (_, _) => ClubeModerno.Pilula(b, t, principal, true, true);
        b.PointerExited += (_, _) => ClubeModerno.Pilula(b, t, principal, true, false);
        b.PointerPressed += (_, a) => { a.Handled = true; clique(); };
        return b;
    }

    // ---------------------------------------------------------------- os itens

    // um lance (Tipo > 0) ou um marcador no centro (Tipo = 0: Texto); Substituicao: Nome =
    // quem entra, Detalhe = quem sai
    internal sealed record Item(int Tipo, int Lado, int Minuto, string Nome, string Detalhe, bool SegundoAmarelo, int Acrescimo = 0);

    internal const int Substituicao = 101, PenaltiFalhado = 102;

    internal static List<Item> Itens(JogoAtual jg, int minuto, bool noIntervalo)
    {
        var r = new List<Item>();
        int gc = 0, gf = 0;
        bool intervalo = false, normal = false;
        void Marcadores(int ate)
        {
            // o intervalo depois do minuto 45 (Jornada.MinutoPassado: Intervalo no 45, ou
            // depois dos acrescimos dele)
            if (!intervalo && ate > 45 && (minuto > 45 || noIntervalo)) { r.Add(new Item(0, 0, 45, Idioma.T("Intervalo"), $"{gc} - {gf}", false)); intervalo = true; }
            // na Taca, os empatados no 90 vao a prorrogacao (+0x50 = 1)
            if (!normal && ate > 90 && minuto >= 90 && jg.Dados.B50 == 1) { r.Add(new Item(0, 0, 90, Idioma.T("Fim do tempo normal"), $"{gc} - {gf}", false)); normal = true; }
        }
        // os extras anotados antes do lance i (LanceExtra.Ordem)
        int x = 0;
        void Extras(int ate)
        {
            for (; x < jg.Extras.Count && jg.Extras[x].Ordem <= ate; x++)
            {
                var e = jg.Extras[x];
                int min = e.Intervalo ? e.Minuto + 1 : e.Minuto, acr = e.Intervalo ? 0 : e.Acrescimo;
                Marcadores(min);
                if (e.Tipo == TipoExtra.Substituicao && e.Entrou != null && e.Saiu != null)
                    r.Add(new Item(Substituicao, e.Lado, min, P98.NomeSimples(e.Entrou).Trim(), P98.NomeSimples(e.Saiu).Trim(), false, acr));
                else if (e.Tipo == TipoExtra.PenaltiFalhado && e.Batedor != null)
                    r.Add(new Item(PenaltiFalhado, e.Lado, min, P98.NomeSimples(e.Batedor).Trim(), Falhado(e), false, acr));
            }
        }
        for (int i = 0; i < jg.Lances.Count; i++)
        {
            Extras(i);
            var l = jg.Lances[i];
            Marcadores(l.Minuto);
            string nome = P98.NomeSimples(l.Jogador).Trim();
            string det;
            switch (l.Tipo)
            {
                case TipoLance.Gol:
                case TipoLance.Penalti:
                    if (l.Lado == Lado.Casa) gc++; else gf++;
                    det = Idioma.T(l.Tipo == TipoLance.Penalti ? "Gol de pênalti" : "Gol") + $" · {gc} - {gf}";
                    break;
                case TipoLance.Amarelo: det = Idioma.T("Cartão amarelo"); break;
                case TipoLance.Expulsao: det = Idioma.T(l.SegundoAmarelo ? "Segundo amarelo · expulso" : "Cartão vermelho"); break;
                case TipoLance.Lesao: det = Idioma.T("Lesão"); break;
                case TipoLance.GolAnulado: det = Idioma.T("Gol anulado"); break;
                default: continue;
            }
            r.Add(new Item(l.Tipo, l.Lado, l.Minuto, nome, det, l.SegundoAmarelo, l.Acrescimo));
        }
        Extras(int.MaxValue);
        Marcadores(int.MaxValue);
        return r;
    }

    // o desfecho de um penalti sem golo (ResultadoPenalti)
    static string Falhado(LanceExtra e) => e.Resultado switch
    {
        ResultadoPenalti.Defesa => Idioma.T("Pênalti defendido por {0}", e.Goleiro == null ? "" : P98.NomeSimples(e.Goleiro).Trim()),
        ResultadoPenalti.Poste => Idioma.T("Pênalti na trave"),
        ResultadoPenalti.Barra => Idioma.T("Pênalti no travessão"),
        ResultadoPenalti.AoLado => Idioma.T("Pênalti para fora"),
        _ => Idioma.T("Pênalti por cima"),
    };

    // a linha do tempo desenhada: a linha vertical no centro, o minuto num circulo e o lance do
    // lado da equipa
    sealed class LinhaDoTempo : Control
    {
        const double Alto = 46, Topo = 14;
        readonly List<Item> _itens;

        public LinhaDoTempo(List<Item> itens)
        {
            _itens = itens;
            Height = Math.Max(120, Topo * 2 + Alto * itens.Count);
            VerticalAlignment = VerticalAlignment.Top;
        }

        static FormattedText T(string s, double tam, IBrush cor, bool negrito = false) =>
            new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Inter, FontStyle.Normal, negrito ? FontWeight.SemiBold : FontWeight.Normal), tam * Acessibilidade.Texto, cor);

        public override void Render(DrawingContext dc)
        {
            double w = Bounds.Width, c = Math.Round(w / 2);
            if (_itens.Count == 0)
            {
                var v = T(Idioma.T("Ainda sem lances"), 13, Fraco);

                dc.DrawText(v, new Point(c - v.Width / 2, 40));
                return;
            }
            dc.FillRectangle(TemaVerde.Borda, new Rect(c - 1, Topo, 2, Alto * _itens.Count));
            for (int i = 0; i < _itens.Count; i++)
            {
                var it = _itens[i];
                double y = Topo + Alto * i, meio = y + Alto / 2;
                if (it.Tipo == 0)
                {
                    // o marcador: "Intervalo  1 - 0" numa pilula no centro
                    var tn = T(it.Nome, 12, Azul, true);
                    var tp = T(it.Detalhe, 12, Texto, true);
                    double pw = tn.Width + 10 + tp.Width + 28, ph = 26;
                    var r = new Rect(c - pw / 2, meio - ph / 2, pw, ph);
                    dc.DrawRectangle(TemaVerde.Cartao, new Pen(TemaVerde.Ouro, 1), r, 13, 13);
                    dc.DrawText(tn, new Point(r.X + 14, meio - tn.Height / 2));
                    dc.DrawText(tp, new Point(r.X + 14 + tn.Width + 10, meio - tp.Height / 2));
                    continue;
                }
                // o minuto
                var tm = T(MinutoTexto.Texto(it.Minuto, it.Acrescimo), it.Acrescimo > 0 ? 9 : 11.5, Texto, true);
                double rr = 17;
                dc.DrawEllipse(TemaVerde.Cartao, new Pen(TemaVerde.Borda, 1.5), new Point(c, meio), rr, rr);
                dc.DrawText(tm, new Point(c - tm.Width / 2, meio - tm.Height / 2));
                // o lance: icone junto ao centro, nome e detalhe para fora
                bool casa = it.Lado == Lado.Casa;
                double q = 16;
                double xi = casa ? c - rr - 14 - q : c + rr + 14;
                var nn = T(it.Nome, 13.5, it.Tipo is TipoLance.GolAnulado or PenaltiFalhado ? Fraco : Texto, true);
                var nd = T(it.Detalhe, it.Tipo == Substituicao ? 12.5 : 11.5, Fraco);
                double alto = nn.Height + nd.Height - 2, yt = meio - alto / 2;
                if (it.Tipo == Substituicao)
                {
                    // seta verde para cima na linha de quem entra, vermelha para baixo na de quem sai
                    double qa = 11, xa = xi + (q - qa) / 2;
                    Icones.Seta(dc, true, new Rect(xa, yt + nn.Height / 2 - qa / 2, qa, qa));
                    Icones.Seta(dc, false, new Rect(xa, yt + nn.Height - 2 + nd.Height / 2 - qa / 2, qa, qa));
                }
                else if (it.Tipo == PenaltiFalhado) Icones.PenaltiFalhado(dc, new Rect(xi, meio - q / 2, q, q));
                else Icones.Desenhar(dc, it.Tipo, it.SegundoAmarelo, new Rect(xi, meio - q / 2, q, q));
                double largura = c - rr - 14 - q - 10 - 12;
                if (casa)
                {
                    double xr = xi - 10;
                    using (dc.PushClip(new Rect(12, y, largura, Alto)))
                    {
                        dc.DrawText(nn, new Point(xr - nn.Width, yt));
                        dc.DrawText(nd, new Point(xr - nd.Width, yt + nn.Height - 2));
                    }
                }
                else
                {
                    double xl = xi + q + 10;
                    using (dc.PushClip(new Rect(xl, y, largura, Alto)))
                    {
                        dc.DrawText(nn, new Point(xl, yt));
                        dc.DrawText(nd, new Point(xl, yt + nn.Height - 2));
                    }
                }
            }
        }
    }

    // ---------------------------------------------------------------- comandos

    void Fechar()
    {
        if (_fechou) return;
        _fechou = true;
        Fechou?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (e.Key is Key.Enter or Key.Escape) { Fechar(); e.Handled = true; }
    }

    static IBrush Hex(string s) => new ImmutableSolidColorBrush(Color.Parse(s));
}
