using Weather.Core;

namespace Weather.Core.Tests;

public class GridGeometry{
    [Fact,Trait("Category","Unit")]public void TryGetPosition(){
        var global=new Core.GridGeometry(720,361,90,0,0.5,0.5);
        {
            //経度方向に一周する格子(GFS 0.5°)は経度を循環させる
            Assert.True(global.WrapsLongitude);
            Assert.True(global.TryGetPosition(0,-0.25,out var x,out var y));
            Assert.Equal(719.5,x,6);
            Assert.Equal(180,y,6);
        }
        {
            //緯度の範囲外は false
            Assert.False(global.TryGetPosition(91,0,out _,out _));
        }
        {
            //一周しない格子は経度の範囲外も false
            var local=new Core.GridGeometry(10,10,40,130,1,1);
            Assert.False(local.WrapsLongitude);
            Assert.False(local.TryGetPosition(35,129,out _,out _));
            Assert.True(local.TryGetPosition(35,135.5,out var lx,out var ly));
            Assert.Equal((5.5,5.0),(lx,ly));
        }
        {
            //不正な格子は作れない
            Assert.Throws<ArgumentOutOfRangeException>(()=>new Core.GridGeometry(1,10,40,130,1,1));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new Core.GridGeometry(10,10,40,130,0,1));
        }
    }
}

public class GridField{
    private static readonly SourceAttribution Source=new(){Provider=ProviderId.Gfs,AgencyName="NOAA",ProductName="GFS",RetrievedAt=DateTimeOffset.UnixEpoch,License=new LicenseInfo("PD",null)};

    private static Core.GridField Field(float[] values,int columns,int rows){
        return new Core.GridField(new Core.GridGeometry(columns,rows,1,0,1,360.0/columns),values,FieldQuantity.TemperatureC,DateTimeOffset.UnixEpoch,DateTimeOffset.UnixEpoch,Source);
    }

    [Fact,Trait("Category","Unit")]public void Sample(){
        {
            //双線形補間
            var f=Field([0,10,20,30,40,50,60,70],4,2);
            Assert.Equal(5f,f.Sample(1,45));
            Assert.Equal(25f,f.Sample(0.5,45));
        }
        {
            //経度方向に循環する(最後の列と最初の列の間)
            var f=Field([0,10,20,30,0,10,20,30],4,2);
            Assert.Equal(15f,f.Sample(1,315));
        }
        {
            //周囲に NaN(欠損・日本周辺)があれば NaN
            var f=Field([0,float.NaN,20,30,40,50,60,70],4,2);
            Assert.True(float.IsNaN(f.Sample(1,45)));
            Assert.False(float.IsNaN(f.Sample(1,180)));
        }
        {
            //値の数が格子と合わない・有効時刻が初期時刻より前は作れない
            Assert.Throws<ArgumentException>(()=>Field([0,1,2],4,2));
            Assert.Throws<ArgumentException>(()=>new Core.GridField(new Core.GridGeometry(2,2,1,0,1,1),[0,0,0,0],FieldQuantity.TemperatureC,DateTimeOffset.UnixEpoch,DateTimeOffset.UnixEpoch.AddHours(1),Source));
        }
    }

    [Fact,Trait("Category","Unit")]public void SampleAvailable(){
        {
            //欠けた点がなければ Sample と同じ
            var f=Field([0,10,20,30,40,50,60,70],4,2);
            Assert.Equal(f.Sample(0.5,45),f.SampleAvailable(0.5,45));
            Assert.Equal(15f,f.SampleAvailable(1,315));
        }
        {
            //欠けた点を除いて重みを付け直す(海岸近くで陸側の点が欠けていても値を返す)
            var f=Field([0,float.NaN,20,30,40,50,60,70],4,2);
            Assert.Equal(0f,f.SampleAvailable(1,40));
            Assert.Equal(30f,f.SampleAvailable(0.5,45),3);
        }
        {
            //最も近い点が欠けていれば NaN(値のない区画に広げない)
            var f=Field([0,float.NaN,20,30,40,50,60,70],4,2);
            Assert.True(float.IsNaN(f.SampleAvailable(0.9,80)));
        }
        {
            //4 点とも欠けていれば NaN
            var f=Field([float.NaN,float.NaN,20,30,float.NaN,float.NaN,60,70],4,2);
            Assert.True(float.IsNaN(f.SampleAvailable(0.5,45)));
        }
    }
}

