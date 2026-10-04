using Weather.Presentation.ViewModels;

namespace Weather.App.Views;

public partial class AlertDetailPage:ContentPage,IQueryAttributable{
    public AlertDetailPage(AlertDetailViewModel viewModel){
        this.InitializeComponent();
        this.BindingContext=viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string,object> query){
        QueryForwarder.Forward(this,query);
    }
}
