using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Weather.Core;

namespace Weather.Providers.Http;

public static class CacheHeaders{
    /// <summary>hit / revalidated / miss / stale</summary>
    public const string State="X-Weather-Cache";
    public const string ReceivedAt="X-Weather-Received-At";
}

/// <summary>
/// HTTP キャッシュ(CacheAndRouting.md)。鮮度内ならネットワークに出ず、期限切れは条件付き GET、
/// 通信失敗時は許容期間内の古いエントリを印付きで返す。タイムアウトもここで扱う。
/// </summary>
public sealed partial class HttpCacheHandler(IHttpCacheStore store,HttpCachePolicy policy,WeatherProviderOptions options,TimeProvider time,ILogger<HttpCacheHandler> logger):DelegatingHandler{
    private readonly ConcurrentDictionary<string,SemaphoreSlim> locks=new(StringComparer.Ordinal);

    public static string CreateKey(Uri uri){
        ArgumentNullException.ThrowIfNull(uri);
        return "GET "+uri.AbsoluteUri;
    }

    /// <summary>範囲要求(GFS の 1 要素の取得)は範囲ごとに別のエントリにする。</summary>
    public static string CreateKey(HttpRequestMessage request){
        ArgumentNullException.ThrowIfNull(request);
        var key=CreateKey(request.RequestUri!);
        if(request.Headers.Range is {} range){
            key+=" "+range;
        }
        return key;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(request);
        if(request.Method!=HttpMethod.Get||request.RequestUri is null){
            return await this.SendWithTimeoutAsync(request,cancellationToken).ConfigureAwait(false);
        }
        var key=CreateKey(request);
        var gate=this.locks.GetOrAdd(key,static _=>new SemaphoreSlim(1,1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try{
            return await this.SendCoreAsync(request,key,cancellationToken).ConfigureAwait(false);
        }finally{
            gate.Release();
        }
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpRequestMessage request,string key,CancellationToken cancellationToken){
        var rule=policy.Resolve(request.RequestUri!);
        var now=time.GetUtcNow();
        var entry=await this.TryGetAsync(key,cancellationToken).ConfigureAwait(false);
        if(entry is not null&&HttpFreshness.IsFresh(entry,now,rule.MinimumFreshness)){
            if(HttpCacheAccess.NeedsUpdate(entry,now)){
                //最終利用時刻を更新する(容量・未使用期限の削除は最終利用時刻の古い順。1 時間に 1 回まで)
                entry=entry with{LastAccessedAt=now};
                await this.TrySetAsync(entry,cancellationToken).ConfigureAwait(false);
            }
            return CreateResponse(request,entry,"hit");
        }
        if(entry is not null){
            if(entry.ETag is not null){
                request.Headers.TryAddWithoutValidation("If-None-Match",entry.ETag);
            }
            if(entry.LastModified is not null){
                request.Headers.TryAddWithoutValidation("If-Modified-Since",entry.LastModified);
            }
        }

        HttpResponseMessage response;
        try{
            response=await this.SendWithTimeoutAsync(request,cancellationToken).ConfigureAwait(false);
        }catch(Exception ex) when((ex is HttpRequestException or TimeoutException)&&HttpFreshness.CanServeStale(entry,now,rule.MaxStale)){
            LogServingStale(logger,request.RequestUri!,ex.GetType().Name);
            return CreateResponse(request,entry!,"stale");
        }

        if(response.StatusCode==HttpStatusCode.NotModified&&entry is not null){
            var updated=HttpFreshness.Revalidate(entry,response,now);
            response.Dispose();
            await this.TrySetAsync(updated,cancellationToken).ConfigureAwait(false);
            return CreateResponse(request,updated,"revalidated");
        }
        if((int)response.StatusCode>=500&&HttpFreshness.CanServeStale(entry,now,rule.MaxStale)){
            LogServingStale(logger,request.RequestUri!,((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
            response.Dispose();
            return CreateResponse(request,entry!,"stale");
        }
        if(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.PartialContent&&HttpFreshness.IsStorable(response)){
            byte[] body;
            try{
                body=await this.ReadBodyWithTimeoutAsync(response,cancellationToken).ConfigureAwait(false);
            }catch(Exception ex) when((ex is HttpRequestException or TimeoutException)&&HttpFreshness.CanServeStale(entry,now,rule.MaxStale)){
                //ヘッダーの後、本文の途中で切れた / 止まった場合も、通信失敗と同じく古いエントリを返す
                LogServingStale(logger,request.RequestUri!,ex.GetType().Name);
                response.Dispose();
                return CreateResponse(request,entry!,"stale");
            }
            var stored=HttpFreshness.CreateEntry(key,response,body,now);
            response.Dispose();
            await this.TrySetAsync(stored,cancellationToken).ConfigureAwait(false);
            return CreateResponse(request,stored,"miss");
        }
        return response;
    }

    private async Task<HttpResponseMessage> SendWithTimeoutAsync(HttpRequestMessage request,CancellationToken cancellationToken){
        using var timeout=new CancellationTokenSource(options.Timeout,time);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,timeout.Token);
        try{
            return await base.SendAsync(request,linked.Token).ConfigureAwait(false);
        }catch(OperationCanceledException ex) when(!cancellationToken.IsCancellationRequested&&timeout.IsCancellationRequested){
            throw new TimeoutException($"タイムアウトしました: {request.RequestUri}",ex);
        }
    }

    /// <summary>本文の読み込みにも、ヘッダー受信と同じタイムアウトを適用する(CacheAndRouting.md「構成」: タイムアウトもここで扱う)。</summary>
    private async Task<byte[]> ReadBodyWithTimeoutAsync(HttpResponseMessage response,CancellationToken cancellationToken){
        using var timeout=new CancellationTokenSource(options.Timeout,time);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,timeout.Token);
        try{
            return await response.Content.ReadAsByteArrayAsync(linked.Token).ConfigureAwait(false);
        }catch(OperationCanceledException ex) when(!cancellationToken.IsCancellationRequested&&timeout.IsCancellationRequested){
            throw new TimeoutException($"本文の読み込みがタイムアウトしました: {response.RequestMessage?.RequestUri}",ex);
        }
    }

    private async ValueTask<HttpCacheEntry?> TryGetAsync(string key,CancellationToken cancellationToken){
        try{
            return await store.GetAsync(key,cancellationToken).ConfigureAwait(false);
        }catch(IOException ex){
            LogStoreFailure(logger,key,ex);
            return null;
        }
    }

    private async ValueTask TrySetAsync(HttpCacheEntry entry,CancellationToken cancellationToken){
        try{
            await store.SetAsync(entry,cancellationToken).ConfigureAwait(false);
        }catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){
            //キャッシュの書き込み失敗は取得結果に影響させない(記録は残す。Windows では置き換えの拒否が UnauthorizedAccessException で届く)
            LogStoreFailure(logger,entry.Key,ex);
        }
    }

    private static HttpResponseMessage CreateResponse(HttpRequestMessage request,HttpCacheEntry entry,string state){
        var response=new HttpResponseMessage((HttpStatusCode)entry.StatusCode){
            RequestMessage=request,
            Content=new ByteArrayContent(entry.Body),
        };
        if(entry.ContentType is not null){
            response.Content.Headers.TryAddWithoutValidation("Content-Type",entry.ContentType);
        }
        response.Headers.TryAddWithoutValidation(CacheHeaders.State,state);
        response.Headers.TryAddWithoutValidation(CacheHeaders.ReceivedAt,entry.ReceivedAt.ToString("o",CultureInfo.InvariantCulture));
        return response;
    }

    [LoggerMessage(Level=LogLevel.Warning,Message="通信できないため古いキャッシュを返します: {Uri} ({Reason})")]
    private static partial void LogServingStale(ILogger logger,Uri uri,string reason);

    [LoggerMessage(Level=LogLevel.Warning,Message="キャッシュストアの読み書きに失敗しました: {Key}")]
    private static partial void LogStoreFailure(ILogger logger,string key,Exception exception);
}
