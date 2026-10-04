using Weather.Core;

namespace Weather.Remote.Contracts;

/// <summary>
/// Core のモデル ⇔ 電文。モデル側の検証(範囲・昇順・日本域は気象庁のみ)は復元時にコンストラクタで再び行われるため、
/// サーバーが不正な電文を返してもクライアントで検出できる。
/// </summary>
public static class ContractMapper{
    public static GeoPointDto ToDto(GeoPoint point){
        return new GeoPointDto(point.Latitude,point.Longitude);
    }

    public static GeoPoint ToModel(GeoPointDto dto){
        ArgumentNullException.ThrowIfNull(dto);
        return new GeoPoint(dto.Latitude,dto.Longitude);
    }

    public static LocationDto ToDto(ResolvedLocation location){
        ArgumentNullException.ThrowIfNull(location);
        JmaAreaDto? area=null;
        if(location.JmaArea is {} a){
            area=new JmaAreaDto(a.Kind,a.Class20Code,a.DistanceKm);
        }
        return new LocationDto(ToDto(location.Point),location.CountryCode,location.DisplayName,location.TimeZoneId,location.AdminName,area);
    }

    public static ResolvedLocation ToModel(LocationDto dto){
        ArgumentNullException.ThrowIfNull(dto);
        JmaAreaMatch? area=null;
        if(dto.JmaArea is {} a){
            area=new JmaAreaMatch(a.Kind,a.Class20Code,a.DistanceKm);
        }
        return new ResolvedLocation{
            Point=ToModel(dto.Point),
            CountryCode=dto.CountryCode,
            DisplayName=dto.DisplayName,
            TimeZoneId=dto.TimeZoneId,
            AdminName=dto.AdminName,
            JmaArea=area,
        };
    }

    public static ForecastDto ToDto(Forecast forecast){
        ArgumentNullException.ThrowIfNull(forecast);
        var sources=new SourceTable();
        var points=forecast.TimeSeries.Select(p=>ToDto(p,sources)).ToList();
        var daily=forecast.Daily.Select(d=>ToDto(d,sources)).ToList();
        var issues=forecast.Issues.Select(static i=>new DataIssueDto(i.Provider,i.ProductName,i.Failure)).ToList();
        return new ForecastDto(ToDto(forecast.Location),forecast.AreaName,sources.Items,points,daily,issues);
    }

    public static Forecast ToModel(ForecastDto dto){
        ArgumentNullException.ThrowIfNull(dto);
        var sources=dto.Sources.Select(ToModel).ToList();
        var points=dto.TimeSeries.Select(p=>ToModel(p,sources)).ToList();
        var daily=dto.Daily.Select(d=>ToModel(d,sources)).ToList();
        var issues=dto.Issues.Select(static i=>new DataIssue(i.Provider,i.ProductName,i.Failure)).ToList();
        return new Forecast(ToModel(dto.Location),points,daily,dto.AreaName,issues);
    }

    public static ForecastResponse ToDto(ForecastResult result){
        ArgumentNullException.ThrowIfNull(result);
        ForecastDto? forecast=null;
        if(result.Forecast is {} f){
            forecast=ToDto(f);
        }
        return new ForecastResponse(result.Availability,forecast);
    }

    public static ForecastResult ToModel(ForecastResponse dto){
        ArgumentNullException.ThrowIfNull(dto);
        Forecast? forecast=null;
        if(dto.Forecast is {} f){
            forecast=ToModel(f);
        }
        return new ForecastResult(dto.Availability,forecast);
    }

    public static AlertResponse ToDto(AlertResult result){
        ArgumentNullException.ThrowIfNull(result);
        if(result.Alerts is not {} set){
            return new AlertResponse(result.Availability,null);
        }
        var sources=new SourceTable();
        var setSource=sources.IndexOf(set.Source);
        var alerts=set.Alerts.Select(a=>new AlertDto{
            Id=a.Id,
            EventName=a.EventName,
            Tier=a.Tier,
            Status=a.Status,
            Source=sources.IndexOf(a.Source),
            EventCode=a.EventCode,
            WarningLevel=a.WarningLevel,
            Onset=a.Onset,
            Expires=a.Expires,
            AreaName=a.AreaName,
            Headline=a.Headline,
            Description=a.Description,
            Instruction=a.Instruction,
            SourceSeverity=a.SourceSeverity,
        }).ToList();
        return new AlertResponse(result.Availability,new AlertSetDto(sources.Items,setSource,set.Headline,alerts));
    }

