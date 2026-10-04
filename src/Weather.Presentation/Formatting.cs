using System.Globalization;
using Weather.Core;
using Weather.Presentation.Resources;

namespace Weather.Presentation;

/// <summary>表示単位への換算と書式(単位の換算は Presentation の責務。DataModel.md「単位」)。</summary>
public static class Units{
    public static string Temperature(double? celsius,UnitSystem units){
        if(celsius is not {} c){
            return "--";
        }
        return string.Create(CultureInfo.InvariantCulture,$"{ToTemperature(c,units):0}°");
    }

    public static double ToTemperature(double celsius,UnitSystem units){
        if(units==UnitSystem.Imperial){
            return celsius*9/5+32;
        }
        return celsius;
    }

    public static string Wind(double? metersPerSecond,UnitSystem units){
        if(metersPerSecond is not {} v){
            return "--";
        }
        if(units==UnitSystem.Imperial){
            return string.Create(CultureInfo.InvariantCulture,$"{v*2.23694:0} mph");
        }
        return string.Create(CultureInfo.InvariantCulture,$"{v:0} m/s");
    }

    public static string WindRange(ValueRange range,UnitSystem units){
        if(units==UnitSystem.Imperial){
            return string.Create(CultureInfo.InvariantCulture,$"{range.Lower*2.23694:0}〜{range.Upper*2.23694:0} mph");
        }
        return string.Create(CultureInfo.InvariantCulture,$"{range.Lower:0}〜{range.Upper:0} m/s");
    }

    public static string Precipitation(double? mm,UnitSystem units){
        if(mm is not {} v){
            return "--";
        }
        if(units==UnitSystem.Imperial){
            return string.Create(CultureInfo.InvariantCulture,$"{v/25.4:0.00} in");
        }
        return string.Create(CultureInfo.InvariantCulture,$"{v:0.#} mm");
    }

    public static double ToPrecipitation(double mm,UnitSystem units){
        if(units==UnitSystem.Imperial){
            return mm/25.4;
        }
        return mm;
    }

    public static string Probability(int? percent){
        if(percent is not {} p){
            return "--";
        }
        return string.Create(CultureInfo.InvariantCulture,$"{p}%");
    }
}

/// <summary>地点のタイムゾーンでの時刻表示。</summary>
public static class TimeText{
    public static TimeZoneInfo Zone(string id){
        try{
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }catch(Exception ex) when(ex is TimeZoneNotFoundException or InvalidTimeZoneException){
            return TimeZoneInfo.Utc;
        }
    }

    public static DateTimeOffset Local(DateTimeOffset time,TimeZoneInfo zone){
        return TimeZoneInfo.ConvertTime(time,zone);
    }

    public static string Hour(DateTimeOffset time,TimeZoneInfo zone){
        return Local(time,zone).ToString(Strings.HourFormat,CultureInfo.InvariantCulture);
    }

    public static string Clock(DateTimeOffset time,TimeZoneInfo zone){
        return Local(time,zone).ToString("HH:mm",CultureInfo.InvariantCulture);
    }

    public static string Day(DateOnly date){
        return date.ToString(Strings.DayFormat,CultureInfo.CurrentUICulture);
    }
}

/// <summary>天気 → アイコン(絵文字。素材のライセンスが不要)。昼夜は太陽高度で判定する。</summary>
public static class ConditionIcons{
    public static string For(CompositeCondition? condition,bool isNight){
        if(condition is not {} c){
            return "・";
        }
        var w=c.Primary;
        if(c.Transition is ConditionTransition.Occasionally or ConditionTransition.Temporarily&&c.Secondary is {} s&&s.HasPrecipitation&&!w.HasPrecipitation){
            w=s;
        }
        if(w.HasThunder){
            return "⛈";
        }
        switch(w.Precipitation){
            case PrecipitationType.Snow:
                return "❄";
            case PrecipitationType.RainAndSnow:
            case PrecipitationType.IcePellets:
            case PrecipitationType.FreezingRain:
            case PrecipitationType.Hail:
                return "🌨";
            case PrecipitationType.Rain:
            case PrecipitationType.Drizzle:
                if(w.IsShowery&&!isNight){
                    return "🌦";
                }
                return "🌧";
        }
        if(w.Obscuration is Obscuration.Fog or Obscuration.Mist){
            return "🌫";
        }
        switch(w.Sky){
            case SkyCover.Clear:
            case SkyCover.MostlyClear:
                if(isNight){
                    return "🌙";
                }
                return "☀";
            case SkyCover.PartlyCloudy:
                if(isNight){
                    return "☁";
                }
                return "⛅";
            case SkyCover.MostlyCloudy:
                if(isNight){
                    return "☁";
                }
                return "🌥";
            default:
                return "☁";
        }
    }

