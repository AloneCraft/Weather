using Microsoft.Extensions.Logging;
using Weather.Core;

namespace Weather.Presentation.Background;

public sealed record BackgroundRefreshResult(int PlacesChecked,int PlacesSkipped,int Notifications,bool WidgetUpdated){
    public static BackgroundRefreshResult Nothing{get;}=new(0,0,0,false);
}

/// <summary>
/// 背景更新(Android の WorkManager / iOS の BGTaskScheduler から呼ぶ)。
/// - 対象はお気に入りの先頭から最大 MaxPlaces 地点。ウィジェットは先頭の地点を表示する。
/// - 気象庁・NWS の地点だけ取得する。MET の地点は規約で背景取得が禁止のため取得しない(前面で作ったウィジェットの内容を残す)。
/// - 通知が無効でウィジェットもなければ何も取得しない。
/// iOS の背景実行は保証されないため、この処理が動かなくても前面の表示には影響しない作りにする。
/// </summary>
public sealed partial class BackgroundRefreshService(
    IWeatherService weather,
    IFavoritesStore favorites,
    AppSettings settings,
    ISettingsStore store,
    IAlertNotifier notifier,
    IWidgetPublisher widget,
    TimeProvider time,
    ILogger<BackgroundRefreshService> logger){
    public const int MaxPlaces=5;

    private readonly NotifiedAlertStore notified=new(store);

    public async Task<BackgroundRefreshResult> RunAsync(CancellationToken cancellationToken){
        var notify=settings.AlertNotifications;
        var updateWidget=widget.HasWidgets;
        if(!notify&&!updateWidget){
            return BackgroundRefreshResult.Nothing;
        }
        var places=await favorites.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if(places.Count==0){
            if(updateWidget){
                widget.Publish(WidgetSnapshotFactory.NoPlace(time.GetUtcNow()));
            }
            return BackgroundRefreshResult.Nothing;
        }
        var checkedCount=0;
        var skipped=0;
        var notifications=0;
        var widgetUpdated=false;
        for(var i=0;i<places.Count&&i<MaxPlaces;i++){
            var place=places[i];
            var isWidgetPlace=i==0&&updateWidget;
            if(!notify&&!isWidgetPlace){
                break;
            }
            var location=await weather.ResolveAsync(place.Point,cancellationToken).ConfigureAwait(false);
            if(!weather.AllowsBackgroundFetch(location)){
                skipped++;
                continue;
            }
            checkedCount++;
            AlertResult? alerts=null;
            try{
                alerts=await weather.GetAlertsAsync(location,cancellationToken).ConfigureAwait(false);
            }catch(WeatherProviderException ex){
                LogFetchFailed(logger,place.Id,ex.Failure);
            }
            if(notify&&alerts?.Alerts is {} set){
                notifications+=this.Notify(place,location,set);
            }
            if(isWidgetPlace){
                try{
                    var forecast=await weather.GetForecastAsync(location,cancellationToken).ConfigureAwait(false);
                    widget.Publish(WidgetSnapshotFactory.Create(place.DisplayName,forecast,alerts,true,settings.Units,time.GetUtcNow()));
                    widgetUpdated=true;
                }catch(WeatherProviderException ex){
                    LogFetchFailed(logger,place.Id,ex.Failure);
                }
            }
        }
        return new BackgroundRefreshResult(checkedCount,skipped,notifications,widgetUpdated);
    }

    /// <summary>新たに発表された警報(警報以上)だけを通知する。解除された警報は記録から外し、再び発表されたら通知する。</summary>
    private int Notify(FavoritePlace place,ResolvedLocation location,AlertSet set){
        var zone=TimeText.Zone(location.TimeZoneId);
        var previous=this.notified.Load(place.Id);
        var current=new HashSet<string>(StringComparer.Ordinal);
        var count=0;
        foreach(var alert in set.Active.Where(AlertNotificationPolicy.ShouldNotify).OrderByDescending(static a=>a.Tier)){
            var key=AlertNotificationPolicy.Key(alert);
            if(!current.Add(key)||previous.Contains(key)){
                continue;
            }
            notifier.Show(AlertNotificationPolicy.Create(place.Id,place.DisplayName,alert,zone));
            count++;
        }
        this.notified.Save(place.Id,current);
        return count;
    }

    [LoggerMessage(Level=LogLevel.Warning,Message="背景更新で取得に失敗しました: {PlaceId} {Failure}")]
    private static partial void LogFetchFailed(ILogger logger,string placeId,ProviderFailure failure);
}

/// <summary>前面で取得した予報をウィジェットに反映する(MET の地点もアプリの使用中なら反映できる)。</summary>
public sealed class WidgetUpdater(IWidgetPublisher widget,IWeatherService weather,AppSettings settings,TimeProvider time){
    /// <summary>ウィジェットの地点 = 現在地を除いた先頭の地点(お気に入りの先頭。背景更新と同じ)。</summary>
    public void Publish(IEnumerable<PlaceData> places){
        if(!widget.HasWidgets){
            return;
        }
        var target=places.FirstOrDefault(static p=>!p.IsCurrentLocation);
        var now=time.GetUtcNow();
        if(target is null){
            widget.Publish(WidgetSnapshotFactory.NoPlace(now));
            return;
        }
        if(target.Forecast is null){
            return;
        }
        var background=target.Location is {} location&&weather.AllowsBackgroundFetch(location);
        widget.Publish(WidgetSnapshotFactory.Create(target.Name,target.Forecast,target.Alerts,background,settings.Units,now));
    }
}