    public static AlertResult ToModel(AlertResponse dto){
        ArgumentNullException.ThrowIfNull(dto);
        if(dto.Alerts is not {} set){
            return new AlertResult(dto.Availability,null);
        }
        var sources=set.Sources.Select(ToModel).ToList();
        var alerts=set.Alerts.Select(a=>new Alert(a.Id,a.EventName,a.Tier,a.Status,sources[a.Source]){
            EventCode=a.EventCode,
            WarningLevel=a.WarningLevel,
            Onset=a.Onset,
            Expires=a.Expires,
            AreaName=a.AreaName,
            Headline=a.Headline,
            Description=a.Description,
            Instruction=a.Instruction,
            SourceSeverity=a.SourceSeverity,
        }).ToList();
        return new AlertResult(dto.Availability,new AlertSet(alerts,sources[set.Source],set.Headline));
    }

    public static StationDto ToDto(ObservationStation station){
        ArgumentNullException.ThrowIfNull(station);
        return new StationDto(station.Id,station.Provider,station.Name,ToDto(station.Location),station.ElevationM);
    }

    public static ObservationStation ToModel(StationDto dto){
        ArgumentNullException.ThrowIfNull(dto);
        return new ObservationStation(dto.Id,dto.Provider,dto.Name,ToModel(dto.Location),dto.ElevationM);
    }

    public static ObservationSeriesDto ToDto(ObservationSeries series){
        ArgumentNullException.ThrowIfNull(series);
        var items=series.Items.Select(static o=>new ObservationDto{
            ObservedAt=o.ObservedAt,
            TemperatureC=ToDto(o.TemperatureC),
            HumidityPercent=ToDto(o.HumidityPercent),
            Precipitation10mMm=ToDto(o.Precipitation10mMm),
            Precipitation1hMm=ToDto(o.Precipitation1hMm),
            Precipitation24hMm=ToDto(o.Precipitation24hMm),
            WindSpeedMs=ToDto(o.WindSpeedMs),
            WindDirectionDeg=ToDto(o.WindDirectionDeg),
            WindGustMs=ToDto(o.WindGustMs),
            PressureHpa=ToDto(o.PressureHpa),
            SeaLevelPressureHpa=ToDto(o.SeaLevelPressureHpa),
            Sunshine1hHours=ToDto(o.Sunshine1hHours),
            SnowDepthCm=ToDto(o.SnowDepthCm),
            VisibilityM=ToDto(o.VisibilityM),
            Condition=ToDto(o.Condition),
            WeatherText=o.WeatherText,
        }).ToList();
        var summaries=series.DailySummaries.Select(static s=>new DailySummaryDto(s.StationKey,s.LocalDate,s.MaxTempC,s.MaxTempAt,s.MinTempC,s.MinTempAt,s.PrecipitationMm,s.IsDerived)).ToList();
        return new ObservationSeriesDto(ToDto(series.Station),ToDto(series.Source),items,summaries);
    }

    /// <summary>観測所は要求元のものを使う(サーバーは ID と Provider だけで取得するため、名称等は要求元が正しい)。</summary>
    public static ObservationSeries ToModel(ObservationSeriesDto dto,ObservationStation station){
        ArgumentNullException.ThrowIfNull(dto);
        var items=dto.Items.Select(static o=>new Observation(o.ObservedAt){
            TemperatureC=ToModel(o.TemperatureC),
            HumidityPercent=ToModel(o.HumidityPercent),
            Precipitation10mMm=ToModel(o.Precipitation10mMm),
            Precipitation1hMm=ToModel(o.Precipitation1hMm),
            Precipitation24hMm=ToModel(o.Precipitation24hMm),
            WindSpeedMs=ToModel(o.WindSpeedMs),
            WindDirectionDeg=ToModel(o.WindDirectionDeg),
            WindGustMs=ToModel(o.WindGustMs),
            PressureHpa=ToModel(o.PressureHpa),
            SeaLevelPressureHpa=ToModel(o.SeaLevelPressureHpa),
            Sunshine1hHours=ToModel(o.Sunshine1hHours),
            SnowDepthCm=ToModel(o.SnowDepthCm),
            VisibilityM=ToModel(o.VisibilityM),
            Condition=ToModel(o.Condition),
            WeatherText=o.WeatherText,
        }).ToList();
        var summaries=dto.DailySummaries.Select(static s=>new DailyObservationSummary(s.StationKey,s.LocalDate){
            MaxTempC=s.MaxTempC,
            MaxTempAt=s.MaxTempAt,
            MinTempC=s.MinTempC,
            MinTempAt=s.MinTempAt,
            PrecipitationMm=s.PrecipitationMm,
            IsDerived=s.IsDerived,
        }).ToList();
        return new ObservationSeries(station,items,ToModel(dto.Source),summaries);
    }

