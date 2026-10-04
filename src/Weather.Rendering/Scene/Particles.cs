using SkiaSharp;
using Weather.Scene;

namespace Weather.Rendering.Scene;

/// <summary>
/// 降水のパーティクル(Rendering.md「パーティクル」)。成分ごとの配列を固定容量で事前確保し、フレームごとにメモリ確保しない。
/// 雨は線分(DrawPoints)、雪・あられは事前生成したスプライト(DrawAtlas)。奥行き 3 層の視差。
/// </summary>
internal sealed class PrecipitationLayer:ISceneLayer{
    private const int Capacity=3000;
    private readonly float[] x=new float[Capacity];
    private readonly float[] y=new float[Capacity];
    private readonly float[] depth=new float[Capacity];
    private readonly float[] phase=new float[Capacity];
    private readonly bool[] alive=new bool[Capacity];
    //描画用バッファは品質段階の上限で確保し、余りは画面外の退化した図形で埋める(フレームごとに確保しない)
    private SKPoint[] lines=[];
    private SKRect[] sprites=[];
    private SKRotationScaleMatrix[] transforms=[];
    private readonly SKPaint rainPaint=new(){IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeCap=SKStrokeCap.Round};
    private readonly SKPaint spritePaint=new(){IsAntialias=true};
    private readonly Random random=new(20261004);
    private SKImage? flake;
    private double spawnCarry;

    public int ActiveCount{get;private set;}

    public void Draw(SceneFrame frame){
        var s=frame.State;
        var type=s.PrecipitationType;
        var target=this.TargetCount(frame);
        var dt=(float)frame.DeltaSeconds;
        if(frame.Options.ReduceMotion){
            dt*=0.2f;
        }
        var windX=frame.WindDrift*frame.Width*0.004f;
        var isSnow=type is ScenePrecipitationType.Snow;
        var isMixed=type is ScenePrecipitationType.RainAndSnow;
        var isIce=type is ScenePrecipitationType.Hail or ScenePrecipitationType.IcePellets;

        //発生(目標数に向けて補充)
        var deficit=target-this.ActiveCount;
        if(deficit>0){
            this.spawnCarry+=Math.Min(deficit,target*frame.DeltaSeconds*2+1);
            while(this.spawnCarry>=1){
                this.spawnCarry-=1;
                this.Spawn(frame,false);
            }
        }

        this.EnsureBuffers(Math.Max(frame.Quality.MaxRain,frame.Quality.MaxSnow));
        var lineCount=0;
        var spriteCount=0;
        var active=0;
        var ground=frame.HorizonY+frame.Height*0.12f;
        for(var i=0;i<Capacity;i++){
            if(!this.alive[i]){
                continue;
            }
            var d=this.depth[i];
            var snowLike=isSnow||(isMixed&&i%2==0);
            float vy;
            float vx;
            if(snowLike){
                vy=(50+70*d)*frame.Height/800;
                vx=windX*0.6f+(float)Math.Sin(frame.Time*1.3+this.phase[i])*18*d;
            }else if(isIce){
                vy=(900+500*d)*frame.Height/800;
                vx=windX*0.5f;
            }else{
                vy=(700+600*d)*frame.Height/800;
                vx=windX;
            }
            this.x[i]+=vx*dt;
            this.y[i]+=vy*dt;
            if(this.y[i]>ground+frame.Height*0.1f*d||this.x[i]<-40||this.x[i]>frame.Width+40){
                if(active+1>target){
                    this.alive[i]=false;
                    continue;
                }
                this.Respawn(i,frame,true);
            }
            active++;
            if(lineCount>=this.sprites.Length||spriteCount>=this.sprites.Length){
                continue;
            }
            if(snowLike||isIce){
                var size=(1.5f+3.5f*d)*frame.Height/800;
                if(isIce){
                    size*=0.7f;
                }
                this.sprites[spriteCount]=new SKRect(0,0,32,32);
                this.transforms[spriteCount]=SKRotationScaleMatrix.Create(size/16,0,this.x[i],this.y[i],16,16);
                spriteCount++;
            }else{
                var length=vy*0.018f;
                this.lines[lineCount*2]=new SKPoint(this.x[i],this.y[i]);
                this.lines[lineCount*2+1]=new SKPoint(this.x[i]-vx*0.018f,this.y[i]-length);
                lineCount++;
            }
        }
        this.ActiveCount=active;
        for(var i=lineCount;i<this.sprites.Length;i++){
            this.lines[i*2]=Hidden;
            this.lines[i*2+1]=Hidden;
        }
        for(var i=spriteCount;i<this.sprites.Length;i++){
            this.sprites[i]=new SKRect(0,0,1,1);
            this.transforms[i]=SKRotationScaleMatrix.Create(0,0,Hidden.X,Hidden.Y,0,0);
        }

        var light=ColorMath.Lerp(new SKColor(0x8a,0x96,0xb0),new SKColor(0xd6,0xe2,0xf0),s.Daylight);
        if(lineCount>0){
            this.rainPaint.Color=light.WithAlpha((byte)(90+110*s.PrecipitationIntensity));
            this.rainPaint.StrokeWidth=Math.Max(1,frame.Height/700f);
            frame.Canvas.DrawPoints(SKPointMode.Lines,this.lines,this.rainPaint);
        }
        if(spriteCount>0){
            this.flake??=CreateFlake();
            this.spritePaint.Color=SKColors.White.WithAlpha((byte)(200+55*s.Daylight));
            frame.Canvas.DrawAtlas(this.flake,this.sprites,this.transforms,new SKSamplingOptions(SKFilterMode.Linear),this.spritePaint);
        }
    }

