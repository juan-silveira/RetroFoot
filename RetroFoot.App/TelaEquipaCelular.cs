using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RetroFoot.Motor.Mercado;
using RetroFoot.Motor.Patrocinio;
using RetroFoot.Motor.Rodada;
using Mem = RetroFoot.Motor.Partida.Memoria;
using SEquipa = RetroFoot.Motor.Save.Equipa;
using SJogador = RetroFoot.Motor.Save.Jogador;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// A tela da equipa no celular (Celular.cs), em pe e ao toque, nas duas peles do kit
// (CelularUi.cs), no desenho do esboco: o topo com o escudo, o tecnico, a divisao, o dinheiro e
// a moral; as abas do time em pilulas (abrem na gaveta); o elenco por setor, uma linha por
// jogador (o ponto da esquerda troca titular / reserva / fora; o resto da linha abre a ficha);
// o Jogar embaixo; e a barra com os menus do jogo, que abrem na gaveta.
// A logica e a da tela do computador: o Painel (TelaEquipa.cs) fica como controlador, sem ser
// mostrado (menus, escalacao, vender, renovar, gravar, jogar), e esta tela so o desenha.
public sealed partial class TelaEquipa
{
    // o que a tela do celular usa do Painel
    sealed partial class Painel
    {
        internal IEnumerable<(string Menu, List<(string Texto, Action? Acao, Func<bool>? Hab)> Itens)> MenusCelular =>
            _menus.Select(m => (m.Texto, m.Itens.Select(i => (i.Texto, i.Acao, i.Habilitado)).ToList()));
        internal List<SJogador> OrdemCelular() => Ordem();
        internal void AlternarCelular(SJogador p) => Alternar(p);
        internal void VenderCelular(SJogador p) { _sel = p; Vender(); }
        internal void RenovarCelular(SJogador p) { _sel = p; Renovar(); }
        internal bool PodeJogar => JogarHabilitado();
        internal void JogarCelular() => Jogar();
        internal void VoltarCelular() => Voltar();
        internal SEquipa? Adversario => _adv;
        internal string DinheiroCelular(long v) => Dinheiro(v);
        internal static double MoralCelular(SEquipa e) => Moral(e);
        internal static string NomeCelular(SJogador p) => NomeJogador(p);
        internal static string PaisCelular(SJogador p) => PaisDe(p.Pais);
        internal static string TextoCelular(byte[] b) => Texto(b);
        internal static int AmarelosCelular(SJogador p) => Amarelos(p);
        internal int HistCelular(SJogador p, int qual) => Hist(p, qual);
        // a mensagem que o Painel mostraria na pagina Mensagem (Gravar, Vender...), uma vez
        internal string? TirarMensagem()
        {
            if (_pagina != PgMensagem) return null;
            _pagina = PaginaInicial;
            return _mensagem;
        }

    internal sealed class EquipaCelular : UserControl
    {
        readonly TelaEquipa _tela;
        readonly Painel _p;
        readonly Sessao _s;
        readonly SEquipa _e;
        readonly Grid _raiz = new();
        readonly ContentControl _topo = new(), _elenco = new();
        readonly Panel _gaveta = new() { IsVisible = false };
        CUi.Botao? _jogar;
        Action? _aoFecharGaveta;

        static readonly string[] Siglas = { "FP", "COR", "CAV", "CAN", "CAC", "SAR" };

        // no Retro a tela segue as cores da equipa, como a do classico: o fundo e o topo na cor
        // de fundo, os textos na cor da letra, e a lista de jogadores invertida (fundo na cor da
        // letra, nomes na cor do fundo). No Moderno, o tema do kit
        IBrush _corLetra = Brushes.Black, _corFundo = Brushes.White;
        static IBrush Alfa(IBrush b, double o) => new SolidColorBrush(((ISolidColorBrush)b).Color, o);
        IBrush TFundo => CUi.Retro ? _corFundo : CUi.Fundo;
        IBrush TBarra => CUi.Retro ? _corFundo : CUi.Barra;
        IBrush TTexto => CUi.Retro ? _corLetra : CUi.TextoBarra;
        IBrush TFraco => CUi.Retro ? Alfa(_corLetra, 0.75) : CUi.FracoBarra;
        IBrush TSecao => CUi.Retro ? _corLetra : CUi.TituloSecao;
        IBrush LFundo => CUi.Retro ? _corLetra : TemaVerde.CartaoHub;
        IBrush? LTexto => CUi.Retro ? _corFundo : null;
        IBrush LFraco => CUi.Retro ? Alfa(_corFundo, 0.7) : CUi.Fraco;
        IBrush LForte => CUi.Retro ? _corFundo : TemaVerde.Destaque;
        IBrush LLinha => CUi.Retro ? Alfa(_corFundo, 0.35) : CUi.Linha;
        static readonly string[] Setores = { "Goleiros", "Defensores", "Meio-campistas", "Atacantes" };
        static readonly string[] NomePosicao = { "Goleiro", "Defensor", "Meio-campista", "Atacante" };

        public EquipaCelular(TelaEquipa tela, Painel p, Sessao s, SEquipa e)
        {
            _tela = tela; _p = p; _s = s; _e = e;
            (_corLetra, _corFundo) = Cores(e);
            Background = TFundo;
            FontFamily = CUi.Fonte;
            _raiz.RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto");
            _raiz.Children.Add(_topo);
            var abas = Abas();
            Grid.SetRow(abas, 1);
            _raiz.Children.Add(abas);
            Grid.SetRow(_elenco, 2);
            _raiz.Children.Add(_elenco);
            var jogar = BarraJogar();
            Grid.SetRow(jogar, 3);
            _raiz.Children.Add(jogar);
            var nav = Navegacao();
            Grid.SetRow(nav, 4);
            _raiz.Children.Add(nav);
            Grid.SetRowSpan(_gaveta, 5);
            _raiz.Children.Add(_gaveta);
            Content = _raiz;
            Refazer();
            AttachedToVisualTree += (_, _) =>
            {
                _s.SemDialogos -= DepoisDeJanela;
                _s.SemDialogos += DepoisDeJanela;
                if (TopLevel.GetTopLevel(this) is { } tl) { tl.BackRequested -= Voltar; tl.BackRequested += Voltar; }
            };
            DetachedFromVisualTree += (_, _) =>
            {
                _s.SemDialogos -= DepoisDeJanela;
                if (TopLevel.GetTopLevel(this) is { } tl) tl.BackRequested -= Voltar;
            };
        }

