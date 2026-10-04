using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Weather.Core;
using Weather.Providers.Http;

namespace Weather.Providers.MetNorway;

/// <summary>MET Norway のシンボル → WeatherCondition(WeatherProviders.md 付録 D)。</summary>
public static class MetSymbolParser{
    public static WeatherCondition? Parse(string? symbolCode){
        if(string.IsNullOrWhiteSpace(symbolCode)){
            return null;
        }
        var s=symbolCode.Trim();
        foreach(var suffix in new[]{"_day","_night","_polartwilight"}){
            if(s.EndsWith(suffix,StringComparison.Ordinal)){
                s=s[..^suffix.Length];
                break;
            }
        }
        switch(s){
            case "clearsky":
                return new WeatherCondition(SkyCover.Clear);
            case "fair":
                return new WeatherCondition(SkyCover.MostlyClear);
            case "partlycloudy":
                return new WeatherCondition(SkyCover.PartlyCloudy);
            case "cloudy":
                return new WeatherCondition(SkyCover.Overcast);
            case "fog":
                return new WeatherCondition(SkyCover.Overcast,obscuration:Obscuration.Fog);
        }
        var intensity=PrecipitationIntensity.Moderate;
        //誤綴りのまま運用されている lightssleet… / lightssnow… も受け付ける
        if(s.StartsWith("lightssleet",StringComparison.Ordinal)||s.StartsWith("lightssnow",StringComparison.Ordinal)){
            intensity=PrecipitationIntensity.Light;
            s=s["lights".Length..];
        }else if(s.StartsWith("light",StringComparison.Ordinal)){
            intensity=PrecipitationIntensity.Light;
            s=s["light".Length..];
        }else if(s.StartsWith("heavy",StringComparison.Ordinal)){
            intensity=PrecipitationIntensity.Heavy;
            s=s["heavy".Length..];
        }
        var thunder=s.EndsWith("andthunder",StringComparison.Ordinal);
        if(thunder){
            s=s[..^"andthunder".Length];
        }
        var showers=s.EndsWith("showers",StringComparison.Ordinal);
        if(showers){
            s=s[..^"showers".Length];
        }
        PrecipitationType type;
        switch(s){
            case "rain":
                type=PrecipitationType.Rain;
                break;
            case "sleet":
                type=PrecipitationType.RainAndSnow;
                break;
            case "snow":
                type=PrecipitationType.Snow;
                break;
            default:
                return null;
        }
        var sky=SkyCover.Overcast;
        if(showers){
            sky=SkyCover.PartlyCloudy;
        }
        return new WeatherCondition(sky,type,intensity,showers,thunder);
    }

    /// <summary>日別の代表天気を選ぶための顕著さ(雷 > 強い降水 > 降水 > 霧 > 曇り)。</summary>
    public static int Significance(WeatherCondition c){
        var score=(int)c.Sky;
        if(c.Obscuration==Obscuration.Fog){
            score=10;
        }
        if(c.HasPrecipitation){
            score=20+(int)c.Intensity;
        }
        if(c.HasThunder){
            score=40+(int)c.Intensity;
        }
        return score;
    }
}

/// <summary>MET Norway Locationforecast 2.0 complete → model(WeatherProviders.md「MET Norway Adapter」)。</summary>
internal static class MetMapper{
    public static readonly LicenseInfo License=new("CC BY 4.0 / NLOD 2.0(MET Norway)",new Uri("https://api.met.no/doc/License"));

    public static SourceAttribution CreateSource(DateTimeOffset? updated,FetchResult fetch,DataProcessing processing){
        return new SourceAttribution{
            Provider=ProviderId.MetNorway,
            AgencyName="MET Norway",
            ProductName="Locationforecast 2.0",
            IssuedAt=updated,
            RetrievedAt=fetch.RetrievedAt,
            License=License,
            SourceUrl=new Uri("https://api.met.no/weatherapi/locationforecast/2.0/documentation"),
            Processing=processing,
            IsStale=fetch.IsStale,
        };
    }

    private readonly record struct Entry(DateTimeOffset Time,JsonElement Data);

