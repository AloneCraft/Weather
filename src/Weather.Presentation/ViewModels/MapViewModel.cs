using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Weather.Core;
using Weather.Presentation.Resources;

namespace Weather.Presentation.ViewModels;

public sealed record LayerOption(FieldLayer Layer,string Title,string Icon);

public sealed record LegendEntry(uint Color,string Label);

/// <summary>地図のピン(お気に入り・現在地)。表示は公式予報(気象庁・NWS・MET)の気温。</summary>
public sealed record MapPinItem(GeoPoint Point,string Label,PlaceWeatherViewModel Place);

/// <summary>
/// 地図のトップ画面(Screens.md「地図」)。層・時間軸・再生・吹き出し・予報シートを持つ。
/// お気に入りの予報の取得と自動更新は MainViewModel(地点の一覧)を使う。
/// 方針 5: 日本周辺の吹き出しには GFS の値を出さない(気象庁のタイル・アメダスの値、またはその旨)。
/// </summary>
public sealed partial class MapViewModel:ObservableObject,IDisposable{
    private static readonly TimeSpan PlayInterval=TimeSpan.FromMilliseconds(700);
    private const double NearestStationKm=25;

    private readonly IMapDataService maps;
    private readonly IWeatherService weather;
    private readonly IJapanArea japan;
    private readonly IMapPixelSampler sampler;
    private readonly MainViewModel places;
    private readonly FavoritesService favorites;
    private readonly MapFocusService focus;
    private readonly AppSettings settings;
    private readonly IAppLifecycle lifecycle;
    private readonly INavigator navigator;
    private readonly ILocationService location;
    private readonly Func<PlaceData,PlaceWeatherViewModel> createPlace;
    private readonly WeatherSession session;
    private readonly TimeProvider time;
    private CancellationTokenSource? frameLoad;
    private CancellationTokenSource? playLoop;
    private DateTimeOffset availabilityAt;

    public MapViewModel(IMapDataService maps,IWeatherService weather,IJapanArea japan,IMapPixelSampler sampler,MainViewModel places,FavoritesService favorites,MapFocusService focus,AppSettings settings,IAppLifecycle lifecycle,INavigator navigator,ILocationService location,Func<PlaceData,PlaceWeatherViewModel> createPlace,WeatherSession session,TimeProvider time){
        this.maps=maps;
        this.weather=weather;
        this.japan=japan;
        this.sampler=sampler;
        this.places=places;
        this.favorites=favorites;
        this.focus=focus;
        this.settings=settings;
        this.lifecycle=lifecycle;
        this.navigator=navigator;
        this.location=location;
        this.createPlace=createPlace;
        this.session=session;
        this.time=time;
        session.PlaceUpdated+=this.OnPlaceUpdated;
        focus.Requested+=this.OnFocusRequested;
        favorites.Changed+=this.OnFavoritesChanged;
        lifecycle.Stopped+=this.OnStopped;
        lifecycle.Resumed+=this.OnResumed;
    }

    public static IReadOnlyList<LayerOption> Layers{get;}=[
        new(FieldLayer.Wind,Strings.LayerWind,"〰"),
        new(FieldLayer.Precipitation,Strings.LayerPrecipitation,"☂"),
        new(FieldLayer.Temperature,Strings.LayerTemperature,"🌡"),
        new(FieldLayer.Clouds,Strings.LayerClouds,"☁"),
    ];

    /// <summary>地図の操作を求める(View がカメラを動かす)。</summary>
    public event EventHandler<MapFocusRequest>? FocusRequested;

