using Weather.App.Services;
using Weather.Core;
using Weather.Presentation;
using Weather.Presentation.Background;
using Weather.Providers;

namespace Weather.App;

public partial class App:Application{
    private readonly AppShell shell;
    private readonly AppLifecycle lifecycle;
    private readonly IServiceProvider services;

    public App(AppShell shell,AppLifecycle lifecycle,IServiceProvider services){
        this.InitializeComponent();
        this.shell=shell;
        this.lifecycle=lifecycle;
        this.services=services;
    }

    protected override Window CreateWindow(IActivationState? activationState){
        var window=new Window(this.shell){Title="空模様"};
        this.lifecycle.OnWindowCreated();
        //背景更新の予約(既に予約済みなら何もしない。通知もウィジェットもなければ実行時に何も取得しない)
        this.services.GetRequiredService<IBackgroundScheduler>().Schedule();
        window.Resumed+=(_,_)=>this.lifecycle.OnResumed();
        window.Activated+=(_,_)=>this.lifecycle.OnResumed();
        window.Stopped+=(_,_)=>this.lifecycle.OnStopped();
        window.Created+=async (_,_)=>await this.MaintainAsync();
        return window;
    }

    /// <summary>起動時の保守: HTTP キャッシュの容量管理と観測履歴の保持期間の適用。</summary>
    private async Task MaintainAsync(){
        try{
            await this.services.GetRequiredService<HttpCacheMaintenance>().TrimAsync(CancellationToken.None);
            var settings=this.services.GetRequiredService<AppSettings>();
            await this.services.GetRequiredService<IObservationHistoryService>().ApplyRetentionAsync(TimeSpan.FromDays(settings.HistoryRetentionDays),CancellationToken.None);
        }catch(IOException ex){
            System.Diagnostics.Debug.WriteLine($"起動時の保守に失敗しました: {ex.Message}");
        }
    }
}
