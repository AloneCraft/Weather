using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Weather.Core;
using Weather.Remote.Contracts;
using Weather.Remote.Server;

namespace Weather.Remote;

/// <summary>
/// Functions(Weather.Functions)の API を呼ぶ IWeatherService(Phase 4)。既定のアプリは直接取得(方針 2)のため使わない。
/// 同期で答えるメソッド(観測の可否・背景取得の可否・保持期間)は、直前の resolve の応答を使う。
/// resolve していない地点は「対応なし / 背景取得しない」と安全側に答える。
/// </summary>
public sealed class RemoteWeatherService(HttpClient http):IWeatherService{
    public const string HttpClientName="weather-remote";

    private readonly ConcurrentDictionary<GeoPoint,ResolveResponse> resolved=new();
    private readonly ConcurrentDictionary<ProviderId,TimeSpan> retention=new();

    public async ValueTask<ResolvedLocation> ResolveAsync(GeoPoint point,CancellationToken cancellationToken){
        var (response,location,retentions)=await this.GetAsync<ResolveResponse,(ResolveResponse Response,ResolvedLocation Location,List<(ProviderId Provider,TimeSpan Retention)> Retentions)>(WeatherApi.ResolveRoute,PointQuery(point),null,static r=>(r,ContractMapper.ToModel(r.Location),[..r.Retention.Select(static i=>(i.Provider,TimeSpan.FromDays(i.Days)))]),cancellationToken).ConfigureAwait(false);
        this.resolved[location.Point.RoundForRequest()]=response;
        foreach(var (provider,retention) in retentions){
            this.retention[provider]=retention;
        }
        return location;
    }