    [ObservableProperty]public partial FieldLayer Layer{get;set;}=FieldLayer.Wind;
    [ObservableProperty]public partial IReadOnlyList<DateTimeOffset> Steps{get;set;}=[];
    [ObservableProperty]public partial int StepIndex{get;set;}
    [ObservableProperty]public partial int MaxStep{get;set;}
    [ObservableProperty]public partial string TimeText{get;set;}="";
    [ObservableProperty]public partial string RelativeText{get;set;}="";
    [ObservableProperty]public partial bool IsPlaying{get;set;}
    [ObservableProperty]public partial bool IsLoading{get;set;}
    [ObservableProperty]public partial MapFrame? Frame{get;set;}
    [ObservableProperty]public partial string LegendTitle{get;set;}="";
    [ObservableProperty]public partial IReadOnlyList<LegendEntry> LegendEntries{get;set;}=[];
    [ObservableProperty]public partial string? LegendNote{get;set;}
    [ObservableProperty]public partial IReadOnlyList<LegendEntry> JapanLegendEntries{get;set;}=[];
    [ObservableProperty]public partial string? JapanNote{get;set;}
    [ObservableProperty]public partial string AttributionText{get;set;}="";
    [ObservableProperty]public partial string? IssuesText{get;set;}
    [ObservableProperty]public partial IReadOnlyList<MapPinItem> Pins{get;set;}=[];
    [ObservableProperty]public partial GeoPoint? SelectedPoint{get;set;}
    [ObservableProperty]public partial bool IsProbeVisible{get;set;}
    [ObservableProperty]public partial string ProbeTitle{get;set;}="";
    [ObservableProperty]public partial string ProbeValue{get;set;}="";
    [ObservableProperty]public partial string ProbeSource{get;set;}="";
    [ObservableProperty]public partial PlaceWeatherViewModel? SheetPlace{get;set;}
    [ObservableProperty]public partial bool IsSheetOpen{get;set;}
    [ObservableProperty]public partial bool CanAddFavorite{get;set;}

    private string? probeName;

    public TimeZoneInfo Zone=>this.time.LocalTimeZone;

