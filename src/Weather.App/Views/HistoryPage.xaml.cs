using Weather.Presentation.ViewModels;

namespace Weather.App.Views;

public partial class HistoryPage:ContentPage,IQueryAttributable{
    private readonly HistoryViewModel viewModel;

    public HistoryPage(HistoryViewModel viewModel){
        this.InitializeComponent();
        this.viewModel=viewModel;
        this.BindingContext=viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string,object> query){
        QueryForwarder.Forward(this,query);
        this.Chart.Zone=this.viewModel.Zone;
    }
}
