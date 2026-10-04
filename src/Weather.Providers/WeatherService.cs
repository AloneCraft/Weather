using Weather.Core;
using Weather.Providers.Routing;

namespace Weather.Providers;

/// <summary>地点の解決・振り分け・取得の窓口(CacheAndRouting.md)。</summary>
public sealed class WeatherService(ILocationResolver resolver,WeatherProviderRouter router):IWeatherService{
    public ValueTask<ResolvedLocation> ResolveAsync(GeoPoint point,CancellationToken cancellationToken){
        return resolver.ResolveAsync(point,cancellationToken);
    }

    public async ValueTask<ForecastResult> GetForecastAsync(ResolvedLocation location,CancellationToken cancellationToken){
        var route=router.RouteForecast(location);
        if(route.Reason==RouteReason.JapanOutOfCoverage){
            return ForecastResult.OutOfCoverage;
        }
        if(route.Primary is null){
            return new ForecastResult(Availability.NotSupported,null);
        }
        try{
            var forecast=await route.Primary.GetForecastAsync(location,cancellationToken).ConfigureAwait(false);
            return new ForecastResult(Availability.Available,forecast);
        }catch(WeatherProviderException ex) when(ex.Failure==ProviderFailure.NotFound&&route.FallbackOnNotFound is not null){
            //NWS のグリッド範囲外(404)のときだけ MET に切り替える。通信障害では切り替えない
            var forecast=await route.FallbackOnNotFound.GetForecastAsync(location,cancellationToken).ConfigureAwait(false);
            return new ForecastResult(Availability.Available,forecast);
        }
    }

    public async ValueTask<AlertResult> GetAlertsAsync(ResolvedLocation location,CancellationToken cancellationToken){
        var route=router.RouteAlerts(location);
        if(route.Reason==RouteReason.JapanOutOfCoverage){
            return AlertResult.OutOfCoverage;
        }
        if(route.Primary is null){
            return AlertResult.NotSupported;
        }
        try{
            var alerts=await route.Primary.GetAlertsAsync(location,cancellationToken).ConfigureAwait(false);
            return new AlertResult(Availability.Available,alerts);
        }catch(WeatherProviderException ex) when(ex.Failure==ProviderFailure.NotFound&&route.Reason==RouteReason.Nws){
            return AlertResult.NotSupported;
        }
    }

    public Availability GetObservationAvailability(ResolvedLocation location){
        var route=router.RouteObservations(location);
        if(route.Reason==RouteReason.JapanOutOfCoverage){
            return Availability.JapanOutOfCoverage;
        }
        if(route.Primary is null){
            return Availability.NotSupported;
        }
        return Availability.Available;
    }

    public async ValueTask<IReadOnlyList<ObservationStation>> FindStationsAsync(ResolvedLocation location,int maxCount,CancellationToken cancellationToken){
        var route=router.RouteObservations(location);
        if(route.Primary is null){
            return [];
        }
        try{
            return await route.Primary.FindStationsAsync(location,maxCount,cancellationToken).ConfigureAwait(false);
        }catch(WeatherProviderException ex) when(ex.Failure==ProviderFailure.NotFound){
            return [];
        }
    }

    public ValueTask<ObservationSeries> GetObservationsAsync(ObservationStation station,DateTimeOffset from,DateTimeOffset to,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(station);
        var provider=router.ObservationProvider(station.Provider)
            ??throw new WeatherProviderException(station.Provider,ProviderFailure.NotFound,"観測に対応していない Provider です。");
        return provider.GetObservationsAsync(station,from,to,cancellationToken);
    }

    public TimeSpan GetObservationServerRetention(ProviderId provider){
        if(router.ObservationProvider(provider) is {} p){
            return p.ServerRetention;
        }
        return TimeSpan.Zero;
    }
}
