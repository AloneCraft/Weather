namespace Weather.Core;

public interface IWeatherProvider{
    ProviderId Id{get;}
}

public interface IForecastProvider:IWeatherProvider{
    ValueTask<Forecast> GetForecastAsync(ResolvedLocation location,CancellationToken cancellationToken);
}

public interface IAlertProvider:IWeatherProvider{
    ValueTask<AlertSet> GetAlertsAsync(ResolvedLocation location,CancellationToken cancellationToken);
}

public interface IObservationProvider:IWeatherProvider{
    ValueTask<IReadOnlyList<ObservationStation>> FindStationsAsync(ResolvedLocation location,int maxCount,CancellationToken cancellationToken);
    ValueTask<ObservationSeries> GetObservationsAsync(ObservationStation station,DateTimeOffset from,DateTimeOffset to,CancellationToken cancellationToken);

    /// <summary>サーバーが過去の観測を保持している期間(History.md)。</summary>
    TimeSpan ServerRetention{get;}
}

/// <summary>NotAllowed: 利用規約により取得しない(MET はアプリの使用中以外に取得しない)。</summary>
public enum ProviderFailure{Network,Timeout,RateLimited,Forbidden,NotFound,ServerError,InvalidResponse,NotAllowed}

/// <summary>
/// アプリが使用中か(MET Norway の規約: モバイルアプリは使用中でないときにデータを取得してはいけない)。
/// アプリは前面/背景から判定する。サーバー側(Functions)やテストでは常に使用中として扱う。
/// </summary>
public interface IAppActivity{
    bool IsInUse{get;}
}

public sealed class AlwaysInUse:IAppActivity{
    public static AlwaysInUse Instance{get;}=new();

    public bool IsInUse=>true;
}

public sealed class WeatherProviderException(ProviderId provider,ProviderFailure failure,string message,Exception? inner=null):Exception(message,inner){
    public ProviderId Provider{get;}=provider;
    public ProviderFailure Failure{get;}=failure;
}
