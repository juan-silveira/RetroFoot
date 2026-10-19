using System;
using Avalonia;

namespace RetroFoot.Desktop;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // o 2o passo da atualizacao (Atualizacao.Concluir): sem tela, troca e abre o novo
        if (RetroFoot.App.Atualizacao.Concluir(args)) return;
        AppBuilder.Configure<RetroFoot.App.App>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
    }
}
