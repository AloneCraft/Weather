using System.Collections.Frozen;
using Weather.Core;

namespace Weather.Providers.Routing;

public enum RouteReason{Jma,JapanOutOfCoverage,Nws,MetNorway,NotSupported}

public sealed record ProviderRoute<T>(T? Primary,T? FallbackOnNotFound,RouteReason Reason) where T:class,IWeatherProvider;

/// <summary>
/// 地点 → Provider の振り分け(CacheAndRouting.md)。
/// 不変条件: 日本周辺域(JmaArea 非 null)では MET / NWS を一切選ばない。
/// </summary>
public sealed class WeatherProviderRouter{
    public static readonly FrozenSet<string> NwsCountries=new[]{"US","PR","GU","VI","AS","MP"}.ToFrozenSet(StringComparer.Ordinal);

    private readonly FrozenDictionary<ProviderId,IForecastProvider> forecasts;
    private readonly FrozenDictionary<ProviderId,IAlertProvider> alerts;
    private readonly FrozenDictionary<ProviderId,IObservationProvider> observations;

    public WeatherProviderRouter(IEnumerable<IForecastProvider> forecastProviders,IEnumerable<IAlertProvider> alertProviders,IEnumerable<IObservationProvider> observationProviders){
        this.forecasts=forecastProviders.ToFrozenDictionary(static p=>p.Id);
        this.alerts=alertProviders.ToFrozenDictionary(static p=>p.Id);
        this.observations=observationProviders.ToFrozenDictionary(static p=>p.Id);
    }

    public ProviderRoute<IForecastProvider> RouteForecast(ResolvedLocation location){
        ArgumentNullException.ThrowIfNull(location);
        if(location.JmaArea is not null){
            if(!location.JmaArea.IsCovered){
                return new ProviderRoute<IForecastProvider>(null,null,RouteReason.JapanOutOfCoverage);
            }
            return new ProviderRoute<IForecastProvider>(Get(this.forecasts,ProviderId.Jma),null,RouteReason.Jma);
        }
        if(NwsCountries.Contains(location.CountryCode)){
            return new ProviderRoute<IForecastProvider>(Get(this.forecasts,ProviderId.Nws),Get(this.forecasts,ProviderId.MetNorway),RouteReason.Nws);
        }
        return new ProviderRoute<IForecastProvider>(Get(this.forecasts,ProviderId.MetNorway),null,RouteReason.MetNorway);
    }

    public ProviderRoute<IAlertProvider> RouteAlerts(ResolvedLocation location){
        return Route(this.alerts,location);
    }

    public ProviderRoute<IObservationProvider> RouteObservations(ResolvedLocation location){
        return Route(this.observations,location);
    }

    public IObservationProvider? ObservationProvider(ProviderId id){
        this.observations.TryGetValue(id,out var provider);
        return provider;
    }

    private static ProviderRoute<T> Route<T>(FrozenDictionary<ProviderId,T> providers,ResolvedLocation location) where T:class,IWeatherProvider{
        ArgumentNullException.ThrowIfNull(location);
        if(location.JmaArea is not null){
            if(!location.JmaArea.IsCovered){
                return new ProviderRoute<T>(null,null,RouteReason.JapanOutOfCoverage);
            }
            return new ProviderRoute<T>(Get(providers,ProviderId.Jma),null,RouteReason.Jma);
        }
        if(NwsCountries.Contains(location.CountryCode)){
            return new ProviderRoute<T>(Get(providers,ProviderId.Nws),null,RouteReason.Nws);
        }
        return new ProviderRoute<T>(null,null,RouteReason.NotSupported);
    }

    private static T? Get<T>(FrozenDictionary<ProviderId,T> providers,ProviderId id) where T:class{
        providers.TryGetValue(id,out var provider);
        return provider;
    }
}
