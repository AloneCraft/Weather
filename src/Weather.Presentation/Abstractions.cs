using Weather.Core;

namespace Weather.Presentation;

/// <summary>Shell のルート(Screens.md「画面遷移」)。</summary>
public static class Routes{
    public const string Main="//main";
    public const string Hourly="hourly";
    public const string Daily="daily";
    public const string Alerts="alerts";
    public const string Alert="alert";
    public const string History="history";
    public const string Search="search";
    public const string Map="map";
    public const string Places="places";
    public const string Settings="settings";
    public const string About="about";
    public const string Sources="sources";
}

/// <summary>画面遷移。VM は Shell を直接触らない。</summary>
public interface INavigator{
    Task GoToAsync(string route,IReadOnlyDictionary<string,string>? parameters=null);
    Task GoBackAsync();
}

/// <summary>Shell の引数を受け取る(IQueryAttributable は MAUI の型なので使わない)。</summary>
public interface INavigationAware{
    Task OnNavigatedToAsync(IReadOnlyDictionary<string,string> parameters);
}

public interface IDialogService{
    Task AlertAsync(string title,string message);
    Task<bool> ConfirmAsync(string title,string message,string accept,string cancel);
}

public enum LocationStatus{Granted,Denied,Unavailable}

public interface ILocationService{
    Task<LocationStatus> RequestPermissionAsync();
    Task<GeoPoint?> GetCurrentLocationAsync(CancellationToken cancellationToken);
}

/// <summary>アプリの前面/背景(MAUI の Window イベント)。</summary>
public interface IAppLifecycle{
    bool IsForeground{get;}
    event EventHandler? Resumed;
    event EventHandler? Stopped;
}

/// <summary>OS の「視差効果を減らす」等。</summary>
public interface IMotionPreferences{
    bool ReduceMotion{get;}
}

public interface ISettingsStore{
    string? Get(string key);
    void Set(string key,string? value);
}
