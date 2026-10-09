using System.Globalization;
using System.Text.Json;
using System.Xml;
using Weather.Core;
using Weather.Providers.Http;

namespace Weather.Providers.Nws;

internal sealed record NwsPoint(Uri Forecast,Uri ForecastHourly,Uri ForecastGridData,Uri ObservationStations,string? TimeZone,string? City,string? State,string? GridId);

internal static class NwsAttribution{
    public static readonly LicenseInfo License=new("米国政府のオープンデータ(National Weather Service)",new Uri("https://www.weather.gov/disclaimer"));

    public static SourceAttribution Create(string product,string? office,DateTimeOffset? issuedAt,FetchResult fetch,GeoPoint point,DataProcessing processing){
        return new SourceAttribution{
            Provider=ProviderId.Nws,
            AgencyName="National Weather Service",
            ProductName=product,
            PublishingOffice=office,
            IssuedAt=issuedAt,
            RetrievedAt=fetch.RetrievedAt,
            License=License,
            SourceUrl=new Uri(string.Create(CultureInfo.InvariantCulture,$"https://forecast.weather.gov/MapClick.php?lat={point.Latitude:0.##}&lon={point.Longitude:0.##}")),
            Processing=processing,
            IsStale=fetch.IsStale,
        };
    }
}

/// <summary>NWS API → model(WeatherProviders.md「NWS Adapter」)。</summary>
internal static class NwsMapper{
    public static NwsPoint MapPoint(JsonElement root){
        var p=root.Required("properties");
        var relative=p.Prop("relativeLocation")?.Prop("properties");
        return new NwsPoint(
            new Uri(p.Str("forecast")??throw new KeyNotFoundException("forecast")),
            new Uri(p.Str("forecastHourly")??throw new KeyNotFoundException("forecastHourly")),
            new Uri(p.Str("forecastGridData")??throw new KeyNotFoundException("forecastGridData")),
            new Uri(p.Str("observationStations")??throw new KeyNotFoundException("observationStations")),
            p.Str("timeZone"),
            relative?.Str("city"),
            relative?.Str("state"),
            p.Str("gridId"));
    }

    /// <summary>12 時間 periods → 日別(昼と夜を現地日付でまとめる)。</summary>
    public static List<DailyForecast> MapDaily(JsonElement root,FetchResult fetch,NwsPoint point,GeoPoint location,List<string> unknownPhrases){
        var p=root.Required("properties");
        var source=NwsAttribution.Create("Forecast",point.GridId,p.Time("updateTime")??p.Time("generatedAt"),fetch,location,DataProcessing.None);
        var groups=new SortedDictionary<DateOnly,(JsonElement? Day,JsonElement? Night,JsonElement? Early)>();
        foreach(var period in p.Items("periods")){
            var start=period.Time("startTime")??throw new FormatException("startTime");
            var date=DateOnly.FromDateTime(start.DateTime);
            groups.TryGetValue(date,out var g);
            if(period.Bool("isDaytime")==true){
                g.Day??=period;
            }else if(start.Hour<12){
                //深夜〜早朝に取得したとき先頭に来る Overnight(同じ日の 02:00〜06:00 など)。夕方からの夜(Tonight など)とは別に持つ
                g.Early??=period;
            }else{
                g.Night??=period;
            }
            groups[date]=g;
        }
        var result=new List<DailyForecast>();
        foreach(var (date,g) in groups){
            var parts=new List<DayPart>();
            CompositeCondition? condition=null;
            string? text=null;
            string? code=null;
            double? max=null;
            double? min=null;
            double? earlyMin=null;
            foreach(var period in new[]{g.Day,g.Night,g.Early}){
                if(period is not {} pe){
                    continue;
                }
                var shortForecast=pe.Str("shortForecast");
                var parsed=NwsForecastPhraseParser.Parse(shortForecast);
                if(parsed is null&&shortForecast is not null){
                    unknownPhrases.Add(shortForecast);
                }
                var temperature=ReadTemperature(pe);
                parts.Add(new DayPart(pe.Time("startTime")!.Value,pe.Time("endTime")!.Value){
                    Label=pe.Str("name"),
                    Condition=parsed,
                    WeatherText=shortForecast,
                    DetailText=pe.Str("detailedForecast"),
                    TemperatureC=temperature,
                    PrecipitationProbability=ToPercent(pe.Quantity("probabilityOfPrecipitation").Value),
                });
                condition??=parsed;
                text??=shortForecast;
                code??=shortForecast;
                if(pe.Bool("isDaytime")==true){
                    max=temperature;
                }else if(g.Early is {} early&&early.Equals(pe)){
                    earlyMin=temperature;
                }else{
                    min=temperature;
                }
            }
            //夕方からの夜がなければ(予報の末尾など)、早朝の気温を最低気温に使う。Parts は時刻順にする
            min??=earlyMin;
            parts.Sort(static (a,b)=>a.Start.CompareTo(b.Start));
            result.Add(new DailyForecast(date,source){
                Condition=condition,
                SourceCode=code,
                WeatherText=text,
                TempMaxC=max,
                TempMinC=min,
                Parts=parts,
            });
        }
        return result;
    }

