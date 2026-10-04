using Weather.Presentation.ViewModels;

namespace Weather.App.Views;

public partial class SourcesPage:ContentPage,IQueryAttributable{
    public SourcesPage(SourcesViewModel viewModel){
        this.InitializeComponent();
        this.BindingContext=viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string,object> query){
        QueryForwarder.Forward(this,query);
    }
}
