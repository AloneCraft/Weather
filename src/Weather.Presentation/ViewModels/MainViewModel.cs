using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Weather.Core;
using Weather.Presentation.Background;
using Weather.Presentation.Resources;

namespace Weather.Presentation.ViewModels;

/// <summary>
/// お気に入り地点(と現在地)の一覧と予報。地図のピン・予報シート・ウィジェットが使う(Screens.md「地図」)。
/// 表示中は 10 分ごとに自動更新し、背景では止める。シーンは 1 分ごとに現在時刻で更新する。
/// </summary>
public sealed partial class MainViewModel:ObservableObject,IDisposable{
    public const string CurrentLocationId="current";
    private static readonly TimeSpan RefreshInterval=TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SceneInterval=TimeSpan.FromMinutes(1);

    private readonly IFavoritesStore favorites;
    private readonly ILocationService location;
    private readonly IAppLifecycle lifecycle;
    private readonly INavigator navigator;
    private readonly TimeProvider time;
    private readonly Func<PlaceData,PlaceWeatherViewModel> createPlace;
    private readonly WidgetUpdater widget;
    private CancellationTokenSource? loop;
    private DateTimeOffset lastRefresh;

    public MainViewModel(IFavoritesStore favorites,ILocationService location,IAppLifecycle lifecycle,INavigator navigator,TimeProvider time,Func<PlaceData,PlaceWeatherViewModel> createPlace,WidgetUpdater widget){
        this.favorites=favorites;
        this.location=location;
        this.lifecycle=lifecycle;
        this.navigator=navigator;
        this.time=time;
        this.createPlace=createPlace;
        this.widget=widget;
        lifecycle.Resumed+=this.OnResumed;
        lifecycle.Stopped+=this.OnStopped;
    }

    public ObservableCollection<PlaceWeatherViewModel> Places{get;}=[];

    [ObservableProperty]public partial int CurrentIndex{get;set;}
    [ObservableProperty]public partial PlaceWeatherViewModel? Current{get;set;}
    [ObservableProperty]public partial bool IsEmpty{get;set;}
    [ObservableProperty]public partial string? LocationMessage{get;set;}

    partial void OnCurrentIndexChanged(int value){
        if(value>=0&&value<this.Places.Count){
            this.Current=this.Places[value];
        }
    }

    /// <summary>
    /// お気に入りと現在地を読み込む。現在地は権限があれば先頭に置く。
    /// 2 回目以降(お気に入りの変更後)は既存の地点の VM を使い回し、追加された地点だけ取得する
    /// (全項目を入れ替えると、読み込み中のスワイプでカルーセルが途中で止まる。取得済みのデータも捨てない)。
    /// </summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken){
        var list=new List<PlaceData>();
        var status=await this.location.RequestPermissionAsync();
        if(status==LocationStatus.Granted){
            var point=await this.location.GetCurrentLocationAsync(cancellationToken);
            if(point is {} p){
                list.Add(new PlaceData{PlaceId=CurrentLocationId,Name=Strings.CurrentLocation,Point=p,IsCurrentLocation=true});
                this.LocationMessage=null;
            }else{
                this.LocationMessage=Strings.CurrentLocationFailed;
            }
        }else{
            this.LocationMessage=Strings.LocationDenied;
        }
        foreach(var f in await this.favorites.GetAllAsync(cancellationToken)){
            list.Add(new PlaceData{PlaceId=f.Id,Name=f.DisplayName,Point=f.Point,StationKey=f.StationKey});
        }
        var existing=this.Places.ToDictionary(static p=>p.Data.PlaceId,StringComparer.Ordinal);
        var next=new List<PlaceWeatherViewModel>(list.Count);
        var added=new List<PlaceWeatherViewModel>();
        foreach(var data in list){
            if(existing.TryGetValue(data.PlaceId,out var reused)&&reused.Data.Point==data.Point&&reused.Name==data.Name){
                next.Add(reused);
            }else{
                var created=this.createPlace(data);
                next.Add(created);
                added.Add(created);
            }
        }
        Synchronize(this.Places,next);
        this.IsEmpty=this.Places.Count==0;
        if(this.Places.Count>0){
            this.CurrentIndex=Math.Clamp(this.CurrentIndex,0,this.Places.Count-1);
            this.Current=this.Places[this.CurrentIndex];
        }
        if(added.Count==next.Count){
            await this.RefreshAllAsync(cancellationToken);
            return;
        }
        if(!this.lifecycle.IsForeground){
            return;
        }
        foreach(var place in added){
            await place.RefreshAsync(cancellationToken);
        }
        this.widget.Publish(this.Places.Select(static p=>p.Data));
    }

    /// <summary>項目を入れ替えずに、削除・挿入・移動だけで並びを合わせる。</summary>
    private static void Synchronize(ObservableCollection<PlaceWeatherViewModel> target,List<PlaceWeatherViewModel> next){
        for(var i=target.Count-1;i>=0;i--){
            if(!next.Contains(target[i])){
                target.RemoveAt(i);
            }
        }
        for(var i=0;i<next.Count;i++){
            var index=target.IndexOf(next[i]);
            if(index<0){
                target.Insert(i,next[i]);
            }else if(index!=i){
                target.Move(index,i);
            }
        }
    }

    [RelayCommand]
    public async Task RefreshAllAsync(CancellationToken cancellationToken){
        if(!this.lifecycle.IsForeground){
            return;
        }
        this.lastRefresh=this.time.GetUtcNow();
        foreach(var place in this.Places.ToList()){
            await place.RefreshAsync(cancellationToken);
        }
        //前面で取得した内容をウィジェットに反映する(MET の地点のウィジェットはこの経路でだけ更新される)
        this.widget.Publish(this.Places.Select(static p=>p.Data));
    }

    /// <summary>表示中のループ(自動更新とシーンの時刻更新)。View の表示時に開始し、非表示・背景で止める。</summary>
    public void Start(){
        this.Stop();
        this.loop=new CancellationTokenSource();
        _=this.RunAsync(this.loop.Token);
    }

    public void Stop(){
        this.loop?.Cancel();
        this.loop?.Dispose();
        this.loop=null;
    }

    private async Task RunAsync(CancellationToken cancellationToken){
        using var timer=new PeriodicTimer(SceneInterval,this.time);
        try{
            while(await timer.WaitForNextTickAsync(cancellationToken)){
                foreach(var place in this.Places){
                    place.UpdateScene();
                }
                if(this.time.GetUtcNow()-this.lastRefresh>=RefreshInterval){
                    await this.RefreshAllAsync(cancellationToken);
                }
            }
        }catch(OperationCanceledException){
            //ループの停止(非表示・背景)
        }
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

    private void OnResumed(object? sender,EventArgs e){
        if(this.time.GetUtcNow()-this.lastRefresh>=RefreshInterval){
            _=this.RefreshAllAsync(CancellationToken.None);
        }
        this.Start();
    }

    private void OnStopped(object? sender,EventArgs e){
        this.Stop();
    }

    public void Dispose(){
        this.lifecycle.Resumed-=this.OnResumed;
        this.lifecycle.Stopped-=this.OnStopped;
        this.Stop();
    }
}
