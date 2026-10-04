using Android;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Content.PM;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using AndroidX.Work;
using Weather.Presentation.Background;
using Weather.Presentation.Resources;

namespace Weather.App.Background;

/// <summary>Android の通知・ウィジェット・背景更新(WorkManager)。</summary>
public sealed class AndroidBackgroundPlatform:IAlertNotifier,IWidgetPublisher,IBackgroundScheduler{
    private const string ChannelId="weather-alerts";
    private const string PeriodicWorkName="weather-refresh";
    private const string OneTimeWorkName="weather-refresh-now";

    private static Context Context=>Android.App.Application.Context;

    public bool HasWidgets=>WidgetIds(Context).Length>0;

    public async Task<bool> RequestPermissionAsync(){
        if(!OperatingSystem.IsAndroidVersionAtLeast(33)){
            return NotificationManagerCompat.From(Context)!.AreNotificationsEnabled();
        }
        var status=await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.PostNotifications>);
        return status==PermissionStatus.Granted;
    }

    public void Show(AlertNotification notification){
        ArgumentNullException.ThrowIfNull(notification);
        var context=Context;
        if(OperatingSystem.IsAndroidVersionAtLeast(33)&&ContextCompat.CheckSelfPermission(context,Manifest.Permission.PostNotifications)!=Permission.Granted){
            return;
        }
        EnsureChannel(context);
        var firstLine=notification.Body.Split('\n')[0];
        var built=new NotificationCompat.Builder(context,ChannelId)
            .SetSmallIcon(Resource.Drawable.ic_stat_weather)!
            .SetContentTitle(notification.Title)!
            .SetContentText(firstLine)!
            .SetStyle(new NotificationCompat.BigTextStyle().BigText(notification.Body))!
            .SetPriority(NotificationCompat.PriorityHigh)!
            .SetAutoCancel(true)!
            .SetContentIntent(OpenAppIntent(context,notification.NumericId))!
            .Build()!;
        NotificationManagerCompat.From(context)!.Notify(notification.NumericId,built);
    }

    public void Publish(WidgetSnapshot snapshot){
        ArgumentNullException.ThrowIfNull(snapshot);
        var context=Context;
        WidgetFile.Write(context,snapshot);
        var ids=WidgetIds(context);
        if(ids.Length>0){
            AppWidgetManager.GetInstance(context)!.UpdateAppWidget(ids,WidgetViews.Create(context,snapshot));
        }
    }

    /// <summary>定期実行(30 分。OS が電池・通信状況に応じて遅らせる)。既に予約されていれば何もしない。</summary>
    public void Schedule(){
        var request=new PeriodicWorkRequest.Builder(Java.Lang.Class.FromType(typeof(RefreshWorker)),30,Java.Util.Concurrent.TimeUnit.Minutes!)
            .SetConstraints(NetworkConstraints())!
            .Build()!;
        WorkManager.GetInstance(Context).EnqueueUniquePeriodicWork(PeriodicWorkName,ExistingPeriodicWorkPolicy.Keep!,(PeriodicWorkRequest)request);
    }

    /// <summary>ウィジェットが置かれた直後など、すぐに 1 回更新する。</summary>
    public static void RunOnce(Context context){
        var request=new OneTimeWorkRequest.Builder(Java.Lang.Class.FromType(typeof(RefreshWorker)))
            .SetConstraints(NetworkConstraints())!
            .Build()!;
        WorkManager.GetInstance(context).EnqueueUniqueWork(OneTimeWorkName,ExistingWorkPolicy.Replace!,(OneTimeWorkRequest)request);
    }

    internal static PendingIntent OpenAppIntent(Context context,int requestCode){
        var intent=new Intent(context,typeof(MainActivity));
        intent.SetFlags(ActivityFlags.NewTask|ActivityFlags.ClearTop|ActivityFlags.SingleTop);
        return PendingIntent.GetActivity(context,requestCode,intent,PendingIntentFlags.Immutable|PendingIntentFlags.UpdateCurrent)!;
    }

    internal static int[] WidgetIds(Context context){
        var manager=AppWidgetManager.GetInstance(context);
        if(manager is null){
            return [];
        }
        return manager.GetAppWidgetIds(new ComponentName(context,Java.Lang.Class.FromType(typeof(WeatherWidgetProvider))))??[];
    }

    private static Constraints NetworkConstraints(){
        return new Constraints.Builder().SetRequiredNetworkType(NetworkType.Connected!)!.Build()!;
    }

    private static void EnsureChannel(Context context){
        if(!OperatingSystem.IsAndroidVersionAtLeast(26)){
            return;
        }
        var manager=(NotificationManager?)context.GetSystemService(Context.NotificationService);
        if(manager is null||manager.GetNotificationChannel(ChannelId) is not null){
            return;
        }
        manager.CreateNotificationChannel(new NotificationChannel(ChannelId,Strings.AlertNotificationsLabel,NotificationImportance.High));
    }
}

/// <summary>WorkManager から呼ばれる背景更新。プロセスが起動していなければ MainApplication が DI を組み立ててから呼ばれる。</summary>
public sealed class RefreshWorker(Context context,WorkerParameters parameters):Worker(context,parameters){
    public override Result DoWork(){
        var services=IPlatformApplication.Current?.Services;
        if(services is null){
            return Result.InvokeRetry()!;
        }
        //WorkManager は 10 分で打ち切るため、その前に止める
        using var cancellation=new CancellationTokenSource(TimeSpan.FromMinutes(8));
        try{
            services.GetRequiredService<BackgroundRefreshService>().RunAsync(cancellation.Token).GetAwaiter().GetResult();
            return Result.InvokeSuccess()!;
        }catch(OperationCanceledException){
            return Result.InvokeRetry()!;
        }catch(Exception ex) when(ex is IOException or HttpRequestException){
            return Result.InvokeRetry()!;
        }
    }
}
