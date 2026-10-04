using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Weather.Core;
using Weather.Presentation.Resources;
using Weather.Scene;

namespace Weather.Presentation.ViewModels;

/// <summary>グラフ用の 1 点(値は表示単位に換算済み)。Rendering の ChartSample と同じ形。</summary>
public readonly record struct ChartPoint(DateTimeOffset Time,double? Temperature,double? Precipitation,int? Probability,bool Suspect=false);

/// <summary>時間別の詳細(グラフのドラッグでシーンの時刻が動く)。</summary>
public sealed partial class HourlyViewModel(WeatherSession session,AppSettings settings):ObservableObject,INavigationAware{
    private readonly SceneConverter converter=new();
    private SceneTimeline? timeline;

    public ObservableCollection<HourItem> Hours{get;}=[];
    [ObservableProperty]public partial IReadOnlyList<ChartPoint> Chart{get;set;}=[];
    [ObservableProperty]public partial string Title{get;set;}="";
    [ObservableProperty]public partial DateTimeOffset? ScrubTime{get;set;}
    [ObservableProperty]public partial string? ScrubText{get;set;}
    [ObservableProperty]public partial SceneState? Scene{get;set;}
    [ObservableProperty]public partial string Attribution{get;set;}="";
    [ObservableProperty]public partial string TemperatureUnit{get;set;}="°C";
    public TimeZoneInfo Zone{get;private set;}=TimeZoneInfo.Utc;

    public Task OnNavigatedToAsync(IReadOnlyDictionary<string,string> parameters){
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.TryGetValue("place",out var id);
        var data=session.Find(id);
        if(data?.Forecast?.Forecast is not {} forecast){
            return Task.CompletedTask;
        }
        var units=settings.Units;
        this.Zone=data.Zone;
        this.Title=string.Format(System.Globalization.CultureInfo.CurrentCulture,Strings.HourlyTitleFormat,data.Name);
        this.TemperatureUnit="°C";
        if(units==UnitSystem.Imperial){
            this.TemperatureUnit="°F";
        }
        this.Hours.Clear();
        var points=new List<ChartPoint>();
        foreach(var p in forecast.TimeSeries){
            double? temperature=null;
            if(p.TemperatureC is {} c){
                temperature=Units.ToTemperature(c,units);
            }
            double? precipitation=null;
            if(p.PrecipitationMm is {} mm){
                precipitation=Units.ToPrecipitation(mm,units);
            }
            points.Add(new ChartPoint(p.Start,temperature,precipitation,p.PrecipitationProbability));
            this.Hours.Add(new HourItem(p.Start,TimeText.Hour(p.Start,this.Zone),ConditionIcons.For(p.Condition,PlaceWeatherViewModel.IsNight(data.Point,p.Start)),Units.Temperature(p.TemperatureC,units),Units.Probability(p.PrecipitationProbability),p.WeatherText));
        }
        this.Chart=points;
        this.timeline=new SceneTimeline(forecast,this.converter);
        if(forecast.TimeSeries.Count>0){
            this.Attribution=AttributionText.Short(forecast.TimeSeries[0].Source,this.Zone)+" ・ "+AttributionText.Credit(forecast.TimeSeries[0].Source);
        }
        return Task.CompletedTask;
    }

    partial void OnScrubTimeChanged(DateTimeOffset? value){
        if(value is not {} t||this.timeline is null){
            this.ScrubText=null;
            return;
        }
        this.Scene=this.timeline.At(t);
        this.ScrubText=TimeText.Local(t,this.Zone).ToString("M/d H:mm",CultureInfo.InvariantCulture);
    }
}

public sealed record DayPartItem(string Label,string Icon,string? Text,string? Detail,string Probability,string? Temperature);

/// <summary>日別の詳細(気象庁: 天気文・風・波・6 時間降水確率・信頼度・予測範囲 / NWS: 昼夜の文章)。</summary>
public sealed partial class DailyDetailViewModel(WeatherSession session,AppSettings settings):ObservableObject,INavigationAware{
    public ObservableCollection<DayPartItem> Parts{get;}=[];
    [ObservableProperty]public partial string Title{get;set;}="";
    [ObservableProperty]public partial string Icon{get;set;}="";
    [ObservableProperty]public partial string? Weather{get;set;}
    [ObservableProperty]public partial string? Wind{get;set;}
    [ObservableProperty]public partial string? Wave{get;set;}
    [ObservableProperty]public partial string Max{get;set;}="--";
    [ObservableProperty]public partial string Min{get;set;}="--";
    [ObservableProperty]public partial string? MaxRange{get;set;}
    [ObservableProperty]public partial string? MinRange{get;set;}
    [ObservableProperty]public partial string? TemperaturePoint{get;set;}
    [ObservableProperty]public partial string? Reliability{get;set;}
    [ObservableProperty]public partial string? MinNote{get;set;}
    [ObservableProperty]public partial string Attribution{get;set;}="";
    [ObservableProperty]public partial string? TemperatureAttribution{get;set;}

