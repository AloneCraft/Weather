using Weather.Core;

namespace Weather.Presentation.Background;

/// <summary>警報の通知 1 件。文面は AlertNotificationPolicy が機関の表現をそのまま使って作る。</summary>
public sealed record AlertNotification(string Key,string PlaceId,string Title,string Body,AlertTier Tier){
    /// <summary>OS の通知 ID(同じ警報の通知を置き換える)。string.GetHashCode はプロセスごとに変わるため FNV-1a を使う。</summary>
    public int NumericId{
        get{
            var hash=2166136261u;
            foreach(var c in this.PlaceId+"|"+this.Key){
                hash=(hash^c)*16777619u;
            }
            return (int)(hash&0x7FFFFFFF);
        }
    }
}

/// <summary>端末の通知(Android の NotificationManager / iOS の UNUserNotificationCenter)。</summary>
public interface IAlertNotifier{
    Task<bool> RequestPermissionAsync();
    void Show(AlertNotification notification);
}

/// <summary>ウィジェットへの反映(Android の AppWidget / iOS の WidgetKit 拡張)。</summary>
public interface IWidgetPublisher{
    /// <summary>ホーム画面にウィジェットが置かれているか(置かれていなければ背景で予報を取得しない)。</summary>
    bool HasWidgets{get;}
    void Publish(WidgetSnapshot snapshot);
}

/// <summary>背景更新の予約(Android の WorkManager / iOS の BGTaskScheduler)。実行間隔・実行の有無は OS が決める。</summary>
public interface IBackgroundScheduler{
    void Schedule();
}

/// <summary>通知・ウィジェット・背景更新に対応しない環境(テスト・未対応 OS)。</summary>
public sealed class NullBackgroundPlatform:IAlertNotifier,IWidgetPublisher,IBackgroundScheduler{
    public bool HasWidgets=>false;

    public Task<bool> RequestPermissionAsync(){
        return Task.FromResult(false);
    }

    public void Show(AlertNotification notification){
    }

    public void Publish(WidgetSnapshot snapshot){
    }

    public void Schedule(){
    }
}
