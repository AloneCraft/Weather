using CsCheck;
using Weather.Core;
using Weather.Scene;
using Weather.Scene.Astronomy;

namespace Weather.Scene.Tests;

internal static class Data{
    public static readonly DateTimeOffset Noon=new(2026,10,4,3,0,0,TimeSpan.Zero);   //JST 12:00

    public static SourceAttribution Source(ProviderId provider){
        return new SourceAttribution{Provider=provider,AgencyName="t",ProductName="t",RetrievedAt=Noon,License=new LicenseInfo("t",null)};
    }

    public static ResolvedLocation Tokyo=>new(){
        Point=new GeoPoint(35.69,139.75),
        CountryCode="JP",
        DisplayName="千代田区",
        TimeZoneId="Asia/Tokyo",
        JmaArea=new JmaAreaMatch(JmaAreaMatchKind.Inside,"1310100",0),
    };

    public static ResolvedLocation Oslo=>new(){Point=new GeoPoint(59.91,10.75),CountryCode="NO",DisplayName="Oslo",TimeZoneId="Europe/Oslo"};

    public static Forecast Daily(Core.CompositeCondition condition,int? pop=null,string? wind=null){
        var day=new DailyForecast(new DateOnly(2026,10,4),Source(ProviderId.Jma)){Condition=condition,PrecipitationProbability=pop,WindText=wind};
        return new Forecast(Tokyo,[],[day]);
    }

    public static Forecast Point(Core.ForecastPoint point){
        return new Forecast(Oslo,[point],[]);
    }

    public static Core.ForecastPoint MetPoint(Core.WeatherCondition condition){
        return new Core.ForecastPoint(Noon.AddHours(-1),Noon.AddHours(1),Source(ProviderId.MetNorway)){Condition=condition};
    }
}

public class SolarPosition{
    [Fact,Trait("Category","Unit")]public void Compute(){
        {
            //春分の南中: グリニッジの高度 ≒ 90 − 緯度(± 0.6°)
            var greenwich=Astronomy.SolarPosition.Compute(51.4779,0,new DateTimeOffset(2026,3,20,12,7,0,TimeSpan.Zero));
            Assert.InRange(greenwich.AltitudeDeg,38.5-0.6,38.5+0.6);
            Assert.InRange(greenwich.AzimuthDeg,175,185);
        }
        {
            //夏至の東京の南中: 90 − 35.69 + 23.44 ≒ 77.75°
            var tokyo=Astronomy.SolarPosition.Compute(35.69,139.75,new DateTimeOffset(2026,6,21,2,43,0,TimeSpan.Zero));
            Assert.InRange(tokyo.AltitudeDeg,77.75-0.6,77.75+0.6);
        }
        {
            //白夜: トロムソの夏至の真夜中でも太陽高度 > 0(Day 区分)
            var tromso=Astronomy.SolarPosition.Compute(69.65,18.96,new DateTimeOffset(2026,6,21,22,45,0,TimeSpan.Zero));
            Assert.True(tromso.AltitudeDeg>0);
            Assert.Equal(DaylightPhase.Day,Astronomy.SolarPosition.Phase(tromso.AltitudeDeg));
        }
        {
            //朝は東寄り、夕方は西寄り
            var morning=Astronomy.SolarPosition.Compute(35.69,139.75,new DateTimeOffset(2026,10,4,0,0,0,TimeSpan.Zero));
            var evening=Astronomy.SolarPosition.Compute(35.69,139.75,new DateTimeOffset(2026,10,4,7,0,0,TimeSpan.Zero));
            Assert.InRange(morning.AzimuthDeg,60,150);
            Assert.InRange(evening.AzimuthDeg,210,300);
        }
    }

    [Fact,Trait("Category","Unit")]public void Phase(){
        {
            //明暗区分のしきい値
            Assert.Equal(DaylightPhase.Day,Astronomy.SolarPosition.Phase(-0.5));
            Assert.Equal(DaylightPhase.CivilTwilight,Astronomy.SolarPosition.Phase(-3));
            Assert.Equal(DaylightPhase.NauticalTwilight,Astronomy.SolarPosition.Phase(-9));
            Assert.Equal(DaylightPhase.AstronomicalTwilight,Astronomy.SolarPosition.Phase(-15));
            Assert.Equal(DaylightPhase.Night,Astronomy.SolarPosition.Phase(-20));
        }
    }
}

