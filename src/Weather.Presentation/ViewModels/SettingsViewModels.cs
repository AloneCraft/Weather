using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Weather.Core;
using Weather.Presentation.Background;
using Weather.Presentation.Resources;

namespace Weather.Presentation.ViewModels;

/// <summary>設定(Screens.md「設定」)。</summary>
public sealed partial class SettingsViewModel:ObservableObject{
    private readonly AppSettings settings;
    private readonly IObservationHistoryService history;
    private readonly IDialogService dialogs;
    private readonly INavigator navigator;
    private readonly IAlertNotifier notifier;
    private readonly IBackgroundScheduler scheduler;

    public SettingsViewModel(AppSettings settings,IObservationHistoryService history,IDialogService dialogs,INavigator navigator,IAlertNotifier notifier,IBackgroundScheduler scheduler){
        this.settings=settings;
        this.history=history;
        this.dialogs=dialogs;
        this.navigator=navigator;
        this.notifier=notifier;
        this.scheduler=scheduler;
        this.AlertNotifications=settings.AlertNotifications;
        this.UseImperial=settings.Units==UnitSystem.Imperial;
        this.QualityIndex=(int)settings.Quality;
        this.ReduceFlashes=settings.ReduceFlashes;
        this.PowerSaving=settings.PowerSaving;
        this.RetentionIndex=Array.IndexOf(RetentionDays,settings.HistoryRetentionDays);
        if(this.RetentionIndex<0){
            this.RetentionIndex=1;
        }
    }

    public static IReadOnlyList<string> QualityOptions{get;}=[Strings.QualityAuto,Strings.QualityLow,Strings.QualityMedium,Strings.QualityHigh];
    public static IReadOnlyList<string> RetentionOptions{get;}=[Strings.Retention30Days,Strings.Retention1Year,Strings.Retention3Years];
    private static readonly int[] RetentionDays=[30,365,1095];

    [ObservableProperty]public partial bool UseImperial{get;set;}
    [ObservableProperty]public partial int QualityIndex{get;set;}
    [ObservableProperty]public partial bool ReduceFlashes{get;set;}
    [ObservableProperty]public partial bool PowerSaving{get;set;}
    [ObservableProperty]public partial int RetentionIndex{get;set;}
    [ObservableProperty]public partial bool AlertNotifications{get;set;}

    //変更ハンドラーは、値が保存済みの値(未保存なら既定値)と同じなら何もしない。
    //コンストラクターで画面に初期値を入れるときにも呼ばれるため、比較しないと画面を開いただけで既定値が保存され、
    //後で端末の地域を変えても単位が追従しなくなる(2026-10-04 にエミュレーターで確認した不具合)
    partial void OnUseImperialChanged(bool value){
        var units=UnitSystem.Metric;
        if(value){
            units=UnitSystem.Imperial;
        }
        if(units==this.settings.Units){
            return;
        }
        this.settings.Units=units;
    }

    partial void OnQualityIndexChanged(int value){
        if(value<0||value>=QualityOptions.Count||(RenderQuality)value==this.settings.Quality){
            return;
        }
        this.settings.Quality=(RenderQuality)value;
    }

    partial void OnReduceFlashesChanged(bool value){
        if(value==this.settings.ReduceFlashes){
            return;
        }
        this.settings.ReduceFlashes=value;
    }

    partial void OnPowerSavingChanged(bool value){
        if(value==this.settings.PowerSaving){
            return;
        }
        this.settings.PowerSaving=value;
    }

    /// <summary>有効にするときに通知の許可を求める。許可されなければ無効に戻す。</summary>
    async partial void OnAlertNotificationsChanged(bool value){
        if(value==this.settings.AlertNotifications){
            return;
        }
        if(value&&!await this.notifier.RequestPermissionAsync()){
            this.AlertNotifications=false;
            await this.dialogs.AlertAsync(Strings.AlertNotificationsLabel,Strings.NotificationPermissionDenied);
            return;
        }
        this.settings.AlertNotifications=value;
        if(value){
            this.scheduler.Schedule();
        }
    }

    async partial void OnRetentionIndexChanged(int value){
        if(value<0||value>=RetentionDays.Length||RetentionDays[value]==this.settings.HistoryRetentionDays){
            return;
        }
        this.settings.HistoryRetentionDays=RetentionDays[value];
        await this.history.ApplyRetentionAsync(TimeSpan.FromDays(RetentionDays[value]),CancellationToken.None);
    }

    [RelayCommand]
    private async Task DeleteHistoryAsync(){
        if(!await this.dialogs.ConfirmAsync(Strings.DeleteHistoryTitle,Strings.DeleteHistoryConfirm,Strings.Delete,Strings.Cancel)){
            return;
        }
        await this.history.DeleteAllAsync(CancellationToken.None);
        await this.dialogs.AlertAsync(Strings.DeleteHistoryTitle,Strings.Deleted);
    }

    [RelayCommand]
    private Task OpenAboutAsync(){
        return this.navigator.GoToAsync(Routes.About);
    }
}

public sealed record LicenseItem(string Name,string Usage,string License,string Url);

/// <summary>データと出典(Screens.md「データと出典」)。</summary>
public sealed partial class AboutViewModel:ObservableObject{
    public ObservableCollection<LicenseItem> Items{get;}=[
        new("気象庁 / Japan Meteorological Agency",Strings.AboutJmaUsage,Strings.AboutJmaLicense,"https://www.jma.go.jp/jma/kishou/info/coment.html"),
        new("National Weather Service",Strings.AboutNwsUsage,Strings.AboutNwsLicense,"https://www.weather.gov/disclaimer"),
        new("MET Norway",Strings.AboutMetUsage,"CC BY 4.0 / NLOD 2.0","https://api.met.no/doc/License"),
        new("GeoNames",Strings.AboutGeoNamesUsage,"CC BY 4.0","https://www.geonames.org/"),
        new("Natural Earth",Strings.AboutNaturalEarthUsage,Strings.AboutPublicDomain,"https://www.naturalearthdata.com/"),
    ];

    public string Privacy{get;}=Strings.Privacy;

    public string Law{get;}=Strings.Law;
}
