using CommunityToolkit.Mvvm.Input;
using Weather.Core;
using Weather.Presentation.ViewModels;
using Weather.Rendering.Map;

namespace Weather.App.Views;

public partial class MapPage:ContentPage{
    private readonly MapPickerViewModel viewModel;

    public MapPage(MapPickerViewModel viewModel){
        this.InitializeComponent();
        this.viewModel=viewModel;
        this.BindingContext=viewModel;
        this.Map.TapCommand=new AsyncRelayCommand<GeoPoint>(this.OnTappedAsync);
    }

    protected override async void OnAppearing(){
        base.OnAppearing();
        await this.viewModel.LoadAsync();
        this.UpdateMarkers();
        if(this.viewModel.Favorites.Count>0){
            this.Map.CenterOn(this.viewModel.Favorites[0].Point,4);
        }
    }

    private async Task OnTappedAsync(GeoPoint point){
        await this.viewModel.SelectAsync(point);
        this.UpdateMarkers();
    }

    private void UpdateMarkers(){
        var markers=new List<MapMarker>();
        foreach(var f in this.viewModel.Favorites){
            markers.Add(new MapMarker(f.Point,f.DisplayName,false));
        }
        if(this.viewModel.Selected is {} selected){
            markers.Add(new MapMarker(selected,this.viewModel.SelectedName??"",true));
        }
        this.Map.Markers=markers;
    }

    private void OnZoomIn(object? sender,EventArgs e){
        this.Map.ZoomBy(2);
    }

    private void OnZoomOut(object? sender,EventArgs e){
        this.Map.ZoomBy(0.5);
    }
}