    /// <summary>天気文がない場合の簡潔な説明(原文がある場合は原文を優先する)。</summary>
    public static string Describe(CompositeCondition? condition){
        if(condition is not {} c){
            return "";
        }
        var text=DescribeOne(c.Primary);
        if(c.Transition is {} t&&c.Secondary is {} s){
            var joiner=Strings.JoinLater;
            if(t==ConditionTransition.Occasionally){
                joiner=Strings.JoinOccasionally;
            }else if(t==ConditionTransition.Temporarily){
                joiner=Strings.JoinTemporarily;
            }
            text+=joiner+DescribeOne(s);
        }
        return text;
    }

    private static string DescribeOne(WeatherCondition w){
        if(w.HasThunder){
            return Strings.Thunderstorm;
        }
        switch(w.Precipitation){
            case PrecipitationType.Snow:
                return Strings.Snow;
            case PrecipitationType.RainAndSnow:
                return Strings.Sleet;
            case PrecipitationType.IcePellets:
                return Strings.IcePellets;
            case PrecipitationType.FreezingRain:
                return Strings.FreezingRain;
            case PrecipitationType.Hail:
                return Strings.Hail;
            case PrecipitationType.Drizzle:
                return Strings.Drizzle;
            case PrecipitationType.Rain:
                if(w.Intensity==PrecipitationIntensity.Heavy){
                    return Strings.HeavyRain;
                }
                return Strings.Rain;
        }
        if(w.Obscuration==Obscuration.Fog){
            return Strings.Fog;
        }
        switch(w.Sky){
            case SkyCover.Clear:
                return Strings.Clear;
            case SkyCover.MostlyClear:
                return Strings.MostlyClear;
            case SkyCover.PartlyCloudy:
                return Strings.PartlyCloudy;
            case SkyCover.MostlyCloudy:
                return Strings.MostlyCloudy;
            case SkyCover.Overcast:
                return Strings.Overcast;
            default:
                return "";
        }
    }
}

/// <summary>出典の表示(Screens.md「出典の表示ルール」)。</summary>
public static class AttributionText{
    /// <summary>カード下部の短い出典(例:「気象庁 05:00 発表」「MET Norway 06:30 更新(アプリで集計)」)。</summary>
    public static string Short(SourceAttribution source,TimeZoneInfo zone){
        ArgumentNullException.ThrowIfNull(source);
        var text=source.AgencyName;
        if(source.Provider==ProviderId.Gfs){
            //地図の格子は数値予報モデルの計算結果であることを示す(例:「NOAA GFS 0.5° 09:00 初期値」)
            text+=" "+source.ProductName;
        }
        if(source.IssuedAt is {} issued){
            var verb=Strings.Updated;
            if(source.Provider==ProviderId.Jma){
                verb=Strings.Issued;
            }else if(source.Provider==ProviderId.Gfs){
                verb=Strings.Initialized;
            }
            if(source.Provider==ProviderId.Jma&&source.ProductName.StartsWith("アメダス",StringComparison.Ordinal)){
                verb=Strings.Observed;
            }
            text+=" "+TimeText.Clock(issued,zone)+" "+verb;
        }
        if(source.Processing.HasFlag(DataProcessing.Aggregated)){
            text+=Strings.AggregatedNote;
        }
        if(source.IsStale){
            text+=string.Format(CultureInfo.CurrentCulture,Strings.LastRetrievedFormat,TimeText.Clock(source.RetrievedAt,zone));
        }
        return text;
    }

    /// <summary>必須の出典表記(気象庁は「出典:気象庁ホームページ」)。</summary>
    public static string Credit(SourceAttribution source){
        ArgumentNullException.ThrowIfNull(source);
        switch(source.Provider){
            case ProviderId.Jma:
                return "出典:気象庁ホームページ";
            case ProviderId.MetNorway:
                return "Data from MET Norway (CC BY 4.0)";
            case ProviderId.Gfs:
                return "Source: NOAA GFS (public domain)";
            default:
                return "Source: National Weather Service";
        }
    }

    /// <summary>加工の説明(CC BY 4.0 の改変表示・気象庁規約の加工表示)。</summary>
    public static string? ProcessingNote(SourceAttribution source){
        ArgumentNullException.ThrowIfNull(source);
        var notes=new List<string>();
        if(source.Processing.HasFlag(DataProcessing.Aggregated)){
            notes.Add(Strings.ProcessingAggregated);
        }
        if(source.Processing.HasFlag(DataProcessing.UnitConverted)){
            notes.Add(Strings.ProcessingUnits);
        }
        if(notes.Count==0){
            return null;
        }
        return string.Join(" ",notes);
    }
}
