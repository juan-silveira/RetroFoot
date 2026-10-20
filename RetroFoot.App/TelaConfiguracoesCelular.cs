using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// As Configuracoes no celular (Celular.cs), em pe e ao toque, nas duas peles do kit
// (CelularUi.cs): a mesma logica e os mesmos textos da tela do computador
// (TelaConfiguracoes.cs), com outro desenho:
//   a lista     o aviso do jogo em curso, as secoes (Jogo, Regras, Aparencia, Idioma, Dados)
//               com o que vale em cada uma, e a nota do que vale quando;
//   as secoes   uma pagina por secao (a seta volta a lista sem perder o que mudou);
//   o rodape    fixo em todas as paginas: o erro em cima e Cancelar / Salvar.
// O formato da tela (4:3 / 16:9) nao existe no celular: fica o que estava gravado.
public sealed partial class TelaConfiguracoes
{
    void MontarCelular(int secao)
    {
        // no Retro, a janela com as abas do computador (TelaConfiguracoesCelularRetro.cs)
        if (CUi.Retro) { RetroCelular(Math.Max(0, secao)); return; }
        Background = CUi.Fundo;
        FontFamily = CUi.Fonte;
        if (secao >= 0) SecaoCelular(secao); else ListaCelular();
    }

    // o rodape de todas as paginas: o erro (quando ha) em cima dos botoes, nunca ao lado
    Control RodapeCelular()
    {
        var erro = CUi.Txt("", 13.5, CUi.Erro, FontWeight.SemiBold, true);
        erro.IsVisible = false;
        _erro = m => { erro.Text = m; erro.IsVisible = m.Length > 0; };
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                erro,
                CUi.Botoes(new CUi.Botao(T("Cancelar"), false, () => Fechar(false)), new CUi.Botao(T("Salvar"), true, () => Salvar(_erro))),
            },
        };
    }

    void PaginaCelular(string titulo, Control corpo, Action voltar, string? sub = null)
    {
        Content = new CUi.Folha(titulo, corpo, RodapeCelular(), sub, inteira: true, fechar: voltar);
        // o foco volta para a tela (o Esc da previa; o controle tocado saiu com a pagina velha)
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    // ---------------------------------------------------------------- a lista das secoes
    void ListaCelular()
    {
        var corpo = new StackPanel { Spacing = 16 };
        if (_emCurso != null)
            corpo.Children.Add(CUi.Cartao_(T("Jogo em curso"), new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    CUi.Txt(RegrasDoJogo(_emCurso), 14.5, null, FontWeight.Medium, true),
                    CCfg.Ajuda(T("O número de equipas vem do jogo gravado e não muda. O que você mudar aqui vale da próxima vez que um jogo for aberto.")),
                },
            }));
        else
            corpo.Children.Add(CCfg.TextoFundo(T("Por padrão, o jogo como em 1998: as adições ficam desligadas até você ligar aqui.")));

        var linhas = new List<Control>();
        var valores = ValoresCelular();
        for (int i = 0; i < Secoes.Length; i++)
        {
            int k = i;
            linhas.Add(CCfg.LinhaSecao(T(Secoes[i]), valores[i], () => SecaoCelular(k)));
        }
        corpo.Children.Add(CUi.Grupo(linhas));
        corpo.Children.Add(CCfg.TextoFundo(Nota()));
        PaginaCelular(T("Configurações"), corpo, () => Fechar(false));
    }

    // o que vale em cada secao, embaixo do nome dela na lista
    string[] ValoresCelular()
    {
        bool mod = _cfg.Modificada;
        int k = _cfg.SobemDescemPara(_cfg.N);
        var jogo = (mod ? T("Atual") : T("Retrô")) + " · " + T("{0} por divisão · {1} rodadas", _cfg.N, 2 * (_cfg.N - 1));
        var regras = string.Join(" · ", new[]
        {
            mod && _cfg.CartoesAmarelos ? T("Amarelos e vermelhos") : T("Só vermelhos"),
            mod && _cfg.Patrocinios ? T("patrocínios ligados") : T("patrocínios desligados"),
            T("sobem e descem {0}", k),
            _cfg.EstrangeirosLiberados ? T("Liberado") : T("Realístico"),
        });
        return new[]
        {
            jogo, regras, T("Visual") + ": " + (_cfg.Moderno ? T("Moderno") : T("Retrô")), Nomes[IndiceIdioma], DadosJogo.Raiz,
        };
    }

    // ---------------------------------------------------------------- uma secao
    void SecaoCelular(int k)
    {
        var corpo = new StackPanel { Spacing = 16 };
        switch (k)
        {
            case 0: JogoCelular(corpo); break;
            case 1: RegrasCelular(corpo); break;
            case 2: AparenciaCelular(corpo); break;
            case 3: IdiomaCelular(corpo); break;
            default: DadosCelular(corpo); break;
        }
        PaginaCelular(T(Secoes[k]), corpo, ListaCelular);
    }

    void JogoCelular(StackPanel p)
    {
        bool mod = _cfg.Modificada;
        p.Children.Add(CCfg.TextoFundo(T("A versão do jogo e o tamanho das divisões.")));
        // a versao refaz a pagina (as equipas por divisao ligam e desligam)
        p.Children.Add(CCfg.Bloco(T("Versão do jogo"), CCfg.Opcoes(new (string, string?)[] { (T("Retrô"), DescOriginal), (T("Atual"), DescModificada) },
            mod ? 1 : 0, i => { if ((i == 1) == _cfg.Modificada) return; _cfg.Modificada = i == 1; SecaoCelular(0); })));
        var ajuda = CCfg.Ajuda(AjudaEquipas());
        var nomes = Array.ConvertAll(Configuracoes.OpcoesEquipas, n => n.ToString());
        var (escolha, _) = CUi.Escolha(nomes, mod ? Math.Max(0, Array.IndexOf(Configuracoes.OpcoesEquipas, _cfg.EquipasPorDivisao)) : 0,
            i => { if (!_cfg.Modificada) return; _cfg.EquipasPorDivisao = Configuracoes.OpcoesEquipas[i]; ajuda.Text = AjudaEquipas(); }, 54);
        escolha.IsEnabled = mod;
        var bloco = new StackPanel { Spacing = 10, Children = { escolha, ajuda } };
        if (!mod) bloco.Children.Add(CUi.Txt(T("(só na versão Atual)"), 13, CUi.Fraco, FontWeight.SemiBold, true));
        p.Children.Add(CCfg.Bloco(T("Equipas por divisão"), new[] { CCfg.Painel(bloco) }, CUi.Etiqueta(T("Atual"))));
    }

    void RegrasCelular(StackPanel p)
    {
        bool mod = _cfg.Modificada;
        p.Children.Add(CCfg.TextoFundo(T("O que vale dentro do jogo.")));
        if (!mod)
        {
            var usar = new CUi.Botao(T("Usar a versão Atual"), false, () => { _cfg.Modificada = true; SecaoCelular(1); }) { HorizontalAlignment = HorizontalAlignment.Stretch };
            p.Children.Add(CUi.Cartao_(T("Na versão Retrô as regras são as de 1998"), new StackPanel
            {
                Spacing = 10,
                Children = { CCfg.Ajuda(T("Cartões amarelos, patrocínios e acréscimos só existem na versão Atual. A escolha fica guardada para quando você usar a Modificada.")), usar },
            }));
        }
        // na Retro aparecem como no jogo original (desligadas) e travadas; o _cfg guarda a escolha
        Control Adicao(string nome, string ajuda, bool valor, Action<bool> mudou) =>
            CUi.Interruptor(nome, ajuda, mod && valor, v => { if (_cfg.Modificada) mudou(v); }, mod);
        p.Children.Add(CCfg.Bloco(T("Adições"), new[]
        {
            Adicao(T("Cartões amarelos"), T("Três amarelos acumulados suspendem por um jogo. Desligados: só vermelhos, como no Retrô."), _cfg.CartoesAmarelos, v => _cfg.CartoesAmarelos = v),
            Adicao(T("Patrocínios"), T("Propostas e contratos de patrocínio master ao longo da temporada."), _cfg.Patrocinios, v => _cfg.Patrocinios = v),
            Adicao(T("Acréscimos"), T("De 1 a 5 minutos no fim do 1º tempo e de 1 a 10 no fim do 2º, com golos, cartões e lesões (45'+2'). Desligados: como no Retrô."), _cfg.Acrescimos, v => _cfg.Acrescimos = v),
            Adicao(T("5 substituições"), T("A regra da FIFA: até 5 substituições por jogo. Desligado: 3, como no Retrô. Na prorrogação da Taça, mais uma."), _cfg.CincoSubstituicoes, v => _cfg.CincoSubstituicoes = v),
            Adicao(T("9 reservas"), T("Até 9 reservas no banco (20 relacionados). Desligado: 5, como no Retrô."), _cfg.NoveReservas, v => _cfg.NoveReservas = v),
        }));
        var ajudaSobem = CUi.Txt(AjudaSobem(), 14, null, FontWeight.Medium, true);
        var (sobem, _) = CUi.Escolha(OpcoesSobem, mod ? _cfg.SobemDescem : 0,
            i => { if (!_cfg.Modificada) return; _cfg.SobemDescem = i; ajudaSobem.Text = AjudaSobem(); }, 54);
        sobem.IsEnabled = mod;
        p.Children.Add(CCfg.Bloco(T("Sobem e descem por divisão"), new[]
        {
            CCfg.Painel(new StackPanel { Spacing = 10, Children = { sobem, ajudaSobem, CCfg.Ajuda(AjudaSobemFixa) } }),
        }, CUi.Etiqueta(T("Atual"))));
        p.Children.Add(CCfg.Bloco(T("Jogadores estrangeiros"), CCfg.Opcoes(new (string, string?)[]
        {
            (T("Realístico"), T("Até 5 por equipa; a Lei Bosman e os países de língua portuguesa não contam.")),
            (T("Liberado"), T("Sem limite: todos os países entram na lista da Lei Bosman.")),
        }, _cfg.EstrangeirosLiberados ? 1 : 0, i => _cfg.EstrangeirosLiberados = i == 1)));
        p.Children.Add(CCfg.Bloco(T("Desempate na classificação"), new[] { CCfg.Painel(CUi.Txt(Desempate(), 14.5, null, FontWeight.Medium, true)) }));
    }

    void AparenciaCelular(StackPanel p)
    {
        p.Children.Add(CCfg.TextoFundo(T("Como as telas são desenhadas. Muda na hora, mesmo com um jogo aberto.")));
        // (o formato 4:3 / 16:9 e do computador: no celular a tela e a do telefone)
        p.Children.Add(CCfg.Bloco(T("Visual"), CCfg.Opcoes(new (string, string?)[]
        {
            (T("Retrô"), T("O aspecto Retrô: janelas do Windows 95 no verde, sem logos.")),
            (T("Moderno"), T("Tema verde e amarelo, cartões e os logos das equipas e das ligas.")),
        }, _cfg.Moderno ? 1 : 0, i => _cfg.VisualTela = i == 1 ? Configuracoes.VisualModerno : Configuracoes.VisualClassico,
            i => new CCfg.PreviaPele(i == 1))));
    }

    void IdiomaCelular(StackPanel p)
    {
        p.Children.Add(CCfg.TextoFundo(T("Os textos do jogo.")));
        // tocar troca na hora: a tela se refaz no idioma escolhido, nesta pagina (TrocarIdioma)
        p.Children.Add(CCfg.Bloco(T("Idioma do jogo"), CCfg.Opcoes(new (string, string?)[]
        {
            (Nomes[0], T("Os textos do jogo Retrô.")),
            (Nomes[1], T("Os textos adaptados para o Brasil.")),
            (Nomes[2], T("Os textos traduzidos para inglês.")),
            (Nomes[3], T("Os textos traduzidos para espanhol.")),
        }, IndiceIdioma, TrocarIdioma)));
    }

    void DadosCelular(StackPanel p)
    {
        // (o gestor de arquivos so existe no computador: no telefone, so o caminho e o que ha nela)
        if (DadosJogo.PodeAbrirPasta) p.Children.Add(CCfg.TextoFundo(T("Equipas, logos, saves e patches numa pasta que você abre no gestor de arquivos.")));
        var dados = new StackPanel { Spacing = 10 };
        dados.Children.Add(new SelectableTextBlock
        {
            Text = DadosJogo.Raiz, FontFamily = CUi.Fonte, FontSize = 14.5 * Acessibilidade.Texto, FontWeight = FontWeight.SemiBold,
            Foreground = CUi.Retro ? W95.Marinho : TemaVerde.Destaque, TextWrapping = TextWrapping.Wrap,
        });
        dados.Children.Add(CCfg.Ajuda(TextoPastaDados));
        if (DadosJogo.PodeAbrirPasta)
            dados.Children.Add(new CUi.Botao(T("Abrir pasta de dados"), false, () => DadosJogo.AbrirPasta()) { HorizontalAlignment = HorizontalAlignment.Stretch });
        p.Children.Add(CCfg.Bloco(T("Pasta de dados"), new[] { CCfg.Painel(dados) }));
    }
}