    public async ValueTask<ForecastResult> GetForecastAsync(ResolvedLocation location,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(location);
        return await this.GetAsync<ForecastResponse,ForecastResult>(WeatherApi.ForecastRoute,PointQuery(location.Point),location,static r=>ContractMapper.ToModel(r),cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<AlertResult> GetAlertsAsync(ResolvedLocation location,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(location);
        return await this.GetAsync<AlertResponse,AlertResult>(WeatherApi.AlertsRoute,PointQuery(location.Point),location,static r=>ContractMapper.ToModel(r),cancellationToken).ConfigureAwait(false);
    }

    public Availability GetObservationAvailability(ResolvedLocation location){
        ArgumentNullException.ThrowIfNull(location);
        if(this.resolved.TryGetValue(location.Point.RoundForRequest(),out var response)){
            return response.ObservationAvailability;
        }
        return Availability.NotSupported;
    }

    public async ValueTask<IReadOnlyList<ObservationStation>> FindStationsAsync(ResolvedLocation location,int maxCount,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(location);
        var query=PointQuery(location.Point)+"&max="+maxCount.ToString(CultureInfo.InvariantCulture);
        return await this.GetAsync<StationsResponse,IReadOnlyList<ObservationStation>>(WeatherApi.StationsRoute,query,location,static r=>[..r.Stations.Select(ContractMapper.ToModel)],cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ObservationSeries> GetObservationsAsync(ObservationStation station,DateTimeOffset from,DateTimeOffset to,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(station);
        var query=string.Create(CultureInfo.InvariantCulture,
            $"provider={station.Provider}&station={Uri.EscapeDataString(station.Id)}&name={Uri.EscapeDataString(station.Name)}&lat={station.Location.Latitude}&lon={station.Location.Longitude}&from={Uri.EscapeDataString(from.ToString("o",CultureInfo.InvariantCulture))}&to={Uri.EscapeDataString(to.ToString("o",CultureInfo.InvariantCulture))}");
        return await this.GetAsync<ObservationSeriesDto,ObservationSeries>(WeatherApi.ObservationsRoute,query,null,r=>ContractMapper.ToModel(r,station),cancellationToken,station.Provider).ConfigureAwait(false);
    }

    public TimeSpan GetObservationServerRetention(ProviderId provider){
        if(this.retention.TryGetValue(provider,out var value)){
            return value;
        }
        return TimeSpan.Zero;
    }

    public bool AllowsBackgroundFetch(ResolvedLocation location){
        ArgumentNullException.ThrowIfNull(location);
        if(this.resolved.TryGetValue(location.Point.RoundForRequest(),out var response)){
            return response.AllowsBackgroundFetch;
        }
        return false;
    }

    /// <summary>GET して T に読み取り、map でモデルへ変換する。変換の検証例外(範囲・昇順・日本域は気象庁のみ)も InvalidResponse にするため、変換まで try の中で行う。</summary>
    private async Task<TResult> GetAsync<T,TResult>(string route,string query,ResolvedLocation? location,Func<T,TResult> map,CancellationToken cancellationToken,ProviderId? provider=null){
        var label=provider??LabelFor(location);
        HttpResponseMessage response;
        try{
            response=await http.GetAsync(new Uri(route+"?"+query,UriKind.Relative),cancellationToken).ConfigureAwait(false);
        }catch(TaskCanceledException ex) when(!cancellationToken.IsCancellationRequested){
            throw new WeatherProviderException(label,ProviderFailure.Timeout,$"サーバーの応答がありません: {route}",ex);
        }catch(HttpRequestException ex){
            throw new WeatherProviderException(label,ProviderFailure.Network,$"サーバーに接続できません: {route}",ex);
        }
        using(response){
            var json=await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if(!response.IsSuccessStatusCode){
                throw ToException(response.StatusCode,json,label,route);
            }
            try{
                return map(RemoteJson.Deserialize<T>(json));
            }catch(Exception ex) when(ex is JsonException or NotSupportedException){
                throw new WeatherProviderException(label,ProviderFailure.InvalidResponse,$"サーバーの応答を読み取れません: {route}",ex);
            }catch(Exception ex) when(ex is ArgumentException or InvalidOperationException or OverflowException or NullReferenceException){
                //モデルの検証(範囲・昇順・日本域は気象庁のみ)・数値の範囲・配列の null 要素(型の注釈では検出できない)に反する応答
                throw new WeatherProviderException(label,ProviderFailure.InvalidResponse,$"サーバーの応答が不変条件に反しています: {route}",ex);
            }
        }
    }

    private static WeatherProviderException ToException(HttpStatusCode status,string json,ProviderId label,string route){
        ErrorResponse? error=null;
        try{
            error=RemoteJson.Deserialize<ErrorResponse>(json);
        }catch(Exception ex) when(ex is JsonException or NotSupportedException){
            //本文が ErrorResponse でない(ゲートウェイの応答など)
        }
        var failure=error?.Failure??MapStatus(status);
        var message=error?.Message??$"サーバーがエラーを返しました({(int)status}): {route}";
        return new WeatherProviderException(error?.Provider??label,failure,message);
    }

    private static ProviderFailure MapStatus(HttpStatusCode status){
        switch(status){
            case HttpStatusCode.Forbidden:
                return ProviderFailure.Forbidden;
            case HttpStatusCode.NotFound:
                return ProviderFailure.NotFound;
            case HttpStatusCode.TooManyRequests:
                return ProviderFailure.RateLimited;
            case HttpStatusCode.BadRequest:
                return ProviderFailure.InvalidResponse;
        }
        if((int)status>=500){
            return ProviderFailure.ServerError;
        }
        return ProviderFailure.InvalidResponse;
    }

    /// <summary>
    /// 通信の失敗をどの機関の失敗として表示するか(表示用の目安。実際の振り分けはサーバーが行う)。
    /// </summary>
    private static ProviderId LabelFor(ResolvedLocation? location){
        if(location?.JmaArea is not null){
            return ProviderId.Jma;
        }
        if(location?.CountryCode=="US"){
            return ProviderId.Nws;
        }
        return ProviderId.MetNorway;
    }

    private static string PointQuery(GeoPoint point){
        var p=point.RoundForRequest();
        return string.Create(CultureInfo.InvariantCulture,$"lat={p.Latitude}&lon={p.Longitude}");
    }
}

public static class RemoteServiceCollectionExtensions{
    /// <summary>
    /// IWeatherService を Functions の API に差し替える。baseAddress は「…/api/」まで(末尾の / を含める)。
    /// AddWeatherProviders の後に呼ぶ(既存の IWeatherService を置き換える)。
    /// </summary>
    public static IServiceCollection AddRemoteWeatherService(this IServiceCollection services,Uri baseAddress,string userAgent){
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(userAgent);
        services.AddHttpClient(RemoteWeatherService.HttpClientName,client=>{
            client.BaseAddress=baseAddress;
            client.Timeout=TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        });
        services.Replace(ServiceDescriptor.Singleton<IWeatherService>(static sp=>new RemoteWeatherService(sp.GetRequiredService<IHttpClientFactory>().CreateClient(RemoteWeatherService.HttpClientName))));
        return services;
    }
}
