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

    public int CountRequests(string urlPrefix){
        return this.Requests.Count(r=>r.RequestUri!.AbsoluteUri.StartsWith(urlPrefix,StringComparison.Ordinal));
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){
        this.Requests.Add(request);
        if(this.Override?.Invoke(request) is {} overridden){
            return Task.FromResult(overridden);
        }
        if(this.routes.TryGetValue(request.RequestUri!.AbsoluteUri,out var file)){
            var response=new HttpResponseMessage(HttpStatusCode.OK){
                Content=new ByteArrayContent(File.ReadAllBytes(FixturePath(file))),
                RequestMessage=request,
            };
            response.Headers.TryAddWithoutValidation("Cache-Control","max-age=60");
            return Task.FromResult(response);
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

    public static TestHost Create(Action<FixtureHttpMessageHandler>? map=null){
        var handler=new FixtureHttpMessageHandler();
        map?.Invoke(handler);
        var time=new FakeTimeProvider(Now);
        var services=new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton<ILocationResolver,StubResolver>();
        services.AddWeatherProviders(static o=>o.UserAgent="WeatherAppTests/1.0 github.com/test/test");
        foreach(var name in new[]{Jma.JmaProvider.HttpClientName,Nws.NwsProvider.HttpClientName,MetNorway.MetNorwayProvider.HttpClientName}){
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(()=>handler);
        }
        return new TestHost(services.BuildServiceProvider(),handler,time);
    }

    public T Get<T>() where T:notnull{
        return this.Services.GetRequiredService<T>();
    }

    public void Dispose(){
        this.Services.Dispose();
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
}
