using System.Text.Json;
using System.Xml;
using Weather.Core;
using Weather.Providers.Conditions;
using Weather.Providers.Http;

namespace Weather.Providers.Jma;

internal sealed record JmaDailyResult(IReadOnlyList<DailyForecast> Daily,string? AreaName,IReadOnlyList<string> UnknownCodes,bool WeeklyMatched=true);

/// <summary>気象庁の府県天気予報・時系列予報 → model(WeatherProviders.md「気象庁 Adapter」)。</summary>
internal static class JmaForecastMapper{
    private sealed class DayBuilder{
        public string? Code;
        public string? Weather;
        public string? Wind;
        public string? Wave;
        public double? Max;
        public double? Min;
        public ValueRange? MaxRange;
        public ValueRange? MinRange;
        public int? Pop;
        public ForecastReliability? Reliability;
        public string? TempPoint;
        public bool FromShortTerm;
        public bool TemperatureFromWeekly;
        public List<DayPart> Parts=[];
    }

    public static JmaDailyResult MapDaily(JsonElement root,JmaArea area,FetchResult fetch,DateOnly today){
        var blocks=root.EnumerateArray().ToList();
        if(blocks.Count==0){
            throw new InvalidOperationException("府県天気予報が空です。");
        }
        var unknown=new List<string>();
        var days=new SortedDictionary<DateOnly,DayBuilder>();
        var weeklyMatched=true;
        var page=JmaEndpoints.ForecastPage(area.OfficeCode);

        var shortBlock=blocks[0];
        var shortIssued=shortBlock.Time("reportDatetime");
        var shortSource=JmaAttribution.Create("府県天気予報",shortBlock.Str("publishingOffice"),shortIssued,fetch,page);
        var shortSeries=shortBlock.ItemList("timeSeries");
        string? areaName=null;

        //天気・天気文・風・波(class10 単位)
        if(shortSeries.Count>0){
            var series=shortSeries[0];
            var times=ReadTimes(series);
            var target=FindArea(series,area.Class10Code,false);
            if(target is {} t){
                areaName=t.Required("area").Str("name");
                var codes=t.ItemList("weatherCodes");
                var weathers=t.ItemList("weathers");
                var winds=t.ItemList("winds");
                var waves=t.ItemList("waves");
                for(var i=0;i<times.Count;i++){
                    var day=GetDay(days,JmaAttribution.JstDate(times[i]));
                    day.FromShortTerm=true;
                    day.Code=At(codes,i);
                    day.Weather=At(weathers,i);
                    day.Wind=At(winds,i);
                    day.Wave=At(waves,i);
                }
            }
        }
        //6 時間降水確率
        if(shortSeries.Count>1){
            var series=shortSeries[1];
            var times=ReadTimes(series);
            var target=FindArea(series,area.Class10Code,false);
            if(target is {} t){
                var pops=t.ItemList("pops");
                for(var i=0;i<times.Count;i++){
                    var day=GetDay(days,JmaAttribution.JstDate(times[i]));
                    day.Parts.Add(new DayPart(times[i],times[i].AddHours(6)){
                        PrecipitationProbability=ToPercent(AtNumber(pops,i)),
                    });
                }
            }
        }
        //気温(代表地点。気象庁サイトの表示ロジックを再現)
        if(shortSeries.Count>2&&shortIssued is {} issued){
            var series=shortSeries[2];
            var target=FindArea(series,area.TemperatureStationCode,true);
            if(target is {} t){
                var temps=t.ItemList("temps");
                var pointName=t.Required("area").Str("name");
                var reportDate=JmaAttribution.JstDate(issued);
                var tomorrow=GetDay(days,reportDate.AddDays(1));
                if(issued.ToOffset(JmaAttribution.Jst).Hour>=17){
                    tomorrow.Min=AtNumber(temps,0);
                    tomorrow.Max=AtNumber(temps,1);
                }else{
                    var todayBuilder=GetDay(days,reportDate);
                    todayBuilder.Max=AtNumber(temps,0);
                    todayBuilder.TempPoint=pointName;
                    tomorrow.Min=AtNumber(temps,2);
                    tomorrow.Max=AtNumber(temps,3);
                }
                tomorrow.TempPoint=pointName;
            }
        }

        //週間予報
        SourceAttribution? weeklySource=null;
        if(blocks.Count>1){
            var weekly=blocks[1];
            weeklySource=JmaAttribution.Create("府県週間天気予報",weekly.Str("publishingOffice"),weekly.Time("reportDatetime"),fetch,page);
            var weeklySeries=weekly.ItemList("timeSeries");
            if(weeklySeries.Count>0){
                var series=weeklySeries[0];
                var times=ReadTimes(series);
                //区域が一致しないときは先頭の区域で代用しない(伊豆諸島などは別の区域の値になるため)。週間予報は出さない
                var target=FindWeeklyArea(series,area);
                weeklyMatched&=target is not null;
                if(target is {} t){
                    var codes=t.ItemList("weatherCodes");
                    var pops=t.ItemList("pops");
                    var reliabilities=t.ItemList("reliabilities");
                    for(var i=0;i<times.Count;i++){
                        var day=GetDay(days,JmaAttribution.JstDate(times[i]));
                        if(day.FromShortTerm){
                            continue;
                        }
                        day.Code=At(codes,i);
                        day.Pop=ToPercent(AtNumber(pops,i));
                        day.Reliability=ParseReliability(At(reliabilities,i));
                    }
                }
            }
            if(weeklySeries.Count>1){
                var series=weeklySeries[1];
                var times=ReadTimes(series);
                var target=FindArea(series,area.TemperatureStationCode,false);
                weeklyMatched&=target is not null;
                if(target is {} t){
                    var pointName=t.Required("area").Str("name");
                    var min=t.ItemList("tempsMin");
                    var minUpper=t.ItemList("tempsMinUpper");
                    var minLower=t.ItemList("tempsMinLower");
                    var max=t.ItemList("tempsMax");
                    var maxUpper=t.ItemList("tempsMaxUpper");
                    var maxLower=t.ItemList("tempsMaxLower");
                    for(var i=0;i<times.Count;i++){
                        var day=GetDay(days,JmaAttribution.JstDate(times[i]));
                        if(day.Max is not null||day.Min is not null){
                            continue;
                        }
                        var weeklyMax=AtNumber(max,i);
                        var weeklyMin=AtNumber(min,i);
                        if(weeklyMax is null&&weeklyMin is null){
                            continue;
                        }
                        day.Max=weeklyMax;
                        day.Min=weeklyMin;
                        day.MaxRange=Range(AtNumber(maxLower,i),AtNumber(maxUpper,i));
                        day.MinRange=Range(AtNumber(minLower,i),AtNumber(minUpper,i));
                        day.TempPoint=pointName;
                        day.TemperatureFromWeekly=true;
                    }
                }
            }
        }

        var result=new List<DailyForecast>();
        foreach(var (date,day) in days){
            if(date<today){
                continue;
            }
            var source=shortSource;
            if(!day.FromShortTerm){
                if(weeklySource is null||day.Code is null){
                    continue;
                }
                source=weeklySource;
            }
            CompositeCondition? condition=null;
            string? weatherText=day.Weather;
            if(day.Code is not null){
                var code=JmaWeatherCodes.Find(day.Code);
                if(code is null){
                    unknown.Add(day.Code);
                }else{
                    condition=code.Condition;
                    weatherText??=code.Name;
                }
            }
            SourceAttribution? temperatureSource=null;
            if(day.TemperatureFromWeekly&&day.FromShortTerm){
                temperatureSource=weeklySource;
            }
            result.Add(new DailyForecast(date,source){
                Condition=condition,
                SourceCode=day.Code,
                WeatherText=weatherText,
                WindText=day.Wind,
                WaveText=day.Wave,
                TempMaxC=day.Max,
                TempMinC=day.Min,
                TempMaxRangeC=day.MaxRange,
                TempMinRangeC=day.MinRange,
                TemperaturePointName=day.TempPoint,
                TemperatureSource=temperatureSource,
                PrecipitationProbability=day.Pop,
                Parts=day.Parts,
                Reliability=day.Reliability,
            });
        }
        return new JmaDailyResult(result,areaName,unknown,weeklyMatched);
    }

