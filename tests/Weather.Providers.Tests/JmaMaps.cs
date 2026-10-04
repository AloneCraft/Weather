using System.Text;
using Weather.Core;
using Target=Weather.Providers.Jma.JmaMaps;

namespace Weather.Providers.Tests;

public class JmaMaps{
    private static readonly SourceAttribution Source=new(){Provider=ProviderId.Jma,AgencyName="気象庁",ProductName="海上分布予報(風向・風速)",RetrievedAt=DateTimeOffset.UnixEpoch,License=new LicenseInfo("t",null),Processing=DataProcessing.UnitConverted};

    [Fact,Trait("Category","Unit")]public void ParseMarineSpeed(){
        {
            //階級の代表値(中央。上限がない階級は下限 + 5 kt)。NoData は除く。穴のリングの中は含まない
            const string json="""
                {"type":"FeatureCollection","features":[
                  {"type":"Feature","geometry":{"type":"Polygon","coordinates":[[[130,30],[140,30],[140,40],[130,40],[130,30]],[[134,34],[136,34],[136,36],[134,36],[134,34]]]},"properties":{"level":"0<=kt<25"}},
                  {"type":"Feature","geometry":{"type":"MultiPolygon","coordinates":[[[[134,34],[136,34],[136,36],[134,36],[134,34]]]]},"properties":{"level":"65<=kt"}},
                  {"type":"Feature","geometry":{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]},"properties":{"level":"NoData"}}
                ]}
                """;
            var areas=Target.ParseMarineSpeed(Encoding.UTF8.GetBytes(json));
            Assert.Equal(2,areas.Count);
            Assert.Equal(12.5*Target.KnotMs,areas[0].SpeedMs,6);
            Assert.Equal(70*Target.KnotMs,areas[1].SpeedMs,6);
            Assert.True(areas[0].Contains(32,132));
            Assert.False(areas[0].Contains(35,135));
            Assert.True(areas[1].Contains(35,135));
        }
        {
            //読めない階級
            Assert.Null(Target.RepresentativeKnots("NoData"));
            Assert.Equal(27.5,Target.RepresentativeKnots("25<=kt<30"));
        }
    }

    [Fact,Trait("Category","Unit")]public void MarineWindField(){
        //北風(N。北から吹く)は南へ流れる: U = 0、V = -速さ。東風(E)は西へ: U = -速さ
        var speeds=Target.ParseMarineSpeed(Encoding.UTF8.GetBytes("""{"type":"FeatureCollection","features":[{"type":"Feature","geometry":{"type":"Polygon","coordinates":[[[130,30],[140,30],[140,40],[130,40],[130,30]]]},"properties":{"level":"25<=kt<30"}}]}"""));
        WindArrow[] arrows=[new(new GeoPoint(35.25,135.25),0,null),new(new GeoPoint(35.25,135.75),90,null),new(new GeoPoint(34.75,135.25),0,null),new(new GeoPoint(45.25,135.25),0,null)];
        var field=Target.MarineWindField(arrows,speeds,DateTimeOffset.UnixEpoch,DateTimeOffset.UnixEpoch,Source)!;
        var speed=27.5*Target.KnotMs;
        var (u0,v0)=field.SampleNearest(35.25,135.25);
        Assert.Equal(0,u0,3);
        Assert.Equal(-speed,v0,3);
        var (u1,v1)=field.SampleNearest(35.25,135.75);
        Assert.Equal(-speed,u1,3);
        Assert.Equal(0,v1,3);
        //風速の階級の外(北緯 45°)の点は NaN
        Assert.True(float.IsNaN(field.SampleNearest(45.25,135.25).U));
        Assert.Same(Source,field.U.Source);
        //点がなければ作らない
        Assert.Null(Target.MarineWindField([],speeds,DateTimeOffset.UnixEpoch,DateTimeOffset.UnixEpoch,Source));
    }
}
