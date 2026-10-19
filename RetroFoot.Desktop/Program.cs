using System;
using Avalonia;

namespace RetroFoot.Desktop;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        AppBuilder.Configure<RetroFoot.App.App>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
}
