using Weather.Core;
using Weather.Presentation;
using Weather.Presentation.ViewModels;

namespace Weather.Presentation.Tests;

/// <summary>地図のデータの偽物。層と時刻ごとに、GFS の格子(日本周辺も値あり: VM の二重の防御を確かめるため)と気象庁のタイルを返す。</summary>
internal sealed class FakeMapData:IMapDataService{
    public static readonly SourceAttribution Gfs=new(){Provider=ProviderId.Gfs,AgencyName="NOAA",ProductName="GFS 0.5°",IssuedAt=Sample.Now.AddHours(-6),RetrievedAt=Sample.Now,License=new LicenseInfo("PD",null),Processing=DataProcessing.Interpolated};
    public static readonly SourceAttribution Nowcast=new(){Provider=ProviderId.Jma,AgencyName="気象庁",ProductName="降水ナウキャスト",IssuedAt=Sample.Now,RetrievedAt=Sample.Now,License=new LicenseInfo("t",null)};

    public List<(FieldLayer Layer,DateTimeOffset Time)> Requests{get;}=[];
    public JapanCoverage Japan{get;set;}=JapanCoverage.Available;

    public ValueTask<MapAvailability> GetAvailabilityAsync(CancellationToken cancellationToken){
        var nowcast=Enumerable.Range(-36,49).Select(static i=>Sample.Now.AddMinutes(5*i)).ToList();
        var cycle=Sample.Now.AddHours(-6);
        var gfs=Enumerable.Range(1,40).Select(i=>cycle.AddHours(3*i)).ToList();
        return ValueTask.FromResult(new MapAvailability{RetrievedAt=Sample.Now,Nowcast=nowcast,Gfs=gfs,GfsReferenceTime=cycle});
    }

    public ValueTask<MapFrame> GetFrameAsync(FieldLayer layer,DateTimeOffset time,CancellationToken cancellationToken){
        this.Requests.Add((layer,time));
        var geometry=new GridGeometry(360,181,90,0,1,1);
        var values=Enumerable.Repeat(12f,geometry.Count).ToArray();
        var quantity=FieldQuantity.TemperatureC;
        if(layer==FieldLayer.Precipitation){
            quantity=FieldQuantity.PrecipitationMmPerHour;
        }
        var field=new GridField(geometry,values,quantity,time,Sample.Now.AddHours(-6),Gfs);
        var tile=new JmaTileLayer{Product=JmaTileProduct.Nowcast,Element="hrpns",BaseTime=Sample.Now,ValidTime=Sample.Now,MinZoom=4,MaxZoom=10,EvenZoomOnly=true,Pixelated=true,Legend=MapLegends.RainRate,Source=Nowcast};
        IReadOnlyList<JmaTileLayer> tiles=[];
        IReadOnlyList<SourceAttribution> sources=[Gfs];
        if(this.Japan==JapanCoverage.Available){
            tiles=[tile];
            sources=[Gfs,Nowcast];
        }
        return ValueTask.FromResult(new MapFrame{Layer=layer,Time=time,Scalar=field,Tiles=tiles,Japan=this.Japan,Legend=MapLegends.RainRate,Sources=sources});
    }

    public ValueTask<byte[]?> GetTileAsync(JmaTileLayer layer,int z,int x,int y,CancellationToken cancellationToken){
        return ValueTask.FromResult<byte[]?>(null);
    }
}

/// <summary>日本周辺のかわりに北緯 30〜46 度・東経 128〜146 度。</summary>
internal sealed class FakeJapanArea:IJapanArea{
    public bool Contains(double latitude,double longitude){
        return latitude is >=30 and <=46&&longitude is >=128 and <=146;
    }
}

internal sealed class FakeSampler(uint? color):IMapPixelSampler{
    public ValueTask<uint?> SampleAsync(JmaTileLayer layer,GeoPoint point,CancellationToken cancellationToken){
        return ValueTask.FromResult(color);
    }
}

