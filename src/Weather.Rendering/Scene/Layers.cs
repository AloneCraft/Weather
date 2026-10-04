using SkiaSharp;
using Weather.Scene;

namespace Weather.Rendering.Scene;

public interface ISceneLayer:IDisposable{
    void Draw(SceneFrame frame);
}

/// <summary>空のグラデーション(太陽高度で色を決め、雲量で灰色に寄せる)。</summary>
internal sealed class SkyLayer:ISceneLayer{
    private static readonly (double Altitude,SKColor Zenith,SKColor Horizon)[] Keys=[
        (-18,new SKColor(0x05,0x08,0x16),new SKColor(0x0b,0x10,0x26)),
        (-12,new SKColor(0x0a,0x14,0x30),new SKColor(0x1d,0x26,0x48)),
        (-6,new SKColor(0x1d,0x2b,0x5a),new SKColor(0xb0,0x5a,0x6e)),
        (0,new SKColor(0x35,0x5f,0xa8),new SKColor(0xf2,0xa0,0x60)),
        (8,new SKColor(0x3f,0x82,0xd0),new SKColor(0xbf,0xd6,0xee)),
        (30,new SKColor(0x2e,0x6c,0xcc),new SKColor(0xa8,0xcd,0xf2)),
    ];

    private readonly SKPaint paint=new(){IsAntialias=false};

    public static (SKColor Zenith,SKColor Horizon) Colors(double altitude){
        if(altitude<=Keys[0].Altitude){
            return (Keys[0].Zenith,Keys[0].Horizon);
        }
        for(var i=1;i<Keys.Length;i++){
            if(altitude<=Keys[i].Altitude){
                var t=(altitude-Keys[i-1].Altitude)/(Keys[i].Altitude-Keys[i-1].Altitude);
                return (ColorMath.Lerp(Keys[i-1].Zenith,Keys[i].Zenith,t),ColorMath.Lerp(Keys[i-1].Horizon,Keys[i].Horizon,t));
            }
        }
        return (Keys[^1].Zenith,Keys[^1].Horizon);
    }

    public void Draw(SceneFrame frame){
        var s=frame.State;
        var (zenith,horizon)=Colors(s.SunAltitude);
        var overcast=s.CloudCover*s.CloudCover;
        zenith=ColorMath.Darken(ColorMath.Grey(zenith,overcast*0.8),s.CloudDarkness*overcast*0.45);
        horizon=ColorMath.Darken(ColorMath.Grey(horizon,overcast*0.8),s.CloudDarkness*overcast*0.35);
        var middle=ColorMath.Lerp(zenith,horizon,0.55);
        using var shader=SKShader.CreateLinearGradient(new SKPoint(0,0),new SKPoint(0,frame.HorizonY),[zenith,middle,horizon],[0,0.6f,1],SKShaderTileMode.Clamp);
        this.paint.Shader=shader;
        frame.Canvas.DrawRect(0,0,frame.Width,frame.Height,this.paint);
        this.paint.Shader=null;
    }

    public void Dispose(){
        this.paint.Dispose();
    }
}

/// <summary>星(薄明で減衰、雲で隠れる)。位置は起動時に決定的に生成する。</summary>
internal sealed class StarsLayer:ISceneLayer{
    private const int Count=260;
    private readonly float[] x=new float[Count];
    private readonly float[] y=new float[Count];
    private readonly float[] size=new float[Count];
    private readonly SKPaint paint=new(){IsAntialias=true,Color=SKColors.White};

    public StarsLayer(){
        for(var i=0;i<Count;i++){
            this.x[i]=(float)DeterministicNoise.Hash(i,1);
            this.y[i]=(float)Math.Pow(DeterministicNoise.Hash(i,2),1.6);
            this.size[i]=(float)(0.6+DeterministicNoise.Hash(i,3)*1.4);
        }
    }

    public void Draw(SceneFrame frame){
        var s=frame.State;
        var night=Math.Clamp((-s.SunAltitude-4)/10,0,1);
        var visible=night*Math.Pow(1-s.CloudCover,2)*(1-s.Fog);
        if(visible<0.02){
            return;
        }
        for(var i=0;i<Count;i++){
            var twinkle=1d;
            if(!frame.Options.ReduceMotion){
                twinkle=0.7+0.3*DeterministicNoise.Smooth(frame.Time*1.5+i*7.3);
            }
            this.paint.Color=SKColors.White.WithAlpha((byte)(255*visible*twinkle*(0.4+0.6*this.size[i]/2)));
            frame.Canvas.DrawCircle(this.x[i]*frame.Width,this.y[i]*frame.HorizonY*0.95f,this.size[i],this.paint);
        }
    }

    public void Dispose(){
        this.paint.Dispose();
    }
}

/// <summary>太陽と月(雲量で減光、月は位相を描く)。</summary>
internal sealed class CelestialLayer:ISceneLayer{
    private readonly SKPaint paint=new(){IsAntialias=true};

