using System.Net;
using Weather.Core;
using Target=Weather.Providers.Nws.NwsProvider;

namespace Weather.Providers.Tests;

public class HttpCacheHandler{
    private const string PointsUrl="https://api.weather.gov/points/38.89,-77.04";

    [Fact,Trait("Category","Unit")]public async Task SendAsync(){
        {
            //鮮度内(max-age=60)ならネットワークに出ない
            using var host=TestHost.Create(Fixtures.MapNws);
            var client=host.Get<IHttpClientFactory>().CreateClient(Target.HttpClientName);
            await client.GetAsync(new Uri(PointsUrl),TestContext.Current.CancellationToken);
            host.Time.Advance(TimeSpan.FromSeconds(30));
            var second=await client.GetAsync(new Uri(PointsUrl),TestContext.Current.CancellationToken);
            Assert.Equal(1,host.Handler.CountRequests(PointsUrl));
            Assert.Equal("hit",string.Join("",second.Headers.GetValues(Http.CacheHeaders.State)));
        }
        {
            //期限切れは条件付き GET。If-Modified-Since は受信した Last-Modified と完全一致、304 で本文を再利用
            using var host=TestHost.Create();
            const string lastModified="Sat, 03 Oct 2026 21:42:41 GMT";
            host.Handler.Override=r=>{
                if(r.Headers.TryGetValues("If-Modified-Since",out var values)){
                    Assert.Equal(lastModified,string.Join("",values));
                    Assert.Equal("\"abc\"",string.Join("",r.Headers.GetValues("If-None-Match")));
                    return new HttpResponseMessage(HttpStatusCode.NotModified);
                }
                var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"v\":1}")};
                response.Content.Headers.TryAddWithoutValidation("Last-Modified",lastModified);
                response.Headers.TryAddWithoutValidation("ETag","\"abc\"");
                response.Headers.TryAddWithoutValidation("Cache-Control","max-age=60");
                return response;
            };
            var client=host.Get<IHttpClientFactory>().CreateClient(Target.HttpClientName);
            await client.GetAsync(new Uri(PointsUrl),TestContext.Current.CancellationToken);
            host.Time.Advance(TimeSpan.FromMinutes(5));
            var revalidated=await client.GetAsync(new Uri(PointsUrl),TestContext.Current.CancellationToken);
            Assert.Equal(2,host.Handler.Requests.Count);
            Assert.Equal("revalidated",string.Join("",revalidated.Headers.GetValues(Http.CacheHeaders.State)));
            Assert.Equal("{\"v\":1}",await revalidated.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
        {
            //通信できないときは許容期間内の古いエントリを印付きで返す
            using var host=TestHost.Create(Fixtures.MapNws);
            var provider=host.Get<Target>();
            var fresh=await provider.GetForecastAsync(Locations.Washington,TestContext.Current.CancellationToken);
            Assert.False(fresh.IsStale);
            host.Time.Advance(TimeSpan.FromDays(2));
            host.Handler.Override=static _=>throw new HttpRequestException("offline");
            var stale=await provider.GetForecastAsync(Locations.Washington,TestContext.Current.CancellationToken);
            Assert.True(stale.IsStale);
            Assert.Equal(TestHost.Now,stale.Daily[0].Source.RetrievedAt);
        }
        {
            //ヘッダーを受け取った後、本文の途中で通信が切れたときも、許容期間内の古いエントリを返す
            using var host=TestHost.Create(Fixtures.MapNws);
            var provider=host.Get<Target>();
            await provider.GetForecastAsync(Locations.Washington,TestContext.Current.CancellationToken);
            host.Time.Advance(TimeSpan.FromDays(2));
            host.Handler.Override=static _=>new HttpResponseMessage(HttpStatusCode.OK){Content=new BrokenContent()};
            var stale=await provider.GetForecastAsync(Locations.Washington,TestContext.Current.CancellationToken);
            Assert.True(stale.IsStale);
            Assert.Equal(TestHost.Now,stale.Daily[0].Source.RetrievedAt);
        }
        {
            //本文が止まったままでもタイムアウトで打ち切り、古いエントリを返す
            using var host=TestHost.Create(Fixtures.MapNws);
            var provider=host.Get<Target>();
            await provider.GetForecastAsync(Locations.Washington,TestContext.Current.CancellationToken);
            host.Time.Advance(TimeSpan.FromDays(2));
            host.Handler.Override=static _=>new HttpResponseMessage(HttpStatusCode.OK){Content=new StalledContent()};
            var task=provider.GetForecastAsync(Locations.Washington,TestContext.Current.CancellationToken);
            for(var i=0;i<500&&!task.IsCompleted;i++){
                host.Time.Advance(TimeSpan.FromSeconds(5));
                await Task.Delay(10,TestContext.Current.CancellationToken);
            }
            Assert.True(task.IsCompleted);
            var stale=await task;
            Assert.True(stale.IsStale);
        }
        {
            //本文の途中で切れて古いエントリもなければ Network
            using var host=TestHost.Create();
            host.Handler.Override=static _=>new HttpResponseMessage(HttpStatusCode.OK){Content=new BrokenContent()};
            var ex=await Assert.ThrowsAsync<WeatherProviderException>(async ()=>await host.Get<Target>().GetAlertsAsync(Locations.Washington,TestContext.Current.CancellationToken));
            Assert.Equal(ProviderFailure.Network,ex.Failure);
        }
        {
            //警報は 6 時間を超えた古いキャッシュを返さない
            using var host=TestHost.Create(Fixtures.MapNws);
            var provider=host.Get<Target>();
            await provider.GetAlertsAsync(Locations.Washington,TestContext.Current.CancellationToken);
            host.Time.Advance(TimeSpan.FromHours(7));
            host.Handler.Override=static _=>throw new HttpRequestException("offline");
            var ex=await Assert.ThrowsAsync<WeatherProviderException>(async ()=>await provider.GetAlertsAsync(Locations.Washington,TestContext.Current.CancellationToken));
            Assert.Equal(ProviderFailure.Network,ex.Failure);
        }
        {
            //5xx で古いキャッシュがなければ ServerError
            using var host=TestHost.Create();
            host.Handler.Override=static _=>new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            var ex=await Assert.ThrowsAsync<WeatherProviderException>(async ()=>await host.Get<Target>().GetAlertsAsync(Locations.Washington,TestContext.Current.CancellationToken));
            Assert.Equal(ProviderFailure.ServerError,ex.Failure);
        }
    }
}

