using System.Globalization;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Weather.Core;
using Weather.Presentation;
using Weather.Presentation.ViewModels;

namespace Weather.Presentation.Tests;

internal static class TestCulture{
    /// <summary>既存のテストは日本語の文言を期待するため、実行環境の言語に関係なく既定を日本語にする。</summary>
    [ModuleInitializer]
    public static void Initialize(){
        CultureInfo.DefaultThreadCurrentUICulture=CultureInfo.GetCultureInfo("ja-JP");
        CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo("ja-JP");
    }
}

public class Strings{
    [Fact,Trait("Category","Unit")]public void ResourceManager(){
        {
            //日本語(既定)と英語のリソースのキーが一致する
            var dir=FindResources();
            var ja=Keys(Path.Combine(dir,"Strings.resx"));
            var en=Keys(Path.Combine(dir,"Strings.en.resx"));
            Assert.NotEmpty(ja);
            Assert.Equal(ja.Order(StringComparer.Ordinal),en.Order(StringComparer.Ordinal));
        }
        {
            //書式文字列の引数の数が言語間で一致する
            var dir=FindResources();
            var jaValues=Values(Path.Combine(dir,"Strings.resx"));
            var enValues=Values(Path.Combine(dir,"Strings.en.resx"));
            foreach(var (key,value) in jaValues){
                Assert.Equal(Placeholders(value),Placeholders(enValues[key]));
            }
        }
    }

    [Fact,Trait("Category","Unit")]public async Task English(){
        var previous=CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo("en-US");
        try{
            {
                //英語環境では英語の文言(機関の天気文は原文のまま)
                Assert.Equal("No active warnings or advisories",Presentation.Resources.Strings.NoActiveAlerts);
                var h=new Harness();
                h.Weather.Location=Sample.Oslo;
                h.Weather.Forecast=new ForecastResult(Availability.Available,Sample.MetForecast());
                var vm=h.Place();
                await vm.RefreshAsync(TestContext.Current.CancellationToken);
                Assert.Equal("Alerts aren't available for this region",vm.AlertSummary);
                Assert.Contains("aggregated by the app",vm.Days[0].Source,StringComparison.Ordinal);
                Assert.Equal("Partly cloudy",Presentation.ConditionIcons.Describe(new WeatherCondition(SkyCover.PartlyCloudy)));
            }
            {
                //日本域の気象庁の天気文は翻訳しない(方針 5: 発表をそのまま表示)
                var h=new Harness();
                h.Weather.Forecast=new ForecastResult(Availability.Available,Sample.JmaForecast());
                var vm=h.Place();
                await vm.RefreshAsync(TestContext.Current.CancellationToken);
                Assert.Equal("くもり 昼過ぎ まで 時々 晴れ",vm.CurrentText);
                Assert.Equal("出典:気象庁ホームページ",vm.AttributionCredit);
            }
        }finally{
            CultureInfo.CurrentUICulture=previous;
        }
    }

    [Fact,Trait("Category","Unit")]public async Task AccessibleSummary(){
        //読み上げ用の要約(地点名・気温・天気)
        var h=new Harness();
        h.Weather.Forecast=new ForecastResult(Availability.Available,Sample.JmaForecast());
        var vm=h.Place();
        await vm.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal("テスト、21°、くもり 昼過ぎ まで 時々 晴れ",vm.AccessibleSummary);
    }

    private static string FindResources(){
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir is not null&&!File.Exists(Path.Combine(dir.FullName,"Weather.slnx"))){
            dir=dir.Parent;
        }
        Assert.NotNull(dir);
        return Path.Combine(dir.FullName,"src","Weather.Presentation","Resources");
    }

    private static List<string> Keys(string path){
        return [..XDocument.Load(path).Root!.Elements("data").Select(static e=>(string)e.Attribute("name")!)];
    }

    private static Dictionary<string,string> Values(string path){
        return XDocument.Load(path).Root!.Elements("data").ToDictionary(static e=>(string)e.Attribute("name")!,static e=>(string?)e.Element("value")??"",StringComparer.Ordinal);
    }

    private static int Placeholders(string value){
        var count=0;
        for(var i=0;i<10;i++){
            if(value.Contains("{"+i.ToString(CultureInfo.InvariantCulture)+"}",StringComparison.Ordinal)){
                count++;
            }
        }
        return count;
    }
}