    /// <summary>1 時間 periods → 時系列。風速は文字列を解析して m/s へ換算する。</summary>
    public static List<ForecastPoint> MapHourly(JsonElement root,FetchResult fetch,NwsPoint point,GeoPoint location,List<string> unknownPhrases){
        var p=root.Required("properties");
        var source=NwsAttribution.Create("Hourly Forecast",point.GridId,p.Time("updateTime")??p.Time("generatedAt"),fetch,location,DataProcessing.UnitConverted);
        var list=new List<ForecastPoint>();
        DateTimeOffset? lastEnd=null;
        foreach(var period in p.Items("periods")){
            var start=period.Time("startTime")??throw new FormatException("startTime");
            var end=period.Time("endTime")??start.AddHours(1);
            if(lastEnd is {} le&&start<le){
                continue;
            }
            var shortForecast=period.Str("shortForecast");
            var parsed=NwsForecastPhraseParser.Parse(shortForecast);
            if(parsed is null&&shortForecast is not null){
                unknownPhrases.Add(shortForecast);
            }
            double? speed=null;
            ValueRange? range=null;
            if(NwsUnits.ParseWindSpeed(period.Str("windSpeed")) is {} wind){
                speed=(wind.Low+wind.High)/2;
                if(wind.High>wind.Low){
                    range=new ValueRange(wind.Low,wind.High);
                }
            }
            list.Add(new ForecastPoint(start,end,source){
                Condition=parsed,
                WeatherText=shortForecast,
                SourceCode=shortForecast,
                TemperatureC=ReadTemperature(period),
                PrecipitationProbability=ToPercent(period.Quantity("probabilityOfPrecipitation").Value),
                HumidityPercent=ToPercent(period.Quantity("relativeHumidity").Value),
                WindSpeedMs=speed,
                WindSpeedRangeMs=range,
                WindDirectionDeg=Conditions.Compass.FromEnglish(period.Str("windDirection")),
            });
            lastEnd=end;
        }
        return list;
    }

    /// <summary>グリッドデータで時系列を補う(雲量・雷確率・降水量・視程・突風)。</summary>
    public static List<ForecastPoint> EnrichWithGrid(IReadOnlyList<ForecastPoint> points,JsonElement root,List<string> unknownWeather){
        var p=root.Required("properties");
        var sky=ReadLayer(p,"skyCover");
        var thunder=ReadLayer(p,"probabilityOfThunder");
        var precipitation=ReadLayer(p,"quantitativePrecipitation");
        var visibility=ReadLayer(p,"visibility");
        var gust=ReadLayer(p,"windGust");
        var weather=ReadWeatherLayer(p);
        var result=new List<ForecastPoint>(points.Count);
        foreach(var point in points){
            var cloud=Find(sky,point.Start);
            var precip=Find(precipitation,point.Start);
            double? hourly=null;
            var processing=point.Source.Processing;
            if(precip is {} pr&&pr.Value is {} amount){
                //区間の合計を時間比で配分する(アプリ側の配分のため Aggregated)
                hourly=amount*point.Duration.TotalHours/pr.Duration.TotalHours;
                if(pr.Duration!=point.Duration){
                    processing|=DataProcessing.Aggregated;
                }
            }
            var condition=point.Condition;
            if(condition is null&&Find(weather,point.Start) is {} w){
                condition=NwsGridWeather.FromValues(w.Values,Conditions.SkyCoverClassifier.FromPercent(cloud?.Value),unknownWeather);
            }
            var source=point.Source;
            if(processing!=source.Processing){
                source=source with{Processing=processing};
            }
            result.Add(new ForecastPoint(point.Start,point.End,source){
                Condition=condition,
                WeatherText=point.WeatherText,
                SourceCode=point.SourceCode,
                TemperatureC=point.TemperatureC,
                PrecipitationProbability=point.PrecipitationProbability,
                HumidityPercent=point.HumidityPercent,
                WindSpeedMs=point.WindSpeedMs,
                WindSpeedRangeMs=point.WindSpeedRangeMs,
                WindDirectionDeg=point.WindDirectionDeg,
                CloudCoverPercent=ToPercent(cloud?.Value),
                ThunderProbability=ToPercent(Find(thunder,point.Start)?.Value),
                PrecipitationMm=hourly,
                VisibilityM=NonNegative(Find(visibility,point.Start)?.Value),
                WindGustMs=NonNegative(Find(gust,point.Start)?.Value),
            });
        }
        return result;
    }