        // o Voltar do Android fecha a gaveta antes de qualquer coisa
        void Voltar(object? o, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (!e.Handled && _gaveta.IsVisible) { FecharGaveta(); e.Handled = true; }
        }

        // uma janela do jogo fechou (o elenco, o dinheiro e a escalacao podem ter mudado)
        void DepoisDeJanela()
        {
            _p.VoltarCelular();
            Refazer();
        }

        // depois de cada acao: o topo, o elenco, o Jogar e a mensagem do Painel
        void Refazer()
        {
            _topo.Content = Topo();
            _elenco.Content = Elenco();
            if (_jogar != null) _jogar.Habilitado = _p.PodeJogar;
            if (_p.TirarMensagem() is { Length: > 0 } m) Aviso(m);
        }

        // ------------------------------------------------------------ o topo
        Control Topo()
        {
            var m = _s.J.M;
            int dv = m.DivisaoDa(_e);
            string onde = dv < 0 ? "" : dv >= 4 ? T("Distrital")
                : LugarNaDivisao(m.S.Divisoes[dv].Equipas.IndexOf(_e.ID) + 1, Pascal(m.S.Divisoes[dv].Nome, 20));
            string tecnico = _s.TreinadorDa(_e) is { } t ? Painel.TextoCelular(t.Nome) : "";
            var (letra, fundo) = Cores(_e);
            Control escudo = (CUi.Retro ? null : Escudos.Controle(_e, 52)) ?? Circulo(fundo, letra, 44);
            var nome = CUi.Txt(Painel.TextoCelular(_e.NomeCompleto), 19, TTexto, FontWeight.Bold);
            var sub = CUi.Txt(tecnico + (onde.Length > 0 ? " · " + onde : ""), 13, TFraco);
            var cab = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*") };
            escudo.VerticalAlignment = VerticalAlignment.Center;
            cab.Children.Add(escudo);
            var nomes = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { nome, sub } };
            Grid.SetColumn(nomes, 2);
            cab.Children.Add(nomes);
            var numeros = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Margin = new Thickness(0, 12, 0, 0) };
            Control Num(string r, Control v, int c)
            {
                var s = new StackPanel { Spacing = 2, Children = { CUi.Txt(r, 12, TFraco), v } };
                Grid.SetColumn(s, c);
                return s;
            }
            numeros.Children.Add(Num(T("Temporada"), CUi.Txt(m.Ano.ToString(), 16, TTexto, FontWeight.Bold), 0));
            numeros.Children.Add(Num(T("Em caixa"), CUi.Txt(_p.DinheiroCelular((int)_e.Dinheiro), 16, TTexto, FontWeight.Bold), 1));
            double moral = Math.Clamp(Math.Round(Painel.MoralCelular(_e) * 10, MidpointRounding.ToEven) / 20, 0, 1);
            var barra = new ProgressBar
            {
                Minimum = 0, Maximum = 1, Value = moral, Height = 8, MinHeight = 8, MinWidth = 0, Margin = new Thickness(0, 6, 8, 0),
                Foreground = CUi.Retro ? _corLetra : TemaVerde.Destaque, Background = CUi.Retro ? Alfa(_corLetra, 0.3) : TemaVerde.Alfa(Colors.White, 0.2),
                CornerRadius = new CornerRadius(CUi.Retro ? 0 : 4),
            };
            numeros.Children.Add(Num(T("Moral"), barra, 2));
            return new Border
            {
                Background = TBarra, Padding = new Thickness(16, 14, 16, 14), Child = new StackPanel { Children = { cab, numeros } },
                CornerRadius = new CornerRadius(0, 0, CUi.Retro ? 0 : 22, CUi.Retro ? 0 : 22),
                BorderBrush = CUi.Retro ? _corLetra : null, BorderThickness = new Thickness(0, 0, 0, CUi.Retro ? 1 : 0),
            };
        }

        static (IBrush Letra, IBrush Fundo) Cores(SEquipa e)
        {
            IBrush C(byte[] b, IBrush padrao) => b.Length < 4 || (b[3] & 0x80) != 0 ? padrao : new SolidColorBrush(Color.FromRgb(b[0], b[1], b[2]));
            return (C(e.CorLetra, Brushes.Black), C(e.CorFundo, Brushes.White));
        }

        // sem logo (ou no Retro): o circulo nas cores da equipa
        static Control Circulo(IBrush fundo, IBrush aro, double tam) => new Border
        {
            Width = tam, Height = tam, CornerRadius = new CornerRadius(tam / 2), Background = fundo, BorderBrush = aro, BorderThickness = new Thickness(3),
        };

