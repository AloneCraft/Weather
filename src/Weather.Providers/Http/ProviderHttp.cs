using System.Globalization;
using System.Net;
using System.Text.Json;
using Weather.Core;

namespace Weather.Providers.Http;

internal readonly record struct FetchResult(byte[] Body,DateTimeOffset RetrievedAt,bool IsStale,HttpStatusCode StatusCode);

/// <summary>Provider 共通の取得処理。HTTP の失敗を ProviderFailure に変換する。</summary>
internal static class ProviderHttp{
    public static async Task<FetchResult> GetAsync(HttpClient client,ProviderId provider,Uri uri,TimeProvider time,CancellationToken cancellationToken,(long From,long? To)? range=null){
        HttpResponseMessage response;
        try{
            using var request=new HttpRequestMessage(HttpMethod.Get,uri);
            if(range is {} r){
                request.Headers.Range=new System.Net.Http.Headers.RangeHeaderValue(r.From,r.To);
            }
            response=await client.SendAsync(request,HttpCompletionOption.ResponseContentRead,cancellationToken).ConfigureAwait(false);
        }catch(TimeoutException ex){
            throw new WeatherProviderException(provider,ProviderFailure.Timeout,$"タイムアウト: {uri}",ex);
        }catch(HttpRequestException ex){
            throw new WeatherProviderException(provider,ProviderFailure.Network,$"通信エラー: {uri}",ex);
        }catch(TaskCanceledException ex) when(!cancellationToken.IsCancellationRequested){
            throw new WeatherProviderException(provider,ProviderFailure.Timeout,$"タイムアウト: {uri}",ex);
        }
        using(response){
            if(!response.IsSuccessStatusCode){
                throw new WeatherProviderException(provider,MapStatus(response.StatusCode),$"HTTP {(int)response.StatusCode}: {uri}");
            }
            var body=await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var retrievedAt=time.GetUtcNow();
            var isStale=false;
            if(response.Headers.TryGetValues(CacheHeaders.ReceivedAt,out var receivedValues)){
                foreach(var value in receivedValues){
                    if(DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var parsed)){
                        retrievedAt=parsed;
                    }
                }
            }
            if(response.Headers.TryGetValues(CacheHeaders.State,out var stateValues)){
                isStale=stateValues.Contains("stale",StringComparer.Ordinal);
            }
            return new FetchResult(body,retrievedAt,isStale,response.StatusCode);
        }
    }

    public static ProviderFailure MapStatus(HttpStatusCode status){
        var code=(int)status;
        if(status==HttpStatusCode.Forbidden){
            return ProviderFailure.Forbidden;
        }
        if(status==HttpStatusCode.TooManyRequests){
            return ProviderFailure.RateLimited;
        }
        if(status==HttpStatusCode.NotFound){
            return ProviderFailure.NotFound;
        }
        if(code>=500){
            return ProviderFailure.ServerError;
        }
        return ProviderFailure.InvalidResponse;
    }

    public static JsonDocument ParseJson(ProviderId provider,FetchResult result,Uri uri){
        try{
            return JsonDocument.Parse(result.Body);
        }catch(JsonException ex){
            throw new WeatherProviderException(provider,ProviderFailure.InvalidResponse,$"JSON を解析できません: {uri}",ex);
        }
    }

    /// <summary>応答の構造が想定外だった場合の例外を InvalidResponse に変換する。</summary>
    public static T Map<T>(ProviderId provider,Uri uri,Func<T> map){
        try{
            return map();
        }catch(Exception ex) when(ex is InvalidOperationException or KeyNotFoundException or FormatException or IndexOutOfRangeException or ArgumentException or JsonException or NotSupportedException or InvalidDataException){
            throw new WeatherProviderException(provider,ProviderFailure.InvalidResponse,$"応答の構造が想定外です: {uri}",ex);
        }
    }
}
