using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Weather.Core;

namespace Weather.Providers.Tests;

/// <summary>URL → 記録ファイルで応答し、送信内容を記録する HTTP ハンドラ。</summary>
internal sealed class FixtureHttpMessageHandler:HttpMessageHandler{
    public const string FixtureDate="2026-10-04";
    private readonly Dictionary<string,string> routes=new(StringComparer.Ordinal);

    public List<HttpRequestMessage> Requests{get;}=[];
    public Func<HttpRequestMessage,HttpResponseMessage?>? Override{get;set;}

    public static string FixturePath(string relative){
        return Path.Combine(AppContext.BaseDirectory,"Fixtures",FixtureDate,relative);
    }

    public FixtureHttpMessageHandler Map(string url,string relativeFile){
        this.routes[url]=relativeFile;
        return this;
    }

    /// <summary>記録データで応答する(Range 要求には 206 で該当部分だけを返す)。</summary>
    public static HttpResponseMessage Respond(HttpRequestMessage request,byte[] body){
        var status=HttpStatusCode.OK;
        if(request.Headers.Range?.Ranges.FirstOrDefault() is {} range){
            var from=(int)(range.From??0);
            var to=(int)Math.Min(range.To??body.Length-1,body.Length-1);
            body=body[from..(to+1)];
            status=HttpStatusCode.PartialContent;
        }
        var response=new HttpResponseMessage(status){
            Content=new ByteArrayContent(body),
            RequestMessage=request,
        };
        response.Headers.TryAddWithoutValidation("Cache-Control","max-age=60");
        return response;
    }

    public int CountRequests(string urlPrefix){
        return this.Requests.Count(r=>r.RequestUri!.AbsoluteUri.StartsWith(urlPrefix,StringComparison.Ordinal));
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){
        this.Requests.Add(request);
        if(this.Override?.Invoke(request) is {} overridden){
            return Task.FromResult(overridden);
        }
        if(this.routes.TryGetValue(request.RequestUri!.AbsoluteUri,out var file)){
            return Task.FromResult(Respond(request,File.ReadAllBytes(FixturePath(file))));
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound){RequestMessage=request});
    }
}

internal sealed class TestHost:IDisposable{
    public static readonly DateTimeOffset Now=new(2026,10,4,0,0,0,TimeSpan.Zero);

    private TestHost(ServiceProvider services,FixtureHttpMessageHandler handler,FakeTimeProvider time){
        this.Services=services;
        this.Handler=handler;
        this.Time=time;
    }

    public ServiceProvider Services{get;}
    public FixtureHttpMessageHandler Handler{get;}
    public FakeTimeProvider Time{get;}

    public static TestHost Create(Action<FixtureHttpMessageHandler>? map=null,Action<IServiceCollection>? configure=null){
        var handler=new FixtureHttpMessageHandler();
        map?.Invoke(handler);
        var time=new FakeTimeProvider(Now);
        var services=new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton<ILocationResolver,StubResolver>();
        services.AddSingleton<IJapanArea,StubJapanArea>();
        //GFS は記録データの小さい 1° を使う
        services.AddSingleton(new Gfs.GfsOptions{Resolution="1p00"});
        services.AddWeatherProviders(static o=>o.UserAgent="WeatherAppTests/1.0 github.com/test/test");
        foreach(var name in new[]{Jma.JmaProvider.HttpClientName,Nws.NwsProvider.HttpClientName,MetNorway.MetNorwayProvider.HttpClientName,Gfs.GfsProvider.HttpClientName}){
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(()=>handler);
        }
        configure?.Invoke(services);
        return new TestHost(services.BuildServiceProvider(),handler,time);
    }

    public T Get<T>() where T:notnull{
        return this.Services.GetRequiredService<T>();
    }

    public void Dispose(){
        this.Services.Dispose();
    }

    /// <summary>日本周辺のかわりに、北緯 24〜46 度・東経 122〜146 度の矩形を使う。</summary>
    internal sealed class StubJapanArea:IJapanArea{
        public bool Contains(double latitude,double longitude){
            return latitude is >=24 and <=46&&longitude is >=122 and <=146;
        }
    }

    private sealed class StubResolver:ILocationResolver{
        public ValueTask<ResolvedLocation> ResolveAsync(GeoPoint point,CancellationToken cancellationToken){
            return ValueTask.FromResult(Locations.World(point.Latitude,point.Longitude,"XX"));
        }
    }
}

internal static class Locations{
    public static ResolvedLocation Tokyo=>Japan("1310100","千代田区");

    public static ResolvedLocation Japan(string class20,string name){
        return new ResolvedLocation{
            Point=new GeoPoint(35.69,139.75),
            CountryCode="JP",
            DisplayName=name,
            TimeZoneId="Asia/Tokyo",
            JmaArea=new JmaAreaMatch(JmaAreaMatchKind.Inside,class20,0),
        };
    }

