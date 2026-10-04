using SkiaSharp;
using Weather.Core;

namespace Weather.Rendering.Map;

/// <summary>
/// 風の粒子(Windy・earth.nullschool と同じ表現)。粒子は画面座標で持ち、各フレームでその地点の風(U・V)で動かす。
/// 軌跡は画面外のサーフェスに描き、毎フレーム薄めて残像にする。
/// 日本周辺(マスクの中)は気象庁の海上分布予報から作った風(japan)だけで動かし、GFS(global)を使わない(方針 5)。
/// 風の値がない場所(マスクの中の陸上・海上分布予報の範囲外、GFS の欠損)では粒子を生まない・入ったら消す。
/// メモリ確保: 粒子と線分のバッファは数が変わったときだけ作り直す。
/// </summary>
internal sealed class WindParticleLayer(JapanMaskTexture mask):IDisposable{
    /// <summary>風速 1 m/s あたり、1 秒に何画素(論理画素)動くか。</summary>
    public const float SpeedScale=3.2f;
    private const int MinAge=40;
    private const int MaxAge=110;

    private readonly SKPaint line=new(){IsAntialias=true,Color=SKColors.White.WithAlpha(215),StrokeCap=SKStrokeCap.Round};
    private readonly SKPaint fade=new(){BlendMode=SKBlendMode.DstIn,Color=SKColors.Black.WithAlpha(232)};
    private readonly SKPaint blit=new();
    private float[] x=[];
    private float[] y=[];
    private int[] age=[];
    private int[] maxAge=[];
    private SKPoint[] segments=[];
    private SKSurface? trails;
    private SKSizeI trailsSize;
    private GRRecordingContext? trailsContext;
    private int seed=1;
    private (MapView View,WindField? Global,WindField? Japan)? last;

    private static readonly SKPoint Hidden=new(-1000,-1000);

    public int Count=>this.x.Length;

    /// <summary>粒子の数(品質段階と画面の広さで決める)。</summary>
    public static int CountFor(QualityTier tier,SKSize size,float pixelRatio){
        var area=size.Width*size.Height/(pixelRatio*pixelRatio);
        var perMegapixel=tier switch{
            QualityTier.Low=>4_000,
            QualityTier.Medium=>8_000,
            _=>13_000,
        };
        return Math.Clamp((int)(area/1_000_000*perMegapixel),300,6_000);
    }

    public void Reset(){
        this.last=null;
        this.trails?.Canvas.Clear(SKColors.Transparent);
    }

    public void Draw(SKCanvas canvas,MapView view,SKSize size,WindField? global,WindField? japan,int count,double deltaSeconds,GRRecordingContext? context){
        this.EnsureParticles(count,size);
        this.EnsureSurface(size,context);
        if(this.trails is null){
            return;
        }
        var trailCanvas=this.trails.Canvas;
        //カメラ・風が変われば軌跡を消し、粒子を配り直す
        if(this.last is not {} l||l.View!=view||!ReferenceEquals(l.Global,global)||!ReferenceEquals(l.Japan,japan)){
            trailCanvas.Clear(SKColors.Transparent);
            for(var i=0;i<this.x.Length;i++){
                this.Respawn(i,size);
                this.age[i]=(int)(DeterministicNoise.Hash(i,this.seed)*this.maxAge[i]);
            }
            this.last=(view,global,japan);
        }
        trailCanvas.DrawPaint(this.fade);
        var dt=(float)Math.Clamp(deltaSeconds,0,0.1);
        var scale=SpeedScale*view.PixelRatio*dt;
        var n=0;
        for(var i=0;i<this.x.Length;i++){
            var px=this.x[i];
            var py=this.y[i];
            if(++this.age[i]>this.maxAge[i]){
                this.Respawn(i,size);
                continue;
            }
            var geo=view.ToGeo(new SKPoint(px,py),size);
            var (u,v)=Sample(global,japan,mask.Contains(geo.Latitude,geo.Longitude),geo.Latitude,geo.Longitude);
            if(float.IsNaN(u)||float.IsNaN(v)){
                this.Respawn(i,size);
                continue;
            }
            var nx=px+u*scale;
            var ny=py-v*scale;
            if(nx<0||ny<0||nx>size.Width||ny>size.Height){
                this.Respawn(i,size);
                continue;
            }
            this.segments[n++]=new SKPoint(px,py);
            this.segments[n++]=new SKPoint(nx,ny);
            this.x[i]=nx;
            this.y[i]=ny;
        }
        this.line.StrokeWidth=1.2f*view.PixelRatio;
        if(n>0){
            //DrawPoints は配列全体を描くため、使わない枠は画面外の長さ 0 の線にする(毎フレーム配列を作らない)
            for(var i=n;i<this.segments.Length;i++){
                this.segments[i]=Hidden;
            }
            trailCanvas.DrawPoints(SKPointMode.Lines,this.segments,this.line);
        }
        this.trails.Draw(canvas,0,0,this.blit);
    }

    /// <summary>日本周辺は気象庁の風だけ、それ以外は GFS だけを使う(どちらもなければ NaN)。</summary>
    private static (float U,float V) Sample(WindField? global,WindField? japan,bool inJapan,double latitude,double longitude){
        if(inJapan){
            if(japan is null){
                return (float.NaN,float.NaN);
            }
            //気象庁の区画ごとの値をそのまま使う(補間しない)
            return japan.SampleNearest(latitude,longitude);
        }
        if(global is null){
            return (float.NaN,float.NaN);
        }
        return global.Sample(latitude,longitude);
    }

    private void Respawn(int i,SKSize size){
        this.seed++;
        this.x[i]=(float)(DeterministicNoise.Hash(i,this.seed)*size.Width);
        this.y[i]=(float)(DeterministicNoise.Hash(i+7919,this.seed)*size.Height);
        this.age[i]=0;
    }

    private void EnsureParticles(int count,SKSize size){
        if(this.x.Length==count){
            return;
        }
        this.x=new float[count];
        this.y=new float[count];
        this.age=new int[count];
        this.maxAge=new int[count];
        this.segments=new SKPoint[count*2];
        for(var i=0;i<count;i++){
            this.maxAge[i]=MinAge+(int)(DeterministicNoise.Hash(i,97)*(MaxAge-MinAge));
            this.Respawn(i,size);
        }
        this.last=null;
    }

    private void EnsureSurface(SKSize size,GRRecordingContext? context){
        var target=new SKSizeI((int)Math.Ceiling(size.Width),(int)Math.Ceiling(size.Height));
        if(this.trails is not null&&this.trailsSize==target&&ReferenceEquals(this.trailsContext,context)){
            return;
        }
        this.trails?.Dispose();
        var info=new SKImageInfo(target.Width,target.Height,SKColorType.Rgba8888,SKAlphaType.Premul);
        if(context is GRContext gpu){
            this.trails=SKSurface.Create(gpu,false,info);
        }
        this.trails??=SKSurface.Create(info);
        this.trailsSize=target;
        this.trailsContext=context;
        this.last=null;
    }

    public void Dispose(){
        this.trails?.Dispose();
        this.line.Dispose();
        this.fade.Dispose();
        this.blit.Dispose();
    }
}
