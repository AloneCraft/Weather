using Microsoft.Extensions.Logging.Abstractions;
using Weather.Core;
using Weather.Presentation;
using Weather.Presentation.Background;

namespace Weather.Presentation.Tests;

internal sealed class BackgroundHarness{
    public static readonly GeoPoint TokyoPoint=new(35.69,139.75);
    public static readonly GeoPoint OsloPoint=new(59.91,10.75);
    public static readonly GeoPoint NewYorkPoint=new(40.71,-74.01);

    public Harness H{get;}=new();
    public FakeFavorites Favorites{get;}=new();

    public BackgroundHarness(){
        this.H.Weather.Resolver=static p=>{
            if(p==OsloPoint){
                return Sample.Oslo;
            }
            if(p==NewYorkPoint){
                return new ResolvedLocation{Point=p,CountryCode="US",DisplayName="New York",TimeZoneId="America/New_York"};
            }
            return Sample.Tokyo;
        };
    }

    public Presentation.Background.BackgroundRefreshService Service(){
        return new Presentation.Background.BackgroundRefreshService(this.H.Weather,this.Favorites,this.H.Settings,this.H.Store,this.H.Platform,this.H.Platform,this.H.Time,NullLogger<Presentation.Background.BackgroundRefreshService>.Instance);
    }

    public void AddFavorite(string id,string name,GeoPoint point){
        this.Favorites.Items.Add(new FavoritePlace(id,name,point,this.Favorites.Items.Count));
    }

    public static AlertSet JmaAlerts(params (string Code,string Name,AlertTier Tier)[] items){
        var src=Sample.Source(ProviderId.Jma);
        return new AlertSet([..items.Select(i=>new Alert("1310100:"+i.Code,i.Name,i.Tier,AlertStatus.Active,src){EventCode=i.Code,AreaName="千代田区",Headline="東京地方では、土砂災害に警戒してください。"})],src);
    }
}

