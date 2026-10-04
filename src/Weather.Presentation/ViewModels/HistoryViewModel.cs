using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Weather.Core;
using Weather.Presentation.Resources;

namespace Weather.Presentation.ViewModels;

public enum HistoryRange{Day,Week,Month,Year}

public sealed record ObservationRow(string Time,string Temperature,string Precipitation,string Wind,string? Text,bool Suspect);

/// <summary>
/// 観測履歴(History.md)。開いたときに同期し、取得済みの範囲を先に表示する。
/// 日本・米国以外は「観測データは提供されていません」と明示する。
/// </summary>
public sealed partial class HistoryViewModel(WeatherSession session,IWeatherService weather,IObservationHistoryService history,IFavoritesStore favorites,AppSettings settings,TimeProvider time):ObservableObject,INavigationAware{
    private PlaceData? data;
    private int loadVersion;

    public ObservableCollection<ObservationStation> Stations{get;}=[];
    public ObservableCollection<ObservationRow> Rows{get;}=[];
    [ObservableProperty]public partial ObservationStation? Station{get;set;}
    [ObservableProperty]public partial HistoryRange Range{get;set;}=HistoryRange.Day;
    [ObservableProperty]public partial IReadOnlyList<ChartPoint> Chart{get;set;}=[];
    [ObservableProperty]public partial string Title{get;set;}=Strings.HistoryDefaultTitle;
    [ObservableProperty]public partial string? Message{get;set;}
    [ObservableProperty]public partial bool IsBusy{get;set;}
    [ObservableProperty]public partial string? SummaryText{get;set;}
    [ObservableProperty]public partial string Attribution{get;set;}="";
    public TimeZoneInfo Zone{get;private set;}=TimeZoneInfo.Utc;

    public async Task OnNavigatedToAsync(IReadOnlyDictionary<string,string> parameters){
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.TryGetValue("place",out var id);
        this.data=session.Find(id);
        if(this.data?.Location is not {} location){
            this.Message=Strings.NoPlaceInfo;
            return;
        }
        this.Zone=this.data.Zone;
        this.Title=string.Format(System.Globalization.CultureInfo.CurrentCulture,Strings.HistoryTitleFormat,this.data.Name);
        var availability=weather.GetObservationAvailability(location);
        if(availability!=Availability.Available){
            this.Message=Strings.ObservationsNotSupported;
            return;
        }
        this.IsBusy=true;
        try{
            var stations=await weather.FindStationsAsync(location,5,CancellationToken.None);
            this.Stations.Clear();
            foreach(var s in stations){
                this.Stations.Add(s);
            }
            this.Station=stations.FirstOrDefault(s=>s.Key==this.data.StationKey)??stations.FirstOrDefault();
            if(this.Station is null){
                this.Message=Strings.NoStationNearby;
            }
        }catch(WeatherProviderException ex){
            this.Message=PlaceWeatherViewModel.FailureMessage(ex.Failure);
        }finally{
            this.IsBusy=false;
        }
    }

    async partial void OnStationChanged(ObservationStation? value){
        if(value is null){
            return;
        }
        await this.SaveStationAsync(value);
        await this.SyncAsync();
    }

    async partial void OnRangeChanged(HistoryRange value){
        await this.LoadAsync();
    }

    [RelayCommand]
    public async Task SyncAsync(){
        if(this.Station is not {} station){
            return;
        }
        this.IsBusy=true;
        try{
            //取得済みの範囲を先に表示し、追加分を後から反映する
            await this.LoadAsync();
            var result=await history.SyncAsync(station,CancellationToken.None);
            if(result.Failed){
                this.Message=Strings.SyncFailed;
            }else{
                this.Message=null;
            }
            await this.LoadAsync();
        }finally{
            this.IsBusy=false;
        }
    }

