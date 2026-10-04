using Weather.Presentation.ViewModels;

namespace Weather.App.Views;

public partial class SettingsPage:ContentPage{
    private readonly AdSettingsViewModel ads;

    public SettingsPage(SettingsViewModel viewModel,AdSettingsViewModel ads){
        this.InitializeComponent();
        this.BindingContext=viewModel;
        this.ads=ads;
        this.AdsSection.BindingContext=ads;
    }

    protected override async void OnAppearing(){
        base.OnAppearing();
        //価格はストアから取得する(取得できなければ購入のボタンを出さない)
        await this.ads.LoadAsync(CancellationToken.None);
    }
}
