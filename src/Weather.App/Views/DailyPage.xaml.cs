using Weather.Presentation.ViewModels;

namespace Weather.App.Views;

public partial class DailyPage:ContentPage,IQueryAttributable{
    public DailyPage(DailyDetailViewModel viewModel){
        this.InitializeComponent();
        this.BindingContext=viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string,object> query){
        QueryForwarder.Forward(this,query);
    }
}