    /// <summary>目標の粒子数 = 強度 × 品質段階の上限 × 断続性ゲート。</summary>
    internal int TargetCount(SceneFrame frame){
        var s=frame.State;
        if(s.PrecipitationType==ScenePrecipitationType.None||s.PrecipitationIntensity<=0){
            return 0;
        }
        var max=frame.Quality.MaxRain;
        if(s.PrecipitationType==ScenePrecipitationType.Snow){
            max=frame.Quality.MaxSnow;
        }
        var gate=1d;
        if(s.Intermittency>0.01){
            //シード固定の低周波ノイズで「降る時間」と「止む時間」を作る
            var n=DeterministicNoise.Smooth(frame.Time*0.05+11);
            gate=Math.Clamp((n-s.Intermittency+0.12)/0.24,0,1);
        }
        return (int)Math.Min(Capacity,max*s.PrecipitationIntensity*gate);
    }

    private static readonly SKPoint Hidden=new(-1000,-1000);

    private void EnsureBuffers(int size){
        if(this.sprites.Length==size){
            return;
        }
        this.lines=new SKPoint[size*2];
        this.sprites=new SKRect[size];
        this.transforms=new SKRotationScaleMatrix[size];
    }

    private void Spawn(SceneFrame frame,bool top){
        for(var i=0;i<Capacity;i++){
            if(!this.alive[i]){
                this.Respawn(i,frame,top);
                return;
            }
        }
    }

    private void Respawn(int i,SceneFrame frame,bool top){
        this.alive[i]=true;
        this.depth[i]=(float)this.random.NextDouble();
        this.phase[i]=(float)(this.random.NextDouble()*Math.PI*2);
        this.x[i]=(float)(this.random.NextDouble()*(frame.Width+80)-40);
        if(top){
            this.y[i]=-(float)(this.random.NextDouble()*frame.Height*0.1);
        }else{
            this.y[i]=(float)(this.random.NextDouble()*frame.HorizonY);
        }
    }

    private static SKImage CreateFlake(){
        using var surface=SKSurface.Create(new SKImageInfo(32,32,SKColorType.Rgba8888,SKAlphaType.Premul));
        using var shader=SKShader.CreateRadialGradient(new SKPoint(16,16),16,[SKColors.White,SKColors.White.WithAlpha(0)],[0.25f,1],SKShaderTileMode.Clamp);
        using var paint=new SKPaint{Shader=shader,IsAntialias=true};
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawCircle(16,16,16,paint);
        return surface.Snapshot();
    }

    public void Dispose(){
        this.rainPaint.Dispose();
        this.spritePaint.Dispose();
        this.flake?.Dispose();
    }
}

/// <summary>
/// 稲光(Rendering.md「稲光とアクセシビリティ」)。経路はシード固定の中点変位法。
/// 閃光は 1 秒に 3 回以下。ReduceFlashes では全画面の閃光を抑える。
/// </summary>
internal sealed class LightningLayer:ISceneLayer{
    public const int MaxFlashesPerSecond=3;
    private readonly SKPaint glow=new(){IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeCap=SKStrokeCap.Round};
    private readonly SKPaint core=new(){IsAntialias=true,Style=SKPaintStyle.Stroke,StrokeCap=SKStrokeCap.Round};
    private readonly SKPaint flashPaint=new();
    private readonly Queue<double> recentFlashes=new();
    private double nextStrike=-1;
    private double strikeStart=-10;
    private int strikeIndex;
    private SKPath? bolt;

