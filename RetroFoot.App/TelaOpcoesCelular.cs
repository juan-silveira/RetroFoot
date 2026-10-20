using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RetroFoot.Motor.Rodada;
using RetroFoot.Motor.Save;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// As Opcoes (TOptionsDlg, TelaOpcoes.cs) no celular: a tela toda, com as abas em pilulas em
// cima (Geral, Jogo, Mercado, Acessibilidade e Controle com o add-in 0x5C), as linhas de
// escolha com a explicacao embaixo e Cancelar / OK fixos no rodape. As mesmas opcoes, os
// mesmos valores e o mesmo OK das caixas do computador; so o desenho muda. O tamanho do texto
// vale na hora (com o exemplo embaixo do deslizador); o Cancelar volta ao de antes.
static class OpcoesCelular
{
    public static void Abrir(Dialogos d, OpcoesDoJogo op, Jogo98? save, Mundo? m, Action<OpcoesDoJogo> ok)
    {
        // as escolhas (os mesmos indices das caixas do computador)
        int chic = Math.Clamp((int)op.MostrarChicotadas, 0, 2);
        int sort = Math.Clamp((int)op.VerSorteio, -1, 2);
        int grav = Math.Clamp(op.GravarQuando - 1, -1, 2);
        int nPerfis = OpcoesDoJogo.PerfisDisponiveis();
        // ItemIndex = DS:2D3C - 1; fora da lista, o 1o
        int vi = op.Velocidade - 1;
        int vel = vi >= 0 && vi < nPerfis ? vi : 0;
        int lances = TelaJornada.LancesMostrados;
        bool inter = op.SubstituirNoIntervalo, pen = op.VerPenaltisCPU, som = op.Som, forca = op.ForcaPorFaixas;
        int tamanho = Acessibilidade.Normalizar(op.TamanhoTexto), volume = Math.Clamp(op.Volume, 0, 100);
        int tamanhoAntes = Acessibilidade.Percentual;

        // Geral
        var geral = new StackPanel { Spacing = 16 };
        geral.Children.Add(CCfg.TextoFundo(T("Como o jogo corre e o que ele mostra. Valem a partir de já.")));
        geral.Children.Add(CUi.Grupo(new[]
        {
            CCfg.LinhaEscolha(T("Mostrar demissões de treinadores"), null, CUi.Escolha(new[] { T("Nunca"), T("Dos humanos"), T("Todas") }, chic, i => chic = i).Painel),
            CCfg.LinhaEscolha(T("Ver sorteio da copa"), null, CUi.Escolha(new[] { T("Nunca"), T("Quando houver humanos"), T("Sempre") }, sort, i => sort = i).Painel),
            CCfg.LinhaEscolha(T("Salvar o jogo"), null, CUi.Escolha(new[] { T("Todas as rodadas"), T("De 3 em 3 rodadas"), T("No início da temporada") }, grav, i => grav = i).Painel),
        }));

        // Jogo: o tempo de jogo pelo nome (e quanto leva uma partida, que no computador e a dica)
        var dica = CCfg.Ajuda(OpcoesDoJogo.DicaPerfil(vel));
        var perfis = Enumerable.Range(0, nPerfis).Select(OpcoesDoJogo.NomePerfil).ToList();
        var (velP, _) = CUi.Escolha(perfis, vel, i => { vel = i; dica.Text = OpcoesDoJogo.DicaPerfil(i); });
        var fichas = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        foreach (var (bit, nome) in TelaJornada.TiposDeLance)
            fichas.Children.Add(CCfg.Ficha(T(nome), (lances & bit) != 0, on => lances = on ? lances | bit : lances & ~bit));
        var jogo = new StackPanel { Spacing = 16 };
        jogo.Children.Add(CUi.Grupo(new[]
        {
            CCfg.LinhaEscolha(T("Tempo de jogo"), T("A velocidade do relógio da jornada: de 1 (lento) a 6 (ultrassônico)."), new StackPanel { Spacing = 8, Children = { velP, dica } }),
            CCfg.LinhaEscolha(T("Mostrar ao lado do placar"), T("Os lances que aparecem ao lado de cada partida na jornada."), fichas),
            CUi.Interruptor(T("Substituições no intervalo"), T("Parar no intervalo para mexer na equipa."), inter, v => inter = v),
            CUi.Interruptor(T("Ver as disputas de pênaltis"), T("Também nos jogos sem treinadores humanos."), pen, v => pen = v),
        }));

        // Mercado (fora do original): a forca na pesquisa de jogadores
        var mercado = new StackPanel { Spacing = 16 };
        var linhasForca = new List<Control> { CCfg.Painel(CCfg.Ajuda(T("Como escolher a força na Lista de Jogadores (Jogador > Procurar)."))) };
        linhasForca.AddRange(CCfg.Opcoes(new (string, string?)[]
        {
            (T("Mínimo e máximo"), T("Duas caixas, de 1 a 50: escolha a força mínima, a máxima ou as duas.")),
            (T("Faixas do Retrô"), T("Uma caixa só, com as faixas do jogo Retrô: 1-10, 5-15, 10-20, 20-30, 30-40 e 40-50.")),
        }, forca ? 1 : 0, i => forca = i == 1));
        mercado.Children.Add(CCfg.Bloco(T("Força na pesquisa de jogadores"), linhasForca));

        // Acessibilidade (fora do original): o tamanho do texto (na hora, com o exemplo) e os efeitos
        var exemplo = new ContentControl();
        void Exemplo() => exemplo.Content = ExemploTexto(save);
        Exemplo();
        var acess = new StackPanel { Spacing = 16 };
        acess.Children.Add(CCfg.Bloco(T("Tamanho do texto"), new[]
        {
            CCfg.Painel(new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    CUi.Deslizador(T("Tamanho do texto"), Acessibilidade.Minimo, Acessibilidade.Maximo, tamanho, v => (int)v + "%",
                        v => { tamanho = (int)v; Acessibilidade.Aplicar(tamanho); Exemplo(); }, Acessibilidade.Passo),
                    CCfg.Ajuda(T("O texto das telas do jogo (equipa, jornada, classificação, listas, mercado), de 80% a 120%. O que não couber ganha a dica com o texto inteiro.")),
                },
            }),
            CCfg.Painel(exemplo),
        }));
        var testar = new CUi.Botao(T("Testar"), false, () =>
        {
            // o som com o volume da tela (sem gravar ainda)
            bool l = Som.Ligado; int v = Som.Volume;
            Som.Ligado = true; Som.Volume = volume;
            Som.Tocar(Som.Efeito.Golo);
            Som.Ligado = l; Som.Volume = v;
        });
        var linhaVolume = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,Auto") };
        var desl = CUi.Deslizador(T("Volume"), 0, 100, volume, v => (int)v + "%", v => volume = (int)v, 10);
        linhaVolume.Children.Add(desl);
        testar.VerticalAlignment = VerticalAlignment.Bottom;
        Grid.SetColumn(testar, 2);
        linhaVolume.Children.Add(testar);
        acess.Children.Add(CCfg.Bloco(T("Efeitos sonoros"), new[]
        {
            CUi.Interruptor(T("Efeitos sonoros"), T("O apito do começo, do intervalo e do fim, o golo e o aviso de expulsão ou lesão nos jogos com treinador humano."), som, v => som = v),
            CCfg.Painel(linhaVolume),
        }));

        var nomes = new List<string> { T("Geral"), T("Jogo"), T("Mercado"), T("Acessibilidade") };
        var paginas = new List<Control> { geral, jogo, mercado, acess };
        // Controle (add-in 0x5C): a inflacao e as listas (so com um jogo aberto)
        if (Registo.AddIn(Registo.AddInPaginaControle))
        {
            var ctrl = new StackPanel { Spacing = 16 };
            Control Lista(string nome, Action<Dialogos, Jogo98> abrir)
            {
                var l = CCfg.LinhaSecao(nome, "", () => { if (save != null) abrir(d, save); });
                l.IsEnabled = save != null;
                return l;
            }
            ctrl.Children.Add(CUi.Grupo(new[]
            {
                CCfg.Painel(CUi.Linha_(T("Nível de inflação"), m == null ? "—" : TelaOpcoes.Inflacao(m))),
                Lista(T("Árbitros"), TelaOpcoes.Arbitros),
                Lista(T("Times endividados..."), TelaOpcoes.Endividados),
                Lista(T("Times com empréstimos..."), TelaOpcoes.Emprestimos),
            }));
            nomes.Add(T("Controle"));
            paginas.Add(ctrl);
        }

        // as abas em cima e a pagina que rola embaixo
        var miolo = new ContentControl();
        var rolar = new ScrollViewer { Content = new Border { Padding = new Thickness(12, 4, 12, 16), Child = miolo }, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        void Aba(int i) { miolo.Content = paginas[i]; rolar.Offset = default; }
        var (abas, _) = CUi.Abas(nomes, Aba);
        Aba(0);
        var corpo = new DockPanel();
        DockPanel.SetDock(abas, Dock.Top);
        corpo.Children.Add(abas);
        corpo.Children.Add(rolar);

        CUi.Folha? f = null;
        void Cancelar() { Acessibilidade.Aplicar(tamanhoAntes); d.Fechar(f!); }
        void Ok()
        {
            op.Velocidade = (short)(vel + 1);
            TelaJornada.LancesMostrados = lances;
            op.VerSorteio = (short)sort;
            op.GravarQuando = (short)(grav + 1);
            op.SubstituirNoIntervalo = inter;
            op.MostrarChicotadas = (short)chic;
            op.VerPenaltisCPU = pen;
            op.Som = som;
            op.ForcaPorFaixas = forca;
            op.TamanhoTexto = tamanho;
            op.Volume = volume;
            op.AplicarInterface();
            d.Fechar(f!);
            ok(op);
        }
        var rodape = CUi.Botoes(new CUi.Botao(T("Cancelar"), false, Cancelar), new CUi.Botao(T("OK"), true, Ok));
        f = new CUi.Folha(T("Opções"), corpo, rodape, inteira: true, rolar: false, fechar: Cancelar) { Focusable = true };
        // o Esc (no computador, a previa do celular) e o Voltar: Cancelar
        f.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape && !e.Handled) { e.Handled = true; Cancelar(); } };
        d.Abrir(f);
    }

    // o exemplo do tamanho do texto: o nome de uma equipa e os primeiros jogadores dela (sem
    // jogo, o nome e o lema do jogo), feitos de novo a cada passo do deslizador
    static Control ExemploTexto(Jogo98? save)
    {
        var p = new StackPanel { Spacing = 4 };
        var e = save?.Equipas.FirstOrDefault(x => x.Jogadores.Count >= 3);
        if (e != null)
        {
            p.Children.Add(CUi.NomeEquipa(e, P98.NomeEquipa(e), 15, true));
            foreach (var j in e.Jogadores.Take(3)) p.Children.Add(CUi.LinhaJogador(j));
        }
        else
        {
            p.Children.Add(CUi.Txt(TelaAcerca.Nome, 17, null, FontWeight.Bold));
            p.Children.Add(CUi.Txt(TelaAcerca.Slogan, 14, CUi.Fraco, FontWeight.Normal, true));
        }
        return new Border
        {
            Padding = new Thickness(10, 8), BorderThickness = new Thickness(1), BorderBrush = CUi.Linha,
            CornerRadius = new CornerRadius(CUi.Retro ? 0 : 10), Child = p,
        };
    }
}