// As pecas das telas de configuracao do celular (as Configuracoes e as Opcoes), por cima do
// kit (CUi): os blocos com titulo, as linhas de escolha com explicacao, as fichas de liga e
// desliga e o desenho de cada pele.
static class CCfg
{
    static readonly BoxShadows Saltado = BoxShadows.Parse("inset 1 1 0 0 #FFFFFF, inset -1 -1 0 0 #6A6A6A");
    static readonly BoxShadows Afundado = BoxShadows.Parse("inset 1 1 0 0 #6A6A6A, inset -1 -1 0 0 #FFFFFF");

    // a explicacao pequena (dentro de um grupo ou cartao)
    public static TextBlock Ajuda(string s) => CUi.Txt(s, 13, CUi.Fraco, FontWeight.Normal, true);

    // um texto direto sobre o fundo (no Retro, branco no verde)
    public static TextBlock TextoFundo(string s)
    {
        var t = CUi.Txt(s, 13.5, CUi.Retro ? W95.Branco : TemaVerde.TextoFraco, FontWeight.Normal, true);
        t.Margin = new Thickness(4, 0);
        return t;
    }

    // um conteudo com a margem das linhas de um grupo
    public static Control Painel(Control c) => new Border { Padding = new Thickness(12, 12), Child = c };