/// <summary>ヘッダーは届くが、本文の途中で接続が切れる応答の再現用。</summary>
internal sealed class BrokenContent:HttpContent{
    protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context){
        throw new IOException("connection reset");
    }

    protected override bool TryComputeLength(out long length){
        length=0;
        return false;
    }
}

/// <summary>ヘッダーは届くが、本文が止まったまま進まない応答の再現用(取り消されるまで待つ)。</summary>
internal sealed class StalledContent:HttpContent{
    protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context){
        return Task.Delay(Timeout.Infinite);
    }

    protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context,CancellationToken cancellationToken){
        return Task.Delay(Timeout.Infinite,cancellationToken);
    }

    protected override bool TryComputeLength(out long length){
        length=0;
        return false;
    }
}

public class HttpFreshness{
    [Fact,Trait("Category","Unit")]public void IsFresh(){
        var now=TestHost.Now;
        {
            //max-age と Age ヘッダーから経過時間を計算する
            var entry=Entry(now)with{MaxAge=TimeSpan.FromSeconds(60),Age=TimeSpan.FromSeconds(50)};
            Assert.True(Http.HttpFreshness.IsFresh(entry,now.AddSeconds(5),TimeSpan.Zero));
            Assert.False(Http.HttpFreshness.IsFresh(entry,now.AddSeconds(15),TimeSpan.Zero));
        }
        {
            //max-age がなければ Expires − Date
            var entry=Entry(now)with{Date=now,Expires=now.AddMinutes(30)};
            Assert.True(Http.HttpFreshness.IsFresh(entry,now.AddMinutes(29),TimeSpan.Zero));
            Assert.False(Http.HttpFreshness.IsFresh(entry,now.AddMinutes(31),TimeSpan.Zero));
        }
        {
            //定義データは最低 7 日は新鮮とみなす
            var entry=Entry(now)with{MaxAge=TimeSpan.FromSeconds(60)};
            Assert.True(Http.HttpFreshness.IsFresh(entry,now.AddDays(6),TimeSpan.FromDays(7)));
        }
    }

    private static HttpCacheEntry Entry(DateTimeOffset now){
        return new HttpCacheEntry{Key="k",StatusCode=200,Body=[],ReceivedAt=now};
    }
}