    public void Draw(SceneFrame frame){
        var s=frame.State;
        var radius=Math.Min(frame.Width,frame.Height)*0.035f;
        var occlusion=(float)(1-s.CloudCover*0.85);
        if(s.SunAltitude>-3){
            var p=frame.SkyPoint(s.SunAltitude,s.SunAzimuth);
            var warm=ColorMath.Lerp(new SKColor(0xff,0xf6,0xd8),new SKColor(0xff,0x9a,0x50),s.Golden);
            using(var glow=SKShader.CreateRadialGradient(p,radius*6,[warm.WithAlpha((byte)(150*occlusion)),warm.WithAlpha(0)],[0,1],SKShaderTileMode.Clamp)){
                this.paint.Shader=glow;
                frame.Canvas.DrawCircle(p,radius*6,this.paint);
                this.paint.Shader=null;
            }
            this.paint.Color=warm.WithAlpha((byte)(255*Math.Clamp(occlusion+0.1f,0,1)));
            frame.Canvas.DrawCircle(p,radius,this.paint);
        }
        if(s.MoonAltitude>-3&&s.MoonIllumination>0.02){
            var p=frame.SkyPoint(s.MoonAltitude,s.MoonAzimuth);
            var r=radius*0.8f;
            var alpha=(byte)(255*occlusion*Math.Clamp(1-s.Daylight*0.7,0.2,1));
            this.paint.Color=new SKColor(0x2a,0x2e,0x3a,(byte)(alpha*0.5));
            frame.Canvas.DrawCircle(p,r,this.paint);
            using var lit=MoonPath(p,r,s.MoonPhase,s.MoonIllumination);
            this.paint.Color=new SKColor(0xf1,0xef,0xe2,alpha);
            frame.Canvas.DrawPath(lit,this.paint);
        }
    }

    /// <summary>位相の輝面(北半球の見え方: 満ちていく月は右側が光る)。</summary>
    internal static SKPath MoonPath(SKPoint center,float r,double phase,double illumination){
        var waxing=phase<0.5;
        using var halfBuilder=new SKPathBuilder();
        var rect=new SKRect(center.X-r,center.Y-r,center.X+r,center.Y+r);
        if(waxing){
            halfBuilder.AddArc(rect,-90,180);
        }else{
            halfBuilder.AddArc(rect,90,180);
        }
        halfBuilder.Close();
        using var half=halfBuilder.Detach();
        var ellipseWidth=(float)(r*Math.Abs(1-2*illumination));
        using var ellipseBuilder=new SKPathBuilder();
        ellipseBuilder.AddOval(new SKRect(center.X-ellipseWidth,center.Y-r,center.X+ellipseWidth,center.Y+r));
        using var ellipse=ellipseBuilder.Detach();
        if(illumination<0.5){
            return half.Op(ellipse,SKPathOp.Difference)??new SKPath();
        }
        return half.Op(ellipse,SKPathOp.Union)??new SKPath();
    }

    public void Dispose(){
        this.paint.Dispose();
    }
}

/// <summary>地平の稜線(起動時・サイズ変更時に生成して再利用)。</summary>
internal sealed class LandscapeLayer:ISceneLayer{
    private readonly SKPaint paint=new(){IsAntialias=true};
    private SKSizeI cachedSize;
    private SKPath? far;
    private SKPath? near;

    public void Draw(SceneFrame frame){
        if(frame.Size!=this.cachedSize||this.far is null||this.near is null){
            this.far?.Dispose();
            this.near?.Dispose();
            this.far=Ridge(frame,frame.HorizonY-frame.Height*0.04f,frame.Height*0.05f,11);
            this.near=Ridge(frame,frame.HorizonY+frame.Height*0.02f,frame.Height*0.04f,23);
            this.cachedSize=frame.Size;
        }
        var s=frame.State;
        var day=s.Daylight;
        var farColor=ColorMath.Lerp(new SKColor(0x0e,0x12,0x1e),new SKColor(0x5b,0x6c,0x80),day);
        var nearColor=ColorMath.Lerp(new SKColor(0x07,0x09,0x10),new SKColor(0x2c,0x3a,0x34),day);
        farColor=ColorMath.Lerp(farColor,new SKColor(0xb8,0xbe,0xc6),s.Fog*0.7);
        nearColor=ColorMath.Lerp(nearColor,new SKColor(0x9a,0xa2,0xaa),s.Fog*0.5);
        this.paint.Color=farColor;
        frame.Canvas.DrawPath(this.far,this.paint);
        this.paint.Color=nearColor;
        frame.Canvas.DrawPath(this.near,this.paint);
    }

    private static SKPath Ridge(SceneFrame frame,float baseY,float amplitude,int seed){
        using var builder=new SKPathBuilder();
        builder.MoveTo(0,frame.Height);
        const int steps=48;
        for(var i=0;i<=steps;i++){
            var x=frame.Width*i/steps;
            var n=DeterministicNoise.Smooth(i*0.35+seed)*0.7+DeterministicNoise.Smooth(i*0.9+seed*3)*0.3;
            builder.LineTo(x,baseY-(float)(n*amplitude));
        }
        builder.LineTo(frame.Width,frame.Height);
        builder.Close();
        return builder.Detach();
    }

    public void Dispose(){
        this.paint.Dispose();
        this.far?.Dispose();
        this.near?.Dispose();
    }
}

/// <summary>もや・煙・砂塵の色かぶり。</summary>
internal sealed class HazeLayer:ISceneLayer{
    private readonly SKPaint paint=new();

    public void Draw(SceneFrame frame){
        var s=frame.State;
        if(s.HazeKind==HazeKind.None||s.HazeDensity<0.01){
            return;
        }
        SKColor color;
        switch(s.HazeKind){
            case HazeKind.Smoke:
                color=new SKColor(0x9a,0x80,0x6a);
                break;
            case HazeKind.Dust:
                color=new SKColor(0xc8,0xa8,0x78);
                break;
            default:
                color=new SKColor(0xc8,0xcc,0xd2);
                break;
        }
        color=ColorMath.Darken(color,(1-s.Daylight)*0.8);
        this.paint.Color=color.WithAlpha((byte)(160*s.HazeDensity));
        frame.Canvas.DrawRect(0,0,frame.Width,frame.Height,this.paint);
    }

    public void Dispose(){
        this.paint.Dispose();
    }
}
