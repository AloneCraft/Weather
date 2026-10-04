using Weather.Presentation.ViewModels;

namespace Weather.App.Views;

public partial class AlertsPage:ContentPage,IQueryAttributable{
    public AlertsPage(AlertsViewModel viewModel){
        this.InitializeComponent();
        this.BindingContext=viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string,object> query){
        QueryForwarder.Forward(this,query);
    }
}