    public static ResolvedLocation JapanOutOfCoverage=>new(){
        Point=new GeoPoint(43.8,146.75),
        CountryCode="JP",
        DisplayName="色丹島",
        TimeZoneId="Asia/Tokyo",
        JmaArea=new JmaAreaMatch(JmaAreaMatchKind.OutOfCoverage,null,70),
    };

    public static ResolvedLocation Washington=>new(){
        Point=new GeoPoint(38.89,-77.04),
        CountryCode="US",
        DisplayName="Washington",
        TimeZoneId="America/New_York",
    };

    public static ResolvedLocation Oslo=>World(59.91,10.75,"NO","Europe/Oslo");

    public static ResolvedLocation World(double lat,double lon,string country,string zone="Etc/UTC"){
        return new ResolvedLocation{
            Point=new GeoPoint(lat,lon),
            CountryCode=country,
            DisplayName="test",
            TimeZoneId=zone,
        };
    }
}

internal static class Fixtures{
    public static void MapJmaTokyo(FixtureHttpMessageHandler h){
        h.Map("https://www.jma.go.jp/bosai/forecast/data/forecast/130000.json","jma/forecast_130000.json")
            .Map("https://www.jma.go.jp/bosai/jmatile/data/wdist/VPFD/130010.json","jma/wdist_130010.json")
            .Map("https://www.jma.go.jp/bosai/warning/data/r8/130000.json","jma/warning_130000.json")
            .Map("https://www.jma.go.jp/bosai/warning/data/r8/011000.json","jma/warning_011000.json")
            .Map("https://www.jma.go.jp/bosai/amedas/data/latest_time.txt","jma/latest_time.txt")
            .Map("https://www.jma.go.jp/bosai/amedas/data/point/44132/20261004_06.json","jma/amedas_44132_20261004_06.json");
    }

    public static void MapNws(FixtureHttpMessageHandler h){
        h.Map("https://api.weather.gov/points/38.89,-77.04","nws/points.json")
            .Map("https://api.weather.gov/gridpoints/LWX/96,71/forecast?units=si","nws/forecast.json")
            .Map("https://api.weather.gov/gridpoints/LWX/96,71/forecast/hourly?units=si","nws/hourly.json")
            .Map("https://api.weather.gov/gridpoints/LWX/96,71","nws/grid.json")
            .Map("https://api.weather.gov/gridpoints/LWX/96,71/stations","nws/stations.json")
            .Map("https://api.weather.gov/alerts/active?point=38.89,-77.04","nws/alerts_tx.json");
    }

    public static void MapMet(FixtureHttpMessageHandler h){
        h.Map("https://api.met.no/weatherapi/locationforecast/2.0/complete?lat=59.91&lon=10.75","met/complete.json");
    }

    /// <summary>GFS: 実行回・予報時間に関係なく同じ記録データ(1°、6 要素)で応答する。unavailableCycles の実行回は 404。</summary>
    public static Action<FixtureHttpMessageHandler> MapGfs(params string[] unavailableCycles){
        return h=>{
            var data=File.ReadAllBytes(FixtureHttpMessageHandler.FixturePath(GfsFixture.File));
            var index=File.ReadAllBytes(FixtureHttpMessageHandler.FixturePath(GfsFixture.File+".idx"));
            h.Override=request=>{
                var uri=request.RequestUri!;
                if(uri.Host!=Http.HttpCachePolicy.GfsHost){
                    return null;
                }
                if(unavailableCycles.Any(c=>uri.AbsolutePath.Contains(c,StringComparison.Ordinal))){
                    return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound){RequestMessage=request};
                }
                if(uri.AbsolutePath.EndsWith(".idx",StringComparison.Ordinal)){
                    return FixtureHttpMessageHandler.Respond(request,index);
                }
                return FixtureHttpMessageHandler.Respond(request,data);
            };
        };
    }

    /// <summary>気象庁の地図タイルの時刻一覧・アメダスの全地点・海上分布予報の風向(2026-10-04 17 時ごろ)。</summary>
    public static void MapJmaTiles(FixtureHttpMessageHandler h){
        const string root="https://www.jma.go.jp/bosai/jmatile/data/";
        h.Map(root+"nowc/targetTimes_N1.json","jmatile/nowc_N1.json")
            .Map(root+"nowc/targetTimes_N2.json","jmatile/nowc_N2.json")
            .Map(root+"rasrf/targetTimes.json","jmatile/rasrf.json")
            .Map(root+"wdist/targetTimes.json","jmatile/wdist.json")
            .Map(root+"umimesh/targetTimes.json","jmatile/umimesh.json")
            .Map(root+"umimesh/20261004000000/none/20261004060000/surf/wd/data.geojson","jmatile/umimesh_wd.geojson")
            .Map(root+"nowc/20261004081500/none/20261004081500/surf/hrpns/4/14/6.png","jmatile/hrpns_4_14_6.png")
            .Map("https://www.jma.go.jp/bosai/amedas/data/latest_time.txt","jmatile/amedas_latest_time.txt")
            .Map("https://www.jma.go.jp/bosai/amedas/data/map/20261004171000.json","jmatile/amedas_map.json");
    }
}
