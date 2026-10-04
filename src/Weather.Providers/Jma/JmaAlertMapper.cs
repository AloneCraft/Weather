using System.Globalization;
using System.Text.Json;
using Weather.Core;
using Weather.Providers.Conditions;
using Weather.Providers.Http;

namespace Weather.Providers.Jma;

internal sealed record JmaAlertResult(AlertSet Alerts,IReadOnlyList<string> UnknownCodes,IReadOnlyList<string> UnknownStatuses);

/// <summary>気象庁の警報(r8)→ AlertSet。ファイルは電文種別ごとの最新の発表を並べた配列なので全要素を統合する。</summary>
internal static class JmaAlertMapper{
    public static JmaAlertResult Map(JsonElement root,JmaArea area,FetchResult fetch){
        var unknownCodes=new List<string>();
        var unknownStatuses=new List<string>();
        var page=JmaEndpoints.WarningPage(area.Class20Code);
        var reports=new List<JsonElement>();
        if(root.ValueKind==JsonValueKind.Array){
            reports.AddRange(root.EnumerateArray());
        }else{
            reports.Add(root);
        }
        //新しい発表を優先する
        reports.Sort(static (a,b)=>Nullable.Compare(b.Time("reportDatetime"),a.Time("reportDatetime")));

        var alerts=new Dictionary<string,Alert>(StringComparer.Ordinal);
        var headlines=new List<string>();
        SourceAttribution? latest=null;
        foreach(var report in reports){
            var source=JmaAttribution.Create("気象警報・注意報",report.Str("publishingOffice"),report.Time("reportDatetime"),fetch,page);
            latest??=source;
            var warning=report.Prop("warning");
            if(warning is null){
                continue;
            }
            var matched=false;
            foreach(var item in warning.Value.Items("class20Items")){
                if(item.Str("areaCode")!=area.Class20Code){
                    continue;
                }
                foreach(var kind in item.Items("kinds")){
                    var code=kind.Str("code");
                    if(code is null){
                        //「発表警報・注意報はなし」は code を持たない。この区域に関する発表ではないので見出しも採用しない
                        continue;
                    }
                    matched=true;
                    if(alerts.ContainsKey(code)){
                        continue;
                    }
                    var definition=JmaWarningCodes.Find(code);
                    if(definition is null){
                        unknownCodes.Add(code);
                        continue;
                    }
                    var status=MapStatus(kind.Str("status"),unknownStatuses);
                    if(status is null){
                        continue;
                    }
                    alerts[code]=new Alert($"{area.Class20Code}:{code}",definition.Name,definition.Tier,status.Value,source){
                        EventCode=code,
                        WarningLevel=definition.WarningLevel,
                        AreaName=area.Class20Name,
                        Headline=report.Str("headlineText"),
                    };
                }
            }
            if(matched&&report.Str("headlineText") is {Length:>0} headline&&!headlines.Contains(headline)){
                headlines.Add(headline);
            }
        }
        if(latest is null){
            latest=JmaAttribution.Create("気象警報・注意報",null,null,fetch,page);
        }
        var ordered=alerts.Values
            .OrderByDescending(static a=>a.Tier)
            .ThenBy(static a=>a.EventCode,StringComparer.Ordinal)
            .ToList();
        string? joined=null;
        if(headlines.Count>0){
            joined=string.Join("\n",headlines);
        }
        return new JmaAlertResult(new AlertSet(ordered,latest,joined),unknownCodes,unknownStatuses);
    }

    private static AlertStatus? MapStatus(string? status,List<string> unknown){
        switch(status){
            case "発表":
            case "継続":
                return AlertStatus.Active;
            case "解除":
                return AlertStatus.Cancelled;
            case null:
            case "発表警報・注意報はなし":
                return null;
            default:
                if(status.Contains("から注意報",StringComparison.Ordinal)){
                    return AlertStatus.Downgraded;
                }
                unknown.Add(status);
                return AlertStatus.Updated;
        }
    }
}

