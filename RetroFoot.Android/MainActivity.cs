using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;

namespace RetroFoot.Android;

[Activity(
#if DEBUG
    Label = "RetroFoot (prova)",
#else
    Label = "RetroFoot",
#endif
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ScreenOrientation = ScreenOrientation.FullUser,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    // ao voltar ao app (depois de outro app por cima, ou do Android refazer a superficie), a
    // tela inteira e redesenhada: num celular ela voltou toda verde, sem o conteudo
    protected override void OnResume()
    {
        base.OnResume();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (RetroFoot.App.App.Raiz is { } v) Redesenhar(v);
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    static void Redesenhar(Avalonia.Visual v)
    {
        v.InvalidateVisual();
        if (v is Avalonia.Layout.Layoutable l) l.InvalidateMeasure();
        foreach (var c in Avalonia.VisualTree.VisualExtensions.GetVisualChildren(v)) Redesenhar(c);
    }
}
