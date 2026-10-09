using Weather.Core;
using Weather.Scene.Astronomy;

namespace Weather.Scene;

/// <summary>変換のしきい値・係数(SceneLab で調整して既定値に反映する)。</summary>
public sealed record SceneTuning{
    public double ClearCover{get;init;}=0.05;
    public double MostlyClearCover{get;init;}=0.25;
    public double PartlyCloudyCover{get;init;}=0.5;
    public double MostlyCloudyCover{get;init;}=0.75;
    public double OvercastCover{get;init;}=0.95;
    public double UnknownCover{get;init;}=0.5;
    public double LightIntensity{get;init;}=0.3;
    public double ModerateIntensity{get;init;}=0.6;
    public double HeavyIntensity{get;init;}=0.9;
    public double OccasionallyIntermittency{get;init;}=0.5;
    public double TemporarilyIntermittency{get;init;}=0.75;
    public double StrongWindMs{get;init;}=12;
    public int ThunderProbabilityThreshold{get;init;}=30;

    public static SceneTuning Default{get;}=new();
}

/// <summary>
/// 予報 → シーン状態の純粋関数(SceneState.md)。乱数や現在時刻を使わない。
/// 降水の有無は Condition か降水量だけで決め、降水確率から降水を作らない。
/// </summary>
public sealed class SceneConverter(SceneTuning tuning){
    public SceneConverter():this(SceneTuning.Default){
    }

    public SceneTuning Tuning{get;}=tuning;

    /// <summary>「後」(Later)で副天気へ切り替える区間の最小の長さ。これより短い区間は主天気のみ(SceneState.md)。</summary>
    internal static readonly TimeSpan LaterMinimumDuration=TimeSpan.FromHours(6);

    public SceneState Convert(Forecast forecast,DateTimeOffset time){
        ArgumentNullException.ThrowIfNull(forecast);
        var location=forecast.Location;
        var sky=this.Sky(location.Point,time);
        var point=forecast.FindPoint(time);
        if(point is not null){
            return this.FromPoint(sky,point,time);
        }
        var local=ToLocalDate(time,location.TimeZoneId);
        foreach(var day in forecast.Daily){
            if(day.Date!=local){
                continue;
            }
            foreach(var part in day.Parts){
                if(part.Start<=time&&time<part.End&&part.Condition is {} pc){
                    var origin=new SceneOrigin(SceneOriginKind.DayPart,day.Source.Provider,part.Start,part.End);
                    return this.FromCondition(sky,pc,part.Start,part.End,time,origin,null,part.TemperatureC);
                }
            }
            if(day.Condition is {} dc){
                var start=new DateTimeOffset(day.Date.ToDateTime(TimeOnly.MinValue),ZoneOffset(location.TimeZoneId,time));
                var origin=new SceneOrigin(SceneOriginKind.Daily,day.Source.Provider,start,start.AddDays(1));
                return this.FromCondition(sky,dc,start,start.AddDays(1),time,origin,DailyWind(day),null);
            }
        }
        return Neutral(sky,new SceneOrigin(SceneOriginKind.None,null,null,null));
    }

    /// <summary>日本の対象外地点など、天気を描かない中立なシーン(空・太陽・月・星のみ)。</summary>
    public SceneState ConvertSkyOnly(GeoPoint point,DateTimeOffset time,SceneOriginKind kind){
        return Neutral(this.Sky(point,time),new SceneOrigin(kind,null,null,null));
    }

    private (DateTimeOffset Time,CelestialPosition Sun,MoonState Moon) Sky(GeoPoint point,DateTimeOffset time){
        var sun=SolarPosition.Compute(point.Latitude,point.Longitude,time);
        var moon=MoonCalculator.Compute(point.Latitude,point.Longitude,time);
        return (time,sun,moon);
    }

