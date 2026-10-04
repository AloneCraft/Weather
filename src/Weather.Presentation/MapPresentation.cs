using System.Globalization;
using Weather.Core;
using Weather.Presentation.Resources;

namespace Weather.Presentation;

/// <summary>
/// 地図の時間軸(Screens.md「地図」)。3 時間前から 5 日後まで。
/// 刻み: 雨雲の動きがある範囲(過去 3 時間〜1 時間先)は 5 分、15 時間先までは 1 時間、それ以降は GFS の 3 時間。
/// </summary>
public static class MapTimeline{
    public static readonly TimeSpan Past=TimeSpan.FromHours(3);
    public static readonly TimeSpan HourlyUntil=TimeSpan.FromHours(15);
    public static readonly TimeSpan Future=TimeSpan.FromDays(5);

    public static IReadOnlyList<DateTimeOffset> Build(MapAvailability availability,DateTimeOffset now){
        ArgumentNullException.ThrowIfNull(availability);
        var start=now-Past;
        var end=now+Future;
        var steps=new List<DateTimeOffset>();
        foreach(var t in availability.Nowcast){
            if(t>=start&&t<=end){
                steps.Add(t);
            }
        }
        DateTimeOffset cursor;
        if(steps.Count>0){
            cursor=steps[^1];
        }else{
            cursor=Floor(start,TimeSpan.FromHours(1))-TimeSpan.FromHours(1);
        }
        var hourly=Floor(cursor,TimeSpan.FromHours(1))+TimeSpan.FromHours(1);
        for(var t=hourly;t<=now+HourlyUntil&&t<=end;t+=TimeSpan.FromHours(1)){
            if(t>=start){
                steps.Add(t);
            }
        }
        var last=start;
        if(steps.Count>0){
            last=steps[^1];
        }
        var later=availability.Gfs.Concat(availability.Distribution).Where(t=>t>last&&t<=end).Distinct().Order();
        steps.AddRange(later);
        return steps;
    }

    /// <summary>現在時刻に最も近い過去(または同時刻)の刻み。なければ 0。</summary>
    public static int IndexOfNow(IReadOnlyList<DateTimeOffset> steps,DateTimeOffset now){
        ArgumentNullException.ThrowIfNull(steps);
        var index=0;
        for(var i=0;i<steps.Count;i++){
            if(steps[i]<=now){
                index=i;
            }
        }
        return index;
    }

    private static DateTimeOffset Floor(DateTimeOffset t,TimeSpan unit){
        return new DateTimeOffset(t.UtcTicks-t.UtcTicks%unit.Ticks,TimeSpan.Zero);
    }
}

/// <summary>地図の表示用の文言(凡例・吹き出し・日本周辺の注記)。</summary>
public static class MapText{
    public static string LayerTitle(FieldLayer layer){
        switch(layer){
            case FieldLayer.Wind:
                return Strings.LayerWind;
            case FieldLayer.Precipitation:
                return Strings.LayerPrecipitation;
            case FieldLayer.Temperature:
                return Strings.LayerTemperature;
            default:
                return Strings.LayerClouds;
        }
    }

    /// <summary>凡例の区分の文言。数値の区分は下限(最初の区分は「〜未満」、最後は「〜以上」)、天気は区分名。</summary>
    public static string LegendLabel(Legend legend,int index){
        ArgumentNullException.ThrowIfNull(legend);
        var c=legend.Classes[index];
        if(c.Lower is null&&c.Upper is null){
            switch(c.Key){
                case "clear":
                    return Strings.WeatherClear;
                case "cloudy":
                    return Strings.WeatherCloudy;
                case "rain":
                    return Strings.WeatherRain;
                case "rainOrSnow":
                    return Strings.WeatherRainOrSnow;
                default:
                    return Strings.WeatherSnow;
            }
        }
        if(c.Lower is null){
            return string.Format(CultureInfo.CurrentCulture,Strings.LegendBelowFormat,Number(c.Upper!.Value));
        }
        if(c.Upper is null&&!legend.Smooth){
            return string.Format(CultureInfo.CurrentCulture,Strings.LegendAboveFormat,Number(c.Lower.Value));
        }
        return Number(c.Lower.Value);
    }

