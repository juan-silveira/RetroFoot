using System;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace RetroFoot.Android;

[global::Android.App.Application]
public class Application : AvaloniaAndroidApplication<RetroFoot.App.App>
{
    protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    // Prova: o modo de desenho sai de "adb shell setprop debug.retrofoot.modo egl|vulkan|software"
    // (sem a propriedade, o padrao do Avalonia: EGL e, se falhar, software)
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // a atualizacao (RetroFoot.App/Atualizacao.cs) e os efeitos sonoros (Som.cs), ligados
        // aqui, antes de qualquer tela: a tela inicial procura a versao nova ao ser montada, e
        // isso pode acontecer antes da MainActivity (assim, as vezes, nao procurava)
        RetroFoot.App.Atualizacao.PastaApk = System.IO.Path.Combine(CacheDir!.AbsolutePath, "atualizacao");
        RetroFoot.App.Atualizacao.InstalarApk = InstalarApk;
        RetroFoot.App.Som.TocarAndroid = Tocar;
        builder = base.CustomizeAppBuilder(builder).WithInterFont();
        string modo = "";
        try
        {
            using var p = Java.Lang.Runtime.GetRuntime()!.Exec("getprop debug.retrofoot.modo");
            using var r = new System.IO.StreamReader(p!.InputStream!);
            modo = r.ReadToEnd().Trim();
        }
        catch { }
        Console.WriteLine("RETROFOOT-PROVA modo de desenho: " + (modo.Length > 0 ? modo : "padrao"));
        try
        {
            using var p = Java.Lang.Runtime.GetRuntime()!.Exec("getprop debug.retrofoot.cenario");
            using var r = new System.IO.StreamReader(p!.InputStream!);
            RetroFoot.App.TelaCelular.Cenario = r.ReadToEnd().Trim() == "1";
        }
        catch { }
        // testes da atualizacao: "adb shell setprop debug.retrofoot.ota <url>" troca a lista de
        // releases (a RETROFOOT_OTA_URL do computador; o Android nao tem variaveis de ambiente)
        try
        {
            using var p = Java.Lang.Runtime.GetRuntime()!.Exec("getprop debug.retrofoot.ota");
            using var r = new System.IO.StreamReader(p!.InputStream!);
            var ota = r.ReadToEnd().Trim();
            if (ota.Length > 0) Environment.SetEnvironmentVariable("RETROFOOT_OTA_URL", ota);
        }
        catch { }
        AndroidRenderingMode[]? modos = modo switch
        {
            "egl" => new[] { AndroidRenderingMode.Egl },
            "vulkan" => new[] { AndroidRenderingMode.Vulkan },
            "software" => new[] { AndroidRenderingMode.Software },
            _ => null,
        };
        return modos == null ? builder : builder.With(new AndroidPlatformOptions { RenderingMode = modos });
    }

    // o APK baixado (no cache, a pasta do FileProvider, Resources/xml/caminhos.xml) no
    // instalador do Android; false: falta a permissao de instalar apps do RetroFoot (abre a
    // tela dela; ao voltar, o jogo chama de novo)
    bool InstalarApk(string caminho)
    {
        // a tela do app (a MainActivity) abre o instalador e os ajustes; sem ela, o contexto do
        // app com NewTask. Num celular a abertura pelo contexto do app falhou (RuntimeException):
        // cada passo tem outro caminho e o erro sai com a causa (a classe Java e a mensagem)
        var ctx = (global::Android.Content.Context?)MainActivity.Atual ?? this;
        void Abrir(global::Android.Content.Intent i)
        {
            if (ctx is not global::Android.App.Activity) i.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            ctx.StartActivity(i);
        }
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.O && !PackageManager!.CanRequestPackageInstalls())
        {
            // a permissao "instalar apps desconhecidos" do RetroFoot; sem a tela dela (alguns
            // fabricantes), a lista geral dessa permissao
            Tentar(() => Abrir(new global::Android.Content.Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources, global::Android.Net.Uri.Parse("package:" + PackageName))),
                () => Abrir(new global::Android.Content.Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources)),
                () => Abrir(new global::Android.Content.Intent(global::Android.Provider.Settings.ActionSecuritySettings)));
            return false;
        }
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(this, PackageName + ".arquivos", new Java.IO.File(caminho));
        global::Android.Content.Intent Ver(string acao)
        {
            var i = new global::Android.Content.Intent(acao);
            i.SetDataAndType(uri, "application/vnd.android.package-archive");
            i.AddFlags(global::Android.Content.ActivityFlags.GrantReadUriPermission);
            return i;
        }
        Tentar(() => Abrir(Ver(global::Android.Content.Intent.ActionView)),
            () => Abrir(Ver(global::Android.Content.Intent.ActionInstallPackage)));
        return true;
    }

    // tenta cada jeito ate um dar certo; se nenhum, o erro do primeiro com a causa legivel
    static void Tentar(params Action[] jeitos)
    {
        Exception? primeiro = null;
        foreach (var j in jeitos)
        {
            try { j(); return; }
            catch (Exception ex)
            {
                Console.WriteLine("atualizacao: " + ex);
                primeiro ??= ex;
            }
        }
        string causa = primeiro is Java.Lang.Throwable t ? (t.Class?.Name ?? t.GetType().Name) + (t.LocalizedMessage is { Length: > 0 } m ? ": " + m : "") : primeiro?.Message ?? "";
        throw new InvalidOperationException(causa, primeiro);
    }

    // um efeito sonoro (o .wav que o jogo gera) no MediaPlayer
    static void Tocar(string arquivo)
    {
        try
        {
            var mp = new global::Android.Media.MediaPlayer();
            mp.SetDataSource(arquivo);
            mp.Completion += (_, _) => mp.Release();
            mp.Prepare();
            mp.Start();
        }
        catch (Exception ex) { Console.WriteLine("som: " + ex.Message); }
    }
}
