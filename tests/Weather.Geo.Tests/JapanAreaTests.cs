using Weather.Core;

namespace Weather.Geo.Tests;

public class JapanArea{
    [Fact,Trait("Category","Unit")]public void Contains(){
        var area=new Geo.JapanArea(Db.Instance);
        var resolver=new Geo.LocationResolver(Db.Instance);
        {
            //地点の解決(ResolvedLocation.IsJapanArea)と同じ判定になる(地図と地点で日本周辺の範囲を食い違わせない)
            for(var lat=18.0;lat<=52;lat+=0.75){
                for(var lon=118.0;lon<=158;lon+=0.75){
                    var expected=resolver.Resolve(new GeoPoint(lat,lon)).IsJapanArea;
                    Assert.True(expected==area.Contains(lat,lon),$"({lat},{lon}) resolver={expected}");
                }
            }
        }
        {
            //代表点: 東京・那覇・南鳥島・沖ノ鳥島は日本周辺。釜山・上海・ウラジオストク・ソウルは外
            Assert.True(area.Contains(35.69,139.75));
            Assert.True(area.Contains(26.21,127.68));
            Assert.True(area.Contains(24.28,153.98));
            Assert.True(area.Contains(20.42,136.08));
            Assert.False(area.Contains(35.10,129.04));
            Assert.False(area.Contains(31.23,121.47));
            Assert.False(area.Contains(43.12,131.89));
            Assert.False(area.Contains(37.57,126.98));
        }
    }
}
