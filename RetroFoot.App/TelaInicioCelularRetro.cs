using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.NovoJogo;
using RetroFoot.Motor.Save;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// O comeco do celular no Retro: as janelas do computador (TelaInicio.cs) adaptadas ao toque,
// em pe. Cada janela vira a tela toda: a barra de titulo dela em cima, a area na cor dela
// (o verde da escolha do jogo e dos paises, o amarelo do Dinheiro, o cinza dos Jogadores) com
// os mesmos rotulos, as mesmas bolinhas, listas e caixas, e os TBitBtn com os glifos no rodape.
//   TselectGameDlg      as duas bolinhas (comecar / continuar), o nome do arquivo, a lista dos
//                       jogos salvos (com a bandeira do pais do time) e o texto do escolhido;
//   TselectCountriesDlg a lista com as bandeiras (o escolhido no amarelo) e os totais;
//   TcurrencyDlg        a caixa da moeda;
//   TgetTeamsDlg        Jogador 1 a 6, o nome e o time sorteado nas cores dele.
public sealed partial class TelaInicio
{
    void PaginaRetro(string titulo, string? sub, Control corpo, Control? rodape, IBrush fundo, Action? enter, Action? esc, Control? foco, Control? direita = null)
    {
        _paginaM.Children.Clear();
        _paginaM.Children.Add(CR95.Pagina(CUi.BarraTopo(titulo, sub, null, direita), corpo, rodape, fundo));
        _enterM = enter; _escM = esc; _focoM = foco; _setasM = null;
        Dispatcher.UIThread.Post(FocarModerno, DispatcherPriority.Loaded);
    }

