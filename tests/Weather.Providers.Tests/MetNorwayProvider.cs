using Microsoft.Extensions.DependencyInjection;
using Weather.Core;
using Target=Weather.Providers.MetNorway.MetNorwayProvider;

namespace Weather.Providers.Tests;

public class MetNorwayProvider{
    [Fact,Trait("Category","Unit")]public async Task GetForecastAsync(){
        using var host=TestHost.Create(Fixtures.MapMet);
        var forecast=await host.Get<Target>().GetForecastAsync(Locations.Oslo,TestContext.Current.CancellationToken);
        {
            //時系列: 区間が重ならず隙間なく連続する(1 時間 → 6 時間の切り替わりを含む)
            var series=forecast.TimeSeries;
            Assert.True(series.Count>50);
            for(var i=1;i<series.Count;i++){
                Assert.Equal(series[i-1].End,series[i].Start);
            }
            Assert.Contains(series,static p=>p.Duration==TimeSpan.FromHours(1));
            Assert.Contains(series,static p=>p.Duration==TimeSpan.FromHours(6));
        }
        {
            //complete の値(霧・雷確率)を使う
            Assert.Contains(forecast.TimeSeries,static p=>p.FogPercent is not null);
            Assert.Contains(forecast.TimeSeries,static p=>p.ThunderProbability is not null);
            Assert.All(forecast.TimeSeries,static p=>Assert.Equal(DataProcessing.None,p.Source.Processing));
        }
        {
            //日別はアプリ側の集計(Aggregated)で、最高 ≧ 最低
            Assert.NotEmpty(forecast.Daily);
            Assert.All(forecast.Daily,static d=>Assert.True(d.Source.Processing.HasFlag(DataProcessing.Aggregated)));
            Assert.All(forecast.Daily.Where(static d=>d.TempMaxC is not null&&d.TempMinC is not null),static d=>Assert.True(d.TempMaxC>=d.TempMinC));
        }
        {
            //座標は小数 2 桁、ライセンスは CC BY 4.0
            Assert.Contains(host.Handler.Requests,static r=>r.RequestUri!.Query=="?lat=59.91&lon=10.75");
            Assert.Contains("CC BY 4.0",forecast.TimeSeries[0].Source.License.Name,StringComparison.Ordinal);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task GetForecastAsync_JapanArea(){
        using var host=TestHost.Create(Fixtures.MapMet);
        {
            //方針 5: 日本周辺域では MET を使わない(Router と二重に防ぐ)
            await Assert.ThrowsAsync<InvalidOperationException>(async ()=>await host.Get<Target>().GetForecastAsync(Locations.JapanOutOfCoverage,TestContext.Current.CancellationToken));
            Assert.Empty(host.Handler.Requests);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task GetForecastAsync_NotInUse(){
        //MET の規約: アプリが使用中でないとき(背景更新・ウィジェット)は通信せずに NotAllowed で失敗する
        using var host=TestHost.Create(Fixtures.MapMet,static s=>s.AddSingleton<IAppActivity>(new Activity(false)));
        var ex=await Assert.ThrowsAsync<WeatherProviderException>(async ()=>await host.Get<Target>().GetForecastAsync(Locations.Oslo,TestContext.Current.CancellationToken));
        Assert.Equal(ProviderFailure.NotAllowed,ex.Failure);
        Assert.Empty(host.Handler.Requests);
    }

    private sealed class Activity(bool inUse):IAppActivity{
        public bool IsInUse=>inUse;
    }
}