/// <summary>アメダス地点データ → 観測値。</summary>
internal static class JmaAmedasMapper{
    public static List<Observation> MapObservations(JsonElement root){
        var list=new List<Observation>();
        foreach(var entry in root.EnumerateObject()){
            if(!DateTime.TryParseExact(entry.Name,"yyyyMMddHHmmss",CultureInfo.InvariantCulture,DateTimeStyles.None,out var local)){
                continue;
            }
            var time=new DateTimeOffset(local,JmaAttribution.Jst);
            var v=entry.Value;
            Measurement? direction=null;
            if(Read(v,"windDirection") is {} dir&&Compass.FromAmedasCode((int)dir.Value) is {} degrees){
                direction=new Measurement(degrees,dir.Quality);
            }
            list.Add(new Observation(time){
                TemperatureC=Read(v,"temp"),
                HumidityPercent=Read(v,"humidity"),
                Precipitation10mMm=Read(v,"precipitation10m"),
                Precipitation1hMm=Read(v,"precipitation1h"),
                Precipitation24hMm=Read(v,"precipitation24h"),
                WindSpeedMs=Read(v,"wind"),
                WindDirectionDeg=direction,
                PressureHpa=Read(v,"pressure"),
                SeaLevelPressureHpa=Read(v,"normalPressure"),
                Sunshine1hHours=Read(v,"sun1h"),
                SnowDepthCm=Read(v,"snow"),
                VisibilityM=Read(v,"visibility"),
            });
        }
        list.Sort(static (a,b)=>a.ObservedAt.CompareTo(b.ObservedAt));
        return list;
    }

    /// <summary>気象庁が計算した日最高・最低(maxTemp / minTemp)を、その日の最後の値から取る。</summary>
    public static List<DailyObservationSummary> MapDailySummaries(JsonElement root,string stationKey){
        var byDate=new SortedDictionary<DateOnly,DailyObservationSummary>();
        foreach(var entry in root.EnumerateObject()){
            if(!DateTime.TryParseExact(entry.Name,"yyyyMMddHHmmss",CultureInfo.InvariantCulture,DateTimeStyles.None,out var local)){
                continue;
            }
            var date=DateOnly.FromDateTime(local);
            var v=entry.Value;
            var max=Read(v,"maxTemp");
            var min=Read(v,"minTemp");
            if(max is null&&min is null){
                continue;
            }
            byDate[date]=new DailyObservationSummary(stationKey,date){
                MaxTempC=max?.Value,
                MaxTempAt=ReadTime(v,"maxTempTime",date),
                MinTempC=min?.Value,
                MinTempAt=ReadTime(v,"minTempTime",date),
                PrecipitationMm=Read(v,"precipitation24h")?.Value,
                IsDerived=false,
            };
        }
        return [..byDate.Values];
    }

    /// <summary>[値, 品質フラグ]。0 = 正常、1 = 準正常(Suspect)、それ以外は欠測扱い(意味は要確認)。</summary>
    private static Measurement? Read(JsonElement v,string name){
        if(v.Prop(name) is not {ValueKind:JsonValueKind.Array} array){
            return null;
        }
        var items=array.EnumerateArray().ToList();
        if(items.Count==0||items[0].AsDouble() is not {} value){
            return null;
        }
        var flag=0d;
        if(items.Count>1&&items[1].AsDouble() is {} f){
            flag=f;
        }
        if(flag==0){
            return new Measurement(value);
        }
        if(flag==1){
            return new Measurement(value,MeasurementQuality.Suspect);
        }
        return null;
    }

    private static DateTimeOffset? ReadTime(JsonElement v,string name,DateOnly date){
        if(v.Prop(name) is {} t&&t.Num("hour") is {} hour&&t.Num("minute") is {} minute){
            var local=date.ToDateTime(TimeOnly.MinValue).AddHours(hour).AddMinutes(minute);
            return new DateTimeOffset(local,JmaAttribution.Jst);
        }
        return null;
    }
}