    public Task OnNavigatedToAsync(IReadOnlyDictionary<string,string> parameters){
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.TryGetValue("place",out var id);
        parameters.TryGetValue("date",out var dateText);
        var data=session.Find(id);
        if(data?.Forecast?.Forecast is not {} forecast||!DateOnly.TryParseExact(dateText,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)){
            return Task.CompletedTask;
        }
        var day=forecast.Daily.FirstOrDefault(d=>d.Date==date);
        if(day is null){
            return Task.CompletedTask;
        }
        var units=settings.Units;
        var zone=data.Zone;
        this.Title=data.Name+" "+TimeText.Day(date);
        this.Icon=ConditionIcons.For(day.Condition,false);
        this.Weather=(day.WeatherText??ConditionIcons.Describe(day.Condition)).Replace('　',' ');
        this.Wind=day.WindText?.Replace('　',' ');
        this.Wave=day.WaveText?.Replace('　',' ');
        this.Max=Units.Temperature(day.TempMaxC,units);
        this.Min=Units.Temperature(day.TempMinC,units);
        if(day.TempMaxRangeC is {} maxRange){
            this.MaxRange=Units.Temperature(maxRange.Lower,units)+"〜"+Units.Temperature(maxRange.Upper,units);
        }
        if(day.TempMinRangeC is {} minRange){
            this.MinRange=Units.Temperature(minRange.Lower,units)+"〜"+Units.Temperature(minRange.Upper,units);
        }
        if(day.TemperaturePointName is {} point){
            this.TemperaturePoint=string.Format(System.Globalization.CultureInfo.CurrentCulture,Strings.TemperaturePointFormat,point);
        }
        if(day.Reliability is {} r){
            this.Reliability=string.Format(System.Globalization.CultureInfo.CurrentCulture,Strings.ReliabilityFormat,r);
        }
        //NWS の最低気温は「その日の夜(今夜)の最低」で、気象庁の「朝の最低」とは意味が違う
        if(day.Source.Provider==ProviderId.Nws){
            this.MinNote=Strings.MinIsTonight;
        }else if(day.Source.Provider==ProviderId.Jma){
            this.MinNote=Strings.MinIsMorning;
        }
        this.Parts.Clear();
        foreach(var part in day.Parts){
            var label=part.Label??TimeText.Hour(part.Start,zone)+"〜"+TimeText.Hour(part.End,zone);
            string? temperature=null;
            if(part.TemperatureC is not null){
                temperature=Units.Temperature(part.TemperatureC,units);
            }
            this.Parts.Add(new DayPartItem(label,ConditionIcons.For(part.Condition,false),part.WeatherText,part.DetailText,Units.Probability(part.PrecipitationProbability),temperature));
        }
        this.Attribution=AttributionText.Short(day.Source,zone)+" ・ "+AttributionText.Credit(day.Source);
        if(day.TemperatureSource is {} ts){
            this.TemperatureAttribution=string.Format(System.Globalization.CultureInfo.CurrentCulture,Strings.TemperatureSourceFormat,AttributionText.Short(ts,zone));
        }
        return Task.CompletedTask;
    }
}

public sealed record AlertItem(string Id,string Name,AlertTier Tier,string Status,string? Area,string? Period,string Source);

/// <summary>警報の一覧。機関の名称・本文・発表官署・発表時刻をそのまま表示する(気象業務法第 23 条)。</summary>
public sealed partial class AlertsViewModel(WeatherSession session,INavigator navigator):ObservableObject,INavigationAware{
    private string? placeId;

    public ObservableCollection<AlertItem> Items{get;}=[];
    [ObservableProperty]public partial string Title{get;set;}=Strings.AlertsDefaultTitle;
    [ObservableProperty]public partial string? Headline{get;set;}
    [ObservableProperty]public partial string? Message{get;set;}
    [ObservableProperty]public partial string Attribution{get;set;}="";

