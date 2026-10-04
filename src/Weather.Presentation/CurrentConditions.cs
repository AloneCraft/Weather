using Weather.Core;

namespace Weather.Presentation;

/// <summary>「いま」の表示値。メイン画面とウィジェットで同じ規則を使う(日本域は気象庁の天気文を優先する。方針 5)。</summary>
public sealed record CurrentConditions(string Temperature,string Icon,string Text,DailyForecast? Today){
    public static CurrentConditions From(Forecast forecast,GeoPoint point,DateTimeOffset now,TimeZoneInfo zone,UnitSystem units){
        ArgumentNullException.ThrowIfNull(forecast);
        var current=forecast.FindPoint(now);
        if(current?.TemperatureC is null){
            //気象庁の時系列は 3 時間ごとで次の時刻から始まり、17 時の発表後は今日の最高気温もない。3 時間以内の次の値を使う
            current=forecast.TimeSeries.FirstOrDefault(p=>p.TemperatureC is not null&&p.End>now&&p.Start-now<=TimeSpan.FromHours(3))??current;
        }
        var today=forecast.Daily.FirstOrDefault(d=>d.Date==DateOnly.FromDateTime(TimeText.Local(now,zone).DateTime))??forecast.Daily.FirstOrDefault();
        var night=IsNight(point,now);
        var temperature="--";
        var icon="";
        var text="";
        if(current is not null){
            temperature=Units.Temperature(current.TemperatureC,units);
            icon=ConditionIcons.For(current.Condition,night);
            text=current.WeatherText??ConditionIcons.Describe(current.Condition);
        }else if(today is not null){
            temperature=Units.Temperature(today.TempMaxC,units);
            icon=ConditionIcons.For(today.Condition,night);
            text=today.WeatherText??ConditionIcons.Describe(today.Condition);
        }
        if(forecast.Location.IsJapanArea&&today?.WeatherText is {} official){
            text=official.Replace('　',' ');
        }
        return new CurrentConditions(temperature,icon,text,today);
    }

    /// <summary>日の出・日の入りの太陽高度(-0.833°)より低ければ夜。</summary>
    public static bool IsNight(GeoPoint point,DateTimeOffset time){
        return Weather.Scene.Astronomy.SolarPosition.Compute(point.Latitude,point.Longitude,time).AltitudeDeg<-0.833;
    }

    /// <summary>その日の降水確率(日の値がなければ時間帯の最大)。</summary>
    public static int? DayProbability(DailyForecast day){
        ArgumentNullException.ThrowIfNull(day);
        var pop=day.PrecipitationProbability;
        if(pop is null&&day.Parts.Count>0){
            pop=day.Parts.Max(static p=>p.PrecipitationProbability);
        }
        return pop;
    }
}