    private SceneState FromPoint((DateTimeOffset Time,CelestialPosition Sun,MoonState Moon) sky,ForecastPoint point,DateTimeOffset time){
        var origin=new SceneOrigin(SceneOriginKind.TimeSeries,point.Source.Provider,point.Start,point.End);
        var state=this.FromCondition(sky,point.Condition,point.Start,point.End,time,origin,null,point.TemperatureC);
        return this.Refine(state,point);
    }

    private SceneState FromCondition((DateTimeOffset Time,CelestialPosition Sun,MoonState Moon) sky,CompositeCondition? composite,DateTimeOffset start,DateTimeOffset end,DateTimeOffset time,SceneOrigin origin,double? windMs,double? temperature){
        if(composite is not {} c){
            return Neutral(sky,origin) with{TemperatureC=temperature};
        }
        var primary=c.Primary;
        WeatherCondition? overlay=null;
        var overlayIntermittency=0d;
        if(c.Transition is {} transition&&c.Secondary is {} secondary){
            switch(transition){
                case ConditionTransition.Later:
                    if(end-start>=LaterMinimumDuration&&time>=start+(end-start)/2){
                        primary=secondary;
                    }
                    break;
                case ConditionTransition.Occasionally:
                    overlay=secondary;
                    overlayIntermittency=this.Tuning.OccasionallyIntermittency;
                    break;
                case ConditionTransition.Temporarily:
                    overlay=secondary;
                    overlayIntermittency=this.Tuning.TemporarilyIntermittency;
                    break;
            }
        }

        var cover=this.Cover(primary.Sky);
        var precipitation=PrecipitationState.None;
        var thunder=0d;
        var cloudType=SceneCloudType.Cumulus;
        if(primary.HasPrecipitation){
            precipitation=new PrecipitationState(MapType(primary.Precipitation),this.Intensity(primary.Intensity),Showery(primary,0));
            var minimum=0.8;
            if(primary.IsShowery){
                minimum=0.6;
            }
            cover=Math.Max(cover,minimum);
        }
        if(overlay is {} o){
            if(o.HasPrecipitation&&!primary.HasPrecipitation){
                precipitation=new PrecipitationState(MapType(o.Precipitation),this.Intensity(o.Intensity),Math.Max(overlayIntermittency,Showery(o,0)));
            }
            cover=Math.Max(cover,(cover+this.Cover(o.Sky))/2);
            if(o.HasPrecipitation){
                cover=Math.Max(cover,0.6);
            }
        }
        var effective=primary;
        if(overlay is {} ov&&ov.HasPrecipitation&&!primary.HasPrecipitation){
            effective=ov;
        }
        if(effective.HasThunder||(overlay?.HasThunder??false)){
            thunder=0.6;
            if(effective.Intensity==PrecipitationIntensity.Heavy){
                thunder=0.9;
            }
            cloudType=SceneCloudType.Cumulonimbus;
        }else if(effective.HasPrecipitation&&!effective.IsShowery){
            cloudType=SceneCloudType.Stratus;
        }else if(primary.Sky==SkyCover.Overcast&&!effective.HasPrecipitation){
            cloudType=SceneCloudType.Stratus;
        }
        if(precipitation.Type==ScenePrecipitationType.None){
            thunder=0;
        }

        var fog=0d;
        var haze=new HazeState(HazeKind.None,0);
        foreach(var obscuration in new[]{primary.Obscuration,overlay?.Obscuration??Obscuration.None}){
            switch(obscuration){
                case Obscuration.Fog:
                    fog=Math.Max(fog,0.8);
                    break;
                case Obscuration.Mist:
                    fog=Math.Max(fog,0.4);
                    break;
                case Obscuration.Haze:
                    haze=new HazeState(HazeKind.Haze,0.5);
                    break;
                case Obscuration.Smoke:
                    haze=new HazeState(HazeKind.Smoke,0.5);
                    break;
                case Obscuration.Dust:
                    haze=new HazeState(HazeKind.Dust,0.5);
                    break;
            }
        }
        var speed=windMs??3;
        if((primary.HasStrongWind||(overlay?.HasStrongWind??false))&&windMs is null){
            speed=this.Tuning.StrongWindMs;
        }
        return new SceneState{
            Time=sky.Time,
            Sun=sky.Sun,
            Moon=sky.Moon,
            Daylight=SolarPosition.Phase(sky.Sun.AltitudeDeg),
            Clouds=new CloudState(Clamp01(cover),Darkness(precipitation.Intensity,thunder),cloudType),
            Precipitation=precipitation,
            Wind=new WindState(speed,270,0),
            ThunderActivity=thunder,
            FogDensity=fog,
            Haze=haze,
            TemperatureC=temperature,
            Origin=origin,
        };
    }

