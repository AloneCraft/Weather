using Weather.App.Views;
using Weather.Presentation;

namespace Weather.App;

public partial class AppShell:Shell{
    public AppShell(){
        this.InitializeComponent();
        Routing.RegisterRoute(Routes.Hourly,typeof(HourlyPage));
        Routing.RegisterRoute(Routes.Daily,typeof(DailyPage));
        Routing.RegisterRoute(Routes.Alerts,typeof(AlertsPage));
        Routing.RegisterRoute(Routes.Alert,typeof(AlertDetailPage));
        Routing.RegisterRoute(Routes.Sources,typeof(SourcesPage));
        Routing.RegisterRoute(Routes.History,typeof(HistoryPage));
        Routing.RegisterRoute(Routes.Search,typeof(SearchPage));
        Routing.RegisterRoute(Routes.PlaceDetail,typeof(PlaceDetailPage));
        Routing.RegisterRoute(Routes.Places,typeof(PlacesPage));
        Routing.RegisterRoute(Routes.Settings,typeof(SettingsPage));
        Routing.RegisterRoute(Routes.About,typeof(AboutPage));
    }
}
