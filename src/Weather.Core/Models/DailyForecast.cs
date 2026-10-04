namespace Weather.Core;

public enum ForecastReliability{A,B,C}

/// <summary>日内区分。気象庁の 6 時間降水確率と NWS の昼/夜 periods を同じ型で表す。</summary>
public sealed record DayPart{
    public DayPart(DateTimeOffset start,DateTimeOffset end){
        if(end<=start){
            throw new ArgumentException("end は start より後である必要があります。",nameof(end));
        }
        this.Start=start;
        this.End=end;
    }

    public DateTimeOffset Start{get;}
    public DateTimeOffset End{get;}
    public string? Label{get;init;}
    public CompositeCondition? Condition{get;init;}
    public string? SourceCode{get;init;}
    public string? WeatherText{get;init;}
    public string? DetailText{get;init;}

    public double? TemperatureC{
        get;
        init{
            Guard.Finite(value,nameof(this.TemperatureC));
            field=value;
        }
    }

    public int? PrecipitationProbability{
        get;
        init{
            Guard.Percent(value,nameof(this.PrecipitationProbability));
            field=value;
        }
    }
}

/// <summary>地点の現地日付単位の予報。</summary>
public sealed record DailyForecast{
    public DailyForecast(DateOnly date,SourceAttribution source){
        ArgumentNullException.ThrowIfNull(source);
        this.Date=date;
        this.Source=source;
    }

    public DateOnly Date{get;}
    public SourceAttribution Source{get;}

    /// <summary>気温を別プロダクトから取った場合の出典。null なら Source と同じ。</summary>
    public SourceAttribution? TemperatureSource{get;init;}

    public CompositeCondition? Condition{get;init;}
    public string? SourceCode{get;init;}
    public string? WeatherText{get;init;}
    public string? WindText{get;init;}
    public string? WaveText{get;init;}

    public double? TempMaxC{
        get;
        init{
            Guard.Finite(value,nameof(this.TempMaxC));
            field=value;
        }
    }

    public double? TempMinC{
        get;
        init{
            Guard.Finite(value,nameof(this.TempMinC));
            field=value;
        }
    }

    public ValueRange? TempMaxRangeC{get;init;}
    public ValueRange? TempMinRangeC{get;init;}
    public string? TemperaturePointName{get;init;}

    public double? PrecipitationMm{
        get;
        init{
            Guard.NonNegative(value,nameof(this.PrecipitationMm));
            field=value;
        }
    }

    public int? PrecipitationProbability{
        get;
        init{
            Guard.Percent(value,nameof(this.PrecipitationProbability));
            field=value;
        }
    }

    public IReadOnlyList<DayPart> Parts{get;init;}=[];
    public ForecastReliability? Reliability{get;init;}

    public SourceAttribution EffectiveTemperatureSource{
        get{
            if(this.TemperatureSource is null){
                return this.Source;
            }
            return this.TemperatureSource;
        }
    }
}
