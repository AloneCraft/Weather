using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Weather.Core;
using Weather.Providers.Http;

namespace Weather.Providers.Jma;

/// <summary>気象庁(bosai)の Provider。予報・警報・観測(アメダス)。</summary>
public sealed partial class JmaProvider:IForecastProvider,IAlertProvider,IObservationProvider{
    public const string HttpClientName="jma";

    private readonly IHttpClientFactory httpClientFactory;
    private readonly JmaDefinitions definitions;
    private readonly TimeProvider time;
    private readonly ILogger<JmaProvider> logger;

    internal JmaProvider(IHttpClientFactory httpClientFactory,JmaDefinitions definitions,TimeProvider time,ILogger<JmaProvider> logger){
        this.httpClientFactory=httpClientFactory;
        this.definitions=definitions;
        this.time=time;
        this.logger=logger;
    }

    public ProviderId Id=>ProviderId.Jma;

    /// <summary>アメダス地点別ファイルの保持期間(2026-10-04 観測で約 10 日)。</summary>
    public TimeSpan ServerRetention=>TimeSpan.FromDays(10);

    public async ValueTask<Forecast> GetForecastAsync(ResolvedLocation location,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(location);
        var area=this.ResolveArea(location);
        var client=this.httpClientFactory.CreateClient(HttpClientName);

        var forecastUri=JmaEndpoints.Forecast(area.ForecastOfficeCode);
        var fetch=await ProviderHttp.GetAsync(client,ProviderId.Jma,forecastUri,this.time,cancellationToken).ConfigureAwait(false);
        using var forecastDoc=ProviderHttp.ParseJson(ProviderId.Jma,fetch,forecastUri);
        var today=JmaAttribution.JstDate(this.time.GetUtcNow());
        var daily=ProviderHttp.Map(ProviderId.Jma,forecastUri,()=>JmaForecastMapper.MapDaily(forecastDoc.RootElement,area,fetch,today));
        foreach(var code in daily.UnknownCodes){
            LogUnknownCode(this.logger,"天気コード",code);
        }

        //時系列予報は補助プロダクト。失敗しても日別は返す(部分結果+Issues)
        IReadOnlyList<ForecastPoint> timeSeries=[];
        var issues=new List<DataIssue>();
        var timeSeriesUri=JmaEndpoints.TimeSeries(area.Class10Code);
        try{
            var tsFetch=await ProviderHttp.GetAsync(client,ProviderId.Jma,timeSeriesUri,this.time,cancellationToken).ConfigureAwait(false);
            using var tsDoc=ProviderHttp.ParseJson(ProviderId.Jma,tsFetch,timeSeriesUri);
            var unknownWords=new List<string>();
            timeSeries=ProviderHttp.Map(ProviderId.Jma,timeSeriesUri,()=>JmaForecastMapper.MapTimeSeries(tsDoc.RootElement,area,tsFetch,unknownWords));
            foreach(var word in unknownWords.Distinct()){
                LogUnknownCode(this.logger,"時系列予報の天気",word);
            }
        }catch(WeatherProviderException ex){
            LogSecondaryFailed(this.logger,"時系列予報",ex);
            issues.Add(new DataIssue(ProviderId.Jma,"時系列予報",ex.Failure));
        }
        if(!daily.WeeklyMatched){
            //週間予報の区域が一致しない地域は週間予報を出さず、利用者に分かるように記録する
            issues.Add(new DataIssue(ProviderId.Jma,"府県週間天気予報",ProviderFailure.NotFound));
        }
        return new Forecast(location,timeSeries,daily.Daily,daily.AreaName??area.Class10Name,issues);
    }

    public async ValueTask<AlertSet> GetAlertsAsync(ResolvedLocation location,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(location);
        var area=this.ResolveArea(location);
        var client=this.httpClientFactory.CreateClient(HttpClientName);
        var uri=JmaEndpoints.Warning(area.OfficeCode);
        var fetch=await ProviderHttp.GetAsync(client,ProviderId.Jma,uri,this.time,cancellationToken).ConfigureAwait(false);
        using var doc=ProviderHttp.ParseJson(ProviderId.Jma,fetch,uri);
        var result=ProviderHttp.Map(ProviderId.Jma,uri,()=>JmaAlertMapper.Map(doc.RootElement,area,fetch));
        foreach(var code in result.UnknownCodes.Distinct()){
            LogUnknownCode(this.logger,"警報コード",code);
        }
        foreach(var status in result.UnknownStatuses.Distinct()){
            LogUnknownCode(this.logger,"警報の状態",status);
        }
        return result.Alerts;
    }