    /// <summary>初回の読み込み: お気に入り(ピン)・時間軸・現在時刻のコマ。</summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken){
        await this.places.LoadAsync(cancellationToken);
        this.UpdatePins();
        await this.RefreshTimelineAsync(cancellationToken);
        if(this.focus.Take() is {} pending){
            await this.ShowPlaceAsync(pending,cancellationToken);
        }
    }

    /// <summary>時間軸を作り直す(10 分ごと・前面に戻ったとき)。選んでいた時刻にできるだけ近い刻みを保つ。</summary>
    public async Task RefreshTimelineAsync(CancellationToken cancellationToken){
        var now=this.time.GetUtcNow();
        var availability=await this.maps.GetAvailabilityAsync(cancellationToken);
        this.availabilityAt=now;
        var previous=this.Steps.Count>0&&this.StepIndex<this.Steps.Count&&this.StepIndex>=0;
        DateTimeOffset? selected=null;
        if(previous){
            selected=this.Steps[this.StepIndex];
        }
        var steps=MapTimeline.Build(availability,now);
        this.Steps=steps;
        this.MaxStep=Math.Max(0,steps.Count-1);
        var index=MapTimeline.IndexOfNow(steps,now);
        if(selected is {} s&&s>=now-MapTimeline.Past){
            index=Nearest(steps,s);
        }
        if(index==this.StepIndex){
            this.UpdateTimeText();
            await this.LoadFrameAsync(cancellationToken);
            return;
        }
        this.StepIndex=index;
    }

    partial void OnStepIndexChanged(int value){
        this.UpdateTimeText();
        _=this.LoadFrameAsync(CancellationToken.None);
    }

    partial void OnLayerChanged(FieldLayer value){
        _=this.LoadFrameAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void SelectLayer(FieldLayer layer){
        this.Layer=layer;
    }

    [RelayCommand]
    private void TogglePlay(){
        if(this.IsPlaying){
            this.StopPlaying();
            return;
        }
        this.IsPlaying=true;
        this.playLoop=new CancellationTokenSource();
        _=this.PlayAsync(this.playLoop.Token);
    }

    /// <summary>再生: コマを読み終えてから一定時間おいて次へ進む(読み込みが遅くても飛ばさない)。最後まで来たら先頭へ。</summary>
    private async Task PlayAsync(CancellationToken cancellationToken){
        try{
            while(!cancellationToken.IsCancellationRequested&&this.Steps.Count>1){
                await Task.Delay(PlayInterval,this.time,cancellationToken);
                var next=this.StepIndex+1;
                if(next>this.MaxStep){
                    next=0;
                }
                this.StepIndex=next;
                if(this.frameLoad is {} pending){
                    await this.WaitFrameAsync(pending,cancellationToken);
                }
            }
        }catch(OperationCanceledException){
            //停止
        }
    }

    private async Task WaitFrameAsync(CancellationTokenSource load,CancellationToken cancellationToken){
        while(!cancellationToken.IsCancellationRequested&&ReferenceEquals(this.frameLoad,load)&&this.IsLoading){
            await Task.Delay(TimeSpan.FromMilliseconds(50),this.time,cancellationToken);
        }
    }

    private void StopPlaying(){
        this.IsPlaying=false;
        this.playLoop?.Cancel();
        this.playLoop?.Dispose();
        this.playLoop=null;
    }

    /// <summary>選んだ層・時刻のコマを読む。新しい要求が来たら古い要求は取り消す。</summary>
    public async Task LoadFrameAsync(CancellationToken cancellationToken){
        if(this.StepIndex<0||this.StepIndex>=this.Steps.Count){
            return;
        }
        this.frameLoad?.Cancel();
        this.frameLoad?.Dispose();
        var load=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        this.frameLoad=load;
        var at=this.Steps[this.StepIndex];
        var layer=this.Layer;
        this.IsLoading=true;
        try{
            var frame=await this.maps.GetFrameAsync(layer,at,load.Token);
            if(load.IsCancellationRequested){
                return;
            }
            this.Apply(frame);
        }catch(OperationCanceledException){
            //次の要求に置き換えた
        }catch(WeatherProviderException ex){
            this.IssuesText=PlaceWeatherViewModel.FailureMessage(ex.Failure);
        }finally{
            if(ReferenceEquals(this.frameLoad,load)){
                this.IsLoading=false;
            }
        }
    }

    internal void Apply(MapFrame frame){
        this.Frame=frame;
        var legend=frame.Legend??Weather.Core.MapLegends.WindSpeed;
        this.LegendTitle=MapText.LayerTitle(frame.Layer)+" ("+legend.Unit+")";
        this.LegendEntries=Entries(legend);
        this.LegendNote=null;
        if(frame.Pressure is not null){
            this.LegendNote=Strings.LegendPressure;
        }
        this.JapanLegendEntries=[];
        foreach(var tile in frame.Tiles){
            if(!ReferenceEquals(tile.Legend,legend)){
                this.JapanLegendEntries=Entries(tile.Legend);
                break;
            }
        }
        this.JapanNote=MapText.JapanNote(frame);
        this.AttributionText=MapText.Attribution(frame,this.Zone);
        this.IssuesText=null;
        if(frame.Issues.Count>0){
            this.IssuesText=string.Format(CultureInfo.CurrentCulture,Strings.IssuesFormat,string.Join(", ",frame.Issues));
        }
        if(this.SelectedPoint is {} p&&this.IsProbeVisible){
            _=this.ProbeAsync(p,CancellationToken.None);
        }
    }

    private static IReadOnlyList<LegendEntry> Entries(Legend legend){
        return [..legend.Classes.Select((c,i)=>new LegendEntry(c.Color,MapText.LegendLabel(legend,i)))];
    }

    private void UpdateTimeText(){
        if(this.StepIndex<0||this.StepIndex>=this.Steps.Count){
            this.TimeText="";
            this.RelativeText="";
            return;
        }
        var t=this.Steps[this.StepIndex];
        this.TimeText=MapText.Time(t,this.Zone);
        this.RelativeText=MapText.Relative(t,this.time.GetUtcNow());
    }

    /// <summary>地図のタップ: その地点のレイヤーの値を吹き出しに出す。</summary>
    public async Task TapAsync(GeoPoint point,CancellationToken cancellationToken){
        this.SelectedPoint=point;
        this.IsProbeVisible=true;
        this.IsSheetOpen=false;
        await this.ProbeAsync(point,cancellationToken);
    }

    /// <summary>
    /// 吹き出しの内容。日本周辺は気象庁(アメダスの観測点・タイルの凡例の区分)だけを出し、GFS の値は出さない(方針 5)。
    /// </summary>
    internal async Task ProbeAsync(GeoPoint point,CancellationToken cancellationToken){
        var resolved=await this.weather.ResolveAsync(point,cancellationToken);
        this.probeName=resolved.DisplayName;
        this.ProbeTitle=resolved.DisplayName;
        this.CanAddFavorite=resolved.JmaArea is not {Kind:JmaAreaMatchKind.OutOfCoverage};
        var frame=this.Frame;
        if(frame is null){
            this.ProbeValue=Strings.ProbeNoData;
            this.ProbeSource="";
            return;
        }
        if(this.japan.Contains(point.Latitude,point.Longitude)){
            await this.ProbeJapanAsync(frame,point,cancellationToken);
            return;
        }
        this.ProbeGfs(frame,point);
    }

    private void ProbeGfs(MapFrame frame,GeoPoint point){
        var units=this.settings.Units;
        if(frame.Wind is {} wind){
            var (u,v)=wind.Sample(point.Latitude,point.Longitude);
            if(float.IsNaN(u)||float.IsNaN(v)){
                this.ProbeValue=Strings.ProbeNoData;
                this.ProbeSource="";
                return;
            }
            var speed=Math.Sqrt(u*u+v*v);
            var from=(Math.Atan2(-u,-v)*180/Math.PI+360)%360;
            this.ProbeValue=Compass(from)+" "+Units.Wind(speed,units);
            this.ProbeSource=Presentation.AttributionText.Short(wind.U.Source,this.Zone);
            return;
        }
        if(frame.Scalar is {} scalar){
            var value=scalar.Sample(point.Latitude,point.Longitude);
            if(float.IsNaN(value)){
                this.ProbeValue=Strings.ProbeNoData;
                this.ProbeSource="";
                return;
            }
            this.ProbeValue=MapText.Value(scalar.Quantity,value,units);
            if(frame.Pressure is {} pressure&&!float.IsNaN(pressure.Sample(point.Latitude,point.Longitude))){
                this.ProbeValue+=" / "+MapText.Value(FieldQuantity.PressureHpa,pressure.Sample(point.Latitude,point.Longitude),units);
            }
            this.ProbeSource=Presentation.AttributionText.Short(scalar.Source,this.Zone);
            return;
        }
        this.ProbeValue=Strings.ProbeNoData;
        this.ProbeSource="";
    }

    private async Task ProbeJapanAsync(MapFrame frame,GeoPoint point,CancellationToken cancellationToken){
        var units=this.settings.Units;
        if(frame.Japan!=JapanCoverage.Available){
            this.ProbeValue=MapText.JapanNote(frame);
            this.ProbeSource=Strings.ProbeJapanOnly;
            return;
        }
        //アメダス(観測点)は近くの観測所の値をそのまま出す
        if(frame.Arrows is {Kind:ArrowKind.Observation} arrows&&Nearest(arrows.Arrows.Select(static a=>(a.Point,a)),point) is {} arrow&&arrow.SpeedMs is {} speed){
            this.ProbeValue=Compass(arrow.FromDirectionDeg)+" "+Units.Wind(speed,units);
            this.ProbeSource=Presentation.AttributionText.Short(arrows.Source,this.Zone);
            return;
        }
        if(frame.Points is {} points&&Nearest(points.Points.Select(static p=>(p.Point,p)),point) is {} observed){
            this.ProbeValue=MapText.Value(points.Quantity,observed.Value,units);
            this.ProbeSource=Presentation.AttributionText.Short(points.Source,this.Zone);
            return;
        }
        foreach(var tile in frame.Tiles){
            if(await this.sampler.SampleAsync(tile,point,cancellationToken) is not {} color||tile.Legend.MatchColor(color) is not {} match){
                continue;
            }
            var index=tile.Legend.Classes.ToList().IndexOf(match);
            this.ProbeValue=MapText.LegendLabel(tile.Legend,index)+" "+tile.Legend.Unit;
            this.ProbeSource=Presentation.AttributionText.Short(tile.Source,this.Zone);
            return;
        }
        this.ProbeValue=Strings.ProbeNone;
        this.ProbeSource=string.Join(" | ",frame.Tiles.Select(t=>Presentation.AttributionText.Short(t.Source,this.Zone)));
    }

    private static T? Nearest<T>(IEnumerable<(GeoPoint Point,T Item)> items,GeoPoint point) where T:struct{
        T? best=null;
        var bestDistance=NearestStationKm;
        foreach(var (p,item) in items){
            var d=GeoMath.HaversineKm(p.Latitude,p.Longitude,point.Latitude,point.Longitude);
            if(d<=bestDistance){
                best=item;
                bestDistance=d;
            }
        }
        return best;
    }

    private static string Compass(double degrees){
        var names=Strings.CompassPoints.Split(',');
        var index=(int)Math.Round(((degrees%360)+360)%360/(360.0/names.Length))%names.Length;
        return names[index];
    }

    [RelayCommand]
    private void CloseProbe(){
        this.IsProbeVisible=false;
        this.SelectedPoint=null;
    }

    /// <summary>吹き出しの地点の予報シートを開く(公式予報: 日本は気象庁、米国は NWS、その他は MET Norway)。</summary>
    [RelayCommand]
    private async Task OpenForecastAsync(CancellationToken cancellationToken){
        if(this.SelectedPoint is not {} point){
            return;
        }
        var data=new PlaceData{PlaceId="map:"+point.Latitude.ToString("0.###",CultureInfo.InvariantCulture)+","+point.Longitude.ToString("0.###",CultureInfo.InvariantCulture),Name=this.probeName??this.ProbeTitle,Point=point};
        var place=this.createPlace(data);
        this.SheetPlace=place;
        this.IsSheetOpen=true;
        this.IsProbeVisible=false;
        await place.RefreshAsync(cancellationToken);
    }

    /// <summary>ピン(お気に入り)のタップ: その地点のシートを開く。</summary>
    [RelayCommand]
    private void SelectPin(MapPinItem? pin){
        if(pin is null){
            return;
        }
        this.SheetPlace=pin.Place;
        this.SelectedPoint=pin.Point;
        this.CanAddFavorite=false;
        this.IsProbeVisible=false;
        this.IsSheetOpen=true;
    }

    [RelayCommand]
    private void CloseSheet(){
        this.IsSheetOpen=false;
    }

    /// <summary>シートを引き上げる: 空のシーンを背景にした全画面の詳細。</summary>
    [RelayCommand]
    private Task OpenDetailAsync(){
        if(this.SheetPlace is null){
            return Task.CompletedTask;
        }
        return this.navigator.GoToAsync(Routes.PlaceDetail);
    }

    [RelayCommand]
    private async Task AddFavoriteAsync(CancellationToken cancellationToken){
        if(this.SheetPlace is not {} place||!this.CanAddFavorite){
            return;
        }
        await this.favorites.AddAsync(place.Name,place.Data.Point,cancellationToken);
        this.CanAddFavorite=false;
    }

    [RelayCommand]
    private Task OpenSearchAsync(){
        return this.navigator.GoToAsync(Routes.Search);
    }

    [RelayCommand]
    private Task OpenPlacesAsync(){
        return this.navigator.GoToAsync(Routes.Places);
    }

    [RelayCommand]
    private Task OpenSettingsAsync(){
        return this.navigator.GoToAsync(Routes.Settings);
    }

    /// <summary>現在地へ移動する。</summary>
    [RelayCommand]
    private async Task LocateAsync(CancellationToken cancellationToken){
        if(await this.location.RequestPermissionAsync()!=LocationStatus.Granted){
            return;
        }
        if(await this.location.GetCurrentLocationAsync(cancellationToken) is {} point){
            this.FocusRequested?.Invoke(this,new MapFocusRequest(point,Strings.CurrentLocation));
        }
    }

    /// <summary>検索で選んだ地点: 地図をその地点へ動かし、予報シートを開く。</summary>
    private async Task ShowPlaceAsync(MapFocusRequest request,CancellationToken cancellationToken){
        this.FocusRequested?.Invoke(this,request);
        this.SelectedPoint=request.Point;
        this.probeName=request.Name;
        this.ProbeTitle=request.Name;
        var resolved=await this.weather.ResolveAsync(request.Point,cancellationToken);
        this.CanAddFavorite=resolved.JmaArea is not {Kind:JmaAreaMatchKind.OutOfCoverage};
        await this.OpenForecastAsync(cancellationToken);
    }

    private async void OnFocusRequested(object? sender,MapFocusRequest request){
        this.focus.Take();
        await this.ShowPlaceAsync(request,CancellationToken.None);
    }

    private async void OnFavoritesChanged(object? sender,EventArgs e){
        await this.places.LoadAsync(CancellationToken.None);
        this.UpdatePins();
    }

    /// <summary>ピンの文言(地点名と気温)。お気に入りの予報が更新されたら呼ぶ。</summary>
    public void UpdatePins(){
        this.Pins=[..this.places.Places.Select(static p=>new MapPinItem(p.Data.Point,Label(p),p))];
    }

    private static string Label(PlaceWeatherViewModel place){
        if(place.CurrentTemperature=="--"){
            return place.Name;
        }
        return place.Name+" "+place.CurrentTemperature;
    }

    /// <summary>表示中のループ(お気に入りの予報の 10 分ごとの更新)。地図の表示時に開始し、非表示で止める。</summary>
    public void Start(){
        this.places.Start();
    }

    public void Stop(){
        this.places.Stop();
        this.StopPlaying();
    }

    /// <summary>お気に入りの予報が更新されたらピンの気温を更新する。</summary>
    private void OnPlaceUpdated(object? sender,string placeId){
        if(this.places.Places.Any(p=>p.Data.PlaceId==placeId)){
            this.UpdatePins();
        }
    }

    private void OnStopped(object? sender,EventArgs e){
        this.StopPlaying();
    }

    private async void OnResumed(object? sender,EventArgs e){
        if(this.time.GetUtcNow()-this.availabilityAt>=TimeSpan.FromMinutes(10)){
            await this.RefreshTimelineAsync(CancellationToken.None);
        }
        this.UpdatePins();
    }

    private static int Nearest(IReadOnlyList<DateTimeOffset> steps,DateTimeOffset target){
        var best=0;
        var bestDistance=TimeSpan.MaxValue;
        for(var i=0;i<steps.Count;i++){
            var d=(steps[i]-target).Duration();
            if(d<bestDistance){
                best=i;
                bestDistance=d;
            }
        }
        return best;
    }

    public void Dispose(){
        this.focus.Requested-=this.OnFocusRequested;
        this.favorites.Changed-=this.OnFavoritesChanged;
        this.lifecycle.Stopped-=this.OnStopped;
        this.lifecycle.Resumed-=this.OnResumed;
        this.session.PlaceUpdated-=this.OnPlaceUpdated;
        this.StopPlaying();
        this.frameLoad?.Cancel();
        this.frameLoad?.Dispose();
    }
}