    public static AlertSet MapAlerts(JsonElement root,FetchResult fetch,GeoPoint location){
        var alerts=new List<Alert>();
        DateTimeOffset? newest=null;
        foreach(var feature in root.Items("features")){
            var p=feature.Required("properties");
            var eventName=p.Str("event")??"Alert";
            var sent=p.Time("sent");
            if(sent is {} s&&(newest is null||s>newest)){
                newest=s;
            }
            var source=NwsAttribution.Create("Alerts",p.Str("senderName"),sent,fetch,location,DataProcessing.None);
            string? eventCode=null;
            if(p.Prop("eventCode")?.Prop("NationalWeatherService") is {ValueKind:JsonValueKind.Array} codes){
                foreach(var c in codes.EnumerateArray()){
                    eventCode=c.GetString();
                    break;
                }
            }
            alerts.Add(new Alert(p.Str("id")??feature.Str("id")??Guid.NewGuid().ToString(),eventName,MapTier(eventName),MapStatus(p.Str("messageType")),source){
                EventCode=eventCode,
                Onset=p.Time("onset")??p.Time("effective"),
                Expires=p.Time("ends")??p.Time("expires"),
                AreaName=p.Str("areaDesc"),
                Headline=p.Str("headline"),
                Description=p.Str("description"),
                Instruction=p.Str("instruction"),
                SourceSeverity=p.Str("severity"),
            });
        }
        var setSource=NwsAttribution.Create("Alerts",null,root.Time("updated")??newest,fetch,location,DataProcessing.None);
        return new AlertSet([..alerts.OrderByDescending(static a=>a.Tier)],setSource);
    }

    public static AlertTier MapTier(string eventName){
        if(eventName.EndsWith("Emergency",StringComparison.OrdinalIgnoreCase)){
            return AlertTier.Emergency;
        }
        if(eventName.EndsWith("Warning",StringComparison.OrdinalIgnoreCase)){
            return AlertTier.Warning;
        }
        if(eventName.EndsWith("Watch",StringComparison.OrdinalIgnoreCase)){
            return AlertTier.Watch;
        }
        if(eventName.EndsWith("Advisory",StringComparison.OrdinalIgnoreCase)){
            return AlertTier.Advisory;
        }
        return AlertTier.Information;
    }

    public static AlertStatus MapStatus(string? messageType){
        switch(messageType){
            case "Update":
                return AlertStatus.Updated;
            case "Cancel":
                return AlertStatus.Cancelled;
            default:
                return AlertStatus.Active;
        }
    }

    public static List<ObservationStation> MapStations(JsonElement root){
        var list=new List<ObservationStation>();
        foreach(var feature in root.Items("features")){
            var p=feature.Required("properties");
            var id=p.Str("stationIdentifier");
            var coordinates=feature.Prop("geometry")?.ItemList("coordinates");
            if(id is null||coordinates is not {Count:>=2}){
                continue;
            }
            var lon=coordinates[0].AsDouble()??0;
            var lat=coordinates[1].AsDouble()??0;
            list.Add(new ObservationStation(id,ProviderId.Nws,p.Str("name")??id,new GeoPoint(lat,lon),p.Quantity("elevation").Value));
        }
        return list;
    }