    public ValueTask<IReadOnlyList<ObservationStation>> FindStationsAsync(ResolvedLocation location,int maxCount,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(location);
        var catalog=this.definitions.Current;
        IReadOnlyList<ObservationStation> stations=[..catalog.Stations
            .Where(static s=>s.HasTemperature)
            .Select(s=>(Station:s,Distance:GeoMath.HaversineKm(location.Point.Latitude,location.Point.Longitude,s.Latitude,s.Longitude)))
            .Where(static x=>x.Distance<=50)
            .OrderBy(static x=>x.Distance)
            .Take(maxCount)
            .Select(static x=>ToStation(x.Station))];
        return ValueTask.FromResult(stations);
    }

    public async ValueTask<ObservationSeries> GetObservationsAsync(ObservationStation station,DateTimeOffset from,DateTimeOffset to,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(station);
        var client=this.httpClientFactory.CreateClient(HttpClientName);
        var latestFetch=await ProviderHttp.GetAsync(client,ProviderId.Jma,JmaEndpoints.AmedasLatestTime,this.time,cancellationToken).ConfigureAwait(false);
        var latestText=Encoding.UTF8.GetString(latestFetch.Body).Trim();
        if(!DateTimeOffset.TryParse(latestText,CultureInfo.InvariantCulture,DateTimeStyles.None,out var latest)){
            throw new WeatherProviderException(ProviderId.Jma,ProviderFailure.InvalidResponse,"アメダスの最新時刻を解析できません。");
        }
        var end=to;
        if(latest<end){
            end=latest;
        }
        var start=from;
        var oldest=latest-this.ServerRetention;
        if(start<oldest){
            start=oldest;
        }

        var observations=new SortedDictionary<DateTimeOffset,Observation>();
        var summaries=new SortedDictionary<DateOnly,DailyObservationSummary>();
        FetchResult? lastFetch=null;
        var block=BlockStart(start);
        while(block<=end){
            var uri=JmaEndpoints.AmedasPoint(station.Id,block);
            try{
                var fetch=await ProviderHttp.GetAsync(client,ProviderId.Jma,uri,this.time,cancellationToken).ConfigureAwait(false);
                lastFetch=fetch;
                using var doc=ProviderHttp.ParseJson(ProviderId.Jma,fetch,uri);
                foreach(var observation in ProviderHttp.Map(ProviderId.Jma,uri,()=>JmaAmedasMapper.MapObservations(doc.RootElement))){
                    if(observation.ObservedAt>=from&&observation.ObservedAt<=to){
                        observations[observation.ObservedAt]=observation;
                    }
                }
                foreach(var summary in JmaAmedasMapper.MapDailySummaries(doc.RootElement,station.Key)){
                    summaries[summary.LocalDate]=summary;
                }
            }catch(WeatherProviderException ex) when(ex.Failure==ProviderFailure.NotFound){
                //保持期間外のブロックは欠測として扱う(補間しない)
            }
            block=block.AddHours(3);
        }
        var page=JmaEndpoints.AmedasPage(station.Id);
        var source=JmaAttribution.Create("アメダス",null,latest,lastFetch??latestFetch,page);
        return new ObservationSeries(station,[..observations.Values],source,[..summaries.Values]);
    }

    private JmaArea ResolveArea(ResolvedLocation location){
        if(location.JmaArea is not {IsCovered:true} match){
            throw new WeatherProviderException(ProviderId.Jma,ProviderFailure.NotFound,"気象庁の予報区域外の地点です。");
        }
        return this.definitions.Current.Resolve(match.Class20Code!)
            ??throw new WeatherProviderException(ProviderId.Jma,ProviderFailure.NotFound,$"区域コードが見つかりません: {match.Class20Code}");
    }

    private static ObservationStation ToStation(AmedasStation station){
        return new ObservationStation(station.Code,ProviderId.Jma,station.Name,new GeoPoint(station.Latitude,station.Longitude),station.AltitudeM);
    }

    private static DateTimeOffset BlockStart(DateTimeOffset time){
        var jst=time.ToOffset(JmaAttribution.Jst);
        return new DateTimeOffset(jst.Year,jst.Month,jst.Day,jst.Hour/3*3,0,0,JmaAttribution.Jst);
    }

    [LoggerMessage(Level=LogLevel.Warning,Message="未知の{Kind}です: {Code}(表示は原文で行います)")]
    private static partial void LogUnknownCode(ILogger logger,string kind,string code);

    [LoggerMessage(Level=LogLevel.Warning,Message="補助プロダクト {Product} を取得できませんでした")]
    private static partial void LogSecondaryFailed(ILogger logger,string product,Exception exception);
}
