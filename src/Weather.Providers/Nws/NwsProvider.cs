using System.Globalization;
using Microsoft.Extensions.Logging;
using Weather.Core;
using Weather.Providers.Http;

namespace Weather.Providers.Nws;

/// <summary>米国 NWS(api.weather.gov)の Provider。予報・警報・観測。</summary>
public sealed partial class NwsProvider(IHttpClientFactory httpClientFactory,TimeProvider time,ILogger<NwsProvider> logger):IForecastProvider,IAlertProvider,IObservationProvider{
    public const string HttpClientName="nws";
    private static readonly Uri BaseUri=new("https://api.weather.gov/");

    public ProviderId Id=>ProviderId.Nws;

    /// <summary>観測の保持期間(2026-10-04 観測で約 7 日)。</summary>
    public TimeSpan ServerRetention=>TimeSpan.FromDays(7);

    public async ValueTask<Forecast> GetForecastAsync(ResolvedLocation location,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(location);
        var client=httpClientFactory.CreateClient(HttpClientName);
        var point=await this.GetPointAsync(client,location.Point,cancellationToken).ConfigureAwait(false);
        var resolved=location;
        if(point.TimeZone is not null&&point.TimeZone!=location.TimeZoneId){
            resolved=location with{TimeZoneId=point.TimeZone};
        }
        var unknown=new List<string>();

        var forecastUri=WithSi(point.Forecast);
        var dailyFetch=await ProviderHttp.GetAsync(client,ProviderId.Nws,forecastUri,time,cancellationToken).ConfigureAwait(false);
        using var dailyDoc=ProviderHttp.ParseJson(ProviderId.Nws,dailyFetch,forecastUri);
        var daily=ProviderHttp.Map(ProviderId.Nws,forecastUri,()=>NwsMapper.MapDaily(dailyDoc.RootElement,dailyFetch,point,location.Point,unknown));

        var issues=new List<DataIssue>();
        IReadOnlyList<ForecastPoint> hourly=[];
        var hourlyUri=WithSi(point.ForecastHourly);
        try{
            var hourlyFetch=await ProviderHttp.GetAsync(client,ProviderId.Nws,hourlyUri,time,cancellationToken).ConfigureAwait(false);
            using var hourlyDoc=ProviderHttp.ParseJson(ProviderId.Nws,hourlyFetch,hourlyUri);
            hourly=ProviderHttp.Map(ProviderId.Nws,hourlyUri,()=>NwsMapper.MapHourly(hourlyDoc.RootElement,hourlyFetch,point,location.Point,unknown));
        }catch(WeatherProviderException ex){
            LogSecondaryFailed(logger,"Hourly Forecast",ex);
            issues.Add(new DataIssue(ProviderId.Nws,"Hourly Forecast",ex.Failure));
        }
        if(hourly.Count>0){
            try{
                var gridFetch=await ProviderHttp.GetAsync(client,ProviderId.Nws,point.ForecastGridData,time,cancellationToken).ConfigureAwait(false);
                using var gridDoc=ProviderHttp.ParseJson(ProviderId.Nws,gridFetch,point.ForecastGridData);
                var current=hourly;
                hourly=ProviderHttp.Map(ProviderId.Nws,point.ForecastGridData,()=>NwsMapper.EnrichWithGrid(current,gridDoc.RootElement,unknown));
            }catch(WeatherProviderException ex){
                LogSecondaryFailed(logger,"Gridpoint",ex);
                issues.Add(new DataIssue(ProviderId.Nws,"Gridpoint",ex.Failure));
            }
        }
        foreach(var phrase in unknown.Distinct()){
            LogUnknownPhrase(logger,phrase);
        }
        string? areaName=null;
        if(point.City is not null){
            areaName=point.City;
            if(point.State is not null){
                areaName+=", "+point.State;
            }
        }
        return new Forecast(resolved,hourly,daily,areaName,issues);
    }

