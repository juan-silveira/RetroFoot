using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RetroFoot.Motor.Base;
using RetroFoot.Motor.NovoJogo;
using RetroFoot.Motor.Save;
using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// O comeco no celular (Celular.cs), em pe e ao toque, nas duas peles do kit (CelularUi.cs):
// o mesmo fluxo do computador (TelaInicio.cs) com as paginas uma embaixo da outra:
//   inicio      os jogos gravados (tocar escolhe; Continuar abre) e o jogo novo;
//   jogo novo   Paises (a lista com as bandeiras) -> Moeda -> Treinadores -> a Sessao.
public sealed partial class TelaInicio
{
    void MontarCelular()
    {
        Background = CUi.Fundo;
        _sobreM.IsVisible = false;
        Content = new Panel { Children = { _paginaM, _sobreM } };
        AddHandler(KeyDownEvent, TeclaModerna, Avalonia.Interactivity.RoutingStrategies.Bubble);
    }

    void PaginaCelular(Control cabecalho, Control corpo, Control? rodape, Action? enter, Action? esc, Control? foco)
    {
        _paginaM.Children.Clear();
        _paginaM.Children.Add(CUi.Pagina(cabecalho, corpo, rodape));
        _enterM = enter; _escM = esc; _focoM = foco; _setasM = null;
        Dispatcher.UIThread.Post(FocarModerno, DispatcherPriority.Loaded);
    }

    void MensagemCelular(string titulo, string texto, Action? depois)
    {
        void Fim() { Sobrepor(null); depois?.Invoke(); }
        SobreporCelular(CUi.Caixa_(titulo, CUi.Txt(texto, 15, null, FontWeight.Normal, true), new CUi.Botao(T("OK"), true, Fim)));
    }

    void ConfirmarCelular(string titulo, string texto, Action sim)
    {
        void Sim() { Sobrepor(null); sim(); }
        SobreporCelular(CUi.Caixa_(titulo, CUi.Txt(texto, 15, null, FontWeight.Normal, true),
            new CUi.Botao(T("Não"), false, () => Sobrepor(null)), new CUi.Botao(T("Sim"), true, Sim)));
    }

    // a caixa do kit ja traz o veu
    void SobreporCelular(Control c)
    {
        _sobreM.Children.Clear();
        _sobreM.Children.Add(c);
        _sobreM.IsVisible = true;
    }

    static Control PreparandoCelular(string texto) => CUi.Caixa_(T("Jogo novo"), new StackPanel
    {
        Orientation = Orientation.Horizontal, Spacing = 12,
        Children = { new ProgressBar { IsIndeterminate = true, Width = 70, VerticalAlignment = VerticalAlignment.Center }, CUi.Txt(texto, 14, null, FontWeight.Normal, true) },
    });

    // o passo do jogo novo, no subtitulo
    static string PassoCelular(int k) => T("Jogo novo") + " · " + (k + 1) + "/3 · " + new[] { T("Países"), T("Moeda"), T("Treinadores") }[k];

    // ---------------------------------------------------------------- inicio
    void EscolherJogoCelular()
    {
        var jogos = ListarJogos();
        int sel = jogos.Count > 0 ? 0 : -1;
        var itens = new List<CUi.Item>();
        var continuar = new CUi.Botao(T("Continuar"), true, () => { });
        var excluir = new CUi.Botao(T("Excluir"), false, () => { if (sel >= 0 && sel < jogos.Count) ExcluirJogo(jogos[sel].Nome, jogos[sel].Caminho); });
        var lista = new StackPanel { Spacing = 6 };
        var nome = CUi.Caixa("", 8, T("NOME"));
        var aviso = CUi.Txt("", 13, CUi.Erro, FontWeight.Normal, true);
        var comecar = new CUi.Botao(T("Começar jogo novo"), true, () => { });
        for (int i = 0; i < jogos.Count; i++)
        {
            int k = i;
            var g = jogos[i];
            var grade = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*") };
            Control escudo = g.Equipa is { } eq ? new ConfigEstilo.Escudo(eq) { Width = 34, Height = 34 }
                : CUi.Txt("E98", 12, CUi.Destaque, FontWeight.Bold);
            escudo.VerticalAlignment = VerticalAlignment.Center;
            grade.Children.Add(escudo);
            var meio = new StackPanel { Spacing = 2 };
            var cab = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            cab.Children.Add(CUi.Txt(g.Nome, 16, null, FontWeight.Bold));
            if (g.Ano > 0) { var et = CUi.Etiqueta(T("Temporada {0}", g.Ano)); cab.Children.Add(et); }
            meio.Children.Add(cab);
            var desc = g.Descricao.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var h in g.Humanos.Count > 0 ? g.Humanos.Select(x => x.ToString()) : desc.Take(1))
                meio.Children.Add(CUi.Txt(h, 13, CUi.Fraco));
            if (g.Quando.Length > 0) meio.Children.Add(CUi.Txt(g.Quando, 11.5, CUi.Fraco));
            Grid.SetColumn(meio, 2);
            grade.Children.Add(meio);
            var it = new CUi.Item(grade, () => { if (sel == k) Abrir(); else Escolher(k); });
            itens.Add(it);
            lista.Children.Add(it);
        }
        Control jogosGravados = jogos.Count > 0
            ? new StackPanel { Spacing = 12, Children = { lista, CUi.Botoes(excluir, continuar) } }
            : CUi.Txt(T("Nenhum jogo gravado. Comece um jogo novo abaixo."), 14, CUi.Fraco, FontWeight.Normal, true);

