using SkiaSharp;

namespace Weather.Rendering;

public enum QualityTier{Low,Medium,High}

/// <summary>品質段階ごとの上限(Rendering.md「性能」)。</summary>
public readonly record struct QualitySettings(float CloudScale,int Octaves,int MaxRain,int MaxSnow,bool Splashes){
    public static QualitySettings For(QualityTier tier){
        switch(tier){
            case QualityTier.Low:
                return new QualitySettings(0.25f,3,600,300,false);
            case QualityTier.High:
                return new QualitySettings(0.75f,6,3000,1500,true);
            default:
                return new QualitySettings(0.5f,4,1500,800,false);
        }
    }
}

/// <summary>描画の設定。OS の「視差効果を減らす」等は App が反映する。</summary>
public sealed class SceneRenderOptions{
    public QualityTier Tier{get;set;}=QualityTier.Medium;
    public bool AutoTier{get;set;}=true;
    public bool ReduceMotion{get;set;}
    public bool ReduceFlashes{get;set;}
}

/// <summary>フレーム時間の統計と自動品質調整(平均 20 ms 超が 3 秒で 1 段下げ、12 ms 未満が 30 秒で 1 段上げ)。</summary>
public sealed class FrameStats{
    private double slowSeconds;
    private double fastSeconds;

    public double AverageFrameMs{get;private set;}=16;
    public int FrameCount{get;private set;}

    public QualityTier? Record(double frameMs,double deltaSeconds,QualityTier current){
        this.FrameCount++;
        this.AverageFrameMs+=(frameMs-this.AverageFrameMs)*0.05;
        if(this.AverageFrameMs>20){
            this.slowSeconds+=deltaSeconds;
            this.fastSeconds=0;
        }else if(this.AverageFrameMs<12){
            this.fastSeconds+=deltaSeconds;
            this.slowSeconds=0;
        }else{
            this.slowSeconds=0;
            this.fastSeconds=0;
        }
        if(this.slowSeconds>=3&&current>QualityTier.Low){
            this.slowSeconds=0;
            return current-1;
        }
        if(this.fastSeconds>=30&&current<QualityTier.High){
            this.fastSeconds=0;
            return current+1;
        }
        return null;
    }
}

/// <summary>決定的な値ノイズ(シード固定)。断続性のゲートや稲妻の経路に使う。</summary>
public static class DeterministicNoise{
    public static double Hash(double x){
        var v=Math.Sin(x*12.9898+78.233)*43758.5453;
        return v-Math.Floor(v);
    }

    public static double Hash(int i,int seed){
        unchecked{
            var h=(uint)(i*374761393+seed*668265263);
            h=(h^(h>>13))*1274126177;
            h^=h>>16;
            return h/(double)uint.MaxValue;
        }
    }

    /// <summary>1 次元の滑らかな値ノイズ(0..1)。</summary>
    public static double Smooth(double x){
        var i=Math.Floor(x);
        var f=x-i;
        var u=f*f*(3-2*f);
        return Hash(i)*(1-u)+Hash(i+1)*u;
    }
}

/// <summary>連続値を目標に滑らかに追従させる(指数平滑。半減期で指定)。</summary>
public static class Smoothing{
    public static double Approach(double current,double target,double deltaSeconds,double halfLifeSeconds){
        if(halfLifeSeconds<=0||double.IsNaN(current)){
            return target;
        }
        var k=1-Math.Pow(0.5,deltaSeconds/halfLifeSeconds);
        return current+(target-current)*k;
    }

    public static double ApproachAngle(double current,double target,double deltaSeconds,double halfLifeSeconds){
        var delta=((target-current+540)%360)-180;
        var next=current+delta*(1-Math.Pow(0.5,deltaSeconds/Math.Max(halfLifeSeconds,1e-6)));
        next%=360;
        if(next<0){
            next+=360;
        }
        return next;
    }
}

/// <summary>書体の一元管理(SkiaSharp 4 は書体の指定が必須)。端末の CJK 書体を使い、フォントは同梱しない。</summary>
public static class FontProvider{
    private static readonly Lazy<SKTypeface> Typeface=new(static ()=>{
        var typeface=SKFontManager.Default.MatchCharacter('あ');
        if(typeface is null){
            typeface=SKFontManager.Default.MatchFamily(null)??SKTypeface.Default;
        }
        return typeface;
    });

    public static SKTypeface Default=>Typeface.Value;

    public static SKFont Create(float size){
        return new SKFont(Default,size,1,0);
    }
}

public static class ColorMath{
    public static SKColor Lerp(SKColor a,SKColor b,double t){
        var u=(float)Math.Clamp(t,0,1);
        return new SKColor(
            (byte)(a.Red+(b.Red-a.Red)*u),
            (byte)(a.Green+(b.Green-a.Green)*u),
            (byte)(a.Blue+(b.Blue-a.Blue)*u),
            (byte)(a.Alpha+(b.Alpha-a.Alpha)*u));
    }

    public static SKColor Grey(SKColor color,double amount){
        var y=(byte)(0.299*color.Red+0.587*color.Green+0.114*color.Blue);
        return Lerp(color,new SKColor(y,y,y,color.Alpha),amount);
    }

    public static SKColor Darken(SKColor color,double amount){
        return Lerp(color,new SKColor(0,0,0,color.Alpha),amount);
    }

    public static float[] Rgb(SKColor color){
        return [color.Red/255f,color.Green/255f,color.Blue/255f];
    }
}