public class BackgroundRefreshService{
    [Fact,Trait("Category","Unit")]public async Task RunAsync_Notifications(){
        var b=new BackgroundHarness();
        b.AddFavorite("home","自宅",BackgroundHarness.TokyoPoint);
        b.H.Settings.AlertNotifications=true;
        var service=b.Service();
        var ct=TestContext.Current.CancellationToken;
        {
            //警報以上だけを通知する(注意報は通知しない)。文面は気象庁の名称・見出しをそのまま使い、出典を含める
            b.H.Weather.Alerts=new AlertResult(Availability.Available,BackgroundHarness.JmaAlerts(("03","大雨警報",AlertTier.Warning),("14","雷注意報",AlertTier.Advisory)));
            var result=await service.RunAsync(ct);
            Assert.Equal(1,result.Notifications);
            var n=Assert.Single(b.H.Platform.Shown);
            Assert.Equal("大雨警報(自宅)",n.Title);
            Assert.Contains("千代田区に発表中",n.Body,StringComparison.Ordinal);
            Assert.Contains("東京地方では、土砂災害に警戒してください。",n.Body,StringComparison.Ordinal);
            Assert.Contains("気象庁",n.Body,StringComparison.Ordinal);
            Assert.EndsWith("出典:気象庁ホームページ",n.Body,StringComparison.Ordinal);
        }
        {
            //同じ警報は再通知しない
            var result=await service.RunAsync(ct);
            Assert.Equal(0,result.Notifications);
            Assert.Single(b.H.Platform.Shown);
        }
        {
            //より重い警報(別の種類)は通知する
            b.H.Weather.Alerts=new AlertResult(Availability.Available,BackgroundHarness.JmaAlerts(("03","大雨警報",AlertTier.Warning),("43","レベル4大雨危険警報",AlertTier.Danger)));
            var result=await service.RunAsync(ct);
            Assert.Equal(1,result.Notifications);
            Assert.Equal("レベル4大雨危険警報(自宅)",b.H.Platform.Shown[^1].Title);
        }
        {
            //解除されたら記録から外し、再び発表されたら通知する
            b.H.Weather.Alerts=new AlertResult(Availability.Available,BackgroundHarness.JmaAlerts());
            Assert.Equal(0,(await service.RunAsync(ct)).Notifications);
            b.H.Weather.Alerts=new AlertResult(Availability.Available,BackgroundHarness.JmaAlerts(("03","大雨警報",AlertTier.Warning)));
            Assert.Equal(1,(await service.RunAsync(ct)).Notifications);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task RunAsync_MetPlace(){
        //MET の地点は背景では一切取得しない(規約)。通知・ウィジェットの対象外
        var b=new BackgroundHarness();
        b.AddFavorite("oslo","Oslo",BackgroundHarness.OsloPoint);
        b.H.Settings.AlertNotifications=true;
        b.H.Platform.HasWidgets=true;
        b.H.Weather.Forecast=new ForecastResult(Availability.Available,Sample.MetForecast());
        var result=await b.Service().RunAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1,result.PlacesSkipped);
        Assert.Equal(0,b.H.Weather.ForecastCalls);
        Assert.Equal(0,b.H.Weather.AlertCalls);
        Assert.Empty(b.H.Platform.Published);
    }

    [Fact,Trait("Category","Unit")]public async Task RunAsync_Widget(){
        var ct=TestContext.Current.CancellationToken;
        {
            //通知が無効でウィジェットもなければ何も取得しない
            var b=new BackgroundHarness();
            b.AddFavorite("home","自宅",BackgroundHarness.TokyoPoint);
            var result=await b.Service().RunAsync(ct);
            Assert.Equal(BackgroundRefreshResult.Nothing,result);
            Assert.Equal(0,b.H.Weather.ForecastCalls+b.H.Weather.AlertCalls);
        }
        {
            //ウィジェットは先頭の地点だけ。気象庁の天気文と出典を表示する
            var b=new BackgroundHarness();
            b.AddFavorite("home","自宅",BackgroundHarness.TokyoPoint);
            b.AddFavorite("ny","New York",BackgroundHarness.NewYorkPoint);
            b.H.Platform.HasWidgets=true;
            b.H.Weather.Forecast=new ForecastResult(Availability.Available,Sample.JmaForecast());
            b.H.Weather.Alerts=new AlertResult(Availability.Available,BackgroundHarness.JmaAlerts(("03","大雨警報",AlertTier.Warning)));
            var result=await b.Service().RunAsync(ct);
            Assert.True(result.WidgetUpdated);
            Assert.Equal(1,b.H.Weather.ForecastCalls);
            var s=Assert.Single(b.H.Platform.Published);
            Assert.Equal(WidgetState.Ready,s.State);
            Assert.Equal("自宅",s.PlaceName);
            Assert.Equal("くもり 昼過ぎ まで 時々 晴れ",s.ConditionText);
            Assert.Equal("出典:気象庁ホームページ",s.Credit);
            Assert.Equal("大雨警報",s.AlertText);
            Assert.True(s.BackgroundRefresh);
            Assert.Null(s.Note);
            //通知が無効なら通知しない
            Assert.Empty(b.H.Platform.Shown);
        }
        {
            //お気に入りがなければ「地点を追加」の表示
            var b=new BackgroundHarness();
            b.H.Platform.HasWidgets=true;
            await b.Service().RunAsync(ct);
            Assert.Equal(WidgetState.NoPlace,Assert.Single(b.H.Platform.Published).State);
        }
    }
}

public class WidgetUpdater{
    [Fact,Trait("Category","Unit")]public void Publish(){
        {
            //前面では MET の地点もウィジェットに反映する。背景では更新されない旨と MET の出典を表示する
            var h=new Harness();
            h.Platform.HasWidgets=true;
            var data=new PlaceData{PlaceId="oslo",Name="Oslo",Point=BackgroundHarness.OsloPoint,Location=Sample.Oslo,Forecast=new ForecastResult(Availability.Available,Sample.MetForecast())};
            var current=new PlaceData{PlaceId="current",Name="現在地",Point=BackgroundHarness.TokyoPoint,IsCurrentLocation=true};
            h.Widget().Publish([current,data]);
            var s=Assert.Single(h.Platform.Published);
            Assert.Equal("Oslo",s.PlaceName);
            Assert.False(s.BackgroundRefresh);
            Assert.Equal(Presentation.Resources.Strings.WidgetForegroundOnly,s.Note);
            Assert.Equal("Data from MET Norway (CC BY 4.0)",s.Credit);
            Assert.Equal("10°",s.Temperature);
        }
        {
            //日本の対象外地点は「対象外」(他ソースのデータを出さない)
            var h=new Harness();
            h.Platform.HasWidgets=true;
            var data=new PlaceData{PlaceId="x",Name="沖合",Point=new GeoPoint(30,135),Forecast=ForecastResult.OutOfCoverage};
            h.Widget().Publish([data]);
            var s=Assert.Single(h.Platform.Published);
            Assert.Equal(WidgetState.OutOfCoverage,s.State);
            Assert.Equal("--",s.Temperature);
            Assert.Equal("",s.Credit);
        }
        {
            //ウィジェットがなければ何もしない
            var h=new Harness();
            h.Widget().Publish([new PlaceData{PlaceId="oslo",Name="Oslo",Point=BackgroundHarness.OsloPoint}]);
            Assert.Empty(h.Platform.Published);
        }
    }

