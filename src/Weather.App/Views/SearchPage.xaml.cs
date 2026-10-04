using Weather.Presentation.ViewModels;

namespace Weather.App.Views;

public partial class SearchPage:ContentPage{
    public SearchPage(SearchViewModel viewModel){
        this.InitializeComponent();
        this.BindingContext=viewModel;
    }
}
