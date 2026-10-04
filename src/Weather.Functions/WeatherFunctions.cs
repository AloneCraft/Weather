using System.Globalization;
using System.Net;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Weather.Remote.Server;

namespace Weather.Functions;

/// <summary>
/// HTTP を受けて WeatherApi に渡すだけの薄い層(要求処理は Weather.Remote の WeatherApi。単体テストはそちらで行う)。
/// 認証なし(Anonymous)。アプリに埋め込む API キーは秘密にできないため使わず、公開時はレート制限(API Management 等)を前提にする。
/// </summary>
public sealed class WeatherFunctions(WeatherApi api){
    [Function("resolve")]
    public Task<HttpResponseData> ResolveAsync([HttpTrigger(AuthorizationLevel.Anonymous,"get",Route="resolve")] HttpRequestData request,CancellationToken cancellationToken){
        return this.HandleAsync(request,WeatherApi.ResolveRoute,cancellationToken);
    }

    [Function("forecast")]
    public Task<HttpResponseData> ForecastAsync([HttpTrigger(AuthorizationLevel.Anonymous,"get",Route="forecast")] HttpRequestData request,CancellationToken cancellationToken){
        return this.HandleAsync(request,WeatherApi.ForecastRoute,cancellationToken);
    }

    [Function("alerts")]
    public Task<HttpResponseData> AlertsAsync([HttpTrigger(AuthorizationLevel.Anonymous,"get",Route="alerts")] HttpRequestData request,CancellationToken cancellationToken){
        return this.HandleAsync(request,WeatherApi.AlertsRoute,cancellationToken);
    }

    [Function("stations")]
    public Task<HttpResponseData> StationsAsync([HttpTrigger(AuthorizationLevel.Anonymous,"get",Route="stations")] HttpRequestData request,CancellationToken cancellationToken){
        return this.HandleAsync(request,WeatherApi.StationsRoute,cancellationToken);
    }

    [Function("observations")]
    public Task<HttpResponseData> ObservationsAsync([HttpTrigger(AuthorizationLevel.Anonymous,"get",Route="observations")] HttpRequestData request,CancellationToken cancellationToken){
        return this.HandleAsync(request,WeatherApi.ObservationsRoute,cancellationToken);
    }

    private async Task<HttpResponseData> HandleAsync(HttpRequestData request,string route,CancellationToken cancellationToken){
        var query=HttpUtility.ParseQueryString(request.Url.Query);
        var result=await api.HandleAsync(route,name=>query[name],cancellationToken).ConfigureAwait(false);
        var response=request.CreateResponse((HttpStatusCode)result.Status);
        response.Headers.Add("Content-Type","application/json; charset=utf-8");
        if(result.MaxAge is {} maxAge){
            response.Headers.Add("Cache-Control","public, max-age="+((int)maxAge.TotalSeconds).ToString(CultureInfo.InvariantCulture));
        }
        await response.WriteStringAsync(result.Json,cancellationToken).ConfigureAwait(false);
        return response;
    }
}
