using Weather.Core;
using Weather.Presentation;

namespace Weather.App.Services;

/// <summary>Shell による画面遷移。引数は文字列の辞書で渡し、ページ基底が VM へ転送する。</summary>
public sealed class MauiNavigator:INavigator{
    public Task GoToAsync(string route,IReadOnlyDictionary<string,string>? parameters=null){
        var shell=Shell.Current;
        if(shell is null){
            return Task.CompletedTask;
        }
        if(parameters is null||parameters.Count==0){
            return shell.GoToAsync(route);
        }
        var query=new ShellNavigationQueryParameters();
        foreach(var (key,value) in parameters){
            query[key]=value;
        }
        return shell.GoToAsync(route,query);
    }

    public Task GoBackAsync(){
        var shell=Shell.Current;
        if(shell is null){
            return Task.CompletedTask;
        }
        return shell.GoToAsync("..");
    }
}

public sealed class MauiDialogService:IDialogService{
    public Task AlertAsync(string title,string message){
        var page=Shell.Current?.CurrentPage;
        if(page is null){
            return Task.CompletedTask;
        }
        return page.DisplayAlertAsync(title,message,"OK");
    }

    public Task<bool> ConfirmAsync(string title,string message,string accept,string cancel){
        var page=Shell.Current?.CurrentPage;
        if(page is null){
            return Task.FromResult(false);
        }
        return page.DisplayAlertAsync(title,message,accept,cancel);
    }
}

/// <summary>現在地(MAUI Geolocation)。最後の既知位置を優先し、なければ現在位置を取得する。</summary>
public sealed class MauiLocationService:ILocationService{
    public async Task<LocationStatus> RequestPermissionAsync(){
        try{
            var status=await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if(status!=PermissionStatus.Granted){
                status=await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.LocationWhenInUse>);
            }
            if(status==PermissionStatus.Granted){
                return LocationStatus.Granted;
            }
            return LocationStatus.Denied;
        }catch(FeatureNotSupportedException){
            return LocationStatus.Unavailable;
        }
    }

    public async Task<GeoPoint?> GetCurrentLocationAsync(CancellationToken cancellationToken){
        try{
            var location=await Geolocation.Default.GetLastKnownLocationAsync();
            location??=await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium,TimeSpan.FromSeconds(10)),cancellationToken);
            if(location is null){
                return null;
            }
            return new GeoPoint(location.Latitude,location.Longitude);
        }catch(Exception ex) when(ex is FeatureNotSupportedException or FeatureNotEnabledException or PermissionException){
            return null;
        }
    }
}

/// <summary>
/// アプリの前面/背景。App の Window イベントから更新する。
/// 背景更新だけでプロセスが起動した場合(Android の WorkManager・iOS の BGTaskScheduler)は前面にならないため、
/// MET の規約(使用中以外は取得しない)を Provider 側でも守れる(IAppActivity)。
/// </summary>
public sealed class AppLifecycle:IAppLifecycle,IAppActivity{
    public bool IsForeground{get;private set;}
    public bool IsInUse=>this.IsForeground;
    public event EventHandler? Resumed;
    public event EventHandler? Stopped;

    /// <summary>画面の作成時。iOS は背景で起動された場合も Window が作られるため、アプリの状態で判定する。</summary>
    public void OnWindowCreated(){
#if IOS
        if(UIKit.UIApplication.SharedApplication.ApplicationState==UIKit.UIApplicationState.Background){
            return;
        }
#endif
        this.IsForeground=true;
    }

    public void OnResumed(){
        this.IsForeground=true;
        this.Resumed?.Invoke(this,EventArgs.Empty);
    }

    public void OnStopped(){
        this.IsForeground=false;
        this.Stopped?.Invoke(this,EventArgs.Empty);
    }
}

public sealed class PreferencesSettingsStore:ISettingsStore{
    public string? Get(string key){
        return Preferences.Default.Get<string?>(key,null);
    }

    public void Set(string key,string? value){
        if(value is null){
            Preferences.Default.Remove(key);
            return;
        }
        Preferences.Default.Set(key,value);
    }
}

/// <summary>OS の「視差効果を減らす」(iOS)/ アニメーションの無効化(Android)。</summary>
public sealed class MotionPreferences:IMotionPreferences{
    public bool ReduceMotion{
        get{
#if IOS
            return UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif ANDROID
            var resolver=Android.App.Application.Context.ContentResolver;
            if(resolver is null){
                return false;
            }
            var scale=Android.Provider.Settings.Global.GetFloat(resolver,Android.Provider.Settings.Global.AnimatorDurationScale,1f);
            return scale==0f;
#else
            return false;
#endif
        }
    }
}