    public static SourceDto ToDto(SourceAttribution source){
        ArgumentNullException.ThrowIfNull(source);
        return new SourceDto(
            source.Provider,
            source.AgencyName,
            source.ProductName,
            source.PublishingOffice,
            source.IssuedAt,
            source.RetrievedAt,
            new LicenseDto(source.License.Name,source.License.Url?.OriginalString),
            source.SourceUrl?.OriginalString,
            source.Processing,
            source.IsStale);
    }

    public static SourceAttribution ToModel(SourceDto dto){
        ArgumentNullException.ThrowIfNull(dto);
        return new SourceAttribution{
            Provider=dto.Provider,
            AgencyName=dto.AgencyName,
            ProductName=dto.ProductName,
            PublishingOffice=dto.PublishingOffice,
            IssuedAt=dto.IssuedAt,
            RetrievedAt=dto.RetrievedAt,
            License=new LicenseInfo(dto.License.Name,ToUri(dto.License.Url)),
            SourceUrl=ToUri(dto.SourceUrl),
            Processing=dto.Processing,
            IsStale=dto.IsStale,
        };
    }

    private static ForecastPointDto ToDto(ForecastPoint p,SourceTable sources){
        return new ForecastPointDto{
            Start=p.Start,
            End=p.End,
            Source=sources.IndexOf(p.Source),
            Condition=ToDto(p.Condition),
            SourceCode=p.SourceCode,
            WeatherText=p.WeatherText,
            TemperatureC=p.TemperatureC,
            PrecipitationProbability=p.PrecipitationProbability,
            PrecipitationMm=p.PrecipitationMm,
            CloudCoverPercent=p.CloudCoverPercent,
            HumidityPercent=p.HumidityPercent,
            WindSpeedMs=p.WindSpeedMs,
            WindSpeedRangeMs=ToDto(p.WindSpeedRangeMs),
            WindGustMs=p.WindGustMs,
            WindDirectionDeg=p.WindDirectionDeg,
            PressureHpa=p.PressureHpa,
            ThunderProbability=p.ThunderProbability,
            FogPercent=p.FogPercent,
            VisibilityM=p.VisibilityM,
        };
    }

    private static ForecastPoint ToModel(ForecastPointDto p,List<SourceAttribution> sources){
        return new ForecastPoint(p.Start,p.End,sources[p.Source]){
            Condition=ToModel(p.Condition),
            SourceCode=p.SourceCode,
            WeatherText=p.WeatherText,
            TemperatureC=p.TemperatureC,
            PrecipitationProbability=p.PrecipitationProbability,
            PrecipitationMm=p.PrecipitationMm,
            CloudCoverPercent=p.CloudCoverPercent,
            HumidityPercent=p.HumidityPercent,
            WindSpeedMs=p.WindSpeedMs,
            WindSpeedRangeMs=ToModel(p.WindSpeedRangeMs),
            WindGustMs=p.WindGustMs,
            WindDirectionDeg=p.WindDirectionDeg,
            PressureHpa=p.PressureHpa,
            ThunderProbability=p.ThunderProbability,
            FogPercent=p.FogPercent,
            VisibilityM=p.VisibilityM,
        };
    }

    private static DailyDto ToDto(DailyForecast d,SourceTable sources){
        int? temperatureSource=null;
        if(d.TemperatureSource is {} ts){
            temperatureSource=sources.IndexOf(ts);
        }
        return new DailyDto{
            Date=d.Date,
            Source=sources.IndexOf(d.Source),
            TemperatureSource=temperatureSource,
            Condition=ToDto(d.Condition),
            SourceCode=d.SourceCode,
            WeatherText=d.WeatherText,
            WindText=d.WindText,
            WaveText=d.WaveText,
            TempMaxC=d.TempMaxC,
            TempMinC=d.TempMinC,
            TempMaxRangeC=ToDto(d.TempMaxRangeC),
            TempMinRangeC=ToDto(d.TempMinRangeC),
            TemperaturePointName=d.TemperaturePointName,
            PrecipitationMm=d.PrecipitationMm,
            PrecipitationProbability=d.PrecipitationProbability,
            Parts=[..d.Parts.Select(static p=>new DayPartDto{
                Start=p.Start,
                End=p.End,
                Label=p.Label,
                Condition=ToDto(p.Condition),
                SourceCode=p.SourceCode,
                WeatherText=p.WeatherText,
                DetailText=p.DetailText,
                TemperatureC=p.TemperatureC,
                PrecipitationProbability=p.PrecipitationProbability,
            })],
            Reliability=d.Reliability,
        };
    }

