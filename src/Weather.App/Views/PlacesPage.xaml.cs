using Weather.Presentation.ViewModels;

namespace Weather.App.Views;

public partial class PlacesPage:ContentPage{
    private readonly PlacesViewModel viewModel;

    public PlacesPage(PlacesViewModel viewModel){
        this.InitializeComponent();
        this.viewModel=viewModel;
        this.BindingContext=viewModel;
    }

    protected override async void OnAppearing(){
        base.OnAppearing();
        await this.viewModel.LoadAsync();
    }
}