    // ---------------------------------------------------------------- TselectGameDlg
    void EscolherJogoRetro()
    {
        var jogos = ListarJogos();
        bool novo = jogos.Count == 0;
        int sel = jogos.Count > 0 ? 0 : -1;
        Action<int> tocar = null!;
        bool okHab = false;

        // jogo novo: a bolinha, o rotulo amarelo, a caixa do nome e o aviso vermelho no branco
        var rNovo = new CR95.Marcador(T("Quero começar um jogo do início"), false, 17, true);
        var nome = CUi.Caixa("", 8);
        nome.FontFamily = W95.Arial13.Familia;
        nome.FontWeight = FontWeight.Bold;
        var avisoT = CR95.Txt("", 13.5, W95.Vermelho, true);
        avisoT.TextAlignment = TextAlignment.Center;
        var aviso = new Border { Background = W95.Branco, Padding = new Thickness(6, 3), Child = avisoT, IsVisible = false };
        var novoP = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                rNovo,
                CR95.Txt(T("Nome do arquivo (8 letras):"), 14, W95.Amarelo, true),
                nome, aviso,
                CR95.Txt(T("Regras do jogo novo") + ": " + ResumoRegras() + ".", 12.5, W95.Branco),
            },
        };

        // continuar: a bolinha, os jogos salvos (a caixa branca) e o texto do escolhido embaixo
        var rCont = new CR95.Marcador(T("Quero continuar um jogo"), false, 17, true);
        var linhas = new List<(Clicavel C, TextBlock Nome)>();
        var lista = new StackPanel();
        for (int i = 0; i < jogos.Count; i++)
        {
            int k = i;
            var g = jogos[i];
            var grade = new Grid { ColumnDefinitions = new ColumnDefinitions("30,*,Auto") };
            // a bandeira do pais do time (como no topo da tela do time)
            if (g.Equipa is { } eq && CUi.Bandeira(TextosMercado.PaisDe(eq.Pais), 24) is { } b) grade.Children.Add(b);
            var n = CR95.Txt(g.Nome, 16, W95.Azul, true, false);
            n.Margin = new Thickness(4, 0, 0, 0);
            Grid.SetColumn(n, 1);
            grade.Children.Add(n);
            if (g.Ano > 0)
            {
                var et = CUi.Etiqueta(T("Temporada {0}", g.Ano));
                et.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(et, 2);
                grade.Children.Add(et);
            }
            var c = CUi.Toque(grade, () => tocar(k), 44);
            c.Padding = new Thickness(6, 2);
            linhas.Add((c, n));
            lista.Children.Add(c);
        }
        var caixa = CR95.CaixaLista(lista);
        caixa.MinHeight = 92;
        var desc = CR95.Txt("", 14, W95.Amarelo);
        var quando = CR95.Txt("", 12, W95.Amarelo);
        var contP = new StackPanel
        {
            Spacing = 8,
            Children = { rCont, CR95.Txt(T("Jogos salvos:"), 14, W95.Amarelo, true), caixa, desc, quando },
        };

        // os botoes do computador, com os glifos (o Cancelar volta ao hub, que o celular nao tem)
        var ok = CR95.Botao(T("OK"), "Botoes/BBOK.png", true, () => Ok());
        var configurar = CR95.Botao(T("Configurar"), "Botoes/CONFIGURAR.png", false, AbrirConfiguracoes);
        var excluir = CR95.Botao(T("Excluir"), "Botoes/EXCLUIR.png", false, () => { if (!novo && sel >= 0 && sel < jogos.Count) ExcluirJogo(jogos[sel].Nome, jogos[sel].Caminho); });
        var sobre = new CUi.Botao(T("Sobre"), false, () => TelaAcerca.Abrir(new Dialogos(c => SobreporCelular(CUi.FolhaDe(c) != null
            ? new Panel { Background = TemaVerde.Veu, Children = { c } }
            : new Panel { Background = TemaVerde.Veu, Children = { new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = c } } }), _ => Sobrepor(null))))
        { Padding = new Thickness(12, 2), MinHeight = 34 };

        void Atualizar()
        {
            rNovo.Marcado = novo; rCont.Marcado = !novo;
            // 6:25d6: a bolinha escolhida com a letra amarela, a outra azul-marinho
            rNovo.Cor = novo ? W95.Amarelo : W95.Marinho;
            rCont.Cor = novo ? W95.Marinho : W95.Amarelo;
            nome.Foreground = novo ? W95.Azul : W95.Cinzento;
            string? av = null;
            var t = nome.Text ?? "";
            if (novo)
            {
                okHab = NomeValido(t);
                if (t.Length > 0 && !okHab) av = T("Nome do arquivo inválido");
                else if (okHab && Achar(pastaJogos, t + ".e98") != null) { av = T("Já existe um jogo com esse nome"); okHab = false; }
            }
            else okHab = sel >= 0 && sel < jogos.Count;
            avisoT.Text = av ?? "";
            aviso.IsVisible = av != null;
            ok.Habilitado = okHab;
            excluir.Habilitado = !novo && sel >= 0 && sel < jogos.Count;
            // a lista desligada (jogo novo) fica sem o realce
            for (int i = 0; i < linhas.Count; i++)
            {
                bool m = !novo && i == sel;
                linhas[i].C.Background = m ? W95.Destaque : Brushes.Transparent;
                linhas[i].Nome.Foreground = m ? W95.Branco : novo ? W95.Cinzento : W95.Azul;
            }
            var g = !novo && sel >= 0 && sel < jogos.Count ? jogos[sel] : null;
            // os humanos com o time e a divisao, a temporada; a data embaixo, menor
            desc.Text = g == null ? "" : g.Humanos.Count == 0 ? g.Descricao
                : string.Join("\n", g.Humanos.Select(h => h.Treinador + "\n" + h.Equipa + (h.Divisao.Length > 0 ? ", " + h.Divisao : "")))
                  + "\n\n" + T("Temporada {0}", g.Ano);
            desc.IsVisible = desc.Text.Length > 0;
            quando.Text = g == null || g.Humanos.Count == 0 ? "" : g.Quando;
            quando.IsVisible = quando.Text.Length > 0;
        }
        // tocar no escolhido abre (o duplo clique do computador)
        void Tocar(int k)
        {
            if (!novo && sel == k) { Ok(); return; }
            sel = k; novo = false;
            Atualizar();
        }
        void Ok()
        {
            if (!okHab) return;
            if (novo)
            {
                var arq = Path.Combine(pastaJogos, (nome.Text ?? "") + ".e98");
                SobreporCelular(PreparandoCelular(T("A ler as equipas e os países...")));
                NovoJogo(arq);
                return;
            }
            Jogo98 s;
            try { s = Jogo98.Ler(File.ReadAllBytes(jogos[sel].Caminho)); }
            catch (Exception ex) { Mensagem(T("Erro"), ex.Message); return; }
            Comecar(s, jogos[sel].Caminho, false);
        }
        tocar = Tocar;
        rNovo.Clique += () => { novo = true; Atualizar(); nome.Focus(); };
        rCont.Clique += () =>
        {
            novo = false;
            if (sel < 0 && jogos.Count > 0) sel = 0;
            Atualizar();
            Focus();
        };
        // tocar na caixa do nome passa ao jogo novo (como o Clicado do TEdit)
        nome.AddHandler(PointerPressedEvent, (_, _) => { if (!novo) { novo = true; Atualizar(); } }, RoutingStrategies.Tunnel, true);
        nome.GotFocus += (_, _) => { if (!novo) { novo = true; Atualizar(); } };
        nome.TextChanged += (_, _) =>
        {
            var t = nome.Text ?? "";
            var u = t.ToUpperInvariant();
            if (u != t) { int c = nome.CaretIndex; nome.Text = u; nome.CaretIndex = c; return; }
            Atualizar();
        };
        Atualizar();

        var corpo = new StackPanel
        {
            Spacing = 10,
            Children = { CR95.Bevel(novoP), CR95.Bevel(contP) },
        };
        PaginaRetro(TelaAcerca.Nome, null, corpo, CUi.Botoes(ok, configurar, excluir), W95.Verde, Ok, null, null, sobre);
    }

    // ---------------------------------------------------------------- TselectCountriesDlg
    void SelecionarPaisesRetro(Dados d, SelecaoPaises sel, string arquivo)
    {
        var itens = new List<(Clicavel C, TextBlock Nome, TextBlock Vagas, Pais P)>();
        Action atualizar = null!;
        var lista = new StackPanel();
        foreach (var p in sel.Lista)
        {
            var pais = p;
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            Control bandeira = Bandeira(p.Codigo) is Bitmap bm
                ? new Image { Source = bm, Width = 42, Height = 28, Stretch = Stretch.Uniform }
                : new Border { Width = 42, Height = 28 };
            bandeira.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(bandeira);
            var nome = Encoding.Latin1.GetString(p.Nome);
            if (nome.Length > 20) nome = nome[..20];
            var tn = CR95.Txt(nome, 16, W95.Preto, false, false);
            tn.Margin = new Thickness(10, 0, 6, 0);
            Grid.SetColumn(tn, 1);
            g.Children.Add(tn);
            var tv = CR95.Txt(p.Vagas + (p.Vagas > 1 ? T(" times") : T(" time")), 16, W95.Preto, false, false);
            Grid.SetColumn(tv, 2);
            g.Children.Add(tv);
            var c = CUi.Toque(g, () => { sel.Alternar(pais); atualizar(); }, 44);
            c.Padding = new Thickness(2, 2, 10, 2);
            itens.Add((c, tn, tv, pais));
            lista.Children.Add(c);
        }

        // os totais (como a direita da janela do computador) e o aviso, fixos embaixo
        var tExEq = CR95.Txt("", 17, W95.Branco, true, false);
        var tSelEq = CR95.Txt("", 17, W95.Amarelo, true, false);
        var tExPa = CR95.Txt("", 17, W95.Branco, true, false);
        var tSelPa = CR95.Txt("", 17, W95.Branco, true, false);
        var totais = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,18,*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 4 };
        void Por(Control c, int lin, int col) { Grid.SetRow(c, lin); Grid.SetColumn(c, col); totais.Children.Add(c); }
        Por(CR95.Txt(T("Times existentes"), 14, W95.Branco, false, false), 0, 0); Por(tExEq, 0, 1);
        Por(CR95.Txt(T("Times selecionados"), 14, W95.Amarelo, false, false), 1, 0); Por(tSelEq, 1, 1);
        Por(CR95.Txt(T("Países existentes"), 14, W95.Branco, false, false), 0, 3); Por(tExPa, 0, 4);
        Por(CR95.Txt(T("Países selecionados"), 14, W95.Branco, false, false), 1, 3); Por(tSelPa, 1, 4);
        var aviso = CR95.Txt("", 13.5, W95.Amarelo, true);

        var todas = CR95.Botao(T("Todas"), "Botoes/TODAS.png", false, () => { if (sel.TodosEscolhidos) sel.Nenhum(); else sel.Todos(); atualizar(); });
        var ok = CR95.Botao(T("OK"), "Botoes/BBOK.png", true, () => Ok());
        var cancelar = CR95.Botao(T("Cancelar"), "Botoes/BBCANCEL.png", false, EscolherJogo);
        var todasT = (TextBlock)((StackPanel)todas.Child!).Children[1];

        void Atualizar()
        {
            foreach (var (c, n, v, p) in itens)
            {
                // 12:0b04: o escolhido no fundo amarelo e em negrito
                c.Background = p.Selecionado ? W95.Amarelo : Brushes.Transparent;
                n.FontWeight = v.FontWeight = p.Selecionado ? FontWeight.Bold : FontWeight.Normal;
            }
            tExEq.Text = sel.Total.ToString();
            tSelEq.Text = sel.Escolhidas.ToString();
            tExPa.Text = sel.Lista.Count.ToString();
            tSelPa.Text = sel.Escolhidos.ToString();
            bool chega = sel.Escolhidas >= sel.MinimoDeEquipas;
            aviso.Text = sel.Escolhidas > SelecaoPaises.MaximoEquipas
                ? T("O jogo só suporta {0} times. Alguns dos times dos países selecionados não farão parte deste jogo.", SelecaoPaises.MaximoEquipas)
                : chega ? "" : T("Faltam {0} times para o mínimo.", sel.MinimoDeEquipas - sel.Escolhidas);
            aviso.IsVisible = aviso.Text.Length > 0;
            ok.Habilitado = chega;
            // "Todas" vira "Nenhum" quando ja estao todos escolhidos
            todasT.Text = sel.TodosEscolhidos ? T("Nenhum") : T("Todas");
        }
        void Ok()
        {
            if (sel.Escolhidas < sel.MinimoDeEquipas) return;
            try { sel.Confirmar(new Aleatorio(Randomize(DateTime.Now))); }
            catch (Exception ex) { Mensagem(T("Erro"), ex.Message); return; }
            EscolherMoeda(d, arquivo);
        }
        atualizar = Atualizar;
        Atualizar();

        var corpo = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                CR95.Txt(T("Selecione os países com que quer jogar, de modo a totalizar pelo menos {0} times.", sel.MinimoDeEquipas), 14, W95.Branco),
                CR95.Bevel(CR95.CaixaLista(lista), new Thickness(6)),
            },
        };
        var rodape = new StackPanel { Spacing = 8, Children = { totais, aviso, CUi.Botoes(todas, ok, cancelar) } };
        PaginaRetro(T("Seleção de Países"), PassoCelular(0), corpo, rodape, W95.Verde, Ok, EscolherJogo, null);
    }

    // ---------------------------------------------------------------- TcurrencyDlg
    void EscolherMoedaRetro(Dados d, string arquivo)
    {
        var moedas = Tabelas.MoedasDoJogoNovo;
        var combo = new CR95.Combo(moedas.Select(Tabelas.NomeMoeda).ToList(), 0, 19);
        void Ok() => EscolherTreinadores(d, moedas[Math.Clamp(combo.Indice, 0, moedas.Length - 1)], arquivo);
        var corpo = CR95.Bevel(new StackPanel
        {
            Spacing = 16,
            Children =
            {
                CR95.Txt(T("Com que moeda vai querer jogar?"), 16, W95.Preto),
                combo,
                CR95.Txt(T("Os preços, os salários e o dinheiro das equipas aparecem nesta moeda durante todo o jogo."), 13, W95.Preto),
            },
        }, new Thickness(14, 18));
        // Color = $80FFFF: o amarelo-claro da janela
        PaginaRetro(T("Dinheiro"), PassoCelular(1), corpo, CUi.Botoes(CR95.Botao(T("OK"), "Botoes/BBOK.png", true, Ok)), W95.Cor(255, 255, 128), Ok, null, null);
    }

    // ---------------------------------------------------------------- TgetTeamsDlg
    void EscolherTreinadoresRetro(Dados d, int moeda, string arquivo)
    {
        var nomes = new TextBox[6];
        var equipas = new Border[6];
        var grade = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,10,*,8,*"), RowSpacing = 8 };
        grade.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        void Por(Control c, int lin, int col) { Grid.SetRow(c, lin); Grid.SetColumn(c, col); grade.Children.Add(c); }
        Por(CR95.Txt(T("Nome"), 14, W95.Preto, true, false), 0, 2);
        Por(CR95.Txt(T("Time"), 14, W95.Preto, true, false), 0, 4);
        for (int i = 0; i < 6; i++)
        {
            grade.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Por(CR95.Txt(T("Jogador {0}", i + 1), 14, W95.Preto, false, false), i + 1, 0);
            nomes[i] = CUi.Caixa("", 20);
            nomes[i].FontSize = 15;
            nomes[i].FontWeight = FontWeight.Bold;
            // desligada (depois do sorteio) como o TEdit do computador: branca, com o nome cinza
            nomes[i].Resources["TextControlBackgroundDisabled"] = W95.Branco;
            nomes[i].Resources["TextControlForegroundDisabled"] = W95.Cinzento;
            nomes[i].Resources["TextControlBorderBrushDisabled"] = W95.Escura;
            Por(nomes[i], i + 1, 2);
            equipas[i] = new Border { Padding = new Thickness(6, 0), MinHeight = 30, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
            Por(equipas[i], i + 1, 4);
        }
        var botao = new CUi.Botao(T("Sortear times"), true, () => { }) { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 56 };
        var dica = CR95.Txt(T("Até 6 treinadores humanos. Escreva os nomes e sorteie as equipas: cada um começa numa equipa da última divisão."), 13, W95.Preto);
        Jogo98? criado = null;
        void Clique()
        {
            if (criado != null)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
                    File.WriteAllBytes(arquivo, criado.Gravar());
                }
                catch (Exception ex) { Mensagem(T("Erro"), T("Não consegui gravar o jogo: {0}", ex.Message)); }
                Comecar(criado, arquivo, true);
                return;
            }
            var humanos = nomes.Select(n => (n.Text ?? "").Trim()).ToList();
            if (humanos.All(h => h.Length == 0)) return;
            Jogo98 s;
            try { s = CriarJogo(d, moeda, humanos, cfg.RegrasDoJogoNovo); }
            catch (Exception ex) { Mensagem(T("Erro"), ex.Message); return; }
            // o time de cada humano, na ordem dos campos, nas cores dele
            var hs = s.Treinadores.Where(t => t.Humano != 0).ToList();
            int k = 0;
            for (int i = 0; i < 6; i++)
            {
                if (humanos[i].Length == 0 || k >= hs.Count) continue;
                var e = s.Equipas.FirstOrDefault(q => q.Treinador == hs[k].ID);
                k++;
                if (e == null) continue;
                equipas[i].Background = W95.CorTColor(e.CorFundo);
                equipas[i].Child = CR95.Txt(TextoSave(e.NomeCurto), 14, W95.CorTColor(e.CorLetra), true, false);
                equipas[i].IsVisible = true;
            }
            foreach (var n in nomes) n.IsEnabled = false;
            botao.Texto = T("Continuar");
            dica.Text = T("As equipas foram sorteadas. Boa sorte!");
            criado = s;
            Focus();
        }
        botao.Clique += Clique;
        var corpo = new StackPanel { Spacing = 12, Children = { CR95.Bevel(grade, new Thickness(10, 12)), dica } };
        PaginaRetro(T("Jogadores"), PassoCelular(2), corpo, botao, W95.Face, Clique, null, null);
    }
}