internal sealed class MapHarness{
    public Harness H{get;}=new();
    public FakeMapData Maps{get;}=new();
    public FakeFavorites Favorites{get;}=new();
    public MapFocusService Focus{get;}=new();

    public Presentation.ViewModels.MapViewModel Create(uint? pixel=0xFFA0D2FF){
        var h=this.H;
        Presentation.ViewModels.PlaceWeatherViewModel Place(PlaceData d)=>new(d,h.Weather,h.Session,h.Settings,h.Navigator,h.Lifecycle,h.Time);
        var places=new Presentation.ViewModels.MainViewModel(this.Favorites,new FakeLocationService(LocationStatus.Denied,null),h.Lifecycle,h.Navigator,h.Time,Place,h.Widget());
        return new Presentation.ViewModels.MapViewModel(this.Maps,h.Weather,new FakeJapanArea(),new FakeSampler(pixel),places,new FavoritesService(this.Favorites,h.Weather),this.Focus,h.Settings,h.Lifecycle,h.Navigator,new FakeLocationService(LocationStatus.Denied,null),Place,h.Session,h.Time);
    }
}

public class MapTimeline{
    [Fact,Trait("Category","Unit")]public async Task Build(){
        var a=await new FakeMapData().GetAvailabilityAsync(TestContext.Current.CancellationToken);
        var steps=Presentation.MapTimeline.Build(a,Sample.Now);
        {
            //3 時間前から。雨雲の範囲(1 時間先まで)は 5 分ごと
            Assert.Equal(Sample.Now.AddHours(-3),steps[0]);
            Assert.Equal(TimeSpan.FromMinutes(5),steps[1]-steps[0]);
            Assert.Contains(Sample.Now.AddHours(1),steps);
        }
        {
            //15 時間先までは 1 時間ごと、その後は GFS の 3 時間ごと。昇順で重複なし
            Assert.Contains(Sample.Now.AddHours(2),steps);
            Assert.Contains(Sample.Now.AddHours(15),steps);
            Assert.DoesNotContain(Sample.Now.AddHours(16),steps);
            Assert.Contains(Sample.Now.AddHours(18),steps);
            Assert.Equal(steps.Order(),steps);
            Assert.Equal(steps.Count,steps.Distinct().Count());
            Assert.True(steps[^1]<=Sample.Now.AddDays(5));
        }
        {
            //現在時刻の刻み
            Assert.Equal(Sample.Now,steps[Presentation.MapTimeline.IndexOfNow(steps,Sample.Now)]);
        }
    }
}

public class MapText{
    [Fact,Trait("Category","Unit")]public void LegendLabel(){
        //最初の区分は「未満」、最後は「以上」、天気は区分名
        Assert.Equal("-25 未満",Presentation.MapText.LegendLabel(MapLegends.Temperature,0));
        Assert.Equal("80 以上",Presentation.MapText.LegendLabel(MapLegends.RainRate,7));
        Assert.Equal("1",Presentation.MapText.LegendLabel(MapLegends.RainRate,1));
        Assert.Equal("晴れ",Presentation.MapText.LegendLabel(MapLegends.Weather,0));
    }

    [Fact,Trait("Category","Unit")]public void Relative(){
        Assert.Equal("現在",Presentation.MapText.Relative(Sample.Now.AddMinutes(10),Sample.Now));
        Assert.Equal("+3 時間",Presentation.MapText.Relative(Sample.Now.AddHours(3),Sample.Now));
        Assert.Equal("-2 時間",Presentation.MapText.Relative(Sample.Now.AddHours(-2),Sample.Now));
    }
}