        comecar.HorizontalAlignment = HorizontalAlignment.Stretch;
        var mudar = new CUi.Botao(T("Mudar nas Configurações"), false, AbrirConfiguracoes);
        mudar.HorizontalAlignment = HorizontalAlignment.Stretch;
        var novo = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                CUi.Txt(T("O nome do arquivo do jogo: até 8 letras ou algarismos."), 13.5, CUi.Fraco, FontWeight.Normal, true),
                nome, aviso, comecar,
                CUi.Txt(T("Regras do jogo novo") + ": " + ResumoRegras() + ".", 13, CUi.Fraco, FontWeight.Normal, true),
                mudar,
            },
        };
        var corpo = new StackPanel
        {
            Spacing = 14,
            Children = { CUi.Cartao_(T("Continuar um jogo"), jogosGravados, jogos.Count > 0 ? CUi.Etiqueta(jogos.Count == 1 ? T("1 jogo") : T("{0} jogos", jogos.Count)) : null), CUi.Cartao_(T("Jogo novo"), novo) },
        };
        var acoes = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10,
            Children = { new CUi.Botao(T("Configurações"), false, AbrirConfiguracoes), new CUi.Botao(T("Sobre"), false, () => TelaAcerca.Abrir(new Dialogos(c => SobreporCelular(new Panel { Background = TemaVerde.Veu, Children = { new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = c } } }), _ => Sobrepor(null)))) },
        };

        bool valido = false;
        void Atualizar()
        {
            var t = nome.Text ?? "";
            string? av = null;
            valido = NomeValido(t);
            if (t.Length > 0 && !valido) av = T("Nome do arquivo inválido: só letras (sem acento), algarismos, _ e -.");
            else if (valido && Achar(pastaJogos, t + ".e98") != null) { av = T("Já existe um jogo com esse nome."); valido = false; }
            aviso.Text = av ?? "";
            aviso.IsVisible = av != null;
            comecar.Habilitado = valido;
            continuar.Habilitado = excluir.Habilitado = sel >= 0;
        }
        void Escolher(int k)
        {
            sel = k;
            for (int i = 0; i < itens.Count; i++) itens[i].Marcar(i == sel);
            Atualizar();
        }
        void Novo()
        {
            if (!valido) return;
            var arq = Path.Combine(pastaJogos, (nome.Text ?? "") + ".e98");
            SobreporCelular(PreparandoCelular(T("A ler as equipas e os países...")));
            NovoJogo(arq);
        }
        void Abrir()
        {
            if (sel < 0 || sel >= jogos.Count) return;
            Jogo98 s;
            try { s = Jogo98.Ler(File.ReadAllBytes(jogos[sel].Caminho)); }
            catch (Exception ex) { Mensagem(T("Erro"), ex.Message); return; }
            Comecar(s, jogos[sel].Caminho, false);
        }
        nome.TextChanged += (_, _) =>
        {
            var t = nome.Text ?? "";
            var u = t.ToUpperInvariant();
            if (u != t) { int c = nome.CaretIndex; nome.Text = u; nome.CaretIndex = c; return; }
            Atualizar();
        };
        comecar.Clique += Novo;
        continuar.Clique += Abrir;
        Escolher(sel);
        PaginaCelular(CUi.Cabecalho(TelaAcerca.Nome, TelaAcerca.Slogan, acoes), corpo, null,
            () => { if (nome.IsFocused) Novo(); else Abrir(); }, null, null);
    }

    // ---------------------------------------------------------------- paises
    void SelecionarPaisesCelular(Dados d, SelecaoPaises sel, string arquivo)
    {
        Sobrepor(null);
        var itens = new List<(CUi.Item I, TextBlock Check, Pais P)>();
        var lista = new StackPanel { Spacing = 6 };
        var resumo = CUi.Txt("", 14, null, FontWeight.SemiBold, true);
        var aviso = CUi.Txt("", 13, CUi.Erro, FontWeight.Normal, true);
        var ok = new CUi.Botao(T("Continuar"), true, () => { });
        var todas = new CUi.Botao(T("Todos"), false, () => { });
        foreach (var p in sel.Lista)
        {
            var pais = p;
            Control bandeira = Bandeira(p.Codigo) is Bitmap bm
                ? new Image { Source = bm, Width = 36, Height = 24, Stretch = Stretch.Uniform }
                : new Border { Width = 36, Height = 24, Background = CUi.Linha };
            bandeira.VerticalAlignment = VerticalAlignment.Center;
            var check = CUi.Txt("✓", 18, CUi.Retro ? null : TemaVerde.Destaque, FontWeight.Bold);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*,Auto") };
            g.Children.Add(bandeira);
            var txt = new StackPanel();
            txt.Children.Add(CUi.Txt(Encoding.Latin1.GetString(p.Nome), 15, null, FontWeight.SemiBold));
            txt.Children.Add(CUi.Txt(p.Vagas == 1 ? T("1 time") : T("{0} times", p.Vagas), 12.5, CUi.Fraco));
            Grid.SetColumn(txt, 2);
            g.Children.Add(txt);
            Grid.SetColumn(check, 3);
            g.Children.Add(check);
            var it = new CUi.Item(g, () => { sel.Alternar(pais); Atualizar(); });
            itens.Add((it, check, pais));
            lista.Children.Add(it);
        }
        void Atualizar()
        {
            foreach (var (i, c, p) in itens) { i.Marcar(p.Selecionado); c.IsVisible = p.Selecionado; }
            bool chega = sel.Escolhidas >= sel.MinimoDeEquipas;
            resumo.Text = T("{0} times em {1} países (mínimo {2})", sel.Escolhidas, sel.Escolhidos, sel.MinimoDeEquipas);
            resumo.Foreground = chega ? CUi.Bom : CUi.Texto;
            aviso.Text = sel.Escolhidas > SelecaoPaises.MaximoEquipas
                ? T("O jogo só suporta {0} times. Alguns dos times dos países selecionados não farão parte deste jogo.", SelecaoPaises.MaximoEquipas)
                : chega ? "" : T("Faltam {0} times para o mínimo.", sel.MinimoDeEquipas - sel.Escolhidas);
            aviso.IsVisible = aviso.Text.Length > 0;
            ok.Habilitado = chega;
            todas.Texto = sel.TodosEscolhidos ? T("Nenhum") : T("Todos");
        }
        void Ok()
        {
            if (sel.Escolhidas < sel.MinimoDeEquipas) return;
            try { sel.Confirmar(new Aleatorio(Randomize(DateTime.Now))); }
            catch (Exception ex) { Mensagem(T("Erro"), ex.Message); return; }
            EscolherMoeda(d, arquivo);
        }
        ok.Clique += Ok;
        todas.Clique += () => { if (sel.TodosEscolhidos) sel.Nenhum(); else sel.Todos(); Atualizar(); };
        Atualizar();
        var corpo = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                CUi.Txt(T("Selecione os países com que quer jogar, de modo a totalizar pelo menos {0} times.", sel.MinimoDeEquipas), 13.5, CUi.Retro ? W95.Branco : CUi.Fraco, FontWeight.Normal, true),
                lista,
            },
        };
        var rodape = new StackPanel { Spacing = 8, Children = { resumo, aviso, CUi.Botoes(new CUi.Botao(T("Voltar"), false, EscolherJogo), todas, ok) } };
        PaginaCelular(CUi.Cabecalho(Path.GetFileNameWithoutExtension(arquivo), PassoCelular(0)), corpo, rodape, Ok, EscolherJogo, null);
    }

    // ---------------------------------------------------------------- moeda
    void EscolherMoedaCelular(Dados d, string arquivo)
    {
        var moedas = Tabelas.MoedasDoJogoNovo;
        int sel = 0;
        var itens = new List<CUi.Item>();
        var lista = new StackPanel { Spacing = 6 };
        for (int i = 0; i < moedas.Length; i++)
        {
            int k = i;
            var it = new CUi.Item(CUi.Txt(Tabelas.NomeMoeda(moedas[i]), 16, null, FontWeight.SemiBold), () => { sel = k; for (int j = 0; j < itens.Count; j++) itens[j].Marcar(j == sel); });
            itens.Add(it);
            lista.Children.Add(it);
        }
        itens[0].Marcar(true);
        void Ok() => EscolherTreinadores(d, moedas[Math.Clamp(sel, 0, moedas.Length - 1)], arquivo);
        var corpo = CUi.Cartao_(T("Com que moeda vai querer jogar?"), new StackPanel
        {
            Spacing = 10,
            Children = { lista, CUi.Txt(T("Os preços, os salários e o dinheiro das equipas aparecem nesta moeda durante todo o jogo."), 13, CUi.Fraco, FontWeight.Normal, true) },
        });
        var ok = new CUi.Botao(T("Continuar"), true, Ok);
        PaginaCelular(CUi.Cabecalho(Path.GetFileNameWithoutExtension(arquivo), PassoCelular(1)), corpo, CUi.Botoes(ok), Ok, null, null);
    }

    // ---------------------------------------------------------------- treinadores
    void EscolherTreinadoresCelular(Dados d, int moeda, string arquivo)
    {
        var nomes = new TextBox[6];
        var equipas = new ContentControl[6];
        var linhas = new StackPanel { Spacing = 10 };
        for (int i = 0; i < 6; i++)
        {
            nomes[i] = CUi.Caixa("", 20, T("Treinador {0}", i + 1));
            equipas[i] = new ContentControl { IsVisible = false };
            linhas.Children.Add(new StackPanel { Spacing = 6, Children = { nomes[i], equipas[i] } });
        }
        var botao = new CUi.Botao(T("Sortear equipas"), true, () => { });
        var dica = CUi.Txt(T("Até 6 treinadores humanos. Escreva os nomes e sorteie as equipas: cada um começa numa equipa da última divisão."), 13.5, CUi.Fraco, FontWeight.Normal, true);
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
            var hs = s.Treinadores.Where(t => t.Humano != 0).ToList();
            int k = 0;
            for (int i = 0; i < 6; i++)
            {
                if (humanos[i].Length == 0 || k >= hs.Count) { nomes[i].IsVisible = humanos[i].Length > 0; continue; }
                var e = s.Equipas.FirstOrDefault(q => q.Treinador == hs[k].ID);
                k++;
                if (e == null) continue;
                var fundo = e.CorFundo.Length >= 3 ? Color.FromRgb(e.CorFundo[0], e.CorFundo[1], e.CorFundo[2]) : Colors.White;
                var letra = e.CorLetra.Length >= 3 ? Color.FromRgb(e.CorLetra[0], e.CorLetra[1], e.CorLetra[2]) : Colors.Black;
                var nomeEq = CUi.Txt(TextoSave(e.NomeCurto), 15, new SolidColorBrush(letra), FontWeight.Bold);
                nomeEq.Tag = "fixo";
                equipas[i].Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 10,
                    Children =
                    {
                        new ConfigEstilo.Escudo(e) { Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Center },
                        new Border { Background = new SolidColorBrush(fundo), CornerRadius = new CornerRadius(CUi.Retro ? 0 : 6), Padding = new Thickness(8, 3), Child = nomeEq },
                    },
                };
                equipas[i].IsVisible = true;
            }
            foreach (var n in nomes) n.IsEnabled = false;
            botao.Texto = T("Começar");
            dica.Text = T("As equipas foram sorteadas. Boa sorte!");
            criado = s;
        }
        botao.Clique += Clique;
        var corpo = CUi.Cartao_(T("Treinadores"), new StackPanel { Spacing = 12, Children = { dica, linhas } });
        PaginaCelular(CUi.Cabecalho(Path.GetFileNameWithoutExtension(arquivo), PassoCelular(2)), corpo, CUi.Botoes(botao), Clique, null, nomes[0]);
    }
}