    public static List<Observation> MapObservations(JsonElement root,List<string> unknownPhrases){
        var byTime=new SortedDictionary<DateTimeOffset,Observation>();
        foreach(var feature in root.Items("features")){
            var p=feature.Required("properties");
            if(p.Time("timestamp") is not {} time){
                continue;
            }
            var text=p.Str("textDescription");
            CompositeCondition? condition=null;
            if(!string.IsNullOrWhiteSpace(text)){
                condition=NwsForecastPhraseParser.Parse(text);
                if(condition is null){
                    unknownPhrases.Add(text);
                }
            }
            Measurement? direction=null;
            if(Read(p,"windDirection") is {} d){
                direction=new Measurement(GeoMath.NormalizeDegrees(d.Value),d.Quality);
            }
            byTime[time]=new Observation(time){
                TemperatureC=Read(p,"temperature"),
                HumidityPercent=Read(p,"relativeHumidity"),
                WindSpeedMs=Read(p,"windSpeed"),
                WindDirectionDeg=direction,
                WindGustMs=Read(p,"windGust"),
                PressureHpa=Read(p,"barometricPressure"),
                SeaLevelPressureHpa=Read(p,"seaLevelPressure"),
                VisibilityM=Read(p,"visibility"),
                Precipitation1hMm=Read(p,"precipitationLastHour"),
                Condition=condition,
                WeatherText=text,
            };
        }
        return [..byTime.Values];
    }

    /// <summary>X = 不合格 → null、Q = 要注意 → Suspect、それ以外は Normal(意味は要確認)。</summary>
    private static Measurement? Read(JsonElement p,string name){
        var (value,unit,quality)=p.Quantity(name);
        if(NwsUnits.ToSi(value,unit) is not {} si||!double.IsFinite(si)){
            return null;
        }
        if(quality=="X"){
            return null;
        }
        if(quality=="Q"){
            return new Measurement(si,MeasurementQuality.Suspect);
        }
        return new Measurement(si);
    }

    private static double? ReadTemperature(JsonElement period){
        var value=period.Num("temperature");
        if(value is null&&period.Prop("temperature") is {ValueKind:JsonValueKind.Object}){
            var q=period.Quantity("temperature");
            return NwsUnits.ToSi(q.Value,q.Unit);
        }
        if(value is {} v&&period.Str("temperatureUnit")=="F"){
            return (v-32)*5/9;
        }
        return value;
    }

    private static int? ToPercent(double? value){
        if(value is {} v&&double.IsFinite(v)){
            return (int)Math.Clamp(Math.Round(v),0,100);
        }
        return null;
    }

    private static double? NonNegative(double? value){
        if(value is {} v&&double.IsFinite(v)&&v>=0){
            return v;
        }
        return null;
    }

    private readonly record struct LayerValue(DateTimeOffset Start,TimeSpan Duration,double? Value);
    private readonly record struct WeatherValue(DateTimeOffset Start,TimeSpan Duration,JsonElement Values);

    private static List<LayerValue> ReadLayer(JsonElement properties,string name){
        var list=new List<LayerValue>();
        if(properties.Prop(name) is not {} layer){
            return list;
        }
        var unit=layer.Str("uom");
        foreach(var item in layer.Items("values")){
            if(ParseInterval(item.Str("validTime")) is {} interval){
                list.Add(new LayerValue(interval.Start,interval.Duration,NwsUnits.ToSi(item.Num("value"),unit)));
            }
        }
        return list;
    }

    private static List<WeatherValue> ReadWeatherLayer(JsonElement properties){
        var list=new List<WeatherValue>();
        if(properties.Prop("weather") is not {} layer){
            return list;
        }
        foreach(var item in layer.Items("values")){
            if(ParseInterval(item.Str("validTime")) is {} interval&&item.Prop("value") is {ValueKind:JsonValueKind.Array} values){
                list.Add(new WeatherValue(interval.Start,interval.Duration,values.Clone()));
            }
        }
        return list;
    }

    private static LayerValue? Find(List<LayerValue> layer,DateTimeOffset time){
        foreach(var value in layer){
            if(value.Start<=time&&time<value.Start+value.Duration){
                return value;
            }
        }
        return null;
    }

    private static WeatherValue? Find(List<WeatherValue> layer,DateTimeOffset time){
        foreach(var value in layer){
            if(value.Start<=time&&time<value.Start+value.Duration){
                return value;
            }
        }
        return null;
    }

    /// <summary>"2026-10-03T14:00:00+00:00/PT1H"。</summary>
    public static (DateTimeOffset Start,TimeSpan Duration)? ParseInterval(string? text){
        if(text is null){
            return null;
        }
        var slash=text.IndexOf('/',StringComparison.Ordinal);
        if(slash<0){
            return null;
        }
        if(!DateTimeOffset.TryParse(text[..slash],CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var start)){
            return null;
        }
        try{
            return (start,XmlConvert.ToTimeSpan(text[(slash+1)..]));
        }catch(FormatException){
            return null;
        }
    }
}
