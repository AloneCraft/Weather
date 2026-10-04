using Foundation;

namespace Weather.App;

[Register("AppDelegate")]
public class AppDelegate:MauiUIApplicationDelegate{
    protected override MauiApp CreateMauiApp(){
        return MauiProgram.CreateMauiApp();
    }
}
