using Microsoft.Extensions.Time.Testing;
using Weather.Core;
using Weather.Presentation;
using Weather.Presentation.ViewModels;

namespace Weather.Presentation.Tests;

internal sealed class Harness{
    public FakeWeatherService Weather{get;}=new();
    public FakeNavigator Navigator{get;}=new();
    public FakeLifecycle Lifecycle{get;}=new();
    public FakeSettingsStore Store{get;}=new();
    public WeatherSession Session{get;}=new();
    public FakeTimeProvider Time{get;}=new(Sample.Now);

    public Presentation.AppSettings Settings{
        get{
            var settings=new Presentation.AppSettings(this.Store){Units=UnitSystem.Metric};
            return settings;
        }
    }

    public FakeBackgroundPlatform Platform{get;}=new();

    public Presentation.Background.WidgetUpdater Widget(){
        return new Presentation.Background.WidgetUpdater(this.Platform,this.Weather,this.Settings,this.Time);
    }

    public Presentation.ViewModels.PlaceWeatherViewModel Place(string id="p1"){
        var data=new PlaceData{PlaceId=id,Name="テスト",Point=new GeoPoint(35.69,139.75)};
        return new Presentation.ViewModels.PlaceWeatherViewModel(data,this.Weather,this.Session,this.Settings,this.Navigator,this.Lifecycle,this.Time);
    }
}