public class Legend{
    [Fact,Trait("Category","Unit")]public void Classify(){
        {
            //区分は [下限, 上限)。最初の下限未満と NaN は null(描かない)
            var rain=MapLegends.RainRate;
            Assert.Null(rain.Classify(0.05));
            Assert.Equal("1",rain.Classify(1)!.Key);
            Assert.Equal("80",rain.Classify(120)!.Key);
            Assert.Null(rain.Classify(double.NaN));
        }
        {
            //下限のない区分(気温の -25 未満)
            Assert.Equal("below-25",MapLegends.Temperature.Classify(-40)!.Key);
            Assert.Equal("20",MapLegends.Temperature.Classify(24.9)!.Key);
        }
    }

    [Fact,Trait("Category","Unit")]public void MatchColor(){
        {
            //タイルの画素の色から区分を引く。凡例画像と数段階違う色(250,245,0)も同じ区分
            Assert.Equal("20",MapLegends.RainRate.MatchColor(0xFFFAF500)!.Key);
            Assert.Equal("1",MapLegends.RainRate.MatchColor(0xFFA0D2FF)!.Key);
            Assert.Equal("rain",MapLegends.Weather.MatchColor(0xFF0041FF)!.Key);
        }
        {
            //透明・凡例にない色は null
            Assert.Null(MapLegends.RainRate.MatchColor(0x00000000));
            Assert.Null(MapLegends.RainRate.MatchColor(0xFF00FF00));
        }
    }
}

public class JmaTileLayer{
    private static Core.JmaTileLayer Layer(bool even,int max){
        return new Core.JmaTileLayer{
            Product=JmaTileProduct.Nowcast,
            Element="hrpns",
            BaseTime=new DateTimeOffset(2026,10,4,8,15,0,TimeSpan.Zero),
            ValidTime=new DateTimeOffset(2026,10,4,8,20,0,TimeSpan.Zero),
            MinZoom=4,
            MaxZoom=max,
            EvenZoomOnly=even,
            Legend=MapLegends.RainRate,
            Source=new SourceAttribution{Provider=ProviderId.Jma,AgencyName="気象庁",ProductName="降水ナウキャスト",RetrievedAt=DateTimeOffset.UnixEpoch,License=new LicenseInfo("t",null)},
        };
    }

    [Fact,Trait("Category","Unit")]public void TileUri(){
        //jmatile の URL(時刻は UTC の yyyyMMddHHmmss)
        Assert.Equal("https://www.jma.go.jp/bosai/jmatile/data/nowc/20261004081500/none/20261004082000/surf/hrpns/6/56/25.png",Layer(true,10).TileUri(6,56,25).AbsoluteUri);
    }

    [Fact,Trait("Category","Unit")]public void TileZoomFor(){
        {
            //偶数ズームだけのタイルは偶数に丸め、範囲に収める
            var layer=Layer(true,10);
            Assert.Equal(6,layer.TileZoomFor(7.2));
            Assert.Equal(4,layer.TileZoomFor(2));
            Assert.Equal(10,layer.TileZoomFor(13));
        }
        {
            //すべてのズームがあるタイル(海上分布予報。最大 8)
            var layer=Layer(false,8);
            Assert.Equal(7,layer.TileZoomFor(7.2));
            Assert.Equal(8,layer.TileZoomFor(9.5));
        }
    }
}
