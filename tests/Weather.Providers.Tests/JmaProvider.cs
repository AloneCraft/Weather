using Weather.Core;
using Target=Weather.Providers.Jma.JmaProvider;

namespace Weather.Providers.Tests;

public class JmaProvider{
    [Fact,Trait("Category","Unit")]public async Task GetForecastAsync(){
        using var host=TestHost.Create(Fixtures.MapJmaTokyo);
        var provider=host.Get<Target>();
        var forecast=await provider.GetForecastAsync(Locations.Tokyo,TestContext.Current.CancellationToken);
        {
            //日別: 今日・明日は府県天気予報(05 時発表)、明後日以降は週間予報
            Assert.Equal(7,forecast.Daily.Count);
            var today=forecast.Daily[0];
            Assert.Equal(new DateOnly(2026,10,4),today.Date);
            Assert.Equal("府県天気予報",today.Source.ProductName);
            Assert.Equal("201",today.SourceCode);
            Assert.Equal("くもり　昼過ぎ　まで　時々　晴れ",today.WeatherText);
            Assert.Equal(24,today.TempMaxC);
            Assert.Null(today.TempMinC);
            Assert.Equal(new[]{0,10,20},today.Parts.Select(static p=>p.PrecipitationProbability??-1));
            Assert.Equal(ConditionTransition.Occasionally,today.Condition!.Value.Transition);
        }
        {
            //05 時発表の明日の気温は temps[2] / temps[3](気象庁サイトの表示ロジック)
            var tomorrow=forecast.Daily[1];
            Assert.Equal(17,tomorrow.TempMinC);
            Assert.Equal(22,tomorrow.TempMaxC);
            Assert.Null(tomorrow.TemperatureSource);
            Assert.Equal(4,tomorrow.Parts.Count);
        }
        {
            //週間予報の日: 信頼度・予測範囲・降水確率
            var day3=forecast.Daily[2];
            Assert.Equal("府県週間天気予報",day3.Source.ProductName);
            Assert.Equal(ForecastReliability.B,day3.Reliability);
            Assert.Equal(28,day3.TempMaxC);
            Assert.Equal(19,day3.TempMinC);
            Assert.NotNull(day3.TempMaxRangeC);
            Assert.Equal(40,day3.PrecipitationProbability);
            Assert.Equal("東京",day3.TemperaturePointName);
        }
        {
            //時系列予報: 3 時間区間が連続し、気温と風速階級を持つ
            Assert.Equal(14,forecast.TimeSeries.Count);
            Assert.All(forecast.TimeSeries,static p=>Assert.Equal(TimeSpan.FromHours(3),p.Duration));
            Assert.All(forecast.TimeSeries,static p=>Assert.NotNull(p.TemperatureC));
            Assert.Equal(new ValueRange(3,5),forecast.TimeSeries[0].WindSpeedRangeMs);
            Assert.Equal("時系列予報",forecast.TimeSeries[0].Source.ProductName);
        }
        {
            //出典: 気象庁・公共データ利用規約・加工なし
            Assert.All(forecast.Sources,static s=>Assert.Equal(ProviderId.Jma,s.Provider));
            Assert.All(forecast.Sources,static s=>Assert.Equal(DataProcessing.None,s.Processing));
            Assert.Equal("東京地方",forecast.AreaName);
            Assert.Empty(forecast.Issues);
        }
        {
            //User-Agent を必ず付ける
            Assert.All(host.Handler.Requests,static r=>Assert.Equal("WeatherAppTests/1.0 github.com/test/test",string.Join(" ",r.Headers.GetValues("User-Agent"))));
        }
    }

    [Fact,Trait("Category","Unit")]public async Task GetForecastAsync_SecondaryFailure(){
        {
            //時系列予報が取れなくても日別は返し、Issues に記録する
            using var host=TestHost.Create(static h=>h.Map("https://www.jma.go.jp/bosai/forecast/data/forecast/130000.json","jma/forecast_130000.json"));
            var forecast=await host.Get<Target>().GetForecastAsync(Locations.Tokyo,TestContext.Current.CancellationToken);
            Assert.Empty(forecast.TimeSeries);
            Assert.NotEmpty(forecast.Daily);
            var issue=Assert.Single(forecast.Issues);
            Assert.Equal("時系列予報",issue.ProductName);
        }
        {
            //主プロダクトが取れなければ例外
            using var host=TestHost.Create();
            var ex=await Assert.ThrowsAsync<WeatherProviderException>(async ()=>await host.Get<Target>().GetForecastAsync(Locations.Tokyo,TestContext.Current.CancellationToken));
            Assert.Equal(ProviderFailure.NotFound,ex.Failure);
        }
        {
            //対象外の地点は NotFound
            using var host=TestHost.Create(Fixtures.MapJmaTokyo);
            var ex=await Assert.ThrowsAsync<WeatherProviderException>(async ()=>await host.Get<Target>().GetForecastAsync(Locations.JapanOutOfCoverage,TestContext.Current.CancellationToken));
            Assert.Equal(ProviderFailure.NotFound,ex.Failure);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task GetAlertsAsync(){
        using var host=TestHost.Create(Fixtures.MapJmaTokyo);
        var provider=host.Get<Target>();
        {
            //稚内市: 濃霧注意報が継続中(r8 形式、複数電文の統合)
            var alerts=await provider.GetAlertsAsync(Locations.Japan("0121400","稚内市"),TestContext.Current.CancellationToken);
            var alert=Assert.Single(alerts.Active);
            Assert.Equal("濃霧注意報",alert.EventName);
            Assert.Equal(AlertTier.Advisory,alert.Tier);
            Assert.Null(alert.WarningLevel);
            Assert.Equal("気象警報・注意報",alert.Source.ProductName);
        }
        {
            //千代田区: 発表中の警報・注意報はない(空の一覧 = 警報なし)
            var alerts=await provider.GetAlertsAsync(Locations.Tokyo,TestContext.Current.CancellationToken);
            Assert.Empty(alerts.Active);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task FindStationsAsync(){
        using var host=TestHost.Create();
        var stations=await host.Get<Target>().FindStationsAsync(Locations.Tokyo,3,TestContext.Current.CancellationToken);
        {
            //最寄りは東京(44132)
            Assert.Equal("44132",stations[0].Id);
            Assert.Equal(ProviderId.Jma,stations[0].Provider);
            Assert.True(stations.Count<=3);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task GetObservationsAsync(){
        using var host=TestHost.Create(Fixtures.MapJmaTokyo);
        var provider=host.Get<Target>();
        var station=new ObservationStation("44132",ProviderId.Jma,"東京",new GeoPoint(35.69,139.75),25);
        var series=await provider.GetObservationsAsync(station,new DateTimeOffset(2026,10,4,6,0,0,TimeSpan.FromHours(9)),new DateTimeOffset(2026,10,4,9,0,0,TimeSpan.FromHours(9)),TestContext.Current.CancellationToken);
        {
            //10 分ごとの観測値を時刻順に返す
            Assert.NotEmpty(series.Items);
            Assert.All(series.Items,static o=>Assert.NotNull(o.TemperatureC));
            Assert.Equal("アメダス",series.Source.ProductName);
        }
        {
            //気象庁が計算した日最高・最低を日ごとの要約として返す
            var summary=Assert.Single(series.DailySummaries);
            Assert.False(summary.IsDerived);
            Assert.NotNull(summary.MaxTempC);
        }
    }
}
