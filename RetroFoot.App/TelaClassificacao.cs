using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media;
using RetroFoot.Motor.Save;
using TRegras = RetroFoot.Motor.Regras.Regras;

namespace RetroFoot.App;

// A Classificacao: a do visual escolhido (Visual.cs). Original = o TclassificationDlg
// (TelaClassificacaoOriginal, 800x600 ampliado sobre o verde); Moderno = a tabela escura
// (TelaClassificacaoModerna). Os dois fecham com Fechou e abrem o plantel com AbrirEquipa.
public sealed class TelaClassificacao : UserControl
{
    public event Action? Fechou;

    // quem abre o plantel de uma equipa clicada (a Sessao liga): equipa, depois da jornada
    // (so ver) ou do menu (a equipa humana pode comprar), ao fechar
    public static Action<Equipa, bool, Action>? AbrirEquipa;

    readonly Control _tela;

    // depois de cada jornada (a Sessao, com o motor parado): titulo com a jornada e relogio
    public TelaClassificacao(Sessao s) : this(s.S, s.J.M.Jornada, true, s.J.M.Regras) { }

    // rodada 0 = so "Classificação" (sem numero); relogio: fecha sozinha em 10 s
    public TelaClassificacao(Sessao s, int rodada, bool relogio) : this(s.S, rodada, relogio, s.J.M.Regras) { }

    public TelaClassificacao(Jogo98 s, int rodada, bool relogio) : this(s, rodada, relogio, TRegras.DoSave(s, TRegras.Original)) { }

    TelaClassificacao(Jogo98 s, int rodada, bool relogio, TRegras regras)
    {
        if (Visual.Moderno)
        {
            var m = new TelaClassificacaoModerna(s, rodada, relogio, regras);
            m.Fechou += () => Fechou?.Invoke();
            _tela = m;
            Content = m;
        }
        else
        {
            var o = new TelaClassificacaoOriginal(s, rodada, relogio);
            o.Fechou += () => Fechou?.Invoke();
            _tela = o;
            Background = new SolidColorBrush(Color.FromRgb(0, 128, 0));
            Content = new Viewbox { Stretch = Stretch.Uniform, Child = o };
        }
        AttachedToVisualTree += (_, _) => _tela.Focus();
    }

    // RETROFOOT_TELA=classificacao: um save do oraculo (RETROFOOT_SAVE = nome ou caminho;
    // padrao j4-original.e98, 8 equipas; a20-original.e98 tem 20). RETROFOOT_RELOGIO=1 liga
    // o fechamento automatico.
    public static Control Demo()
    {
        var nome = Environment.GetEnvironmentVariable("RETROFOOT_SAVE") ?? "j4-original.e98";
        var arq = File.Exists(nome) ? nome : AcharOraculo(nome);
        if (arq == null) return new TextBlock { Text = "classificacao: não achei " + nome, Foreground = Brushes.White };
        var sessao = new Sessao(Jogo98.Ler(File.ReadAllBytes(arq)), _ => { }, arquivo: null);
        var t = new TelaClassificacao(sessao, Math.Max(sessao.J.M.Jornada - 1, 1),
            Environment.GetEnvironmentVariable("RETROFOOT_RELOGIO") == "1");
        t.Fechou += () => Console.WriteLine("RETROFOOT-CLASSIFICACAO fechou");
        return t;
    }

    static string? AcharOraculo(string nome)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "RetroFoot.Motor.Testes", "Oraculo", nome);
            if (File.Exists(p)) return p;
        }
        return null;
    }
}