public class PlaceWeatherViewModel{
    [Fact,Trait("Category","Unit")]public async Task RefreshAsync(){
        {
            //日本域は気象庁の天気文を優先し、出典は「気象庁 hh:mm 発表」
            var h=new Harness();
            h.Weather.Forecast=new ForecastResult(Availability.Available,Sample.JmaForecast());
            var vm=h.Place();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Equal(PlaceState.Ready,vm.State);
            Assert.Equal("くもり 昼過ぎ まで 時々 晴れ",vm.CurrentText);
            Assert.Equal("21°",vm.CurrentTemperature);
            Assert.StartsWith("気象庁 10:00 発表",vm.AttributionShort,StringComparison.Ordinal);
            Assert.Equal("出典:気象庁ホームページ",vm.AttributionCredit);
            Assert.Equal("この地域の警報情報は提供されていません",vm.AlertSummary);
            Assert.NotNull(vm.Scene);
        }
        {
            //背景にあるときは取得しない(MET 規約)
            var h=new Harness();
            h.Weather.Forecast=new ForecastResult(Availability.Available,Sample.MetForecast());
            h.Lifecycle.IsForeground=false;
            var vm=h.Place();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0,h.Weather.ForecastCalls);
        }
        {
            //日本の対象外: 天気を描かず、メッセージを出す
            var h=new Harness();
            h.Weather.Forecast=ForecastResult.OutOfCoverage;
            var vm=h.Place();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Equal(PlaceState.OutOfCoverage,vm.State);
            Assert.Contains("予報区域外",vm.StatusMessage,StringComparison.Ordinal);
            Assert.Equal(Weather.Scene.ScenePrecipitationType.None,vm.Scene!.Precipitation.Type);
            Assert.Equal(0,vm.Scene.Clouds.Cover);
        }
        {
            //初回の失敗は Error、前回のデータがあれば Stale(古いデータを表示したまま)
            var h=new Harness();
            h.Weather.ForecastError=new WeatherProviderException(ProviderId.Jma,ProviderFailure.Network,"offline");
            var vm=h.Place();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Equal(PlaceState.Error,vm.State);
            h.Weather.ForecastError=null;
            h.Weather.Forecast=new ForecastResult(Availability.Available,Sample.JmaForecast());
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            h.Weather.ForecastError=new WeatherProviderException(ProviderId.Jma,ProviderFailure.Network,"offline");
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Equal(PlaceState.Stale,vm.State);
            Assert.NotEmpty(vm.Days);
        }
        {
            //キャッシュの古いデータは Stale と取得時刻
            var h=new Harness();
            h.Weather.Forecast=new ForecastResult(Availability.Available,Sample.JmaForecast(stale:true));
            var vm=h.Place();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Equal(PlaceState.Stale,vm.State);
            Assert.Contains("オフライン",vm.AttributionShort,StringComparison.Ordinal);
        }
        {
            //MET の日別はアプリで集計した旨を表示する
            var h=new Harness();
            h.Weather.Location=Sample.Oslo;
            h.Weather.Forecast=new ForecastResult(Availability.Available,Sample.MetForecast());
            var vm=h.Place();
            await vm.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Contains("アプリで集計",vm.Days[0].Source,StringComparison.Ordinal);
            Assert.False(vm.ObservationsAvailable);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task OpenDayCommand(){
        var h=new Harness();
        h.Weather.Forecast=new ForecastResult(Availability.Available,Sample.JmaForecast());
        var vm=h.Place("abc");
        await vm.RefreshAsync(TestContext.Current.CancellationToken);
        {
            //日別の詳細へ地点 ID と日付を渡す
            await vm.OpenDayCommand.ExecuteAsync(vm.Days[0]);
            var call=Assert.Single(h.Navigator.Calls);
            Assert.Equal(Routes.Daily,call.Route);
            Assert.Equal("abc",call.Parameters!["place"]);
            Assert.Equal("2026-10-04",call.Parameters["date"]);
        }
    }
}

public class MainViewModel{
    [Fact,Trait("Category","Unit")]public async Task LoadAsync(){
        {
            //位置情報が拒否されたら検索への導線を出し、お気に入りだけを読み込む
            var h=new Harness();
            h.Weather.Forecast=new ForecastResult(Availability.Available,Sample.JmaForecast());
            var favorites=new FakeFavorites();
            favorites.Items.Add(new FavoritePlace("f1","大阪",new GeoPoint(34.69,135.5),0));
            var vm=new Presentation.ViewModels.MainViewModel(favorites,new FakeLocationService(LocationStatus.Denied,null),h.Lifecycle,h.Navigator,h.Time,d=>new Presentation.ViewModels.PlaceWeatherViewModel(d,h.Weather,h.Session,h.Settings,h.Navigator,h.Lifecycle,h.Time),h.Widget());
            await vm.LoadAsync(TestContext.Current.CancellationToken);
            Assert.Single(vm.Places);
            Assert.Contains("許可されていません",vm.LocationMessage,StringComparison.Ordinal);
            Assert.Equal(1,h.Weather.ForecastCalls);
        }
        {
            //現在地は先頭
            var h=new Harness();
            h.Weather.Forecast=new ForecastResult(Availability.Available,Sample.JmaForecast());
            var favorites=new FakeFavorites();
            favorites.Items.Add(new FavoritePlace("f1","大阪",new GeoPoint(34.69,135.5),0));
            var vm=new Presentation.ViewModels.MainViewModel(favorites,new FakeLocationService(LocationStatus.Granted,new GeoPoint(35.69,139.75)),h.Lifecycle,h.Navigator,h.Time,d=>new Presentation.ViewModels.PlaceWeatherViewModel(d,h.Weather,h.Session,h.Settings,h.Navigator,h.Lifecycle,h.Time),h.Widget());
            await vm.LoadAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2,vm.Places.Count);
            Assert.True(vm.Places[0].Data.IsCurrentLocation);
            Assert.Same(vm.Places[0],vm.Current);
        }
        {
            //再読み込みでは既存の地点の VM を使い回し、追加された地点だけ取得する。削除・並べ替えも反映する
            var h=new Harness();
            h.Weather.Forecast=new ForecastResult(Availability.Available,Sample.JmaForecast());
            var favorites=new FakeFavorites();
            favorites.Items.Add(new FavoritePlace("f1","大阪",new GeoPoint(34.69,135.5),0));
            favorites.Items.Add(new FavoritePlace("f2","札幌",new GeoPoint(43.06,141.35),1));
            var vm=new Presentation.ViewModels.MainViewModel(favorites,new FakeLocationService(LocationStatus.Denied,null),h.Lifecycle,h.Navigator,h.Time,d=>new Presentation.ViewModels.PlaceWeatherViewModel(d,h.Weather,h.Session,h.Settings,h.Navigator,h.Lifecycle,h.Time),h.Widget());
            await vm.LoadAsync(TestContext.Current.CancellationToken);
            var osaka=vm.Places[0];
            var sapporo=vm.Places[1];
            Assert.Equal(2,h.Weather.ForecastCalls);
            favorites.Items.Add(new FavoritePlace("f3","那覇",new GeoPoint(26.21,127.68),2));
            await vm.LoadAsync(TestContext.Current.CancellationToken);
            Assert.Equal(3,vm.Places.Count);
            Assert.Same(osaka,vm.Places[0]);
            Assert.Same(sapporo,vm.Places[1]);
            Assert.Equal(3,h.Weather.ForecastCalls);
            await favorites.DeleteAsync("f1",TestContext.Current.CancellationToken);
            await favorites.ReorderAsync(["f3","f2"],TestContext.Current.CancellationToken);
            await vm.LoadAsync(TestContext.Current.CancellationToken);
            Assert.Equal(["那覇","札幌"],vm.Places.Select(static p=>p.Name));
            Assert.Same(sapporo,vm.Places[1]);
            Assert.Equal(3,h.Weather.ForecastCalls);
        }
    }
}

