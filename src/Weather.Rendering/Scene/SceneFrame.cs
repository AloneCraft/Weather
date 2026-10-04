using SkiaSharp;
using Weather.Scene;

namespace Weather.Rendering.Scene;

/// <summary>描画中の値(目標の SceneState に滑らかに追従する)。</summary>
public sealed class DisplayState{
    public double SunAltitude;
    public double SunAzimuth=180;
    public double MoonAltitude=-90;
    public double MoonAzimuth=180;
    public double MoonPhase=0.5;
    public double MoonIllumination=1;
    public double CloudCover;
    public double CloudDarkness;
    public SceneCloudType CloudType;
    public ScenePrecipitationType PrecipitationType;
    public double PrecipitationIntensity;
    public double Intermittency;
    public double WindSpeed;
    public double WindDirection=270;
    public double Gustiness;
    public double Thunder;
    public double Fog;
    public HazeKind HazeKind;
    public double HazeDensity;
    public bool Initialized;

    /// <summary>昼の度合い(太陽高度 −18°〜+6° を 0..1 に)。</summary>
    public double Daylight=>Math.Clamp((this.SunAltitude+18)/24,0,1);

    /// <summary>朝夕の暖色の度合い(太陽高度 −6°〜+10° で最大)。</summary>
    public double Golden=>Math.Clamp(1-Math.Abs(this.SunAltitude-2)/10,0,1);

    public void Snap(SceneState s){
        this.SunAltitude=s.Sun.AltitudeDeg;
        this.SunAzimuth=s.Sun.AzimuthDeg;
        this.MoonAltitude=s.Moon.Position.AltitudeDeg;
        this.MoonAzimuth=s.Moon.Position.AzimuthDeg;
        this.MoonPhase=s.Moon.Phase;
        this.MoonIllumination=s.Moon.Illumination;
        this.CloudCover=s.Clouds.Cover;
        this.CloudDarkness=s.Clouds.Darkness;
        this.CloudType=s.Clouds.Type;
        this.PrecipitationType=s.Precipitation.Type;
        this.PrecipitationIntensity=s.Precipitation.Intensity;
        this.Intermittency=s.Precipitation.Intermittency;
        this.WindSpeed=s.Wind.SpeedMs;
        this.WindDirection=s.Wind.DirectionDeg;
        this.Gustiness=s.Wind.Gustiness;
        this.Thunder=s.ThunderActivity;
        this.Fog=s.FogDensity;
        this.HazeKind=s.Haze.Kind;
        this.HazeDensity=s.Haze.Density;
        this.Initialized=true;
    }

    /// <summary>天気の値は半減期 0.8 秒、天体は 0.3 秒で追従する。降水種別が変わるときは強度を 0 に落としてから切り替える。</summary>
    public void Approach(SceneState s,double dt){
        const double weather=0.8;
        const double sky=0.3;
        this.SunAltitude=Smoothing.Approach(this.SunAltitude,s.Sun.AltitudeDeg,dt,sky);
        this.SunAzimuth=Smoothing.ApproachAngle(this.SunAzimuth,s.Sun.AzimuthDeg,dt,sky);
        this.MoonAltitude=Smoothing.Approach(this.MoonAltitude,s.Moon.Position.AltitudeDeg,dt,sky);
        this.MoonAzimuth=Smoothing.ApproachAngle(this.MoonAzimuth,s.Moon.Position.AzimuthDeg,dt,sky);
        this.MoonPhase=s.Moon.Phase;
        this.MoonIllumination=s.Moon.Illumination;
        this.CloudCover=Smoothing.Approach(this.CloudCover,s.Clouds.Cover,dt,weather);
        this.CloudDarkness=Smoothing.Approach(this.CloudDarkness,s.Clouds.Darkness,dt,weather);
        this.CloudType=s.Clouds.Type;
        if(this.PrecipitationType!=s.Precipitation.Type){
            this.PrecipitationIntensity=Smoothing.Approach(this.PrecipitationIntensity,0,dt,weather/2);
            if(this.PrecipitationIntensity<0.02){
                this.PrecipitationType=s.Precipitation.Type;
                this.PrecipitationIntensity=0;
            }
        }else{
            this.PrecipitationIntensity=Smoothing.Approach(this.PrecipitationIntensity,s.Precipitation.Intensity,dt,weather);
        }
        this.Intermittency=Smoothing.Approach(this.Intermittency,s.Precipitation.Intermittency,dt,weather);
        this.WindSpeed=Smoothing.Approach(this.WindSpeed,s.Wind.SpeedMs,dt,weather);
        this.WindDirection=Smoothing.ApproachAngle(this.WindDirection,s.Wind.DirectionDeg,dt,weather);
        this.Gustiness=Smoothing.Approach(this.Gustiness,s.Wind.Gustiness,dt,weather);
        this.Thunder=Smoothing.Approach(this.Thunder,s.ThunderActivity,dt,weather);
        this.Fog=Smoothing.Approach(this.Fog,s.FogDensity,dt,weather);
        if(this.HazeKind!=s.Haze.Kind){
            this.HazeDensity=Smoothing.Approach(this.HazeDensity,0,dt,weather/2);
            if(this.HazeDensity<0.02){
                this.HazeKind=s.Haze.Kind;
            }
        }else{
            this.HazeDensity=Smoothing.Approach(this.HazeDensity,s.Haze.Density,dt,weather);
        }
    }
}

/// <summary>1 フレームの描画文脈。</summary>
public sealed class SceneFrame{
    //描画ごとに作り直さず、SceneRenderer が 1 つを使い回す
    public SKCanvas Canvas{get;set;}=null!;
    public SKSizeI Size{get;set;}
    public DisplayState State{get;set;}=null!;
    public double Time{get;set;}
    public double DeltaSeconds{get;set;}
    public QualitySettings Quality{get;set;}
    public SceneRenderOptions Options{get;set;}=null!;

    /// <summary>稲光の明るさ(雲の照り返しに使う)。</summary>
    public double Flash{get;set;}

    public float Width=>this.Size.Width;
    public float Height=>this.Size.Height;
    public float HorizonY=>this.Height*0.78f;

    /// <summary>南を向いた視野で、方位・高度を画面座標に写す(東 = 左、西 = 右)。</summary>
    public SKPoint SkyPoint(double altitudeDeg,double azimuthDeg){
        var x=(float)(this.Width*(0.5+(azimuthDeg-180)/160));
        var y=(float)(this.HorizonY-altitudeDeg/70*this.HorizonY);
        return new SKPoint(x,y);
    }

    /// <summary>風下へ向かう画面上の流れ(x のみ。風速に比例)。</summary>
    public float WindDrift{
        get{
            var to=(this.State.WindDirection+180)*Math.PI/180;
            return (float)(Math.Sin(to)*this.State.WindSpeed);
        }
    }
}
