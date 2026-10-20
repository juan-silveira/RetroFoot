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
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // a atualizacao (RetroFoot.App/Atualizacao.cs): o APK vai para o cache (a pasta do
        // FileProvider, Resources/xml/caminhos.xml) e e entregue ao instalador do Android
        RetroFoot.App.Atualizacao.PastaApk = System.IO.Path.Combine(CacheDir!.AbsolutePath, "atualizacao");
        RetroFoot.App.Atualizacao.InstalarApk = InstalarApk;
    }

    // false: falta a permissao de instalar apps do RetroFoot (abre a tela dela; ao voltar, o
    // jogo chama de novo)
    bool InstalarApk(string caminho)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O && !PackageManager!.CanRequestPackageInstalls())
        {
            var p = new Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources, global::Android.Net.Uri.Parse("package:" + PackageName));
            StartActivity(p);
            return false;
        }
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(this, PackageName + ".arquivos", new Java.IO.File(caminho));
        var i = new Intent(Intent.ActionView);
        i.SetDataAndType(uri, "application/vnd.android.package-archive");
        i.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
        StartActivity(i);
        return true;
    }
}
