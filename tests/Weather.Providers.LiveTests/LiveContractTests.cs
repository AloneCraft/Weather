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

    public void Dispose(){
        this.services.Dispose();
    }
}
