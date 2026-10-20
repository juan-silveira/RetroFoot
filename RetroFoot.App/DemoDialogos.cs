using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using RetroFoot.Motor.Ciclo;
using RetroFoot.Motor.Save;
using RetroFoot.Motor.Temporada;
using Mem = RetroFoot.Motor.Partida.Memoria;

using static RetroFoot.App.Idioma;

namespace RetroFoot.App;

// RETROFOOT_TELA=dialogos: os dialogos dos menus Campeonato e Tecnico, o fim de temporada, o
// convite, as opcoes e o Sobre, com um save de teste (RETROFOOT_SAVE ou o j3 do oraculo).
// RETROFOOT_DIALOGO=nome abre um logo ao comecar (calendario, artilheiros, sempre, vencedores,
// transferencias, historia, gestao, ranking, perfil, fim, convite, opcoes, sobre,
// extras).
public static class DemoDialogos
{
    public static Control Demo()
    {
        var raiz = new Border { Background = W95.Verde };
        var s = Sessao(raiz);
        if (s == null) return new TextBlock { Text = "Demo: não achei o save (RETROFOOT_SAVE)", Foreground = Brushes.White };
        // a tela de baixo (um quadro de 800x600) e a camada dos dialogos por cima
        var quadro = new Canvas { Width = 800, Height = 600, Background = W95.Verde };
        // no celular, as janelas abrem na camada da Sessao (as folhas do celular)
        var dlg = Celular.Ativo ? (Dialogos)s : new Dialogos(c =>
        {
            Canvas.SetLeft(c, Math.Floor((800 - c.Width) / 2));
            Canvas.SetTop(c, Math.Floor((600 - c.Height) / 2));
            quadro.Children.Add(c);
        }, c => quadro.Children.Remove(c));
        var humano = s.S.Equipas.FirstOrDefault(e => Mem.Humano(s.TreinadorDa(e))) ?? s.S.Equipas[0];
        // (as janelas da partida precisam do time escalado: titulares e reservas)
        if (!humano.Jogadores.Any(j => Mem.Escalacao(j) == Mem.EscTitular)) RetroFoot.Motor.Rodada.Escalacao.Automatico(s.J.R, humano);
        var acoes = new (string Nome, Action Abrir)[]
        {
            ("calendario", () => TelaListas.Calendario(dlg, s, humano)),
            ("artilheiros", () => TelaListas.Marcadores(dlg, s, false)),
            ("sempre", () => TelaListas.Marcadores(dlg, s, true)),
            ("vencedores", () => TelaListas.UltimosVencedores(dlg, s)),
            ("transferencias", () => TelaListas.UltimasTransferencias(dlg, s)),
            ("historia", () => TelaListas.HistoriaTreinador(dlg, s, s.TreinadorDa(humano))),
            ("gestao", () => TelaTreinadores.Gestao(dlg, s)),
            ("ranking", () => TelaListas.RankingTreinadores(dlg, s)),
            ("perfil", () => TelaTreinadores.Perfil(dlg, s.TreinadorDa(humano))),
            ("fim", () =>
            {
                var r = new Resultado { Campeao = s.S.Divisoes[0].Equipas[0], VencedorTaca = s.S.OutrasEquipas.Count > 0 ? s.S.OutrasEquipas[0] : s.S.Divisoes[0].Equipas[1] };
                r.Subiram[0].Add(s.S.Divisoes[1].Equipas[0]);
                r.Desceram[0].Add(s.S.Divisoes[0].Equipas[^1]);
                Control? c = null;
                c = TelaFimDeTemporada.Janela(s, r, () =>
                {
                    dlg.Fechar(c!);
                    Control? c2 = null;
                    c2 = TelaFimDeTemporada.SubidasEDescidas(s, r, () => dlg.Fechar(c2!));
                    Mostrar(c2);
                });
                Mostrar(c);
            }),
            ("convite", () =>
            {
                Control? c = null;
                var t = s.TreinadorDa(humano)!;
                var outra = s.S.Equipas.First(e => e != humano);
                c = TelaTreinadores.JanelaConvite(t, outra, v => dlg.Fechar(c!));
                Mostrar(c);
            }),
            ("opcoes", () => TelaOpcoes.Abrir(dlg, OpcoesDoJogo.De(Opcoes.Padrao), s.S, s.J.M, _ => { })),
            ("sobre", () => TelaAcerca.Abrir(dlg)),
            ("perguntar", () => Dlg.Perguntar(dlg, T("Gestão de Treinadores"), T("{0} deixa de jogar?", "JOAO SILVA"), () => Console.WriteLine("RETROFOOT-DEMO sim"), () => Console.WriteLine("RETROFOOT-DEMO nao"))),
            ("texto", () => Dlg.PedirTexto(dlg, T("Novo treinador"), T("Nome do novo treinador:"), "", 20, t => Console.WriteLine("RETROFOOT-DEMO texto " + t))),
            ("patrocinio", () =>
            {
                Control? c = null;
                var p = new RetroFoot.Motor.Patrocinio.Proposta { Valor = 850000, Quantidade = 2, Tipo = RetroFoot.Motor.Patrocinio.Proposta.Master, Multa = 300000 };
                p.NomeBruto[0] = 9; System.Text.Encoding.Latin1.GetBytes("SUPERBOCK").CopyTo(p.NomeBruto, 1);
                c = new TelaPatrocinio(p, s.J.Op.Marcas, RetroFoot.Motor.Mercado.Moeda.DoSave(s.S), v => { Console.WriteLine("RETROFOOT-DEMO patrocinio " + v); dlg.Fechar(c!); });
                Mostrar(c);
            }),
            ("extras", () => TelaAcerca.Extras(dlg)),
            ("lesao", () =>
            {
                Control? c = null;
                var saiu = humano.Jogadores.First(j => Mem.Escalacao(j) == Mem.EscTitular && j.Posicao != 0);
                var t = new TelaSubstituicaoForcada(humano, saiu);
                t.Fechou += () => dlg.Fechar(t);
                c = t;
                Mostrar(c);
            }),
        };
        // as janelas do celular de cada grupo (DemoCelular*.cs)
        var extras = new System.Collections.Generic.List<(string Nome, Action Abrir)>();
        DemoCelular.Entradas(extras, dlg, s, humano, c => Mostrar(c));
        acoes = acoes.Concat(extras).ToArray();
        void Mostrar(Control c)
        {
            if (Celular.Ativo) { s.AbrirDialogo(c); return; }
            Canvas.SetLeft(c, Math.Floor((800 - c.Width) / 2));
            Canvas.SetTop(c, Math.Floor((600 - c.Height) / 2));
            quadro.Children.Add(c);
        }
        if (Celular.Ativo)
        {
            // o celular: os botoes numa pagina do kit, por baixo das janelas da Sessao
            var w = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
            foreach (var a in acoes) w.Children.Add(new CUi.Botao(a.Nome, false, a.Abrir));
            s.Mostrar(CUi.Pagina(CUi.BarraTopo("Demo", "as janelas no celular"), w));
            var ab = Environment.GetEnvironmentVariable("RETROFOOT_DIALOGO");
            if (!string.IsNullOrEmpty(ab))
                Dispatcher.UIThread.Post(() => acoes.FirstOrDefault(a => a.Nome == ab).Abrir?.Invoke(), DispatcherPriority.Loaded);
            return raiz;
        }
        // os botoes do demo, em duas colunas a esquerda
        for (int i = 0; i < acoes.Length; i++)
        {
            var a = acoes[i];
            var b = new Botao { Texto = a.Nome, Fonte = W95.Arial11b, Width = 110, Height = 24 };
            b.Clique += a.Abrir;
            Canvas.SetLeft(b, 4 + i / 15 * 114);
            Canvas.SetTop(b, 4 + i % 15 * 28);
            quadro.Children.Add(b);
        }
        raiz.Child = new Viewbox { Stretch = Stretch.Uniform, Child = quadro };
        var abrir = Environment.GetEnvironmentVariable("RETROFOOT_DIALOGO");
        if (!string.IsNullOrEmpty(abrir))
            Dispatcher.UIThread.Post(() => acoes.FirstOrDefault(a => a.Nome == abrir).Abrir?.Invoke(), DispatcherPriority.Loaded);
        return raiz;
    }

    // uma Sessao com o save de teste (so para ler os dados: o motor nao comeca)
    internal static Sessao? Sessao(Border raiz)
    {
        var arq = AcharSave();
        if (arq == null) return null;
        var s = Jogo98.Ler(File.ReadAllBytes(arq));
        return new Sessao(s, c => raiz.Child = c, Opcoes.Padrao, Path.Combine(Path.GetTempPath(), "retrofoot-demo-dialogos.e98"));
    }

    static string? AcharSave()
    {
        var env = Environment.GetEnvironmentVariable("RETROFOOT_SAVE");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        foreach (var ini in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            for (var d = new DirectoryInfo(ini); d != null; d = d.Parent)
            {
                var f = Path.Combine(d.FullName, "RetroFoot.Motor.Testes", "Oraculo", "j3-entrada.e98");
                if (File.Exists(f)) return f;
            }
        return null;
    }
}
