using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Weather.Core;
using Weather.Presentation.Resources;

namespace Weather.Presentation.ViewModels;

/// <summary>お気に入りの追加(検索・地図の共通処理)。観測所は地点の追加時に最寄りを自動で選ぶ(History.md)。</summary>
public sealed class FavoritesService(IFavoritesStore store,IWeatherService weather){
    public event EventHandler? Changed;

    public async Task<FavoritePlace> AddAsync(string name,GeoPoint point,CancellationToken cancellationToken){
        var all=await store.GetAllAsync(cancellationToken);
        var id="p"+Guid.NewGuid().ToString("N")[..12];
        string? station=null;
        try{
            var location=await weather.ResolveAsync(point,cancellationToken);
            if(weather.GetObservationAvailability(location)==Availability.Available){
                var stations=await weather.FindStationsAsync(location,1,cancellationToken);
                if(stations.Count>0){
                    station=stations[0].Key;
                }
            }
        }catch(WeatherProviderException){
            //観測所は後から選べるため、追加自体は続ける
        }
        var order=0;
        if(all.Count>0){
            order=all.Max(static f=>f.SortOrder)+1;
        }
        var place=new FavoritePlace(id,name,point,order){StationKey=station};
        await store.SaveAsync(place,cancellationToken);
        this.Changed?.Invoke(this,EventArgs.Empty);
        return place;
    }

    public void NotifyChanged(){
        this.Changed?.Invoke(this,EventArgs.Empty);
    }
}

/// <summary>地点検索(オフライン)。入力のたびに検索し、選んだ候補を地図に出して予報シートを開く(お気に入りへの追加はシートから)。</summary>
public sealed partial class SearchViewModel(IPlaceSearch search,FavoritesService favorites,ILocationService location,IWeatherService weather,INavigator navigator,IDialogService dialogs,MapFocusService focus):ObservableObject{
    public ObservableCollection<PlaceSuggestion> Results{get;}=[];
    [ObservableProperty]public partial string Query{get;set;}="";
    [ObservableProperty]public partial bool IsBusy{get;set;}
    [ObservableProperty]public partial string? Message{get;set;}

    async partial void OnQueryChanged(string value){
        await this.SearchAsync(value);
    }

    public async Task SearchAsync(string query){
        var results=await search.SearchAsync(query,30,CancellationToken.None);
        this.Results.Clear();
        foreach(var r in results){
            this.Results.Add(r);
        }
        this.Message=null;
        if(!string.IsNullOrWhiteSpace(query)&&results.Count==0){
            this.Message=Strings.NotFound;
        }
    }

    [RelayCommand]
    private async Task SelectAsync(PlaceSuggestion? suggestion){
        if(suggestion is null){
            return;
        }
        focus.Request(suggestion.Point,suggestion.Name);
        await navigator.GoBackAsync();
    }

    [RelayCommand]
    private async Task UseCurrentLocationAsync(){
        if(await location.RequestPermissionAsync()!=LocationStatus.Granted){
            await dialogs.AlertAsync(Strings.LocationTitle,Strings.LocationDeniedDetail);
            return;
        }
        this.IsBusy=true;
        try{
            var point=await location.GetCurrentLocationAsync(CancellationToken.None);
            if(point is not {} p){
                this.Message=Strings.CurrentLocationFailed;
                return;
            }
            var resolved=await weather.ResolveAsync(p,CancellationToken.None);
            await favorites.AddAsync(resolved.DisplayName,p,CancellationToken.None);
            await navigator.GoBackAsync();
        }finally{
            this.IsBusy=false;
        }
    }
}

/// <summary>お気に入りの管理(並べ替え・削除)。</summary>
public sealed partial class PlacesViewModel(IFavoritesStore store,FavoritesService favorites,IDialogService dialogs,INavigator navigator):ObservableObject{
    public ObservableCollection<FavoritePlace> Items{get;}=[];

    [RelayCommand]
    public async Task LoadAsync(){
        this.Items.Clear();
        foreach(var f in await store.GetAllAsync(CancellationToken.None)){
            this.Items.Add(f);
        }
    }

    [RelayCommand]
    private async Task MoveUpAsync(FavoritePlace? place){
        await this.MoveAsync(place,-1);
    }

    [RelayCommand]
    private async Task MoveDownAsync(FavoritePlace? place){
        await this.MoveAsync(place,1);
    }

    [RelayCommand]
    private async Task DeleteAsync(FavoritePlace? place){
        if(place is null){
            return;
        }
        if(!await dialogs.ConfirmAsync(Strings.DeletePlaceTitle,string.Format(System.Globalization.CultureInfo.CurrentCulture,Strings.DeletePlaceFormat,place.DisplayName),Strings.Delete,Strings.Cancel)){
            return;
        }
        await store.DeleteAsync(place.Id,CancellationToken.None);
        this.Items.Remove(place);
        favorites.NotifyChanged();
    }

    [RelayCommand]
    private Task AddAsync(){
        return navigator.GoToAsync(Routes.Search);
    }

    private async Task MoveAsync(FavoritePlace? place,int delta){
        if(place is null){
            return;
        }
        var index=this.Items.IndexOf(place);
        var target=index+delta;
        if(index<0||target<0||target>=this.Items.Count){
            return;
        }
        this.Items.Move(index,target);
        await store.ReorderAsync([..this.Items.Select(static i=>i.Id)],CancellationToken.None);
        favorites.NotifyChanged();
    }
}
