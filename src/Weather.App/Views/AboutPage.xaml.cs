using Weather.Presentation.ViewModels;

namespace Weather.App.Views;

public partial class AboutPage:ContentPage{
    public AboutPage(AboutViewModel viewModel){
        this.InitializeComponent();
        this.BindingContext=viewModel;
    }
}
