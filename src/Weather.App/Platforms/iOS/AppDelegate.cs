using Foundation;
using UIKit;
using Weather.App.Background;

namespace Weather.App;

[Register("AppDelegate")]
public class AppDelegate:MauiUIApplicationDelegate{
    protected override MauiApp CreateMauiApp(){
        return MauiProgram.CreateMauiApp();
    }

    public override bool FinishedLaunching(UIApplication application,NSDictionary? launchOptions){
        //BGTaskScheduler の登録は起動処理が終わる前に行う必要がある
        IosBackgroundPlatform.Register();
        return base.FinishedLaunching(application,launchOptions);
    }
}
