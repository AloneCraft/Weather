using Weather.Core;

namespace Weather.Scene;

public enum DaylightPhase{Night,AstronomicalTwilight,NauticalTwilight,CivilTwilight,Day}
public enum SceneCloudType{Cumulus,Stratus,Cumulonimbus}
public enum ScenePrecipitationType{None,Drizzle,Rain,FreezingRain,RainAndSnow,Snow,IcePellets,Hail}
public enum HazeKind{None,Haze,Smoke,Dust}
public enum SceneOriginKind{TimeSeries,DayPart,Daily,None,OutOfCoverage}

public readonly record struct CelestialPosition(double AltitudeDeg,double AzimuthDeg);

/// <summary>Phase 0 = 新月、0.5 = 満月。</summary>
public readonly record struct MoonState(CelestialPosition Position,double Phase,double Illumination);

public readonly record struct CloudState{
    public CloudState(double cover,double darkness,SceneCloudType type){
        Guard.UnitInterval(cover,nameof(cover));
        Guard.UnitInterval(darkness,nameof(darkness));
        this.Cover=cover;
        this.Darkness=darkness;
        this.Type=type;
    }

    public double Cover{get;}
    public double Darkness{get;}
    public SceneCloudType Type{get;}
}

/// <summary>Intermittency: 0 = 連続、1 = ほぼ降らない。</summary>
public readonly record struct PrecipitationState{
    public PrecipitationState(ScenePrecipitationType type,double intensity,double intermittency){
        Guard.UnitInterval(intensity,nameof(intensity));
        Guard.UnitInterval(intermittency,nameof(intermittency));
        if((type==ScenePrecipitationType.None)!=(intensity==0)){
            throw new ArgumentException("降水種別 None ⇔ 強度 0 である必要があります。",nameof(intensity));
        }
        this.Type=type;
        this.Intensity=intensity;
        this.Intermittency=intermittency;
    }

    public ScenePrecipitationType Type{get;}
    public double Intensity{get;}
    public double Intermittency{get;}

    public static PrecipitationState None{get;}=new(ScenePrecipitationType.None,0,0);
}

public readonly record struct WindState{
    public WindState(double speedMs,double directionDeg,double gustiness){
        Guard.NonNegative(speedMs,nameof(speedMs));
        Guard.Direction(directionDeg,nameof(directionDeg));
        Guard.UnitInterval(gustiness,nameof(gustiness));
        this.SpeedMs=speedMs;
        this.DirectionDeg=directionDeg;
        this.Gustiness=gustiness;
    }

    public double SpeedMs{get;}
    public double DirectionDeg{get;}
    public double Gustiness{get;}
}

public readonly record struct HazeState{
    public HazeState(HazeKind kind,double density){
        Guard.UnitInterval(density,nameof(density));
        this.Kind=kind;
        this.Density=density;
    }

    public HazeKind Kind{get;}
    public double Density{get;}
}

public sealed record SceneOrigin(SceneOriginKind Kind,ProviderId? Provider,DateTimeOffset? Start,DateTimeOffset? End);

/// <summary>描画に必要なシーン状態(SceneState.md)。</summary>
public sealed record SceneState{
    public required DateTimeOffset Time{get;init;}
    public required CelestialPosition Sun{get;init;}
    public required MoonState Moon{get;init;}
    public required DaylightPhase Daylight{get;init;}
    public required CloudState Clouds{get;init;}
    public required PrecipitationState Precipitation{get;init;}
    public required WindState Wind{get;init;}

    public required double ThunderActivity{
        get;
        init{
            Guard.UnitInterval(value,nameof(this.ThunderActivity));
            field=value;
        }
    }

    public required double FogDensity{
        get;
        init{
            Guard.UnitInterval(value,nameof(this.FogDensity));
            field=value;
        }
    }

    public required HazeState Haze{get;init;}
    public double? TemperatureC{get;init;}
    public required SceneOrigin Origin{get;init;}
}
