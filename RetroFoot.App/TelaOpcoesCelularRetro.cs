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

// As Opcoes do celular no Retro: a TOptionsDlg do computador (TelaOpcoes.cs) adaptada ao toque,
// em pe: a barra de titulo, as abas do TTabbedNotebook (Geral, Jogo, Mercado, Acessibilidade e
// Controle com o add-in 0x5C) e, em cada uma, o rotulo e a caixa de lista embaixo dele (os mesmos
// itens e os mesmos indices), as caixas de marcar dos lances e OK / Cancelar com os glifos.
static class OpcoesCelularRetro
{
    public static void Abrir(Dialogos d, OpcoesDoJogo op, Jogo98? save, Mundo? m, Action<OpcoesDoJogo> ok)
    {
        // as escolhas (os indices das caixas do computador)
        int chic = Math.Clamp((int)op.MostrarChicotadas, 0, 2);
        int sort = Math.Clamp((int)op.VerSorteio, -1, 2);
        int grav = Math.Clamp(op.GravarQuando - 1, -1, 2);
        int nPerfis = OpcoesDoJogo.PerfisDisponiveis();
        // ItemIndex = DS:2D3C - 1; fora da lista, o 1o
        int vi = op.Velocidade - 1;
        int vel = vi >= 0 && vi < nPerfis ? vi : 0;
        int lances = TelaJornada.LancesMostrados;
        bool inter = op.SubstituirNoIntervalo, pen = op.VerPenaltisCPU, som = op.Som, forca = op.ForcaPorFaixas;
        int tamanho = Acessibilidade.Normalizar(op.TamanhoTexto), volume = Math.Clamp(op.Volume / 10, 0, 10) * 10;
        int tamanhoAntes = Acessibilidade.Percentual;
        bool controle = Registo.AddIn(Registo.AddInPaginaControle);
        CR95.Caderno? caderno = null;

        TextBlock Cinza(string s) => CR95.Txt(s, 13, W95.Cinzento);
        CR95.Combo Combo(int indice, Action<int> mudou, params string[] itens)
        {
            var c = new CR95.Combo(itens, indice, 15.5);
            c.Mudou += mudou;
            return c;
        }

        Control Pagina(int k)
        {
            var p = new StackPanel { Spacing = 6 };
            switch (k)
            {
                case 0:
                    p.Children.Add(CR95.Rotulado(T("Mostrar demissões de treinadores"), Combo(chic, i => chic = i, T("Nunca"), T("Dos humanos"), T("Todas"))));
                    p.Children.Add(CR95.Rotulado(T("Ver sorteio da copa"), Combo(sort, i => sort = i, T("Nunca"), T("Quando houver humanos"), T("Sempre"))));
                    p.Children.Add(CR95.Rotulado(T("Salvar o jogo"), Combo(grav, i => grav = i, T("Todas as rodadas"), T("De 3 em 3 rodadas"), T("No início da temporada"))));
                    p.Children.Add(CR95.Rotulado(T("Habilitar som"), Combo(som ? 0 : 1, i => som = i == 0, T("Sim"), T("Não"))));
                    break;
                case 1:
                {
                    p.Children.Add(CR95.Rotulado(T("Substituições no intervalo"), Combo(inter ? 1 : 0, i => inter = i == 1, T("Não"), T("Sim"))));
                    p.Children.Add(CR95.Rotulado(T("Ver as disputas de pênaltis nos jogos sem treinadores humanos"), Combo(pen ? 1 : 0, i => pen = i == 1, T("Não"), T("Sim"))));
                    // o tempo de jogo pelo nome (e quanto leva uma partida, que no computador e a dica)
                    var dica = Cinza(OpcoesDoJogo.DicaPerfil(vel));
                    var perfis = Enumerable.Range(0, nPerfis).Select(OpcoesDoJogo.NomePerfil).ToArray();
                    var cv = Combo(vel, i => { vel = i; dica.Text = OpcoesDoJogo.DicaPerfil(i); }, perfis);
                    p.Children.Add(new StackPanel { Spacing = 6, Margin = new Thickness(0, 4, 0, 8), Children = { CR95.Txt(T("Tempo de jogo"), 15, W95.Preto), cv, dica } });
                    // os lances mostrados ao lado do placar na jornada, em duas colunas
                    p.Children.Add(CR95.Txt(T("Mostrar ao lado do placar"), 15, W95.Preto));
                    var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
                    var tipos = TelaJornada.TiposDeLance;
                    for (int i = 0; i < tipos.Length; i++)
                    {
                        var (bit, nome) = tipos[i];
                        if (i % 2 == 0) g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                        var mk = CR95.Marca(T(nome), (lances & bit) != 0, on => lances = on ? lances | bit : lances & ~bit);
                        Grid.SetRow(mk, i / 2); Grid.SetColumn(mk, i % 2);
                        g.Children.Add(mk);
                    }
                    p.Children.Add(g);
                    break;
                }
                case 2:
                    // (fora do original) a forca na pesquisa de jogadores
                    p.Children.Add(CR95.Rotulado(T("Força na pesquisa de jogadores"), Combo(forca ? 1 : 0, i => forca = i == 1, T("Mínimo e máximo"), T("Faixas do Retrô")),
                        T("Mínimo e máximo: duas caixas, de 1 a 50. Faixas do Retrô: 1-10, 5-15, 10-20, 20-30, 30-40 e 40-50.")));
                    break;
                case 3:
                {
                    // (fora do original) o tamanho do texto vale na hora (a pagina se refaz, com o
                    // exemplo); o Cancelar volta ao de antes
                    var tamanhos = new List<string>();
                    for (int t = Acessibilidade.Minimo; t <= Acessibilidade.Maximo; t += Acessibilidade.Passo) tamanhos.Add(t + "%");
                    p.Children.Add(CR95.Rotulado(T("Tamanho do texto"), Combo((tamanho - Acessibilidade.Minimo) / Acessibilidade.Passo, i =>
                    {
                        tamanho = Acessibilidade.Minimo + i * Acessibilidade.Passo;
                        Acessibilidade.Aplicar(tamanho);
                        caderno?.Refazer();
                    }, tamanhos.ToArray())));
                    var ex = OpcoesCelular.ExemploTexto(save);
                    ex.Margin = new Thickness(0, 0, 0, 8);
                    p.Children.Add(ex);
                    var vols = Enumerable.Range(0, 11).Select(i => i * 10 + "%").ToArray();
                    var cv = Combo(volume / 10, i => volume = i * 10, vols);
                    var testar = new CUi.Botao(T("Testar"), false, () =>
                    {
                        // o som com o volume da tela (sem gravar ainda)
                        bool l = Som.Ligado; int v = Som.Volume;
                        Som.Ligado = true; Som.Volume = volume;
                        Som.Tocar(Som.Efeito.Golo);
                        Som.Ligado = l; Som.Volume = v;
                    }) { Padding = new Thickness(14, 4) };
                    var lv = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,Auto") };
                    lv.Children.Add(cv);
                    Grid.SetColumn(testar, 2);
                    lv.Children.Add(testar);
                    p.Children.Add(CR95.Rotulado(T("Volume dos efeitos"), lv));
                    p.Children.Add(Cinza(T("O tamanho vale para o texto das telas do jogo. Os efeitos tocam com \"Habilitar som\" (Geral) ligado.")));
                    break;
                }
                default:
                {
                    // Controle: as listas (so com um jogo aberto) e o nivel de inflacao
                    CUi.Botao Lista(string nome, Action<Dialogos, Jogo98> abrir) =>
                        new(nome, false, () => { if (save != null) abrir(d, save); }) { HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = save != null };
                    p.Spacing = 10;
                    p.Children.Add(Lista(T("Árbitros"), TelaOpcoes.Arbitros));
                    p.Children.Add(Lista(T("Times endividados..."), TelaOpcoes.Endividados));
                    p.Children.Add(Lista(T("Times com empréstimos..."), TelaOpcoes.Emprestimos));
                    var infl = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 8, 0, 0) };
                    infl.Children.Add(CR95.Txt(T("Nível de inflação"), 15, W95.Preto));
                    var v = CR95.Txt(m == null ? "" : TelaOpcoes.Inflacao(m), 17, W95.Preto, true, false);
                    Grid.SetColumn(v, 1);
                    infl.Children.Add(v);
                    p.Children.Add(infl);
                    break;
                }
            }
            return p;
        }

        var nomes = new List<string> { T("Geral"), T("Jogo"), T("Mercado"), T("Acessibilidade") };
        if (controle) nomes.Add(T("Controle"));
        caderno = new CR95.Caderno(nomes, Pagina);

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
        var rodape = CUi.Botoes(CR95.Botao(T("OK"), "Botoes/BBOK.png", true, Ok, false), CR95.Botao(T("Cancelar"), "Botoes/BBCANCEL.png", false, Cancelar, false));
        f = new CUi.Folha(T("Opções"), new Border { Background = W95.Face, Child = caderno }, rodape, inteira: true, rolar: false, fechar: Cancelar) { Focusable = true };
        // o Esc (no computador, a previa do celular) e o Voltar: Cancelar
        f.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Escape && !e.Handled) { e.Handled = true; Cancelar(); } };
        d.Abrir(f);
    }
}
