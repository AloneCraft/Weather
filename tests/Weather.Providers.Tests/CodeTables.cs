using System.Text.Json;
using Weather.Core;
using Weather.Providers.Jma;
using Weather.Providers.MetNorway;
using Weather.Providers.Nws;

namespace Weather.Providers.Tests;

public class JmaWeatherCodes{
    [Fact,Trait("Category","Unit")]public void Find(){
        {
            //気象庁ページの TELOPS(118 件)がすべて変換される
            using var doc=JsonDocument.Parse(File.ReadAllBytes(FixtureHttpMessageHandler.FixturePath("jma/telops.json")));
            var codes=doc.RootElement.EnumerateObject().Select(static p=>p.Name).ToList();
            Assert.Equal(118,codes.Count);
            Assert.All(codes,static c=>Assert.NotNull(Jma.JmaWeatherCodes.Find(c)));
        }
        {
            //代表例: 201 曇時々晴 / 308 雨で暴風を伴う / 209 霧
            var c201=Jma.JmaWeatherCodes.Find("201")!.Condition;
            Assert.Equal(SkyCover.Overcast,c201.Primary.Sky);
            Assert.Equal(ConditionTransition.Occasionally,c201.Transition);
            Assert.Equal(SkyCover.Clear,c201.Secondary!.Value.Sky);
            var c308=Jma.JmaWeatherCodes.Find("308")!.Condition.Primary;
            Assert.Equal(PrecipitationIntensity.Heavy,c308.Intensity);
            Assert.True(c308.HasStrongWind);
            Assert.Equal(Obscuration.Fog,Jma.JmaWeatherCodes.Find("209")!.Condition.Primary.Obscuration);
        }
        {
            //未知のコードは null(表示は原文)
            Assert.Null(Jma.JmaWeatherCodes.Find("999"));
        }
    }
}

public class JmaWarningCodes{
    [Fact,Trait("Category","Unit")]public void Find(){
        {
            //39 件。警戒レベルは大雨・土砂災害・氾濫・高潮のみ
            Assert.Equal(39,Jma.JmaWarningCodes.Codes.Count);
            foreach(var code in Jma.JmaWarningCodes.Codes){
                var w=Jma.JmaWarningCodes.Find(code)!;
                var hazard=w.Element is "rain" or "landslide" or "flood" or "tide";
                Assert.Equal(hazard,w.WarningLevel is not null);
            }
        }
        {
            //レベル4 危険警報
            var c43=Jma.JmaWarningCodes.Find("43")!;
            Assert.Equal("レベル4大雨危険警報",c43.Name);
            Assert.Equal(AlertTier.Danger,c43.Tier);
            Assert.Equal(4,c43.WarningLevel);
        }
    }
}

public class MetSymbolParser{
    [Fact,Trait("Category","Unit")]public void Parse(){
        {
            //legend.csv の 41 件すべて(昼・夜・薄明の接尾辞付きも)が変換される
            var symbols=File.ReadAllLines(FixtureHttpMessageHandler.FixturePath("met/legend.csv")).Skip(1).Select(static l=>l.Split(',')[0].Trim()).Where(static s=>s.Length>0).ToList();
            Assert.Equal(41,symbols.Count);
            foreach(var symbol in symbols){
                Assert.NotNull(MetNorway.MetSymbolParser.Parse(symbol));
                Assert.NotNull(MetNorway.MetSymbolParser.Parse(symbol+"_day"));
                Assert.NotNull(MetNorway.MetSymbolParser.Parse(symbol+"_polartwilight"));
            }
        }
        {
            //誤綴りと正しい綴りの両方を受け付ける
            Assert.Equal(PrecipitationIntensity.Light,MetNorway.MetSymbolParser.Parse("lightssleetshowersandthunder")!.Value.Intensity);
            Assert.Equal(PrecipitationIntensity.Light,MetNorway.MetSymbolParser.Parse("lightsleetshowersandthunder")!.Value.Intensity);
        }
        {
            //sleet(ノルウェー語 sludd)はみぞれ
            Assert.Equal(PrecipitationType.RainAndSnow,MetNorway.MetSymbolParser.Parse("sleet_day")!.Value.Precipitation);
            Assert.Null(MetNorway.MetSymbolParser.Parse("unknownsymbol"));
        }
    }
}

public class NwsForecastPhraseParser{
    [Fact,Trait("Category","Unit")]public void Parse(){
        {
            //"then" の後は Later、確率語は除去
            var c=Nws.NwsForecastPhraseParser.Parse("Chance Rain Showers then Mostly Sunny")!.Value;
            Assert.Equal(PrecipitationType.Rain,c.Primary.Precipitation);
            Assert.True(c.Primary.IsShowery);
            Assert.Equal(ConditionTransition.Later,c.Transition);
            Assert.Equal(SkyCover.MostlyClear,c.Secondary!.Value.Sky);
        }
        {
            //米国の sleet は凍雨(IcePellets)、Snow Showers は雪
            Assert.Equal(PrecipitationType.IcePellets,Nws.NwsForecastPhraseParser.Parse("Sleet Likely")!.Value.Primary.Precipitation);
            Assert.Equal(PrecipitationType.Snow,Nws.NwsForecastPhraseParser.Parse("Snow Showers")!.Value.Primary.Precipitation);
            Assert.Equal(PrecipitationType.RainAndSnow,Nws.NwsForecastPhraseParser.Parse("Rain And Snow")!.Value.Primary.Precipitation);
        }
        {
            //雷雨・霧・強風
            Assert.True(Nws.NwsForecastPhraseParser.Parse("Slight Chance Showers And Thunderstorms")!.Value.Primary.HasThunder);
            Assert.Equal(Obscuration.Fog,Nws.NwsForecastPhraseParser.Parse("Patchy Fog")!.Value.Primary.Obscuration);
            Assert.True(Nws.NwsForecastPhraseParser.Parse("Sunny and Breezy")!.Value.Primary.HasStrongWind);
            Assert.Null(Nws.NwsForecastPhraseParser.Parse("Zzz"));
        }
    }
}

public class NwsGridWeather{
    [Fact,Trait("Category","Unit")]public void FromValues(){
        {
            //OpenAPI の weather enum(23 件)に未知がない
            foreach(var weather in Nws.NwsGridWeather.KnownWeather){
                var unknown=new List<string>();
                using var doc=JsonDocument.Parse($"[{{\"weather\":\"{weather}\",\"intensity\":\"light\",\"attributes\":[]}}]");
                Nws.NwsGridWeather.FromValues(doc.RootElement,SkyCover.Unknown,unknown);
                Assert.Empty(unknown);
            }
        }
    }
}