    public int FlashCount{get;private set;}

    /// <summary>雲の照り返し用の閃光の明るさ(0..1)を返す。</summary>
    public double Update(SceneFrame frame){
        var s=frame.State;
        var t=frame.Time;
        while(this.recentFlashes.Count>0&&t-this.recentFlashes.Peek()>1){
            this.recentFlashes.Dequeue();
        }
        if(s.Thunder<0.05||s.PrecipitationType==ScenePrecipitationType.None){
            this.nextStrike=-1;
            return 0;
        }
        if(this.nextStrike<0){
            this.nextStrike=t+this.Interval(s.Thunder);
        }
        if(t>=this.nextStrike&&this.recentFlashes.Count<MaxFlashesPerSecond){
            this.strikeStart=t;
            this.strikeIndex++;
            this.recentFlashes.Enqueue(t);
            this.FlashCount++;
            this.bolt?.Dispose();
            this.bolt=Bolt(frame,this.strikeIndex);
            this.nextStrike=t+this.Interval(s.Thunder);
        }
        var age=t-this.strikeStart;
        if(age<0||age>0.35){
            return 0;
        }
        //2 段の明滅(合計で 1 回の閃光として数える)
        var pulse=Math.Exp(-age*14);
        if(age>0.12){
            pulse=0.6*Math.Exp(-(age-0.12)*12);
        }
        if(frame.Options.ReduceFlashes||frame.Options.ReduceMotion){
            pulse*=0.25;
        }
        return Math.Clamp(pulse,0,1);
    }

    public void Draw(SceneFrame frame){
        var flash=frame.Flash;
        if(flash<=0.01){
            return;
        }
        if(this.bolt is not null){
            this.glow.Color=new SKColor(0xb8,0xc4,0xff,(byte)(120*flash));
            this.glow.StrokeWidth=frame.Height/150f;
            this.core.Color=new SKColor(0xff,0xff,0xff,(byte)(255*flash));
            this.core.StrokeWidth=frame.Height/600f;
            frame.Canvas.DrawPath(this.bolt,this.glow);
            frame.Canvas.DrawPath(this.bolt,this.core);
        }
        var maxAlpha=90;
        if(frame.Options.ReduceFlashes){
            maxAlpha=20;
        }
        this.flashPaint.Color=new SKColor(0xe0,0xe6,0xff,(byte)(maxAlpha*flash));
        frame.Canvas.DrawRect(0,0,frame.Width,frame.Height,this.flashPaint);
    }

    /// <summary>雷活動度に比例した間隔(活動度 1 で平均約 3 秒)。</summary>
    private double Interval(double thunder){
        var mean=3/Math.Max(thunder,0.05);
        return mean*(0.4+1.2*DeterministicNoise.Hash(this.strikeIndex,99));
    }

    internal static SKPath Bolt(SceneFrame frame,int seed){
        var startX=(float)(frame.Width*(0.15+0.7*DeterministicNoise.Hash(seed,1)));
        var points=new List<SKPoint>{new(startX,frame.Height*0.08f),new(startX+(float)((DeterministicNoise.Hash(seed,2)-0.5)*frame.Width*0.2),frame.HorizonY)};
        var offset=frame.Width*0.08f;
        for(var level=0;level<6;level++){
            var next=new List<SKPoint>(points.Count*2);
            for(var i=0;i+1<points.Count;i++){
                var a=points[i];
                var b=points[i+1];
                var mid=new SKPoint((a.X+b.X)/2+(float)((DeterministicNoise.Hash(seed*131+level*17+i,3)-0.5)*offset),(a.Y+b.Y)/2);
                next.Add(a);
                next.Add(mid);
            }
            next.Add(points[^1]);
            points=next;
            offset*=0.55f;
        }
        using var builder=new SKPathBuilder();
        builder.MoveTo(points[0]);
        for(var i=1;i<points.Count;i++){
            builder.LineTo(points[i]);
        }
        return builder.Detach();
    }

    public void Dispose(){
        this.glow.Dispose();
        this.core.Dispose();
        this.flashPaint.Dispose();
        this.bolt?.Dispose();
    }
}
