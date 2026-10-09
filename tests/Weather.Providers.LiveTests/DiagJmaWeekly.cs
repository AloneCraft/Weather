using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace Weather.Providers.LiveTests;

//一時的な診断(札幌の週間予報の区域対応を実データで確かめる。確認後に revert する)
public sealed class DiagJmaWeekly{
    [Fact(Explicit=true),Trait("Category","Live")]
    public async Task Diag_SapporoWeeklyAreas(){
        using var http=new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WeatherAppLiveTests/0.1 github.com/AloneCraft/Weather");
        var text=await http.GetStringAsync("https://www.jma.go.jp/bosai/forecast/data/forecast/016000.json",TestContext.Current.CancellationToken);
        var root=JsonNode.Parse(text)!.AsArray();
        var sb=new StringBuilder();
        for(var b=0;b<root.Count;b++){
            sb.Append($"<block{b}>");
            var series=root[b]!["timeSeries"]!.AsArray();
            for(var i=0;i<series.Count;i++){
                sb.Append($"[ts{i}]");
                foreach(var a in series[i]!["areas"]!.AsArray().Take(40)){
                    sb.Append($"{a!["area"]?["code"]}:{a["area"]?["name"]} ");
                }
            }
            sb.Append("</block>");
        }
        Assert.Fail("DIAG_JMA_AREAS "+sb);
    }
}