public class MoonCalculator{
    [Fact,Trait("Category","Unit")]public void Compute(){
        {
            //2024-04-08 の皆既日食(新月)と 2024-04-23 の満月
            var newMoon=Astronomy.MoonCalculator.Compute(35.69,139.75,new DateTimeOffset(2024,4,8,18,21,0,TimeSpan.Zero));
            var fullMoon=Astronomy.MoonCalculator.Compute(35.69,139.75,new DateTimeOffset(2024,4,23,23,49,0,TimeSpan.Zero));
            Assert.True(newMoon.Illumination<0.02);
            Assert.True(fullMoon.Illumination>0.98);
            Assert.InRange(fullMoon.Phase,0.45,0.55);
        }
    }
}

public class SceneConverter{
    private static readonly Scene.SceneConverter Converter=new();

    [Fact,Trait("Category","Unit")]public void Convert(){
        {
            //降水確率は降水を作らない(気象庁「晴れ」+ 降水確率 30%)
            var state=Converter.Convert(Data.Daily(new Core.WeatherCondition(SkyCover.Clear),pop:30),Data.Noon);
            Assert.Equal(ScenePrecipitationType.None,state.Precipitation.Type);
            Assert.Equal(0,state.Precipitation.Intensity);
            Assert.Equal(SceneOriginKind.Daily,state.Origin.Kind);
        }
        {
            //時々雨: 断続性 0.5 で重ねる
            var c=new Core.CompositeCondition(new Core.WeatherCondition(SkyCover.Overcast),ConditionTransition.Occasionally,new Core.WeatherCondition(SkyCover.MostlyCloudy,PrecipitationType.Rain,PrecipitationIntensity.Moderate,isShowery:true));
            var state=Converter.Convert(Data.Daily(c),Data.Noon);
            Assert.Equal(ScenePrecipitationType.Rain,state.Precipitation.Type);
            Assert.Equal(0.5,state.Precipitation.Intermittency);
        }
        {
            //雷は条件に雷があるときのみ、強い雷雨は 0.9
            var thunder=Converter.Convert(Data.Point(Data.MetPoint(new Core.WeatherCondition(SkyCover.Overcast,PrecipitationType.Rain,PrecipitationIntensity.Heavy,hasThunder:true))),Data.Noon);
            Assert.Equal(0.9,thunder.ThunderActivity);
            Assert.Equal(SceneCloudType.Cumulonimbus,thunder.Clouds.Type);
            var rain=Converter.Convert(Data.Point(Data.MetPoint(new Core.WeatherCondition(SkyCover.Overcast,PrecipitationType.Rain,PrecipitationIntensity.Light))),Data.Noon);
            Assert.Equal(0,rain.ThunderActivity);
        }
        {
            //数値で補正: 雲量・霧・降水量(mm/h)
            var point=Data.MetPoint(new Core.WeatherCondition(SkyCover.Overcast,PrecipitationType.Rain,PrecipitationIntensity.Light))with{CloudCoverPercent=92,FogPercent=40,PrecipitationMm=10,WindSpeedMs=6,WindDirectionDeg=90};
            var state=Converter.Convert(Data.Point(point),Data.Noon);
            Assert.Equal(0.92,state.Clouds.Cover,3);
            Assert.Equal(0.4,state.FogDensity,3);
            Assert.Equal(0.6,state.Precipitation.Intensity);   //10 mm / 2 h = 5 mm/h
            Assert.Equal(90,state.Wind.DirectionDeg);
        }
        {
            //気象庁の風の文章 → 演出用の近似風速
            var state=Converter.Convert(Data.Daily(new Core.WeatherCondition(SkyCover.Clear),wind:"北の風　やや強く"),Data.Noon);
            Assert.Equal(7,state.Wind.SpeedMs);
        }
        {
            //データがない時刻は天気なし
            var empty=new Forecast(Data.Oslo,[],[]);
            var state=Converter.Convert(empty,Data.Noon);
            Assert.Equal(SceneOriginKind.None,state.Origin.Kind);
            Assert.Equal(0,state.Clouds.Cover);
        }
        {
            //決定的: 同じ入力から同じ出力
            var f=Data.Point(Data.MetPoint(new Core.WeatherCondition(SkyCover.PartlyCloudy)));
            Assert.Equal(Converter.Convert(f,Data.Noon),Converter.Convert(f,Data.Noon));
        }
    }

