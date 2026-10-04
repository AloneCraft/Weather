using System.Diagnostics;
using SkiaSharp;
using Weather.Core;
using Weather.Geo;

namespace Weather.Rendering.Map;

/// <summary>地図のピン(お気に入り・現在地)。Label は公式予報の気温など。</summary>
public readonly record struct MapPin(GeoPoint Point,string Label,bool Selected);

public sealed class MapRenderOptions{
    public QualityTier Tier{get;set;}=QualityTier.Medium;
    public bool AutoTier{get;set;}=true;

    /// <summary>「視差効果を減らす」: 粒子を動かさず、静止した矢印にする。</summary>
    public bool ReduceMotion{get;set;}
}

/// <summary>
/// 地図の描画(Rendering.md「地図」)。奥から順に:
/// 背景の地図(海・陸)→ 格子の色(GFS)→ 気象庁のタイル(日本周辺だけ)→ 予報期間外の斜線 → 等圧線 → 風の粒子 →
/// 日本周辺の矢印・観測点 → 海岸線・境界 → 地名 → ピン・選択地点。
/// 操作(カメラ・コマ・ピン)は UI のスレッド、描画は描画スレッドから呼ばれる。コマとピンは参照の差し替えで渡す。
/// </summary>
public sealed class MapRenderer:IDisposable{
    private readonly BaseMapLayer baseMap;
    private readonly ScalarFieldLayer field;
    private readonly JapanOverlay overlay;
    private readonly TileLayer tiles=new();
    private readonly IsobarLayer isobars=new();
    private readonly WindParticleLayer particles;
    private readonly ArrowLayer arrows;
    private readonly SKPaint layerPaint=new();
    private readonly SKPaint pinFill=new(){IsAntialias=true,Color=new SKColor(0x14,0x1c,0x2a,0xe6)};
    private readonly SKPaint pinText=new(){IsAntialias=true,Color=SKColors.White};
    private readonly SKPaint pinDot=new(){IsAntialias=true,Color=new SKColor(0xff,0xb3,0x4d)};
    private readonly SKPaint selection=new(){IsAntialias=true,Style=SKPaintStyle.Stroke,Color=SKColors.White};
    private readonly List<WindArrow> staticArrows=[];
    private readonly Stopwatch watch=new();
    private MapFrame? pending;
    private MapFrame? current;
    private double lastTime=double.NaN;
    private SKFont? font;

    public MapRenderer(GeoDatabase database){
        ArgumentNullException.ThrowIfNull(database);
        var mask=JapanMaskTexture.For(database);
        this.baseMap=new BaseMapLayer(database);
        this.field=new ScalarFieldLayer(mask);
        this.overlay=new JapanOverlay(mask);
        this.particles=new WindParticleLayer(mask);
        this.arrows=new ArrowLayer(mask);
        this.tiles.TileLoaded+=()=>this.RedrawRequested?.Invoke();
    }

    public MapCamera Camera{get;}=new();
    public MapRenderOptions Options{get;}=new();
    public FrameStats Stats{get;}=new();

    /// <summary>非同期の読み込み(気象庁のタイル)が終わり、描き直しが必要になった。</summary>
    public event Action? RedrawRequested;

    public MapTileLoader? TileLoader{
        get=>this.tiles.Loader;
        set=>this.tiles.Loader=value;
    }

    /// <summary>表示するコマ。次の描画で切り替える。</summary>
    public MapFrame? Frame{
        get=>Volatile.Read(ref this.pending)??this.current;
        set=>Volatile.Write(ref this.pending,value);
    }

    public IReadOnlyList<MapPin> Pins{get;set;}=[];

    public GeoPoint? Selected{get;set;}

    /// <summary>連続して描く必要があるか(風の粒子)。</summary>
    public bool NeedsAnimation=>this.Frame?.Wind is not null&&!this.Options.ReduceMotion;

