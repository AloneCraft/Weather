using Weather.Core;
using Weather.Presentation;

namespace Weather.Presentation.Tests;

internal static class Sample{
    public static readonly DateTimeOffset Now=new(2026,10,4,3,0,0,TimeSpan.Zero);

    public static SourceAttribution Source(ProviderId provider,DataProcessing processing=DataProcessing.None,bool stale=false){
        var agency="MET Norway";
        if(provider==ProviderId.Jma){
            agency="気象庁";
        }else if(provider==ProviderId.Nws){
            agency="National Weather Service";
        }
        return new SourceAttribution{Provider=provider,AgencyName=agency,ProductName="test",IssuedAt=Now.AddHours(-2),RetrievedAt=Now,License=new LicenseInfo("t",null),Processing=processing,IsStale=stale};
    }

    public static ResolvedLocation Tokyo=>new(){Point=new GeoPoint(35.69,139.75),CountryCode="JP",DisplayName="千代田区",TimeZoneId="Asia/Tokyo",JmaArea=new JmaAreaMatch(JmaAreaMatchKind.Inside,"1310100",0)};
    public static ResolvedLocation Oslo=>new(){Point=new GeoPoint(59.91,10.75),CountryCode="NO",DisplayName="Oslo",TimeZoneId="Europe/Oslo"};

    public static Forecast JmaForecast(bool stale=false){
        var src=Source(ProviderId.Jma,stale:stale);
        var day=new DailyForecast(new DateOnly(2026,10,4),src){Condition=new WeatherCondition(SkyCover.Overcast),WeatherText="くもり　昼過ぎ　まで　時々　晴れ",TempMaxC=24};
        var point=new ForecastPoint(Now.AddHours(-1),Now.AddHours(2),src){Condition=new WeatherCondition(SkyCover.Clear),WeatherText="晴れ",TemperatureC=21};
        return new Forecast(Tokyo,[point],[day]);
    }

    public static Forecast MetForecast(){
        var src=Source(ProviderId.MetNorway);
        var point=new ForecastPoint(Now.AddMinutes(-30),Now.AddMinutes(30),src){Condition=new WeatherCondition(SkyCover.PartlyCloudy),TemperatureC=10};
        var day=new DailyForecast(new DateOnly(2026,10,4),Source(ProviderId.MetNorway,DataProcessing.Aggregated)){TempMaxC=12,TempMinC=5};
        return new Forecast(Oslo,[point],[day]);
    }
}

internal sealed class FakeWeatherService:IWeatherService{
    public ResolvedLocation Location{get;set;}=Sample.Tokyo;
    public ForecastResult? Forecast{get;set;}
    public AlertResult? Alerts{get;set;}
    public WeatherProviderException? ForecastError{get;set;}
    public int ForecastCalls{get;private set;}

    public ValueTask<ResolvedLocation> ResolveAsync(GeoPoint point,CancellationToken cancellationToken){
        return ValueTask.FromResult(this.Location);
    }

    public ValueTask<ForecastResult> GetForecastAsync(ResolvedLocation location,CancellationToken cancellationToken){
        this.ForecastCalls++;
        if(this.ForecastError is not null){
            throw this.ForecastError;
        }
        return ValueTask.FromResult(this.Forecast!);
    }

    public ValueTask<AlertResult> GetAlertsAsync(ResolvedLocation location,CancellationToken cancellationToken){
        return ValueTask.FromResult(this.Alerts??AlertResult.NotSupported);
    }

    public Availability GetObservationAvailability(ResolvedLocation location){
        if(location.CountryCode is "JP" or "US"){
            return Availability.Available;
        }
        return Availability.NotSupported;
    }

    public ValueTask<IReadOnlyList<ObservationStation>> FindStationsAsync(ResolvedLocation location,int maxCount,CancellationToken cancellationToken){
        IReadOnlyList<ObservationStation> list=[new ObservationStation("44132",ProviderId.Jma,"東京",new GeoPoint(35.69,139.75),25)];
        return ValueTask.FromResult(list);
    }

    public ValueTask<ObservationSeries> GetObservationsAsync(ObservationStation station,DateTimeOffset from,DateTimeOffset to,CancellationToken cancellationToken){
        throw new NotSupportedException();
    }

    public TimeSpan GetObservationServerRetention(ProviderId provider){
        return TimeSpan.FromDays(10);
    }
}

internal sealed class FakeNavigator:INavigator{
    public List<(string Route,IReadOnlyDictionary<string,string>? Parameters)> Calls{get;}=[];
    public int BackCount{get;private set;}

    public Task GoToAsync(string route,IReadOnlyDictionary<string,string>? parameters=null){
        this.Calls.Add((route,parameters));
        return Task.CompletedTask;
    }

    public Task GoBackAsync(){
        this.BackCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeLifecycle:IAppLifecycle{
    public bool IsForeground{get;set;}=true;
    public event EventHandler? Resumed;
    public event EventHandler? Stopped;

    public void Resume(){
        this.IsForeground=true;
        this.Resumed?.Invoke(this,EventArgs.Empty);
    }

    public void Stop(){
        this.IsForeground=false;
        this.Stopped?.Invoke(this,EventArgs.Empty);
    }
}

internal sealed class FakeSettingsStore:ISettingsStore{
    private readonly Dictionary<string,string?> values=new(StringComparer.Ordinal);

    public string? Get(string key){
        this.values.TryGetValue(key,out var value);
        return value;
    }

    public void Set(string key,string? value){
        this.values[key]=value;
    }
}

internal sealed class FakeLocationService(LocationStatus status,GeoPoint? point):ILocationService{
    public Task<LocationStatus> RequestPermissionAsync(){
        return Task.FromResult(status);
    }

    public Task<GeoPoint?> GetCurrentLocationAsync(CancellationToken cancellationToken){
        return Task.FromResult(point);
    }
}

internal sealed class FakeFavorites:IFavoritesStore{
    public List<FavoritePlace> Items{get;}=[];

    public ValueTask<IReadOnlyList<FavoritePlace>> GetAllAsync(CancellationToken cancellationToken){
        IReadOnlyList<FavoritePlace> list=[..this.Items.OrderBy(static f=>f.SortOrder)];
        return ValueTask.FromResult(list);
    }

    public ValueTask SaveAsync(FavoritePlace place,CancellationToken cancellationToken){
        this.Items.RemoveAll(f=>f.Id==place.Id);
        this.Items.Add(place);
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync(string id,CancellationToken cancellationToken){
        this.Items.RemoveAll(f=>f.Id==id);
        return ValueTask.CompletedTask;
    }

    public ValueTask ReorderAsync(IReadOnlyList<string> idsInOrder,CancellationToken cancellationToken){
        for(var i=0;i<idsInOrder.Count;i++){
            var index=this.Items.FindIndex(f=>f.Id==idsInOrder[i]);
            this.Items[index]=this.Items[index] with{SortOrder=i};
        }
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeDialogs:IDialogService{
    public bool Confirm{get;set;}=true;

    public Task AlertAsync(string title,string message){
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmAsync(string title,string message,string accept,string cancel){
        return Task.FromResult(this.Confirm);
    }
}
