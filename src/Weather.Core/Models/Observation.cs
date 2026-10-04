namespace Weather.Core;

public enum MeasurementQuality{Normal,Suspect}    //Suspect = 気象庁の準正常値 / NWS の QC 要注意値

public readonly record struct Measurement{
    public Measurement(double value,MeasurementQuality quality=MeasurementQuality.Normal){
        Guard.Finite(value,nameof(value));
        this.Value=value;
        this.Quality=quality;
    }

    public double Value{get;}
    public MeasurementQuality Quality{get;}
}

public sealed record ObservationStation(string Id,ProviderId Provider,string Name,GeoPoint Location,double? ElevationM){
    public string Key=>$"{this.Provider}:{this.Id}";
}

/// <summary>観測値。欠測は null、準正常値は Suspect として保持する。</summary>
public sealed record Observation{
    public Observation(DateTimeOffset observedAt){
        this.ObservedAt=observedAt;
    }

    public DateTimeOffset ObservedAt{get;}
    public Measurement? TemperatureC{get;init;}
    public Measurement? HumidityPercent{get;init;}
    public Measurement? Precipitation10mMm{get;init;}
    public Measurement? Precipitation1hMm{get;init;}
    public Measurement? Precipitation24hMm{get;init;}
    public Measurement? WindSpeedMs{get;init;}

    public Measurement? WindDirectionDeg{
        get;
        init{
            if(value is {} m){
                Guard.Direction(m.Value,nameof(this.WindDirectionDeg));
            }
            field=value;
        }
    }

    public Measurement? WindGustMs{get;init;}
    public Measurement? PressureHpa{get;init;}
    public Measurement? SeaLevelPressureHpa{get;init;}
    public Measurement? Sunshine1hHours{get;init;}
    public Measurement? SnowDepthCm{get;init;}
    public Measurement? VisibilityM{get;init;}
    public CompositeCondition? Condition{get;init;}
    public string? WeatherText{get;init;}
}

/// <summary>観測所の観測値の一覧(時刻の昇順)。</summary>
public sealed class ObservationSeries{
    public ObservationSeries(ObservationStation station,IReadOnlyList<Observation> items,SourceAttribution source,IReadOnlyList<DailyObservationSummary>? dailySummaries=null){
        ArgumentNullException.ThrowIfNull(station);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(source);
        for(var i=1;i<items.Count;i++){
            if(items[i].ObservedAt<=items[i-1].ObservedAt){
                throw new ArgumentException("観測値は時刻の昇順で重複なしである必要があります。",nameof(items));
            }
        }
        this.Station=station;
        this.Items=[..items];
        this.Source=source;
        if(dailySummaries is null){
            this.DailySummaries=[];
        }else{
            this.DailySummaries=[..dailySummaries];
        }
    }

    public ObservationStation Station{get;}
    public IReadOnlyList<Observation> Items{get;}
    public SourceAttribution Source{get;}

    /// <summary>機関が計算した日ごとの値(気象庁の日最高・最低)。提供しない Provider では空。</summary>
    public IReadOnlyList<DailyObservationSummary> DailySummaries{get;}
}

/// <summary>観測所の日ごとの要約。気象庁は機関の値、NWS はアプリで集計した値(IsDerived)。</summary>
public sealed record DailyObservationSummary(string StationKey,DateOnly LocalDate){
    public double? MaxTempC{get;init;}
    public DateTimeOffset? MaxTempAt{get;init;}
    public double? MinTempC{get;init;}
    public DateTimeOffset? MinTempAt{get;init;}
    public double? PrecipitationMm{get;init;}
    public bool IsDerived{get;init;}
}
