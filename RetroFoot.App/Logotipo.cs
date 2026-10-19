using System;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace RetroFoot.App;

// A marca do RetroFoot (Assets/Marca): o icone (o quadrado escuro com a bola e o nome), a bola
// sozinha, o nome (TURBO verde, FOOT ouro, contorno branco, 700x135) e a capa da tela inicial
// (o quadro verde com a bola no centro: capa-16x9 1076x605, capa-4x3 807x605)
public static class Logotipo
{
    static Bitmap? Ler(string arquivo)
    {
        try { using var s = AssetLoader.Open(new Uri("avares://RetroFoot.App/Assets/Marca/" + arquivo)); return new Bitmap(s); }
        catch { return null; }
    }

    static Bitmap? _bola, _nome, _capaLarga, _capa;
    public static Bitmap? Bola => _bola ??= Ler("bola-256.png");
    public static Bitmap? Nome => _nome ??= Ler("nome.png");
    public static Bitmap? Capa(bool largo) => largo ? _capaLarga ??= Ler("capa-16x9.jpg") : _capa ??= Ler("capa-4x3.jpg");

    // a capa a encher a area toda (corta o que sobra, centrada)
    public static Image ImagemCapa(bool largo) => new()
    {
        Source = Capa(largo), Stretch = Stretch.UniformToFill,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    // o icone da janela e da barra de tarefas
    public static WindowIcon? IconeJanela()
    {
        try { using var s = AssetLoader.Open(new Uri("avares://RetroFoot.App/Assets/Marca/icone-256.png")); return new WindowIcon(s); }
        catch { return null; }
    }

    public static Image ImagemBola(double tam) => new()
    {
        Source = Bola, Width = tam, Height = tam, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
    };

    // o nome na altura pedida (a largura pela proporcao 700x135)
    public static Image ImagemNome(double altura) => new()
    {
        Source = Nome, Height = altura, Width = Math.Round(altura * 700 / 135), Stretch = Stretch.Uniform,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // a bola e o nome lado a lado (o logo na horizontal)
    public static StackPanel Logo(double altura) => new()
    {
        Orientation = Orientation.Horizontal, Spacing = Math.Round(altura * 0.18), VerticalAlignment = VerticalAlignment.Center,
        Children = { ImagemBola(altura), ImagemNome(altura * 0.62) },
    };
}
