using Microsoft.Extensions.DependencyInjection;
using Weather.Core;
using Weather.Geo;
using Weather.Providers;

namespace Weather.Providers.LiveTests;

/// <summary>
/// 実 API の契約テスト(TestStrategy.md)。Explicit のため通常の実行では動かない。
/// 実行: dotnet test --project tests/Weather.Providers.LiveTests -- --explicit only
/// 失敗したら新しい記録データを取り、差分を調査する(気象庁 bosai は予告なく変わる)。
/// </summary>
public sealed class LiveContractTests:IDisposable{
    private readonly ServiceProvider services;

    public LiveContractTests(){
        var collection=new ServiceCollection();
        collection.AddLogging();
        collection.AddWeatherProviders(static o=>o.UserAgent="WeatherAppLiveTests/0.1 github.com/AloneCraft/Weather");
        collection.AddWeatherGeo();
        this.services=collection.BuildServiceProvider();
    }

    private IWeatherService Weather=>this.services.GetRequiredService<IWeatherService>();

    [Theory(Explicit=true),Trait("Category","Live")]
    [InlineData(35.6940,139.7536,ProviderId.Jma)]     //東京(気象庁)
    [InlineData(43.0621,141.3544,ProviderId.Jma)]     //札幌(気象庁)
    [InlineData(40.7128,-74.0060,ProviderId.Nws)]     //ニューヨーク(NWS)
    [InlineData(21.3069,-157.8583,ProviderId.Nws)]    //ホノルル(NWS)
    [InlineData(51.5074,-0.1278,ProviderId.MetNorway)]//ロンドン(MET)
    [InlineData(-33.8688,151.2093,ProviderId.MetNorway)]//シドニー(MET)
    public async Task GetForecastAsync(double lat,double lon,ProviderId expected){
        var location=await this.Weather.ResolveAsync(new GeoPoint(lat,lon),TestContext.Current.CancellationToken);
        var result=await this.Weather.GetForecastAsync(location,TestContext.Current.CancellationToken);
        Assert.Equal(Availability.Available,result.Availability);
        var forecast=result.Forecast!;
        Assert.NotEmpty(forecast.Daily);
        Assert.NotEmpty(forecast.TimeSeries);
        Assert.Empty(forecast.Issues);
        Assert.All(forecast.Sources,s=>Assert.Equal(expected,s.Provider));
        //変換表に未知のコードがない(Condition が付かない区間・日がない)
        Assert.All(forecast.Daily,static d=>Assert.NotNull(d.Condition));
        Assert.All(forecast.TimeSeries.Take(24),static p=>Assert.NotNull(p.Condition));
    }

    [Theory(Explicit=true),Trait("Category","Live")]
    [InlineData(35.6940,139.7536)]
    [InlineData(45.4152,141.6731)]   //稚内
    [InlineData(40.7128,-74.0060)]
    public async Task GetAlertsAsync(double lat,double lon){
        var location=await this.Weather.ResolveAsync(new GeoPoint(lat,lon),TestContext.Current.CancellationToken);
        var result=await this.Weather.GetAlertsAsync(location,TestContext.Current.CancellationToken);
        Assert.Equal(Availability.Available,result.Availability);
        Assert.NotNull(result.Alerts);
    }

    [Theory(Explicit=true),Trait("Category","Live")]
    [InlineData(35.6940,139.7536)]
    [InlineData(38.89,-77.04)]
    public async Task GetObservationsAsync(double lat,double lon){
        var location=await this.Weather.ResolveAsync(new GeoPoint(lat,lon),TestContext.Current.CancellationToken);
        var stations=await this.Weather.FindStationsAsync(location,1,TestContext.Current.CancellationToken);
        var station=Assert.Single(stations);
        var now=TimeProvider.System.GetUtcNow();
        var series=await this.Weather.GetObservationsAsync(station,now.AddHours(-6),now,TestContext.Current.CancellationToken);
        Assert.NotEmpty(series.Items);
    }

    private IMapDataService Maps=>this.services.GetRequiredService<IMapDataService>();

    /// <summary>地図: GFS の最新の実行回と各 targetTimes、アメダスの時刻がそろう。</summary>
    [Fact(Explicit=true),Trait("Category","Live")]
    public async Task GetAvailabilityAsync(){
        var a=await this.Maps.GetAvailabilityAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(a.GfsReferenceTime);
        Assert.Equal(40,a.Gfs.Count);
        Assert.NotEmpty(a.Nowcast);
        Assert.NotEmpty(a.RainForecast);
        Assert.NotEmpty(a.Distribution);
        Assert.NotEmpty(a.Marine);
        Assert.NotNull(a.AmedasTime);
    }

    /// <summary>地図の各層: GFS の格子(0.5°、日本周辺は NaN)と気象庁のデータ。現在時刻の風・気温はアメダス。</summary>
    [Theory(Explicit=true),Trait("Category","Live")]
    [InlineData(FieldLayer.Wind)]
    [InlineData(FieldLayer.Precipitation)]
    [InlineData(FieldLayer.Temperature)]
    [InlineData(FieldLayer.Clouds)]
    public async Task GetFrameAsync(FieldLayer layer){
        var ct=TestContext.Current.CancellationToken;
        var a=await this.Maps.GetAvailabilityAsync(ct);
        var at=a.AmedasTime!.Value;
        var frame=await this.Maps.GetFrameAsync(layer,at,ct);
        Assert.Empty(frame.Issues);
        Assert.NotNull(frame.Scalar);
        Assert.Equal(new GridGeometry(720,361,90,0,0.5,0.5),frame.Scalar.Geometry);
        Assert.True(float.IsNaN(frame.Scalar.Sample(35.69,139.75)));
        Assert.False(float.IsNaN(frame.Scalar.Sample(48.85,2.35)));
        //雲(天気分布予報の天気)は次の 3 時間ごとの時刻からで、現在時刻には該当がないことがある
        if(layer!=FieldLayer.Clouds){
            Assert.Equal(JapanCoverage.Available,frame.Japan);
        }
        if(layer==FieldLayer.Wind){
            Assert.Equal(ArrowKind.Observation,frame.Arrows!.Kind);
            Assert.True(frame.Arrows.Arrows.Count>500);
        }
        if(layer==FieldLayer.Temperature){
            Assert.True(frame.Points!.Points.Count>500);
        }
        foreach(var tile in frame.Tiles){
            var z=tile.TileZoomFor(6);
            var n=1<<z;
            var png=await this.Maps.GetTileAsync(tile,z,(int)(0.888*n),(int)(0.4*n),ct);
            Assert.NotNull(png);
        }
    }

    public void Dispose(){
        this.services.Dispose();
    }
}
