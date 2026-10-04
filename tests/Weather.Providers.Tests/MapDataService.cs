using Weather.Core;

namespace Weather.Providers.Tests;

/// <summary>地図のデータ(気象庁のタイル + GFS)。記録データは 2026-10-04 17 時(日本時間)ごろ、GFS は 00Z 初期値。</summary>
public class MapDataService{
    private static TestHost Host(params string[] unavailableGfs){
        var gfs=Fixtures.MapGfs(unavailableGfs);
        return TestHost.Create(h=>{
            Fixtures.MapJmaTiles(h);
            gfs(h);
        });
    }

    private static DateTimeOffset Utc(int day,int hour,int minute=0){
        return new DateTimeOffset(2026,10,day,hour,minute,0,TimeSpan.Zero);
    }

    [Fact,Trait("Category","Unit")]public async Task GetAvailabilityAsync(){
        using var host=Host();
        var a=await host.Get<IMapDataService>().GetAvailabilityAsync(TestContext.Current.CancellationToken);
        {
            //雨雲の動きは解析(過去 3 時間)と予測(1 時間先)を合わせ、有効時刻の昇順
            Assert.Equal(49,a.Nowcast.Count);
            Assert.Equal(a.Nowcast.Order(),a.Nowcast);
            Assert.Equal(Utc(4,5,15),a.Nowcast[0]);
            Assert.Equal(Utc(4,9,15),a.Nowcast[^1]);
        }
        {
            //GFS は最新の実行回の 3〜120 時間、アメダスは最新の観測時刻
            Assert.Equal(Utc(4,0),a.GfsReferenceTime);
            Assert.Equal(40,a.Gfs.Count);
            Assert.Equal(Utc(4,8,10),a.AmedasTime);
            Assert.Equal(11,a.Distribution.Count);
            Assert.Equal(4,a.Marine.Count);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task GetFrameAsync(){
        var ct=TestContext.Current.CancellationToken;
        using var host=Host();
        var maps=host.Get<IMapDataService>();
        {
            //降水(解析の時刻): 日本は雨雲の動き、日本以外は GFS(日本周辺は NaN)
            var f=await maps.GetFrameAsync(FieldLayer.Precipitation,Utc(4,8,15),ct);
            var tile=Assert.Single(f.Tiles);
            Assert.Equal((JmaTileProduct.Nowcast,"hrpns"),(tile.Product,tile.Element));
            Assert.Equal(JapanCoverage.Available,f.Japan);
            Assert.Equal(FieldQuantity.PrecipitationMmPerHour,f.Scalar!.Quantity);
            Assert.True(float.IsNaN(f.Scalar.Sample(35,140)));
            Assert.Same(MapLegends.RainRate,f.Legend);
            Assert.Contains(f.Sources,static s=>s.Provider==ProviderId.Gfs);
            Assert.Contains(f.Sources,static s=>s.ProductName=="降水ナウキャスト");
        }
        {
            //1 時間先までは雨雲の予測、15 時間先までは今後の雨、その後は天気分布予報の 3 時間降水量
            Assert.Equal(JmaTileProduct.Nowcast,Assert.Single((await maps.GetFrameAsync(FieldLayer.Precipitation,Utc(4,8,45),ct)).Tiles).Product);
            Assert.Equal(JmaTileProduct.RainForecast,Assert.Single((await maps.GetFrameAsync(FieldLayer.Precipitation,Utc(4,14),ct)).Tiles).Product);
            var d=Assert.Single((await maps.GetFrameAsync(FieldLayer.Precipitation,Utc(5,12),ct)).Tiles);
            Assert.Equal((JmaTileProduct.DistributionForecast,"r3"),(d.Product,d.Element));
            Assert.Same(MapLegends.Rain3h,d.Legend);
        }
        {
            //気象庁の予報期間を過ぎた日本周辺は OutOfRange。GFS で埋めない(GFS の日本周辺は NaN のまま)
            var f=await maps.GetFrameAsync(FieldLayer.Precipitation,Utc(7,0),ct);
            Assert.Equal(JapanCoverage.OutOfRange,f.Japan);
            Assert.Empty(f.Tiles);
            Assert.NotNull(f.Scalar);
            Assert.True(float.IsNaN(f.Scalar.Sample(35,140)));
            Assert.DoesNotContain(f.Sources,static s=>s.Provider==ProviderId.Jma);
        }
        {
            //風(観測の時刻): アメダスの観測を矢印で(補間しない)。GFS は U・V と風速
            var f=await maps.GetFrameAsync(FieldLayer.Wind,Utc(4,8,10),ct);
            Assert.Equal(ArrowKind.Observation,f.Arrows!.Kind);
            Assert.NotEmpty(f.Arrows.Arrows);
            Assert.All(f.Arrows.Arrows,static a=>Assert.NotNull(a.SpeedMs));
            Assert.NotNull(f.Wind);
            Assert.Equal(FieldQuantity.WindSpeed,f.Scalar!.Quantity);
            Assert.Contains(f.Sources,static s=>s.ProductName=="アメダス");
            //海上は最も近い時刻(3 時間以内)の海上分布予報の流れ
            Assert.NotNull(f.JapanWind);
        }
        {
            //風(予報の時刻): 海上分布予報の風向(8 方位の矢印)。風速のタイルは使わない
            var f=await maps.GetFrameAsync(FieldLayer.Wind,Utc(4,6),ct);
            Assert.Equal(ArrowKind.Forecast,f.Arrows!.Kind);
            Assert.True(f.Arrows.Arrows.Count>1000);
            Assert.All(f.Arrows.Arrows,static a=>Assert.Equal(0,a.FromDirectionDeg%45));
            Assert.Empty(f.Tiles);
        }
        {
            //風(予報の時刻): 海上の流れは海上分布予報の風向と風速の階級から作る。加工データとして出典を分ける
            var f=await maps.GetFrameAsync(FieldLayer.Wind,Utc(4,6),ct);
            var japan=f.JapanWind!;
            Assert.Equal(ProviderId.Jma,japan.U.Source.Provider);
            Assert.True(japan.U.Source.Processing.HasFlag(DataProcessing.Interpolated));
            Assert.True(japan.U.Source.Processing.HasFlag(DataProcessing.UnitConverted));
            Assert.Contains(f.Sources,static s=>s.ProductName=="海上分布予報(風向・風速)");
            //矢印の点では、流れの向きが矢印と同じで、速さが階級の代表値
            var representatives=new[]{12.5,27.5,32.5,37.5,42.5,47.5,57.5,70}.Select(static k=>k*Jma.JmaMaps.KnotMs).ToArray();
            var matched=0;
            foreach(var a in f.Arrows!.Arrows){
                var (u,v)=japan.SampleAvailable(a.Point.Latitude,a.Point.Longitude);
                if(float.IsNaN(u)){
                    continue;
                }
                matched++;
                var from=(Math.Atan2(-u,-v)*180/Math.PI+360)%360;
                Assert.Equal(a.FromDirectionDeg,from,3);
                var speed=Math.Sqrt(u*u+v*v);
                Assert.Contains(representatives,r=>Math.Abs(r-speed)<1e-3);
            }
            Assert.True(matched>1000,$"流れの値がある矢印の点 {matched}");
            //内陸(長野)は海上分布予報の範囲外で NaN(陸上の風の予報はない)
            Assert.True(float.IsNaN(japan.SampleAvailable(36.65,138.18).U));
        }
        {
            //風(海上分布予報の後): 日本周辺は予報期間外
            Assert.Equal(JapanCoverage.OutOfRange,(await maps.GetFrameAsync(FieldLayer.Wind,Utc(5,9),ct)).Japan);
        }
        {
            //気温(観測の時刻): 天気分布予報がまだない時刻はアメダスの気温
            var f=await maps.GetFrameAsync(FieldLayer.Temperature,Utc(4,8,10),ct);
            Assert.Equal(JapanCoverage.Available,f.Japan);
            Assert.NotEmpty(f.Points!.Points);
            Assert.Equal(FieldQuantity.TemperatureC,f.Points.Quantity);
        }
        {
            //雲・気圧: 日本は天気分布予報の天気、GFS は雲量と海面気圧
            var f=await maps.GetFrameAsync(FieldLayer.Clouds,Utc(4,12),ct);
            var wm=Assert.Single(f.Tiles);
            Assert.Equal("wm",wm.Element);
            Assert.Equal(FieldQuantity.CloudCoverPercent,f.Scalar!.Quantity);
            Assert.Equal(FieldQuantity.PressureHpa,f.Pressure!.Quantity);
        }
        {
            //過去の時刻(天気分布予報より前)の雲は日本周辺に該当なし
            Assert.Equal(JapanCoverage.NotAvailable,(await maps.GetFrameAsync(FieldLayer.Clouds,Utc(4,5),ct)).Japan);
        }
    }

    [Fact,Trait("Category","Unit")]public async Task GetFrameAsync_GfsUnavailable(){
        //GFS が取れなくても気象庁の部分は表示し、取れなかった部分を Issues に出す
        using var host=Host("gfs.2026");
        var f=await host.Get<IMapDataService>().GetFrameAsync(FieldLayer.Precipitation,Utc(4,8,15),TestContext.Current.CancellationToken);
        Assert.Null(f.Scalar);
        Assert.Single(f.Tiles);
        Assert.Contains("GFS",f.Issues);
    }

    [Fact,Trait("Category","Unit")]public async Task GetTileAsync(){
        var ct=TestContext.Current.CancellationToken;
        using var host=Host();
        var maps=host.Get<IMapDataService>();
        var f=await maps.GetFrameAsync(FieldLayer.Precipitation,Utc(4,8,15),ct);
        var layer=f.Tiles[0];
        {
            //タイルの URL は jmatile の形
            Assert.Equal("https://www.jma.go.jp/bosai/jmatile/data/nowc/20261004081500/none/20261004081500/surf/hrpns/4/14/6.png",layer.TileUri(4,14,6).AbsoluteUri);
            var png=await maps.GetTileAsync(layer,4,14,6,ct);
            Assert.Equal((byte)0x89,png![0]);
        }
        {
            //ないタイルは null
            Assert.Null(await maps.GetTileAsync(layer,6,57,25,ct));
        }
    }
}