    public static (List<ForecastPoint> TimeSeries,List<DailyForecast> Daily) Map(JsonElement root,FetchResult fetch,TimeZoneInfo zone,List<string> unknownSymbols){
        var p=root.Required("properties");
        var updated=p.Prop("meta")?.Time("updated_at");
        var seriesSource=CreateSource(updated,fetch,DataProcessing.None);
        var dailySource=CreateSource(updated,fetch,DataProcessing.Aggregated);
        var entries=new List<Entry>();
        foreach(var item in p.Items("timeseries")){
            if(item.Time("time") is {} time&&item.Prop("data") is {} data){
                entries.Add(new Entry(time,data));
            }
        }
        entries.Sort(static (a,b)=>a.Time.CompareTo(b.Time));

        var points=new List<ForecastPoint>();
        var previousStep=TimeSpan.FromHours(1);
        for(var i=0;i<entries.Count;i++){
            var entry=entries[i];
            var step=previousStep;
            if(i+1<entries.Count){
                step=entries[i+1].Time-entry.Time;
            }
            if(step<=TimeSpan.Zero){
                continue;
            }
            //次の要素までの間隔に合う予報を選ぶ(区間が重ならず隙間なく連続する)
            JsonElement? period=null;
            if(step<=TimeSpan.FromHours(1)){
                period=entry.Data.Prop("next_1_hours");
            }else if(step<=TimeSpan.FromHours(6)){
                period=entry.Data.Prop("next_6_hours");
            }else{
                period=entry.Data.Prop("next_12_hours");
            }
            var instant=entry.Data.Prop("instant")?.Prop("details");
            string? symbol=period?.Prop("summary")?.Str("symbol_code");
            var condition=MetSymbolParser.Parse(symbol);
            if(symbol is not null&&condition is null){
                unknownSymbols.Add(symbol);
            }
            var details=period?.Prop("details");
            points.Add(new ForecastPoint(entry.Time,entry.Time+step,seriesSource){
                Condition=ToComposite(condition),
                SourceCode=symbol,
                TemperatureC=instant?.Num("air_temperature"),
                HumidityPercent=Percent(instant?.Num("relative_humidity")),
                CloudCoverPercent=Percent(instant?.Num("cloud_area_fraction")),
                FogPercent=Percent(instant?.Num("fog_area_fraction")),
                WindSpeedMs=NonNegative(instant?.Num("wind_speed")),
                WindGustMs=NonNegative(instant?.Num("wind_speed_of_gust")),
                WindDirectionDeg=Direction(instant?.Num("wind_from_direction")),
                PressureHpa=NonNegative(instant?.Num("air_pressure_at_sea_level")),
                PrecipitationMm=NonNegative(details?.Num("precipitation_amount")),
                PrecipitationProbability=Percent(details?.Num("probability_of_precipitation")),
                ThunderProbability=Percent(details?.Num("probability_of_thunder")),
            });
            previousStep=step;
        }

        return (points,MapDaily(entries,points,zone,dailySource));
    }

    /// <summary>現地日付で集計する(アプリ側集計)。6 時間ブロックは UTC 0/6/12/18 時始まりのものを使う。</summary>
    private static List<DailyForecast> MapDaily(List<Entry> entries,List<ForecastPoint> points,TimeZoneInfo zone,SourceAttribution source){
        var blocks=new SortedDictionary<DateOnly,List<(DateTimeOffset Start,JsonElement Block)>>();
        foreach(var entry in entries){
            if(entry.Time.UtcDateTime.Hour%6!=0){
                continue;
            }
            if(entry.Data.Prop("next_6_hours") is not {} block){
                continue;
            }
            var local=TimeZoneInfo.ConvertTime(entry.Time,zone);
            var date=DateOnly.FromDateTime(local.DateTime);
            if(!blocks.TryGetValue(date,out var list)){
                list=[];
                blocks[date]=list;
            }
            list.Add((entry.Time,block));
        }
        var result=new List<DailyForecast>();
        foreach(var (date,list) in blocks){
            double? max=null;
            double? min=null;
            double precipitation=0;
            var hasPrecipitation=false;
            int? pop=null;
            WeatherCondition? best=null;
            var bestScore=-1;
            var parts=new List<DayPart>();
            foreach(var (start,block) in list){
                var details=block.Prop("details");
                max=Max(max,details?.Num("air_temperature_max"));
                min=Min(min,details?.Num("air_temperature_min"));
                if(details?.Num("precipitation_amount") is {} amount&&amount>=0){
                    precipitation+=amount;
                    hasPrecipitation=true;
                }
                var blockPop=Percent(details?.Num("probability_of_precipitation"));
                if(blockPop is {} bp&&(pop is null||bp>pop)){
                    pop=bp;
                }
                var symbol=block.Prop("summary")?.Str("symbol_code");
                var condition=MetSymbolParser.Parse(symbol);
                var localHour=TimeZoneInfo.ConvertTime(start,zone).Hour;
                var daytime=localHour>=6&&localHour<18;
                if(condition is {} c){
                    var score=MetSymbolParser.Significance(c);
                    if(daytime){
                        score+=100;
                    }
                    if(score>bestScore){
                        bestScore=score;
                        best=c;
                    }
                }
                parts.Add(new DayPart(start,start.AddHours(6)){
                    Condition=ToComposite(condition),
                    SourceCode=symbol,
                    PrecipitationProbability=blockPop,
                });
            }
            //時系列の瞬間値で最高・最低を補う
            foreach(var point in points){
                var local=TimeZoneInfo.ConvertTime(point.Start,zone);
                if(DateOnly.FromDateTime(local.DateTime)==date){
                    max=Max(max,point.TemperatureC);
                    min=Min(min,point.TemperatureC);
                }
            }
            double? sum=null;
            if(hasPrecipitation){
                sum=Math.Round(precipitation,1);
            }
            result.Add(new DailyForecast(date,source){
                Condition=ToComposite(best),
                TempMaxC=max,
                TempMinC=min,
                PrecipitationMm=sum,
                PrecipitationProbability=pop,
                Parts=parts,
            });
        }
        return result;
    }