    public static string JapanNote(MapFrame frame){
        ArgumentNullException.ThrowIfNull(frame);
        switch(frame.Japan){
            case JapanCoverage.OutOfRange:
                return Strings.JapanNoteOutOfRange;
            case JapanCoverage.NotAvailable:
                return Strings.JapanNoteNotAvailable;
        }
        if(frame.Arrows is {Kind:ArrowKind.Observation}||frame.Points is not null&&frame.Tiles.Count==0){
            if(frame.JapanWind is not null){
                return Strings.JapanNoteObservationMarineFlow;
            }
            return Strings.JapanNoteObservation;
        }
        if(frame.Arrows is {Kind:ArrowKind.Forecast}){
            if(frame.JapanWind is not null){
                return Strings.JapanNoteMarineFlow;
            }
            return Strings.JapanNoteMarine;
        }
        return Strings.JapanNoteJma;
    }

    /// <summary>出典の帯(方針 7)。例:「気象庁 降水ナウキャスト 17:15 | NOAA GFS 09:00 初期値」。</summary>
    public static string Attribution(MapFrame frame,TimeZoneInfo zone){
        ArgumentNullException.ThrowIfNull(frame);
        return string.Join(" | ",frame.Sources.Select(s=>AttributionText.Short(s,zone)));
    }

    /// <summary>格子の値(GFS)の表示。単位は設定に従う。</summary>
    public static string Value(FieldQuantity quantity,double value,UnitSystem units){
        switch(quantity){
            case FieldQuantity.TemperatureC:
                return Units.Temperature(value,units);
            case FieldQuantity.PrecipitationMmPerHour:
                if(units==UnitSystem.Imperial){
                    return string.Create(CultureInfo.InvariantCulture,$"{value/25.4:0.00} in/h");
                }
                return string.Create(CultureInfo.InvariantCulture,$"{value:0.0} mm/h");
            case FieldQuantity.CloudCoverPercent:
                return string.Create(CultureInfo.InvariantCulture,$"{value:0}%");
            case FieldQuantity.PressureHpa:
                return string.Create(CultureInfo.InvariantCulture,$"{value:0} hPa");
            default:
                return Units.Wind(value,units);
        }
    }

    public static string Time(DateTimeOffset time,TimeZoneInfo zone){
        return TimeZoneInfo.ConvertTime(time,zone).ToString(Strings.MapTimeFormat,CultureInfo.CurrentCulture);
    }

    /// <summary>現在時刻との差(「現在」「+3 時間」「−2 時間」)。</summary>
    public static string Relative(DateTimeOffset time,DateTimeOffset now){
        var hours=(time-now).TotalHours;
        if(Math.Abs(hours)<0.5){
            return Strings.TimelineNow;
        }
        return string.Format(CultureInfo.CurrentCulture,Strings.RelativeHoursFormat,(int)Math.Round(hours));
    }

    private static string Number(double value){
        return value.ToString("0.##",CultureInfo.CurrentCulture);
    }
}

/// <summary>気象庁のタイルの、ある地点の画素の色(ARGB)を読む(App が Rendering で実装する)。</summary>
public interface IMapPixelSampler{
    ValueTask<uint?> SampleAsync(JmaTileLayer layer,GeoPoint point,CancellationToken cancellationToken);
}

/// <summary>タイルの画素を読めない環境(テスト・未対応)。吹き出しは「該当なし」になる。</summary>
public sealed class NullMapPixelSampler:IMapPixelSampler{
    public ValueTask<uint?> SampleAsync(JmaTileLayer layer,GeoPoint point,CancellationToken cancellationToken){
        return ValueTask.FromResult<uint?>(null);
    }
}

/// <summary>地図への移動の依頼(検索で選んだ地点を地図に出す)。</summary>
public sealed record MapFocusRequest(GeoPoint Point,string Name);

public sealed class MapFocusService{
    public event EventHandler<MapFocusRequest>? Requested;

    /// <summary>まだ地図が受け取っていない依頼(地図画面が作られる前の依頼)。</summary>
    public MapFocusRequest? Pending{get;private set;}

    public void Request(GeoPoint point,string name){
        var request=new MapFocusRequest(point,name);
        this.Pending=request;
        this.Requested?.Invoke(this,request);
    }

    public MapFocusRequest? Take(){
        var request=this.Pending;
        this.Pending=null;
        return request;
    }
}