public class MapViewModel{
    [Fact,Trait("Category","Unit")]public async Task LoadAsync(){
        var m=new MapHarness();
        var vm=m.Create();
        await vm.LoadAsync(TestContext.Current.CancellationToken);
        {
            //時間軸は現在時刻から始まり、現在時刻のコマ(風)を読む
            Assert.Equal(Sample.Now,vm.Steps[vm.StepIndex]);
            Assert.Equal("現在",vm.RelativeText);
            Assert.Equal((FieldLayer.Wind,Sample.Now),m.Maps.Requests[^1]);
            Assert.NotNull(vm.Frame);
        }
        {
            //出典の帯は表示中のデータ源(方針 7)、日本周辺の注記
            Assert.Contains("NOAA",vm.AttributionText,StringComparison.Ordinal);
            Assert.Contains("気象庁",vm.AttributionText,StringComparison.Ordinal);
            Assert.Equal(Presentation.Resources.Strings.JapanNoteJma,vm.JapanNote);
            Assert.Equal(8,vm.LegendEntries.Count);
        }
        {
            //層を変えると同じ時刻で読み直す
            vm.SelectLayerCommand.Execute(FieldLayer.Precipitation);
            await vm.LoadFrameAsync(TestContext.Current.CancellationToken);
            Assert.Equal(FieldLayer.Precipitation,m.Maps.Requests[^1].Layer);
        }
        {
            //時刻を進める
            vm.StepIndex+=12;
            await vm.LoadFrameAsync(TestContext.Current.CancellationToken);
            Assert.Equal(Sample.Now.AddHours(1),m.Maps.Requests[^1].Time);
            Assert.Equal("+1 時間",vm.RelativeText);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task TapAsync(){
        var ct=TestContext.Current.CancellationToken;
        {
            //日本周辺の外: GFS の値
            var m=new MapHarness();
            m.H.Weather.Location=Sample.Oslo;
            var vm=m.Create();
            await vm.LoadAsync(ct);
            await vm.TapAsync(new GeoPoint(59.9,10.7),ct);
            Assert.True(vm.IsProbeVisible);
            Assert.Equal("12°",vm.ProbeValue);
            Assert.Contains("NOAA GFS",vm.ProbeSource,StringComparison.Ordinal);
        }
        {
            //方針 5: 日本周辺では GFS に値があっても出さない。気象庁のタイルの凡例の区分を出す
            var m=new MapHarness();
            var vm=m.Create(0xFFA0D2FF);
            await vm.LoadAsync(ct);
            await vm.TapAsync(new GeoPoint(35.69,139.75),ct);
            Assert.Equal("1 mm/h",vm.ProbeValue);
            Assert.Contains("気象庁",vm.ProbeSource,StringComparison.Ordinal);
            Assert.DoesNotContain("NOAA",vm.ProbeSource,StringComparison.Ordinal);
        }
        {
            //日本周辺でタイルに色がない(雨なし)場合も GFS の値は出さない
            var m=new MapHarness();
            var vm=m.Create(null);
            await vm.LoadAsync(ct);
            await vm.TapAsync(new GeoPoint(35.69,139.75),ct);
            Assert.Equal(Presentation.Resources.Strings.ProbeNone,vm.ProbeValue);
            Assert.DoesNotContain("°",vm.ProbeValue,StringComparison.Ordinal);
        }
        {
            //気象庁の予報期間外の日本周辺: その旨
            var m=new MapHarness();
            m.Maps.Japan=JapanCoverage.OutOfRange;
            var vm=m.Create();
            await vm.LoadAsync(ct);
            await vm.TapAsync(new GeoPoint(35.69,139.75),ct);
            Assert.Equal(Presentation.Resources.Strings.JapanNoteOutOfRange,vm.ProbeValue);
            Assert.Equal(Presentation.Resources.Strings.ProbeJapanOnly,vm.ProbeSource);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task OpenForecast(){
        var ct=TestContext.Current.CancellationToken;
        var m=new MapHarness();
        m.H.Weather.Forecast=new ForecastResult(Availability.Available,Sample.JmaForecast());
        var vm=m.Create();
        await vm.LoadAsync(ct);
        await vm.TapAsync(new GeoPoint(35.69,139.75),ct);
        {
            //吹き出しから予報シート(公式予報)。お気に入りに追加できる
            await vm.OpenForecastCommand.ExecuteAsync(null);
            Assert.True(vm.IsSheetOpen);
            Assert.False(vm.IsProbeVisible);
            Assert.Equal("千代田区",vm.SheetPlace!.Name);
            Assert.Equal("21°",vm.SheetPlace.CurrentTemperature);
            Assert.True(vm.CanAddFavorite);
            await vm.AddFavoriteCommand.ExecuteAsync(null);
            Assert.Single(m.Favorites.Items);
            Assert.False(vm.CanAddFavorite);
        }
        {
            //シートを引き上げると詳細画面へ
            await vm.OpenDetailCommand.ExecuteAsync(null);
            Assert.Equal(Routes.PlaceDetail,m.H.Navigator.Calls[^1].Route);
        }
        {
            //お気に入りが増えるとピンに出る(公式予報の気温)
            await Task.Yield();
            Assert.Contains(vm.Pins,static p=>p.Label=="千代田区 21°");
        }
    }

    [Fact,Trait("Category","Unit")]public async Task Focus(){
        //検索で選んだ地点: 地図をその地点へ動かし、予報シートを開く
        var ct=TestContext.Current.CancellationToken;
        var m=new MapHarness();
        m.H.Weather.Forecast=new ForecastResult(Availability.Available,Sample.JmaForecast());
        var vm=m.Create();
        await vm.LoadAsync(ct);
        MapFocusRequest? requested=null;
        vm.FocusRequested+=(_,r)=>requested=r;
        m.Focus.Request(new GeoPoint(34.69,135.5),"大阪");
        await Task.Yield();
        Assert.Equal(new GeoPoint(34.69,135.5),requested!.Point);
        Assert.True(vm.IsSheetOpen);
        Assert.Null(m.Focus.Pending);
    }

    [Fact,Trait("Category","Unit")]public async Task TogglePlay(){
        //再生は一定間隔で次の刻みへ進み、背景に移ると止まる
        var ct=TestContext.Current.CancellationToken;
        var m=new MapHarness();
        var vm=m.Create();
        await vm.LoadAsync(ct);
        var start=vm.StepIndex;
        vm.TogglePlayCommand.Execute(null);
        Assert.True(vm.IsPlaying);
        for(var i=0;i<3;i++){
            m.H.Time.Advance(TimeSpan.FromMilliseconds(700));
            await Task.Delay(30,ct);
        }
        Assert.True(vm.StepIndex>start);
        m.H.Lifecycle.Stop();
        Assert.False(vm.IsPlaying);
    }
}

public class CurrentConditions{
    [Fact,Trait("Category","Unit")]public void From(){
        var src=Sample.Source(ProviderId.Jma);
        {
            //現在時刻を含む区間がなければ、3 時間以内の次の区間の気温(気象庁の時系列は次の 3 時間ごとの時刻から始まる)
            var next=new ForecastPoint(Sample.Now.AddMinutes(10),Sample.Now.AddHours(3),src){TemperatureC=18,Condition=new WeatherCondition(SkyCover.Overcast)};
            var forecast=new Forecast(Sample.Tokyo,[next],[]);
            var c=Presentation.CurrentConditions.From(forecast,Sample.Tokyo.Point,Sample.Now,TimeZoneInfo.Utc,UnitSystem.Metric);
            Assert.Equal("18°",c.Temperature);
        }
        {
            //3 時間より先しかなければ使わない
            var later=new ForecastPoint(Sample.Now.AddHours(4),Sample.Now.AddHours(7),src){TemperatureC=18};
            var forecast=new Forecast(Sample.Tokyo,[later],[]);
            var c=Presentation.CurrentConditions.From(forecast,Sample.Tokyo.Point,Sample.Now,TimeZoneInfo.Utc,UnitSystem.Metric);
            Assert.Equal("--",c.Temperature);
        }
    }
}