    private static DailyForecast ToModel(DailyDto d,List<SourceAttribution> sources){
        SourceAttribution? temperatureSource=null;
        if(d.TemperatureSource is {} ts){
            temperatureSource=sources[ts];
        }
        return new DailyForecast(d.Date,sources[d.Source]){
            TemperatureSource=temperatureSource,
            Condition=ToModel(d.Condition),
            SourceCode=d.SourceCode,
            WeatherText=d.WeatherText,
            WindText=d.WindText,
            WaveText=d.WaveText,
            TempMaxC=d.TempMaxC,
            TempMinC=d.TempMinC,
            TempMaxRangeC=ToModel(d.TempMaxRangeC),
            TempMinRangeC=ToModel(d.TempMinRangeC),
            TemperaturePointName=d.TemperaturePointName,
            PrecipitationMm=d.PrecipitationMm,
            PrecipitationProbability=d.PrecipitationProbability,
            Parts=[..d.Parts.Select(static p=>new DayPart(p.Start,p.End){
                Label=p.Label,
                Condition=ToModel(p.Condition),
                SourceCode=p.SourceCode,
                WeatherText=p.WeatherText,
                DetailText=p.DetailText,
                TemperatureC=p.TemperatureC,
                PrecipitationProbability=p.PrecipitationProbability,
            })],
            Reliability=d.Reliability,
        };
    }

    private static CompositeConditionDto? ToDto(CompositeCondition? condition){
        if(condition is not {} c){
            return null;
        }
        ConditionDto? secondary=null;
        if(c.Secondary is {} s){
            secondary=ToDto(s);
        }
        return new CompositeConditionDto(ToDto(c.Primary),c.Transition,secondary);
    }

    private static CompositeCondition? ToModel(CompositeConditionDto? dto){
        if(dto is null){
            return null;
        }
        WeatherCondition? secondary=null;
        if(dto.Secondary is {} s){
            secondary=ToModel(s);
        }
        return new CompositeCondition(ToModel(dto.Primary),dto.Transition,secondary);
    }

    private static ConditionDto ToDto(WeatherCondition w){
        return new ConditionDto(w.Sky,w.Precipitation,w.Intensity,w.IsShowery,w.HasThunder,w.HasStrongWind,w.Obscuration);
    }

    private static WeatherCondition ToModel(ConditionDto dto){
        return new WeatherCondition(dto.Sky,dto.Precipitation,dto.Intensity,dto.IsShowery,dto.HasThunder,dto.HasStrongWind,dto.Obscuration);
    }

    private static RangeDto? ToDto(ValueRange? range){
        if(range is not {} r){
            return null;
        }
        return new RangeDto(r.Lower,r.Upper);
    }

    private static ValueRange? ToModel(RangeDto? dto){
        if(dto is null){
            return null;
        }
        return new ValueRange(dto.Lower,dto.Upper);
    }

    private static MeasurementDto? ToDto(Measurement? measurement){
        if(measurement is not {} m){
            return null;
        }
        return new MeasurementDto(m.Value,m.Quality);
    }

    private static Measurement? ToModel(MeasurementDto? dto){
        if(dto is null){
            return null;
        }
        return new Measurement(dto.Value,dto.Quality);
    }

    private static Uri? ToUri(string? value){
        if(string.IsNullOrEmpty(value)){
            return null;
        }
        return new Uri(value,UriKind.RelativeOrAbsolute);
    }

    /// <summary>同じ出典(値として等しい)を 1 回だけ表に載せる。</summary>
    private sealed class SourceTable{
        private readonly Dictionary<SourceAttribution,int> indexes=[];

        public List<SourceDto> Items{get;}=[];

        public int IndexOf(SourceAttribution source){
            if(this.indexes.TryGetValue(source,out var index)){
                return index;
            }
            index=this.Items.Count;
            this.Items.Add(ToDto(source));
            this.indexes.Add(source,index);
            return index;
        }
    }
}