    /// <summary>時系列予報(3 時間区間)。</summary>
    public static IReadOnlyList<ForecastPoint> MapTimeSeries(JsonElement root,JmaArea area,FetchResult fetch,List<string> unknownWords){
        var source=JmaAttribution.Create("時系列予報",root.Str("publishingOffice"),root.Time("reportDateTime"),fetch,JmaEndpoints.ForecastPage(area.OfficeCode));
        var areaSeries=root.Required("areaTimeSeries");
        var temperatures=new Dictionary<DateTimeOffset,double>();
        if(root.Prop("pointTimeSeries") is {} pointSeries){
            var pointTimes=pointSeries.ItemList("timeDefines");
            var values=pointSeries.ItemList("temperature");
            for(var i=0;i<pointTimes.Count&&i<values.Count;i++){
                if(pointTimes[i].Time("dateTime") is {} time&&values[i].AsDouble() is {} value){
                    temperatures[time]=value;
                }
            }
        }
        var timeDefines=areaSeries.ItemList("timeDefines");
        var weathers=areaSeries.ItemList("weather");
        var winds=areaSeries.ItemList("wind");
        var points=new List<ForecastPoint>();
        DateTimeOffset? lastEnd=null;
        for(var i=0;i<timeDefines.Count;i++){
            var start=timeDefines[i].Time("dateTime")??throw new FormatException("dateTime がありません。");
            var duration=TimeSpan.FromHours(3);
            if(timeDefines[i].Str("duration") is {} text){
                duration=XmlConvert.ToTimeSpan(text);
            }
            if(lastEnd is {} le&&start<le){
                //重複・逆順・重なり。補助プロダクトの異常で予報全体を失わないよう、重なる点は飛ばす(NWS の hourly と同じ)
                continue;
            }
            var word=At(weathers,i);
            var condition=JmaWeatherCodes.FromTimeSeriesWord(word);
            if(word is not null&&condition is null){
                unknownWords.Add(word);
            }
            double? direction=null;
            ValueRange? speedRange=null;
            if(i<winds.Count){
                direction=Compass.FromJapanese(winds[i].Str("direction"));
                speedRange=ParseRange(winds[i].Str("range"));
            }
            double? temperature=null;
            if(temperatures.TryGetValue(start,out var t)){
                temperature=t;
            }
            points.Add(new ForecastPoint(start,start+duration,source){
                Condition=condition,
                WeatherText=word,
                TemperatureC=temperature,
                WindDirectionDeg=direction,
                WindSpeedRangeMs=speedRange,
            });
            lastEnd=start+duration;
        }
        return points;
    }

