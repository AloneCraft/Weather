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

public enum ProviderFailure{Network,Timeout,RateLimited,Forbidden,NotFound,ServerError,InvalidResponse}

public sealed class WeatherProviderException(ProviderId provider,ProviderFailure failure,string message,Exception? inner=null):Exception(message,inner){
    public ProviderId Provider{get;}=provider;
    public ProviderFailure Failure{get;}=failure;
}
