using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Weather.Core;
using Weather.Providers.Routing;
using Target=Weather.Providers.Routing.WeatherProviderRouter;

namespace Weather.Providers.Tests;

public class WeatherProviderRouter{
    [Fact,Trait("Category","Unit")]public void RouteForecast(){
        using var host=TestHost.Create();
        var router=host.Get<Target>();
        {
            //日本の区域内は気象庁のみ(代替なし)
            var route=router.RouteForecast(Locations.Tokyo);
            Assert.Equal(ProviderId.Jma,route.Primary!.Id);
            Assert.Null(route.FallbackOnNotFound);
        }
        {
            //日本周辺域の対象外はどの Provider にも振り分けない
            var route=router.RouteForecast(Locations.JapanOutOfCoverage);
            Assert.Null(route.Primary);
            Assert.Equal(RouteReason.JapanOutOfCoverage,route.Reason);
        }
        {
            //米国は NWS、範囲外なら MET
            var route=router.RouteForecast(Locations.Washington);
            Assert.Equal(ProviderId.Nws,route.Primary!.Id);
            Assert.Equal(ProviderId.MetNorway,route.FallbackOnNotFound!.Id);
        }
        {
            //その他は MET
            Assert.Equal(ProviderId.MetNorway,router.RouteForecast(Locations.Oslo).Primary!.Id);
        }
        {
            //不変条件: 日本周辺域では国コードに関係なく MET / NWS を選ばない
            foreach(var country in new[]{"JP","US","XX"}){
                var location=Locations.JapanOutOfCoverage with{CountryCode=country};
                Assert.Null(router.RouteForecast(location).Primary);
                Assert.Null(router.RouteAlerts(location).Primary);
            }
        }
    }

    [Fact,Trait("Category","Unit")]public void RouteAlerts(){
        using var host=TestHost.Create();
        var router=host.Get<Target>();
        {
            //MET の地域は警報非対応
            Assert.Equal(RouteReason.NotSupported,router.RouteAlerts(Locations.Oslo).Reason);
            Assert.Equal(ProviderId.Jma,router.RouteAlerts(Locations.Tokyo).Primary!.Id);
            Assert.Equal(ProviderId.Nws,router.RouteAlerts(Locations.Washington).Primary!.Id);
        }
    }
}

public class WeatherService{
    [Fact,Trait("Category","Unit")]public async Task GetForecastAsync(){
        {
            //NWS が 404(グリッド範囲外)なら MET に切り替える
            using var host=TestHost.Create(Fixtures.MapMet);
            var location=Locations.World(59.91,10.75,"US","Europe/Oslo");
            var result=await host.Get<IWeatherService>().GetForecastAsync(location,TestContext.Current.CancellationToken);
            Assert.Equal(Availability.Available,result.Availability);
            Assert.Equal(ProviderId.MetNorway,result.Forecast!.Sources.First().Provider);
        }
        {
            //通信障害では切り替えない(出典が黙って変わるのを防ぐ)
            using var host=TestHost.Create(Fixtures.MapMet);
            host.Handler.Override=static r=>{
                if(r.RequestUri!.Host=="api.weather.gov"){
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                }
                return null;
            };
            var location=Locations.World(59.91,10.75,"US","Europe/Oslo");
            var ex=await Assert.ThrowsAsync<WeatherProviderException>(async ()=>await host.Get<IWeatherService>().GetForecastAsync(location,TestContext.Current.CancellationToken));
            Assert.Equal(ProviderFailure.ServerError,ex.Failure);
        }
        {
            //アプリが使用中でないとき(背景更新・ウィジェット)は、NWS が 404 でも MET へ通信しない(MET の規約。NotAllowed で失敗する)
            using var host=TestHost.Create(Fixtures.MapMet,static s=>s.AddSingleton<IAppActivity>(new Activity(false)));
            var location=Locations.World(59.91,10.75,"US","Europe/Oslo");
            Assert.True(host.Get<IWeatherService>().AllowsBackgroundFetch(location));
            var ex=await Assert.ThrowsAsync<WeatherProviderException>(async ()=>await host.Get<IWeatherService>().GetForecastAsync(location,TestContext.Current.CancellationToken));
            Assert.Equal(ProviderFailure.NotAllowed,ex.Failure);
            Assert.DoesNotContain(host.Handler.Requests,static r=>r.RequestUri!.Host=="api.met.no");
        }
        {
            //日本の対象外は取得せずに OutOfCoverage
            using var host=TestHost.Create();
            var result=await host.Get<IWeatherService>().GetForecastAsync(Locations.JapanOutOfCoverage,TestContext.Current.CancellationToken);
            Assert.Equal(Availability.JapanOutOfCoverage,result.Availability);
            Assert.Empty(host.Handler.Requests);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task GetAlertsAsync(){
        using var host=TestHost.Create();
        {
            //警報非対応の地域は NotSupported(空の一覧とは区別する)
            var result=await host.Get<IWeatherService>().GetAlertsAsync(Locations.Oslo,TestContext.Current.CancellationToken);
            Assert.Equal(Availability.NotSupported,result.Availability);
            Assert.Null(result.Alerts);
        }
    }

    [Fact,Trait("Category","Unit")]public void GetObservationAvailability(){
        using var host=TestHost.Create();
        var service=host.Get<IWeatherService>();
        {
            //観測は日本と米国のみ
            Assert.Equal(Availability.Available,service.GetObservationAvailability(Locations.Tokyo));
            Assert.Equal(Availability.Available,service.GetObservationAvailability(Locations.Washington));
            Assert.Equal(Availability.NotSupported,service.GetObservationAvailability(Locations.Oslo));
            Assert.Equal(Availability.JapanOutOfCoverage,service.GetObservationAvailability(Locations.JapanOutOfCoverage));
        }
    }

    [Fact,Trait("Category","Unit")]public void AllowsBackgroundFetch(){
        using var host=TestHost.Create();
        var service=host.Get<IWeatherService>();
        {
            //背景で取得できるのは気象庁・NWS の地点だけ(MET は規約で禁止、対象外地点はデータがない)
            Assert.True(service.AllowsBackgroundFetch(Locations.Tokyo));
            Assert.True(service.AllowsBackgroundFetch(Locations.Washington));
            Assert.False(service.AllowsBackgroundFetch(Locations.Oslo));
            Assert.False(service.AllowsBackgroundFetch(Locations.JapanOutOfCoverage));
        }
    }

    private sealed class Activity(bool inUse):IAppActivity{
        public bool IsInUse=>inUse;
    }
}