    [Fact,Trait("Category","Unit")]public void Convert_Properties(){
        //性質ベース: 任意の数値・分類でも範囲内に収まり、降水なしなら強度 0、雷は降水があるときのみ
        var gen=Gen.Select(Gen.Int[0,5],Gen.Int[0,7],Gen.Int[0,3],Gen.Bool,Gen.Int[0,100],Gen.Double[0,60],Gen.Int[0,100],Gen.Int[0,100]);
        gen.Sample(static t=>{
            var (sky,precip,inten,thunder,cloud,mm,thunderProb,pop)=t;
            var type=(PrecipitationType)precip;
            var intensity=(PrecipitationIntensity)inten;
            if(type==PrecipitationType.None){
                intensity=PrecipitationIntensity.None;
            }else if(intensity==PrecipitationIntensity.None){
                intensity=PrecipitationIntensity.Light;
            }
            var condition=new Core.WeatherCondition((SkyCover)sky,type,intensity,hasThunder:thunder);
            var point=Data.MetPoint(condition)with{CloudCoverPercent=cloud,PrecipitationMm=mm,ThunderProbability=thunderProb,PrecipitationProbability=pop};
            var state=Converter.Convert(Data.Point(point),Data.Noon);
            if(state.Precipitation.Type==ScenePrecipitationType.None){
                return state.Precipitation.Intensity==0&&state.ThunderActivity==0;
            }
            return state.Precipitation.Intensity>0&&state.Clouds.Cover is >=0 and <=1;
        });
    }

    [Fact,Trait("Category","Unit")]public void ConvertSkyOnly(){
        {
            //日本の対象外地点は天気を描かない
            var state=Converter.ConvertSkyOnly(new GeoPoint(43.8,146.75),Data.Noon,SceneOriginKind.OutOfCoverage);
            Assert.Equal(ScenePrecipitationType.None,state.Precipitation.Type);
            Assert.Equal(0,state.Clouds.Cover);
            Assert.Equal(0,state.FogDensity);
            Assert.Equal(0,state.ThunderActivity);
        }
    }
}

public class SceneTimeline{
    [Fact,Trait("Category","Unit")]public void At(){
        var source=Data.Source(ProviderId.MetNorway);
        var start=Data.Noon;
        var points=new[]{
            new Core.ForecastPoint(start,start.AddHours(1),source){Condition=new Core.WeatherCondition(SkyCover.Clear),CloudCoverPercent=0},
            new Core.ForecastPoint(start.AddHours(1),start.AddHours(2),source){Condition=new Core.WeatherCondition(SkyCover.Overcast),CloudCoverPercent=100},
        };
        var timeline=new Scene.SceneTimeline(new Forecast(Data.Oslo,points,[]),new Scene.SceneConverter());
        {
            //区間の中点どうしを線形補間する(境界では中間の値)
            var middle=timeline.At(start.AddHours(1));
            Assert.InRange(middle.Clouds.Cover,0.45,0.55);
        }
        {
            //連続値は補間の範囲外に飛ばない
            for(var m=30;m<=90;m+=5){
                var c=timeline.At(start.AddMinutes(m)).Clouds.Cover;
                Assert.InRange(c,0,1);
            }
        }
        {
            //「後」(6 時間以上の区間): 中点より前は主天気(晴れ)、後は副天気(雨)。前半に雨が出ない
            var later=new Core.CompositeCondition(new Core.WeatherCondition(SkyCover.Clear),Core.ConditionTransition.Later,new Core.WeatherCondition(SkyCover.Overcast,Core.PrecipitationType.Rain,Core.PrecipitationIntensity.Moderate));
            var laterPoints=new[]{
                new Core.ForecastPoint(start,start.AddHours(6),source){Condition=new Core.WeatherCondition(SkyCover.Clear)},
                new Core.ForecastPoint(start.AddHours(6),start.AddHours(12),source){Condition=later},
            };
            var laterTimeline=new Scene.SceneTimeline(new Forecast(Data.Oslo,laterPoints,[]),new Scene.SceneConverter());
            for(var m=0;m<=7.5*60;m+=30){
                var s=laterTimeline.At(start.AddMinutes(m));
                Assert.Equal(ScenePrecipitationType.None,s.Precipitation.Type);
            }
            Assert.Equal(ScenePrecipitationType.Rain,laterTimeline.At(start.AddHours(10.5)).Precipitation.Type);
            Assert.Equal(ScenePrecipitationType.Rain,laterTimeline.At(start.AddHours(11.5)).Precipitation.Type);
        }
    }
}

public class ArchitectureRules{
    [Fact,Trait("Category","Unit")]public void NoMauiReference(){
        var names=typeof(Scene.SceneState).Assembly.GetReferencedAssemblies().Select(static a=>a.Name??"");
        Assert.DoesNotContain(names,static n=>n.StartsWith("Microsoft.Maui",StringComparison.Ordinal)||n.StartsWith("SkiaSharp",StringComparison.Ordinal));
    }
}