public class AlertsViewModel{
    [Fact,Trait("Category","Unit")]public async Task OnNavigatedToAsync(){
        var h=new Harness();
        var src=Sample.Source(ProviderId.Jma);
        var alerts=new AlertSet([new Alert("1310100:43","レベル4大雨危険警報",AlertTier.Danger,AlertStatus.Active,src){WarningLevel=4},new Alert("1310100:15","強風注意報",AlertTier.Advisory,AlertStatus.Active,src)],src,"大雨に警戒してください");
        h.Session.Put(new PlaceData{PlaceId="p",Name="千代田区",Point=new GeoPoint(35.69,139.75),Location=Sample.Tokyo,Alerts=new AlertResult(Availability.Available,alerts)});
        var vm=new Presentation.ViewModels.AlertsViewModel(h.Session,h.Navigator);
        await vm.OnNavigatedToAsync(new Dictionary<string,string>{["place"]="p"});
        {
            //Tier の高い順。名称・見出しは機関の表現のまま
            Assert.Equal("レベル4大雨危険警報",vm.Items[0].Name);
            Assert.Equal("大雨に警戒してください",vm.Headline);
            Assert.Contains("出典:気象庁ホームページ",vm.Attribution,StringComparison.Ordinal);
        }
        {
            //警報非対応の地域は「提供されていない」(警報なしとは区別)
            h.Session.Put(new PlaceData{PlaceId="o",Name="Oslo",Point=new GeoPoint(59.91,10.75),Location=Sample.Oslo,Alerts=AlertResult.NotSupported});
            var other=new Presentation.ViewModels.AlertsViewModel(h.Session,h.Navigator);
            await other.OnNavigatedToAsync(new Dictionary<string,string>{["place"]="o"});
            Assert.Equal("この地域の警報情報は提供されていません",other.Message);
        }
    }
}

public class Units{
    [Fact,Trait("Category","Unit")]public void Temperature(){
        {
            //°C / °F、欠損は "--"
            Assert.Equal("20°",Presentation.Units.Temperature(20,UnitSystem.Metric));
            Assert.Equal("68°",Presentation.Units.Temperature(20,UnitSystem.Imperial));
            Assert.Equal("--",Presentation.Units.Temperature(null,UnitSystem.Metric));
        }
    }

    [Fact,Trait("Category","Unit")]public void Wind(){
        {
            //m/s / mph
            Assert.Equal("5 m/s",Presentation.Units.Wind(5,UnitSystem.Metric));
            Assert.Equal("11 mph",Presentation.Units.Wind(5,UnitSystem.Imperial));
        }
    }
}

public class AppSettings{
    [Fact,Trait("Category","Unit")]public void DefaultUnits(){
        {
            //米国は Imperial、それ以外は Metric
            Assert.Equal(UnitSystem.Imperial,Presentation.AppSettings.DefaultUnits("US"));
            Assert.Equal(UnitSystem.Metric,Presentation.AppSettings.DefaultUnits("JP"));
        }
        {
            //保存した値と既定の保持期間(1 年)
            var settings=new Presentation.AppSettings(new FakeSettingsStore());
            Assert.Equal(365,settings.HistoryRetentionDays);
            settings.Units=UnitSystem.Imperial;
            Assert.Equal(UnitSystem.Imperial,settings.Units);
        }
    }
}

public class ConditionIcons{
    [Fact,Trait("Category","Unit")]public void For(){
        {
            //時々雨は雨のアイコン、晴れの夜は月
            var c=new CompositeCondition(new WeatherCondition(SkyCover.Overcast),ConditionTransition.Occasionally,new WeatherCondition(SkyCover.MostlyCloudy,PrecipitationType.Rain,PrecipitationIntensity.Moderate,isShowery:true));
            Assert.Equal("🌦",Presentation.ConditionIcons.For(c,false));
            Assert.Equal("🌙",Presentation.ConditionIcons.For(new WeatherCondition(SkyCover.Clear),true));
            Assert.Equal("⛈",Presentation.ConditionIcons.For(new WeatherCondition(SkyCover.Overcast,PrecipitationType.Rain,PrecipitationIntensity.Heavy,hasThunder:true),false));
        }
    }
}

public class ArchitectureRules{
    [Fact,Trait("Category","Unit")]public void NoMauiReference(){
        var names=typeof(Presentation.ViewModels.MainViewModel).Assembly.GetReferencedAssemblies().Select(static a=>a.Name??"");
        Assert.DoesNotContain(names,static n=>n.StartsWith("Microsoft.Maui",StringComparison.Ordinal));
    }
}

public class SettingsViewModel{
    [Fact,Trait("Category","Unit")]public async Task UseImperial(){
        {
            //画面を開いただけでは何も保存しない(既定値は端末の地域に追従し続ける)
            var store=new FakeSettingsStore();
            var history=new FakeHistory();
            var settings=new Presentation.AppSettings(store);
            var vm=new Presentation.ViewModels.SettingsViewModel(settings,history,new FakeDialogs(),new FakeNavigator(),new FakeBackgroundPlatform(),new FakeBackgroundPlatform());
            await Task.Yield();
            Assert.Null(store.Get("units"));
            Assert.Null(store.Get("historyRetentionDays"));
            Assert.Null(store.Get("quality"));
            Assert.Empty(history.Retentions);
            Assert.Equal(settings.Units==UnitSystem.Imperial,vm.UseImperial);
        }
        {
            //利用者が切り替えたら保存する(既定値と逆の値)
            var store=new FakeSettingsStore();
            var settings=new Presentation.AppSettings(store);
            var vm=new Presentation.ViewModels.SettingsViewModel(settings,new FakeHistory(),new FakeDialogs(),new FakeNavigator(),new FakeBackgroundPlatform(),new FakeBackgroundPlatform());
            var chosen=!vm.UseImperial;
            vm.UseImperial=chosen;
            Assert.NotNull(store.Get("units"));
            Assert.Equal(chosen,settings.Units==UnitSystem.Imperial);
        }
    }
}
