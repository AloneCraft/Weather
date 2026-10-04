using Weather.Presentation.ViewModels;

namespace Weather.App.Views;

public partial class SettingsPage:ContentPage{
    public SettingsPage(SettingsViewModel viewModel){
        this.InitializeComponent();
        this.BindingContext=viewModel;
    }
}
