namespace Weather.Core;

public readonly record struct ValueRange{
    public ValueRange(double lower,double upper){
        Guard.Finite(lower,nameof(lower));
        Guard.Finite(upper,nameof(upper));
        if(lower>upper){
            throw new ArgumentException("lower は upper 以下である必要があります。",nameof(lower));
        }
        this.Lower=lower;
        this.Upper=upper;
    }

    public double Lower{get;}
    public double Upper{get;}
    public double Middle=>(this.Lower+this.Upper)/2;
}

/// <summary>
/// 時系列の 1 区間 [Start, End)。瞬間値(気温・湿度・気圧・風・雲量)は Start 時点の値、
/// 区間値(天気・降水量・降水確率)は区間全体の値。
/// </summary>
public sealed record ForecastPoint{
    public ForecastPoint(DateTimeOffset start,DateTimeOffset end,SourceAttribution source){
        ArgumentNullException.ThrowIfNull(source);
        if(end<=start){
            throw new ArgumentException("end は start より後である必要があります。",nameof(end));
        }
        this.Start=start;
        this.End=end;
        this.Source=source;
    }

    public DateTimeOffset Start{get;}
    public DateTimeOffset End{get;}
    public SourceAttribution Source{get;}
    public TimeSpan Duration=>this.End-this.Start;

    public CompositeCondition? Condition{get;init;}
    public string? SourceCode{get;init;}
    public string? WeatherText{get;init;}

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

    public double? PrecipitationMm{
        get;
        init{
            Guard.NonNegative(value,nameof(this.PrecipitationMm));
            field=value;
        }
    }

    public int? CloudCoverPercent{
        get;
        init{
            Guard.Percent(value,nameof(this.CloudCoverPercent));
            field=value;
        }
    }

    public int? HumidityPercent{
        get;
        init{
            Guard.Percent(value,nameof(this.HumidityPercent));
            field=value;
        }
    }

    public double? WindSpeedMs{
        get;
        init{
            Guard.NonNegative(value,nameof(this.WindSpeedMs));
            field=value;
        }
    }

    public ValueRange? WindSpeedRangeMs{get;init;}

    public double? WindGustMs{
        get;
        init{
            Guard.NonNegative(value,nameof(this.WindGustMs));
            field=value;
        }
    }

    public double? WindDirectionDeg{
        get;
        init{
            Guard.Direction(value,nameof(this.WindDirectionDeg));
            field=value;
        }
    }

    public double? PressureHpa{
        get;
        init{
            Guard.NonNegative(value,nameof(this.PressureHpa));
            field=value;
        }
    }

    public int? ThunderProbability{
        get;
        init{
            Guard.Percent(value,nameof(this.ThunderProbability));
            field=value;
        }
    }

    public int? FogPercent{
        get;
        init{
            Guard.Percent(value,nameof(this.FogPercent));
            field=value;
        }
    }

    public double? VisibilityM{
        get;
        init{
            Guard.NonNegative(value,nameof(this.VisibilityM));
            field=value;
        }
    }

    public bool Contains(DateTimeOffset time){
        return this.Start<=time&&time<this.End;
    }
}
