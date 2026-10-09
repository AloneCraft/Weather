using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Weather.Core;
using Weather.Presentation.Resources;
using Weather.Scene;

namespace Weather.Presentation.ViewModels;

public enum PlaceState{Loading,Ready,Stale,OutOfCoverage,Error,Empty}

public sealed record HourItem(DateTimeOffset Time,string TimeText,string Icon,string Temperature,string Probability,string? Text);

public sealed record DayItem(DateOnly Date,string DateText,string Icon,string Weather,string Max,string Min,string Probability,string? Reliability,string Source);

/// <summary>
/// 1 地点の予報・警報・シーン(Screens.md「メイン」)。
/// VM のメソッドでは ConfigureAwait(false) を使わない(UI スレッドに戻って PropertyChanged を発火させるため)。
/// </summary>
public sealed partial class PlaceWeatherViewModel:ObservableObject{
    private readonly IWeatherService weather;
    private readonly WeatherSession session;
    private readonly AppSettings settings;
    private readonly INavigator navigator;
    private readonly IAppLifecycle lifecycle;
    private readonly TimeProvider time;
    private int refreshVersion;
    private readonly SceneConverter converter=new();

    public PlaceWeatherViewModel(PlaceData data,IWeatherService weather,WeatherSession session,AppSettings settings,INavigator navigator,IAppLifecycle lifecycle,TimeProvider time){
        ArgumentNullException.ThrowIfNull(data);
        this.Data=data;
        this.weather=weather;
        this.session=session;
        this.settings=settings;
        this.navigator=navigator;
        this.lifecycle=lifecycle;
        this.time=time;
        this.Name=data.Name;
        session.Put(data);
    }

    public PlaceData Data{get;}
    public ObservableCollection<HourItem> Hours{get;}=[];
    public ObservableCollection<DayItem> Days{get;}=[];

    [ObservableProperty]public partial string Name{get;set;}
    [ObservableProperty]public partial string? AreaNote{get;set;}
    [ObservableProperty]public partial PlaceState State{get;set;}=PlaceState.Loading;
    [ObservableProperty]public partial bool IsBusy{get;set;}
    [ObservableProperty]public partial string CurrentTemperature{get;set;}="--";
    [ObservableProperty]public partial string CurrentIcon{get;set;}="";
    [ObservableProperty]public partial string CurrentText{get;set;}="";
    [ObservableProperty]public partial string? StatusMessage{get;set;}
    [ObservableProperty]public partial string? IssuesText{get;set;}
    [ObservableProperty]public partial string AttributionShort{get;set;}="";
    [ObservableProperty]public partial string AttributionCredit{get;set;}="";
    [ObservableProperty]public partial string? AlertSummary{get;set;}
    [ObservableProperty]public partial AlertTier? HighestAlertTier{get;set;}
    [ObservableProperty]public partial bool HasActiveAlerts{get;set;}
    [ObservableProperty]public partial bool ObservationsAvailable{get;set;}
    [ObservableProperty]public partial SceneState? Scene{get;set;}
    [ObservableProperty]public partial bool IsDarkBackground{get;set;}=true;
    [ObservableProperty]public partial DateTimeOffset? LastUpdated{get;set;}

    /// <summary>読み上げ用の要約(シーンは装飾として扱う。Screens.md「アクセシビリティ」)。</summary>
    [ObservableProperty]public partial string AccessibleSummary{get;set;}="";

