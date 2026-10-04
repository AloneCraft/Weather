using System.Net;
using System.Net.Http;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Weather.Core;
using Weather.Providers;
using Weather.Providers.Jma;
using Weather.Providers.MetNorway;
using Weather.Providers.Nws;

namespace Weather.SceneLab;

/// <summary>
/// 描画調整ツール(SolutionStructure.md「SceneLab」)。SceneState をスライダーで直接操作するほか、
/// 記録データの予報を変換器に通して時間軸で再生できる。CPU 描画のため見た目の調整用(性能測定には使わない)。
/// </summary>
public static class Program{
    [STAThread]
    public static void Main(){
        var app=new Application();
        app.Run(new MainWindow());
    }
}

/// <summary>記録データ(テストのフィクスチャ)で応答する HTTP ハンドラ。</summary>
internal sealed class FixtureHandler:HttpMessageHandler{
    private static readonly Dictionary<string,string> Routes=new(StringComparer.Ordinal){
        ["https://www.jma.go.jp/bosai/forecast/data/forecast/130000.json"]="jma/forecast_130000.json",
        ["https://www.jma.go.jp/bosai/jmatile/data/wdist/VPFD/130010.json"]="jma/wdist_130010.json",
        ["https://api.weather.gov/points/38.89,-77.04"]="nws/points.json",
        ["https://api.weather.gov/gridpoints/LWX/96,71/forecast?units=si"]="nws/forecast.json",
        ["https://api.weather.gov/gridpoints/LWX/96,71/forecast/hourly?units=si"]="nws/hourly.json",
        ["https://api.weather.gov/gridpoints/LWX/96,71"]="nws/grid.json",
        ["https://api.met.no/weatherapi/locationforecast/2.0/complete?lat=59.91&lon=10.75"]="met/complete.json",
    };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){
        if(Routes.TryGetValue(request.RequestUri!.AbsoluteUri,out var file)){
            var path=Path.Combine(AppContext.BaseDirectory,"Fixtures",file);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(File.ReadAllBytes(path))});
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

internal static class FixtureForecasts{
    public static readonly (string Name,ResolvedLocation Location)[] Locations=[
        ("気象庁 東京(2026-10-04)",new ResolvedLocation{Point=new GeoPoint(35.69,139.75),CountryCode="JP",DisplayName="千代田区",TimeZoneId="Asia/Tokyo",JmaArea=new JmaAreaMatch(JmaAreaMatchKind.Inside,"1310100",0)}),
        ("NWS ワシントン(2026-10-04)",new ResolvedLocation{Point=new GeoPoint(38.89,-77.04),CountryCode="US",DisplayName="Washington",TimeZoneId="America/New_York"}),
        ("MET オスロ(2026-10-04)",new ResolvedLocation{Point=new GeoPoint(59.91,10.75),CountryCode="NO",DisplayName="Oslo",TimeZoneId="Europe/Oslo"}),
    ];

    public static async Task<Forecast> LoadAsync(int index){
        var services=new ServiceCollection();
        services.AddLogging();
        services.AddWeatherProviders();
        foreach(var name in new[]{JmaProvider.HttpClientName,NwsProvider.HttpClientName,MetNorwayProvider.HttpClientName}){
            services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(static ()=>new FixtureHandler());
        }
        await using var provider=services.BuildServiceProvider();
        var location=Locations[index].Location;
        IForecastProvider forecastProvider=index switch{
            0=>provider.GetRequiredService<JmaProvider>(),
            1=>provider.GetRequiredService<NwsProvider>(),
            _=>provider.GetRequiredService<MetNorwayProvider>(),
        };
        return await forecastProvider.GetForecastAsync(location,CancellationToken.None);
    }
}
