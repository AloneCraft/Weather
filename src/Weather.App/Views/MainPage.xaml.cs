using Weather.Presentation.ViewModels;

namespace Weather.App.Views;

public partial class MainPage:ContentPage{
    private readonly MainViewModel viewModel;
    private bool loaded;
    private bool reloadRequested;

    public MainPage(MainViewModel viewModel,FavoritesService favorites){
        this.InitializeComponent();
        this.viewModel=viewModel;
        this.BindingContext=viewModel;
        favorites.Changed+=(_,_)=>this.reloadRequested=true;
    }

    protected override async void OnAppearing(){
        base.OnAppearing();
        this.Scene.IsRunning=true;
        if(!this.loaded||this.reloadRequested){
            this.loaded=true;
            this.reloadRequested=false;
            await this.viewModel.LoadAsync(CancellationToken.None);
        }
        this.viewModel.Start();
    }

    protected override void OnDisappearing(){
        //非表示では描画ループと自動更新を止める(Rendering.md「描画ホスト」)
        this.Scene.IsRunning=false;
        this.viewModel.Stop();
        base.OnDisappearing();
    }
}
