using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace RetroFoot.Android;

// O host de um jogo online no celular (docs/multiplayer-online.md): o jogo roda neste aparelho e
// os outros esperam por ele. Sem isto o Android para o app em segundo plano (ou com a tela
// apagada) e a rodada de todos trava. Um servico em primeiro plano com a notificacao "jogo online
// em andamento" e um WakeLock parcial (a CPU continua) enquanto a sala esta aberta.
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class ServicoOnline : Service
{
    const string Canal = "online";
    const int Id = 4242;
    PowerManager.WakeLock? _trava;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var nm = (NotificationManager)GetSystemService(NotificationService)!;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            nm.CreateNotificationChannel(new NotificationChannel(Canal, "Jogo online", NotificationImportance.Low));
        var abrir = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        var n = (Build.VERSION.SdkInt >= BuildVersionCodes.O ? new Notification.Builder(this, Canal) : new Notification.Builder(this))
            .SetContentTitle("RetroFoot")!
            .SetContentText(intent?.GetStringExtra("texto") ?? "Jogo online em andamento (você é o host)")!
            .SetSmallIcon(Resource.Drawable.Icon)!
            .SetOngoing(true)!
            .SetContentIntent(abrir)!
            .Build()!;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q) StartForeground(Id, n, ForegroundService.TypeDataSync);
        else StartForeground(Id, n);
        if (_trava == null)
        {
            var pm = (PowerManager)GetSystemService(PowerService)!;
            _trava = pm.NewWakeLock(WakeLockFlags.Partial, "retrofoot:online");
            _trava!.Acquire();
        }
        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        if (_trava is { IsHeld: true }) _trava.Release();
        _trava = null;
        base.OnDestroy();
    }

    // ligar/desligar (de RetroFoot.App: Online.ManterVivo)
    public static void Manter(Context ctx, bool ligado)
    {
        var i = new Intent(ctx, typeof(ServicoOnline));
        if (!ligado) { ctx.StopService(i); return; }
        // Android 13+: a notificacao precisa da permissao (sem ela o servico roda, sem aparecer)
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu && MainActivity.Atual is { } a
            && a.CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications) != Permission.Granted)
            a.RequestPermissions(new[] { global::Android.Manifest.Permission.PostNotifications }, 7);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O) ctx.StartForegroundService(i);
        else ctx.StartService(i);
    }
}