    /// <summary>
    /// 予報と警報を取得する。アプリが背景にあるときは取得しない(MET 規約: 使用中でないときに取得しない)。
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken){
        if(!this.lifecycle.IsForeground){
            return;
        }
        //更新が重なる(定期更新・地図からの更新・手動更新)と、先に始めた更新が後で終わることがある。最後に始めた更新の結果だけを反映する
        var version=Interlocked.Increment(ref this.refreshVersion);
        bool IsLatest()=>version==Volatile.Read(ref this.refreshVersion);
        this.IsBusy=true;
        if(this.Data.Forecast is null){
            this.State=PlaceState.Loading;
        }
        try{
            this.Data.Location??=await this.weather.ResolveAsync(this.Data.Point,cancellationToken);
            var location=this.Data.Location;
            this.AreaNote=AreaNoteFor(location);
            this.ObservationsAvailable=this.weather.GetObservationAvailability(location)==Availability.Available;
            var forecastTask=this.weather.GetForecastAsync(location,cancellationToken).AsTask();
            var alertsTask=this.LoadAlertsAsync(location,IsLatest,cancellationToken);
            var forecast=await forecastTask;
            if(!IsLatest()){
                await alertsTask;
                return;
            }
            this.Data.Forecast=forecast;
            await alertsTask;
            if(!IsLatest()){
                return;
            }
            this.LastUpdated=this.time.GetUtcNow();
            this.Apply();
        }catch(WeatherProviderException ex){
            if(!IsLatest()){
                return;
            }
            this.StatusMessage=FailureMessage(ex.Failure);
            if(this.Data.Forecast?.Forecast is not null){
                this.State=PlaceState.Stale;
            }else{
                this.State=PlaceState.Error;
                this.Scene=this.converter.ConvertSkyOnly(this.Data.Point,this.time.GetUtcNow(),SceneOriginKind.None);
            }
        }finally{
            if(IsLatest()){
                this.IsBusy=false;
            }
            this.session.NotifyUpdated(this.Data.PlaceId);
        }
    }

    /// <summary>現在時刻でシーンを更新する(1 分ごと・時間軸の操作時)。</summary>
    public void UpdateScene(DateTimeOffset? at=null){
        var now=at??this.time.GetUtcNow();
        var result=this.Data.Forecast;
        if(result?.Forecast is {} forecast){
            this.Scene=this.converter.Convert(forecast,now);
        }else if(result?.Availability==Availability.JapanOutOfCoverage){
            this.Scene=this.converter.ConvertSkyOnly(this.Data.Point,now,SceneOriginKind.OutOfCoverage);
        }else{
            this.Scene=this.converter.ConvertSkyOnly(this.Data.Point,now,SceneOriginKind.None);
        }
        var s=this.Scene;
        //背景が暗いか(文字色の切り替え)。昼でも厚い雲・霧では明るい背景になる
        this.IsDarkBackground=s.Sun.AltitudeDeg<4||(s.Clouds.Cover<0.6&&s.FogDensity<0.4);
    }

    [RelayCommand]
    private Task OpenHourlyAsync(){
        return this.navigator.GoToAsync(Routes.Hourly,Parameters());
    }

    [RelayCommand]
    private Task OpenDayAsync(DayItem? day){
        if(day is null){
            return Task.CompletedTask;
        }
        var p=new Dictionary<string,string>(this.Parameters()){["date"]=day.Date.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)};
        return this.navigator.GoToAsync(Routes.Daily,p);
    }

    [RelayCommand]
    private Task OpenAlertsAsync(){
        return this.navigator.GoToAsync(Routes.Alerts,this.Parameters());
    }

    [RelayCommand]
    private Task OpenHistoryAsync(){
        return this.navigator.GoToAsync(Routes.History,this.Parameters());
    }

    [RelayCommand]
    private Task OpenSourcesAsync(){
        return this.navigator.GoToAsync(Routes.Sources,this.Parameters());
    }

    private Dictionary<string,string> Parameters(){
        return new Dictionary<string,string>{["place"]=this.Data.PlaceId};
    }

    private async Task LoadAlertsAsync(ResolvedLocation location,Func<bool> isLatest,CancellationToken cancellationToken){
        AlertResult alerts;
        try{
            alerts=await this.weather.GetAlertsAsync(location,cancellationToken);
        }catch(WeatherProviderException){
            if(!isLatest()){
                return;
            }
            this.Data.Alerts=null;
            this.AlertSummary=Strings.AlertsFetchFailed;
            this.HasActiveAlerts=false;
            return;
        }
        if(!isLatest()){
            return;
        }
        this.Data.Alerts=alerts;
        this.ApplyAlerts();
    }

    /// <summary>取得結果を表示用の値に変換する。</summary>
    internal void Apply(){
        var units=this.settings.Units;
        var result=this.Data.Forecast;
        var zone=this.Data.Zone;
        this.Hours.Clear();
        this.Days.Clear();
        if(result is null){
            return;
        }
        if(result.Availability==Availability.JapanOutOfCoverage){
            this.State=PlaceState.OutOfCoverage;
            this.StatusMessage=Strings.OutOfCoverage;
            this.CurrentTemperature="--";
            this.CurrentText="";
            this.CurrentIcon="";
            this.AttributionShort="";
            this.AttributionCredit="";
            this.UpdateScene();
            return;
        }
        if(result.Forecast is not {} forecast){
            this.State=PlaceState.Empty;
            this.UpdateScene();
            return;
        }
        var now=this.time.GetUtcNow();
        //日本域の表示は常に気象庁の天気文を優先する(方針 5。CurrentConditions)
        var current=CurrentConditions.From(forecast,this.Data.Point,now,zone,units);
        this.CurrentTemperature=current.Temperature;
        this.CurrentIcon=current.Icon;
        this.CurrentText=current.Text;
        foreach(var point in forecast.TimeSeries.Where(p=>p.End>now).Take(48)){
            this.Hours.Add(new HourItem(
                point.Start,
                TimeText.Hour(point.Start,zone),
                ConditionIcons.For(point.Condition,IsNight(this.Data.Point,point.Start+point.Duration/2)),
                Units.Temperature(point.TemperatureC,units),
                Units.Probability(point.PrecipitationProbability),
                point.WeatherText));
        }
        foreach(var day in forecast.Daily){
            var pop=CurrentConditions.DayProbability(day);
            string? reliability=null;
            if(day.Reliability is {} r){
                reliability=string.Format(System.Globalization.CultureInfo.CurrentCulture,Strings.ReliabilityFormat,r);
            }
            this.Days.Add(new DayItem(
                day.Date,
                TimeText.Day(day.Date),
                ConditionIcons.For(day.Condition,false),
                (day.WeatherText??ConditionIcons.Describe(day.Condition)).Replace('　',' '),
                Units.Temperature(day.TempMaxC,units),
                Units.Temperature(day.TempMinC,units),
                Units.Probability(pop),
                reliability,
                AttributionText.Short(day.Source,zone)));
        }
        var primary=forecast.Daily.Select(static d=>d.Source).FirstOrDefault()??forecast.TimeSeries.Select(static p=>p.Source).FirstOrDefault();
        if(primary is not null){
            this.AttributionShort=AttributionText.Short(primary,zone);
            this.AttributionCredit=AttributionText.Credit(primary);
        }
        if(forecast.Issues.Count>0){
            this.IssuesText=string.Format(System.Globalization.CultureInfo.CurrentCulture,Strings.IssuesFormat,string.Join(", ",forecast.Issues.Select(static i=>i.ProductName)));
        }else{
            this.IssuesText=null;
        }
        this.AccessibleSummary=string.Format(System.Globalization.CultureInfo.CurrentCulture,Strings.AccessibleSummaryFormat,this.Name,this.CurrentTemperature,this.CurrentText);
        if(forecast.IsStale){
            this.State=PlaceState.Stale;
            this.StatusMessage=Strings.OfflineShowingPrevious;
        }else{
            this.State=PlaceState.Ready;
            this.StatusMessage=null;
        }
        this.UpdateScene(now);
    }

    private void ApplyAlerts(){
        var result=this.Data.Alerts;
        if(result is null){
            this.AlertSummary=null;
            this.HasActiveAlerts=false;
            this.HighestAlertTier=null;
            return;
        }
        if(result.Availability==Availability.NotSupported){
            this.AlertSummary=Strings.AlertsNotSupported;
            this.HasActiveAlerts=false;
            this.HighestAlertTier=null;
            return;
        }
        if(result.Alerts is not {} set){
            this.AlertSummary=null;
            this.HasActiveAlerts=false;
            this.HighestAlertTier=null;
            return;
        }
        var active=set.Active.ToList();
        this.HasActiveAlerts=active.Count>0;
        if(active.Count==0){
            this.AlertSummary=Strings.NoActiveAlerts;
            this.HighestAlertTier=null;
            return;
        }
        var top=active.MaxBy(static a=>a.Tier)!;
        this.HighestAlertTier=top.Tier;
        var names=string.Join(", ",active.Select(static a=>a.EventName).Distinct().Take(3));
        if(active.Count>3){
            names+=Strings.AndMore;
        }
        this.AlertSummary=names;
    }

    internal static string? AreaNoteFor(ResolvedLocation location){
        if(location.JmaArea is {Kind:JmaAreaMatchKind.Nearest}){
            return string.Format(System.Globalization.CultureInfo.CurrentCulture,Strings.NearestAreaFormat,location.DisplayName);
        }
        return null;
    }

    internal static bool IsNight(GeoPoint point,DateTimeOffset time){
        return CurrentConditions.IsNight(point,time);
    }

    internal static string FailureMessage(ProviderFailure failure){
        switch(failure){
            case ProviderFailure.Network:
                return Strings.FailureNetwork;
            case ProviderFailure.Timeout:
                return Strings.FailureTimeout;
            case ProviderFailure.RateLimited:
                return Strings.FailureRateLimited;
            case ProviderFailure.NotFound:
                return Strings.FailureNotFound;
            case ProviderFailure.InvalidResponse:
                return Strings.FailureInvalid;
            default:
                return Strings.FailureOther;
        }
    }
}
