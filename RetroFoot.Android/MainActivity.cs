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
    // a tela em primeiro plano (o instalador da atualizacao abre a partir dela)
    internal static MainActivity? Atual;

    // o Voltar do Android: as janelas do app respondem (a folha de cima, a pagina e, no fim, a
    // pergunta se quer sair). A tecla chega aqui, mas a vista do Avalonia a engolia como uma tecla
    // qualquer (nem fechava o app nem avisava): tratada antes dela
    public override bool DispatchKeyEvent(global::Android.Views.KeyEvent? e)
    {
        if (e?.KeyCode == global::Android.Views.Keycode.Back && RetroFoot.App.App.Voltar is { } v)
        {
            if (e.Action == global::Android.Views.KeyEventActions.Up) Avalonia.Threading.Dispatcher.UIThread.Post(v);
            return true;
        }
        return base.DispatchKeyEvent(e);
    }

    protected override void OnDestroy()
    {
        // o app terminando (o Sair, ou o app fechado pelo usuario): o jogo com amigo apaga a sala
        if (IsFinishing) RetroFoot.App.App.AvisarFechando();
        base.OnDestroy();
    }

    protected override void OnPause()
    {
        if (Atual == this) Atual = null;
        base.OnPause();
    }

    protected override void OnResume()
    {
        base.OnResume();
        Atual = this;
        // o Sair da pergunta do Voltar (App.PerguntarSair): fecha o app
        RetroFoot.App.App.FecharApp = () => Atual?.FinishAndRemoveTask();
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (RetroFoot.App.App.Raiz is { } v) Redesenhar(v);
            IconesClaros();
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    // os icones da barra de status e dos botoes claros, sobre as faixas no verde escuro da area
    // segura (App.AreaSegura); de novo a cada foco, se o sistema ou o Avalonia os trocarem
    public override void OnWindowFocusChanged(bool temFoco)
    {
        base.OnWindowFocusChanged(temFoco);
        if (temFoco) IconesClaros();
    }

    void IconesClaros()
    {
        if (Window is not { } w) return;
        var c = AndroidX.Core.View.WindowCompat.GetInsetsController(w, w.DecorView);
        c.AppearanceLightStatusBars = false;
        c.AppearanceLightNavigationBars = false;
    }

    static void Redesenhar(Avalonia.Visual v)
    {
        v.InvalidateVisual();
        if (v is Avalonia.Layout.Layoutable l) l.InvalidateMeasure();
        foreach (var c in Avalonia.VisualTree.VisualExtensions.GetVisualChildren(v)) Redesenhar(c);
    }
}
