using BackgroundTasks;
using Foundation;
using UserNotifications;
using Weather.Presentation.Background;

namespace Weather.App.Background;

/// <summary>
/// iOS の通知・ウィジェット・背景更新(BGTaskScheduler)。
/// 背景更新は OS が実行の有無と時刻を決め、実行は保証されない(Phases.md)。
/// ウィジェット(WidgetKit 拡張。src/Weather.Widget.iOS)とは App Group のファイルで内容を渡す。
/// </summary>
public sealed class IosBackgroundPlatform:IAlertNotifier,IWidgetPublisher,IBackgroundScheduler{
    /// <summary>Info.plist の BGTaskSchedulerPermittedIdentifiers と一致させる。</summary>
    public const string RefreshTaskId="io.github.alonecraft.soramoyou.refresh";

    /// <summary>App Group(アプリと WidgetKit 拡張の両方の Entitlements に必要)。</summary>
    public const string AppGroupId="group.io.github.alonecraft.soramoyou";

    /// <summary>WidgetKit の再読み込みは Swift の API しかないため、拡張側が 30 分ごとにファイルを読み直す。拡張を同梱していれば常に true とする。</summary>
    public bool HasWidgets=>ContainerDirectory() is not null;

    public async Task<bool> RequestPermissionAsync(){
        var (granted,_)=await UNUserNotificationCenter.Current.RequestAuthorizationAsync(UNAuthorizationOptions.Alert|UNAuthorizationOptions.Sound|UNAuthorizationOptions.Badge);
        return granted;
    }

    public void Show(AlertNotification notification){
        ArgumentNullException.ThrowIfNull(notification);
        var content=new UNMutableNotificationContent{
            Title=notification.Title,
            Body=notification.Body,
            Sound=UNNotificationSound.Default,
            ThreadIdentifier=notification.PlaceId,
        };
        var request=UNNotificationRequest.FromIdentifier(notification.PlaceId+"|"+notification.Key,content,null);
        UNUserNotificationCenter.Current.AddNotificationRequest(request,static error=>{
            if(error is not null){
                System.Diagnostics.Debug.WriteLine($"通知の登録に失敗しました: {error.LocalizedDescription}");
            }
        });
    }

    public void Publish(WidgetSnapshot snapshot){
        ArgumentNullException.ThrowIfNull(snapshot);
        if(ContainerDirectory() is not {} directory){
            return;
        }
        var path=Path.Combine(directory,WidgetSnapshotJson.FileName);
        var temp=path+".tmp";
        File.WriteAllText(temp,WidgetSnapshotJson.Serialize(snapshot));
        File.Move(temp,path,true);
    }

    /// <summary>次回の背景更新を予約する(早くても 30 分後。実際の時刻は OS が決める)。</summary>
    public void Schedule(){
        var request=new BGAppRefreshTaskRequest(RefreshTaskId){EarliestBeginDate=NSDate.FromTimeIntervalSinceNow(30*60)};
        if(!BGTaskScheduler.Shared.Submit(request,out var error)){
            System.Diagnostics.Debug.WriteLine($"背景更新の予約に失敗しました: {error?.LocalizedDescription}");
        }
    }

    /// <summary>起動処理の中(FinishedLaunching が終わる前)に登録する必要がある。</summary>
    public static void Register(){
        BGTaskScheduler.Shared.Register(RefreshTaskId,null,static task=>Run((BGAppRefreshTask)task));
    }

    private static void Run(BGAppRefreshTask task){
        var services=IPlatformApplication.Current?.Services;
        if(services is null){
            task.SetTaskCompleted(false);
            return;
        }
        //次回を先に予約する(今回が打ち切られても次の機会を失わない)
        services.GetRequiredService<IBackgroundScheduler>().Schedule();
        var cancellation=new CancellationTokenSource();
        task.ExpirationHandler=cancellation.Cancel;
        _=Task.Run(async ()=>{
            var success=false;
            try{
                await services.GetRequiredService<BackgroundRefreshService>().RunAsync(cancellation.Token);
                success=true;
            }catch(OperationCanceledException){
                //OS による打ち切り
            }catch(Exception ex) when(ex is IOException or HttpRequestException){
                System.Diagnostics.Debug.WriteLine($"背景更新に失敗しました: {ex.Message}");
            }finally{
                cancellation.Dispose();
                task.SetTaskCompleted(success);
            }
        });
    }

    private static string? ContainerDirectory(){
        return NSFileManager.DefaultManager.GetContainerUrl(AppGroupId)?.Path;
    }
}
