using Weather.Core;

namespace Weather.Core.Tests;

internal static class Sources{
    public static SourceAttribution Of(ProviderId provider,DataProcessing processing=DataProcessing.None){
        return new SourceAttribution{
            Provider=provider,
            AgencyName="test",
            ProductName="test",
            RetrievedAt=DateTimeOffset.UnixEpoch,
            License=new LicenseInfo("test",null),
            Processing=processing,
        };
    }

    public static ResolvedLocation Location(string country,JmaAreaMatch? jma=null){
        return new ResolvedLocation{Point=new Core.GeoPoint(35,139),CountryCode=country,DisplayName="x",TimeZoneId="Asia/Tokyo",JmaArea=jma};
    }
}

public class WeatherCondition{
    [Fact,Trait("Category","Unit")]public void Constructor(){
        {
            //降水種別 None ⇔ 強度 None
            Assert.Throws<ArgumentException>(static ()=>new Core.WeatherCondition(SkyCover.Clear,PrecipitationType.Rain,PrecipitationIntensity.None));
            Assert.Throws<ArgumentException>(static ()=>new Core.WeatherCondition(SkyCover.Clear,PrecipitationType.None,PrecipitationIntensity.Light));
        }
        {
            //既定値は Unknown・降水なし
            Assert.False(default(Core.WeatherCondition).HasPrecipitation);
        }
    }
}

public class CompositeCondition{
    [Fact,Trait("Category","Unit")]public void Constructor(){
        {
            //Transition と Secondary はどちらも指定するかどちらも省略する
            Assert.Throws<ArgumentException>(static ()=>new Core.CompositeCondition(new Core.WeatherCondition(SkyCover.Clear),ConditionTransition.Later,null));
            var ok=new Core.CompositeCondition(new Core.WeatherCondition(SkyCover.Clear),ConditionTransition.Later,new Core.WeatherCondition(SkyCover.Overcast));
            Assert.Equal(SkyCover.Overcast,ok.Secondary!.Value.Sky);
        }
    }
}

public class ForecastPoint{
    [Fact,Trait("Category","Unit")]public void Constructor(){
        var t=DateTimeOffset.UnixEpoch;
        {
            //Start < End
            Assert.Throws<ArgumentException>(()=>new Core.ForecastPoint(t,t,Sources.Of(ProviderId.MetNorway)));
        }
        {
            //範囲の検証(with 式でも検証される)
            var p=new Core.ForecastPoint(t,t.AddHours(1),Sources.Of(ProviderId.MetNorway)){PrecipitationProbability=50};
            Assert.Throws<ArgumentOutOfRangeException>(()=>p with{PrecipitationProbability=101});
            Assert.Throws<ArgumentOutOfRangeException>(()=>p with{WindDirectionDeg=360});
            Assert.Throws<ArgumentOutOfRangeException>(()=>p with{PrecipitationMm=-1});
            Assert.Throws<ArgumentOutOfRangeException>(()=>p with{TemperatureC=double.NaN});
        }
    }
}

public class Forecast{
    [Fact,Trait("Category","Unit")]public void Constructor(){
        var t=DateTimeOffset.UnixEpoch;
        var met=Sources.Of(ProviderId.MetNorway);
        var jma=Sources.Of(ProviderId.Jma);
        var japan=Sources.Location("JP",new JmaAreaMatch(JmaAreaMatchKind.Inside,"1310100",0));
        {
            //時系列の区間の重なりは不可
            var a=new Core.ForecastPoint(t,t.AddHours(2),met);
            var b=new Core.ForecastPoint(t.AddHours(1),t.AddHours(3),met);
            Assert.Throws<ArgumentException>(()=>new Core.Forecast(Sources.Location("NO"),[a,b],[]));
        }
        {
            //日別の日付の重複は不可
            var d=new DailyForecast(new DateOnly(2026,10,4),met);
            Assert.Throws<ArgumentException>(()=>new Core.Forecast(Sources.Location("NO"),[],[d,d]));
        }
        {
            //日本域に気象庁以外のデータは不可(方針 5)
            var p=new Core.ForecastPoint(t,t.AddHours(1),met);
            Assert.Throws<InvalidOperationException>(()=>new Core.Forecast(japan,[p],[]));
            var outOfCoverage=Sources.Location("XX",new JmaAreaMatch(JmaAreaMatchKind.OutOfCoverage,null,50));
            Assert.Throws<InvalidOperationException>(()=>new Core.Forecast(outOfCoverage,[p],[]));
        }
        {
            //気象庁データにアプリ側の集計は不可、気温の出典も検査する
            var aggregated=new DailyForecast(new DateOnly(2026,10,4),Sources.Of(ProviderId.Jma,DataProcessing.Aggregated));
            Assert.Throws<InvalidOperationException>(()=>new Core.Forecast(japan,[],[aggregated]));
            var mixed=new DailyForecast(new DateOnly(2026,10,4),jma){TemperatureSource=met};
            Assert.Throws<InvalidOperationException>(()=>new Core.Forecast(japan,[],[mixed]));
        }
        {
            //気象庁のみなら成立し、出典を重複なく列挙できる
            var day=new DailyForecast(new DateOnly(2026,10,4),jma);
            var forecast=new Core.Forecast(japan,[new Core.ForecastPoint(t,t.AddHours(3),jma)],[day]);
            Assert.Single(forecast.Sources);
            Assert.False(forecast.IsStale);
        }
    }
}

public class GeoPoint{
    [Fact,Trait("Category","Unit")]public void RoundForRequest(){
        {
            //小数 2 桁に丸める
            var p=new Core.GeoPoint(35.68123,139.76789).RoundForRequest();
            Assert.Equal(35.68,p.Latitude);
            Assert.Equal(139.77,p.Longitude);
        }
        {
            //範囲外は例外
            Assert.Throws<ArgumentOutOfRangeException>(static ()=>new Core.GeoPoint(91,0));
            Assert.Throws<ArgumentOutOfRangeException>(static ()=>new Core.GeoPoint(0,181));
        }
    }

    [Fact,Trait("Category","Unit")]public void DistanceKmTo(){
        {
            //東京〜大阪 約 400 km
            var tokyo=new Core.GeoPoint(35.68,139.77);
            var osaka=new Core.GeoPoint(34.69,135.50);
            Assert.InRange(tokyo.DistanceKmTo(osaka),390,410);
        }
    }
}

public class Alert{
    [Fact,Trait("Category","Unit")]public void Constructor(){
        {
            //警戒レベルは 2〜5
            var a=new Core.Alert("1","大雨警報",AlertTier.Warning,AlertStatus.Active,Sources.Of(ProviderId.Jma));
            Assert.Throws<ArgumentOutOfRangeException>(()=>a with{WarningLevel=1});
            Assert.Equal(3,(a with{WarningLevel=3}).WarningLevel);
        }
    }
}
