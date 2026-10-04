using Weather.Presentation.ViewModels;

namespace Weather.App.Views;

public partial class HourlyPage:ContentPage,IQueryAttributable{
    private readonly HourlyViewModel viewModel;

    public HourlyPage(HourlyViewModel viewModel){
        this.InitializeComponent();
        this.viewModel=viewModel;
        this.BindingContext=viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string,object> query){
        QueryForwarder.Forward(this,query);
        this.Chart.Zone=this.viewModel.Zone;
    }

    protected override void OnAppearing(){
        base.OnAppearing();
        this.Scene.IsRunning=true;
    }

    protected override void OnDisappearing(){
        this.Scene.IsRunning=false;
        base.OnDisappearing();
    }
}