    public async ValueTask<AlertSet> GetAlertsAsync(ResolvedLocation location,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(location);
        var client=httpClientFactory.CreateClient(HttpClientName);
        var p=location.Point.RoundForRequest();
        var uri=new Uri(BaseUri,string.Create(CultureInfo.InvariantCulture,$"alerts/active?point={p.Latitude:0.##},{p.Longitude:0.##}"));
        var fetch=await ProviderHttp.GetAsync(client,ProviderId.Nws,uri,time,cancellationToken).ConfigureAwait(false);
        using var doc=ProviderHttp.ParseJson(ProviderId.Nws,fetch,uri);
        return ProviderHttp.Map(ProviderId.Nws,uri,()=>NwsMapper.MapAlerts(doc.RootElement,fetch,location.Point));
    }

    public async ValueTask<IReadOnlyList<ObservationStation>> FindStationsAsync(ResolvedLocation location,int maxCount,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(location);
        var client=httpClientFactory.CreateClient(HttpClientName);
        var point=await this.GetPointAsync(client,location.Point,cancellationToken).ConfigureAwait(false);
        var fetch=await ProviderHttp.GetAsync(client,ProviderId.Nws,point.ObservationStations,time,cancellationToken).ConfigureAwait(false);
        using var doc=ProviderHttp.ParseJson(ProviderId.Nws,fetch,point.ObservationStations);
        var stations=ProviderHttp.Map(ProviderId.Nws,point.ObservationStations,()=>NwsMapper.MapStations(doc.RootElement));
        return [..stations.Take(maxCount)];
    }

    public async ValueTask<ObservationSeries> GetObservationsAsync(ObservationStation station,DateTimeOffset from,DateTimeOffset to,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(station);
        var client=httpClientFactory.CreateClient(HttpClientName);
        var now=time.GetUtcNow();
        var start=from;
        if(start<now-this.ServerRetention){
            start=now-this.ServerRetention;
        }
        var uri=new Uri(BaseUri,string.Create(CultureInfo.InvariantCulture,
            $"stations/{Uri.EscapeDataString(station.Id)}/observations?start={start.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}&end={to.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}"));
        var fetch=await ProviderHttp.GetAsync(client,ProviderId.Nws,uri,time,cancellationToken).ConfigureAwait(false);
        using var doc=ProviderHttp.ParseJson(ProviderId.Nws,fetch,uri);
        var unknown=new List<string>();
        var observations=ProviderHttp.Map(ProviderId.Nws,uri,()=>NwsMapper.MapObservations(doc.RootElement,unknown));
        foreach(var phrase in unknown.Distinct()){
            LogUnknownPhrase(logger,phrase);
        }
        var source=NwsAttribution.Create("Observations",station.Id,null,fetch,station.Location,DataProcessing.UnitConverted);
        return new ObservationSeries(station,observations,source);
    }

    private async Task<NwsPoint> GetPointAsync(HttpClient client,GeoPoint location,CancellationToken cancellationToken){
        var p=location.RoundForRequest();
        var uri=new Uri(BaseUri,string.Create(CultureInfo.InvariantCulture,$"points/{p.Latitude:0.##},{p.Longitude:0.##}"));
        var fetch=await ProviderHttp.GetAsync(client,ProviderId.Nws,uri,time,cancellationToken).ConfigureAwait(false);
        using var doc=ProviderHttp.ParseJson(ProviderId.Nws,fetch,uri);
        return ProviderHttp.Map(ProviderId.Nws,uri,()=>NwsMapper.MapPoint(doc.RootElement));
    }

    private static Uri WithSi(Uri uri){
        if(uri.Query.Contains("units=",StringComparison.Ordinal)){
            return uri;
        }
        var separator="?";
        if(uri.Query.Length>0){
            separator="&";
        }
        return new Uri(uri.AbsoluteUri+separator+"units=si");
    }

    [LoggerMessage(Level=LogLevel.Warning,Message="NWS の天気表現を解析できませんでした: {Phrase}(表示は原文で行います)")]
    private static partial void LogUnknownPhrase(ILogger logger,string phrase);

    [LoggerMessage(Level=LogLevel.Warning,Message="補助プロダクト {Product} を取得できませんでした")]
    private static partial void LogSecondaryFailed(ILogger logger,string product,Exception exception);
}