    public void Render(SKCanvas canvas,SKSizeI pixelSize,double timeSeconds,GRRecordingContext? context=null){
        ArgumentNullException.ThrowIfNull(canvas);
        this.watch.Restart();
        this.ApplyPending();
        var size=new SKSize(pixelSize.Width,pixelSize.Height);
        var view=this.Camera.Snapshot();
        var delta=0.0;
        if(!double.IsNaN(this.lastTime)){
            delta=timeSeconds-this.lastTime;
        }
        this.lastTime=timeSeconds;

        this.baseMap.DrawLand(canvas,view,size);
        var frame=this.current;
        if(frame is not null){
            var opacity=0.82f;
            if(frame.Layer==FieldLayer.Clouds){
                opacity=1f;
            }
            this.field.Draw(canvas,view,size,opacity);
            if(frame.Tiles.Count>0){
                //気象庁のタイルは日本周辺だけに描く(GFS との継ぎ目をそろえる)
                canvas.SaveLayer(this.layerPaint);
                foreach(var layer in frame.Tiles){
                    this.tiles.Draw(canvas,view,size,layer);
                }
                this.overlay.ClipToJapan(canvas,view,size);
                canvas.Restore();
            }
            if(frame.Japan==JapanCoverage.OutOfRange){
                this.overlay.DrawOutOfRange(canvas,view,size);
            }
            this.isobars.Draw(canvas,view,size);
            if(frame.Wind is {} wind){
                if(this.Options.ReduceMotion){
                    this.DrawStaticWind(canvas,view,size,wind);
                }else{
                    var count=WindParticleLayer.CountFor(this.Options.Tier,size,view.PixelRatio);
                    this.particles.Draw(canvas,view,size,wind,count,delta,context);
                }
            }
            if(frame.Arrows is {} arrowSet){
                this.arrows.DrawArrows(canvas,view,size,arrowSet);
            }
            if(frame.Points is {} points&&(frame.Tiles.Count==0||view.Zoom>=6)){
                this.arrows.DrawPoints(canvas,view,size,points,MapLegends.Temperature);
            }
        }
        this.baseMap.DrawLines(canvas,view,size);
        this.baseMap.DrawLabels(canvas,view,size);
        this.DrawPins(canvas,view,size);
        this.watch.Stop();
        if(this.Options.AutoTier&&delta>0&&this.Stats.Record(this.watch.Elapsed.TotalMilliseconds,delta,this.Options.Tier) is {} tier){
            this.Options.Tier=tier;
        }
    }

    private void ApplyPending(){
        var next=Interlocked.Exchange(ref this.pending,null);
        if(next is null){
            return;
        }
        var previousTiles=this.current?.Tiles;
        this.current=next;
        this.field.Set(next.Scalar,next.Legend);
        this.isobars.Set(next.Pressure);
        if(next.Wind is null){
            this.particles.Reset();
        }
        if(previousTiles is not null&&!previousTiles.SequenceEqual(next.Tiles)){
            this.tiles.CancelPending();
        }
    }

    /// <summary>視差効果を減らす設定: 画面の格子ごとに GFS の風を静止した矢印で描く。</summary>
    private void DrawStaticWind(SKCanvas canvas,MapView view,SKSize size,WindField wind){
        this.staticArrows.Clear();
        var spacing=48*view.PixelRatio;
        for(var y=spacing/2;y<size.Height;y+=spacing){
            for(var x=spacing/2;x<size.Width;x+=spacing){
                var geo=view.ToGeo(new SKPoint(x,y),size);
                var (u,v)=wind.Sample(geo.Latitude,geo.Longitude);
                if(float.IsNaN(u)||float.IsNaN(v)){
                    continue;
                }
                var speed=Math.Sqrt(u*u+v*v);
                var from=(Math.Atan2(-u,-v)*180/Math.PI+360)%360;
                this.staticArrows.Add(new WindArrow(geo,from,speed));
            }
        }
        var set=new WindArrowSet(this.staticArrows,ArrowKind.Forecast,wind.U.ValidTime,wind.U.Source);
        this.arrows.DrawArrows(canvas,view,size,set,japanOnly:false);
    }

    private void DrawPins(SKCanvas canvas,MapView view,SKSize size){
        var scale=view.PixelRatio;
        this.font??=FontProvider.Create(13);
        this.font.Size=13*scale;
        foreach(var pin in this.Pins){
            var p=view.ToScreen(pin.Point.Latitude,pin.Point.Longitude,size);
            if(p.X<-80||p.Y<-40||p.X>size.Width+80||p.Y>size.Height+40){
                continue;
            }
            var width=this.font.MeasureText(pin.Label,this.pinText)+14*scale;
            var height=22*scale;
            var rect=SKRect.Create(p.X-width/2,p.Y-height-9*scale,width,height);
            canvas.DrawRoundRect(rect,height/2,height/2,this.pinFill);
            canvas.DrawText(pin.Label,p.X,rect.Bottom-6.5f*scale,SKTextAlign.Center,this.font,this.pinText);
            canvas.DrawCircle(p,4*scale,this.pinDot);
        }
        if(this.Selected is {} selected){
            var p=view.ToScreen(selected.Latitude,selected.Longitude,size);
            this.selection.StrokeWidth=2.5f*scale;
            canvas.DrawCircle(p,10*scale,this.selection);
            canvas.DrawCircle(p,3*scale,this.pinText);
        }
    }

    public void Dispose(){
        this.baseMap.Dispose();
        this.field.Dispose();
        this.overlay.Dispose();
        this.tiles.Dispose();
        this.isobars.Dispose();
        this.particles.Dispose();
        this.arrows.Dispose();
        this.layerPaint.Dispose();
        this.pinFill.Dispose();
        this.pinText.Dispose();
        this.pinDot.Dispose();
        this.selection.Dispose();
        this.font?.Dispose();
    }
}