    /// <summary>数値データによる補正(ある場合は分類より優先)。</summary>
    private SceneState Refine(SceneState state,ForecastPoint point){
        var clouds=state.Clouds;
        if(point.CloudCoverPercent is {} cloud){
            clouds=new CloudState(cloud/100d,clouds.Darkness,clouds.Type);
        }
        var precipitation=state.Precipitation;
        if(point.PrecipitationMm is {} mm){
            var hours=Math.Max(point.Duration.TotalHours,1d/60);
            var rate=mm/hours;
            if(precipitation.Type!=ScenePrecipitationType.None){
                if(rate>0){
                    precipitation=new PrecipitationState(precipitation.Type,RateIntensity(precipitation.Type,rate),precipitation.Intermittency);
                }else{
                    precipitation=new PrecipitationState(precipitation.Type,precipitation.Intensity,Math.Max(precipitation.Intermittency,0.6));
                }
            }
        }
        if(point.PrecipitationProbability is {} pop&&precipitation.Type!=ScenePrecipitationType.None&&pop<50){
            precipitation=new PrecipitationState(precipitation.Type,precipitation.Intensity,Math.Max(precipitation.Intermittency,0.6));
        }
        var thunder=state.ThunderActivity;
        if(point.ThunderProbability is {} tp&&precipitation.Type!=ScenePrecipitationType.None&&tp>=this.Tuning.ThunderProbabilityThreshold){
            thunder=Math.Max(thunder,tp/100d);
        }
        if(precipitation.Type==ScenePrecipitationType.None){
            thunder=0;
        }
        var fog=state.FogDensity;
        if(point.FogPercent is {} f){
            fog=f/100d;
        }else if(point.VisibilityM is {} visibility){
            if(visibility<1000){
                fog=Math.Max(fog,0.8);
            }else if(visibility<5000){
                fog=Math.Max(fog,0.4);
            }
        }
        var wind=state.Wind;
        var speed=wind.SpeedMs;
        if(point.WindSpeedMs is {} ws){
            speed=ws;
        }else if(point.WindSpeedRangeMs is {} range){
            speed=range.Middle;
        }
        var direction=wind.DirectionDeg;
        if(point.WindDirectionDeg is {} wd){
            direction=wd;
        }
        var gustiness=0d;
        if(point.WindGustMs is {} gust&&speed>0.1){
            gustiness=Clamp01((gust-speed)/speed);
        }
        var cloudType=clouds.Type;
        if(thunder>0){
            cloudType=SceneCloudType.Cumulonimbus;
        }
        return state with{
            Clouds=new CloudState(clouds.Cover,Darkness(precipitation.Intensity,thunder),cloudType),
            Precipitation=precipitation,
            ThunderActivity=Clamp01(thunder),
            FogDensity=Clamp01(fog),
            Wind=new WindState(speed,direction,gustiness),
        };
    }

    private static SceneState Neutral((DateTimeOffset Time,CelestialPosition Sun,MoonState Moon) sky,SceneOrigin origin){
        return new SceneState{
            Time=sky.Time,
            Sun=sky.Sun,
            Moon=sky.Moon,
            Daylight=SolarPosition.Phase(sky.Sun.AltitudeDeg),
            Clouds=new CloudState(0,0,SceneCloudType.Cumulus),
            Precipitation=PrecipitationState.None,
            Wind=new WindState(0,270,0),
            ThunderActivity=0,
            FogDensity=0,
            Haze=new HazeState(HazeKind.None,0),
            Origin=origin,
        };
    }