    [Fact,Trait("Category","Unit")]public void Json(){
        //Android / iOS(Swift)が読む JSON: camelCase、列挙は文字列、往復で同じ値
        var snapshot=WidgetSnapshotFactory.Create("自宅",new ForecastResult(Availability.Available,Sample.JmaForecast()),null,true,UnitSystem.Metric,Sample.Now);
        var json=WidgetSnapshotJson.Serialize(snapshot);
        Assert.Contains("\"state\":\"Ready\"",json,StringComparison.Ordinal);
        Assert.Contains("\"placeName\":",json,StringComparison.Ordinal);
        Assert.Contains("\"backgroundRefresh\":true",json,StringComparison.Ordinal);
        Assert.Equal(snapshot,WidgetSnapshotJson.Deserialize(json));
        Assert.Null(WidgetSnapshotJson.Deserialize("{broken"));
    }
}

public class Notifications{
    [Fact,Trait("Category","Unit")]public void NumericId(){
        //OS の通知 ID はプロセスをまたいで同じ値(同じ警報の通知を置き換える)
        var a=new Presentation.Background.AlertNotification("Jma:03:千代田区","home","t","b",AlertTier.Warning);
        var b=a with{Title="別の見出し"};
        var c=a with{Key="Jma:04:千代田区"};
        Assert.Equal(a.NumericId,b.NumericId);
        Assert.NotEqual(a.NumericId,c.NumericId);
        Assert.True(a.NumericId>=0);
        Assert.Equal(1281434406,new Presentation.Background.AlertNotification("k","p","t","b",AlertTier.Warning).NumericId);
    }

    [Fact,Trait("Category","Unit")]public void Key(){
        //NWS は更新のたびに ID が変わるため、種類と区域で同じ警報とみなす(更新では再通知しない)
        var src=Sample.Source(ProviderId.Nws);
        var first=new Alert("urn:1","Flood Warning",AlertTier.Warning,AlertStatus.Active,src){AreaName="Kings, NY"};
        var update=new Alert("urn:2","Flood Warning",AlertTier.Warning,AlertStatus.Updated,src){AreaName="Kings, NY"};
        Assert.Equal(AlertNotificationPolicy.Key(first),AlertNotificationPolicy.Key(update));
        //解除と Warning 未満(Watch)は通知しない
        Assert.False(AlertNotificationPolicy.ShouldNotify(new Alert("urn:3","Flood Warning",AlertTier.Warning,AlertStatus.Cancelled,src)));
        Assert.False(AlertNotificationPolicy.ShouldNotify(new Alert("urn:4","Flood Watch",AlertTier.Watch,AlertStatus.Active,src)));
    }
}

public class SettingsNotifications{
    [Fact,Trait("Category","Unit")]public async Task Toggle(){
        {
            //通知が許可されなければ無効に戻し、理由を表示する
            var h=new Harness();
            h.Platform.Permission=false;
            var dialogs=new FakeDialogs();
            var vm=new Presentation.ViewModels.SettingsViewModel(h.Settings,new FakeHistory(),dialogs,h.Navigator,h.Platform,h.Platform);
            vm.AlertNotifications=true;
            await Task.Yield();
            Assert.False(vm.AlertNotifications);
            Assert.False(h.Settings.AlertNotifications);
            Assert.Equal(Presentation.Resources.Strings.NotificationPermissionDenied,Assert.Single(dialogs.Messages));
            Assert.Equal(0,h.Platform.ScheduleCount);
        }
        {
            //許可されれば保存して背景更新を予約する
            var h=new Harness();
            var vm=new Presentation.ViewModels.SettingsViewModel(h.Settings,new FakeHistory(),new FakeDialogs(),h.Navigator,h.Platform,h.Platform);
            vm.AlertNotifications=true;
            await Task.Yield();
            Assert.True(h.Settings.AlertNotifications);
            Assert.Equal(1,h.Platform.ScheduleCount);
        }
    }

}
