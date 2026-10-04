using Weather.Core;
using Weather.Presentation;
using Weather.Presentation.Monetization;
using Weather.Presentation.ViewModels;

namespace Weather.App.Views;

/// <summary>トップ画面(地図)。地図の操作は WeatherMapView、表示内容は MapViewModel。</summary>
public partial class MapPage:ContentPage{
    private readonly MapViewModel viewModel;
    private readonly AppSettings settings;
    private readonly IMotionPreferences motion;
    private readonly AdPolicy ads;
    private const double FocusZoom=6.5;
    private bool loaded;

    public MapPage(MapViewModel viewModel,AppSettings settings,IMotionPreferences motion,AdPolicy ads){
        this.InitializeComponent();
        this.viewModel=viewModel;
        this.settings=settings;
        this.motion=motion;
        this.ads=ads;
        this.BindingContext=viewModel;
        this.Map.PointTapped+=this.OnPointTapped;
        this.Map.PinTapped+=(_,pin)=>viewModel.SelectPinCommand.Execute(pin);
        viewModel.FocusRequested+=this.OnFocusRequested;
    }

    protected override async void OnAppearing(){
        base.OnAppearing();
        this.Map.SetActive(true,this.settings,this.motion);
        if(!this.loaded){
            this.loaded=true;
            await this.viewModel.LoadAsync(CancellationToken.None);
        }
        this.viewModel.UpdatePins();
        this.viewModel.Start();
        //地図を出してから、購入の確認と広告の同意(必要な地域だけフォームを地図の上に出す)。2 回目以降は何もしない
        await this.ads.StartAsync(CancellationToken.None);
    }

    protected override void OnDisappearing(){
        //非表示では描画ループを止める(Rendering.md「描画ホスト」)
        this.Map.SetActive(false,this.settings,this.motion);
        this.viewModel.Stop();
        base.OnDisappearing();
    }

    private async void OnPointTapped(object? sender,GeoPoint point){
        await this.viewModel.TapAsync(point,CancellationToken.None);
    }

    private void OnFocusRequested(object? sender,MapFocusRequest request){
        this.Map.FocusOn(request.Point,FocusZoom);
    }

    private async void OnAttributionTapped(object? sender,TappedEventArgs e){
        await Shell.Current.GoToAsync(Routes.About);
    }
}
