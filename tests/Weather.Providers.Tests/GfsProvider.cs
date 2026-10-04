using Weather.Core;
using Weather.Providers.Gfs;
using Target=Weather.Providers.Gfs.GfsProvider;

namespace Weather.Providers.Tests;

public class GfsProvider{
    private static readonly DateTimeOffset Cycle=new(2026,10,4,0,0,0,TimeSpan.Zero);

    [Fact,Trait("Category","Unit")]public async Task GetLatestCycleAsync(){
        var ct=TestContext.Current.CancellationToken;
        {
            //最後の予報時間の索引がある最新の実行回
            using var host=TestHost.Create(Fixtures.MapGfs());
            Assert.Equal(Cycle,await host.Get<Target>().GetLatestCycleAsync(ct));
            Assert.Contains(host.Handler.Requests,static r=>r.RequestUri!.AbsoluteUri.EndsWith("/gfs.20261004/00/atmos/gfs.t00z.pgrb2.1p00.f120.idx",StringComparison.Ordinal));
        }
        {
            //まだ出ていない実行回(404)は飛ばして一つ前
            using var host=TestHost.Create(Fixtures.MapGfs("gfs.20261004/00"));
            Assert.Equal(Cycle.AddHours(-6),await host.Get<Target>().GetLatestCycleAsync(ct));
        }
        {
            //どの実行回もなければ null
            using var host=TestHost.Create(Fixtures.MapGfs("gfs.2026"));
            Assert.Null(await host.Get<Target>().GetLatestCycleAsync(ct));
        }
    }

    [Fact,Trait("Category","Unit")]public void ValidTimes(){
        //3 時間ごと、120 時間先まで(0 時間は降水強度がないため除く)
        using var host=TestHost.Create(Fixtures.MapGfs());
        var times=host.Get<Target>().ValidTimes(Cycle);
        Assert.Equal(40,times.Count);
        Assert.Equal(Cycle.AddHours(3),times[0]);
        Assert.Equal(Cycle.AddHours(120),times[^1]);
    }

    [Fact,Trait("Category","Unit")]public async Task GetFieldAsync(){
        var ct=TestContext.Current.CancellationToken;
        using var host=TestHost.Create(Fixtures.MapGfs());
        var gfs=host.Get<Target>();
        {
            //気温: ℃ に換算、格子は北端・西端から 1° 間隔、有効時刻は初期時刻 + 3 時間
            var t=await gfs.GetFieldAsync(GfsElement.Temperature,Cycle,3,ct);
            Assert.Equal(new GridGeometry(360,181,90,0,1,1),t.Geometry);
            Assert.Equal(FieldQuantity.TemperatureC,t.Quantity);
            Assert.Equal(Cycle.AddHours(3),t.ValidTime);
            Assert.Equal(Cycle,t.ReferenceTime);
            Assert.All(t.Values.Where(static v=>!float.IsNaN(v)),static v=>Assert.InRange(v,-90f,60f));
            Assert.InRange(t.Sample(-80,0),-90f,-10f);
        }
        {
            //日本周辺(テストでは矩形)は NaN。外側は値がある(方針 5)
            var t=await gfs.GetFieldAsync(GfsElement.Temperature,Cycle,3,ct);
            Assert.True(float.IsNaN(t.Sample(35,140)));
            Assert.True(float.IsNaN(t[140,90-35]));
            Assert.False(float.IsNaN(t.Sample(35,100)));
            Assert.False(float.IsNaN(t.Sample(35,-100)));
        }
        {
            //1 要素だけを Range 要求で取る
            Assert.Contains(host.Handler.Requests,static r=>r.Headers.Range is not null&&r.RequestUri!.AbsolutePath.EndsWith(".f003",StringComparison.Ordinal));
        }
        {
            //出典: NOAA GFS、初期時刻、補間と単位換算の表示
            var t=await gfs.GetFieldAsync(GfsElement.Temperature,Cycle,3,ct);
            Assert.Equal(ProviderId.Gfs,t.Source.Provider);
            Assert.Equal(Cycle,t.Source.IssuedAt);
            Assert.True(t.Source.Processing.HasFlag(DataProcessing.Interpolated));
        }
        {
            //降水強度は mm/h(0 以上)、海面気圧は hPa、雲量は %
            var p=await gfs.GetFieldAsync(GfsElement.PrecipitationRate,Cycle,3,ct);
            Assert.All(p.Values.Where(static v=>!float.IsNaN(v)),static v=>Assert.InRange(v,0f,200f));
            var slp=await gfs.GetFieldAsync(GfsElement.Pressure,Cycle,3,ct);
            Assert.All(slp.Values.Where(static v=>!float.IsNaN(v)),static v=>Assert.InRange(v,900f,1100f));
            var c=await gfs.GetFieldAsync(GfsElement.CloudCover,Cycle,3,ct);
            Assert.All(c.Values.Where(static v=>!float.IsNaN(v)),static v=>Assert.InRange(v,0f,100f));
        }
    }

    [Fact,Trait("Category","Unit")]public void GfsIndex(){
        var text=File.ReadAllText(FixtureHttpMessageHandler.FixturePath(GfsFixture.File+".idx"));
        var entries=Gfs.GfsIndex.Parse(text);
        {
            //各行の範囲は次の行の直前まで。最後の行は終端なし
            Assert.Equal(6,entries.Count);
            Assert.Equal(entries[1].Offset-1,entries[0].End);
            Assert.Null(entries[^1].End);
        }
        {
            //降水強度は瞬間値の行(平均値の行ではない)
            var line="1:0:d=2026100400:PRATE:surface:0-6 hour ave fcst:\n2:100:d=2026100400:PRATE:surface:6 hour fcst:\n";
            var e=Gfs.GfsIndex.Find(Gfs.GfsIndex.Parse(line),GfsElement.PrecipitationRate);
            Assert.Equal(100,e!.Offset);
        }
        {
            //壊れた行は FormatException
            Assert.Throws<FormatException>(()=>Gfs.GfsIndex.Parse("broken line\n"));
        }
    }
}
