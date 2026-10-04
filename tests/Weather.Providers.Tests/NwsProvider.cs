using Weather.Core;
using Target=Weather.Providers.Nws.NwsProvider;

namespace Weather.Providers.Tests;

public class NwsProvider{
    [Fact,Trait("Category","Unit")]public async Task GetForecastAsync(){
        using var host=TestHost.Create(Fixtures.MapNws);
        var forecast=await host.Get<Target>().GetForecastAsync(Locations.Washington,TestContext.Current.CancellationToken);
        {
            //日別: 昼と夜の period を現地日付でまとめる(先頭は Tonight のみ)
            Assert.NotEmpty(forecast.Daily);
            var first=forecast.Daily[0];
            Assert.Null(first.TempMaxC);
            Assert.NotNull(first.TempMinC);
            Assert.Equal("Tonight",first.Parts[0].Label);
            Assert.All(forecast.Daily.Skip(1).Take(5),static d=>Assert.Equal(2,d.Parts.Count));
        }
        {
            //時系列: hourly を土台にグリッドで雲量・雷確率を補い、風速は m/s に換算
            Assert.True(forecast.TimeSeries.Count>100);
            var point=forecast.TimeSeries[0];
            Assert.NotNull(point.CloudCoverPercent);
            Assert.NotNull(point.WindSpeedMs);
            Assert.True(point.Source.Processing.HasFlag(DataProcessing.UnitConverted));
            Assert.InRange(point.TemperatureC!.Value,-40,50);
        }
        {
            //タイムゾーンは points の値で上書き、区域名は relativeLocation
            Assert.Equal("America/New_York",forecast.Location.TimeZoneId);
            Assert.Contains("Washington",forecast.AreaName,StringComparison.Ordinal);
            Assert.Empty(forecast.Issues);
        }
        {
            //座標は小数 2 桁以下で送る
            Assert.Contains(host.Handler.Requests,static r=>r.RequestUri!.AbsoluteUri=="https://api.weather.gov/points/38.89,-77.04");
        }
    }

    [Fact,Trait("Category","Unit")]public async Task GetAlertsAsync(){
        using var host=TestHost.Create(Fixtures.MapNws);
        var alerts=await host.Get<Target>().GetAlertsAsync(Locations.Washington,TestContext.Current.CancellationToken);
        {
            //CAP の event 名から Tier を決め、本文はそのまま保持する
            Assert.NotEmpty(alerts.Alerts);
            var warning=alerts.Alerts.First(static a=>a.EventName=="Flash Flood Warning");
            Assert.Equal(AlertTier.Warning,warning.Tier);
            Assert.Equal("Severe",warning.SourceSeverity);
            Assert.False(string.IsNullOrEmpty(warning.Description));
        }
    }

    [Fact,Trait("Category","Unit")]public async Task FindStationsAsync(){
        using var host=TestHost.Create(Fixtures.MapNws);
        var stations=await host.Get<Target>().FindStationsAsync(Locations.Washington,3,TestContext.Current.CancellationToken);
        {
            //points の observationStations の先頭から返す
            Assert.Equal(3,stations.Count);
            Assert.All(stations,static s=>Assert.Equal(ProviderId.Nws,s.Provider));
        }
    }

    [Fact,Trait("Category","Unit")]public async Task GetObservationsAsync(){
        using var host=TestHost.Create(static h=>h.Override=static r=>{
            if(r.RequestUri!.AbsolutePath=="/stations/KDCA/observations"){
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new ByteArrayContent(File.ReadAllBytes(FixtureHttpMessageHandler.FixturePath("nws/observations.json")))};
            }
            return null;
        });
        var station=new ObservationStation("KDCA",ProviderId.Nws,"Washington/Reagan National Airport",new GeoPoint(38.85,-77.03),5);
        var series=await host.Get<Target>().GetObservationsAsync(station,TestHost.Now.AddDays(-2),TestHost.Now,TestContext.Current.CancellationToken);
        {
            //時刻の昇順・重複なし、単位を SI に換算(気圧は hPa)
            Assert.NotEmpty(series.Items);
            Assert.All(series.Items.Where(static o=>o.SeaLevelPressureHpa is not null),static o=>Assert.InRange(o.SeaLevelPressureHpa!.Value.Value,900,1100));
            Assert.Empty(series.DailySummaries);
        }
    }
}
