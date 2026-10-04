namespace Weather.Core;

public enum Availability{Available,NotSupported,JapanOutOfCoverage}

public sealed record ForecastResult(Availability Availability,Forecast? Forecast){
    public static ForecastResult OutOfCoverage{get;}=new(Availability.JapanOutOfCoverage,null);
}

public sealed record AlertResult(Availability Availability,AlertSet? Alerts){
    public static AlertResult NotSupported{get;}=new(Availability.NotSupported,null);
    public static AlertResult OutOfCoverage{get;}=new(Availability.JapanOutOfCoverage,null);
}

/// <summary>
/// 地点の解決・Provider の振り分け・取得をまとめた窓口(CacheAndRouting.md)。
/// Functions 移行時はサーバー側で同じ実装を動かし、クライアントは遠隔実装に差し替える。
/// </summary>
public interface IWeatherService{
    ValueTask<ResolvedLocation> ResolveAsync(GeoPoint point,CancellationToken cancellationToken);
    ValueTask<ForecastResult> GetForecastAsync(ResolvedLocation location,CancellationToken cancellationToken);
    ValueTask<AlertResult> GetAlertsAsync(ResolvedLocation location,CancellationToken cancellationToken);
    Availability GetObservationAvailability(ResolvedLocation location);
    ValueTask<IReadOnlyList<ObservationStation>> FindStationsAsync(ResolvedLocation location,int maxCount,CancellationToken cancellationToken);
    ValueTask<ObservationSeries> GetObservationsAsync(ObservationStation station,DateTimeOffset from,DateTimeOffset to,CancellationToken cancellationToken);
    TimeSpan GetObservationServerRetention(ProviderId provider);

    /// <summary>
    /// 背景(ウィジェット・通知)で取得してよい地点か。気象庁・NWS の地点のみ true。
    /// MET の地点は規約で背景取得が禁止、日本周辺の対象外地点は取得するデータがない。
    /// </summary>
    bool AllowsBackgroundFetch(ResolvedLocation location);
}