    private double Cover(SkyCover sky){
        switch(sky){
            case SkyCover.Clear:
                return this.Tuning.ClearCover;
            case SkyCover.MostlyClear:
                return this.Tuning.MostlyClearCover;
            case SkyCover.PartlyCloudy:
                return this.Tuning.PartlyCloudyCover;
            case SkyCover.MostlyCloudy:
                return this.Tuning.MostlyCloudyCover;
            case SkyCover.Overcast:
                return this.Tuning.OvercastCover;
            default:
                return this.Tuning.UnknownCover;
        }
    }

    private double Intensity(PrecipitationIntensity intensity){
        switch(intensity){
            case PrecipitationIntensity.Light:
                return this.Tuning.LightIntensity;
            case PrecipitationIntensity.Heavy:
                return this.Tuning.HeavyIntensity;
            default:
                return this.Tuning.ModerateIntensity;
        }
    }

    private static double Showery(WeatherCondition condition,double current){
        if(condition.IsShowery){
            return Math.Max(current,0.3);
        }
        return current;
    }

    /// <summary>時間雨量(mm/h)→ 強度。雪は水換算。</summary>
    internal static double RateIntensity(ScenePrecipitationType type,double rate){
        if(type is ScenePrecipitationType.Snow or ScenePrecipitationType.RainAndSnow){
            if(rate<0.5){
                return 0.3;
            }
            if(rate<=2){
                return 0.6;
            }
            return 0.9;
        }
        if(rate<0.5){
            return 0.2;
        }
        if(rate<2.5){
            return 0.35;
        }
        if(rate<7.6){
            return 0.6;
        }
        if(rate<50){
            return 0.85;
        }
        return 1;
    }

    private static ScenePrecipitationType MapType(PrecipitationType type){
        switch(type){
            case PrecipitationType.Drizzle:
                return ScenePrecipitationType.Drizzle;
            case PrecipitationType.Rain:
                return ScenePrecipitationType.Rain;
            case PrecipitationType.FreezingRain:
                return ScenePrecipitationType.FreezingRain;
            case PrecipitationType.RainAndSnow:
                return ScenePrecipitationType.RainAndSnow;
            case PrecipitationType.Snow:
                return ScenePrecipitationType.Snow;
            case PrecipitationType.IcePellets:
                return ScenePrecipitationType.IcePellets;
            case PrecipitationType.Hail:
                return ScenePrecipitationType.Hail;
            default:
                return ScenePrecipitationType.None;
        }
    }

    /// <summary>気象庁の日別の風の文章 → 演出用の近似風速(数値として表示しない)。</summary>
    private static double? DailyWind(DailyForecast day){
        if(day.WindText is not {} text){
            return null;
        }
        if(text.Contains("やや強く",StringComparison.Ordinal)){
            return 7;
        }
        if(text.Contains("強く",StringComparison.Ordinal)){
            return 10;
        }
        return 3;
    }

    private static double Darkness(double intensity,double thunder){
        return Clamp01(0.2+0.5*intensity+0.3*thunder);
    }

    private static double Clamp01(double value){
        if(double.IsNaN(value)){
            return 0;
        }
        return Math.Clamp(value,0,1);
    }

    private static DateOnly ToLocalDate(DateTimeOffset time,string zoneId){
        return DateOnly.FromDateTime(time.ToOffset(ZoneOffset(zoneId,time)).DateTime);
    }

    private static TimeSpan ZoneOffset(string zoneId,DateTimeOffset time){
        try{
            return TimeZoneInfo.FindSystemTimeZoneById(zoneId).GetUtcOffset(time);
        }catch(Exception ex) when(ex is TimeZoneNotFoundException or InvalidTimeZoneException){
            return TimeSpan.Zero;
        }
    }
}