    private static DayBuilder GetDay(SortedDictionary<DateOnly,DayBuilder> days,DateOnly date){
        if(!days.TryGetValue(date,out var day)){
            day=new DayBuilder();
            days[date]=day;
        }
        return day;
    }

    private static List<DateTimeOffset> ReadTimes(JsonElement series){
        return [..series.Items("timeDefines").Select(static e=>DateTimeOffset.Parse(e.GetString()!,System.Globalization.CultureInfo.InvariantCulture))];
    }

    /// <summary>
    /// 週間予報の区域。府県の区域(class10)が一致すればそれを使う。無いときは、気象台の区域(office の code)が
    /// 1 件だけ入っている場合に限りそれを使う(札幌は 016000「石狩・空知・後志地方」の 1 件のみ)。
    /// 伊豆諸島などは別の区域が並ぶため、この規則には当たらず、一致しないままになる。
    /// </summary>
    private static JsonElement? FindWeeklyArea(JsonElement series,JmaArea area){
        if(FindArea(series,area.Class10Code,false) is {} byClass10){
            return byClass10;
        }
        var areas=series.Items("areas").ToList();
        if(areas.Count==1&&areas[0].Prop("area")?.Str("code")==area.ForecastOfficeCode){
            return areas[0];
        }
        return null;
    }

    /// <summary>area.code が一致する要素。fallbackToFirst なら一致しないとき先頭を返す(週間予報の区域対応)。</summary>
    private static JsonElement? FindArea(JsonElement series,string? code,bool fallbackToFirst){
        JsonElement? first=null;
        foreach(var item in series.Items("areas")){
            first??=item;
            if(code is not null&&item.Prop("area")?.Str("code")==code){
                return item;
            }
        }
        if(fallbackToFirst){
            return first;
        }
        return null;
    }

    private static string? At(IReadOnlyList<JsonElement> items,int index){
        if(index<items.Count){
            var text=items[index].AsString();
            if(!string.IsNullOrWhiteSpace(text)){
                return text;
            }
        }
        return null;
    }

    private static double? AtNumber(IReadOnlyList<JsonElement> items,int index){
        if(index<items.Count){
            return items[index].AsDouble();
        }
        return null;
    }

    private static int? ToPercent(double? value){
        if(value is {} v){
            return (int)Math.Clamp(Math.Round(v),0,100);
        }
        return null;
    }

    private static ValueRange? Range(double? lower,double? upper){
        if(lower is {} l&&upper is {} u&&l<=u){
            return new ValueRange(l,u);
        }
        return null;
    }

    private static ValueRange? ParseRange(string? text){
        if(string.IsNullOrWhiteSpace(text)){
            return null;
        }
        var parts=text.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length==2
            &&double.TryParse(parts[0],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var lower)
            &&double.TryParse(parts[1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var upper)){
            return Range(lower,upper);
        }
        return null;
    }

    private static ForecastReliability? ParseReliability(string? text){
        switch(text){
            case "A":
                return ForecastReliability.A;
            case "B":
                return ForecastReliability.B;
            case "C":
                return ForecastReliability.C;
            default:
                return null;
        }
    }
}