        // ------------------------------------------------------------ as abas (pilulas)
        Control Abas()
        {
            var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12, 10, 12, 4) };
            void Aba(string nome, Func<Control> conteudo)
            {
                var b = new CUi.Botao(T(nome), false, () => AbrirGaveta(T(nome), conteudo()));
                b.MinHeight = 38;
                b.Padding = new Thickness(16, 6);
                s.Children.Add(b);
            }
            Aba("Jogo", PaginaJogo);
            Aba("Escalação", PaginaEscalacao);
            Aba("Finanças", PaginaFinancas);
            if (_p.Adversario != null) Aba("Adversário", PaginaAdversario);
            if (_s.J.Op.Patrocinios) Aba("Patrocínio", PaginaPatrocinio);
            return new ScrollViewer { Content = s, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }

        Control Linha(string rotulo, string valor, IBrush? cor = null)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 30 };
            g.Children.Add(CUi.Txt(rotulo, 14, CUi.Fraco));
            var v = CUi.Txt(valor, 15, cor, FontWeight.SemiBold);
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            return g;
        }

        Control PaginaJogo()
        {
            var p = new StackPanel { Spacing = 8 };
            var adv = _p.Adversario;
            var jogo = _tela._jogo;
            if (adv != null && jogo != null)
            {
                // a caixa do adversario nas cores dele: o nome e "CASA|FORA titulo" (4:12a5)
                string nome = Painel.TextoCelular(adv.NomeCurto);
                var m = _s.J.M;
                if (jogo.Taca != 0 && m.DivisaoDa(adv) is var d && d >= 0) nome += " (" + Pascal(m.S.Divisoes[d].Nome, 20) + ")";
                string lado = jogo.Casa == _e.ID ? T("CASA") : T("FORA");
                p.Children.Add(CUi.SecaoFolha(T("Adversário")));
                p.Children.Add(CaixaAdversario(adv, nome, lado + " " + Br(Pascal(jogo.B04, 40))));
                // o arbitro: tocar abre a ajuda do arbitro (a balanca, textRefereeClick)
                if (jogo.Casa != 0 && jogo.Fora != 0 && _s.S.Arbitros.FindLast(x => x.ID == jogo.Arbitro) is { } arb)
                {
                    var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                    g.Children.Add(new StackPanel
                    {
                        Spacing = 1,
                        Children =
                        {
                            CUi.Txt(T("Árbitro"), 13, CUi.Fraco),
                            CUi.Txt(Texto(arb.Nome) + " (" + NomePais(PaisDe(arb.Pais)) + ")", 15, null, FontWeight.Bold),
                        },
                    });
                    var seta = CUi.Txt("⚖", 20, CUi.Destaque);
                    Grid.SetColumn(seta, 1);
                    g.Children.Add(seta);
                    var t = CUi.Toque(g, AbrirArbitro, 52);
                    t.Padding = new Thickness(0, 4);
                    p.Children.Add(t);
                }
                p.Children.Add(CUi.Txt(T("Moral do adversário"), 13, CUi.Fraco));
                p.Children.Add(BarraMoral(adv));
            }
            else p.Children.Add(CUi.Txt(T("Sem jogo nesta rodada."), 14, CUi.Fraco));
            p.Children.Add(Linha(T("Dinheiro em caixa"), _p.DinheiroCelular((int)_e.Dinheiro)));
            p.Children.Add(CUi.Txt(T("Moral"), 13, CUi.Fraco));
            p.Children.Add(BarraMoral(_e));
            return p;
        }

        // a caixa com o nome nas cores da equipa (o Moderno com o logo)
        Control CaixaAdversario(SEquipa adv, string nome, string? linha2)
        {
            var (letra, fundo) = Cores(adv);
            var s = new StackPanel { Spacing = 1 };
            s.Children.Add(CUi.Txt(nome, 16, letra, FontWeight.Bold));
            if (linha2 != null) s.Children.Add(CUi.Txt(linha2, 13.5, letra));
            Control c = s;
            if (!CUi.Retro)
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,10,*") };
                g.Children.Add(CUi.Escudo(adv, 34));
                Grid.SetColumn(s, 2);
                g.Children.Add(s);
                c = g;
            }
            return new Border { Background = fundo, Padding = new Thickness(10, 6), Child = c, CornerRadius = new CornerRadius(CUi.Retro ? 0 : 10) };
        }

        // a barra da moral (MedidorMoral): de 0 a 20, verde acima de 1,25, vermelha abaixo de 0,65
        static Control BarraMoral(SEquipa e)
        {
            double m = Painel.MoralCelular(e);
            IBrush cor = m > 1.25 ? new SolidColorBrush(Color.FromRgb(0, 200, 0)) : m < 0.65 ? new SolidColorBrush(Color.FromRgb(230, 0, 0)) : new SolidColorBrush(Color.FromRgb(240, 210, 0));
            return new ProgressBar
            {
                Minimum = 0, Maximum = 20, Value = Math.Clamp(Math.Round(m * 10, MidpointRounding.ToEven), 0, 20), Height = 18, MinHeight = 18,
                Foreground = cor, Background = CUi.Retro ? W95.Branco : TemaVerde.Alfa(Colors.White, 0.15),
                BorderBrush = CUi.Retro ? W95.Escura : null, BorderThickness = new Thickness(CUi.Retro ? 1 : 0),
                CornerRadius = new CornerRadius(CUi.Retro ? 0 : 6),
            };
        }

        // a ajuda do arbitro (TRefereeAidDlg, seg06:27b5): a balanca e, com o add-in 0x23 ou o
        // launcher, os dois times nas cores deles com a influencia (0 a 15)
        void AbrirArbitro()
        {
            var jogo = _tela._jogo;
            if (jogo == null) return;
            var arb = _s.S.Arbitros.FindLast(a => a.ID == jogo.Arbitro);
            var casa = _s.Equipa(jogo.Casa);
            var fora = _s.Equipa(jogo.Fora);
            int vc = jogo.W4C, vf = jogo.W4E;
            var p = new StackPanel { Spacing = 8 };
            var balanca = new Pintor((dc, t) =>
            {
                // o desenho do original tem 330 x 190: centrado na largura
                double k = Math.Min(1.0, t.Width / 330);
                using (dc.PushTransform(Matrix.CreateScale(k, k) * Matrix.CreateTranslation((t.Width - 330 * k) / 2, 0)))
                    Balanca(dc, (short)(vc - vf), casa, fora);
            }) { Height = 190 };
            p.Children.Add(new Border { Background = Brushes.White, Child = balanca, Padding = new Thickness(0, 4), CornerRadius = new CornerRadius(CUi.Retro ? 0 : 12) });
            if (_p.ArbitroCompleto)
            {
                void Time(SEquipa? eq, int v, byte[] pen)
                {
                    if (eq == null) return;
                    int k = Math.Min(v, 15);
                    string extra = _p._launcher ? T("   Pênalti: {0}%   Cartões: -{1}%", pen[k], 2 * (k + 1)).Trim() : null!;
                    p.Children.Add(CaixaAdversario(eq, Painel.TextoCelular(eq.NomeCurto), extra));
                    p.Children.Add(new ProgressBar
                    {
                        Minimum = 0, Maximum = 15, Value = Math.Clamp(v, 0, 15), Height = 16, MinHeight = 16,
                        Foreground = CUi.Retro ? W95.Azul : TemaVerde.Destaque, Background = CUi.Retro ? W95.Branco : TemaVerde.Alfa(Colors.White, 0.15),
                        BorderBrush = CUi.Retro ? W95.Escura : null, BorderThickness = new Thickness(CUi.Retro ? 1 : 0), Margin = new Thickness(0, 0, 0, 6),
                    });
                }
                Time(casa, vc, PenaltiCasa);
                Time(fora, vf, PenaltiFora);
            }
            AbrirGaveta(arb != null ? Texto(arb.Nome) : T("Tendência do Árbitro"), p);
        }

        Control PaginaFinancas()
        {
            var p = new StackPanel { Spacing = 6 };
            p.Children.Add(Linha(T("Dinheiro em caixa"), _p.DinheiroCelular((int)_e.Dinheiro), (int)_e.Dinheiro < 0 ? CUi.Erro : null));
            long folha = _e.Jogadores.Sum(j => (long)j.Salario);
            p.Children.Add(Linha(T("Salários por jogo"), _p.DinheiroCelular(folha)));
            p.Children.Add(Linha(T("Jogadores"), _e.Jogadores.Count.ToString()));
            var botoes = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var (menu, itens) in _p.MenusCelular)
                foreach (var (texto, acao, hab) in itens)
                    if (acao != null && (texto.Contains(T("Estádio...")) || texto.Contains(T("Pedir / pagar empréstimo..."))))
                    {
                        var b = new CUi.Botao(texto.TrimEnd('.'), false, () => { FecharGaveta(); acao(); });
                        b.HorizontalAlignment = HorizontalAlignment.Stretch;
                        botoes.Children.Add(b);
                    }
            p.Children.Add(botoes);
            return p;
        }

        Control PaginaEscalacao()
        {
            var p = new StackPanel { Spacing = 8 };
            var escalar = _p.MenusCelular.Skip(1).FirstOrDefault().Itens ?? new();
            var formacoes = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
            var outros = new List<CUi.Botao>();
            foreach (var (texto, acao, hab) in escalar)
            {
                if (texto == "-" || acao == null) continue;
                var b = new CUi.Botao(texto, texto == T("Automático"), () => { acao(); FecharGaveta(); Refazer(); });
                b.Habilitado = hab?.Invoke() ?? true;
                if (char.IsDigit(texto[0])) { b.MinWidth = 86; formacoes.Children.Add(b); }
                else outros.Add(b);
            }
            p.Children.Add(CUi.Txt(T("Formação"), 13, CUi.Fraco, FontWeight.SemiBold));
            p.Children.Add(formacoes);
            p.Children.Add(CUi.Botoes(outros.ToArray()));
            p.Children.Add(CUi.Txt(T("Toque no ponto à esquerda de cada jogador para trocar entre titular, reserva e fora."), 13, CUi.Fraco, FontWeight.Normal, true));
            return p;
        }

        // a pagina Adversario do original: o nome nas cores dele, a moral, o tecnico, o
        // Calendario e o Elenco; e o elenco dele na lista do Retro (nas cores dele) ou do kit
        Control PaginaAdversario()
        {
            var adv = _p.Adversario!;
            var p = new StackPanel { Spacing = 8 };
            p.Children.Add(CaixaAdversario(adv, Painel.TextoCelular(adv.NomeCurto), null));
            p.Children.Add(BarraMoral(adv));
            p.Children.Add(Linha(T("Técnico"), _s.TreinadorDa(adv) is { } t ? Texto(t.Nome) : ""));
            var cal = new CUi.Botao(T("Calendário"), false, () => { FecharGaveta(); Campeonato.Calendario(_s, adv, _p.VoltarCelular); });
            if (Registo.AddIn(0x10))
                p.Children.Add(CUi.Botoes(cal, new CUi.Botao(T("Elenco"), false, () => { FecharGaveta(); TelaPlantel.Abrir(_s, adv, _e, 0, _p.VoltarCelular); })));
            else p.Children.Add(CUi.Botoes(cal));
            var (letra, fundo) = Cores(adv);
            var jog = adv.Jogadores.OrderBy(x => x.Posicao).ThenByDescending(x => x.Forca).ToList();
            var lista = new StackPanel { Background = CUi.Retro ? letra : null };
            for (int i = 0; i < jog.Count; i++)
            {
                var j = jog[i];
                bool traco = i < jog.Count - 1 && Math.Min((int)j.Posicao, 3) != Math.Min((int)jog[i + 1].Posicao, 3);
                IBrush cor = CUi.Retro ? fundo : CUi.Texto;
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("26,*,36"), MinHeight = 30 };
                void Por(string x, int c, HorizontalAlignment h = HorizontalAlignment.Left)
                {
                    var tb = CUi.Txt(x, 14, cor, FontWeight.Bold);
                    tb.HorizontalAlignment = h;
                    Grid.SetColumn(tb, c);
                    g.Children.Add(tb);
                }
                Por(Posicao(Math.Min((int)j.Posicao, 3)), 0, HorizontalAlignment.Center);
                Por(Painel.NomeCelular(j), 1);
                Por(j.Forca.ToString(), 2, HorizontalAlignment.Right);
                g.Margin = new Thickness(0, 0, 8, 0);
                lista.Children.Add(new Border
                {
                    Child = g, BorderBrush = CUi.Retro ? fundo : CUi.Linha,
                    BorderThickness = new Thickness(0, 0, 0, traco ? 1 : 0), Margin = new Thickness(0, 0, 0, traco ? 4 : 0),
                });
            }
            p.Children.Add(CUi.Retro
                ? new Border
                {
                    Padding = new Thickness(2), Background = letra, Child = lista,
                    BoxShadow = BoxShadows.Parse("inset 1 1 0 0 #6A6A6A, inset 2 2 0 0 #000000, inset -1 -1 0 0 #FFFFFF, inset -2 -2 0 0 #E3E3E3"),
                }
                : CUi.Cartao_(null, lista));
            return p;
        }

        Control PaginaPatrocinio()
        {
            var p = new StackPanel { Spacing = 6 };
            var marcas = _s.J.Op.Marcas;
            var mc = _e.MarcaMaster() != 0 ? marcas?.Registro(_e.MarcaMaster()) : null;
            p.Children.Add(CUi.TituloSecao_(T("Patrocinador master")));
            if (mc != null)
            {
                p.Children.Add(Linha(mc.Nome, _p.DinheiroCelular(_e.ValorMaster()) + " " + T("por temporada")));
                int n = _e.EpocasMaster();
                p.Children.Add(Linha(T("Restante"), T(n == 1 ? "{0} temporada restante" : "{0} temporadas restantes", (char)('0' + n))));
            }
            else p.Children.Add(CUi.Txt(T("Sem patrocinador"), 14, CUi.Fraco));
            var mp = _e.JogosPontual() != 0 ? marcas?.Registro(_e.MarcaPontual()) : null;
            p.Children.Add(CUi.TituloSecao_(T("Patrocínio pontual")));
            if (mp != null)
            {
                int j = _e.JogosPontual();
                p.Children.Add(Linha(mp.Nome, T("{0} por jogo", _p.DinheiroCelular(_e.ValorPontual()))));
                p.Children.Add(Linha(T("Restante"), T(j == 1 ? "{0} jogo restante" : "{0} jogos restantes", (char)('0' + j))));
            }
            else p.Children.Add(CUi.Txt(T("Nenhum no momento"), 14, CUi.Fraco));
            return p;
        }

        // ------------------------------------------------------------ o elenco
        Control Elenco()
        {
            if (CUi.Retro) return ElencoRetro();
            var lista = new StackPanel { Spacing = 6, Margin = new Thickness(12, 4, 12, 12) };
            foreach (var grupo in _p.OrdemCelular().GroupBy(j => Math.Min((int)j.Posicao, 3)))
            {
                var cab = new Grid { ColumnDefinitions = new ColumnDefinitions("*,44,44"), Margin = new Thickness(4, 10, 8, 2) };
                cab.Children.Add(CUi.Txt((T(Setores[grupo.Key]) + "  ·  " + grupo.Count()).ToUpperInvariant(), 12.5, TSecao, FontWeight.Bold));
                var c1 = CUi.Txt(T("Comp"), 11.5, TSecao, FontWeight.SemiBold); c1.HorizontalAlignment = HorizontalAlignment.Center;
                var c2 = CUi.Txt(T("Força"), 11.5, TSecao, FontWeight.SemiBold); c2.HorizontalAlignment = HorizontalAlignment.Center;
                Grid.SetColumn(c1, 1); Grid.SetColumn(c2, 2);
                cab.Children.Add(c1); cab.Children.Add(c2);
                lista.Children.Add(cab);
                var cartao = new StackPanel();
                foreach (var j in grupo) cartao.Children.Add(LinhaJogador(j));
                lista.Children.Add(new Border
                {
                    Background = LFundo, CornerRadius = new CornerRadius(CUi.Retro ? 0 : 14),
                    BorderBrush = CUi.Retro ? _corLetra : CUi.Linha, BorderThickness = new Thickness(1), ClipToBounds = true, Child = cartao,
                });
            }
            return new ScrollViewer { Content = lista, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }

        // o Retro: a lista do computador (ListBoxOfPlayersDrawItem) ao toque: uma caixa afundada
        // nas cores da equipa trocadas (fundo na cor da letra), texto em negrito, uma linha por
        // jogador (a marca de titular / reserva, a posicao, o nome, a forca, S, L e o salario
        // com o + se ja revisto) e o traco no fim de cada posicao. Tocar na marca troca titular /
        // reserva / fora; no resto da linha, a ficha
        Control ElencoRetro()
        {
            var lista = new StackPanel { Background = _corLetra, Margin = new Thickness(0, 0, 8, 0) };
            var o = _p.OrdemCelular();
            for (int i = 0; i < o.Count; i++)
            {
                bool fim = i == o.Count - 1 || Math.Min((int)o[i].Posicao, 3) != Math.Min((int)o[i + 1].Posicao, 3);
                lista.Children.Add(LinhaRetro(o[i], fim && i < o.Count - 1));
            }
            // o cabecalho das colunas fica fixo em cima (TitulosElenco do computador)
            var cab = new Border { Child = ColunasRetro(true, null), BorderBrush = _corFundo, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 8, 0) };
            var d = new DockPanel();
            DockPanel.SetDock(cab, Dock.Top);
            d.Children.Add(cab);
            d.Children.Add(new ScrollViewer { Content = lista, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            var caixa = new Border
            {
                Margin = new Thickness(10, 6, 10, 10), Padding = new Thickness(2), Background = _corLetra,
                BoxShadow = BoxShadows.Parse("inset 1 1 0 0 #6A6A6A, inset 2 2 0 0 #000000, inset -1 -1 0 0 #FFFFFF, inset -2 -2 0 0 #E3E3E3"),
                Child = d,
            };
            return caixa;
        }

        // nota e risco de lesao so na versao Atual (as regras do launcher), como no computador
        bool Atual => _s.J.M.Regras.CartoesAmarelos;

        // as colunas da lista (titulos ou um jogador): marca, P, nome, forca, [nota, RL], S, L,
        // salario (com o + de ja revisto)
        Grid ColunasRetro(bool titulos, SJogador? j)
        {
            var cor = _corFundo;
            var g = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions(Atual ? "34,20,*,28,30,28,18,14,84" : "34,20,*,30,20,16,90"),
                MinHeight = titulos ? 26 : 34,
            };
            void Por(string t, int col, HorizontalAlignment h = HorizontalAlignment.Left)
            {
                var x = CUi.Txt(t, titulos ? 11.5 : 14, cor, titulos ? FontWeight.Normal : FontWeight.Bold);
                x.HorizontalAlignment = h;
                Grid.SetColumn(x, col);
                g.Children.Add(x);
            }
            string S(SJogador p) => p.Suspensao != 0 ? "S" : Atual && Painel.AmarelosCelular(p) > 0 ? "A" : "";
            int c = 1;
            Por(titulos ? "P" : Posicao(Math.Min((int)j!.Posicao, 3)), c++);
            Por(titulos ? T("Nome") : Painel.NomeCelular(j!), c++);
            Por(titulos ? "F" : j!.Forca.ToString(), c++, HorizontalAlignment.Right);
            if (Atual)
            {
                Por(titulos ? "N" : j!.Nota.ToString(), c++, HorizontalAlignment.Right);
                Por(titulos ? "RL" : j!.Lesao.ToString(), c++, HorizontalAlignment.Right);
            }
            Por(titulos ? "S" : S(j!), c++, HorizontalAlignment.Center);
            Por(titulos ? "L" : j!.JogosLesao != 0 ? "L" : "", c++);
            Por(titulos ? T("Salário") : _p.DinheiroCelular(j!.Salario) + (MercadoJogo.PodeMudarSalario(j) ? " " : "+"), c, HorizontalAlignment.Right);
            return g;
        }

        Control LinhaRetro(SJogador j, bool traco)
        {
            var cor = _corFundo;
            TextBlock T_(string t, HorizontalAlignment h = HorizontalAlignment.Left)
            {
                var x = CUi.Txt(t, 14, cor, FontWeight.Bold);
                x.HorizontalAlignment = h;
                return x;
            }
            int esc = Mem.Escalacao(j);
            // a marca do original: titular um quadradinho, reserva um traco
            var marca = new Border
            {
                Width = esc == Mem.EscReserva ? 10 : 6, Height = esc == Mem.EscReserva ? 2 : 6,
                Background = esc == Mem.EscFora ? Brushes.Transparent : cor,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var alvo = new Clicavel { Background = Brushes.Transparent, Child = marca, CorFoco = null };
            alvo.Clique += () => { _p.AlternarCelular(j); Refazer(); };
            var g = ColunasRetro(false, j);
            g.Children.Add(alvo);
            var corpo = new Clicavel { Background = Brushes.Transparent, Child = g, CorFoco = null, Padding = new Thickness(0, 0, 4, 0) };
            corpo.Clique += () => { if (!_arrastou) AbrirFicha(j); };
            var frente = new Border { Child = corpo, Background = _corLetra };
            return new Border
            {
                Child = Deslizante(frente, Tras(j, _corFundo, _corLetra, false)), BorderBrush = cor,
                BorderThickness = new Thickness(0, 0, 0, traco ? 1 : 0), Margin = new Thickness(0, 0, 0, traco ? 4 : 0),
            };
        }

        Control LinhaJogador(SJogador j)
        {
            int esc = Mem.Escalacao(j);
            bool fora = j.JogosLesao > 0 || j.Suspensao > 0;
            // o ponto da escalacao (toque troca): titular cheio, reserva anel, fora apagado
            var ponto = new Border
            {
                Width = 14, Height = 14, CornerRadius = new CornerRadius(7),
                Background = esc == Mem.EscTitular ? (CUi.Retro ? _corFundo : CUi.Bom) : Brushes.Transparent,
                BorderBrush = CUi.Retro ? (esc == Mem.EscFora ? LLinha : _corFundo)
                    : esc == Mem.EscReserva ? TemaVerde.Destaque : esc == Mem.EscTitular ? CUi.Bom : CUi.Linha,
                BorderThickness = new Thickness(esc == Mem.EscReserva ? 2.5 : 1.5),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var alvoPonto = new Clicavel { Width = 40, MinHeight = 40, Background = Brushes.Transparent, Child = ponto, CorFoco = null };
            alvoPonto.Clique += () => { _p.AlternarCelular(j); Refazer(); };
            var pos = CUi.Txt(Posicao(Math.Min((int)j.Posicao, 3)), 12.5, LForte, FontWeight.Bold);
            pos.Width = 18;
            var meio = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            if (W95.Imagem($"Bandeiras/{Painel.PaisCelular(j)}.png") is { } bb)
                meio.Children.Add(new Image { Source = bb, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center });
            var nome = CUi.Txt(Painel.NomeCelular(j), 15, fora ? LFraco : LTexto, FontWeight.Medium);
            meio.Children.Add(nome);
            foreach (var sel in Selos(j)) meio.Children.Add(sel);
            var comp = CUi.Txt(j.Comportamento < Siglas.Length ? Siglas[j.Comportamento] : "", 11.5, LFraco, FontWeight.Bold);
            comp.HorizontalAlignment = HorizontalAlignment.Center;
            var forca = CUi.Txt(j.Forca.ToString(), 16, LForte, FontWeight.Bold);
            forca.HorizontalAlignment = HorizontalAlignment.Center;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,44,44"), MinHeight = 40 };
            g.Children.Add(alvoPonto);
            Grid.SetColumn(pos, 1); g.Children.Add(pos);
            var corpo = new Clicavel { Background = Brushes.Transparent, Child = meio, CorFoco = null, Padding = new Thickness(6, 0, 0, 0) };
            corpo.Clique += () => { if (!_arrastou) AbrirFicha(j); };
            Grid.SetColumn(corpo, 2); g.Children.Add(corpo);
            Grid.SetColumn(comp, 3); g.Children.Add(comp);
            Grid.SetColumn(forca, 4); g.Children.Add(forca);
            var frente = new Border { Background = TemaVerde.CartaoHub, Child = g };
            return new Border
            {
                BorderBrush = LLinha, BorderThickness = new Thickness(0, 0, 0, 0.5),
                Child = Deslizante(frente, Tras(j, TemaVerde.Selecao, TemaVerde.Texto, true)),
            };
        }

        // ------------------------------------------------------------ deslizar a linha
        // Deslizar a linha de um jogador para a esquerda mostra por tras os outros dados dele
        // (sem abrir a ficha), como na prova do celular; soltar na metade abre ou fecha, e uma
        // so linha fica aberta. O toque depois de arrastar nao abre a ficha
        Control? _linhaAberta;
        bool _arrastou;

        Control Deslizante(Control frente, Control tras)
        {
            var linha = new Grid { ClipToBounds = true };
            tras.HorizontalAlignment = HorizontalAlignment.Right;
            // (escondido com a linha fechada: no Moderno o cartao e meio transparente)
            tras.Opacity = 0;
            linha.Children.Add(tras);
            linha.Children.Add(frente);
            double ini = double.NaN, iniY = 0, desloc = 0;
            bool arrastando = false;
            double Largura() => Math.Max(tras.Bounds.Width, tras.DesiredSize.Width);
            void Mover(double x, bool animar)
            {
                frente.Transitions = animar ? new Avalonia.Animation.Transitions
                {
                    new Avalonia.Animation.TransformOperationsTransition
                    {
                        Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
                    },
                } : null;
                frente.RenderTransform = Avalonia.Media.Transformation.TransformOperations.Parse($"translateX({x:0}px)");
                desloc = x;
                if (x < 0) tras.Opacity = 1;
                else if (!animar) tras.Opacity = 0;
                else DispatcherTimer.RunOnce(() => { if (desloc == 0) tras.Opacity = 0; }, TimeSpan.FromMilliseconds(220));
            }
            linha.Tag = (Action)(() => Mover(0, true));
            linha.AddHandler(PointerPressedEvent, (_, e) =>
            {
                ini = e.GetPosition(linha).X - desloc; iniY = e.GetPosition(linha).Y; arrastando = false; _arrastou = false;
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
            linha.AddHandler(PointerMovedEvent, (_, e) =>
            {
                if (double.IsNaN(ini)) return;
                var pt = e.GetPosition(linha);
                double dx = pt.X - ini;
                if (!arrastando)
                {
                    if (Math.Abs(dx - desloc) < 12 || Math.Abs(dx - desloc) < Math.Abs(pt.Y - iniY)) return;
                    arrastando = _arrastou = true;
                    e.Pointer.Capture(linha);
                    if (_linhaAberta != null && _linhaAberta != linha && _linhaAberta.Tag is Action f) f();
                }
                Mover(Math.Clamp(dx, -Largura(), 0), false);
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
            linha.AddHandler(PointerReleasedEvent, (_, e) =>
            {
                if (arrastando)
                {
                    e.Pointer.Capture(null);
                    bool abre = desloc < -Largura() / 2;
                    Mover(abre ? -Largura() : 0, true);
                    _linhaAberta = abre ? linha : null;
                    e.Handled = true;
                }
                ini = double.NaN;
                arrastando = false;
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
            return linha;
        }

        // o que fica por tras: nota e risco de lesao (versao Atual), jogos, gols, gols na
        // temporada e (salario = true) o salario com "acertado" (o + da lista do computador)
        Control Tras(SJogador j, IBrush fundo, IBrush cor, bool salario)
        {
            var s = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            void Cel(string v, string rotulo)
            {
                var c = new StackPanel { Width = 42, VerticalAlignment = VerticalAlignment.Center };
                var a = CUi.Txt(v, 15, cor, FontWeight.Bold); a.HorizontalAlignment = HorizontalAlignment.Center;
                var b = CUi.Txt(rotulo, 10, cor); b.HorizontalAlignment = HorizontalAlignment.Center; b.Opacity = 0.8;
                a.Tag = b.Tag = "fixo";
                c.Children.Add(a); c.Children.Add(b);
                s.Children.Add(c);
            }
            if (_s.J.M.Regras.CartoesAmarelos) { Cel(j.Nota.ToString(), T("Nota")); Cel(j.Lesao.ToString(), "RL"); }
            Cel(_p.HistCelular(j, Mem.HistJogos).ToString(), T("Jogos"));
            Cel(_p.HistCelular(j, Mem.HistGolos).ToString(), T("Gols"));
            Cel(Mem.Golos14(j).ToString(), T("Temp."));
            if (salario)
            {
                bool acertado = !MercadoJogo.PodeMudarSalario(j);
                var v = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                v.Children.Add(CUi.Txt(_p.DinheiroCelular(j.Salario), 14, cor, FontWeight.Bold));
                v.Children.Add(CUi.Txt(T(acertado ? "Salário acertado" : "Não acertado"), 10.5, cor));
                s.Children.Add(v);
            }
            return new Border { Background = fundo, Padding = new Thickness(6, 0, 12, 0), Child = s };
        }

        // ao lado do nome: amarelos (1 ou 2 sobrepostos), suspensao e lesao
        IEnumerable<Control> Selos(SJogador j)
        {
            Control Selo(string t, IBrush fundo, IBrush letra) => new Border
            {
                Background = fundo, CornerRadius = new CornerRadius(CUi.Retro ? 0 : 5), Padding = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = CUi.Txt(t, 11.5, letra, FontWeight.Bold),
            };
            if (j.Suspensao > 0) yield return Selo("⊘" + j.Suspensao, new SolidColorBrush(Color.Parse("#FFE4E1")), new SolidColorBrush(Color.Parse("#C62828")));
            else if (_s.J.M.Regras.CartoesAmarelos && Painel.AmarelosCelular(j) is int a && a > 0)
                yield return Selo(a == 1 ? "▮" : "▮▮", Brushes.Transparent, new SolidColorBrush(Color.Parse("#F5B400")));
            if (j.JogosLesao > 0) yield return Selo("✚" + j.JogosLesao, Brushes.White, new SolidColorBrush(Color.Parse("#D32F2F")));
        }

        // a ficha do jogador, com as acoes
        void AbrirFicha(SJogador j)
        {
            var p = new StackPanel { Spacing = 8 };
            int pos = Math.Min((int)j.Posicao, 3);
            // o topo: a bandeira, o pais e a posicao (e a Lei Bosman, como no computador)
            string pais = Painel.PaisCelular(j);
            var topo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (CUi.Bandeira(pais, 28) is { } bb) topo.Children.Add(bb);
            topo.Children.Add(CUi.Txt(NomePais(pais) + "  ·  " + T(NomePosicao[pos]), 15, null, FontWeight.SemiBold));
            p.Children.Add(topo);
            if (j.B35 == 1) p.Children.Add(CUi.Txt(T("Não é considerado jogador estrangeiro, ao abrigo da Lei Bosman"), 12.5, CUi.Fraco, FontWeight.Normal, true));
            if (j.JogosLesao > 0) p.Children.Add(CUi.Txt(T(j.JogosLesao == 1 ? "Lesionado por 1 jogo" : "Lesionado por {0} jogos", j.JogosLesao), 14, CUi.Erro, FontWeight.Bold));
            if (j.Suspensao > 0) p.Children.Add(CUi.Txt(T(j.Suspensao == 1 ? "Suspenso por 1 jogo" : "Suspenso por {0} jogos", j.Suspensao), 14, CUi.Erro, FontWeight.Bold));
            // os dados dois por linha
            var dados = new List<(string, string)>
            {
                (T("Força"), j.Forca.ToString()),
                (T("Salário"), _p.DinheiroCelular(j.Salario)),
                (T("Comportamento"), j.Comportamento < Comportamentos.Length ? T(Comportamentos[j.Comportamento]) : j.Comportamento.ToString()),
                (T("Gols na Temporada"), Mem.Golos14(j).ToString()),
            };
            if (_s.J.M.Regras.CartoesAmarelos) { dados.Add((T("Nota"), j.Nota.ToString())); dados.Add((T("Risco de lesão"), j.Lesao.ToString())); }
            p.Children.Add(Pares(dados));
            var hist = new List<(string, string)>
            {
                (T("Jogos"), _p.HistCelular(j, Mem.HistJogos).ToString()),
                (T("Gols"), _p.HistCelular(j, Mem.HistGolos).ToString()),
            };
            if (_s.J.M.Regras.CartoesAmarelos) hist.Add((T("Cartões amarelos"), Painel.AmarelosCelular(j).ToString()));
            hist.Add((T("Cartões vermelhos"), _p.HistCelular(j, Mem.HistVermelhos).ToString()));
            hist.Add((T("Lesões"), _p.HistCelular(j, Mem.HistLesoes).ToString()));
            p.Children.Add(CUi.SecaoFolha(T("Histórico")));
            p.Children.Add(Pares(hist));
            var vender = new CUi.Botao(T("Vender"), true, () => { FecharGaveta(); _p.VenderCelular(j); Refazer(); });
            var renovar = new CUi.Botao(T("Renovar contrato"), false, () => { FecharGaveta(); _p.RenovarCelular(j); Refazer(); });
            renovar.Habilitado = MercadoJogo.PodeMudarSalario(j);
            p.Children.Add(new Border { Height = 2 });
            p.Children.Add(CUi.Botoes(renovar, vender));
            AbrirGaveta(Painel.NomeCelular(j), p);
        }

        // os dados em duas colunas: o rotulo pequeno em cima e o valor embaixo
        static Control Pares(List<(string Rotulo, string Valor)> dados)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12, RowSpacing = 8 };
            for (int i = 0; i < dados.Count; i++)
            {
                g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var c = new StackPanel
                {
                    Spacing = 0,
                    Children = { CUi.Txt(dados[i].Rotulo, 12, CUi.Fraco), CUi.Txt(dados[i].Valor, 16, null, FontWeight.Bold) },
                };
                Grid.SetRow(c, i / 2);
                Grid.SetColumn(c, i % 2);
                g.Children.Add(c);
            }
            while (g.RowDefinitions.Count > (dados.Count + 1) / 2) g.RowDefinitions.RemoveAt(g.RowDefinitions.Count - 1);
            return g;
        }

        // ------------------------------------------------------------ jogar e os menus
        Control BarraJogar()
        {
            _jogar = new CUi.Botao(T("Jogar"), true, () => _p.JogarCelular());
            _jogar.HorizontalAlignment = HorizontalAlignment.Stretch;
            _jogar.MinHeight = 48;
            return new Border { Padding = new Thickness(12, 6, 12, 6), Background = TFundo, Child = _jogar };
        }

        Control Navegacao()
        {
            var g = new UniformGrid { Rows = 1 };
            string[] icones = { "ajustes", "escudo", "escudo", "lupa", "trofeu", "info" };
            int k = 0;
            foreach (var (menu, itens) in _p.MenusCelular)
            {
                var m = menu; var lista = itens; int i = k++;
                var conteudo = new StackPanel
                {
                    Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        Ui.Icone(icones[Math.Min(i, icones.Length - 1)], 20, TTexto),
                        new TextBlock { Text = m, FontFamily = CUi.Fonte, FontSize = 10.5, Foreground = TTexto, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis },
                    },
                };
                var b = new Clicavel { Background = Brushes.Transparent, Child = conteudo, MinHeight = 56, Padding = new Thickness(2, 6), CorFoco = null };
                // o Escalar abre a mesma gaveta da pilula Escalacao (as formacoes e o Automatico)
                if (i == 1) b.Clique += () => AbrirGaveta(T("Escalação"), PaginaEscalacao());
                else b.Clique += () => AbrirGaveta(m, Itens(lista));
                g.Children.Add(b);
            }
            return new Border { Background = TBarra, Child = g, BorderBrush = CUi.Retro ? _corLetra : CUi.Linha, BorderThickness = new Thickness(0, 1, 0, 0) };
        }

        Control Itens(List<(string Texto, Action? Acao, Func<bool>? Hab)> itens)
        {
            var p = new StackPanel { Spacing = 4 };
            foreach (var (texto, acao, hab) in itens)
            {
                if (texto == "-") { p.Children.Add(new Border { Height = 1, Background = CUi.Linha, Margin = new Thickness(0, 4) }); continue; }
                var a = acao;
                var it = new CUi.Item(CUi.Txt(texto, 15), () => { FecharGaveta(); a?.Invoke(); Refazer(); });
                it.IsEnabled = a != null && (hab?.Invoke() ?? true);
                p.Children.Add(it);
            }
            return p;
        }

        // ------------------------------------------------------------ a gaveta (de baixo)
        void AbrirGaveta(string titulo, Control conteudo, Action? aoFechar = null)
        {
            _aoFecharGaveta = aoFechar;
            var cab = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 10) };
            cab.Children.Add(CUi.Txt(titulo, 18, null, FontWeight.Bold));
            var fechar = new CUi.Botao("✕", false, FecharGaveta) { Padding = new Thickness(14, 4), MinHeight = 38 };
            Grid.SetColumn(fechar, 1);
            cab.Children.Add(fechar);
            var folha = new Border
            {
                Background = CUi.Retro ? W95.Face : TemaVerde.FundoTela, VerticalAlignment = VerticalAlignment.Bottom, MaxHeight = Math.Max(400, _raiz.Bounds.Height * 0.92),
                CornerRadius = new CornerRadius(CUi.Retro ? 0 : 22, CUi.Retro ? 0 : 22, 0, 0), Padding = new Thickness(16, 14, 16, 18),
                BorderBrush = CUi.Retro ? W95.Preto : TemaVerde.Borda, BorderThickness = new Thickness(1, 1, 1, 0),
                Child = new DockPanel
                {
                    // (a folga a direita: a barra de rolagem fica por cima dos valores)
                    Children = { cab, new ScrollViewer { Content = new Border { Padding = new Thickness(0, 0, 12, 0), Child = conteudo }, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } },
                },
            };
            DockPanel.SetDock(cab, Dock.Top);
            var veu = new Border { Background = TemaVerde.Veu };
            veu.PointerPressed += (_, e) => { e.Handled = true; FecharGaveta(); };
            _gaveta.Children.Clear();
            _gaveta.Children.Add(veu);
            _gaveta.Children.Add(folha);
            _gaveta.IsVisible = true;
        }

        void FecharGaveta()
        {
            _gaveta.IsVisible = false;
            _gaveta.Children.Clear();
            var f = _aoFecharGaveta;
            _aoFecharGaveta = null;
            f?.Invoke();
        }

        // um aviso curto (Gravar, Vender...) numa caixa
        void Aviso(string texto)
        {
            var caixa = CUi.Caixa_(TelaAcerca.Nome, CUi.Txt(texto, 15, null, FontWeight.Normal, true), new CUi.Botao(T("OK"), true, FecharGaveta));
            _gaveta.Children.Clear();
            _gaveta.Children.Add(caixa);
            _gaveta.IsVisible = true;
        }
    }
    }
}