    public async Task LoadAsync(){
        if(this.Station is not {} station){
            return;
        }
        var units=settings.Units;
        var now=time.GetUtcNow();
        var from=now-Span(this.Range);
        //範囲を素早く切り替えると読み込みが重なる。後から始めた読み込みだけを反映する(先に始めた読み込みが後で終わっても、最後に選んだ範囲を残す)
        var version=Interlocked.Increment(ref this.loadVersion);
        var observations=await history.GetObservationsAsync(station,from,now,CancellationToken.None);
        if(version!=Volatile.Read(ref this.loadVersion)){
            return;
        }
        var points=new List<ChartPoint>(observations.Count);
        for(var i=0;i<observations.Count;i++){
            var o=observations[i];
            double? temperature=null;
            var suspect=false;
            if(o.TemperatureC is {} t){
                temperature=Units.ToTemperature(t.Value,units);
                suspect=t.Quality==MeasurementQuality.Suspect;
            }
            double? precipitation=null;
            if(o.Precipitation1hMm is {} p&&IsHourlyRow(station.Provider,observations,i)){
                precipitation=Units.ToPrecipitation(p.Value,units);
            }
            points.Add(new ChartPoint(o.ObservedAt,temperature,precipitation,null,suspect));
        }
        this.Chart=points;
        this.Rows.Clear();
        foreach(var o in observations.Reverse().Take(200)){
            var suspect=o.TemperatureC?.Quality==MeasurementQuality.Suspect;
            var temperature=Units.Temperature(o.TemperatureC?.Value,units);
            if(suspect){
                temperature+=")";
            }
            this.Rows.Add(new ObservationRow(
                TimeText.Local(o.ObservedAt,this.Zone).ToString("M/d H:mm",CultureInfo.InvariantCulture),
                temperature,
                Units.Precipitation(o.Precipitation1hMm?.Value,units),
                Units.Wind(o.WindSpeedMs?.Value,units),
                o.WeatherText,
                suspect));
        }
        var localToday=DateOnly.FromDateTime(TimeText.Local(now,this.Zone).DateTime);
        var summaries=await history.GetDailySummariesAsync(station,localToday,localToday,CancellationToken.None);
        if(version!=Volatile.Read(ref this.loadVersion)){
            return;
        }
        if(summaries.LastOrDefault(s=>s.LocalDate==localToday) is {} today){
            var text=string.Format(System.Globalization.CultureInfo.CurrentCulture,Strings.TodaySummaryFormat,Units.Temperature(today.MaxTempC,units),Units.Temperature(today.MinTempC,units));
            if(today.IsDerived){
                text+=Strings.AggregatedNote;
            }
            this.SummaryText=text;
        }else{
            //今日の要約がまだない(別の観測所・範囲から切り替えた直後を含む)ときは、前の値を残さない
            this.SummaryText=null;
        }
        if(station.Provider==ProviderId.Jma){
            this.Attribution="出典:気象庁ホームページ(アメダス)";
        }else{
            this.Attribution="Source: National Weather Service";
        }
        if(observations.Count==0&&this.Message is null){
            this.Message=Strings.NoSavedObservations;
        }else if(observations.Count>0&&this.Message==Strings.NoSavedObservations){
            //別の範囲に切り替えて観測が見つかったら消す(同期失敗など他の Message は残す)
            this.Message=null;
        }
    }

    /// <summary>降水量の棒に使う行。気象庁(10 分値)は正時の行、NWS(毎時 52 分前後の定時観測と特別観測)は各時間帯の最後の観測(History.md)。</summary>
    private static bool IsHourlyRow(ProviderId provider,IReadOnlyList<Observation> observations,int index){
        var current=observations[index].ObservedAt.UtcDateTime;
        if(provider==ProviderId.Jma){
            return current.Minute==0;
        }
        if(index+1>=observations.Count){
            return true;
        }
        var next=observations[index+1].ObservedAt.UtcDateTime;
        return next.Date!=current.Date||next.Hour!=current.Hour;
    }

    private async Task SaveStationAsync(ObservationStation station){
        if(this.data is null||this.data.IsCurrentLocation||this.data.StationKey==station.Key){
            return;
        }
        this.data.StationKey=station.Key;
        var all=await favorites.GetAllAsync(CancellationToken.None);
        if(all.FirstOrDefault(f=>f.Id==this.data.PlaceId) is {} favorite){
            await favorites.SaveAsync(favorite with{StationKey=station.Key},CancellationToken.None);
        }
    }

    internal static TimeSpan Span(HistoryRange range){
        switch(range){
            case HistoryRange.Week:
                return TimeSpan.FromDays(7);
            case HistoryRange.Month:
                return TimeSpan.FromDays(30);
            case HistoryRange.Year:
                return TimeSpan.FromDays(365);
            default:
                return TimeSpan.FromHours(24);
        }
    }
}
