using Weather.Core;
using Weather.Geo;
using Weather.Geo.Data;

namespace Weather.Geo.Tests;

internal static class Db{
    public static readonly Geo.GeoDatabase Instance=Geo.GeoDatabase.LoadEmbedded();
}

public class LocationResolver{
    private static ResolvedLocation ResolveAt(double lat,double lon){
        return new Geo.LocationResolver(Db.Instance).Resolve(new GeoPoint(lat,lon));
    }

    [Theory,Trait("Category","Unit")]
    [InlineData(35.6940,139.7536,"1310100")]   //千代田区
    [InlineData(26.2124,127.6809,null)]         //那覇市
    [InlineData(43.0621,141.3544,null)]         //札幌市
    [InlineData(27.0940,142.1920,"1342100")]   //小笠原村(父島)
    [InlineData(24.4680,122.9970,null)]         //与那国町
    public void Resolve_InsideJapan(double lat,double lon,string? expectedCode){
        var location=ResolveAt(lat,lon);
        Assert.Equal("JP",location.CountryCode);
        Assert.Equal("Asia/Tokyo",location.TimeZoneId);
        Assert.Equal(JmaAreaMatchKind.Inside,location.JmaArea!.Kind);
        if(expectedCode is not null){
            Assert.Equal(expectedCode,location.JmaArea.Class20Code);
        }
    }

    [Fact,Trait("Category","Unit")]public void Resolve(){
        {
            //東京湾の海上: 20 km 以内の最寄り区域
            var bay=ResolveAt(35.52,139.88);
            Assert.NotNull(bay.JmaArea);
            Assert.NotEqual(JmaAreaMatchKind.OutOfCoverage,bay.JmaArea!.Kind);
            Assert.NotNull(bay.JmaArea.Class20Code);
        }
        {
            //沖合 100 km 超の海上・北方領土: 日本周辺域だが対象外
            foreach(var (lat,lon) in new[]{(34.3,141.6),(43.8,146.75)}){
                var location=ResolveAt(lat,lon);
                Assert.Equal("JP",location.CountryCode);
                Assert.Equal(JmaAreaMatchKind.OutOfCoverage,location.JmaArea!.Kind);
                Assert.Null(location.JmaArea.Class20Code);
            }
        }
        {
            //竹島・尖閣: 気象庁の区域ポリゴン(隠岐の島町・石垣市)の内側または 20 km 以内
            Assert.Equal("3252800",ResolveAt(37.24,131.87).JmaArea!.Class20Code);
            Assert.Equal("4720700",ResolveAt(25.75,123.5).JmaArea!.Class20Code);
        }
        {
            //米国と準州は NWS の国コード
            Assert.Equal("GU",ResolveAt(13.4443,144.7937).CountryCode);
            Assert.Equal("PR",ResolveAt(18.4655,-66.1057).CountryCode);
            Assert.Equal("US",ResolveAt(21.3069,-157.8583).CountryCode);
            Assert.Equal("US",ResolveAt(40.7128,-74.0060).CountryCode);
        }
        {
            //その他の国・公海、日本の近くでも他国は日本周辺域にしない
            var vancouver=ResolveAt(49.2827,-123.1207);
            Assert.Equal("CA",vancouver.CountryCode);
            Assert.Null(vancouver.JmaArea);
            Assert.Equal("America/Vancouver",vancouver.TimeZoneId);
            Assert.Equal("XX",ResolveAt(30,-140).CountryCode);
            var seoul=ResolveAt(37.5665,126.9780);
            Assert.Equal("KR",seoul.CountryCode);
            Assert.Null(seoul.JmaArea);
        }
        {
            //座標はリクエスト用に小数 2 桁へ丸める
            Assert.Equal(35.69,ResolveAt(35.6940,139.7536).Point.Latitude);
        }
    }
}

public class PlaceSearch{
    private static readonly Geo.PlaceSearch Search=new(Db.Instance);

    [Fact,Trait("Category","Unit")]public void Search_(){
        {
            //日本語の入力は気象庁の区域を優先(漢字・かな)
            Assert.Equal("jma:1310100",Search.Search("千代田区",5)[0].Id);
            Assert.Contains(Search.Search("ちよだ",10),static s=>s.Id=="jma:1310100");
            Assert.Contains(Search.Search("チヨダ",10),static s=>s.Id=="jma:1310100");
        }
        {
            //英語・日本語名で世界の都市
            Assert.Equal("GB",Search.Search("London",5)[0].CountryCode);
            Assert.Contains(Search.Search("ロンドン",5),static s=>s.CountryCode=="GB");
            Assert.Equal("US",Search.Search("New York",5)[0].CountryCode);
        }
        {
            //空の入力は結果なし
            Assert.Empty(Search.Search("  ",5));
        }
    }
}

public class TextNormalizer{
    [Fact,Trait("Category","Unit")]public void Normalize(){
        {
            //NFKC・小文字化・カタカナ→ひらがな・空白除去
            Assert.Equal("とうきょう",Geo.TextNormalizer.Normalize("トウキョウ"));
            Assert.Equal("newyork",Geo.TextNormalizer.Normalize("Ｎｅｗ York"));
        }
    }
}

public class GeoDatabase{
    private static int? Expected(double lat,double lon,double maxKm){
        //総当たり(基準)
        PlaceRecord? best=null;
        var bestDistance=maxKm;
        foreach(var place in Db.Instance.Places){
            var d=GeoMath.HaversineKm(lat,lon,place.Latitude,place.Longitude);
            if(d<=bestDistance){
                bestDistance=d;
                best=place;
            }
        }
        return best?.Id;
    }

    [Fact,Trait("Category","Unit")]public void FindNearestPlace(){
        {
            //高緯度でも、経度 1° の距離が短くなる分だけ東西に広く探す(イエローナイフ・ヌーク・北大西洋・千島の東)
            foreach(var (lat,lon) in new[]{(65.0,-124.5),(65.0,-104.5),(60.0,-34.5),(49.82,-20.29),(53.39,171.22),(44.37,155.26)}){
                Assert.Equal(Expected(lat,lon,800),Db.Instance.FindNearestPlace(lat,lon,800)?.Id);
            }
            Assert.Equal("America/Edmonton",Db.Instance.FindNearestPlace(65,-124.5,800)!.TimeZone);
        }
        {
            //経度 ±180 をまたぐ・極に近い地点も総当たりと一致する
            foreach(var (lat,lon) in new[]{(60.0,179.5),(60.0,-179.5),(0.0,179.99),(-45.0,-179.99),(89.0,10.0),(-89.0,100.0)}){
                Assert.Equal(Expected(lat,lon,800),Db.Instance.FindNearestPlace(lat,lon,800)?.Id);
                Assert.Equal(Expected(lat,lon,50),Db.Instance.FindNearestPlace(lat,lon,50)?.Id);
            }
        }
        {
            //全緯度帯の擬似乱数の点(固定シード)で、50 km と 800 km の探索が総当たりと一致する
            var random=new Random(20261004);
            for(var i=0;i<300;i++){
                var lat=-85+random.NextDouble()*170;
                var lon=-180+random.NextDouble()*360;
                foreach(var maxKm in new[]{50.0,800.0}){
                    Assert.Equal(Expected(lat,lon,maxKm),Db.Instance.FindNearestPlace(lat,lon,maxKm)?.Id);
                }
            }
        }
    }
}
