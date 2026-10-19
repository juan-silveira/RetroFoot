using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;

namespace RetroFoot.App;

// Acessibilidade (Opcoes > Acessibilidade, gravado em opcoes.json): o tamanho do texto das
// telas do jogo, de 80% a 120% do original. As funcoes de texto das telas desenhadas
// (equipa, jornada, classificacao, listas, mercado, partida; os dois visuais) multiplicam o
// tamanho por Texto. As telas montadas com controles (Configuracoes, hub, ferramentas) ficam
// como estao. As posicoes das telas nao mudam: com texto maior, o que nao couber e cortado
// e ganha a dica com o texto inteiro.
public static class Acessibilidade
{
    public const int Minimo = 80, Maximo = 120, Passo = 5;

    // o tamanho escolhido, em % (100 = o original)
    public static int Percentual { get; private set; } = Normalizar(OpcoesDoJogo.Ler()?.TamanhoTexto ?? 100);

    // o fator das funcoes de texto
    public static double Texto => Percentual / 100.0;

    public static int Normalizar(int p) => Math.Clamp((int)Math.Round(p / (double)Passo) * Passo, Minimo, Maximo);

    // muda o tamanho; as telas abertas redesenham na hora (o deslizante das Opcoes mostra o
    // tamanho novo nas telas por tras dele)
    public static void Aplicar(int percentual)
    {
        int p = Normalizar(percentual);
        if (p == Percentual) return;
        Percentual = p;
        Visual.Redesenhar();
        Redesenhar();
    }

    // cada controle desenhado das janelas abertas mede e desenha de novo
    static void Redesenhar()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime d) return;
        foreach (var w in d.Windows)
            foreach (var v in w.GetVisualDescendants().Prepend(w))
            {
                v.InvalidateVisual();
                if (v is Avalonia.Layout.Layoutable l) l.InvalidateMeasure();
            }
    }
}