    // o titulo do bloco sobre o fundo (e uma etiqueta a direita) e as linhas num grupo
    public static Control Bloco(string? titulo, IEnumerable<Control> linhas, Control? direita = null)
    {
        var p = new StackPanel { Spacing = 7 };
        if (titulo != null)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(4, 0) };
            var t = CUi.TituloSecao_(titulo);
            t.TextWrapping = TextWrapping.Wrap;
            g.Children.Add(t);
            if (direita != null) { Grid.SetColumn(direita, 1); direita.Margin = new Thickness(8, 0, 0, 0); g.Children.Add(direita); }
            p.Children.Add(g);
        }
        p.Children.Add(CUi.Grupo(linhas));
        return p;
    }

    // uma linha de escolha (o titulo, a explicacao e o controle embaixo), dentro de um grupo
    public static Control LinhaEscolha(string titulo, string? ajuda, Control controle)
    {
        var p = new StackPanel { Spacing = 6 };
        p.Children.Add(CUi.Txt(titulo, 15, null, FontWeight.Medium, true));
        if (!string.IsNullOrEmpty(ajuda)) p.Children.Add(Ajuda(ajuda));
        controle.Margin = new Thickness(0, 4, 0, 0);
        p.Children.Add(controle);
        return Painel(p);
    }

    // a linha da lista de secoes: o nome, o que vale nela e a seta
    public static Control LinhaSecao(string nome, string valor, Action toque)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var t = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(CUi.Txt(nome, 16.5, null, FontWeight.SemiBold));
        t.Children.Add(CUi.Txt(valor, 13, CUi.Fraco, FontWeight.Normal, true));
        g.Children.Add(t);
        var seta = CUi.Txt("›", 26, CUi.Fraco, FontWeight.Normal);
        seta.Margin = new Thickness(12, -4, 0, 0);
        Grid.SetColumn(seta, 1);
        g.Children.Add(seta);
        var c = CUi.Toque(g, toque, 64);
        c.Padding = new Thickness(14, 10);
        return c;
    }

    // uma escolha unica em linhas com explicacao e a bolinha a direita (a versao, o visual, o
    // idioma...); figura(i): o desenho a esquerda. escolher(i) ao tocar
    public static List<Control> Opcoes(IReadOnlyList<(string Nome, string? Desc)> ops, int atual, Action<int> escolher, Func<int, Control>? figura = null)
    {
        var marcas = new List<(Border Aro, Border Ponto, TextBlock Nome)>();
        void Marcar(int k)
        {
            for (int i = 0; i < marcas.Count; i++)
            {
                bool m = i == k;
                var (aro, ponto, nome) = marcas[i];
                aro.BorderBrush = CUi.Retro ? (m ? W95.Preto : W95.Escura) : (m ? TemaVerde.Destaque : TemaVerde.Borda);
                ponto.Background = m ? (CUi.Retro ? W95.Preto : TemaVerde.Destaque) : Brushes.Transparent;
                nome.FontWeight = m ? FontWeight.Bold : FontWeight.Medium;
            }
        }
        var linhas = new List<Control>();
        for (int i = 0; i < ops.Count; i++)
        {
            int k = i;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            if (figura?.Invoke(i) is { } f)
            {
                f.Margin = new Thickness(0, 0, 12, 0);
                f.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(f);
            }
            var t = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var nome = CUi.Txt(ops[i].Nome, 15.5, null, FontWeight.Medium, true);
            t.Children.Add(nome);
            if (!string.IsNullOrEmpty(ops[i].Desc)) t.Children.Add(Ajuda(ops[i].Desc!));
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            var ponto = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var aro = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), Margin = new Thickness(12, 0, 0, 0),
                Background = CUi.Retro ? W95.Branco : Brushes.Transparent, Child = ponto, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(aro, 2);
            g.Children.Add(aro);
            marcas.Add((aro, ponto, nome));
            var c = CUi.Toque(g, () => { Marcar(k); escolher(k); }, 56);
            c.Padding = new Thickness(12, 10);
            linhas.Add(c);
        }
        Marcar(atual);
        return linhas;
    }

    // uma ficha que liga e desliga (escolha multipla: os lances ao lado do placar), no desenho
    // das pilulas da CUi.Escolha
    public static Control Ficha(string texto, bool ligada, Action<bool> mudou)
    {
        var t = CUi.Txt(texto, 14.5, null, FontWeight.SemiBold);
        var c = new Clicavel
        {
            Child = t, MinHeight = 42, Padding = new Thickness(14, 6), CornerRadius = new CornerRadius(CUi.Retro ? 0 : 12),
            BorderThickness = new Thickness(CUi.Retro ? 1 : 1.5), CorFoco = null,
        };
        void Pintar()
        {
            t.Text = (ligada ? "✓ " : "") + texto;
            if (CUi.Retro)
            {
                c.Background = ligada ? W95.Marinho : W95.Face;
                c.BorderBrush = W95.Preto;
                t.Foreground = ligada ? W95.Branco : W95.Preto;
                c.BoxShadow = ligada ? Afundado : Saltado;
            }
            else
            {
                c.Background = ligada ? TemaVerde.Selecao : TemaVerde.BotaoSecundario;
                c.BorderBrush = ligada ? TemaVerde.Destaque : TemaVerde.Borda;
                t.Foreground = TemaVerde.Texto;
            }
        }
        c.Clique += () => { ligada = !ligada; Pintar(); mudou(ligada); };
        Pintar();
        return c;
    }

    // um botao que ocupa a largura (as acoes de uma pagina)
    public static CUi.Botao BotaoLargo(string texto, bool principal, Action clique) =>
        new(texto, principal, clique) { HorizontalAlignment = HorizontalAlignment.Stretch };

    // o desenho pequeno de cada pele num telefone em pe (Retro: o verde, a janela cinza e a
    // barra azul-marinho; Moderno: o verde escuro, os cartoes e o botao amarelo)
    public sealed class PreviaPele : Control
    {
        readonly bool _moderno;
        public PreviaPele(bool moderno) { _moderno = moderno; Width = 44; Height = 74; IsHitTestVisible = false; }

        static IBrush H(string s) => new SolidColorBrush(Color.Parse(s));

        public override void Render(DrawingContext dc)
        {
            var r = new Rect(0.5, 0.5, Bounds.Width - 1, Bounds.Height - 1);
            if (!_moderno)
            {
                dc.DrawRectangle(W95.Verde, new Pen(W95.Preto, 1), r);
                var j = new Rect(r.X + 4, r.Y + 8, r.Width - 8, r.Height - 22);
                dc.FillRectangle(W95.Face, j);
                dc.FillRectangle(W95.Marinho, new Rect(j.X, j.Y, j.Width, 6));
                dc.FillRectangle(W95.Branco, new Rect(j.X + 3, j.Y + 10, j.Width - 6, j.Height - 14));
                for (int i = 0; i < 5; i++) dc.FillRectangle(W95.Marinho, new Rect(j.X + 6, j.Y + 14 + 7 * i, 14 + 4 * (i % 3), 2));
                dc.DrawRectangle(W95.Face, new Pen(W95.Preto, 1), new Rect(r.X + 8, r.Bottom - 11, r.Width - 16, 8));
            }
            else
            {
                dc.DrawRectangle(H("#0D5A2E"), new Pen(H("#4E7A2E"), 1), r, 7, 7);
                for (int i = 0; i < 2; i++)
                {
                    var c = new Rect(r.X + 4, r.Y + 8 + 22 * i, r.Width - 8, 18);
                    dc.DrawRectangle(H("#094622"), new Pen(H("#4E7A2E"), 1), c, 4, 4);
                    dc.DrawEllipse(H("#F8D734"), null, new Point(c.X + 6, c.Y + 9), 3, 3);
                    dc.FillRectangle(H("#9DC8AB"), new Rect(c.X + 12, c.Y + 8, c.Width - 16, 2));
                }
                dc.DrawRectangle(H("#F8D734"), null, new Rect(r.X + 6, r.Bottom - 13, r.Width - 12, 9), 4.5, 4.5);
            }
        }
    }
}
