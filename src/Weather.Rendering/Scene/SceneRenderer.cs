using System.Diagnostics;
using SkiaSharp;
using Weather.Scene;

namespace Weather.Rendering.Scene;

/// <summary>
/// シーン描画(Rendering.md)。目標の SceneState に滑らかに追従しながら、奥から手前へレイヤーを描く。
/// MAUI 非依存。描画ホスト(App / SceneLab)はフレームごとに Render を呼ぶ。
/// </summary>
public sealed class SceneRenderer:IDisposable{
    private readonly DisplayState display=new();
    private readonly SkyLayer sky=new();
    private readonly StarsLayer stars=new();
    private readonly CelestialLayer celestial=new();
    private readonly CloudLayer farClouds=new(false);
    private readonly LandscapeLayer landscape=new();
    private readonly CloudLayer nearClouds=new(true);
    private readonly PrecipitationLayer precipitation=new();
    private readonly FogLayer fog=new();
    private readonly HazeLayer haze=new();
    private readonly LightningLayer lightning=new();
    private readonly Stopwatch stopwatch=new();
    private readonly SceneFrame frame=new();
    private SceneState? target;
    private double lastTime=double.NaN;

    public SceneRenderer(SceneRenderOptions? options=null){
        this.Options=options??new SceneRenderOptions();
    }

    public SceneRenderOptions Options{get;}
    public FrameStats Stats{get;}=new();
    public DisplayState Display=>this.display;
    public int ParticleCount=>this.precipitation.ActiveCount;
    public int LightningCount=>this.lightning.FlashCount;

    public void SetTarget(SceneState state){
        ArgumentNullException.ThrowIfNull(state);
        this.target=state;
        if(!this.display.Initialized){
            this.display.Snap(state);
        }
    }

    /// <summary>目標状態へ即座に合わせる(画面の初回表示や地点の切り替え)。</summary>
    public void SnapToTarget(){
        if(this.target is not null){
            this.display.Snap(this.target);
        }
    }

    public void Render(SKCanvas canvas,SKSizeI size,double timeSeconds){
        ArgumentNullException.ThrowIfNull(canvas);
        if(size.Width<=0||size.Height<=0){
            return;
        }
        this.stopwatch.Restart();
        var dt=0d;
        if(!double.IsNaN(this.lastTime)){
            dt=Math.Clamp(timeSeconds-this.lastTime,0,0.1);
        }
        this.lastTime=timeSeconds;
        if(this.target is not null){
            this.display.Approach(this.target,dt);
        }
        var frame=this.frame;
        frame.Canvas=canvas;
        frame.Size=size;
        frame.State=this.display;
        frame.Time=timeSeconds;
        frame.DeltaSeconds=dt;
        frame.Quality=QualitySettings.For(this.Options.Tier);
        frame.Options=this.Options;
        frame.Flash=this.lightning.Update(frame);

        this.sky.Draw(frame);
        this.stars.Draw(frame);
        this.celestial.Draw(frame);
        this.farClouds.Draw(frame);
        this.landscape.Draw(frame);
        this.nearClouds.Draw(frame);
        this.precipitation.Draw(frame);
        this.fog.Draw(frame);
        this.haze.Draw(frame);
        this.lightning.Draw(frame);

        this.stopwatch.Stop();
        if(this.Options.AutoTier&&dt>0){
            if(this.Stats.Record(this.stopwatch.Elapsed.TotalMilliseconds,dt,this.Options.Tier) is {} tier){
                this.Options.Tier=tier;
            }
        }
    }

    public void Dispose(){
        this.sky.Dispose();
        this.stars.Dispose();
        this.celestial.Dispose();
        this.farClouds.Dispose();
        this.landscape.Dispose();
        this.nearClouds.Dispose();
        this.precipitation.Dispose();
        this.fog.Dispose();
        this.haze.Dispose();
        this.lightning.Dispose();
    }
}
