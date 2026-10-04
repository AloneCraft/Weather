using Weather.Core;

namespace Weather.Scene;

/// <summary>
/// 時間軸の操作(スクラブ)用。区間ごとの SceneState を事前計算し、連続値を区間の中点どうしで線形補間する。
/// 分類値(降水種別など)は近い方を使い、切り替えのクロスフェードは描画側が行う。
/// </summary>
public sealed class SceneTimeline{
    private readonly List<SceneState> keyframes;
    private readonly SceneConverter converter;
    private readonly Forecast forecast;

    public SceneTimeline(Forecast forecast,SceneConverter converter){
        ArgumentNullException.ThrowIfNull(forecast);
        ArgumentNullException.ThrowIfNull(converter);
        this.forecast=forecast;
        this.converter=converter;
        this.keyframes=[];
        foreach(var point in forecast.TimeSeries){
            var middle=point.Start+point.Duration/2;
            this.keyframes.Add(converter.Convert(forecast,middle));
        }
    }

    public DateTimeOffset? Start{
        get{
            if(this.forecast.TimeSeries.Count==0){
                return null;
            }
            return this.forecast.TimeSeries[0].Start;
        }
    }

    public DateTimeOffset? End{
        get{
            if(this.forecast.TimeSeries.Count==0){
                return null;
            }
            return this.forecast.TimeSeries[^1].End;
        }
    }

    public SceneState At(DateTimeOffset time){
        if(this.keyframes.Count<2){
            return this.converter.Convert(this.forecast,time);
        }
        var exact=this.converter.Convert(this.forecast,time);
        for(var i=0;i+1<this.keyframes.Count;i++){
            var a=this.keyframes[i];
            var b=this.keyframes[i+1];
            if(a.Time<=time&&time<=b.Time){
                var span=(b.Time-a.Time).TotalSeconds;
                var t=0d;
                if(span>0){
                    t=(time-a.Time).TotalSeconds/span;
                }
                return Blend(exact,a,b,t);
            }
        }
        return exact;
    }

    /// <summary>天体は正確な値、天気の連続値は補間、分類値は近い方。</summary>
    internal static SceneState Blend(SceneState exact,SceneState a,SceneState b,double t){
        var near=a;
        if(t>=0.5){
            near=b;
        }
        var precipitationType=near.Precipitation.Type;
        var intensity=Lerp(a.Precipitation.Intensity,b.Precipitation.Intensity,t);
        if(precipitationType==ScenePrecipitationType.None){
            intensity=0;
        }else if(intensity<=0){
            intensity=near.Precipitation.Intensity;
        }
        var thunder=Lerp(a.ThunderActivity,b.ThunderActivity,t);
        if(precipitationType==ScenePrecipitationType.None){
            thunder=0;
        }
        return exact with{
            Clouds=new CloudState(Lerp(a.Clouds.Cover,b.Clouds.Cover,t),Lerp(a.Clouds.Darkness,b.Clouds.Darkness,t),near.Clouds.Type),
            Precipitation=new PrecipitationState(precipitationType,intensity,Lerp(a.Precipitation.Intermittency,b.Precipitation.Intermittency,t)),
            Wind=new WindState(Lerp(a.Wind.SpeedMs,b.Wind.SpeedMs,t),LerpAngle(a.Wind.DirectionDeg,b.Wind.DirectionDeg,t),Lerp(a.Wind.Gustiness,b.Wind.Gustiness,t)),
            ThunderActivity=thunder,
            FogDensity=Lerp(a.FogDensity,b.FogDensity,t),
            Haze=new HazeState(near.Haze.Kind,Lerp(a.Haze.Density,b.Haze.Density,t)),
            TemperatureC=LerpNullable(a.TemperatureC,b.TemperatureC,t),
            Origin=near.Origin,
        };
    }

    private static double Lerp(double a,double b,double t){
        return a+(b-a)*Math.Clamp(t,0,1);
    }

    private static double? LerpNullable(double? a,double? b,double t){
        if(a is {} x&&b is {} y){
            return Lerp(x,y,t);
        }
        return a??b;
    }

    private static double LerpAngle(double a,double b,double t){
        var delta=((b-a+540)%360)-180;
        return GeoMath.NormalizeDegrees(a+delta*Math.Clamp(t,0,1));
    }
}
