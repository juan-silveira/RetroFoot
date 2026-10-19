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
        AndroidRenderingMode[]? modos = modo switch
        {
            "egl" => new[] { AndroidRenderingMode.Egl },
            "vulkan" => new[] { AndroidRenderingMode.Vulkan },
            "software" => new[] { AndroidRenderingMode.Software },
            _ => null,
        };
        return modos == null ? builder : builder.With(new AndroidPlatformOptions { RenderingMode = modos });
    }
}
