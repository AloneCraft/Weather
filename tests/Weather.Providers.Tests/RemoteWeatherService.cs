using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Weather.Core;
using Weather.Remote.Server;

namespace Weather.Providers.Tests;

/// <summary>
/// Phase 4 の中継(Functions)の結合テスト: 記録データ → 実際の Provider → WeatherApi → JSON → RemoteWeatherService。
/// 3 機関の実データの全項目が電文を往復しても失われないことを確かめる(Weather.Remote.Tests は合成データでの単体テスト)。
/// </summary>
public class RemoteWeatherService{
    [Fact,Trait("Category","Unit")]public async Task GetForecastAsync(){
        var ct=TestContext.Current.CancellationToken;
        foreach(var (location,map) in new (ResolvedLocation,Action<FixtureHttpMessageHandler>)[]{(Locations.Tokyo,Fixtures.MapJmaTokyo),(Locations.Washington,Fixtures.MapNws),(Locations.Oslo,Fixtures.MapMet)}){
            //直接取得と中継経由の結果が同じ(時系列・日別・出典・部分結果)
            using var host=Host(location,map);
            var direct=(await host.Get<IWeatherService>().GetForecastAsync(location,ct)).Forecast!;
            var remote=Client(host);
            var resolved=await remote.ResolveAsync(location.Point,ct);
            var relayed=(await remote.GetForecastAsync(resolved,ct)).Forecast!;
            Assert.Equal(direct.TimeSeries,relayed.TimeSeries);
            Assert.Equal(direct.Daily.Count,relayed.Daily.Count);
            for(var i=0;i<direct.Daily.Count;i++){
                Assert.Equal(direct.Daily[i] with{Parts=[]},relayed.Daily[i] with{Parts=[]});
                Assert.Equal(direct.Daily[i].Parts,relayed.Daily[i].Parts);
            }
            Assert.Equal(direct.Issues,relayed.Issues);
            Assert.Equal(direct.Location,relayed.Location);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task GetAlertsAsync(){
        //警報の名称・見出し・出典が中継を経ても原文のまま(方針 5)
        var ct=TestContext.Current.CancellationToken;
        using var host=Host(Locations.Tokyo,Fixtures.MapJmaTokyo);
        var direct=(await host.Get<IWeatherService>().GetAlertsAsync(Locations.Tokyo,ct)).Alerts!;
        var remote=Client(host);
        var relayed=(await remote.GetAlertsAsync(await remote.ResolveAsync(Locations.Tokyo.Point,ct),ct)).Alerts!;
        Assert.Equal(direct.Alerts,relayed.Alerts);
        Assert.Equal(direct.Source,relayed.Source);
        Assert.Equal(direct.Headline,relayed.Headline);
    }

    /// <summary>サーバー側の地点解決を、テスト用の地点に固定する。</summary>
    private static TestHost Host(ResolvedLocation location,Action<FixtureHttpMessageHandler> map){
        return TestHost.Create(map,s=>s.AddSingleton<ILocationResolver>(new FixedResolver(location)));
    }

    private static Remote.RemoteWeatherService Client(TestHost host){
        var handler=new InProcessHandler(new WeatherApi(host.Get<IWeatherService>()));
        return new Remote.RemoteWeatherService(new HttpClient(handler){BaseAddress=new Uri("https://example.test/api/")});
    }

    private sealed class FixedResolver(ResolvedLocation location):ILocationResolver{
        public ValueTask<ResolvedLocation> ResolveAsync(GeoPoint point,CancellationToken cancellationToken){
            return ValueTask.FromResult(location);
        }
    }

    /// <summary>HTTP を経由せずに WeatherApi へ渡す(Weather.Functions の WeatherFunctions と同じ変換)。</summary>
    private sealed class InProcessHandler(WeatherApi api):HttpMessageHandler{
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){
            var uri=request.RequestUri!;
            var query=System.Web.HttpUtility.ParseQueryString(uri.Query);
            var route=uri.AbsolutePath["/api/".Length..];
            var result=await api.HandleAsync(route,name=>query[name],cancellationToken);
            return new HttpResponseMessage((HttpStatusCode)result.Status){Content=new StringContent(result.Json,Encoding.UTF8,"application/json")};
        }
    }
}
