using Weather.Presentation.ViewModels;

namespace Weather.App.Views;

/// <summary>地点の詳細(空のシーン)。表示する地点は地図の予報シートで選んだ地点。</summary>
public partial class PlaceDetailPage:ContentPage{
    private readonly MapViewModel map;

    public PlaceDetailPage(MapViewModel map){
        this.InitializeComponent();
        this.map=map;
    }

    protected override void OnAppearing(){
        base.OnAppearing();
        this.BindingContext=this.map.SheetPlace;
        this.map.SheetPlace?.UpdateScene();
        this.Scene.IsRunning=true;
    }

    protected override void OnDisappearing(){
        this.Scene.IsRunning=false;
        base.OnDisappearing();
    }

    private async void OnBackClicked(object? sender,EventArgs e){
        await Shell.Current.GoToAsync("..");
    }
}