    public Task OnNavigatedToAsync(IReadOnlyDictionary<string,string> parameters){
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.TryGetValue("place",out this.placeId);
        var data=session.Find(this.placeId);
        this.Items.Clear();
        if(data is null){
            return Task.CompletedTask;
        }
        this.Title=string.Format(System.Globalization.CultureInfo.CurrentCulture,Strings.AlertsTitleFormat,data.Name);
        var result=data.Alerts;
        if(result is null){
            this.Message=Strings.AlertsFetchFailed;
            return Task.CompletedTask;
        }
        if(result.Availability==Availability.NotSupported){
            this.Message=Strings.AlertsNotSupported;
            return Task.CompletedTask;
        }
        if(result.Availability==Availability.JapanOutOfCoverage||result.Alerts is null){
            this.Message=Strings.JmaOutOfCoverageShort;
            return Task.CompletedTask;
        }
        var zone=data.Zone;
        this.Headline=result.Alerts.Headline;
        foreach(var a in result.Alerts.Alerts.OrderByDescending(static a=>a.Tier)){
            this.Items.Add(ToItem(a,zone));
        }
        if(this.Items.Count==0){
            this.Message=Strings.NoActiveAlerts;
        }
        this.Attribution=AttributionText.Short(result.Alerts.Source,zone)+" ・ "+AttributionText.Credit(result.Alerts.Source);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task OpenAsync(AlertItem? item){
        if(item is null||this.placeId is null){
            return Task.CompletedTask;
        }
        return navigator.GoToAsync(Routes.Alert,new Dictionary<string,string>{["place"]=this.placeId,["id"]=item.Id});
    }

    internal static AlertItem ToItem(Alert a,TimeZoneInfo zone){
        string status;
        switch(a.Status){
            case AlertStatus.Cancelled:
                status=Strings.StatusCancelled;
                break;
            case AlertStatus.Updated:
                status=Strings.StatusUpdated;
                break;
            case AlertStatus.Downgraded:
                status=Strings.StatusDowngraded;
                break;
            default:
                status=Strings.StatusActive;
                break;
        }
        string? period=null;
        if(a.Onset is not null||a.Expires is not null){
            var from="";
            if(a.Onset is {} o){
                from=TimeText.Local(o,zone).ToString("M/d H:mm",CultureInfo.InvariantCulture);
            }
            var to="";
            if(a.Expires is {} e){
                to=TimeText.Local(e,zone).ToString("M/d H:mm",CultureInfo.InvariantCulture);
            }
            period=from+"〜"+to;
        }
        return new AlertItem(a.Id,a.EventName,a.Tier,status,a.AreaName,period,AttributionText.Short(a.Source,zone));
    }
}

/// <summary>警報の詳細(本文・指示はそのまま)。</summary>
public sealed partial class AlertDetailViewModel(WeatherSession session):ObservableObject,INavigationAware{
    [ObservableProperty]public partial string Title{get;set;}="";
    [ObservableProperty]public partial string? Headline{get;set;}
    [ObservableProperty]public partial string? Description{get;set;}
    [ObservableProperty]public partial string? Instruction{get;set;}
    [ObservableProperty]public partial string? Area{get;set;}
    [ObservableProperty]public partial string? Office{get;set;}
    [ObservableProperty]public partial string Attribution{get;set;}="";

    public Task OnNavigatedToAsync(IReadOnlyDictionary<string,string> parameters){
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.TryGetValue("place",out var placeId);
        parameters.TryGetValue("id",out var id);
        var data=session.Find(placeId);
        var alert=data?.Alerts?.Alerts?.Alerts.FirstOrDefault(a=>a.Id==id);
        if(data is null||alert is null){
            return Task.CompletedTask;
        }
        this.Title=alert.EventName;
        this.Headline=alert.Headline;
        this.Description=alert.Description;
        this.Instruction=alert.Instruction;
        this.Area=alert.AreaName;
        this.Office=alert.Source.PublishingOffice;
        this.Attribution=AttributionText.Short(alert.Source,data.Zone)+" ・ "+AttributionText.Credit(alert.Source);
        return Task.CompletedTask;
    }
}

public sealed record SourceItem(string Agency,string Product,string? Office,string? Issued,string Retrieved,string License,string? Url,string Credit,string? Processing);

/// <summary>出典の詳細(機関名・プロダクト・発表時刻・取得時刻・ライセンス・URL・加工の有無)。</summary>
public sealed partial class SourcesViewModel(WeatherSession session):ObservableObject,INavigationAware{
    public ObservableCollection<SourceItem> Items{get;}=[];

    public Task OnNavigatedToAsync(IReadOnlyDictionary<string,string> parameters){
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.TryGetValue("place",out var id);
        var data=session.Find(id);
        this.Items.Clear();
        if(data is null){
            return Task.CompletedTask;
        }
        var zone=data.Zone;
        var sources=new List<SourceAttribution>();
        if(data.Forecast?.Forecast is {} forecast){
            sources.AddRange(forecast.Sources);
        }
        if(data.Alerts?.Alerts is {} alerts){
            sources.Add(alerts.Source);
        }
        foreach(var s in sources.DistinctBy(static s=>(s.Provider,s.ProductName,s.IssuedAt))){
            string? issued=null;
            if(s.IssuedAt is {} i){
                issued=TimeText.Local(i,zone).ToString("yyyy/M/d H:mm",CultureInfo.InvariantCulture);
            }
            this.Items.Add(new SourceItem(
                s.AgencyName,
                s.ProductName,
                s.PublishingOffice,
                issued,
                TimeText.Local(s.RetrievedAt,zone).ToString("yyyy/M/d H:mm",CultureInfo.InvariantCulture),
                s.License.Name,
                s.SourceUrl?.ToString(),
                AttributionText.Credit(s),
                AttributionText.ProcessingNote(s)));
        }
        return Task.CompletedTask;
    }
}