    private static CompositeCondition? ToComposite(WeatherCondition? condition){
        if(condition is {} c){
            return new CompositeCondition(c);
        }
        return null;
    }

    private static double? Max(double? a,double? b){
        if(a is null){
            return b;
        }
        if(b is null){
            return a;
        }
        return Math.Max(a.Value,b.Value);
    }

    private static double? Min(double? a,double? b){
        if(a is null){
            return b;
        }
        if(b is null){
            return a;
        }
        return Math.Min(a.Value,b.Value);
    }

    private static int? Percent(double? value){
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

    private static double? Direction(double? value){
        if(value is {} v&&double.IsFinite(v)){
            return GeoMath.NormalizeDegrees(v);
        }
        return null;
    }
}

/// <summary>MET Norway Locationforecast 2.0 complete の Provider(予報のみ)。</summary>
public sealed partial class MetNorwayProvider(IHttpClientFactory httpClientFactory,TimeProvider time,ILogger<MetNorwayProvider> logger):IForecastProvider{
    public const string HttpClientName="met";

    public ProviderId Id=>ProviderId.MetNorway;

    public async ValueTask<Forecast> GetForecastAsync(ResolvedLocation location,CancellationToken cancellationToken){
        ArgumentNullException.ThrowIfNull(location);
        if(location.IsJapanArea){
            //方針 5: 日本周辺域には MET を使わない(Router と二重に防ぐ)
            throw new InvalidOperationException("日本周辺域で MET Norway を使うことはできません。");
        }
        var client=httpClientFactory.CreateClient(HttpClientName);
        var p=location.Point.RoundForRequest();
        var uri=new Uri(string.Create(CultureInfo.InvariantCulture,$"https://api.met.no/weatherapi/locationforecast/2.0/complete?lat={p.Latitude:0.##}&lon={p.Longitude:0.##}"));
        var fetch=await ProviderHttp.GetAsync(client,ProviderId.MetNorway,uri,time,cancellationToken).ConfigureAwait(false);
        using var doc=ProviderHttp.ParseJson(ProviderId.MetNorway,fetch,uri);
        var zone=FindZone(location.TimeZoneId);
        var unknown=new List<string>();
        var (series,daily)=ProviderHttp.Map(ProviderId.MetNorway,uri,()=>MetMapper.Map(doc.RootElement,fetch,zone,unknown));
        foreach(var symbol in unknown.Distinct()){
            LogUnknownSymbol(logger,symbol);
        }
        return new Forecast(location,series,daily);
    }

    internal static TimeZoneInfo FindZone(string id){
        try{
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }catch(Exception ex) when(ex is TimeZoneNotFoundException or InvalidTimeZoneException){
            return TimeZoneInfo.Utc;
        }
    }

    [LoggerMessage(Level=LogLevel.Warning,Message="未知の MET シンボルです: {Symbol}")]
    private static partial void LogUnknownSymbol(ILogger logger,string symbol);
}
